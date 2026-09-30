using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record PassBuildResult(
    string ZipPath,
    string ManifestPath,
    int FileCount,
    int TransformCount,
    int SkippedCount);

/// <summary>
/// Generates a reversible overlay ZIP from resolver decisions only.
/// Generator supports only finite resolver-authorized recipes:
/// ACTION_ROUTING_*, FRAME_DISPATCH_CONSOLIDATION, source-proven
/// AUTHOR_CADENCE_WHOLE_CALLBACK, AUTHOR_DISCOVERY_DORMANT_SCHEDULE,
/// and cost-gated STRUCTURAL_HOTPATH_REWRITE.
/// It never invents candidates from mod names or unclassified source.
/// </summary>
public static class PassGeneratorService
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true
    };

    private static readonly Regex OnActionOpening = new(
        @"Observe\s*\(\s*(['""])PlayerPuppet\1\s*,\s*(['""])OnAction\2\s*,\s*function\s*\((?<args>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex OnActionClosing = new(
        @"end\s*\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex UnsafeLuaPatternChars = new(
        @"[%^$()%.\[\]*+\-?]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PassBuildResult Generate(
        string captureRoot,
        string modsRoot,
        string? outputZipPath = null)
    {
        captureRoot = Path.GetFullPath(captureRoot);
        modsRoot = Path.GetFullPath(modsRoot);

        var resolverPath = Path.Combine(captureRoot, ResultReportService.ResolverResolutionFileName);
        if (!File.Exists(resolverPath))
            throw new InvalidOperationException(
                $"Resolver output is missing: {resolverPath}. Run ANALYZE first.");

        if (!Directory.Exists(modsRoot))
            throw new DirectoryNotFoundException($"CET mods folder was not found: {modsRoot}");

        using var resolver = JsonDocument.Parse(File.ReadAllText(resolverPath));
        var candidates = ReadCandidates(resolver.RootElement);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "The resolver produced no V1 pass candidates. No ZIP was generated.");

        var groups = candidates
            .GroupBy(x => x.RelativeFile, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var fileManifest = new List<object>();
        var transformManifest = new List<object>();
        var skipped = new List<object>();

        foreach (var group in groups)
        {
            var expectedHashes = group
                .Select(x => x.SourceSha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (expectedHashes.Length != 1)
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(candidate, "Conflicting source hashes were recorded for the same file."));
                continue;
            }

            var livePath = ResolveInsideMods(modsRoot, group.Key);
            if (livePath is null || !File.Exists(livePath))
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(candidate, "Current deployed source file was not found."));
                continue;
            }

            var originalBytes = File.ReadAllBytes(livePath);
            var liveHash = Sha256(originalBytes);
            if (!liveHash.Equals(expectedHashes[0], StringComparison.OrdinalIgnoreCase))
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(
                        candidate,
                        $"Current source SHA256 no longer matches the analyzed capture. Expected {expectedHashes[0]}, got {liveHash}."));
                continue;
            }

            var transformed = TransformFile(
                originalBytes,
                group.OrderByDescending(x => x.LineStart).ToList(),
                transformManifest,
                skipped);

            if (transformed.AppliedTransforms == 0)
                continue;

            staged[group.Key] = transformed.Bytes;
            fileManifest.Add(new
            {
                path = group.Key.Replace('\\', '/'),
                sourceSha256 = liveHash,
                generatedSha256 = Sha256(transformed.Bytes),
                appliedTransforms = transformed.AppliedTransforms
            });
        }

        if (staged.Count == 0)
            throw new InvalidOperationException(
                "All resolver candidates were rejected during current-source revalidation. No ZIP was generated.");

        var callbackFileCount = staged.Count;

        // 0-Engine is the one deliberate fixed-runtime exception. The target
        // callbacks above are still selected strictly from capture/resolver
        // evidence; the shared runtime needed to execute those generic recipes
        // is shipped with every generated CET pass.
        var fixedRuntime = FixedZeroEngineRuntime.Prepare(modsRoot);
        foreach (var pair in fixedRuntime.Files)
        {
            if (staged.ContainsKey(pair.Key))
                throw new InvalidOperationException(
                    $"Resolver output unexpectedly targets fixed runtime file: {pair.Key}");

            staged[pair.Key] = pair.Value;

            var liveRuntimePath = ResolveInsideMods(modsRoot, pair.Key);
            var liveRuntimeHash =
                liveRuntimePath is not null && File.Exists(liveRuntimePath)
                    ? Sha256(File.ReadAllBytes(liveRuntimePath))
                    : null;

            fileManifest.Add(new
            {
                path = pair.Key.Replace('\\', '/'),
                sourceSha256 = liveRuntimeHash,
                generatedSha256 = Sha256(pair.Value),
                appliedTransforms = 0,
                fixedInfrastructure = true
            });
        }

        var resolverBytes = File.ReadAllBytes(resolverPath);
        var captureName = new DirectoryInfo(captureRoot).Name;
        var generatedUtc = DateTime.UtcNow;
        outputZipPath ??= Path.Combine(
            captureRoot,
            $"G-CET_PASS_{generatedUtc:yyyyMMdd-HHmmss}.zip");
        outputZipPath = Path.GetFullPath(outputZipPath);

        var manifest = new
        {
            schemaVersion = "0.1",
            generatedUtc = generatedUtc.ToString("O"),
            capture = captureName,
            resolverSha256 = Sha256(resolverBytes),
            policy = new
            {
                selection = "ONLY_AUTOMATABLE_CANDIDATES_FROM_G-CET_Resolver.json",
                modNameRules = false,
                sourceShaRequired = true,
                fullFileOverlay = true,
                cadenceTransforms = true,
                cadencePolicy = "AUTHOR_RATE_ONLY_AND_MEASURED_PAYBACK_REQUIRED",
                supportedPasses = new[]
                {
                    "ACTION_ROUTING_*",
                    "FRAME_DISPATCH_CONSOLIDATION",
                    "AUTHOR_CADENCE_WHOLE_CALLBACK",
                    "AUTHOR_DISCOVERY_DORMANT_SCHEDULE",
                    "STRUCTURAL_HOTPATH_REWRITE"
                },
                fixedRuntimeException = "0-Engine",
                note = "V1 target selection is capture/resolver-only. 0-Engine is the explicit fixed infrastructure exception and is always shipped so generic ActionRouter/frame registrar recipes have one known runtime."
            },
            fixedRuntime = new
            {
                included = true,
                name = "0-Engine",
                exception = true,
                baseVersion = FixedZeroEngineRuntime.BaseVersion,
                fixedRuntime.FixedVersion,
                fixedRuntime.LiveState,
                fixedRuntime.LiveInitSha256,
                fixedRuntime.FixedInitSha256,
                files = fixedRuntime.Files.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            },
            summary = new
            {
                files = staged.Count,
                callbackFiles = callbackFileCount,
                fixedRuntimeFiles = fixedRuntime.Files.Count,
                transforms = transformManifest.Count,
                skipped = skipped.Count
            },
            files = fileManifest,
            transforms = transformManifest,
            skipped
        };

        var outputDirectory = Path.GetDirectoryName(outputZipPath)!;
        Directory.CreateDirectory(outputDirectory);

        var manifestPath = Path.Combine(
            outputDirectory,
            Path.GetFileNameWithoutExtension(outputZipPath) + ".json");

        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        using (var archive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create))
        {
            foreach (var pair in staged.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entryName =
                    "bin/x64/plugins/cyber_engine_tweaks/mods/" +
                    pair.Key.Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(pair.Value, 0, pair.Value.Length);
            }
        }

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJson) + Environment.NewLine,
            new UTF8Encoding(false));

        return new PassBuildResult(
            outputZipPath,
            manifestPath,
            staged.Count,
            transformManifest.Count,
            skipped.Count);
    }

    private static List<PassCandidate> ReadCandidates(JsonElement root)
    {
        var result = new List<PassCandidate>();

        if (!root.TryGetProperty("callbackFamilies", out var families) ||
            families.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var family in families.EnumerateArray())
        {
            var resolverFamily = JsonString(family, "resolverFamily");
            if (!family.TryGetProperty("topConsumers", out var consumers) ||
                consumers.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var consumer in consumers.EnumerateArray())
            {
                if (!consumer.TryGetProperty("generic", out var generic) ||
                    generic.ValueKind != JsonValueKind.Object ||
                    !JsonBool(generic, "Automatable"))
                    continue;

                if (!consumer.TryGetProperty("source", out var source) ||
                    source.ValueKind != JsonValueKind.Object)
                    continue;

                var recipes = JsonStringArray(generic, "RecipeFamilies");
                CandidateKind? kind = null;

                if (resolverFamily.Equals("ONACTION", StringComparison.OrdinalIgnoreCase) &&
                    recipes.Any(x =>
                        x.StartsWith("ACTION_ROUTING", StringComparison.OrdinalIgnoreCase) &&
                        !x.Equals("ACTION_ROUTING_OVERRIDE", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Action;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("AUTHOR_CADENCE_WHOLE_CALLBACK", StringComparison.OrdinalIgnoreCase)))
                {
                    // Author cadence supersedes plain frame consolidation for
                    // this callback; never apply both transforms to one target.
                    kind = CandidateKind.AuthorCadence;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("AUTHOR_DISCOVERY_DORMANT_SCHEDULE", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.DiscoveryDormant;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("STRUCTURAL_HOTPATH_REWRITE", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Structural;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("HARD_DORMANT_GUARD_HOIST", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.HardDormant;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Frame;
                }

                if (kind is null)
                    continue;

                var registrationId = JsonNullableLong(consumer, "registrationId");
                var owner = JsonString(consumer, "owner");
                var relativeFile = JsonString(source, "RelativeFile", "relativeFile");
                var sha = JsonString(source, "Sha256", "sha256");
                var lineStart = (int)(JsonNullableLong(source, "LineStart", "lineStart") ?? 0);
                var lineEnd = (int)(JsonNullableLong(source, "LineEnd", "lineEnd") ?? 0);
                var pattern = JsonString(generic, "Pattern", "pattern");

                if (registrationId is null ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(relativeFile) ||
                    string.IsNullOrWhiteSpace(sha) ||
                    lineStart <= 0 ||
                    lineEnd < lineStart)
                    continue;

                var facts = ReadFacts(generic);

                result.Add(new PassCandidate
                {
                    Kind = kind.Value,
                    RegistrationId = registrationId.Value,
                    Owner = owner,
                    RelativeFile = relativeFile.Replace('\\', '/'),
                    SourceSha256 = sha,
                    LineStart = lineStart,
                    LineEnd = lineEnd,
                    Pattern = pattern,
                    Actions = facts.Actions,
                    ActionPatterns = facts.ActionPatterns,
                    RequiresActionType = facts.RequiresActionType,
                    RequiresActionValue = facts.RequiresActionValue,
                    ConsumerMutation = facts.ConsumerMutation,
                    StateGatePresent = facts.StateGatePresent,
                    DynamicGateResolved = facts.DynamicGateResolved,
                    DynamicGateExpression = facts.DynamicGateExpression,
                    AuthorCadenceWholeCallback = facts.AuthorCadenceWholeCallback,
                    AuthorBaseIntervalSeconds = facts.AuthorBaseIntervalSeconds,
                    AuthorDeltaParameter = facts.AuthorDeltaParameter,
                    EstimatedAvoidablePollingMsPerSecond = facts.EstimatedAvoidablePollingMsPerSecond,
                    EstimatedCallbackPaybackPct = facts.EstimatedCallbackPaybackPct,
                    EstimatedGlobalPaybackPct = facts.EstimatedGlobalPaybackPct,
                    StructuralExpressions = facts.StructuralExpressions,
                    StructuralConstructors = facts.StructuralConstructors,
                    HardDormantGuardHoist = facts.HardDormantGuardHoist,
                    HardDormantGateExpression = facts.HardDormantGateExpression,
                    HardDormantPreGuardReadCount = facts.HardDormantPreGuardReadCount,
                    AuthorDiscoveryDormantSchedule = facts.AuthorDiscoveryDormantSchedule,
                    AuthorDiscoveryIntervalSeconds = facts.AuthorDiscoveryIntervalSeconds,
                    AuthorDiscoveryAccumulator = facts.AuthorDiscoveryAccumulator,
                    AuthorDiscoveryGate = facts.AuthorDiscoveryGate,
                    AlsoFrameDispatch = recipes.Any(x =>
                        x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase))
                });
            }
        }

        return result;
    }

    private static CandidateFacts ReadFacts(JsonElement generic)
    {
        if (!generic.TryGetProperty("Facts", out var facts) ||
            facts.ValueKind != JsonValueKind.Object)
        {
            return new CandidateFacts();
        }

        return new CandidateFacts
        {
            Actions = JsonStringArray(facts, "actions"),
            ActionPatterns = JsonStringArray(facts, "actionPatterns"),
            RequiresActionType = JsonBool(facts, "requiresActionType"),
            RequiresActionValue = JsonBool(facts, "requiresActionValue"),
            ConsumerMutation = JsonBool(facts, "consumerMutation"),
            StateGatePresent = JsonBool(facts, "stateGatePresent"),
            DynamicGateResolved = JsonBool(facts, "gatedWildcardResolved"),
            DynamicGateExpression = JsonString(facts, "dynamicGateExpression"),
            AuthorCadenceWholeCallback = JsonBool(facts, "authorCadenceWholeCallback"),
            AuthorBaseIntervalSeconds = JsonDouble(facts, "baseIntervalSeconds"),
            AuthorDeltaParameter = JsonString(facts, "deltaParameter"),
            EstimatedAvoidablePollingMsPerSecond = JsonDouble(facts, "estimatedAvoidablePollingMsPerSecond"),
            EstimatedCallbackPaybackPct = JsonDouble(facts, "estimatedCallbackPaybackPct"),
            EstimatedGlobalPaybackPct = JsonDouble(facts, "estimatedGlobalPaybackPct"),
            StructuralExpressions = ReadStructuralExpressions(facts, "identicalExpressions"),
            StructuralConstructors = ReadStructuralExpressions(facts, "literalConstructors"),
            HardDormantGuardHoist = JsonBool(facts, "hardDormantGuardHoist"),
            HardDormantGateExpression = JsonString(facts, "hardDormantGateExpression"),
            HardDormantPreGuardReadCount = (int)(JsonNullableLong(facts, "hardDormantPreGuardReadCount") ?? 0),
            AuthorDiscoveryDormantSchedule = JsonBool(facts, "authorDiscoveryDormantSchedule"),
            AuthorDiscoveryIntervalSeconds = JsonDouble(facts, "authorDiscoveryIntervalSeconds"),
            AuthorDiscoveryAccumulator = JsonString(facts, "authorDiscoveryAccumulator"),
            AuthorDiscoveryGate = JsonString(facts, "authorDiscoveryGate")
        };
    }

    private static StructuralExpressionFact[] ReadStructuralExpressions(
        JsonElement facts,
        string name)
    {
        if (!facts.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return Array.Empty<StructuralExpressionFact>();

        var result = new List<StructuralExpressionFact>();
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                continue;

            var expression = JsonString(row, "Expression", "expression");
            var count = (int)(JsonNullableLong(row, "Count", "count") ?? 0);
            if (!string.IsNullOrWhiteSpace(expression) && count >= 2)
                result.Add(new StructuralExpressionFact(expression, count));
        }

        return result.ToArray();
    }

    private static TransformResult TransformFile(
        byte[] originalBytes,
        IReadOnlyList<PassCandidate> candidates,
        List<object> transformManifest,
        List<object> skipped)
    {
        var hasBom =
            originalBytes.Length >= 3 &&
            originalBytes[0] == 0xEF &&
            originalBytes[1] == 0xBB &&
            originalBytes[2] == 0xBF;

        var text = (hasBom
                ? Encoding.UTF8.GetString(originalBytes, 3, originalBytes.Length - 3)
                : Encoding.UTF8.GetString(originalBytes))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var newline = originalBytes.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
        var hadTerminalNewline = text.EndsWith('\n');
        var lines = text.Split('\n').ToList();
        if (hadTerminalNewline && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var applied = 0;
        var frameHelpers = new List<(PassCandidate Candidate, string Token)>();

        foreach (var candidate in candidates)
        {
            // Resolver source ranges are based on Split('\n') semantics and may
            // include the terminal empty logical line of a newline-terminated
            // file. TransformFile removes that empty element above, so accept
            // exactly that one-line difference rather than rejecting a valid
            // callback as stale/out-of-range.
            var effectiveLineEnd =
                hadTerminalNewline && candidate.LineEnd == lines.Count + 1
                    ? lines.Count
                    : candidate.LineEnd;

            if (candidate.LineStart > lines.Count)
            {
                skipped.Add(Skip(candidate, "Recorded source range is outside the current file."));
                continue;
            }

            if (candidate.Kind == CandidateKind.AuthorCadence)
            {
                if (!candidate.AuthorCadenceWholeCallback ||
                    candidate.AuthorBaseIntervalSeconds <= 0 ||
                    string.IsNullOrWhiteSpace(candidate.AuthorDeltaParameter))
                {
                    skipped.Add(Skip(
                        candidate,
                        "Resolver did not emit a complete whole-callback author cadence handoff."));
                    continue;
                }

                if (effectiveLineEnd > lines.Count)
                {
                    skipped.Add(Skip(candidate, "Recorded author-cadence source range is outside the current file."));
                    continue;
                }

                var authorSegmentLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(effectiveLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var authorSegment = string.Join("\n", authorSegmentLines);

                var authorOpening = Regex.Match(
                    authorSegment,
                    @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\((?<args>[^)]*)\)",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!authorOpening.Success)
                {
                    skipped.Add(Skip(
                        candidate,
                        "The source-proven author-cadence onUpdate opening could not be revalidated."));
                    continue;
                }

                var parameters = authorOpening.Groups["args"].Value
                    .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parameters.Length != 1 ||
                    !parameters[0].Equals(candidate.AuthorDeltaParameter, StringComparison.Ordinal))
                {
                    skipped.Add(Skip(
                        candidate,
                        "The author-cadence callback delta parameter no longer matches resolver evidence."));
                    continue;
                }

                var authorClosing = Regex.Match(
                    authorSegment,
                    @"end\s*\)\s*;?\s*$",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!authorClosing.Success || authorClosing.Index <= authorOpening.Index)
                {
                    skipped.Add(Skip(
                        candidate,
                        "The source-proven author-cadence onUpdate closing could not be revalidated."));
                    continue;
                }

                var authorFunctionName = $"__gcetAuthorCadence_{candidate.RegistrationId}";
                var rewritten =
                    authorSegment[..authorOpening.Index] +
                    $"local function {authorFunctionName}({candidate.AuthorDeltaParameter})" +
                    authorSegment[(authorOpening.Index + authorOpening.Length)..];
                var rewrittenClosing = Regex.Match(
                    rewritten,
                    @"end\s*\)\s*;?\s*$",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!rewrittenClosing.Success)
                {
                    skipped.Add(Skip(
                        candidate,
                        "The author-cadence callback could not be converted to a named function."));
                    continue;
                }

                rewritten =
                    rewritten[..rewrittenClosing.Index] +
                    "end" +
                    rewritten[(rewrittenClosing.Index + rewrittenClosing.Length)..];

                var authorIndent = Regex.Match(authorSegmentLines[0], @"^\s*").Value;
                var authorRegistration = BuildAuthorCadenceRegistration(
                    candidate,
                    authorFunctionName,
                    authorIndent);
                var authorReplacementLines = (rewritten + "\n\n" + authorRegistration).Split('\n');

                lines.RemoveRange(
                    candidate.LineStart - 1,
                    effectiveLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, authorReplacementLines);

                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "AUTHOR_CADENCE_WHOLE_CALLBACK",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                    facts = new
                    {
                        baseIntervalSeconds = candidate.AuthorBaseIntervalSeconds,
                        deltaParameter = candidate.AuthorDeltaParameter,
                        estimatedAvoidablePollingMsPerSecond = candidate.EstimatedAvoidablePollingMsPerSecond,
                        estimatedCallbackPaybackPct = candidate.EstimatedCallbackPaybackPct,
                        estimatedGlobalPaybackPct = candidate.EstimatedGlobalPaybackPct,
                        semantics = "author rate preserved; spread disabled; no catch-up; original onUpdate fallback retained"
                    }
                });
                applied++;
                continue;
            }

            if (candidate.Kind == CandidateKind.DiscoveryDormant)
            {
                if (effectiveLineEnd > lines.Count ||
                    !candidate.AuthorDiscoveryDormantSchedule ||
                    candidate.AuthorDiscoveryIntervalSeconds <= 0 ||
                    string.IsNullOrWhiteSpace(candidate.AuthorDiscoveryAccumulator) ||
                    string.IsNullOrWhiteSpace(candidate.AuthorDiscoveryGate))
                {
                    skipped.Add(Skip(candidate, "Author discovery-dormant handoff is incomplete or outside the current source range."));
                    continue;
                }

                // Profiler ranges can stop at the callback body's final inner
                // end while leaving the registration's closing end) on the next
                // line. Complete only that syntactic boundary; do not broaden
                // the semantic source window.
                var discoveryLineEnd = effectiveLineEnd;
                for (var i = effectiveLineEnd; i < Math.Min(lines.Count, effectiveLineEnd + 3); i++)
                {
                    if (!Regex.IsMatch(
                            lines[i].Trim(),
                            @"^end\s*\)\s*;?\s*$",
                            RegexOptions.CultureInvariant))
                        continue;

                    discoveryLineEnd = i + 1;
                    break;
                }

                var discoveryLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(discoveryLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var discoverySegment = string.Join("\n", discoveryLines);
                var discoveryIndent = Regex.Match(discoveryLines[0], @"^\s*").Value;

                if (!TryBuildDiscoveryDormantRewrite(
                        candidate,
                        discoverySegment,
                        discoveryIndent,
                        out var discoveryRewrite,
                        out var discoveryBlocker))
                {
                    skipped.Add(Skip(candidate, discoveryBlocker));
                    continue;
                }

                var discoveryCallback = discoveryRewrite.Callback;
                if (candidate.AlsoFrameDispatch)
                {
                    var registrationMatch = Regex.Match(
                        discoveryCallback,
                        @"\bregisterForEvent\b",
                        RegexOptions.CultureInvariant);
                    if (registrationMatch.Success)
                    {
                        var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                        discoveryCallback =
                            discoveryCallback[..registrationMatch.Index] +
                            token +
                            discoveryCallback[(registrationMatch.Index + "registerForEvent".Length)..];
                        frameHelpers.Add((candidate, token));
                    }
                }

                var discoveryReplacementLines =
                    (discoveryRewrite.Prefix + "\n" + discoveryCallback).Split('\n');

                lines.RemoveRange(
                    candidate.LineStart - 1,
                    discoveryLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, discoveryReplacementLines);

                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "AUTHOR_DISCOVERY_DORMANT_SCHEDULE",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                    facts = new
                    {
                        intervalSeconds = candidate.AuthorDiscoveryIntervalSeconds,
                        accumulator = candidate.AuthorDiscoveryAccumulator,
                        activeGate = candidate.AuthorDiscoveryGate,
                        frameDispatchConsolidation = candidate.AlsoFrameDispatch,
                        semantics = "author inactive discovery interval preserved through 0-Engine; original frame-timer path retained as fallback"
                    }
                });
                applied++;
                continue;
            }

            if (candidate.Kind == CandidateKind.Structural)
            {
                if (effectiveLineEnd > lines.Count)
                {
                    skipped.Add(Skip(candidate, "Recorded structural callback range is outside the current file."));
                    continue;
                }

                var structuralLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(effectiveLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var structuralSegment = string.Join("\n", structuralLines);

                var structuralOpening = Regex.Match(
                    structuralSegment,
                    @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\((?<args>[^)]*)\)",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!structuralOpening.Success)
                {
                    skipped.Add(Skip(candidate, "Structural rewrite could not revalidate the direct onUpdate callback opening."));
                    continue;
                }

                var prefix = new List<string>();
                var callbackLocals = new List<string>();
                var rewrittenBody = structuralSegment[
                    (structuralOpening.Index + structuralOpening.Length)..];
                var transformedExpressions = new List<string>();
                var transformedConstructors = new List<string>();
                var ordinal = 0;

                // Resolver authorization is authoritative; generator still
                // revalidates the current source. If the serialized expression
                // detail is absent, rediscover only the same narrow finite
                // whitelist that the resolver is allowed to authorize.
                var structuralExpressions = candidate.StructuralExpressions.Length > 0
                    ? candidate.StructuralExpressions
                    : DiscoverRepeatedStructuralExpressions(structuralSegment, constructors: false);
                var structuralConstructors = candidate.StructuralConstructors.Length > 0
                    ? candidate.StructuralConstructors
                    : DiscoverRepeatedStructuralExpressions(structuralSegment, constructors: true);

                foreach (var fact in structuralExpressions)
                {
                    var observed = Regex.Matches(
                        rewrittenBody,
                        Regex.Escape(fact.Expression),
                        RegexOptions.CultureInvariant).Count;
                    if (observed < 2)
                        continue;

                    ordinal++;
                    var localName = $"__gcetReuse_{candidate.RegistrationId}_{ordinal}";
                    callbackLocals.Add($"local {localName} = {fact.Expression}");
                    rewrittenBody = rewrittenBody.Replace(
                        fact.Expression,
                        localName,
                        StringComparison.Ordinal);
                    transformedExpressions.Add(fact.Expression);
                }

                foreach (var fact in structuralConstructors)
                {
                    var observed = Regex.Matches(
                        rewrittenBody,
                        Regex.Escape(fact.Expression),
                        RegexOptions.CultureInvariant).Count;
                    if (observed < 2)
                        continue;

                    ordinal++;
                    var localName = $"__gcetStatic_{candidate.RegistrationId}_{ordinal}";
                    prefix.Add($"local {localName} = {fact.Expression}");
                    rewrittenBody = rewrittenBody.Replace(
                        fact.Expression,
                        localName,
                        StringComparison.Ordinal);
                    transformedConstructors.Add(fact.Expression);
                }

                if (transformedExpressions.Count == 0 &&
                    transformedConstructors.Count == 0)
                {
                    skipped.Add(Skip(candidate, "Resolver structural expressions no longer repeat in the current callback source."));
                    continue;
                }

                var openingText = structuralSegment[
                    ..(structuralOpening.Index + structuralOpening.Length)];
                var structuralIndent = Regex.Match(structuralLines[0], @"^\s*").Value;
                var localIndent = structuralIndent + "    ";
                var guardText =
                    candidate.HardDormantGuardHoist &&
                    !string.IsNullOrWhiteSpace(candidate.HardDormantGateExpression)
                        ? $"\n{localIndent}if not {candidate.HardDormantGateExpression} then return end -- G-CET dormant guard hoist"
                        : "";
                var localText = callbackLocals.Count == 0
                    ? ""
                    : "\n" + string.Join("\n", callbackLocals.Select(x => localIndent + x));
                var rewritten = openingText + guardText + localText + rewrittenBody;

                if (candidate.AlsoFrameDispatch)
                {
                    var registrationMatch = Regex.Match(
                        rewritten,
                        @"\bregisterForEvent\b",
                        RegexOptions.CultureInvariant);
                    if (registrationMatch.Success)
                    {
                        var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                        rewritten =
                            rewritten[..registrationMatch.Index] +
                            token +
                            rewritten[(registrationMatch.Index + "registerForEvent".Length)..];
                        frameHelpers.Add((candidate, token));
                    }
                }

                var replacementText =
                    (prefix.Count == 0
                        ? ""
                        : string.Join("\n", prefix.Select(x => structuralIndent + x)) + "\n") +
                    rewritten;
                var structuralReplacementLines = replacementText.Split('\n');

                lines.RemoveRange(
                    candidate.LineStart - 1,
                    effectiveLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, structuralReplacementLines);

                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "STRUCTURAL_HOTPATH_REWRITE",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                    facts = new
                    {
                        identicalExpressions = transformedExpressions,
                        literalConstructors = transformedConstructors,
                        callbackLocalReuse = true,
                        literalConstructorHoist = true,
                        hardDormantGuardHoist = candidate.HardDormantGuardHoist,
                        hardDormantGateExpression = candidate.HardDormantGateExpression,
                        hardDormantPreGuardReadCount = candidate.HardDormantPreGuardReadCount,
                        frameDispatchConsolidation = candidate.AlsoFrameDispatch,
                        estimatedCallbackPaybackPct = candidate.EstimatedCallbackPaybackPct,
                        estimatedGlobalPaybackPct = candidate.EstimatedGlobalPaybackPct
                    }
                });
                applied++;
                continue;
            }

            if (candidate.Kind == CandidateKind.HardDormant)
            {
                if (effectiveLineEnd > lines.Count ||
                    !candidate.HardDormantGuardHoist ||
                    string.IsNullOrWhiteSpace(candidate.HardDormantGateExpression))
                {
                    skipped.Add(Skip(candidate, "Dormant guard-hoist handoff is incomplete or outside the current source range."));
                    continue;
                }

                var dormantLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(effectiveLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var dormantSegment = string.Join("\n", dormantLines);
                var dormantOpening = Regex.Match(
                    dormantSegment,
                    @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\((?<args>[^)]*)\)",
                    RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!dormantOpening.Success)
                {
                    skipped.Add(Skip(candidate, "Dormant guard hoist could not revalidate the direct onUpdate callback opening."));
                    continue;
                }

                var dormantIndent = Regex.Match(dormantLines[0], @"^\s*").Value;
                var bodyIndent = dormantIndent + "    ";
                var dormantRewritten =
                    dormantSegment[..(dormantOpening.Index + dormantOpening.Length)] +
                    $"\n{bodyIndent}if not {candidate.HardDormantGateExpression} then return end -- G-CET dormant guard hoist" +
                    dormantSegment[(dormantOpening.Index + dormantOpening.Length)..];

                if (candidate.AlsoFrameDispatch)
                {
                    var registrationMatch = Regex.Match(
                        dormantRewritten,
                        @"\bregisterForEvent\b",
                        RegexOptions.CultureInvariant);
                    if (registrationMatch.Success)
                    {
                        var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                        dormantRewritten =
                            dormantRewritten[..registrationMatch.Index] +
                            token +
                            dormantRewritten[(registrationMatch.Index + "registerForEvent".Length)..];
                        frameHelpers.Add((candidate, token));
                    }
                }

                var dormantReplacementLines = dormantRewritten.Split('\n');
                lines.RemoveRange(
                    candidate.LineStart - 1,
                    effectiveLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, dormantReplacementLines);

                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "HARD_DORMANT_GUARD_HOIST",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                    facts = new
                    {
                        gateExpression = candidate.HardDormantGateExpression,
                        preGuardReadCount = candidate.HardDormantPreGuardReadCount,
                        frameDispatchConsolidation = candidate.AlsoFrameDispatch,
                        semantics = "existing source-proven inactive guard duplicated at callback entry; original guard retained"
                    }
                });
                applied++;
                continue;
            }

            if (candidate.Kind == CandidateKind.Frame)
            {
                var index = candidate.LineStart - 1;
                var line = lines[index];

                if (!line.Contains("registerForEvent", StringComparison.Ordinal) ||
                    !line.Contains("onUpdate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(Skip(
                        candidate,
                        "Recorded source line is no longer a direct registerForEvent(onUpdate) registration."));
                    continue;
                }

                var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                var marker = line.IndexOf("registerForEvent", StringComparison.Ordinal);
                lines[index] =
                    line[..marker] +
                    token +
                    line[(marker + "registerForEvent".Length)..];

                frameHelpers.Add((candidate, token));
                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "FRAME_DISPATCH_CONSOLIDATION",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd }
                });
                applied++;
                continue;
            }

            if (effectiveLineEnd > lines.Count)
            {
                skipped.Add(Skip(candidate, "Recorded OnAction source range is outside the current file."));
                continue;
            }

            var segmentLines = lines
                .Skip(candidate.LineStart - 1)
                .Take(effectiveLineEnd - candidate.LineStart + 1)
                .ToArray();
            var segment = string.Join("\n", segmentLines);

            var opening = OnActionOpening.Match(segment);
            if (!opening.Success)
            {
                skipped.Add(Skip(
                    candidate,
                    "The analyzed OnAction Observe opening could not be revalidated in the recorded source range."));
                continue;
            }

            if (candidate.ActionPatterns.Any(x => UnsafeLuaPatternChars.IsMatch(x)))
            {
                skipped.Add(Skip(
                    candidate,
                    "The action-name pattern contains Lua pattern metacharacters. V1 only generates literal substring routing."));
                continue;
            }

            if (candidate.Actions.Length == 0 &&
                candidate.ActionPatterns.Length == 0 &&
                !candidate.DynamicGateResolved)
            {
                skipped.Add(Skip(candidate, "No concrete action names, patterns, or proven dynamic state gate were emitted by the resolver."));
                continue;
            }

            if (candidate.DynamicGateResolved &&
                string.IsNullOrWhiteSpace(candidate.DynamicGateExpression))
            {
                skipped.Add(Skip(candidate, "Resolver marked gated wildcard routing but did not emit a gate expression."));
                continue;
            }

            var functionName = $"__gcetOnAction_{candidate.RegistrationId}";
            var callbackArgs = opening.Groups["args"].Value;
            var replaced =
                segment[..opening.Index] +
                $"local function {functionName}({callbackArgs})" +
                segment[(opening.Index + opening.Length)..];

            var closing = OnActionClosing.Match(replaced);
            if (!closing.Success)
            {
                skipped.Add(Skip(
                    candidate,
                    "The analyzed OnAction Observe closing end) could not be revalidated in the recorded source range."));
                continue;
            }

            replaced =
                replaced[..closing.Index] +
                "end" +
                replaced[(closing.Index + closing.Length)..];

            var indent = Regex.Match(segmentLines[0], @"^\s*").Value;
            var generated = BuildActionRegistration(candidate, functionName, indent);

            var replacementLines = (replaced + "\n\n" + generated)
                .Split('\n');

            lines.RemoveRange(
                candidate.LineStart - 1,
                effectiveLineEnd - candidate.LineStart + 1);
            lines.InsertRange(candidate.LineStart - 1, replacementLines);

            transformManifest.Add(new
            {
                registrationId = candidate.RegistrationId,
                owner = candidate.Owner,
                type = candidate.Pattern,
                file = candidate.RelativeFile,
                sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                facts = new
                {
                    actions = candidate.Actions,
                    actionPatterns = candidate.ActionPatterns,
                    candidate.StateGatePresent,
                    candidate.DynamicGateResolved,
                    candidate.DynamicGateExpression,
                    candidate.ConsumerMutation,
                    candidate.RequiresActionType,
                    candidate.RequiresActionValue
                }
            });
            applied++;
        }

        if (frameHelpers.Count > 0)
        {
            var header = new List<string>
            {
                "-- G-CET generated frame registrar: source-selected from resolver evidence."
            };

            foreach (var item in frameHelpers.OrderBy(x => x.Candidate.RegistrationId))
            {
                var owner = LuaQuote(item.Candidate.Owner);
                header.Add($"local {item.Token} = registerForEvent");
                header.Add("do");
                header.Add("    local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")");
                header.Add("    if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.MakeEventRegistrar) == \"function\" then");
                header.Add($"        {item.Token} = __gcetEngine.MakeEventRegistrar({owner}, registerForEvent)");
                header.Add("    end");
                header.Add("end");
                header.Add("");
            }

            lines.InsertRange(0, header);
        }

        var outputText = string.Join(newline, lines);
        if (hadTerminalNewline)
            outputText += newline;

        var body = Encoding.UTF8.GetBytes(outputText);
        if (!hasBom)
            return new TransformResult(body, applied);

        var withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
        return new TransformResult(withBom, applied);
    }

    private static bool TryBuildDiscoveryDormantRewrite(
        PassCandidate candidate,
        string segment,
        string indent,
        out DiscoveryRewriteParts rewrite,
        out string blocker)
    {
        rewrite = new DiscoveryRewriteParts("", "");
        blocker = "Discovery dormant rewrite could not be revalidated.";

        var opening = Regex.Match(
            segment,
            @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\(\s*(?<delta>[A-Za-z_]\w*)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var closing = Regex.Match(
            segment,
            @"end\s*\)\s*;?\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!opening.Success || !closing.Success || closing.Index <= opening.Index)
        {
            blocker = "Discovery dormant callback opening/closing could not be revalidated.";
            return false;
        }

        var delta = opening.Groups["delta"].Value;
        var body = segment.Substring(
            opening.Index + opening.Length,
            closing.Index - (opening.Index + opening.Length));
        var bodyLines = body
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .ToList();

        var inactiveIndex = -1;
        var inactiveIndent = 0;
        var inactivePattern = new Regex(
            @"^(?<indent>\s*)if\s+not\s+" +
            Regex.Escape(candidate.AuthorDiscoveryGate) +
            @"\s+then\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        for (var i = 0; i < bodyLines.Count; i++)
        {
            var match = inactivePattern.Match(bodyLines[i]);
            if (!match.Success)
                continue;
            inactiveIndex = i;
            inactiveIndent = match.Groups["indent"].Value.Length;
            break;
        }

        if (inactiveIndex < 0)
        {
            blocker = "Author inactive discovery gate no longer matches current source.";
            return false;
        }

        var inactiveEnd = FindSameIndentEnd(bodyLines, inactiveIndex, inactiveIndent);
        if (inactiveEnd <= inactiveIndex)
        {
            blocker = "Author inactive discovery block boundary could not be revalidated.";
            return false;
        }

        var incrementFound = false;
        var thresholdIndex = -1;
        var thresholdEnd = -1;
        var thresholdIndent = 0;
        var intervalText = candidate.AuthorDiscoveryIntervalSeconds.ToString(
            "0.################",
            System.Globalization.CultureInfo.InvariantCulture);

        for (var i = inactiveIndex + 1; i < inactiveEnd; i++)
        {
            var line = bodyLines[i];

            if (Regex.IsMatch(
                    line,
                    @"^\s*" + Regex.Escape(candidate.AuthorDiscoveryAccumulator) +
                    @"\s*(?:=\s*" + Regex.Escape(candidate.AuthorDiscoveryAccumulator) +
                    @"\s*\+\s*" + Regex.Escape(delta) +
                    @"|\+=\s*" + Regex.Escape(delta) + @")\s*;?\s*$",
                    RegexOptions.CultureInvariant))
            {
                incrementFound = true;
                continue;
            }

            var threshold = Regex.Match(
                line,
                @"^(?<indent>\s*)if\s+" +
                Regex.Escape(candidate.AuthorDiscoveryAccumulator) +
                @"\s*(?:>=|>)\s*(?<seconds>\d+(?:\.\d+)?)\s+then\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!threshold.Success)
                continue;

            if (!double.TryParse(
                    threshold.Groups["seconds"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var currentSeconds) ||
                Math.Abs(currentSeconds - candidate.AuthorDiscoveryIntervalSeconds) > 0.000001)
            {
                blocker = "Author discovery interval changed since resolver analysis.";
                return false;
            }

            thresholdIndex = i;
            thresholdIndent = threshold.Groups["indent"].Value.Length;
            thresholdEnd = FindSameIndentEnd(bodyLines, i, thresholdIndent);
            break;
        }

        if (!incrementFound || thresholdIndex < 0 || thresholdEnd <= thresholdIndex)
        {
            blocker = "Author discovery accumulator/threshold shape no longer matches resolver evidence.";
            return false;
        }

        var discoveryRegion = bodyLines
            .Skip(thresholdIndex + 1)
            .Take(thresholdEnd - thresholdIndex - 1)
            .ToArray();

        var discoveryText = string.Join("\n", discoveryRegion);
        if (!Regex.IsMatch(
                discoveryText,
                @"\b" + Regex.Escape(candidate.AuthorDiscoveryAccumulator) +
                @"\s*=\s*0(?:\.0+)?\b",
                RegexOptions.CultureInvariant) ||
            Regex.IsMatch(
                discoveryText,
                @"\b" + Regex.Escape(delta) + @"\b",
                RegexOptions.CultureInvariant))
        {
            blocker = "Discovery region reset/delta proof no longer matches resolver evidence.";
            return false;
        }

        var commonIndent = discoveryRegion
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Length - x.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var functionIndent = indent + "    ";
        var discoveryBody = discoveryRegion
            .Select(x =>
                string.IsNullOrWhiteSpace(x)
                    ? ""
                    : functionIndent + x[Math.Min(commonIndent, x.Length)..])
            .ToArray();

        var functionName = $"__gcetDiscovery_{candidate.RegistrationId}";
        var handleName = $"__gcetDiscoveryHandle_{candidate.RegistrationId}";
        var scheduledName = $"__gcetDiscoveryScheduled_{candidate.RegistrationId}";
        var owner = LuaQuote(candidate.Owner);
        var id = LuaQuote($"G-CET.Discovery.{candidate.RegistrationId}");

        var prefixLines = new List<string>
        {
            $"{indent}local {scheduledName} = false",
            $"{indent}local {handleName} = nil",
            $"{indent}local function {functionName}()",
            $"{functionIndent}if {candidate.AuthorDiscoveryGate} then return end"
        };
        prefixLines.AddRange(discoveryBody);
        prefixLines.Add($"{indent}end");
        prefixLines.Add($"{indent}registerForEvent(\"onInit\", function()");
        prefixLines.Add($"{functionIndent}local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")");
        prefixLines.Add($"{functionIndent}if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.Schedule) == \"table\" and type(__gcetEngine.Schedule.Every) == \"function\" then");
        prefixLines.Add($"{functionIndent}    local __gcetScheduleOk, __gcetScheduleHandle = pcall(function()");
        prefixLines.Add($"{functionIndent}        return __gcetEngine.Schedule.Every({intervalText}, {{");
        prefixLines.Add($"{functionIndent}            id = {id},");
        prefixLines.Add($"{functionIndent}            owner = {owner},");
        prefixLines.Add($"{functionIndent}            pause = \"never\",");
        prefixLines.Add($"{functionIndent}            spread = false,");
        prefixLines.Add($"{functionIndent}            catchUp = false");
        prefixLines.Add($"{functionIndent}        }}, function(ctx)");
        prefixLines.Add($"{functionIndent}            {functionName}()");
        prefixLines.Add($"{functionIndent}        end)");
        prefixLines.Add($"{functionIndent}    end)");
        prefixLines.Add($"{functionIndent}    if __gcetScheduleOk and type(__gcetScheduleHandle) == \"table\" then");
        prefixLines.Add($"{functionIndent}        {handleName} = __gcetScheduleHandle");
        prefixLines.Add($"{functionIndent}        {scheduledName} = true");
        prefixLines.Add($"{functionIndent}    end");
        prefixLines.Add($"{functionIndent}end");
        prefixLines.Add($"{indent}end)");
        prefixLines.Add($"{indent}registerForEvent(\"onShutdown\", function()");
        prefixLines.Add($"{functionIndent}if {handleName} and type({handleName}.Cancel) == \"function\" then pcall({handleName}.Cancel) end");
        prefixLines.Add($"{functionIndent}{handleName} = nil");
        prefixLines.Add($"{functionIndent}{scheduledName} = false");
        prefixLines.Add($"{indent}end)");

        var wrappedInactive = new List<string>
        {
            new string(' ', inactiveIndent) + $"if not {scheduledName} then"
        };
        for (var i = inactiveIndex; i <= inactiveEnd; i++)
            wrappedInactive.Add(new string(' ', 4) + bodyLines[i]);
        wrappedInactive.Add(new string(' ', inactiveIndent) + "end");

        bodyLines.RemoveRange(inactiveIndex, inactiveEnd - inactiveIndex + 1);
        bodyLines.InsertRange(inactiveIndex, wrappedInactive);

        var callback =
            segment[..(opening.Index + opening.Length)] +
            string.Join("\n", bodyLines) +
            segment[closing.Index..];

        rewrite = new DiscoveryRewriteParts(
            string.Join("\n", prefixLines),
            callback);
        blocker = "";
        return true;
    }

    private static int FindSameIndentEnd(
        IReadOnlyList<string> lines,
        int start,
        int indent)
    {
        for (var i = start + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!Regex.IsMatch(
                    trimmed,
                    @"^end\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            var candidateIndent = lines[i].Length - lines[i].TrimStart().Length;
            if (candidateIndent == indent)
                return i;
        }

        return -1;
    }

    private static string BuildAuthorCadenceRegistration(
        PassCandidate candidate,
        string functionName,
        string indent)
    {
        var id = LuaQuote($"G-CET.AuthorCadence.{candidate.RegistrationId}");
        var owner = LuaQuote(candidate.Owner);
        var seconds = candidate.AuthorBaseIntervalSeconds.ToString(
            "0.################",
            System.Globalization.CultureInfo.InvariantCulture);
        var handle = $"__gcetAuthorCadenceHandle_{candidate.RegistrationId}";
        var scheduled = $"__gcetAuthorCadenceScheduled_{candidate.RegistrationId}";

        var lines = new List<string>
        {
            $"{indent}local {handle} = nil",
            $"{indent}local {scheduled} = false",
            $"{indent}registerForEvent(\"onInit\", function()",
            $"{indent}    local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")",
            $"{indent}    if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.Schedule) == \"table\" and type(__gcetEngine.Schedule.Every) == \"function\" then",
            $"{indent}        local __gcetScheduleOk, __gcetScheduleHandle = pcall(function()",
            $"{indent}            return __gcetEngine.Schedule.Every({seconds}, {{",
            $"{indent}                id = {id},",
            $"{indent}                owner = {owner},",
            $"{indent}                pause = \"never\",",
            $"{indent}                spread = false,",
            $"{indent}                catchUp = false",
            $"{indent}            }}, function(ctx)",
            $"{indent}                {functionName}(ctx.elapsed)",
            $"{indent}            end)",
            $"{indent}        end)",
            $"{indent}        if __gcetScheduleOk and type(__gcetScheduleHandle) == \"table\" then",
            $"{indent}            {handle} = __gcetScheduleHandle",
            $"{indent}            {scheduled} = true",
            $"{indent}        end",
            $"{indent}    end",
            $"{indent}    if not {scheduled} then",
            $"{indent}        registerForEvent(\"onUpdate\", {functionName})",
            $"{indent}    end",
            $"{indent}end)",
            $"{indent}registerForEvent(\"onShutdown\", function()",
            $"{indent}    if {handle} and type({handle}.Cancel) == \"function\" then pcall({handle}.Cancel) end",
            $"{indent}    {handle} = nil",
            $"{indent}    {scheduled} = false",
            $"{indent}end)"
        };

        return string.Join("\n", lines);
    }

    private static string BuildActionRegistration(
        PassCandidate candidate,
        string functionName,
        string indent)
    {
        var lines = new List<string>();
        var idBase = $"G-CET.{candidate.RegistrationId}";
        var owner = LuaQuote(candidate.Owner);

        lines.Add($"{indent}local __gcetRouted_{candidate.RegistrationId} = false");
        lines.Add($"{indent}local __gcetHandles_{candidate.RegistrationId} = {{}}");
        lines.Add($"{indent}local __gcetOk_{candidate.RegistrationId}, __gcetEngine_{candidate.RegistrationId} = pcall(GetMod, \"0-Engine\")");
        lines.Add($"{indent}if __gcetOk_{candidate.RegistrationId} and type(__gcetEngine_{candidate.RegistrationId}) == \"table\" and type(__gcetEngine_{candidate.RegistrationId}.SubscribeAction) == \"function\" then");
        lines.Add($"{indent}    __gcetRouted_{candidate.RegistrationId} = pcall(function()");

        if (candidate.DynamicGateResolved)
        {
            lines.Add($"{indent}        local __gcetExact_{candidate.RegistrationId} = {{}}");
            foreach (var action in candidate.Actions)
                lines.Add($"{indent}        __gcetExact_{candidate.RegistrationId}[{LuaQuote(action)}] = true");

            lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetEngine_{candidate.RegistrationId}.SubscribeAction({{");
            lines.Add($"{indent}            id = {LuaQuote(idBase + ".GatedWildcard")},");
            lines.Add($"{indent}            actions = \"*\",");
            lines.Add($"{indent}            decodeType = false");
            lines.Add($"{indent}        }}, function(this, action, consumer, routedName)");

            var routedConditions = new List<string>();
            if (candidate.Actions.Length > 0)
                routedConditions.Add($"(routedName and __gcetExact_{candidate.RegistrationId}[routedName])");

            if (candidate.ActionPatterns.Length > 0)
            {
                routedConditions.Add(
                    "(routedName and (" +
                    string.Join(
                        " or ",
                        candidate.ActionPatterns.Select(x =>
                            $"string.find(routedName, {LuaQuote(x)}, 1, true)")) +
                    "))");
            }

            routedConditions.Add($"({candidate.DynamicGateExpression})");

            lines.Add($"{indent}            if {string.Join(" or ", routedConditions)} then");
            lines.Add($"{indent}                {functionName}(this, action, consumer)");
            lines.Add($"{indent}            end");
            lines.Add($"{indent}        end, {owner})");
        }
        else
        {
            if (candidate.Actions.Length > 0)
            {
                var actions = string.Join(", ", candidate.Actions.Select(LuaQuote));
                lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetEngine_{candidate.RegistrationId}.SubscribeAction({{");
                lines.Add($"{indent}            id = {LuaQuote(idBase + ".Exact")},");
                lines.Add($"{indent}            actions = {{ {actions} }},");
                // The generated router never consumes routed action type. The
                // original callback still receives the raw action object and
                // performs any source-required GetType() itself, so decoding
                // type inside 0-Engine would be duplicate work.
                lines.Add($"{indent}            decodeType = false");
                lines.Add($"{indent}        }}, {functionName}, {owner})");
            }

            if (candidate.ActionPatterns.Length > 0)
            {
                lines.Add($"{indent}        local __gcetExact_{candidate.RegistrationId} = {{}}");
                foreach (var action in candidate.Actions)
                    lines.Add($"{indent}        __gcetExact_{candidate.RegistrationId}[{LuaQuote(action)}] = true");

                lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetEngine_{candidate.RegistrationId}.SubscribeAction({{");
                lines.Add($"{indent}            id = {LuaQuote(idBase + ".Pattern")},");
                lines.Add($"{indent}            actions = \"*\",");
                lines.Add($"{indent}            decodeType = false");
                lines.Add($"{indent}        }}, function(this, action, consumer, routedName)");

                var conditions = string.Join(
                    " or ",
                    candidate.ActionPatterns.Select(x =>
                        $"string.find(routedName, {LuaQuote(x)}, 1, true)"));

                lines.Add($"{indent}            if routedName and not __gcetExact_{candidate.RegistrationId}[routedName] and ({conditions}) then");
                lines.Add($"{indent}                {functionName}(this, action, consumer)");
                lines.Add($"{indent}            end");
                lines.Add($"{indent}        end, {owner})");
            }
        }

        lines.Add($"{indent}    end)");
        lines.Add($"{indent}    if not __gcetRouted_{candidate.RegistrationId} then");
        lines.Add($"{indent}        for _, __gcetHandle in ipairs(__gcetHandles_{candidate.RegistrationId}) do");
        lines.Add($"{indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end");
        lines.Add($"{indent}        end");
        lines.Add($"{indent}    end");
        lines.Add($"{indent}end");
        lines.Add($"{indent}if not __gcetRouted_{candidate.RegistrationId} then Observe(\"PlayerPuppet\", \"OnAction\", {functionName}) end");

        return string.Join("\n", lines);
    }

    private static StructuralExpressionFact[] DiscoverRepeatedStructuralExpressions(
        string source,
        bool constructors)
    {
        var pattern = constructors
            ? new Regex(
                @"\b(?:CName|TweakDBID)\.new\s*\(\s*(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>\s*\)",
                RegexOptions.CultureInvariant)
            : new Regex(
                @"\bGame\.(?:GetPlayer|GetTargetingSystem|GetBlackboardSystem|GetAllBlackboardDefs|GetQuestsSystem|GetTimeSystem|GetStatsSystem|GetStatPoolsSystem|GetSystemRequestsHandler|GetTeleportationFacility|GetCameraSystem)\s*\(\s*\)",
                RegexOptions.CultureInvariant);

        return pattern.Matches(source)
            .Cast<Match>()
            .GroupBy(x => x.Value, StringComparer.Ordinal)
            .Where(x => x.Count() >= 2)
            .Select(x => new StructuralExpressionFact(x.Key, x.Count()))
            .ToArray();
    }

    private static string? ResolveInsideMods(string modsRoot, string relativeFile)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(
                modsRoot,
                relativeFile.Replace('/', Path.DirectorySeparatorChar)));

        var rootPrefix =
            Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static object Skip(PassCandidate candidate, string reason) => new
    {
        registrationId = candidate.RegistrationId,
        owner = candidate.Owner,
        file = candidate.RelativeFile,
        type = candidate.Kind.ToString().ToUpperInvariant(),
        reason
    };

    private static string LuaQuote(string value) =>
        "\"" +
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) +
        "\"";

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string JsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        return "";
    }

    private static bool JsonBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    private static double JsonDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.TryGetDouble(out var number))
                return number;
        }
        return 0;
    }

    private static long? JsonNullableLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) ||
                value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.TryGetInt64(out var number))
                return number;
        }
        return null;
    }

    private static string[] JsonStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private enum CandidateKind
    {
        Action,
        Frame,
        AuthorCadence,
        DiscoveryDormant,
        Structural,
        HardDormant
    }

    private sealed class PassCandidate
    {
        public CandidateKind Kind { get; init; }
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string SourceSha256 { get; init; } = "";
        public int LineStart { get; init; }
        public int LineEnd { get; init; }
        public string Pattern { get; init; } = "";
        public string[] Actions { get; init; } = Array.Empty<string>();
        public string[] ActionPatterns { get; init; } = Array.Empty<string>();
        public bool RequiresActionType { get; init; }
        public bool RequiresActionValue { get; init; }
        public bool ConsumerMutation { get; init; }
        public bool StateGatePresent { get; init; }
        public bool DynamicGateResolved { get; init; }
        public string DynamicGateExpression { get; init; } = "";
        public bool AuthorCadenceWholeCallback { get; init; }
        public double AuthorBaseIntervalSeconds { get; init; }
        public string AuthorDeltaParameter { get; init; } = "";
        public double EstimatedAvoidablePollingMsPerSecond { get; init; }
        public double EstimatedCallbackPaybackPct { get; init; }
        public double EstimatedGlobalPaybackPct { get; init; }
        public StructuralExpressionFact[] StructuralExpressions { get; init; } = Array.Empty<StructuralExpressionFact>();
        public StructuralExpressionFact[] StructuralConstructors { get; init; } = Array.Empty<StructuralExpressionFact>();
        public bool HardDormantGuardHoist { get; init; }
        public string HardDormantGateExpression { get; init; } = "";
        public int HardDormantPreGuardReadCount { get; init; }
        public bool AuthorDiscoveryDormantSchedule { get; init; }
        public double AuthorDiscoveryIntervalSeconds { get; init; }
        public string AuthorDiscoveryAccumulator { get; init; } = "";
        public string AuthorDiscoveryGate { get; init; } = "";
        public bool AlsoFrameDispatch { get; init; }
    }

    private sealed record StructuralExpressionFact(string Expression, int Count);

    private sealed class CandidateFacts
    {
        public string[] Actions { get; init; } = Array.Empty<string>();
        public string[] ActionPatterns { get; init; } = Array.Empty<string>();
        public bool RequiresActionType { get; init; }
        public bool RequiresActionValue { get; init; }
        public bool ConsumerMutation { get; init; }
        public bool StateGatePresent { get; init; }
        public bool DynamicGateResolved { get; init; }
        public string DynamicGateExpression { get; init; } = "";
        public bool AuthorCadenceWholeCallback { get; init; }
        public double AuthorBaseIntervalSeconds { get; init; }
        public string AuthorDeltaParameter { get; init; } = "";
        public double EstimatedAvoidablePollingMsPerSecond { get; init; }
        public double EstimatedCallbackPaybackPct { get; init; }
        public double EstimatedGlobalPaybackPct { get; init; }
        public StructuralExpressionFact[] StructuralExpressions { get; init; } = Array.Empty<StructuralExpressionFact>();
        public StructuralExpressionFact[] StructuralConstructors { get; init; } = Array.Empty<StructuralExpressionFact>();
        public bool HardDormantGuardHoist { get; init; }
        public string HardDormantGateExpression { get; init; } = "";
        public int HardDormantPreGuardReadCount { get; init; }
        public bool AuthorDiscoveryDormantSchedule { get; init; }
        public double AuthorDiscoveryIntervalSeconds { get; init; }
        public string AuthorDiscoveryAccumulator { get; init; } = "";
        public string AuthorDiscoveryGate { get; init; } = "";
    }

    private sealed record DiscoveryRewriteParts(string Prefix, string Callback);

    private sealed record TransformResult(byte[] Bytes, int AppliedTransforms);
}
