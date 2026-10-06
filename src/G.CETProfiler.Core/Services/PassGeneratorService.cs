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
/// Generic AUTO is deliberately restricted to mechanically source-proven recipes:
/// ACTION_ROUTING_*, ACTION_OVERRIDE_EXACT_PREFILTER, FRAME_DISPATCH_CONSOLIDATION,
/// and explicitly enabled finite shared-provider reads backed by 0-Engine.
/// Structural and dormancy analyzers may still emit evidence, but behavior-changing
/// cadence is authorized only by source-proven semantic per-mod rules.
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

    private static readonly IReadOnlyDictionary<string, SharedProviderRecipe> SharedProviderRecipes =
        SharedProviderCatalog.Authorized
            .Select(x => SharedProviderRecipe.Create(
                x.Provider,
                x.Getter,
                "__gcet" + x.Getter))
            .ToDictionary(x => x.Provider, StringComparer.OrdinalIgnoreCase);

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

        var groups = candidates
            .GroupBy(x => x.RelativeFile, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var liveSourceHashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var appliedTransformsByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fixedInfrastructurePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

            var relative = group.Key.Replace('\\', '/');
            staged[relative] = transformed.Bytes;
            liveSourceHashes[relative] = liveHash;
            appliedTransformsByFile[relative] =
                appliedTransformsByFile.TryGetValue(relative, out var priorCount)
                    ? priorCount + transformed.AppliedTransforms
                    : transformed.AppliedTransforms;
        }

        var semanticTransformCount = SemanticPassGeneratorService.Apply(
            resolver.RootElement,
            modsRoot,
            staged,
            liveSourceHashes,
            appliedTransformsByFile,
            transformManifest,
            skipped);

        if (staged.Count == 0)
            throw new InvalidOperationException(
                "The resolver produced no applicable generic or source-proven semantic transforms. No ZIP was generated.");

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

            var relative = pair.Key.Replace('\\', '/');
            staged[relative] = pair.Value;

            var liveRuntimePath = ResolveInsideMods(modsRoot, relative);
            var liveRuntimeHash =
                liveRuntimePath is not null && File.Exists(liveRuntimePath)
                    ? Sha256(File.ReadAllBytes(liveRuntimePath))
                    : null;

            liveSourceHashes[relative] = liveRuntimeHash;
            appliedTransformsByFile[relative] = 0;
            fixedInfrastructurePaths.Add(relative);
        }

        var fileManifest = staged
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (object)new
            {
                path = pair.Key.Replace('\\', '/'),
                sourceSha256 = liveSourceHashes.TryGetValue(pair.Key, out var sourceHash)
                    ? sourceHash
                    : null,
                generatedSha256 = Sha256(pair.Value),
                appliedTransforms = appliedTransformsByFile.TryGetValue(pair.Key, out var transformCount)
                    ? transformCount
                    : 0,
                fixedInfrastructure = fixedInfrastructurePaths.Contains(pair.Key)
            })
            .ToList();

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
                selection = "MEASURED_GENERIC_PLUS_SOURCE_PROVEN_SEMANTIC_CANDIDATES_FROM_G-CET_Resolver.json",
                modNameRules = false,
                sourceShaRequired = true,
                sourceShaRequiredForGeneric = true,
                semanticRuntimeThresholdMsPerSecond = SemanticPassGeneratorService.MaterialThresholdMsPerSecond,
                semanticCurrentSourceProofRequired = true,
                semanticExistingLiveFilesOnly = true,
                semanticCreatesNewModFiles = false,
                semanticReferenceOverridesShipped = false,
                sharedProviderFamilies = SharedProviderRecipes.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                sharedProviderPolicy = "MEASURED_CALLBACKS_ONLY; EXACT_CURRENT_SOURCE_GETTER; 0-Engine accessor WITH original Game getter fallback",
                fullFileOverlay = true,
                cadenceTransforms = true,
                cadencePolicy = "SEMANTIC_RULES_ONLY_AFTER_CURRENT_SOURCE_GRAPH_PROOF",
                supportedPasses = new[]
                {
                    "ACTION_ROUTING_*",
                    "ACTION_OVERRIDE_EXACT_PREFILTER",
                    "FRAME_DISPATCH_CONSOLIDATION",
                    "SHARED_PROVIDER_READ",
                    "SEMANTIC_RULE_SOURCE_INJECTION"
                },
                fixedRuntimeException = "0-Engine",
                zeroEngineCompatibilityPolicy = "KNOWN_GCET_FIXED_RUNTIME_OR_STRUCTURALLY_PROVEN_HOST_PRESERVING_ADAPTER",
                note = "The overlay generator composes generic AUTO and measured semantic source injections. 0-Engine uses the exact proven G-CET runtime for known states; unknown versions are preserved and receive only a namespaced Engine.GCET adapter when a safe final exported-table structure is proven. Semantic rules are admitted only for >=3 ms/s measured callbacks and must re-prove current live source."
            },
            fixedRuntime = new
            {
                included = true,
                name = "0-Engine",
                exception = true,
                mode = fixedRuntime.LiveState.Contains("STRUCTURAL_COMPAT", StringComparison.OrdinalIgnoreCase)
                    ? "HOST_PRESERVING_ADAPTER"
                    : "KNOWN_FIXED_RUNTIME",
                hostPreserving = fixedRuntime.LiveState.Contains("STRUCTURAL_COMPAT", StringComparison.OrdinalIgnoreCase),
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
                genericTransforms = transformManifest.Count - semanticTransformCount,
                semanticTransforms = semanticTransformCount,
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
                var pattern = JsonString(generic, "Pattern", "pattern");
                CandidateKind? kind = null;

                if (resolverFamily.Equals("ONACTION", StringComparison.OrdinalIgnoreCase) &&
                    pattern.Equals("ACTION_OVERRIDE_EXACT_PREFILTER", StringComparison.OrdinalIgnoreCase))
                {
                    kind = CandidateKind.OverridePrefilter;
                }
                else if (resolverFamily.Equals("ONACTION", StringComparison.OrdinalIgnoreCase) &&
                    pattern.StartsWith("ACTION_ROUTING", StringComparison.OrdinalIgnoreCase) &&
                    !pattern.Equals("ACTION_ROUTING_OVERRIDE", StringComparison.OrdinalIgnoreCase))
                {
                    kind = CandidateKind.Action;
                }
                else if ((resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) ||
                          resolverFamily.Equals("ONDRAW", StringComparison.OrdinalIgnoreCase)) &&
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
                    FrameEvent = resolverFamily.Equals("ONDRAW", StringComparison.OrdinalIgnoreCase)
                        ? "onDraw"
                        : "onUpdate",
                    Actions = facts.Actions,
                    ActionPatterns = facts.ActionPatterns,
                    RequiresActionType = facts.RequiresActionType,
                    RequiresActionValue = facts.RequiresActionValue,
                    ConsumerMutation = facts.ConsumerMutation,
                    StateGatePresent = facts.StateGatePresent,
                    DynamicGateResolved = facts.DynamicGateResolved,
                    DynamicGateExpression = facts.DynamicGateExpression,
                    OverridePrefilterProven = facts.OverridePrefilterProven,
                    OverrideWrappedMethodReturns = facts.OverrideWrappedMethodReturns,
                    OverrideWrappedMethodTakesSelf = facts.OverrideWrappedMethodTakesSelf,
                    OverridePrefilterGateReceiver = facts.OverridePrefilterGateReceiver,
                    OverridePrefilterGateMember = facts.OverridePrefilterGateMember,
                    AlsoFrameDispatch = recipes.Any(x =>
                        x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase))
                });
            }
        }

        if (root.TryGetProperty("sharedProviderOpportunities", out var providers) &&
            providers.ValueKind == JsonValueKind.Array)
        {
            foreach (var provider in providers.EnumerateArray())
            {
                var providerName = JsonString(provider, "provider");
                if (!JsonBool(provider, "generationEnabled") ||
                    !SharedProviderRecipes.ContainsKey(providerName) ||
                    !provider.TryGetProperty("callbacks", out var callbacks) ||
                    callbacks.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var callback in callbacks.EnumerateArray())
                {
                    if (!JsonBool(callback, "substitutionEligible"))
                        continue;

                    var registrationId = JsonNullableLong(callback, "registrationId");
                    var owner = JsonString(callback, "owner");
                    var relativeFile = JsonString(callback, "sourceFile");
                    var sha = JsonString(callback, "sourceSha256");
                    var lineStart = (int)(JsonNullableLong(callback, "lineStart") ?? 0);
                    var lineEnd = (int)(JsonNullableLong(callback, "lineEnd") ?? 0);

                    if (registrationId is null ||
                        string.IsNullOrWhiteSpace(owner) ||
                        string.IsNullOrWhiteSpace(relativeFile) ||
                        string.IsNullOrWhiteSpace(sha) ||
                        lineStart <= 0 ||
                        lineEnd < lineStart)
                        continue;

                    result.Add(new PassCandidate
                    {
                        Kind = CandidateKind.SharedProvider,
                        Provider = providerName,
                        RegistrationId = registrationId.Value,
                        Owner = owner,
                        RelativeFile = relativeFile.Replace('\\', '/'),
                        SourceSha256 = sha,
                        LineStart = lineStart,
                        LineEnd = lineEnd,
                        Pattern = "SHARED_PROVIDER_READ"
                    });
                }
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
            OverridePrefilterProven = JsonBool(facts, "overridePrefilterProven"),
            OverrideWrappedMethodReturns = JsonBool(facts, "overrideWrappedMethodReturns"),
            OverrideWrappedMethodTakesSelf = JsonBool(facts, "overrideWrappedMethodTakesSelf"),
            OverridePrefilterGateReceiver = JsonString(facts, "overridePrefilterGateReceiver"),
            OverridePrefilterGateMember = JsonString(facts, "overridePrefilterGateMember")
        };
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
        var sharedProvidersApplied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Shared-provider substitutions are expression-only and preserve line
        // counts. Apply them first so later structural transforms can keep using
        // the resolver's original callback ranges.
        foreach (var candidate in candidates.Where(x =>
                     x.Kind == CandidateKind.SharedProvider))
        {
            if (!SharedProviderRecipes.TryGetValue(candidate.Provider, out var providerRecipe))
            {
                skipped.Add(Skip(candidate, $"Unsupported shared provider: {candidate.Provider}."));
                continue;
            }

            var helperAlreadyPresent =
                text.Contains(providerRecipe.Marker, StringComparison.Ordinal);
            var helperNameCollision =
                !helperAlreadyPresent &&
                Regex.IsMatch(
                    text,
                    @"\b" + Regex.Escape(providerRecipe.HelperName) + @"\b",
                    RegexOptions.CultureInvariant);

            if (helperNameCollision)
            {
                skipped.Add(Skip(
                    candidate,
                    $"Current file already defines {providerRecipe.HelperName} without the G-CET provider marker."));
                continue;
            }

            var effectiveLineEnd =
                hadTerminalNewline && candidate.LineEnd == lines.Count + 1
                    ? lines.Count
                    : candidate.LineEnd;

            if (candidate.LineStart > lines.Count || effectiveLineEnd > lines.Count)
            {
                skipped.Add(Skip(
                    candidate,
                    "Recorded shared-provider callback range is outside the current file."));
                continue;
            }

            var sourceLines = lines
                .Skip(candidate.LineStart - 1)
                .Take(effectiveLineEnd - candidate.LineStart + 1)
                .ToArray();
            var segment = string.Join("\n", sourceLines);
            var matches = providerRecipe.SourceRegex.Matches(segment).Count;
            if (matches == 0)
            {
                skipped.Add(Skip(
                    candidate,
                    $"Exact Game.{providerRecipe.GameGetter}() provider read could not be revalidated in the recorded source range."));
                continue;
            }

            var replaced = providerRecipe.SourceRegex.Replace(
                segment,
                providerRecipe.HelperName + "()");
            var replacementLines = replaced.Split('\n').ToList();
            if (replacementLines.Count != sourceLines.Length)
            {
                skipped.Add(Skip(
                    candidate,
                    "Shared-provider substitution unexpectedly changed source line count."));
                continue;
            }

            lines.RemoveRange(
                candidate.LineStart - 1,
                effectiveLineEnd - candidate.LineStart + 1);
            lines.InsertRange(candidate.LineStart - 1, replacementLines);

            transformManifest.Add(new
            {
                registrationId = candidate.RegistrationId,
                owner = candidate.Owner,
                type = "SHARED_PROVIDER_READ",
                provider = providerRecipe.Provider,
                providerApi = "0-Engine." + providerRecipe.EngineGetter,
                file = candidate.RelativeFile,
                sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                replacedOccurrences = matches,
                fallback = "Game." + providerRecipe.GameGetter
            });
            applied++;
            sharedProvidersApplied.Add(providerRecipe.Provider);
        }

        foreach (var candidate in candidates.Where(x =>
                     x.Kind != CandidateKind.SharedProvider))
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

            if (candidate.Kind == CandidateKind.Frame)
            {
                if (effectiveLineEnd > lines.Count)
                {
                    skipped.Add(Skip(
                        candidate,
                        "Recorded frame callback range is outside the current file."));
                    continue;
                }

                var frameLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(effectiveLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var frameSegment = string.Join("\n", frameLines);

                // Resolver authorizes a direct onUpdate registration from the
                // complete callback source range. Revalidate the same semantic
                // shape here instead of requiring registerForEvent and
                // "onUpdate" to happen to share one physical source line.
                var frameOpening = Regex.Match(
                    frameSegment,
                    @"\b(?<registrar>registerForEvent|registerRuntimeEvent)\s*\(\s*(?<quote>['""])" +
                    Regex.Escape(candidate.FrameEvent) +
                    @"\k<quote>",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!frameOpening.Success ||
                    !IsBareRegistrarStatement(frameSegment, frameOpening.Groups["registrar"].Index))
                {
                    skipped.Add(Skip(
                        candidate,
                        $"Recorded callback range no longer contains a bare source-proven {candidate.FrameEvent} registrar, or the registrar is wrapped/aliased by an owner abstraction."));
                    continue;
                }

                var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                var registrar = frameOpening.Groups["registrar"].Value;
                var registrarOffset = frameOpening.Groups["registrar"].Index;
                if (string.IsNullOrWhiteSpace(registrar) || registrarOffset < 0)
                {
                    skipped.Add(Skip(
                        candidate,
                        "Direct onUpdate registrar token could not be revalidated."));
                    continue;
                }

                var rewrittenFrame =
                    frameSegment[..registrarOffset] +
                    token +
                    frameSegment[(registrarOffset + registrar.Length)..];
                var frameReplacementLines = rewrittenFrame.Split('\n');

                lines.RemoveRange(
                    candidate.LineStart - 1,
                    effectiveLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, frameReplacementLines);

                frameHelpers.Add((candidate, token));
                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "FRAME_DISPATCH_CONSOLIDATION",
                    eventTarget = candidate.FrameEvent,
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd }
                });
                applied++;
                continue;
            }

            if (candidate.Kind == CandidateKind.OverridePrefilter)
            {
                if (effectiveLineEnd > lines.Count ||
                    !candidate.OverridePrefilterProven ||
                    candidate.Actions.Length == 0 ||
                    candidate.ActionPatterns.Length != 0)
                {
                    skipped.Add(Skip(candidate, "Override prefilter handoff is incomplete or outside the current source range."));
                    continue;
                }

                var overrideLines = lines
                    .Skip(candidate.LineStart - 1)
                    .Take(effectiveLineEnd - candidate.LineStart + 1)
                    .ToArray();
                var overrideSegment = string.Join("\n", overrideLines);
                var overrideOpening = Regex.Match(
                    overrideSegment,
                    @"Override\s*\(\s*(['""])PlayerPuppet\1\s*,\s*(['""])OnAction\2\s*,\s*function\s*\(\s*(?<receiver>[A-Za-z_]\w*)\s*,\s*action\s*,\s*consumer\s*,\s*wrappedMethod\s*\)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
                if (!overrideOpening.Success ||
                    !IsBareRegistrarStatement(overrideSegment, overrideOpening.Index))
                {
                    skipped.Add(Skip(
                        candidate,
                        "Transparent OnAction Override is not a bare global registrar; wrapper/alias semantics are left untouched."));
                    continue;
                }

                var wrappedMatches = Regex.Matches(
                    overrideSegment,
                    @"(?m)^\s*(?:return\s+)?wrappedMethod\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?action\s*,\s*consumer\s*\)\s*;?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (wrappedMatches.Count != 1)
                {
                    skipped.Add(Skip(candidate, "Transparent Override no longer contains exactly one supported wrappedMethod continuation call."));
                    continue;
                }

                var overrideIndent = Regex.Match(overrideLines[0], @"^\s*").Value;
                var bodyIndent = overrideIndent + "    ";
                var tableName = $"__gcetOverrideActions_{candidate.RegistrationId}";
                var actionName = $"__gcetOverrideName_{candidate.RegistrationId}";
                var entries = string.Join(
                    ", ",
                    candidate.Actions
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .Select(x => $"[{LuaQuote(x)}] = true"));
                var prefix = $"{overrideIndent}local {tableName} = {{ {entries} }}";

                var receiver = overrideOpening.Groups["receiver"].Value;
                var wrappedCall = candidate.OverrideWrappedMethodTakesSelf
                    ? $"wrappedMethod({receiver}, action, consumer)"
                    : "wrappedMethod(action, consumer)";
                var earlyWrapped = candidate.OverrideWrappedMethodReturns
                    ? $"return {wrappedCall}"
                    : $"{wrappedCall}\n{bodyIndent}    return";

                var namedName = $"__gcetOverrideNamed_{candidate.RegistrationId}";
                var gateExpression = "";
                var insertionIndex = overrideOpening.Index + overrideOpening.Length;

                if (!string.IsNullOrWhiteSpace(candidate.OverridePrefilterGateMember))
                {
                    if (string.IsNullOrWhiteSpace(candidate.OverridePrefilterGateReceiver))
                    {
                        skipped.Add(Skip(candidate, "Override prefilter trace gate has no proven wrapper receiver."));
                        continue;
                    }

                    var receiverDeclaration = Regex.Match(
                        overrideSegment[(overrideOpening.Index + overrideOpening.Length)..],
                        @"(?m)^\s*local\s+" + Regex.Escape(candidate.OverridePrefilterGateReceiver) +
                        @"\s*=\s*[^\r\n]+$",
                        RegexOptions.CultureInvariant);
                    if (!receiverDeclaration.Success)
                    {
                        skipped.Add(Skip(candidate, "Override prefilter trace-gate receiver declaration could not be revalidated."));
                        continue;
                    }

                    insertionIndex =
                        overrideOpening.Index +
                        overrideOpening.Length +
                        receiverDeclaration.Index +
                        receiverDeclaration.Length;

                    gateExpression =
                        $" and (not {candidate.OverridePrefilterGateReceiver} or " +
                        $"not {candidate.OverridePrefilterGateReceiver}.{candidate.OverridePrefilterGateMember})";
                }

                var injected =
                    $"\n{bodyIndent}local {namedName}, {actionName} = pcall(function() return Game.NameToString(action:GetName()) end)" +
                    $"\n{bodyIndent}if {namedName}{gateExpression} and not {tableName}[{actionName}] then" +
                    $"\n{bodyIndent}    {earlyWrapped}" +
                    $"\n{bodyIndent}end -- G-CET finite Override prefilter";

                var rewritten =
                    overrideSegment[..insertionIndex] +
                    injected +
                    overrideSegment[insertionIndex..];

                var overrideReplacementLines = (prefix + "\n" + rewritten).Split('\n');
                lines.RemoveRange(
                    candidate.LineStart - 1,
                    effectiveLineEnd - candidate.LineStart + 1);
                lines.InsertRange(candidate.LineStart - 1, overrideReplacementLines);

                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "ACTION_OVERRIDE_EXACT_PREFILTER",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                    facts = new
                    {
                        actions = candidate.Actions,
                        preservesOverride = true,
                        preservesWrappedMethod = true,
                        protectedNameDecode = true,
                        prefilterGateReceiver = candidate.OverridePrefilterGateReceiver,
                        prefilterGateMember = candidate.OverridePrefilterGateMember,
                        semantics = "irrelevant actions bypass only source-proven finite custom downstream work; optional diagnostic full-stream gates remain authoritative"
                    }
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
            if (!opening.Success ||
                !IsBareRegistrarStatement(segment, opening.Index))
            {
                skipped.Add(Skip(
                    candidate,
                    "The analyzed OnAction registration is not a bare global Observe statement; wrapper/alias lifecycle semantics are left untouched."));
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
            var routedCallbackArgs = string.IsNullOrWhiteSpace(callbackArgs)
                ? "__gcetRoutedName"
                : callbackArgs.TrimEnd() + ", __gcetRoutedName";
            var replaced =
                segment[..opening.Index] +
                $"local function {functionName}({routedCallbackArgs})" +
                segment[(opening.Index + opening.Length)..];

            var routedNameReused = TryRewriteActionNameDecode(
                ref replaced,
                "__gcetRoutedName");

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
                    candidate.RequiresActionValue,
                    routedNameReused
                }
            });
            applied++;
        }

        var sharedHeaders = new List<string>();
        foreach (var providerName in sharedProvidersApplied
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var providerRecipe = SharedProviderRecipes[providerName];
            if (text.Contains(providerRecipe.Marker, StringComparison.Ordinal))
                continue;

            sharedHeaders.AddRange(BuildSharedProviderHeader(providerRecipe));
        }

        if (sharedHeaders.Count > 0)
            lines.InsertRange(0, sharedHeaders);

        if (frameHelpers.Count > 0)
        {
            var header = new List<string>
            {
                "-- G-CET generated frame registrar: source-selected from resolver evidence."
            };

            foreach (var item in frameHelpers.OrderBy(x => x.Candidate.RegistrationId))
            {
                if (HasExistingFrameHelper(text, item.Token, item.Candidate.Owner))
                    continue;

                var owner = LuaQuote(item.Candidate.Owner);
                header.Add($"local {item.Token} = registerForEvent");
                header.Add("do");
                header.Add("    local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")");
                header.Add("    local __gcetApi = __gcetEngine");
                header.Add("    if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.GCET) == \"table\" then __gcetApi = __gcetEngine.GCET end");
                header.Add("    if __gcetOk and type(__gcetApi) == \"table\" and type(__gcetApi.MakeEventRegistrar) == \"function\" then");
                header.Add($"        {item.Token} = __gcetApi.MakeEventRegistrar({owner}, registerForEvent)");
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

    private static bool IsBareRegistrarStatement(
        string source,
        int tokenIndex)
    {
        if (tokenIndex < 0 || tokenIndex > source.Length)
            return false;

        var lineStart = tokenIndex == 0
            ? 0
            : source.LastIndexOf('\n', Math.Max(0, tokenIndex - 1)) + 1;

        for (var i = lineStart; i < tokenIndex; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
                return false;
        }

        return true;
    }

    private static bool HasExistingFrameHelper(
        string text,
        string token,
        string owner)
    {
        var escapedToken = Regex.Escape(token);
        var hasFallback = Regex.IsMatch(
            text,
            @"\blocal\s+" + escapedToken + @"\s*=\s*registerForEvent\b",
            RegexOptions.CultureInvariant);
        if (!hasFallback)
            return false;

        var escapedOwner = Regex.Escape(LuaQuote(owner));
        return Regex.IsMatch(
            text,
            @"\b" + escapedToken +
            @"\s*=\s*__gcet(?:Engine|Api)\.MakeEventRegistrar\s*\(\s*" +
            escapedOwner +
            @"\s*,\s*registerForEvent\s*\)",
            RegexOptions.CultureInvariant);
    }

    private static bool TryRewriteActionNameDecode(
        ref string source,
        string routedNameVariable)
    {
        // The router has already decoded the action name before dispatch. Reuse
        // that value only for a simple local assignment whose RHS is a pure
        // action-name decode. When the callback runs through the original
        // Observe fallback the routed value is nil and the original expression
        // executes unchanged.
        var match = Regex.Match(
            source,
            @"(?m)^(?<indent>\s*)local\s+(?<var>[A-Za-z_]\w*)\s*=\s*(?<expr>[^\r\n;]*\b(?:GetName|NameToString)\b[^\r\n;]*)\s*;?\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var expression = match.Groups["expr"].Value.Trim();
        if (!Regex.IsMatch(
                expression,
                @"^(?:Game\.NameToString\s*\(\s*)?action\s*[:.]\s*GetName\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        if (Regex.IsMatch(
                expression,
                @"\b(?:Set|Consume|Write|Update|Send|Trigger|Call)\w*\s*\(",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        var replacement =
            match.Groups["indent"].Value +
            "local " +
            match.Groups["var"].Value +
            " = (" +
            routedNameVariable +
            " or (" +
            expression +
            "))";

        source =
            source[..match.Index] +
            replacement +
            source[(match.Index + match.Length)..];
        return true;
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
        lines.Add($"{indent}local __gcetApi_{candidate.RegistrationId} = __gcetEngine_{candidate.RegistrationId}");
        lines.Add($"{indent}if __gcetOk_{candidate.RegistrationId} and type(__gcetEngine_{candidate.RegistrationId}) == \"table\" and type(__gcetEngine_{candidate.RegistrationId}.GCET) == \"table\" then __gcetApi_{candidate.RegistrationId} = __gcetEngine_{candidate.RegistrationId}.GCET end");
        lines.Add($"{indent}if __gcetOk_{candidate.RegistrationId} and type(__gcetApi_{candidate.RegistrationId}) == \"table\" and type(__gcetApi_{candidate.RegistrationId}.SubscribeAction) == \"function\" then");
        lines.Add($"{indent}    __gcetRouted_{candidate.RegistrationId} = pcall(function()");

        if (candidate.DynamicGateResolved)
        {
            lines.Add($"{indent}        local __gcetExact_{candidate.RegistrationId} = {{}}");
            foreach (var action in candidate.Actions)
                lines.Add($"{indent}        __gcetExact_{candidate.RegistrationId}[{LuaQuote(action)}] = true");

            lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetApi_{candidate.RegistrationId}.SubscribeAction({{");
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
            lines.Add($"{indent}                {functionName}(this, action, consumer, routedName)");
            lines.Add($"{indent}            end");
            lines.Add($"{indent}        end, {owner})");
        }
        else
        {
            if (candidate.Actions.Length > 0)
            {
                var actions = string.Join(", ", candidate.Actions.Select(LuaQuote));
                lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetApi_{candidate.RegistrationId}.SubscribeAction({{");
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

                lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetApi_{candidate.RegistrationId}.SubscribeAction({{");
                lines.Add($"{indent}            id = {LuaQuote(idBase + ".Pattern")},");
                lines.Add($"{indent}            actions = \"*\",");
                lines.Add($"{indent}            decodeType = false");
                lines.Add($"{indent}        }}, function(this, action, consumer, routedName)");

                var conditions = string.Join(
                    " or ",
                    candidate.ActionPatterns.Select(x =>
                        $"string.find(routedName, {LuaQuote(x)}, 1, true)"));

                lines.Add($"{indent}            if routedName and not __gcetExact_{candidate.RegistrationId}[routedName] and ({conditions}) then");
                lines.Add($"{indent}                {functionName}(this, action, consumer, routedName)");
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

    private static IEnumerable<string> BuildSharedProviderHeader(
        SharedProviderRecipe recipe)
    {
        if (recipe.Provider.Equals("PLAYER", StringComparison.OrdinalIgnoreCase))
        {
            return new[]
            {
                recipe.Marker,
                "local __gcetSharedPlayerProvider = nil",
                "local function __gcetGetPlayer()",
                "    if __gcetSharedPlayerProvider == nil then",
                "        local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")",
                "        if __gcetOk and type(__gcetEngine) == \"table\" then",
                "            local __gcetApi = type(__gcetEngine.GCET) == \"table\" and __gcetEngine.GCET or __gcetEngine",
                "            if type(__gcetApi.GetPlayer) == \"function\" then __gcetSharedPlayerProvider = __gcetApi.GetPlayer end",
                "        end",
                "    end",
                "    if __gcetSharedPlayerProvider ~= nil then",
                "        local __gcetPlayer = __gcetSharedPlayerProvider()",
                "        if __gcetPlayer ~= nil then return __gcetPlayer end",
                "    end",
                "    return Game.GetPlayer()",
                "end",
                ""
            };
        }

        var providerVariable = "__gcetProvider_" + recipe.EngineGetter;
        return new[]
        {
            recipe.Marker,
            $"local {providerVariable} = nil",
            $"local function {recipe.HelperName}()",
            $"    if {providerVariable} == nil then",
            "        local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")",
            "        if __gcetOk and type(__gcetEngine) == \"table\" then",
            "            local __gcetApi = type(__gcetEngine.GCET) == \"table\" and __gcetEngine.GCET or __gcetEngine",
            $"            if type(__gcetApi.{recipe.EngineGetter}) == \"function\" then {providerVariable} = __gcetApi.{recipe.EngineGetter} end",
            "        end",
            "    end",
            $"    if {providerVariable} ~= nil then",
            $"        local __gcetValue = {providerVariable}()",
            "        if __gcetValue ~= nil then return __gcetValue end",
            "    end",
            $"    return Game.{recipe.GameGetter}()",
            "end",
            ""
        };
    }

    private sealed class SharedProviderRecipe
    {
        private SharedProviderRecipe(
            string provider,
            string gameGetter,
            string helperName)
        {
            Provider = provider;
            GameGetter = gameGetter;
            EngineGetter = gameGetter;
            HelperName = helperName;
            Marker = "-- G-CET shared provider: " + provider;
            SourceRegex = new Regex(
                @"\bGame\s*\.\s*" + Regex.Escape(gameGetter) + @"\s*\(\s*\)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant);
        }

        public string Provider { get; }
        public string GameGetter { get; }
        public string EngineGetter { get; }
        public string HelperName { get; }
        public string Marker { get; }
        public Regex SourceRegex { get; }

        public static SharedProviderRecipe Create(
            string provider,
            string gameGetter,
            string helperName) =>
            new(provider, gameGetter, helperName);
    }

    private enum CandidateKind
    {
        Action,
        OverridePrefilter,
        Frame,
        SharedProvider
    }

    private sealed class PassCandidate
    {
        public CandidateKind Kind { get; init; }
        public string Provider { get; init; } = "";
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string SourceSha256 { get; init; } = "";
        public int LineStart { get; init; }
        public int LineEnd { get; init; }
        public string Pattern { get; init; } = "";
        public string FrameEvent { get; init; } = "onUpdate";
        public string[] Actions { get; init; } = Array.Empty<string>();
        public string[] ActionPatterns { get; init; } = Array.Empty<string>();
        public bool RequiresActionType { get; init; }
        public bool RequiresActionValue { get; init; }
        public bool ConsumerMutation { get; init; }
        public bool StateGatePresent { get; init; }
        public bool DynamicGateResolved { get; init; }
        public string DynamicGateExpression { get; init; } = "";
        public bool OverridePrefilterProven { get; init; }
        public bool OverrideWrappedMethodReturns { get; init; }
        public bool OverrideWrappedMethodTakesSelf { get; init; }
        public string OverridePrefilterGateReceiver { get; init; } = "";
        public string OverridePrefilterGateMember { get; init; } = "";
        public bool AlsoFrameDispatch { get; init; }
    }


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
        public bool OverridePrefilterProven { get; init; }
        public bool OverrideWrappedMethodReturns { get; init; }
        public bool OverrideWrappedMethodTakesSelf { get; init; }
        public string OverridePrefilterGateReceiver { get; init; } = "";
        public string OverridePrefilterGateMember { get; init; } = "";
    }


    private sealed record TransformResult(byte[] Bytes, int AppliedTransforms);
}
