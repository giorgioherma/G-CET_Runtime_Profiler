using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static class SemanticPassGeneratorService
{
    internal const double MaterialThresholdMsPerSecond = 3.0;

    internal static int Apply(
        JsonElement resolverRoot,
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        var candidates = ReadCandidates(resolverRoot)
            .GroupBy(x => x.Owner + "\n" + x.RuleId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.RuntimeMsPerSecond).First())
            // All source-aware behavior stages must complete BEFORE
            // native-location subscriptions. Never prioritize a native owner
            // before its scheduler and wake/fallback semantics by CPU alone.
            .OrderBy(x => x.RuleId.StartsWith("native-location-",
                StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(x => x.RuntimeMsPerSecond)
            .ThenBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var applied = 0;

        foreach (var candidate in candidates)
        {
            var context = new SemanticPatchContext(
                modsRoot,
                candidate.Owner,
                candidate.RuleId,
                staged);

            try
            {
                var result = SemanticInjectors.Apply(candidate, context);
                if (!result.Applied)
                {
                    skipped.Add(new
                    {
                        type = "SEMANTIC_RULE",
                        candidate.RuleId,
                        candidate.Owner,
                        candidate.Handler,
                        candidate.PolicyClass,
                        candidate.RuntimeMsPerSecond,
                        reason = result.Reason
                    });
                    continue;
                }

                if (context.Changes.Count == 0 && context.OwnedModules.Count == 0)
                    throw new InvalidOperationException(
                        "Semantic injector reported success without changing current source or creating an owned module.");

                // Never ship unexpanded named-regex substitution tokens as Lua.
                if (context.Changes.Any(change =>
                    Encoding.UTF8.GetString(change.Bytes).Contains("${opening}", StringComparison.Ordinal) ||
                    Encoding.UTF8.GetString(change.Bytes).Contains("${close}", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Semantic injector emitted unexpanded callback placeholders; refusing the generated pass.");
                }

                // Source-composed semantic rules can have independently completed
                // stages in the input. The unchanged stages are validated by
                // their authoritative injectors, so count changed files rather
                // than demanding that already-complete files be emitted again.
                // Single-stage rules retain their exact expected-file count.
                var validChangedFileCount = candidate.StageComposed
                    ? context.Changes.Count > 0 &&
                      context.Changes.Count <= candidate.ExpectedMarkerFileCount
                    : context.Changes.Count == candidate.ExpectedMarkerFileCount;
                if (candidate.ExpectedMarkerFileCount > 0 && !validChangedFileCount)
                {
                    throw new InvalidOperationException(
                        $"Semantic injector changed {context.Changes.Count} file(s), but rule " +
                        $"expects {(candidate.StageComposed ? "between 1 and " : "exactly ")}" +
                        $"{candidate.ExpectedMarkerFileCount} changed file(s).");
                }

                var localLimitFailure = context.Changes.Concat(context.OwnedModules)
                    .Select(change => new
                    {
                        Change = change,
                        Budget = LuaLocalBudgetAnalyzer.Analyze(change.Bytes)
                    })
                    .FirstOrDefault(x =>
                        x.Budget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit);

                if (localLimitFailure is not null)
                {
                    skipped.Add(new
                    {
                        type = "LUA_LOCAL_LIMIT",
                        candidate.RuleId,
                        candidate.Owner,
                        candidate.Handler,
                        file = localLimitFailure.Change.RelativeFile.Replace('\\', '/'),
                        limit = LuaLocalBudgetAnalyzer.Limit,
                        generatedMaxActiveLocals = localLimitFailure.Budget.MaxActiveLocals,
                        generatedPeakLine = localLimitFailure.Budget.Line,
                        generatedPeakScope = localLimitFailure.Budget.Scope,
                        reason =
                            $"G-CET refused semantic optimization because the transformed Lua source would require {localLimitFailure.Budget.MaxActiveLocals} simultaneously active local variables in {localLimitFailure.Budget.Scope} at line {localLimitFailure.Budget.Line}; Lua's hard per-function limit is {LuaLocalBudgetAnalyzer.Limit}. Author code was not deleted or rewritten to make room."
                    });
                    continue;
                }

                foreach (var change in context.Changes.Concat(context.OwnedModules))
                {
                    var relative = change.RelativeFile.Replace('\\', '/');
                    var livePath = Path.Combine(
                        Path.GetFullPath(modsRoot),
                        relative.Replace('/', Path.DirectorySeparatorChar));

                    if (!liveSourceHashes.ContainsKey(relative))
                    {
                        liveSourceHashes[relative] = File.Exists(livePath)
                            ? Sha256(File.ReadAllBytes(livePath))
                            : null;
                    }

                    staged[relative] = change.Bytes;
                    appliedTransformsByFile[relative] =
                        appliedTransformsByFile.TryGetValue(relative, out var count)
                            ? count + 1
                            : 1;
                }

                transformManifest.Add(new
                {
                    type = "SEMANTIC_RULE",
                    candidate.RuleId,
                    candidate.Owner,
                    candidate.Handler,
                    candidate.PolicyClass,
                    // No measured callback exists for source-only candidates.
                    // Zero is NOT a measurement; preserve null provenance.
                    runtimeMsPerSecond = candidate.SourceIndependent
                        ? (double?)null : Math.Round(candidate.RuntimeMsPerSecond, 6),
                    admissionEvidence = candidate.SourceIndependent
                        ? "SOURCE_PROVEN_NO_CAPTURE"
                        : "CAPTURE_MATCHED_SOURCE_PROVEN",
                    patchStyle = candidate.PatchStyle,
                    sourceProof = true,
                    files = context.Changes.Concat(context.OwnedModules)
                        .Select(x => x.RelativeFile.Replace('\\', '/'))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    result.Note
                });
                applied++;
            }
            catch (Exception ex)
            {
                skipped.Add(new
                {
                    type = "SEMANTIC_RULE",
                    candidate.RuleId,
                    candidate.Owner,
                    candidate.Handler,
                    candidate.PolicyClass,
                    candidate.RuntimeMsPerSecond,
                    reason = "Semantic injection failed closed: " + ex.Message
                });
            }
        }


        // Pristine author input can lack a native-location prerequisite that
        // the earlier semantic rule creates in THIS SAME generation.
        // Compose only production-admitted owners, only from already-staged
        // current source, and only after the exact source injector accepts it.
        applied += ApplyPreparedNativeLocationStages(
            modsRoot,staged,liveSourceHashes,appliedTransformsByFile,
            transformManifest,skipped);

        // Only after all inline native scheduler adapters have run can the
        // source-proven PASS4 implementation move into its owned Lua chunk.
        // This is a packaging transform, never a new scheduling policy.
        applied += PokerSemanticCoordinatorSource.TryStage(
            modsRoot,staged,liveSourceHashes,appliedTransformsByFile,
            transformManifest,skipped);
        return applied;
    }


    private static int ApplyPreparedNativeLocationStages(
        string modsRoot,
        Dictionary<string,byte[]> staged,
        Dictionary<string,string?> liveSourceHashes,
        Dictionary<string,int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        var verifiedFollowups = new (string Rule,string Owner,string File,
            string Prerequisite,string Completion)[]
        {
            ("native-location-poker","StreetGamesPoker","init.lua",
                "poker.world_far","G-CET native location:poker-v2"),
            ("native-location-tunnel","tunnel_rescue","init.lua",
                "local function __gcetTunnelFarOutside()","G-CET native location:tunnel-v2"),
            ("native-location-illegal","illegal_mechanic_phase1a","physical_npc.lua",
                "GetMechanicCountForCET","G-CET native location:illegal-v3"),
            ("native-location-dedka","Dedka Auto Shop","init.lua",
                "dedka_entrance2_cluster","G-CET native location:dedka-v2"),
            ("native-location-card-collection","Card Collection","init.lua",
                "local function buildSpatialEntries()","G-CET native location:card-collection-v1"),
            ("native-location-marmurbank","marmurbank","module/Bank.lua",
                "function bank:registerSpatialSet()","G-CET native location:marmurbank-v1"),
            ("native-location-yaiba-bathtub","yaibaPenthouse","bathtub.lua",
                "G-CET semantic:yaiba-penthouse-distant-bathtub","G-CET native location:yaiba-bathtub-v1"),
            ("native-location-ziggy-offered","ziggy_last_play","init.lua",
                "local __gcetZiggyOfferedFarAcc=0.0","G-CET native location:ziggy-offered-v1")
        };
        var applied=0;
        foreach(var item in verifiedFollowups)
        {
            var relative=item.Owner+"/"+item.File;
            if(!staged.TryGetValue(relative,out var prior))continue;
            var priorText=Encoding.UTF8.GetString(prior);
            if(!priorText.Contains(item.Prerequisite,StringComparison.Ordinal) ||
               priorText.Contains(item.Completion,StringComparison.Ordinal) ||
               NativeLocationStructuralSource.CompleteStage(item.Rule,new[]{priorText}))
                continue;
            var context=new SemanticPatchContext(modsRoot,item.Owner,item.Rule,staged);
            try
            {
                var rule=new SemanticCandidate(item.Rule,item.Owner,
                    "SEMANTIC_NATIVE_LOCATION_SOURCE_PROVEN",
                    "NATIVE_SOURCE_COMPOSED","source-injection",
                    0.0,1,false,SourceIndependent:true);
                var result=SemanticInjectors.Apply(rule,context);
                if(!result.Applied || context.Changes.Count!=1)
                    continue;
                var change=context.Changes.Single();
                var code=Encoding.UTF8.GetString(change.Bytes);
                // A semantic stage may be complete through a structurally
                // proven source adapter even though its legacy comment marker
                // is absent. Treat each implementation's postcondition as
                // independently authoritative, not the earlier template ID.
                var hookComplete=code.Contains(item.Completion,StringComparison.Ordinal) ||
                    NativeLocationStructuralSource.CompleteStage(item.Rule,new[]{code});
                if(!hookComplete ||
                   code.Contains("$"+"{opening}",StringComparison.Ordinal) ||
                   code.Contains("$"+"{close}",StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Composed native location source postcondition did not hold.");
                var limit=LuaLocalBudgetAnalyzer.Analyze(change.Bytes);
                if(limit.MaxActiveLocals>LuaLocalBudgetAnalyzer.Limit)
                    throw new InvalidOperationException(
                        "Composed native location exceeds the source's Lua local limit.");
                var path=change.RelativeFile.Replace('\\','/');
                if(!path.Equals(relative,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Composed native stage escaped its reviewed owner source.");
                if(!liveSourceHashes.ContainsKey(path))
                {
                    var livePath=Path.Combine(Path.GetFullPath(modsRoot),
                        path.Replace('/',Path.DirectorySeparatorChar));
                    liveSourceHashes[path]=File.Exists(livePath)
                        ? Sha256(File.ReadAllBytes(livePath)):null;
                }
                staged[path]=change.Bytes;
                appliedTransformsByFile[path]=appliedTransformsByFile.TryGetValue(
                    path,out var count)?count+1:1;
                transformManifest.Add(new {
                    type="SEMANTIC_RULE",
                    ruleId=item.Rule,owner=item.Owner,
                    handler="SEMANTIC_NATIVE_LOCATION_SOURCE_PROVEN",
                    policyClass="NATIVE_SOURCE_COMPOSED",
                    runtimeMsPerSecond=(double?)null,
                    admissionEvidence="SOURCE_PROVEN_SAME_PASS_COMPOSITION",
                    patchStyle="source-injection",sourceProof=true,
                    files=new[]{path},result.Note
                });
                applied++;
            }
            catch(Exception error)
            {
                skipped.Add(new {
                    type="SEMANTIC_RULE",ruleId=item.Rule,
                    owner=item.Owner,
                    reason="Composed native location source refused: "+error.Message
                });
            }
        }
        return applied;
    }

    private static List<SemanticCandidate> ReadCandidates(JsonElement root)
    {
        var result = new List<SemanticCandidate>();

        if (!root.TryGetProperty("callbackFamilies", out var families) ||
            families.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var family in families.EnumerateArray())
        {
            if (!family.TryGetProperty("topConsumers", out var consumers) ||
                consumers.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var consumer in consumers.EnumerateArray())
            {
                if (!consumer.TryGetProperty("runtime", out var runtime) ||
                    runtime.ValueKind != JsonValueKind.Object)
                    continue;

                if (!consumer.TryGetProperty("semantic", out var semantic) ||
                    semantic.ValueKind != JsonValueKind.Object)
                    continue;

                var runtimeMs = JsonDouble(runtime, "exclusiveMsPerSecond");
                var runtimeCalls = JsonDouble(runtime, "callsPerSecond");
                var semanticAdmission = SemanticAdmissionPolicy.Evaluate(
                    JsonBool(semantic, "Matched"),
                    JsonBool(semantic, "SourceProofSatisfied"),
                    JsonBool(semantic, "GenerationEnabled"),
                    JsonBool(semantic, "AlreadySatisfied"),
                    JsonBool(semantic, "PartialState"),
                    runtimeCalls,
                    runtimeMs,
                    JsonDouble(semantic, "RuntimeThresholdMsPerSecond"));
                if (!semanticAdmission.Ready)
                    continue;

                var ruleId = JsonString(semantic, "RuleId");
                var owner = JsonString(consumer, "owner");
                var handler = JsonString(semantic, "Handler");
                var policyClass = JsonString(semantic, "PolicyClass");
                var patchStyle = JsonString(semantic, "PatchStyle");
                var expectedMarkerFileCount = (int)JsonLong(
                    semantic,
                    "ExpectedMarkerFileCount");

                if (string.IsNullOrWhiteSpace(ruleId) ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(handler))
                    continue;

                result.Add(new SemanticCandidate(
                    ruleId,
                    owner,
                    handler,
                    policyClass,
                    patchStyle,
                    runtimeMs,
                    expectedMarkerFileCount,
                    JsonBool(semantic, "StageComposed")));
            }
        }

        // Opted-in source-confirmed rules may remain eligible without any
        // measured callback row. Never invent call rate, Lua CPU or native
        // allocation; emit a normal reviewed injector candidate with zero
        // measured cost and preserve its independent source safety gate.
        if (root.TryGetProperty("semanticSourceOnlyCandidates", out var independent) &&
            independent.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in independent.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object ||
                    !JsonBool(row, "sourceIndependent") ||
                    !JsonBool(row, "sourceProofSatisfied") ||
                    !JsonBool(row, "generationEnabled") ||
                    JsonBool(row, "alreadySatisfied") ||
                    JsonString(row, "admissionLane") != "SEMANTIC_AUTHOR_SOURCE_ONLY")
                    continue;

                var ruleId = JsonString(row, "ruleId");
                var owner = JsonString(row, "owner");
                var handler = JsonString(row, "handler");
                var expectedFiles = (int)JsonLong(row, "expectedMarkerFileCount");
                if (string.IsNullOrWhiteSpace(ruleId) ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(handler) ||
                    expectedFiles < 1)
                    continue;

                result.Add(new SemanticCandidate(
                    ruleId, owner, handler,
                    JsonString(row, "policyClass"),
                    JsonString(row, "patchStyle"),
                    0.0, expectedFiles,
                    JsonBool(row, "stageComposed"),
                    SourceIndependent: true));
            }
        }

        return result;
    }

    private static string JsonString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static bool JsonBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind == JsonValueKind.True;
    }

    private static double JsonDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
            return number;
        return 0;
    }

    private static long JsonLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number))
            return 0;
        return number;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record SemanticCandidate(
    string RuleId,
    string Owner,
    string Handler,
    string PolicyClass,
    string PatchStyle,
    double RuntimeMsPerSecond,
    int ExpectedMarkerFileCount,
    bool StageComposed,
    bool SourceIndependent = false);

internal sealed record SemanticInjectionResult(
    bool Applied,
    string Reason,
    string Note)
{
    internal static SemanticInjectionResult Success(string note) =>
        new(true, "", note);

    internal static SemanticInjectionResult Skip(string reason) =>
        new(false, reason, "");
}

internal sealed class SemanticPatchContext
{
    private static readonly Regex NormalizeNonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _modsRoot;
    private readonly Dictionary<string, byte[]> _staged;
    private readonly Dictionary<string, SemanticFileChange> _changes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemanticFileChange> _ownedModules =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _ownerFolder;

    internal string Owner { get; }
    internal string RuleId { get; }

    internal IReadOnlyCollection<SemanticFileChange> Changes =>
        _changes.Values;

    internal IReadOnlyCollection<SemanticFileChange> OwnedModules =>
        _ownedModules.Values;

    internal SemanticPatchContext(
        string modsRoot,
        string owner,
        string ruleId,
        Dictionary<string, byte[]> staged)
    {
        _modsRoot = Path.GetFullPath(modsRoot);
        Owner = owner;
        RuleId = ruleId;
        _staged = staged;
    }

    internal SemanticTextFile FindFile(
        string preferredRelative,
        params string[] requiredTokens)
    {
        var ownerFolder = ResolveOwnerFolder()
            ?? throw new InvalidOperationException(
                $"Live mod folder could not be resolved for {Owner}.");

        if (!string.IsNullOrWhiteSpace(preferredRelative))
        {
            var preferred = Path.Combine(
                ownerFolder,
                preferredRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(preferred))
            {
                var file = Read(preferred);
                if (requiredTokens.All(token => Contains(file.Text, token)))
                    return file;
            }
        }

        var matches = new List<SemanticTextFile>();
        foreach (var path in Directory.EnumerateFiles(
                     ownerFolder,
                     "*.lua",
                     SearchOption.AllDirectories))
        {
            var file = Read(path);
            if (requiredTokens.All(token => Contains(file.Text, token)))
                matches.Add(file);
            if (matches.Count > 1)
                break;
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No current Lua source uniquely proves [{string.Join(", ", requiredTokens)}]."),
            _ => throw new InvalidOperationException(
                $"Current Lua source is ambiguous for [{string.Join(", ", requiredTokens)}].")
        };
    }

    internal void Write(SemanticTextFile file, string text)
    {
        if (text == file.Text)
            return;

        // Semantic rules may only rewrite a file that already belongs to the
        // user's live mod (or was already staged from that same live file by a
        // generic transform). The semantic catalog is knowledge, never a source
        // template capable of creating an absent mod/file.
        var livePath = Path.Combine(
            _modsRoot,
            file.RelativeFile.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(livePath) && !_staged.ContainsKey(file.RelativeFile))
        {
            throw new InvalidOperationException(
                $"Semantic injection refused to create non-live file: {file.RelativeFile}");
        }

        var marker = $"G-CET semantic:{RuleId}";
        if (!text.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            text = $"-- {marker}\n" + text;
        }

        _changes[file.RelativeFile] = new SemanticFileChange(
            file.RelativeFile,
            file.Encode(text));
    }

    // Only a reviewed per-mod semantic injector may create a new Lua module.
    // Generic callbacks never use this path. The name is relative to the
    // installed owner folder, and must be beneath the reserved G-CET subtree.
    // Never overwrite an author's own file or emit files for absent mods.
    internal void WriteOwnedModule(string relativeModule, string lua)
    {
        var ownerFolder = ResolveOwnerFolder()
            ?? throw new InvalidOperationException("Semantic owner is absent.");
        var normalized = relativeModule.Replace('\\', '/');
        if (!normalized.StartsWith("G-CET/", StringComparison.Ordinal) ||
            !normalized.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.Split('/').Any(part => part.Length == 0 || part == ".") ||
            Path.IsPathRooted(relativeModule))
            throw new InvalidOperationException("Invalid semantic-owned module path.");
        const string signature = "-- G-CET semantic coordinator:";
        var expected = signature + RuleId;
        if (!lua.StartsWith(expected + "\n", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Semantic-owned module requires its exact rule ownership header.");
        var dest = Path.GetFullPath(Path.Combine(ownerFolder,
            normalized.Replace('/', Path.DirectorySeparatorChar)));
        var folderPrefix = Path.GetFullPath(ownerFolder)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!dest.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Semantic module escaped its owner.");

        var key = Path.GetRelativePath(_modsRoot, dest).Replace('\\', '/');
        if (_changes.ContainsKey(key))
            throw new InvalidOperationException("Owned module conflicts with an author edit.");
        string? prior = null;
        if (_staged.TryGetValue(key, out var staged))
            prior = Encoding.UTF8.GetString(staged);
        else if (File.Exists(dest))
            prior = File.ReadAllText(dest);
        if (prior is not null &&
            !prior.StartsWith(signature, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusing to replace a non-G-CET Lua module.");
        if (prior is not null &&
            !prior.StartsWith(expected, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Owned module belongs to a different semantic rule.");
        if (prior == lua) return;
        _ownedModules[key] = new SemanticFileChange(key,
            new UTF8Encoding(false).GetBytes(lua));
    }

    internal bool OwnerAlreadyMarked()
    {
        var folder = ResolveOwnerFolder();
        if (folder is null)
            return false;

        var marker = $"G-CET semantic:{RuleId}";
        foreach (var path in Directory.EnumerateFiles(folder, "*.lua", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/');
            string text;
            if (_changes.TryGetValue(relative, out var changed))
                text = Encoding.UTF8.GetString(changed.Bytes);
            else if (_staged.TryGetValue(relative, out var stagedBytes))
                text = Encoding.UTF8.GetString(stagedBytes);
            else
                text = File.ReadAllText(path);

            if (HasMarkerLine(text, marker))
                return true;
        }

        return false;
    }

    private static bool HasMarkerLine(
        string text,
        string marker)
    {
        var expectedComment = "-- " + marker.Trim();
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal)
                                 .Replace('\r', '\n')
                                 .Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Equals(marker.Trim(), StringComparison.OrdinalIgnoreCase) ||
                trimmed.Equals(expectedComment, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private SemanticTextFile Read(string path)
    {
        var relative = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/');

        byte[] bytes;
        if (_changes.TryGetValue(relative, out var changed))
            bytes = changed.Bytes;
        else if (_staged.TryGetValue(relative, out var staged))
            bytes = staged;
        else
            bytes = File.ReadAllBytes(path);

        return SemanticTextFile.From(relative, bytes);
    }

    private string? ResolveOwnerFolder()
    {
        if (_ownerFolder is not null)
            return _ownerFolder;

        var exact = Directory.EnumerateDirectories(_modsRoot)
            .FirstOrDefault(path =>
                Path.GetFileName(path).Equals(
                    Owner,
                    StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return _ownerFolder = exact;

        var normalized = Normalize(Owner);
        var matches = Directory.EnumerateDirectories(_modsRoot)
            .Where(path => Normalize(Path.GetFileName(path)) == normalized)
            .Take(2)
            .ToList();

        return _ownerFolder = matches.Count == 1 ? matches[0] : null;
    }

    private static bool Contains(string text, string token)
    {
        if (text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            return true;

        // File discovery for semantic injectors should survive harmless source
        // formatting changes. Ignore whitespace and quote style, but keep every
        // identifier/operator/punctuation token intact.
        var normalizedText = NormalizeProof(text);
        var normalizedToken = NormalizeProof(token);
        return normalizedToken.Length > 0 &&
            normalizedText.Contains(
                normalizedToken,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProof(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '\'' || ch == '"')
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string Normalize(string value) =>
        NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");
}

internal sealed record SemanticFileChange(
    string RelativeFile,
    byte[] Bytes);

internal sealed class SemanticTextFile
{
    private readonly bool _bom;
    private readonly string _newline;

    internal string RelativeFile { get; }
    internal string Text { get; }

    private SemanticTextFile(
        string relativeFile,
        string text,
        bool bom,
        string newline)
    {
        RelativeFile = relativeFile;
        Text = text;
        _bom = bom;
        _newline = newline;
    }

    internal static SemanticTextFile From(
        string relativeFile,
        byte[] bytes)
    {
        var bom =
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;

        var raw = bom
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);

        var newline = raw.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var text = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return new SemanticTextFile(
            relativeFile,
            text,
            bom,
            newline);
    }

    internal byte[] Encode(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var output = _newline == "\r\n"
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;

        var payload = Encoding.UTF8.GetBytes(output);
        if (!_bom)
            return payload;

        var withBom = new byte[payload.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Buffer.BlockCopy(payload, 0, withBom, 3, payload.Length);
        return withBom;
    }
}
