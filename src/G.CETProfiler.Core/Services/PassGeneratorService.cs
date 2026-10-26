using System.IO.Compression;
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
/// INTERACTION_UI_IDLE_GUARD, UI_VISIBILITY_DORMANCY, HARD_DORMANT_GUARD_HOIST,
/// SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL,
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
        string? outputZipPath = null,
        bool hotscenesEnglishOnly = false)
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
        // Use exactly the framework capability projection seen by ANALYZE.
        // Recompose it from the current author sources on every GENERATE so
        // updates cannot silently invalidate a stale analysis snapshot.
        var effectiveRuntime = EffectiveRuntimeView.Build(modsRoot);
        var fixedRuntime = effectiveRuntime.RequireFramework();

        var candidates = ReadCandidates(resolver.RootElement)
            .Where(c => !ModOptimizationBlacklist.IsBlacklistedOwner(c.Owner) &&
                !ModOptimizationBlacklist.IsBlacklistedPath(c.RelativeFile))
            .ToList();

        var groups = candidates
            .GroupBy(x => x.RelativeFile, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var appliedTransformsByFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var fixedInfrastructurePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var transformManifest = new List<object>();
        var skipped = new List<object>();
        var blacklistedMods = ModOptimizationBlacklist.FindInstalledOwners(modsRoot);
        foreach (var owner in blacklistedMods)
            skipped.Add(new {type = "MOD_BLACKLIST", category = "MOD_BLACKLIST",
                owner, reason = ModOptimizationBlacklist.Warning});

        foreach (var group in groups)
        {
        // Match current installed Lua code, not a captured fingerprint.
            // each rewrite against the current installed Lua and its recorded
            // callback bounds. Mod updates must not veto unrelated safe rules.
            var livePath = ResolveInsideMods(modsRoot, group.Key);
            if (livePath is null || !File.Exists(livePath))
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(candidate, "Current deployed source file was not found."));
                continue;
            }

            var originalBytes = File.ReadAllBytes(livePath);
            var transformed = TransformFile(
                originalBytes,
                group.OrderByDescending(x => x.LineStart).ToList(),
                modsRoot,
                transformManifest,
                skipped);

            if (transformed.AppliedTransforms == 0)
                continue;

            var relative = group.Key.Replace('\\', '/');
            staged[relative] = transformed.Bytes;
            appliedTransformsByFile[relative] =
                appliedTransformsByFile.TryGetValue(relative, out var priorCount)
                    ? priorCount + transformed.AppliedTransforms
                    : transformed.AppliedTransforms;
        }

        var genericInfrastructureTransformCount = 0;
        // Existing generic source proof: pair the optional empty-Cron timer
        // fast path and hidden interaction UI guard with the required Cron
        // module API. Compose from untouched mod files, without AudioXL or
        // an earlier optimized init.lua prerequisite.
        genericInfrastructureTransformCount +=
            GenericInteractionUiTransform.TryStageCronAndUiIdle(
                modsRoot,staged,appliedTransformsByFile,transformManifest);

        // Restore source-proven Phase1+2 hook compatibility directly from
        // untouched mod files, before native integration and semantics. These
        // author callbacks were previously preserved with a valid hook /
        // disabled nonexistent GameUI event; never require old G-CET output.
        genericInfrastructureTransformCount += StageUnsupportedGameHookCompatibility(
            modsRoot,staged,appliedTransformsByFile,transformManifest);


        // GENERIC PHASE 2: finish source infrastructure and shared-read
        // rewrites before any semantic source injection.
        // Explicitly reviewed 0-Engine Stage A: the 12 immutable PSM
        // definition keys, not the blackboard handle/getters/callback cadence.
        // Requires all 12 allocation-producing callsites in the active capture,
        // exact source hash and author lifecycle anchors; unknown source fails
        // closed. This is independent of the 62 per-mod semantic catalog.
        if (BlackboardPsmDefinitionCacheSource.TryStage(
                captureRoot, modsRoot, staged,
                appliedTransformsByFile, transformManifest, skipped))
            ++genericInfrastructureTransformCount;

        // Reviewed 0-Engine same-update allocation batch. Independent exact
        // source/evidence gates for the spatial and Lifecycle modules; no
        // dynamic entity snapshots or cross-mod sharing.
        genericInfrastructureTransformCount += ZeroEngineAllocationBatchSource.TryStage(
            captureRoot, modsRoot, staged,
            appliedTransformsByFile, transformManifest, skipped);

        // Direct, source-proven Game.GetPlayer():GetWorldPosition() only:
        // typed native forced-live getter plus original fail-open fallback.
        // Runs BEFORE generic GetPlayer shared-read replacement, otherwise
        // the direct chain would be hidden inside __gcetGetPlayer().
        var nativePlayerVectorCount=NativePlayerVectorStage.TryStage(
            modsRoot,staged,appliedTransformsByFile,
            transformManifest,skipped);

        // Recover Lua local capacity before shared-read adoption. Move only
        // exact G-CET-generated provider helpers into a source-owned Lua
        // worker; original author locals, callbacks, and 0-Engine fallback
        // behavior remain unchanged.
        // Only explicitly requested personal source cuts remove localization.
        // The generator reads no configuration file from CET's mods root.
        var englishOnlyHotscenes = HotscenesEnglishOnlySource.TryStage(
            hotscenesEnglishOnly, modsRoot, staged, appliedTransformsByFile,
            transformManifest, skipped);

        // Fix historical G-CET provider-template interpolation in already
        // optimized Lua BEFORE any current-source generic/semantic rewrite.
        // Each repaired helper is checked against the canonical 0-Engine
        // method lookup and original Game getter fallback.
        var repairedProviderFiles = SharedProviderTemplateRepairSource.TryStage(
            modsRoot, staged, appliedTransformsByFile, transformManifest, skipped,
            provider => SharedProviderRecipes.TryGetValue(provider, out var recipe)
                ? BuildSharedProviderHeader(recipe) : Array.Empty<string>());

        var genericReadWorkers = GenericReadCoordinatorSource.TryStage(
            modsRoot, staged, appliedTransformsByFile, transformManifest, skipped,
            provider => SharedProviderRecipes.TryGetValue(provider, out var recipe)
                ? BuildSharedProviderHeader(recipe) : Array.Empty<string>());

        // Final generic rewrite before source-structural semantic stages.
        // Semantic injectors must adapt to the resulting current source.
        // Authorize using current staged Lua source shape.
        var nativeReadReuseCount = NativeReadReuseSource.TryStage(
            resolver.RootElement, modsRoot, staged,
            appliedTransformsByFile, transformManifest, skipped,
            provider => SharedProviderRecipes.TryGetValue(provider, out var recipe)
                ? BuildSharedProviderHeader(recipe) : Array.Empty<string>());

        // Repair pre-existing generated method lookups as well: previously
        // optimized Lua must not fall back just because Engine.GCET contains
        // native-location services but not the requested top-level API.
        var methodLookupRepairs=ZeroEngineMethodResolutionSource.TryStage(
            modsRoot,staged,appliedTransformsByFile,transformManifest,skipped);

        // NATIVE STATIC-LOCATION INVENTORY: inspect final generic source and
        // immutable author/REDscript coordinate definitions BEFORE handpicked
        // semantic source processing. Location entries are passive data, not
        // subscriptions. Semantic injectors may then attach to these owners;
        // no post-semantic native database rewrite is necessary.
        var availableLocations=CompiledNativeLocations.Compile(modsRoot,staged);

        // SEMANTIC PHASE: current-source behavior, scheduling and dormancy,
        // including OPTIONAL Lua subscriptions to already-inventoried
        // static native owners. No generic or native-geometry pass follows.
        var semanticTransformCount = genericInfrastructureTransformCount +
            SemanticPassGeneratorService.Apply(
                resolver.RootElement,
                modsRoot,
                staged,
                appliedTransformsByFile,
                transformManifest,
                skipped);

        // Fail closed if a future stage bypasses the central owner policy.
        var forbiddenOutput = staged.Keys.FirstOrDefault(ModOptimizationBlacklist.IsBlacklistedPath);
        if (forbiddenOutput is not null)
            throw new InvalidOperationException(
                "Marmur Bank blacklist violation: generated output targets " + forbiddenOutput);

        if (staged.Count == 0 && availableLocations.OwnerCounts.Count == 0)
        {
            // An already-admitted authored rule may still fail its injector's
            // independent source or lifecycle proof. Do not hide that failure
            // under the same "no applicable rules" message used when nothing
            // was eligible. Bound diagnostics to three semantic reasons.
            var semanticFailures = skipped
                .Select(item => JsonSerializer.SerializeToElement(item))
                .Where(row => row.TryGetProperty("type", out var kind) &&
                    kind.ValueKind == JsonValueKind.String &&
                    kind.GetString() == "SEMANTIC_RULE")
                .Take(3)
                .Select(row =>
                {
                    var owner = row.TryGetProperty("Owner", out var name)
                        ? name.GetString() : "unknown";
                    var rule = row.TryGetProperty("RuleId", out var id)
                        ? id.GetString() : "unknown";
                    var reason = row.TryGetProperty("reason", out var why)
                        ? why.GetString() : "source proof failed";
                    return $"{owner}/{rule}: {reason}";
                })
                .ToArray();
            var details = semanticFailures.Length > 0
                ? " Source-proven semantic candidates were refused: " +
                  string.Join("; ", semanticFailures)
                : "";
            throw new InvalidOperationException(
                "The resolver produced no applicable generic or source-proven semantic transforms. " +
                "No ZIP was generated." + details +
                (blacklistedMods.Length > 0 ? " " + ModOptimizationBlacklist.Warning : ""));
        }

        var callbackFileCount = staged.Count;

        // 0-Engine is the one deliberate fixed-runtime exception. The target
        // callbacks above are still selected strictly from capture/resolver
        // evidence; the shared runtime needed to execute those generic recipes
        // is shipped with every generated CET pass.
        foreach (var pair in fixedRuntime.Files)
        {
            if (staged.ContainsKey(pair.Key))
                throw new InvalidOperationException(
                    $"Resolver output unexpectedly targets fixed runtime file: {pair.Key}");

            var relative = pair.Key.Replace('\\', '/');
            staged[relative] = pair.Value;

            appliedTransformsByFile[relative] = 0;
            fixedInfrastructurePaths.Add(relative);
        }

        // Package the ALREADY-COMPILED native inventory. A semantic Lua
        // subscriber is optional, but cannot silently add or remove geometry.
        // The immutable native DLL is taken from CI, never the player's game.
        var requiresNative = availableLocations.OwnerCounts.Count>0 ||
            nativePlayerVectorCount > 0 || staged.Any(pair =>
            !pair.Key.StartsWith("0-Engine/", StringComparison.OrdinalIgnoreCase) &&
            (Encoding.UTF8.GetString(pair.Value).Contains(
                "G-CET native location:", StringComparison.Ordinal) ||
             Encoding.UTF8.GetString(pair.Value).Contains(
                 NativePlayerVectorStage.Marker, StringComparison.Ordinal)));
        var nativeRuntime = effectiveRuntime.NativeForGeneration(requiresNative);
        // Explicit empty GCAS prevents ghost anchors from a stale overlay.
        var nativeLocations = nativeRuntime is null ? null : availableLocations;

        // Whole-overlay Lua budget validation, AFTER semantic/native hooks
        // and fixed 0-Engine assembly. Individual stages refuse unsafe edits;
        // this last backstop prevents packaging any invalid combination.
        foreach (var pair in staged.Where(p =>
                     p.Key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)))
        {
            var budget = LuaLocalBudgetAnalyzer.Analyze(pair.Value);
            if (budget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit)
                throw new InvalidOperationException(
                    $"Final Lua validation refused {pair.Key}: " +
                    $"{budget.MaxActiveLocals} active locals at " +
                    $"{budget.Scope}:{budget.Line} exceed Lua's " +
                    $"{LuaLocalBudgetAnalyzer.Limit}-local hard limit. " +
                    "No optimization ZIP was produced.");
        }

        var fileManifest = staged
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (object)new
            {
                path = pair.Key.Replace('\\', '/'),
                appliedTransforms = appliedTransformsByFile.TryGetValue(pair.Key, out var transformCount)
                    ? transformCount
                    : 0,
                fixedInfrastructure = fixedInfrastructurePaths.Contains(pair.Key)
            })
            .ToList();

        if (nativeRuntime is not null)
        {
            fileManifest.Add(new
            {
                path = NativeRuntimeBundle.GameRootRelativePath,
                appliedTransforms = 0,
                fixedInfrastructure = true
            });
        }

        if (nativeLocations is not null)
        {
            fileManifest.Add(new
            {
                path = CompiledNativeLocations.GameRootRelativePath,
                appliedTransforms = 0,
                fixedInfrastructure = true
            });
        }

        var captureName = new DirectoryInfo(captureRoot).Name;
        var generatedUtc = DateTime.UtcNow;
        outputZipPath ??= Path.Combine(
            captureRoot,
            $"G-CET_PASS_{generatedUtc:yyyyMMdd-HHmmss}.zip");
        outputZipPath = Path.GetFullPath(outputZipPath);

        var skipOutcomes = PassSkipSummaryService.FromSkipped(skipped);
        // A coordinator is a newly emitted mod Lua file, not a replacement
        // for an author file. The old constant "false" contradicted the
        // actual 15-worker output and hid generic read workers as well.
        var coordinatorOutputFiles = staged.Keys
            .Where(key=>key.Contains("/GCET_Semantic/",StringComparison.OrdinalIgnoreCase)
                 && key.EndsWith(".lua",StringComparison.OrdinalIgnoreCase))
            .OrderBy(key=>key,StringComparer.OrdinalIgnoreCase).ToArray();
        var newCoordinatorOutputFiles = coordinatorOutputFiles
            .Where(key=>!File.Exists(Path.Combine(modsRoot,
                key.Replace('/',Path.DirectorySeparatorChar)))).ToArray();
        var semanticCoordinatorOutputFiles = transformManifest
            .Select(item=>JsonSerializer.SerializeToElement(item))
            .Where(item=>item.TryGetProperty("type",out var type) &&
                type.GetString()=="SEMANTIC_RULE")
            .SelectMany(item=>item.TryGetProperty("files",out var files) &&
                files.ValueKind==JsonValueKind.Array
                    ? files.EnumerateArray().Select(file=>file.GetString() ?? "").ToArray()
                    : Array.Empty<string>())
            .Where(file=>file.Contains("/GCET_Semantic/",StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var newSemanticCoordinatorOutputCount=semanticCoordinatorOutputFiles.Count(file=>
            !File.Exists(Path.Combine(modsRoot,file.Replace('/',
                Path.DirectorySeparatorChar))));

        var manifest = new
        {
            schemaVersion = "0.1",
            generatedUtc = generatedUtc.ToString("O"),
            capture = captureName,
            policy = new
            {
                selection = "MEASURED_GENERIC_PLUS_SOURCE_PROVEN_SEMANTIC_CANDIDATES_FROM_G-CET_Resolver.json",
                nativeReadReuseContract = NativeReadReuseSource.Contract,
                nativeReadReuseAppliedOccurrences = nativeReadReuseCount,
                sourceOwnedGenericReadCoordinatorStages = genericReadWorkers,
                personalEnglishOnlyHotscenesStages = englishOnlyHotscenes,
                sourceOwnedMalformedProviderFilesRepaired = repairedProviderFiles,
                personalEnglishOnlyHotscenesRequested = hotscenesEnglishOnly,
                legacyMethodLookupRepairOccurrences = methodLookupRepairs,
                nativeReadReuseBypassesCallbackCpuGate = true,
                nativeReadReuseBackend = "0-Engine:existing-shared-provider",
                nativePlayerVectorRewrite = nativePlayerVectorCount > 0,
                nativePlayerVectorGuardedLiveAppliedOccurrences = nativePlayerVectorCount,
                nativePlayerVectorFrameReuseAuthorized = false,
                nativePlayerVectorContract = NativePlayerVectorStage.Contract,
                nativeRuntimeIncluded = nativeRuntime is not null,
                nativeRuntimeManifestOptionalForDynamicOwners = false,
                compiledSourceOwnedNativeLocations = nativeLocations is not null,
                compiledNativeLocationOwnerCounts = nativeLocations?.OwnerCounts,
                modNameRules = false,
                incompatibleModBlacklist = "MARMUR_BANK_ALL_RELEASES",
                blacklistRule = ModOptimizationBlacklist.Warning,
                blacklistedInstalledMods = blacklistedMods,
                semanticDefaultRuntimeThresholdMsPerSecond = SemanticPassGeneratorService.MaterialThresholdMsPerSecond,
                semanticRuleLocalRuntimeThresholds = true,
                semanticThreeMsPerSecondIsSoftBoundary = true,
                // Retained legacy policy field for consuming tools: source
                // proof is mandatory but captured activity no longer is.
                semanticHandAuthoredAdmissionRequiresMeasuredActivityAndCurrentSourceProof = false,
                semanticHandAuthoredCurrentSourceProofRequired = true,
                semanticSourceIndependentAdmissionExplicitCatalogOptInOnly = true,
                semanticCurrentSourceProofRequired = true,
                semanticExistingLiveFilesOnly = true,
                semanticCreatesNewModFiles = newSemanticCoordinatorOutputCount > 0,
                semanticMayCreateSourceOwnedLuaCoordinators = true,
                semanticCoordinatorFilesEmitted = semanticCoordinatorOutputFiles.Length,
                sourceOwnedLuaCoordinatorFilesEmitted = coordinatorOutputFiles.Length,
                sourceOwnedLuaCoordinatorFilesNew = newCoordinatorOutputFiles.Length,
                semanticReferenceOverridesShipped = false,
                luaLocalVariableLimit = LuaLocalBudgetAnalyzer.Limit,
                luaLocalBudgetPolicy = "ALLOW_UP_TO_200_ACTIVE_LOCALS_PER_FUNCTION; REFUSE_G-CET_FILE_TRANSFORM_IF_GENERATED_SOURCE_EXCEEDS_LIMIT; NEVER_DELETE_AUTHOR_CODE_TO_MAKE_ROOM",
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
                    "INTERACTION_UI_IDLE_GUARD",
                    "CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD",
                    "UI_VISIBILITY_DORMANCY",
                    "HARD_DORMANT_GUARD_HOIST",
                    "SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL",
                    "SHARED_PROVIDER_READ",
                    "NATIVE_READ_REUSE",
                    "NATIVE_PLAYER_VECTOR_LIVE",
                    "SEMANTIC_RULE_SOURCE_INJECTION",
                    "0_ENGINE_PSM_DEFINITIONS_CACHE_STAGE_A"
                },
                fixedRuntimeException = "0-Engine",
                zeroEngineCompatibilityPolicy = "KNOWN_GCET_FIXED_RUNTIME_OR_STRUCTURALLY_PROVEN_HOST_PRESERVING_ADAPTER",
                note = "The overlay generator composes generic AUTO and measured semantic source injections. 0-Engine uses the exact proven G-CET runtime for known states; unknown versions are preserved and receive only a namespaced Engine.GCET adapter when a safe final exported-table structure is proven. The 3 ms/s boundary prioritizes CPU materiality but never vetoes validated, production-enabled hand-authored semantic stages with current-source proof, even for registered callbacks with zero observed calls in this capture. Already-complete source stages are not re-applied; generic native-dispatch and inferred dormancy retain independent proofs."
            },
            effectiveRuntime = effectiveRuntime.Describe(),
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
                files = fixedRuntime.Files.Keys
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            },
            nativeRuntime = new
            {
                included = nativeRuntime is not null,
                gameRootPath = nativeRuntime is null ? null : NativeRuntimeBundle.GameRootRelativePath,
                bootstrap = "COMPILED_GCAS_V1_FROM_INSTALLED_AUTHOR_SOURCES; UNKNOWN_OWNER_DYNAMIC_ORIGINAL_FALLBACK",
                locationFilePath = nativeLocations is null ? null : CompiledNativeLocations.GameRootRelativePath,
                compiledOwnerCounts = nativeLocations?.OwnerCounts,
                requiredByGeneratedSource = requiresNative
            },
            summary = new
            {
                files = staged.Count + (nativeRuntime is null ? 0 : 2),
                callbackFiles = callbackFileCount,
                fixedRuntimeFiles = fixedRuntime.Files.Count,
                nativeRuntimeFiles = nativeRuntime is null ? 0 : 2,
                genericTransforms = transformManifest.Count - semanticTransformCount,
                semanticTransforms = semanticTransformCount,
                transforms = transformManifest.Count,
                skipped = skipped.Count,
                alreadyImplemented = skipOutcomes.AlreadyImplementedCount,
                luaLocalLimitDeferred = skipOutcomes.LuaLocalLimitCount,
                generationFailures = skipOutcomes.GenerationFailureCount,
                blacklistedMods = skipOutcomes.BlacklistedCount
            },
            files = fileManifest,
            transforms = transformManifest,
            optimizationClassCoverage = OptimizationClassCountService.FromManifest(
                JsonSerializer.SerializeToElement(new
                {
                    transforms = transformManifest,
                    files = fileManifest
                })),
            skipOutcomes = new
            {
                alreadyImplemented = skipOutcomes.AlreadyImplementedCount,
                luaLocalLimitDeferred = skipOutcomes.LuaLocalLimitCount,
                generationFailures = skipOutcomes.GenerationFailureCount,
                blacklistedMods = skipOutcomes.BlacklistedCount,
                totalNotApplied = skipOutcomes.TotalNotApplied,
                entries = skipOutcomes.Entries.Select(row => new
                {
                    category = row.Category,
                    type = row.Type,
                    owner = row.Owner,
                    ruleId = row.RuleId,
                    file = row.File,
                    reason = row.Reason
                }).ToArray()
            },
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
            if (nativeRuntime is not null)
            {
                var entry = archive.CreateEntry(
                    NativeRuntimeBundle.GameRootRelativePath, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(nativeRuntime.Bytes, 0, nativeRuntime.Bytes.Length);
            }
            if (nativeLocations is not null)
            {
                var entry = archive.CreateEntry(
                    CompiledNativeLocations.GameRootRelativePath, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(nativeLocations.Bytes, 0, nativeLocations.Bytes.Length);
            }
        }

        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJson) + Environment.NewLine,
            new UTF8Encoding(false));

        return new PassBuildResult(
            outputZipPath,
            manifestPath,
            staged.Count + (nativeRuntime is null ? 0 : 2),
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
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase) ||
                             x.Equals("SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL", StringComparison.OrdinalIgnoreCase) ||
                             x.Equals("HARD_DORMANT_GUARD_HOIST", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Frame;
                }
                else if (resolverFamily.Equals("ONDRAW", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase) ||
                             x.Equals("INTERACTION_UI_IDLE_GUARD", StringComparison.OrdinalIgnoreCase) ||
                             x.Equals("CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD", StringComparison.OrdinalIgnoreCase) ||
                             x.Equals("UI_VISIBILITY_DORMANCY", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Frame;
                }

                if (kind is null)
                    continue;

                var registrationId = JsonNullableLong(consumer, "registrationId");
                var owner = JsonString(consumer, "owner");
                var relativeFile = JsonString(source, "RelativeFile", "relativeFile");
                var lineStart = (int)(JsonNullableLong(source, "LineStart", "lineStart") ?? 0);
                var lineEnd = (int)(JsonNullableLong(source, "LineEnd", "lineEnd") ?? 0);

                if (registrationId is null ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(relativeFile) ||
                    lineStart <= 0 ||
                    lineEnd < lineStart)
                    continue;

                var facts = ReadFacts(generic);

                result.Add(new PassCandidate
                {
                    Kind = kind.Value,
                    SourceFirst = JsonBool(consumer, "sourceFirst"),
                    RegistrationId = registrationId.Value,
                    Owner = owner,
                    RelativeFile = relativeFile.Replace('\\', '/'),
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
                        x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase)),
                    ApplyFrameDispatch = recipes.Any(x =>
                        x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase)),
                    ApplyInteractionUiIdleGuard = recipes.Any(x =>
                        x.Equals("INTERACTION_UI_IDLE_GUARD", StringComparison.OrdinalIgnoreCase)),
                    ApplyCrossFileInteractionUiIdleCallGuard = recipes.Any(x =>
                        x.Equals("CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD", StringComparison.OrdinalIgnoreCase)),
                    ApplyUiVisibilityDormancy = recipes.Any(x =>
                        x.Equals("UI_VISIBILITY_DORMANCY", StringComparison.OrdinalIgnoreCase)),
                    ApplySequenceFirstPolling = recipes.Any(x =>
                        x.Equals("SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL", StringComparison.OrdinalIgnoreCase)),
                    ApplyHardDormantGuardHoist = recipes.Any(x =>
                        x.Equals("HARD_DORMANT_GUARD_HOIST", StringComparison.OrdinalIgnoreCase)),
                    HardDormantGateExpression = facts.HardDormantGateExpression,
                    HardDormantPreGuardReadCount = facts.HardDormantPreGuardReadCount,
                    InteractionUiFunction = facts.InteractionUiFunction,
                    InteractionUiGate = facts.InteractionUiGate,
                    InteractionUiIdleReset = facts.InteractionUiIdleReset,
                    InteractionUiHubVariable = facts.InteractionUiHubVariable,
                    CrossFileInteractionUiFunction = facts.CrossFileInteractionUiFunction,
                    CrossFileInteractionUiReceiver = facts.CrossFileInteractionUiReceiver,
                    CrossFileInteractionUiGate = facts.CrossFileInteractionUiGate,
                    CrossFileInteractionUiPending = facts.CrossFileInteractionUiPending,
                    CrossFileInteractionUiIdleReset = facts.CrossFileInteractionUiIdleReset,
                    CrossFileInteractionUiModuleFile = facts.CrossFileInteractionUiModuleFile,
                    UiVisibilityGate = facts.UiVisibilityGate,
                    UiVisibilityWakeKind = facts.UiVisibilityWakeKind,
                    SequenceSystemName = facts.SequenceSystemName,
                    SequenceBridgeVariable = facts.SequenceBridgeVariable,
                    SequenceVariable = facts.SequenceVariable,
                    SequenceMember = facts.SequenceMember,
                    SequenceLastVariable = facts.SequenceLastVariable,
                    SequenceWrappedPcall = facts.SequenceWrappedPcall
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
                    var lineStart = (int)(JsonNullableLong(callback, "lineStart") ?? 0);
                    var lineEnd = (int)(JsonNullableLong(callback, "lineEnd") ?? 0);

                    if (registrationId is null ||
                        string.IsNullOrWhiteSpace(owner) ||
                        string.IsNullOrWhiteSpace(relativeFile) ||
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
                        LineStart = lineStart,
                        LineEnd = lineEnd,
                        Pattern = "SHARED_PROVIDER_READ",
                        MeasuredExclusiveMsPerSecond = JsonDouble(callback, "exclusiveMsPerSecond"),
                        DeepObserved = JsonBool(callback, "deepObserved")
                    });
                }
            }
        }

        // Minified/packed Lua can map several measured callbacks to the same
        // physical source range (for example line 1..1 for an entire one-line
        // file). A shared-provider substitution replaces every exact getter in
        // that range, so emitting another candidate for the same provider/site
        // can only revalidate against text already rewritten by the first one.
        //
        // Coalesce those duplicate physical sites before generation. Prefer a
        // callback with direct deep evidence, then the highest measured work,
        // so the manifest attribution stays attached to the strongest evidence.
        var nonShared = result
            .Where(x => x.Kind != CandidateKind.SharedProvider)
            .ToList();

        var shared = result
            .Where(x => x.Kind == CandidateKind.SharedProvider)
            .GroupBy(
                x => string.Join(
                    "|",
                    x.Provider,
                    x.RelativeFile,
                    x.LineStart.ToString(),
                    x.LineEnd.ToString()),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => x.DeepObserved)
                .ThenByDescending(x => x.MeasuredExclusiveMsPerSecond)
                .ThenBy(x => x.RegistrationId)
                .First())
            .ToList();

        nonShared.AddRange(shared);

        // Current-source registrations supersede captured coordinates for the
        // SAME kind/event in the SAME Lua file. A capture from an optimized
        // runtime may point to different lines after a Nexus reset. Retain
        // measured rows in ANALYZE for attribution, but never let them race a
        // structural current-source registrar in GENERATE. Other independent
        // candidate kinds and files remain untouched.
        var sourceFirstSites = nonShared
            .Where(candidate => candidate.SourceFirst &&
                (candidate.Kind == CandidateKind.Action ||
                 candidate.Kind == CandidateKind.Frame))
            .Select(candidate => string.Join("|", candidate.RelativeFile,
                candidate.Kind, candidate.FrameEvent))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        nonShared.RemoveAll(candidate => !candidate.SourceFirst &&
            (candidate.Kind == CandidateKind.Action ||
             candidate.Kind == CandidateKind.Frame) &&
            sourceFirstSites.Contains(string.Join("|", candidate.RelativeFile,
                candidate.Kind, candidate.FrameEvent)));
        return nonShared;
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
            OverridePrefilterGateMember = JsonString(facts, "overridePrefilterGateMember"),
            InteractionUiFunction = JsonString(facts, "interactionUiFunction"),
            InteractionUiGate = JsonString(facts, "interactionUiGate"),
            InteractionUiIdleReset = JsonString(facts, "interactionUiIdleReset"),
            InteractionUiHubVariable = JsonString(facts, "interactionUiHubVariable"),
            CrossFileInteractionUiFunction = JsonString(facts, "crossFileInteractionUiFunction"),
            CrossFileInteractionUiReceiver = JsonString(facts, "crossFileInteractionUiReceiver"),
            CrossFileInteractionUiGate = JsonString(facts, "crossFileInteractionUiGate"),
            CrossFileInteractionUiPending = JsonString(facts, "crossFileInteractionUiPending"),
            CrossFileInteractionUiIdleReset = JsonString(facts, "crossFileInteractionUiIdleReset"),
            CrossFileInteractionUiModuleFile = JsonString(facts, "crossFileInteractionUiModuleFile"),
            UiVisibilityGate = JsonString(facts, "uiVisibilityGate"),
            UiVisibilityWakeKind = JsonString(facts, "uiVisibilityWakeKind"),
            HardDormantGateExpression = JsonString(facts, "hardDormantGateExpression"),
            HardDormantPreGuardReadCount = (int)(JsonNullableLong(facts, "hardDormantPreGuardReadCount") ?? 0),
            SequenceSystemName = JsonString(facts, "sequenceSystemName"),
            SequenceBridgeVariable = JsonString(facts, "sequenceBridgeVariable"),
            SequenceVariable = JsonString(facts, "sequenceVariable"),
            SequenceMember = JsonString(facts, "sequenceMember"),
            SequenceLastVariable = JsonString(facts, "sequenceLastVariable"),
            SequenceWrappedPcall = JsonBool(facts, "sequenceWrappedPcall")
        };
    }

    private static TransformResult TransformFile(
        byte[] originalBytes,
        IReadOnlyList<PassCandidate> candidates,
        string modsRoot,
        List<object> transformManifest,
        List<object> skipped)
    {
        var transformManifestStart = transformManifest.Count;
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
                if (candidate.ApplyFrameDispatch)
                {
                    if (effectiveLineEnd > lines.Count)
                    {
                        skipped.Add(Skip(
                            candidate,
                            "Recorded frame callback range is outside the current file."));
                    }
                    else
                    {
                        var frameLines = lines
                            .Skip(candidate.LineStart - 1)
                            .Take(effectiveLineEnd - candidate.LineStart + 1)
                            .ToArray();
                        var frameSegment = string.Join("\n", frameLines);

                        // Resolver authorizes a direct onUpdate/onDraw registration from the
                        // complete callback source range. Revalidate the same semantic
                        // shape here instead of requiring the registrar and event name
                        // to happen to share one physical source line.
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
                        }
                        else
                        {
                            var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                            var registrar = frameOpening.Groups["registrar"].Value;
                            var registrarOffset = frameOpening.Groups["registrar"].Index;
                            if (string.IsNullOrWhiteSpace(registrar) || registrarOffset < 0)
                            {
                                skipped.Add(Skip(
                                    candidate,
                                    $"Direct {candidate.FrameEvent} registrar token could not be revalidated."));
                            }
                            else
                            {
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
                            }
                        }
                    }
                }

                if (candidate.ApplyInteractionUiIdleGuard)
                {
                    var expectedProof = new InteractionUiIdleGuardProof(
                        candidate.InteractionUiFunction,
                        candidate.InteractionUiGate,
                        candidate.InteractionUiIdleReset,
                        candidate.InteractionUiHubVariable);
                    var currentText = string.Join("\n", lines);
                    if (GenericInteractionUiTransform.TryApply(
                            currentText,
                            expectedProof,
                            out var rewrittenInteractionUi))
                    {
                        lines = rewrittenInteractionUi.Split('\n').ToList();
                        transformManifest.Add(new
                        {
                            registrationId = candidate.RegistrationId,
                            owner = candidate.Owner,
                            type = "INTERACTION_UI_IDLE_GUARD",
                            file = candidate.RelativeFile,
                            function = candidate.InteractionUiFunction,
                            gate = candidate.InteractionUiGate,
                            preservedIdleReset = candidate.InteractionUiIdleReset,
                            proof = "getDialogChoiceHubs read is dominated by the visible hub gate; hidden path retains only the original false-state reset"
                        });
                        applied++;
                    }
                    else
                    {
                        skipped.Add(Skip(
                            candidate,
                            "Interaction-UI idle guard could not be revalidated after earlier generic transforms; leave the function unchanged."));
                    }
                }

                if (candidate.ApplyCrossFileInteractionUiIdleCallGuard)
                {
                    var modulePath = ResolveInsideMods(
                        modsRoot,
                        candidate.CrossFileInteractionUiModuleFile);
                    if (modulePath is null || !File.Exists(modulePath))
                    {
                        skipped.Add(Skip(
                            candidate,
                            "Cross-file interaction UI module is missing; leave onDraw unchanged."));
                    }
                    else
                    {
                        var moduleBytes = File.ReadAllBytes(modulePath);
                        if (effectiveLineEnd > lines.Count)
                        {
                            skipped.Add(Skip(
                                candidate,
                                "Recorded cross-file interaction UI callback range is outside the current file."));
                        }
                        else
                        {
                            var callbackLines = lines
                                .Skip(candidate.LineStart - 1)
                                .Take(effectiveLineEnd - candidate.LineStart + 1)
                                .ToArray();
                            var callbackSegment = string.Join("\n", callbackLines);
                            var moduleText = Encoding.UTF8.GetString(moduleBytes)
                                .TrimStart('\uFEFF');

                            if (!GenericInteractionUiTransform.TryProveCrossFileCall(
                                    callbackSegment,
                                    moduleText,
                                    candidate.CrossFileInteractionUiModuleFile,
                                    out var currentProof))
                            {
                                skipped.Add(Skip(
                                    candidate,
                                    "Cross-file interaction UI proof could not be revalidated; leave onDraw unchanged."));
                            }
                            else
                            {
                                var expectedProof = new CrossFileInteractionUiCallProof(
                                    candidate.CrossFileInteractionUiFunction,
                                    candidate.CrossFileInteractionUiReceiver,
                                    candidate.CrossFileInteractionUiGate,
                                    candidate.CrossFileInteractionUiPending,
                                    candidate.CrossFileInteractionUiIdleReset,
                                    candidate.CrossFileInteractionUiModuleFile);

                                // Recheck current callback and module semantics.
                                if (currentProof != expectedProof ||
                                    !GenericInteractionUiTransform.TryApplyCrossFileCallGuard(
                                        callbackSegment,
                                        currentProof,
                                        out var rewrittenCrossFileUi))
                                {
                                    skipped.Add(Skip(
                                        candidate,
                                        "Cross-file interaction UI call guard no longer matches the analyzed proof."));
                                }
                                else
                                {
                                    var crossFileReplacementLines = rewrittenCrossFileUi.Split('\n');
                                    lines.RemoveRange(
                                        candidate.LineStart - 1,
                                        effectiveLineEnd - candidate.LineStart + 1);
                                    lines.InsertRange(
                                        candidate.LineStart - 1,
                                        crossFileReplacementLines);

                                    transformManifest.Add(new
                                    {
                                        registrationId = candidate.RegistrationId,
                                        owner = candidate.Owner,
                                        type = "CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD",
                                        file = candidate.RelativeFile,
                                        function = candidate.CrossFileInteractionUiFunction,
                                        gate = candidate.CrossFileInteractionUiGate,
                                        pending = candidate.CrossFileInteractionUiPending,
                                        moduleFile = candidate.CrossFileInteractionUiModuleFile,
                                        proof = "call-only onDraw delegates to a required module whose hidden path is source-proven to perform only the preserved false-state reset"
                                    });
                                    applied++;
                                }
                            }
                        }
                    }
                }

                if (candidate.ApplyUiVisibilityDormancy)
                {
                    var expectedProof = new UiVisibilityDormancyProof(
                        candidate.UiVisibilityGate,
                        candidate.UiVisibilityWakeKind);
                    var currentText = string.Join("\n", lines);
                    if (GenericUiVisibilityTransform.TryApply(
                            currentText,
                            expectedProof,
                            out var rewrittenUiVisibility))
                    {
                        lines = rewrittenUiVisibility.Split('\n').ToList();
                        transformManifest.Add(new
                        {
                            registrationId = candidate.RegistrationId,
                            owner = candidate.Owner,
                            type = "UI_VISIBILITY_DORMANCY",
                            file = candidate.RelativeFile,
                            gate = candidate.UiVisibilityGate,
                            wake = candidate.UiVisibilityWakeKind,
                            proof = "entire onDraw body is source-proven under one simple visibility gate with an independent wake writer"
                        });
                        applied++;
                    }
                    else
                    {
                        skipped.Add(Skip(
                            candidate,
                            "UI visibility dormancy proof could not be revalidated after earlier generic transforms; leave onDraw unchanged."));
                    }
                }

                if (candidate.ApplyHardDormantGuardHoist)
                {
                    if (effectiveLineEnd > lines.Count)
                    {
                        skipped.Add(Skip(
                            candidate,
                            "Recorded hard-dormant callback range is outside the current file."));
                    }
                    else
                    {
                        var callbackLines = lines
                            .Skip(candidate.LineStart - 1)
                            .Take(effectiveLineEnd - candidate.LineStart + 1)
                            .ToArray();
                        var callbackSegment = string.Join("\n", callbackLines);
                        var expectedProof = new HardDormantGuardProof(
                            candidate.HardDormantGateExpression,
                            candidate.HardDormantPreGuardReadCount);

                        if (GenericHardDormantGuardTransform.TryApply(
                                callbackSegment,
                                expectedProof,
                                out var rewrittenHardDormant))
                        {
                            var hardDormantReplacementLines = rewrittenHardDormant.Split('\n');
                            lines.RemoveRange(
                                candidate.LineStart - 1,
                                effectiveLineEnd - candidate.LineStart + 1);
                            lines.InsertRange(candidate.LineStart - 1, hardDormantReplacementLines);

                            transformManifest.Add(new
                            {
                                registrationId = candidate.RegistrationId,
                                owner = candidate.Owner,
                                type = "HARD_DORMANT_GUARD_HOIST",
                                file = candidate.RelativeFile,
                                gate = candidate.HardDormantGateExpression,
                                bypassedReadCount = candidate.HardDormantPreGuardReadCount,
                                proof = "existing inactive early-return moved ahead of read-only setup; source-proven hotkey/input wake remains outside callback"
                            });
                            applied++;
                        }
                        else
                        {
                            skipped.Add(Skip(
                                candidate,
                                "Hard-dormant guard hoist could not be revalidated after earlier generic transforms; leave callback unchanged."));
                        }
                    }
                }

                if (candidate.ApplySequenceFirstPolling)
                {
                    var expectedProof = new SequencePollingProof(
                        candidate.SequenceSystemName,
                        candidate.SequenceBridgeVariable,
                        candidate.SequenceVariable,
                        candidate.SequenceMember,
                        candidate.SequenceLastVariable,
                        candidate.SequenceWrappedPcall);
                    var currentText = string.Join("\n", lines);
                    if (GenericSequencePollingTransform.TryApply(
                            currentText,
                            expectedProof,
                            out var rewrittenSequence))
                    {
                        lines = rewrittenSequence.Split('\n').ToList();
                        transformManifest.Add(new
                        {
                            registrationId = candidate.RegistrationId,
                            owner = candidate.Owner,
                            type = "SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL",
                            file = candidate.RelativeFile,
                            system = candidate.SequenceSystemName,
                            sequence = candidate.SequenceMember,
                            last = candidate.SequenceLastVariable,
                            proof = "literal ScriptableSystem bridge feeds an unchanged-sequence early return before later callback work"
                        });
                        applied++;
                    }
                    else
                    {
                        skipped.Add(Skip(
                            candidate,
                            "Sequence-first ScriptableSystem polling proof could not be revalidated after earlier generic transforms; leave callback unchanged."));
                    }
                }

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

        var insertedFrameHeaderCount = 0;
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
                header.Add("    if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.GCET) == \"table\" and type(__gcetEngine.GCET.MakeEventRegistrar) == \"function\" then __gcetApi = __gcetEngine.GCET end");
                header.Add("    if __gcetOk and type(__gcetApi) == \"table\" and type(__gcetApi.MakeEventRegistrar) == \"function\" then");
                header.Add($"        {item.Token} = __gcetApi.MakeEventRegistrar({owner}, registerForEvent)");
                header.Add("    end");
                header.Add("end");
                header.Add("");
            }

            lines.InsertRange(0, header);
            insertedFrameHeaderCount = header.Count;
        }

        var outputText = string.Join(newline, lines);
        if (hadTerminalNewline)
            outputText += newline;

        var generatedBudget = LuaLocalBudgetAnalyzer.Analyze(outputText);
        if(generatedBudget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit &&
           insertedFrameHeaderCount>0)
        {
            // Move only generated frame registrar locals into one-time local
            // closures at the original callsites. No author code/scope changes.
            var scoped=string.Join(newline,lines.Skip(insertedFrameHeaderCount));
            var proven=true;
            foreach(var item in frameHelpers)
            {
                var call=item.Token+"(";
                if(scoped.Split(call,StringSplitOptions.None).Length!=2)
                {
                    proven=false;
                    break;
                }
                scoped=scoped.Replace(call,
                    BuildScopedFrameRegistrar(item.Candidate.Owner)+"(",
                    StringComparison.Ordinal);
            }
            if(proven)
            {
                var scopedText=scoped+(hadTerminalNewline?newline:"");
                var scopedBudget=LuaLocalBudgetAnalyzer.Analyze(scopedText);
                if(scopedBudget.MaxActiveLocals<=LuaLocalBudgetAnalyzer.Limit)
                {
                    outputText=scopedText;
                    generatedBudget=scopedBudget;
                }
            }
        }
        if (generatedBudget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit)
        {
            var sourceBudget = LuaLocalBudgetAnalyzer.Analyze(text);
            if (transformManifest.Count > transformManifestStart)
            {
                transformManifest.RemoveRange(
                    transformManifestStart,
                    transformManifest.Count - transformManifestStart);
            }

            var relativeFile = candidates.FirstOrDefault()?.RelativeFile ?? "";
            var owners = candidates
                .Select(x => x.Owner)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            skipped.Add(new
            {
                type = "LUA_LOCAL_LIMIT",
                file = relativeFile,
                owner = string.Join(", ", owners),
                limit = LuaLocalBudgetAnalyzer.Limit,
                sourceMaxActiveLocals = sourceBudget.MaxActiveLocals,
                generatedMaxActiveLocals = generatedBudget.MaxActiveLocals,
                generatedPeakLine = generatedBudget.Line,
                generatedPeakScope = generatedBudget.Scope,
                reason =
                    $"G-CET refused all generated optimization for this Lua file because the transformed source would require {generatedBudget.MaxActiveLocals} simultaneously active local variables in {generatedBudget.Scope} at line {generatedBudget.Line}; Lua's hard per-function limit is {LuaLocalBudgetAnalyzer.Limit}. The live source peaks at {sourceBudget.MaxActiveLocals}. Author code was not deleted or rewritten to make room."
            });

            return new TransformResult(originalBytes, 0);
        }

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

    private static string BuildScopedFrameRegistrar(string owner)
    {
        // Registration-time factory: inner locals never enter the author's
        // saturated main chunk. Original 0-Engine and CET fallback preserved.
        return "((function() " +
            "local __gcetRegister = registerForEvent; " +
            "local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\"); " +
            "local __gcetApi = __gcetEngine; " +
            "if __gcetOk and type(__gcetEngine) == \"table\" and " +
            "type(__gcetEngine.GCET) == \"table\" and " +
            "type(__gcetEngine.GCET.MakeEventRegistrar) == \"function\" " +
            "then __gcetApi = __gcetEngine.GCET end; " +
            "if __gcetOk and type(__gcetApi) == \"table\" and " +
            "type(__gcetApi.MakeEventRegistrar) == \"function\" " +
            "then __gcetRegister = __gcetApi.MakeEventRegistrar(" +
            LuaQuote(owner) + ", registerForEvent) end; " +
            "return __gcetRegister end)())";
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
        // Reuse the router's decoded name only for a source-proven pure decode.
        // Preserve the original author decode on the Observe fallback (nil
        // routed name), including trailing Lua comments. Do not put generated
        // closing parentheses after --, which would be swallowed as a comment.
        var match = Regex.Match(
            source,
            @"(?m)^(?<indent>[ \t]*)local\s+(?<var>[A-Za-z_]\w*)\s*=\s*(?<expr>[^\r\n;]*\b(?:GetName|NameToString)\b[^\r\n;]*)[ \t]*;?[ \t]*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
            return false;

        var rhs = match.Groups["expr"].Value;
        var commentIndex = rhs.IndexOf("--", StringComparison.Ordinal);
        var beforeComment = commentIndex >= 0 ? rhs[..commentIndex] : rhs;
        var expression = beforeComment.Trim();
        var trailingComment = commentIndex >= 0
            ? beforeComment[beforeComment.TrimEnd().Length..] + rhs[commentIndex..]
            : "";

        // Full-expression proof: no concatenation, other function calls,
        // boolean fallback, string containing --, or side effects are folded
        // into the cached name. Unknown author forms retain their original code.
        if (!Regex.IsMatch(
                expression,
                @"^(?:Game\s*\.\s*NameToString\s*\(\s*)?action\s*[:.]\s*GetName\s*\(\s*(?:action\s*)?\)\s*(?:\.\s*value\s*)?\)?$",
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
            "))" +
            trailingComment;

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
        lines.Add($"{indent}if __gcetOk_{candidate.RegistrationId} and type(__gcetEngine_{candidate.RegistrationId}) == \"table\" and type(__gcetEngine_{candidate.RegistrationId}.GCET) == \"table\" and type(__gcetEngine_{candidate.RegistrationId}.GCET.SubscribeAction) == \"function\" then __gcetApi_{candidate.RegistrationId} = __gcetEngine_{candidate.RegistrationId}.GCET end");
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
                "            local __gcetApi = type(__gcetEngine.GCET) == \"table\" and type(__gcetEngine.GCET.GetPlayer) == \"function\" and __gcetEngine.GCET or __gcetEngine",
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
            $"            local __gcetApi = type(__gcetEngine.GCET) == \"table\" and type(__gcetEngine.GCET.{recipe.EngineGetter}) == \"function\" and __gcetEngine.GCET or __gcetEngine",
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
        public bool SourceFirst { get; init; }
        public string Provider { get; init; } = "";
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public int LineStart { get; init; }
        public int LineEnd { get; init; }
        public string Pattern { get; init; } = "";
        public double MeasuredExclusiveMsPerSecond { get; init; }
        public bool DeepObserved { get; init; }
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
        public bool ApplyFrameDispatch { get; init; }
        public bool ApplyInteractionUiIdleGuard { get; init; }
        public bool ApplyCrossFileInteractionUiIdleCallGuard { get; init; }
        public bool ApplyUiVisibilityDormancy { get; init; }
        public bool ApplySequenceFirstPolling { get; init; }
        public bool ApplyHardDormantGuardHoist { get; init; }
        public string HardDormantGateExpression { get; init; } = "";
        public int HardDormantPreGuardReadCount { get; init; }
        public string InteractionUiFunction { get; init; } = "";
        public string InteractionUiGate { get; init; } = "";
        public string InteractionUiIdleReset { get; init; } = "";
        public string InteractionUiHubVariable { get; init; } = "";
        public string CrossFileInteractionUiFunction { get; init; } = "";
        public string CrossFileInteractionUiReceiver { get; init; } = "";
        public string CrossFileInteractionUiGate { get; init; } = "";
        public string CrossFileInteractionUiPending { get; init; } = "";
        public string CrossFileInteractionUiIdleReset { get; init; } = "";
        public string CrossFileInteractionUiModuleFile { get; init; } = "";
        public string UiVisibilityGate { get; init; } = "";
        public string UiVisibilityWakeKind { get; init; } = "";
        public string SequenceSystemName { get; init; } = "";
        public string SequenceBridgeVariable { get; init; } = "";
        public string SequenceVariable { get; init; } = "";
        public string SequenceMember { get; init; } = "";
        public string SequenceLastVariable { get; init; } = "";
        public bool SequenceWrappedPcall { get; init; }
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
        public string InteractionUiFunction { get; init; } = "";
        public string InteractionUiGate { get; init; } = "";
        public string InteractionUiIdleReset { get; init; } = "";
        public string InteractionUiHubVariable { get; init; } = "";
        public string CrossFileInteractionUiFunction { get; init; } = "";
        public string CrossFileInteractionUiReceiver { get; init; } = "";
        public string CrossFileInteractionUiGate { get; init; } = "";
        public string CrossFileInteractionUiPending { get; init; } = "";
        public string CrossFileInteractionUiIdleReset { get; init; } = "";
        public string CrossFileInteractionUiModuleFile { get; init; } = "";
        public string UiVisibilityGate { get; init; } = "";
        public string UiVisibilityWakeKind { get; init; } = "";
        public string HardDormantGateExpression { get; init; } = "";
        public int HardDormantPreGuardReadCount { get; init; }
        public string SequenceSystemName { get; init; } = "";
        public string SequenceBridgeVariable { get; init; } = "";
        public string SequenceVariable { get; init; } = "";
        public string SequenceMember { get; init; } = "";
        public string SequenceLastVariable { get; init; } = "";
        public bool SequenceWrappedPcall { get; init; }
    }


    // Generic one-pass source generation for two previously established
    // Phase1+2 runtime-hook corrections. Applies to matching SOURCE STRUCTURES,
    // not a fixed mod inventory. Do not reintroduce the invalid author hook
    // simply because an original file, rather than old G-CET output, is used.
    internal static bool TryTransformUnsupportedGameHooks(
        string relative, string text, out string output, out string[] stages)
    {
        output=text;
        var accepted=new List<string>();
        if (Path.GetFileName(relative).Equals("GameUI.lua",StringComparison.OrdinalIgnoreCase))
        {
            // GameUI's pause menu contains a copied table of CET observers.
            // OnSwitchToCredits is absent on MenuScenario_PauseMenu and CET
            // logs an error for every attempted observer registration.
            // Preserve the other entries; never invent a replacement event.
            var menu=Regex.Matches(output,
                @"(?m)^[ \t]*\[[ \t]*['""]MenuScenario_PauseMenu['""][ \t]*\][ \t]*=[ \t]*\{[ \t]*(?:--[^\r\n]*)?$",
                RegexOptions.CultureInvariant);
            if(menu.Count>1)
                throw new InvalidOperationException("Ambiguous GameUI pause-menu observer table: "+relative);
            if(menu.Count==1)
            {
                var begin=menu[0].Index+menu[0].Length;
                var closing=Regex.Match(output[begin..],
                    @"(?m)^[ \t]*\},[ \t]*(?:--[^\r\n]*)?$",
                    RegexOptions.CultureInvariant);
                if(!closing.Success || closing.Index>1500)
                    throw new InvalidOperationException("GameUI pause-menu table drifted: "+relative);
                var table=output.Substring(begin,closing.Index);
                var entries=Regex.Matches(table,
                    @"(?m)^(?<indent>[ \t]*)(?<entry>\[[ \t]*['""]OnSwitchToCredits['""][ \t]*\][ \t]*=[ \t]*['""]Credits['""][ \t]*,)(?<tail>[ \t]*(?:--[^\r\n]*)?)$",
                    RegexOptions.CultureInvariant);
                if(entries.Count>1)
                    throw new InvalidOperationException("Ambiguous GameUI credits observer: "+relative);
                if(entries.Count==1)
                {
                    var m=entries[0];
                    var suffix=m.Groups["tail"].Value;
                    var replaced=m.Groups["indent"].Value+"-- "+m.Groups["entry"].Value+
                        suffix+(suffix.Contains("--",StringComparison.Ordinal)?"":
                            " -- G-CET: nonexistent MenuScenario_PauseMenu game event");
                    var i=begin+m.Index;
                    output=output[..i]+replaced+output[(i+m.Length)..];
                    accepted.Add("PAUSE_MENU_MISSING_CREDITS_OBSERVER");
                }
            }
        }

        // The prior optimized Exodus source already established this exact
        // functional hook replacement. Its author-provided boss validation and
        // death/cleanup callbacks must remain byte-for-byte unchanged.
        var attaches=Regex.Matches(output,
            @"Observe[ \t]*\([ \t]*['""]ScriptedPuppet['""][ \t]*,[ \t]*['""]OnAttach['""][ \t]*,[ \t]*function[ \t]*\([ \t]*self[ \t]*\)",
            RegexOptions.CultureInvariant);
        var candidateSource=output; // out parameters cannot be captured by LINQ lambdas
        var qualified=attaches.Cast<Match>().Where(m=>
        {
            var tail=candidateSource.Substring(m.Index,Math.Min(1600,candidateSource.Length-m.Index));
            return tail.Contains("if combatPhase >= 1 and activeBossRecord then",StringComparison.Ordinal) &&
                tail.Contains("local selfIDStr = tostring(self:GetRecordID())",StringComparison.Ordinal) &&
                tail.Contains("local isValidEnemy = false",StringComparison.Ordinal);
        }).ToArray();
        if(qualified.Length>1)
            throw new InvalidOperationException("Ambiguous boss attachment observer: "+relative);
        if(qualified.Length==1)
        {
            var m=qualified[0];
            var rewritten=m.Value.Replace("OnAttach","OnGameAttached",StringComparison.Ordinal);
            output=output[..m.Index]+rewritten+output[(m.Index+m.Length)..];
            accepted.Add("SCRIPTED_PUPPET_BOSS_GAME_ATTACHED");
        }
        stages=accepted.ToArray();
        return stages.Length>0;
    }

    private static int StageUnsupportedGameHookCompatibility(
        string modsRoot,Dictionary<string,byte[]> staged,
        Dictionary<string,int> appliedTransformsByFile,List<object> transformManifest)
    {
        var root=Path.GetFullPath(modsRoot);
        var applied=0;
        foreach(var path in Directory.EnumerateFiles(root,"*.lua",
            SearchOption.AllDirectories).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            var rel=Path.GetRelativePath(root,path).Replace('\\','/');
            if(ModOptimizationBlacklist.IsBlacklistedPath(rel))continue;
            var input=staged.TryGetValue(rel,out var previous)?previous:File.ReadAllBytes(path);
            var hasBom=input.Length>=3&&input[0]==0xef&&input[1]==0xbb&&input[2]==0xbf;
            var text=Encoding.UTF8.GetString(input,hasBom?3:0,input.Length-(hasBom?3:0));
            if(!TryTransformUnsupportedGameHooks(rel,text,out var output,out var stages))continue;
            var bytes=Encoding.UTF8.GetBytes(output);
            if(hasBom)bytes=[0xef,0xbb,0xbf,..bytes];
            staged[rel]=bytes;
            appliedTransformsByFile[rel]=
                appliedTransformsByFile.GetValueOrDefault(rel)+stages.Length;
            foreach(var stage in stages)
                transformManifest.Add(new {
                    type="GENERIC_SOURCE_RUNTIME_HOOK",
                    file=rel,
                    stage,
                    sourceProof="CURRENT_LUA_CALLSITE_AND_AUTHOR_CALLBACK",
                    provenance="PHASE12_PRESERVED_BEHAVIOR",
                    sourceState="ORIGINAL_OR_PREVIOUSLY_OPTIMIZED",
                    originalCallbackBodyPreserved=true
                });
            applied+=stages.Length;
        }
        return applied;
    }

    private sealed record TransformResult(byte[] Bytes, int AppliedTransforms);
}
