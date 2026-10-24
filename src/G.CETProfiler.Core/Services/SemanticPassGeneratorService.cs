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
            .OrderBy(x => x.RuleId.EndsWith("-worker", StringComparison.OrdinalIgnoreCase)
                ? 2 : x.RuleId.StartsWith("native-location-",
                StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenByDescending(x => x.RuntimeMsPerSecond)
            .ThenBy(x => x.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var applied = 0;

        foreach (var candidate in candidates)
        {
            if (ModOptimizationBlacklist.IsBlacklistedOwner(candidate.Owner))
                continue;
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

                if (context.Changes.Count == 0)
                    throw new InvalidOperationException(
                        "Semantic injector reported success without changing any live source file.");

                // Never ship unexpanded named-regex substitution tokens as Lua.
                if (context.Changes.Any(change =>
                    Encoding.UTF8.GetString(change.Bytes).Contains("${opening}", StringComparison.Ordinal) ||
                    Encoding.UTF8.GetString(change.Bytes).Contains("${close}", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException(
                        "Semantic injector emitted unexpanded callback placeholders; refusing the generated pass.");
                }

                // Coordinator-based semantic stages use source ownership and
                // proof markers instead of an arbitrary count of author files.
                // This tolerates author updates moving a proven bridge between
                // init and submodules, while forbidding unrelated output.
                // Legacy rules without coordinators retain their file budget.
                ValidateSemanticOutput(candidate, context);

                var localLimitFailure = context.Changes
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

                foreach (var change in context.Changes)
                {
                    var relative = change.RelativeFile.Replace('\\', '/');
                    var livePath = Path.Combine(
                        Path.GetFullPath(modsRoot),
                        relative.Replace('/', Path.DirectorySeparatorChar));


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
                    files = context.Changes
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
            modsRoot,staged,appliedTransformsByFile,
            transformManifest,skipped);
        return applied;
    }


    // Coordinator output is owned by the current mod and the exact semantic
    // rule. A file-count assertion does not prove any of those properties.
    // Source injectors are responsible for validating the live callback graph
    // and postconditions before reporting Success.
    private static void ValidateSemanticOutput(
        SemanticCandidate candidate, SemanticPatchContext context)
    {
        var coordinatorChanges = context.Changes
            .Where(change => context.IsOwnedCoordinator(change.RelativeFile))
            .ToList();
        if (coordinatorChanges.Count > 0 || context.HasOwnedCoordinator())
        {
            var authorChanges = context.Changes
                .Where(change => !context.IsOwnedCoordinator(change.RelativeFile))
                .ToList();
            if (authorChanges.Count == 0)
                throw new InvalidOperationException(
                    "Coordinator stage has no source bridge in the current pass.");

            var marker = "-- G-CET semantic:" + candidate.RuleId;
            var workerMarker = "-- G-CET semantic coordinator:" + candidate.RuleId + "\n";
            foreach (var change in context.Changes)
            {
                var path = change.RelativeFile.Replace('\\', '/');
                if (!context.IsOwnerPath(path))
                    throw new InvalidOperationException(
                        "Coordinator stage changed a file outside its source owner: " + path);
                var source = Encoding.UTF8.GetString(change.Bytes)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n');
                if (context.IsOwnedCoordinator(path))
                {
                    if (!source.StartsWith(workerMarker, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "Coordinator output lost its exact semantic rule ownership.");
                }
                else if (!source.Split('\n').Any(line =>
                {
                    var comment = line.Trim();
                    if (comment.Equals(marker, StringComparison.OrdinalIgnoreCase))
                        return true;
                    // Reviewed injectors may version the marker independently
                    // of the stable catalog RuleId (for example, "-v2").
                    if (!comment.StartsWith(marker + "-v", StringComparison.OrdinalIgnoreCase))
                        return false;
                    var suffix = comment[(marker.Length + 2)..];
                    return suffix.Length > 0 && suffix.All(char.IsDigit);
                }))
                {
                    throw new InvalidOperationException(
                        "Author source output lost its semantic rule ownership: " + path);
                }
            }
            return;
        }

        // Original exact/partial budget remains for source-only legacy passes.
        var changedSources = context.Changes.Count(change =>
            !context.IsOwnedCoordinator(change.RelativeFile));
        var valid = candidate.StageComposed
            ? changedSources > 0 && changedSources <= candidate.ExpectedMarkerFileCount
            : changedSources == candidate.ExpectedMarkerFileCount;
        if (candidate.ExpectedMarkerFileCount > 0 && !valid)
            throw new InvalidOperationException(
                $"Semantic injector changed {context.Changes.Count} file(s), but rule " +
                $"expects {(candidate.StageComposed ? "between 1 and " : "exactly ")}" +
                $"{candidate.ExpectedMarkerFileCount} changed file(s).");
    }

    private static int ApplyPreparedNativeLocationStages(
        string modsRoot,
        Dictionary<string,byte[]> staged,
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
            if(ModOptimizationBlacklist.IsBlacklistedOwner(item.Owner))continue;
            var relative=item.Owner+"/"+item.File;
            if(!staged.TryGetValue(relative,out var prior))continue;
            var context=new SemanticPatchContext(modsRoot,item.Owner,item.Rule,staged);
            var priorText=Encoding.UTF8.GetString(prior);
            if(!priorText.Contains(item.Prerequisite,StringComparison.Ordinal) ||
               priorText.Contains(item.Completion,StringComparison.Ordinal) ||
               NativeLocationStructuralSource.CompleteStageWithCoordinator(
                   context,item.Rule,priorText))
                continue;
            try
            {
                var rule=new SemanticCandidate(item.Rule,item.Owner,
                    "SEMANTIC_NATIVE_LOCATION_SOURCE_PROVEN",
                    "NATIVE_SOURCE_COMPOSED","source-injection",
                    0.0,1,false,SourceIndependent:true);
                var result=SemanticInjectors.Apply(rule,context);
                if (!result.Applied) continue;
                var changes = context.Changes.ToList();
                var source = changes.SingleOrDefault(change =>
                    change.RelativeFile.Equals(relative, StringComparison.OrdinalIgnoreCase));
                if (source is null)
                    throw new InvalidOperationException(
                        "Composed native location failed to rewrite its reviewed author source.");
                // The source-proven native hook and its external coordinator are
                // one atomic owner pass. Do not discard the worker by retaining
                // the historical single-modified-file assumption.
                ValidateSemanticOutput(rule, context);
                var code = Encoding.UTF8.GetString(source.Bytes);
                var hookComplete = code.Contains(item.Completion, StringComparison.Ordinal) ||
                    // The external native worker may already exist unchanged
                    // under the SAME reviewed owner in a staged/partial stack.
                    // Completion must inspect source + owned worker, not only
                    // this pass's changed bytes.
                    NativeLocationStructuralSource.CompleteStageWithCoordinator(
                        context, item.Rule, code);
                if (!hookComplete ||
                    code.Contains("$"+"{opening}", StringComparison.Ordinal) ||
                    code.Contains("$"+"{close}", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Composed native location source postcondition did not hold.");
                foreach (var change in changes)
                {
                    var path = change.RelativeFile.Replace('\\', '/');
                    if (!context.IsOwnerPath(path) ||
                        (!path.Equals(relative, StringComparison.OrdinalIgnoreCase) &&
                         !context.IsOwnedCoordinator(path)))
                        throw new InvalidOperationException(
                            "Composed native stage escaped its reviewed owner source/worker.");
                    var limit = LuaLocalBudgetAnalyzer.Analyze(change.Bytes);
                    if (limit.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit)
                        throw new InvalidOperationException(
                            "Composed native stage exceeds the source's Lua local limit.");
                }
                foreach (var change in changes)
                {
                    var path = change.RelativeFile.Replace('\\', '/');
                    staged[path] = change.Bytes;
                    appliedTransformsByFile[path] =
                        appliedTransformsByFile.TryGetValue(path, out var count) ? count + 1 : 1;
                }
                transformManifest.Add(new {
                    type="SEMANTIC_RULE",
                    ruleId=item.Rule,owner=item.Owner,
                    handler="SEMANTIC_NATIVE_LOCATION_SOURCE_PROVEN",
                    policyClass="NATIVE_SOURCE_COMPOSED",
                    runtimeMsPerSecond=(double?)null,
                    admissionEvidence="SOURCE_PROVEN_SAME_PASS_COMPOSITION",
                    patchStyle="source-injection",sourceProof=true,
                    files=changes.Select(x=>x.RelativeFile.Replace('\\','/')).ToArray(),result.Note
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
                    JsonBool(row, "alreadySatisfied"))
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
    private string? _ownerFolder;

    internal string Owner { get; }
    internal string RuleId { get; }

    internal IReadOnlyCollection<SemanticFileChange> Changes =>
        _changes.Values;

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

    // New, author-scoped modules are allowed ONLY for a reviewed semantic
    // injector. They do not create a new mod, change arbitrary user files,
    // or bypass the per-file Lua budget and final overlay manifest.
    // Read only a source-owner worker, considering staged output before live
    // files; coordinator writes remain separately guarded by WriteCoordinator.
    internal string? TryReadOwnedCoordinator(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            !fileName.EndsWith(".lua",StringComparison.OrdinalIgnoreCase))
            return null;
        var folder=ResolveOwnerFolder();
        if(folder is null)return null;
        var relative=Path.GetFileName(folder)+"/GCET_Semantic/"+fileName;
        if(_changes.TryGetValue(relative,out var pending))
            return SemanticTextFile.From(relative,pending.Bytes).Text;
        if(_staged.TryGetValue(relative,out var staged))
            return SemanticTextFile.From(relative,staged).Text;
        var absolute=Path.Combine(folder,"GCET_Semantic",fileName);
        if(!File.Exists(absolute))return null;
        return SemanticTextFile.From(relative,File.ReadAllBytes(absolute)).Text;
    }

    // A worker already installed (and unchanged) is still part of this
    // source-owned stage. Its absence from Changes must not send two valid
    // author bridges back through an obsolete one-file count.
    internal bool HasOwnedCoordinator()
    {
        var folder = ResolveOwnerFolder();
        if (folder is null) return false;
        var ownerPrefix = Path.GetFileName(folder) + "/GCET_Semantic/";
        var marker = "-- G-CET semantic coordinator:" + RuleId + "\n";
        foreach (var pair in _staged)
        {
            if (pair.Key.Replace('\\', '/').StartsWith(
                    ownerPrefix, StringComparison.OrdinalIgnoreCase) &&
                SemanticTextFile.From(pair.Key, pair.Value).Text
                    .StartsWith(marker, StringComparison.Ordinal))
                return true;
        }
        var directory = Path.Combine(folder, "GCET_Semantic");
        if (!Directory.Exists(directory)) return false;
        foreach (var file in Directory.EnumerateFiles(directory, "*.lua",
                     SearchOption.TopDirectoryOnly))
        {
            if (File.ReadAllText(file)
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .StartsWith(marker, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    // A structural path check is independent of how many source files
    // an author version happens to use. Reject parent traversal and aliases.
    internal bool IsOwnerPath(string relativeFile)
    {
        var folder = ResolveOwnerFolder();
        if (folder is null) return false;
        var relative = relativeFile.Replace('\\', '/');
        var root = Path.GetFileName(folder) + "/";
        if (!relative.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;
        return relative.Split('/').All(part =>
            !string.IsNullOrEmpty(part) && part != "." && part != "..");
    }

    internal bool IsOwnedCoordinator(string relativeFile)
    {
        var folder=ResolveOwnerFolder();
        if (folder is null) return false;
        var root=Path.GetFileName(folder);
        return relativeFile.StartsWith(root + "/GCET_Semantic/",
            StringComparison.OrdinalIgnoreCase);
    }

    internal void WriteCoordinator(string fileName, string contents)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName != Path.GetFileName(fileName) ||
            !fileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException(
                "Semantic coordinator filename must be one safe .lua basename.");

        var folder=ResolveOwnerFolder() ??
            throw new InvalidOperationException("Coordinator owner is absent.");
        var relative=Path.GetFileName(folder)+"/GCET_Semantic/"+fileName;
        var marker="-- G-CET semantic coordinator:"+RuleId;
        var normalized=contents.Replace("\r\n","\n",StringComparison.Ordinal)
            .Replace('\r','\n');
        if (!normalized.StartsWith(marker+"\n",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Semantic coordinator is missing its exact reviewed owner/rule marker.");

        // A single semantic implementation may prepare a coordinator more
        // than once while composing independent stages. An identical write
        // is idempotent; a different one must never silently overwrite the
        // file already reserved by this context.
        if (_changes.TryGetValue(relative,out var pending))
        {
            var current=SemanticTextFile.From(relative,pending.Bytes).Text;
            if (!current.Equals(normalized,StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Conflicting coordinator definitions in one semantic pass.");
            return;
        }

        // A prior generated worker is replaceable only when its FIRST line
        // proves the same owner/rule. Different users' files and foreign
        // stages remain protected. Updated coordinator code never needs
        // to be squeezed back into an author's init.lua.
        static bool SameCoordinatorOwner(string current, string ownerMarker) =>
            current.StartsWith(ownerMarker+"\n",StringComparison.Ordinal);

        if (_staged.TryGetValue(relative,out var stagedBytes))
        {
            var current=SemanticTextFile.From(relative,stagedBytes).Text
                .Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            if (current.Equals(normalized,StringComparison.Ordinal)) return;
            if (!SameCoordinatorOwner(current,marker))
                throw new InvalidOperationException(
                    "A different semantic stage already owns the coordinator path.");
            _changes[relative]=new SemanticFileChange(
                relative,new UTF8Encoding(false).GetBytes(normalized));
            return;
        }

        var path=Path.Combine(folder,"GCET_Semantic",fileName);
        if (File.Exists(path))
        {
            var current=File.ReadAllText(path)
                .Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            if (current.Equals(normalized,StringComparison.Ordinal)) return;
            if (!SameCoordinatorOwner(current,marker))
                throw new InvalidOperationException(
                    "Existing coordinator has a different owner; manual semantic adaptation required.");
            _changes[relative]=new SemanticFileChange(
                relative,new UTF8Encoding(false).GetBytes(normalized));
            return;
        }

        _changes[relative]=new SemanticFileChange(
            relative,new UTF8Encoding(false).GetBytes(normalized));
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
        // Stage-specific labels (e.g. -five-job-v1) do not establish the
        // exact catalog rule ownership required by ValidateSemanticOutput.
        // Emit the canonical marker if its own line is absent.
        if (!HasMarkerLine(text, marker))
        {
            text = $"-- {marker}\n" + text;
        }

        _changes[file.RelativeFile] = new SemanticFileChange(
            file.RelativeFile,
            file.Encode(text));
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
