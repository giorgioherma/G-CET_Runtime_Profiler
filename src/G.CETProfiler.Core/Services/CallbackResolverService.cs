using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record CallbackResolverDocumentResult(
    object Document,
    int FamilyCount,
    int RankedCallbackCount,
    int GenericResolvedCount,
    int NonFrameOnlyAutoCount,
    int FrameOnlyAutoCount,
    int MaterialRemainingCount,
    int BelowThresholdCount,
    int SemanticReadyRuleCount,
    int SharedProviderReadyCallbackCount,
    int SharedProviderReadyReadCount,
    string[] SharedProviderReadyFamilies,
    int AlreadySatisfiedCount,
    int UnresolvedCount);

internal static class CallbackResolverService
{
    // A source-safe transform still does not justify touching user code unless
    // the measured avoidable portion is meaningful. These are relative floors,
    // so selection scales with the user's actual CET workload rather than CPU
    // speed or an arbitrary fixed millisecond budget.
    private const double AuthorCadenceMinCallbackPaybackPct = 10.0;
    private const double AuthorCadenceMinGlobalPaybackPct = 0.05;
    private const double MaterialRemainingMsPerSecond = 3.0;

    private static readonly HashSet<string> SharedProviderGenerationFamilies = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "PLAYER",
        "QUESTS_SYSTEM",
        "STATS_SYSTEM",
        "TRANSACTION_SYSTEM",
        "BLACKBOARD_SYSTEM",
        "TARGETING_SYSTEM",
        "CAMERA_SYSTEM",
        "TIME_SYSTEM",
        "PREVENTION_SYSTEM",
        "SCRIPTABLE_SYSTEMS_CONTAINER"
    };

    private static readonly Regex NormalizeNonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static CallbackResolverDocumentResult Build(
        string handoffPath,
        string modsRoot,
        string? cadenceFinalPath,
        string? semanticLibraryPath)
    {
        using var handoff = JsonDocument.Parse(File.ReadAllText(handoffPath));
        var callbacks = ReadCallbacks(handoff.RootElement)
            .Where(x => !x.Infrastructure && x.ExclusiveMsPerSecond > 0)
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenByDescending(x => x.CallsPerSecond)
            .ToList();

        var cadence = ReadCadenceDecisions(cadenceFinalPath);
        var semanticLibrary = SemanticLibraryService.Load(semanticLibraryPath, modsRoot);
        var sourceIndex = new LiveSourceIndex(modsRoot);

        var familyGroups = callbacks
            .GroupBy(x => FamilyKey(x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Key = g.Key,
                Rows = g.OrderByDescending(x => x.ExclusiveMsPerSecond)
                    .ThenByDescending(x => x.CallsPerSecond)
                    .ToList(),
                Work = g.Sum(x => x.ExclusiveMsPerSecond),
                Calls = g.Sum(x => x.CallsPerSecond)
            })
            .OrderByDescending(x => x.Work)
            .ThenByDescending(x => x.Calls)
            .ToList();

        var familyDocuments = new List<object>();
        var rankedCount = 0;
        var genericResolved = 0;
        var nonFrameOnlyAuto = 0;
        var frameOnlyAuto = 0;
        var materialRemaining = 0;
        var belowThreshold = 0;
        var semanticMatches = 0;
        var semanticSourceProven = 0;
        var semanticReadyRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = 0;
        var alreadySatisfied = 0;

        foreach (var family in familyGroups)
        {
            var first = family.Rows[0];
            var consumers = new List<object>();

            foreach (var callback in family.Rows)
            {
                rankedCount++;
                var generic = ResolveGeneric(callback, sourceIndex, cadence);
                var semantic = semanticLibrary.Match(
                    callback.Owner,
                    callback.Kind,
                    callback.Target);
                if (semantic.Matched)
                {
                    semanticMatches++;
                    if (semantic.SourceProofSatisfied)
                        semanticSourceProven++;
                }
                var semanticGenerationReady =
                    semantic.Matched &&
                    semantic.SourceProofSatisfied &&
                    semantic.GenerationEnabled &&
                    !semantic.AlreadySatisfied &&
                    !semantic.PartialState &&
                    callback.ExclusiveMsPerSecond >= MaterialRemainingMsPerSecond &&
                    !string.IsNullOrWhiteSpace(semantic.RuleId);
                if (semanticGenerationReady)
                    semanticReadyRules.Add(semantic.RuleId);

                var dormancy = ResolveDormancyEvidence(callback, generic.Source, sourceIndex);

                var isAlreadySatisfied =
                    generic.Status.Equals(
                        "ALREADY_SATISFIED",
                        StringComparison.OrdinalIgnoreCase) ||
                    semantic.AlreadySatisfied;

                if (generic.Automatable)
                {
                    genericResolved++;
                    var recipes = generic.RecipeFamilies;
                    var frameOnly =
                        recipes.Length == 1 &&
                        recipes[0].Equals(
                            "FRAME_DISPATCH_CONSOLIDATION",
                            StringComparison.OrdinalIgnoreCase);
                    if (frameOnly)
                        frameOnlyAuto++;
                    else
                        nonFrameOnlyAuto++;
                }
                else if (isAlreadySatisfied)
                    alreadySatisfied++;
                else if (semanticGenerationReady)
                {
                    // This measured callback is accounted for by a source-proven
                    // semantic rule and must not be presented as unresolved work.
                }
                else
                {
                    unresolved++;
                    if (callback.ExclusiveMsPerSecond >= MaterialRemainingMsPerSecond)
                        materialRemaining++;
                    else
                        belowThreshold++;
                }

                consumers.Add(new
                {
                    callback.registrationId,
                    callback.owner,
                    callback.kind,
                    callback.target,
                    runtime = new
                    {
                        callback.callsPerSecond,
                        callback.exclusiveMsPerSecond,
                        callback.globalWorkSharePct,
                        callback.familyWorkSharePct,
                        callback.avgExclusiveUs,
                        callback.maxExclusiveMs,
                        callback.spikeCount,
                        callback.maxSpikeExclusiveMs
                    },
                    source = generic.Source,
                    generic = new
                    {
                        generic.Status,
                        generic.Automatable,
                        generic.Pattern,
                        generic.RecipeFamilies,
                        generic.Facts,
                        generic.Evidence,
                        generic.Blockers
                    },
                    dormancy = new
                    {
                        dormancy.Class,
                        dormancy.Confidence,
                        dormancy.EvidenceOnly,
                        dormancy.ActiveSignals,
                        dormancy.WakeSignals,
                        dormancy.DiscoverySignals,
                        dormancy.BackgroundSignals,
                        dormancy.SensitiveSignals,
                        dormancy.StateWriterSignals,
                        dormancy.CompleteWakePathProven,
                        dormancy.AuthorDiscoveryCadenceProven,
                        dormancy.AuthorDiscoveryIntervalSeconds,
                        dormancy.AuthorDiscoveryAccumulator,
                        dormancy.AuthorDiscoveryGate,
                        dormancy.DiscoveryRegionSelfContained,
                        dormancy.AuthorDiscoveryBlocker,
                        dormancy.Evidence,
                        dormancy.Blockers
                    },
                    semantic = new
                    {
                        semantic.Matched,
                        semantic.SourceProofSatisfied,
                        semantic.AlreadySatisfied,
                        semantic.PartialState,
                        semantic.MarkerFileCount,
                        semantic.ExpectedMarkerFileCount,
                        semantic.RuleId,
                        semantic.PolicyClass,
                        semantic.Handler,
                        semantic.PatchStyle,
                        semantic.GenerationEnabled,
                        generationReady = semanticGenerationReady,
                        semantic.ShipReferenceOverride,
                        semantic.MatchedAnchors,
                        semantic.MissingAnchors,
                        semantic.Graph,
                        note = semantic.PartialState
                            ? "A partial G-CET semantic marker state was detected. AUTO fails closed instead of treating the rule as complete or attempting a blind repair."
                            : semantic.AlreadySatisfied
                                ? "The complete expected semantic marker state is already present; AUTO will not re-apply the rule."
                                : semantic.Matched
                                    ? "Identity selected a semantic candidate; current live mod source graph must prove the rule. The library contains behavior knowledge, not replacement mod files."
                                    : "No semantic-library rule matched this measured callback."
                    },
                    disposition = generic.Automatable
                        ? "GENERIC_PATTERN"
                        : isAlreadySatisfied
                            ? "ALREADY_SATISFIED"
                            : semantic.PartialState
                                ? "SEMANTIC_RULE_PARTIAL_STATE"
                                : semantic.Matched
                                    ? semantic.SourceProofSatisfied
                                        ? "SEMANTIC_RULE_PROVEN"
                                        : "SEMANTIC_RULE_NEEDS_SOURCE_PROOF"
                                    : "UNRESOLVED"
                });
            }

            familyDocuments.Add(new
            {
                kind = first.Kind,
                target = first.Target,
                exclusiveMsPerSecond = Round(family.Work),
                callsPerSecond = Round(family.Calls),
                consumerCount = family.Rows.Count,
                resolverFamily = ClassifyCallbackFamily(first.Kind, first.Target),
                topConsumers = consumers
            });
        }

        var globalTop = callbacks
            .Take(10)
            .Select((x, index) => new
            {
                rank = index + 1,
                x.registrationId,
                x.owner,
                x.kind,
                x.target,
                x.exclusiveMsPerSecond,
                x.globalWorkSharePct
            })
            .ToArray();

        var sharedProviderOpportunities = BuildSharedProviderOpportunities(
            callbacks,
            sourceIndex,
            handoff.RootElement);

        var sharedProviderReadyOpportunities = sharedProviderOpportunities
            .Where(x => SharedProviderGenerationFamilies.Contains(x.Provider))
            .Select(x => new
            {
                Opportunity = x,
                EligibleCallbacks = x.Callbacks
                    .Where(IsSharedProviderSubstitutionEligible)
                    .ToArray()
            })
            .Where(x => x.EligibleCallbacks.Length > 0)
            .ToArray();

        var sharedProviderReadyCallbacks = sharedProviderReadyOpportunities
            .SelectMany(x => x.EligibleCallbacks)
            .ToArray();
        var sharedProviderReadyCallbackCount = sharedProviderReadyCallbacks
            .Select(cb => cb.RegistrationId ?? 0)
            .Where(id => id > 0)
            .Distinct()
            .Count();
        var sharedProviderReadyReadCount = sharedProviderReadyCallbacks
            .Sum(cb => cb.SourceRecognizedOccurrences);
        var sharedProviderReadyFamilies = sharedProviderReadyOpportunities
            .Select(x => x.Opportunity.Provider)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var document = new
        {
            schemaVersion = "0.2",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                producer = "G-CET-Resolver",
                input = Path.GetFileName(handoffPath),
                domain = "cet/callbacks"
            },
            policy = new
            {
                callbackOriented = true,
                familyFirst = true,
                exhaustiveMeasuredCallbacks = true,
                genericPatternsBeforeSemanticLibrary = true,
                cadenceIsSubset = true,
                liveSourcesReadOnly = true,
                dormancyClassification = true,
                dormancyClassificationEvidenceOnly = true,
                dormancyCanAuthorizeGeneration = false,
                dormancyClasses = new[] { "NEVER_GATE", "HARD_DORMANT", "DISCOVERY_DORMANT", "BACKGROUND", "UNKNOWN" },
                sharedProviderOpportunityAnalysis = true,
                sharedProviderGenerationEnabled = true,
                sharedProviderGenerationFamilies = SharedProviderGenerationFamilies
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                sharedProviderScope = "MEASURED_CALLBACKS_ONLY",
                sharedProviderDeepEvidence = "UNTRUNCATED_KNOWN_PROVIDER_CALLEES_WITH_LEGACY_HOTCALLEES_FALLBACK",
                sharedProviderDiscovery = "ALL_GET_STAR_SYSTEM_PLUS_EXPLICIT_SPECIALS; DEEP_ONLY_EVIDENCE_IS_ANALYSIS_ONLY_UNTIL_CURRENT_SOURCE_IS_PROVEN",
                note = "Generic AUTO is restricted to mechanically source-proven action routing, exact Override prefiltering, frame-dispatch consolidation, and explicitly enabled shared-provider reads with exact current-source proof. Dynamic player-derived state remains analysis-only. Identity-specific automatic behavior lives exclusively in the source-proven semantic library. Shared-provider opportunity totals describe measured callback territory, not estimated savings. Shared-provider deep evidence is aggregated across the measured stack without a per-callback hot-callee top-N gate. Already-satisfied generated states are not re-applied."
            },
            cadence = new
            {
                available = !string.IsNullOrWhiteSpace(cadenceFinalPath) && File.Exists(cadenceFinalPath),
                sourceConfirmedOutput = cadenceFinalPath is null ? null : Path.GetFileName(cadenceFinalPath)
            },
            semanticLibrary = new
            {
                path = semanticLibraryPath,
                loaded = semanticLibrary.Loaded,
                entryCount = semanticLibrary.EntryCount,
                matchedCallbacks = semanticMatches,
                sourceProvenCallbacks = semanticSourceProven,
                policy = "Only measured callbacks are considered. Mod identity selects a candidate; the live owner directory is then graphed and source anchors must prove the rule. Reference overrides are never shipped."
            },
            summary = new
            {
                familyCount = familyGroups.Count,
                rankedCallbackCount = rankedCount,
                genericResolved,
                nonFrameOnlyAuto,
                frameOnlyAuto,
                materialRemaining,
                belowThreshold,
                materialThresholdMsPerSecond = MaterialRemainingMsPerSecond,
                semanticMatches,
                semanticSourceProven,
                semanticReadyRules = semanticReadyRules.Count,
                sharedProviderReadyCallbacks = sharedProviderReadyCallbackCount,
                sharedProviderReadyReads = sharedProviderReadyReadCount,
                sharedProviderReadyFamilies,
                alreadySatisfied,
                unresolved,
                sharedProviderFamilies = sharedProviderOpportunities.Length,
                sharedProviderMeasuredCallbacks = sharedProviderOpportunities
                    .SelectMany(x => x.Callbacks)
                    .Select(x => x.RegistrationId ?? 0)
                    .Where(x => x > 0)
                    .Distinct()
                    .Count()
            },
            globalTopCallbacks = globalTop,
            sharedProviderOpportunities = sharedProviderOpportunities.Select(x => new
            {
                provider = x.Provider,
                category = x.Category,
                measuredOwnerCount = x.MeasuredOwnerCount,
                measuredCallbackCount = x.MeasuredCallbackCount,
                sourceOccurrences = x.SourceOccurrences,
                sourceRecognizedOccurrences = x.SourceRecognizedOccurrences,
                sourceUnresolvedOccurrences = x.SourceUnresolvedOccurrences,
                callbacksWithRepeatedSourceReads = x.CallbacksWithRepeatedSourceReads,
                deepObservedCallbackCount = x.DeepObservedCallbackCount,
                deepSampledCalls = x.DeepSampledCalls,
                deepRepeatedSameInvocationCount = x.DeepRepeatedSameInvocationCount,
                deepMultiCallsiteSampleCount = x.DeepMultiCallsiteSampleCount,
                affectedCallbackWorkMsPerSecond = Round(x.AffectedCallbackWorkMsPerSecond),
                affectedCallbackCallsPerSecond = Round(x.AffectedCallbackCallsPerSecond),
                affectedSpikeCount = x.AffectedSpikeCount,
                maxAffectedSpikeExclusiveMs = Round(x.MaxAffectedSpikeExclusiveMs),
                analysisOnly = !SharedProviderGenerationFamilies.Contains(x.Provider),
                generationEnabled = SharedProviderGenerationFamilies.Contains(x.Provider),
                generationRecipe = SharedProviderGenerationFamilies.Contains(x.Provider)
                    ? "SHARED_PROVIDER_READ"
                    : null,
                providerApi = SharedProviderGenerationFamilies.Contains(x.Provider)
                    ? "0-Engine." + SharedProviderDefinitions
                        .First(d => d.Provider.Equals(x.Provider, StringComparison.OrdinalIgnoreCase))
                        .DeepFunctionNames.First()
                    : null,
                metricMeaning = "Affected callback work is the measured workload of callbacks containing this provider candidate; it is not an estimate of provider savings and provider totals are not additive.",
                owners = x.Owners,
                callbacks = x.Callbacks.Select(cb => new
                {
                    registrationId = cb.RegistrationId,
                    owner = cb.Owner,
                    kind = cb.Kind,
                    target = cb.Target,
                    sourceFile = cb.SourceFile,
                    sourceSha256 = cb.SourceSha256,
                    lineStart = cb.LineStart,
                    lineEnd = cb.LineEnd,
                    substitutionEligible =
                        SharedProviderGenerationFamilies.Contains(x.Provider) &&
                        IsSharedProviderSubstitutionEligible(cb),
                    sourceOccurrences = cb.SourceOccurrences,
                    sourceRecognizedOccurrences = cb.SourceRecognizedOccurrences,
                    sourceUnresolvedOccurrences = cb.SourceUnresolvedOccurrences,
                    sourceCallsites = cb.SourceCallsites,
                    deepOnly = cb.DeepObserved && cb.SourceOccurrences == 0,
                    deepObserved = cb.DeepObserved,
                    deepSampledCalls = cb.DeepSampledCalls,
                    deepRepeatedSameInvocationCount = cb.DeepRepeatedSameInvocationCount,
                    deepMultiCallsiteSampleCount = cb.DeepMultiCallsiteSampleCount,
                    callsPerSecond = Round(cb.CallsPerSecond),
                    exclusiveMsPerSecond = Round(cb.ExclusiveMsPerSecond),
                    spikeCount = cb.SpikeCount,
                    maxSpikeExclusiveMs = Round(cb.MaxSpikeExclusiveMs)
                }).ToArray()
            }).ToArray(),
            callbackFamilies = familyDocuments
        };

        return new CallbackResolverDocumentResult(
            document,
            familyGroups.Count,
            rankedCount,
            genericResolved,
            nonFrameOnlyAuto,
            frameOnlyAuto,
            materialRemaining,
            belowThreshold,
            semanticReadyRules.Count,
            sharedProviderReadyCallbackCount,
            sharedProviderReadyReadCount,
            sharedProviderReadyFamilies,
            alreadySatisfied,
            unresolved);
    }


    private static bool IsSharedProviderSubstitutionEligible(
        SharedProviderCallbackEvidence cb) =>
        cb.SourceRecognizedOccurrences > 0 &&
        cb.SourceUnresolvedOccurrences == 0 &&
        !string.IsNullOrWhiteSpace(cb.SourceSha256) &&
        cb.LineStart.GetValueOrDefault() > 0 &&
        cb.LineEnd.GetValueOrDefault() >= cb.LineStart.GetValueOrDefault();

    private static readonly Regex GenericSharedSystemGetterSourceRegex = new(
        @"\bGame\s*\.\s*(?<getter>Get[A-Za-z0-9_]+System)\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericSharedSystemGetterNameRegex = new(
        @"\b(?<getter>Get[A-Za-z0-9_]+System)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly SharedProviderDefinition[] SharedProviderDefinitions =
    [
        SharedProviderDefinition.Direct(
            "PLAYER",
            "ENTITY_REFERENCE",
            @"\bGame\s*\.\s*GetPlayer\s*\(\s*\)",
            "GetPlayer"),
        SharedProviderDefinition.Direct(
            "QUESTS_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetQuestsSystem\s*\(\s*\)",
            "GetQuestsSystem"),
        SharedProviderDefinition.Direct(
            "STATS_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetStatsSystem\s*\(\s*\)",
            "GetStatsSystem"),
        SharedProviderDefinition.Direct(
            "TRANSACTION_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetTransactionSystem\s*\(\s*\)",
            "GetTransactionSystem"),
        SharedProviderDefinition.Direct(
            "BLACKBOARD_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetBlackboardSystem\s*\(\s*\)",
            "GetBlackboardSystem"),
        SharedProviderDefinition.Direct(
            "TARGETING_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetTargetingSystem\s*\(\s*\)",
            "GetTargetingSystem"),
        SharedProviderDefinition.Direct(
            "CAMERA_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetCameraSystem\s*\(\s*\)",
            "GetCameraSystem"),
        SharedProviderDefinition.Direct(
            "TIME_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetTimeSystem\s*\(\s*\)",
            "GetTimeSystem"),
        SharedProviderDefinition.Direct(
            "PREVENTION_SYSTEM",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetPreventionSystem\s*\(\s*\)",
            "GetPreventionSystem"),
        SharedProviderDefinition.Direct(
            "SCRIPTABLE_SYSTEMS_CONTAINER",
            "SYSTEM_HANDLE",
            @"\bGame\s*\.\s*GetScriptableSystemsContainer\s*\(\s*\)",
            "GetScriptableSystemsContainer"),
        SharedProviderDefinition.Direct(
            "ALL_BLACKBOARD_DEFS",
            "LOOKUP_RESULT_CANDIDATE",
            @"\bGame\s*\.\s*GetAllBlackboardDefs\s*\(\s*\)",
            "GetAllBlackboardDefs"),
        SharedProviderDefinition.Direct(
            "SYSTEM_REQUESTS_HANDLER",
            "SYSTEM_HANDLE_CANDIDATE",
            @"\bGame\s*\.\s*GetSystemRequestsHandler\s*\(\s*\)",
            "GetSystemRequestsHandler"),
        SharedProviderDefinition.Direct(
            "TELEPORTATION_FACILITY",
            "SYSTEM_HANDLE_CANDIDATE",
            @"\bGame\s*\.\s*GetTeleportationFacility\s*\(\s*\)",
            "GetTeleportationFacility"),
        SharedProviderDefinition.PlayerDerived(
            "PLAYER_POSITION",
            "DYNAMIC_STATE",
            "GetWorldPosition"),
        SharedProviderDefinition.PlayerDerived(
            "PLAYER_ORIENTATION",
            "DYNAMIC_STATE",
            "GetWorldOrientation"),
        SharedProviderDefinition.PlayerDerived(
            "PLAYER_COMBAT_STATE",
            "DYNAMIC_STATE",
            "IsInCombat")
    ];

    private static SharedProviderOpportunity[] BuildSharedProviderOpportunities(
        IReadOnlyList<CallbackMetric> callbacks,
        LiveSourceIndex sourceIndex,
        JsonElement handoffRoot)
    {
        var deepEvidence = ReadSharedProviderDeepEvidence(handoffRoot);
        var byProvider = new Dictionary<string, List<SharedProviderCallbackEvidence>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var callback in callbacks)
        {
            var callbackDeep = callback.RegistrationId is long registrationId &&
                               deepEvidence.TryGetValue(registrationId, out var foundDeep)
                ? foundDeep
                : SharedProviderDeepCallbackEvidence.Empty;

            var source = sourceIndex.Resolve(callback);
            var matches = source is null
                ? new List<SharedProviderSourceMatch>()
                : DetectSharedProviderSourceMatches(source);

            var sourceProviders = matches
                .Select(x => x.Provider)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var providerGroup in matches
                         .GroupBy(x => x.Provider, StringComparer.OrdinalIgnoreCase))
            {
                var providerName = providerGroup.Key;
                var providerMatches = providerGroup.ToArray();
                var recognized = providerMatches.Count(x => x.SourceRecognized);
                var unresolved = providerMatches.Length - recognized;
                callbackDeep.ByProvider.TryGetValue(
                    providerName,
                    out var deepForProvider);

                if (!byProvider.TryGetValue(providerName, out var rows))
                {
                    rows = [];
                    byProvider[providerName] = rows;
                }

                rows.Add(new SharedProviderCallbackEvidence
                {
                    RegistrationId = callback.RegistrationId,
                    Owner = callback.Owner,
                    Kind = callback.Kind,
                    Target = callback.Target,
                    SourceFile = source?.RelativeFile ?? "",
                    SourceSha256 = source?.Sha256 ?? "",
                    LineStart = source?.LineStart,
                    LineEnd = source?.LineEnd,
                    SourceOccurrences = providerMatches.Length,
                    SourceRecognizedOccurrences = recognized,
                    SourceUnresolvedOccurrences = unresolved,
                    SourceCallsites = providerMatches
                        .Select(x => new SharedProviderSourceCallsite
                        {
                            Line = x.Line,
                            Proof = x.Proof
                        })
                        .OrderBy(x => x.Line)
                        .ToArray(),
                    DeepObserved = deepForProvider is not null,
                    DeepSampledCalls = deepForProvider?.SampledCalls ?? 0,
                    DeepRepeatedSameInvocationCount =
                        deepForProvider?.RepeatedSameInvocationCount ?? 0,
                    DeepMultiCallsiteSampleCount =
                        deepForProvider?.MultiCallsiteSampleCount ?? 0,
                    CallsPerSecond = callback.CallsPerSecond,
                    ExclusiveMsPerSecond = callback.ExclusiveMsPerSecond,
                    SpikeCount = callback.SpikeCount,
                    MaxSpikeExclusiveMs = callback.MaxSpikeExclusiveMs
                });
            }

            // Discovery must not lose a provider just because the measured
            // callback reaches it through an owned helper outside the callback
            // block. Preserve that deep evidence as analysis-only territory;
            // without exact source proof it can never become substitutionEligible.
            foreach (var deepPair in callbackDeep.ByProvider)
            {
                if (sourceProviders.Contains(deepPair.Key))
                    continue;

                if (!byProvider.TryGetValue(deepPair.Key, out var rows))
                {
                    rows = [];
                    byProvider[deepPair.Key] = rows;
                }

                rows.Add(new SharedProviderCallbackEvidence
                {
                    RegistrationId = callback.RegistrationId,
                    Owner = callback.Owner,
                    Kind = callback.Kind,
                    Target = callback.Target,
                    SourceFile = source?.RelativeFile ?? "",
                    SourceSha256 = source?.Sha256 ?? "",
                    LineStart = source?.LineStart,
                    LineEnd = source?.LineEnd,
                    SourceOccurrences = 0,
                    SourceRecognizedOccurrences = 0,
                    SourceUnresolvedOccurrences = 0,
                    DeepObserved = true,
                    DeepSampledCalls = deepPair.Value.SampledCalls,
                    DeepRepeatedSameInvocationCount =
                        deepPair.Value.RepeatedSameInvocationCount,
                    DeepMultiCallsiteSampleCount =
                        deepPair.Value.MultiCallsiteSampleCount,
                    CallsPerSecond = callback.CallsPerSecond,
                    ExclusiveMsPerSecond = callback.ExclusiveMsPerSecond,
                    SpikeCount = callback.SpikeCount,
                    MaxSpikeExclusiveMs = callback.MaxSpikeExclusiveMs
                });
            }
        }

        return byProvider
            .Select(pair =>
            {
                var rows = pair.Value
                    .OrderByDescending(x => x.ExclusiveMsPerSecond)
                    .ThenByDescending(x => x.CallsPerSecond)
                    .ToArray();

                return new SharedProviderOpportunity
                {
                    Provider = pair.Key,
                    Category = SharedProviderCategory(pair.Key),
                    MeasuredOwnerCount = rows
                        .Select(x => x.Owner)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    MeasuredCallbackCount = rows.Length,
                    SourceOccurrences = rows.Sum(x => x.SourceOccurrences),
                    SourceRecognizedOccurrences = rows.Sum(x => x.SourceRecognizedOccurrences),
                    SourceUnresolvedOccurrences = rows.Sum(x => x.SourceUnresolvedOccurrences),
                    CallbacksWithRepeatedSourceReads =
                        rows.Count(x => x.SourceOccurrences > 1),
                    DeepObservedCallbackCount = rows.Count(x => x.DeepObserved),
                    DeepSampledCalls = rows.Sum(x => x.DeepSampledCalls),
                    DeepRepeatedSameInvocationCount =
                        rows.Sum(x => x.DeepRepeatedSameInvocationCount),
                    DeepMultiCallsiteSampleCount =
                        rows.Sum(x => x.DeepMultiCallsiteSampleCount),
                    AffectedCallbackWorkMsPerSecond =
                        rows.Sum(x => x.ExclusiveMsPerSecond),
                    AffectedCallbackCallsPerSecond =
                        rows.Sum(x => x.CallsPerSecond),
                    AffectedSpikeCount = rows.Sum(x => x.SpikeCount),
                    MaxAffectedSpikeExclusiveMs = rows
                        .Select(x => x.MaxSpikeExclusiveMs)
                        .DefaultIfEmpty(0)
                        .Max(),
                    Owners = rows
                        .Select(x => x.Owner)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    Callbacks = rows
                };
            })
            .OrderByDescending(x => x.AffectedCallbackWorkMsPerSecond)
            .ThenByDescending(x => x.SourceOccurrences)
            .ThenBy(x => x.Provider, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static List<SharedProviderSourceMatch> DetectSharedProviderSourceMatches(
        ResolvedSource source)
    {
        var result = new List<SharedProviderSourceMatch>();
        var text = source.CallbackText;

        foreach (var definition in SharedProviderDefinitions)
        {
            if (!definition.RequiresPlayerReceiver)
            {
                foreach (Match match in definition.SourceRegex.Matches(text))
                {
                    result.Add(new SharedProviderSourceMatch
                    {
                        Provider = definition.Provider,
                        Line = SharedProviderLine(source, match.Index),
                        SourceRecognized = true,
                        Proof = "EXACT_KNOWN_GETTER"
                    });
                }
                continue;
            }

            var playerVariables = Regex.Matches(
                    text,
                    @"(?m)\b(?:local\s+)?(?<name>[A-Za-z_]\w*)\s*=\s*Game\s*\.\s*GetPlayer\s*\(\s*\)")
                .Cast<Match>()
                .Select(x => x.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            var derivedRegex = new Regex(
                @"\b(?<receiver>[A-Za-z_]\w*)\s*:\s*" +
                Regex.Escape(definition.DeepFunctionNames[0]) +
                @"\s*\(\s*\)",
                RegexOptions.CultureInvariant);

            foreach (Match match in derivedRegex.Matches(text))
            {
                var receiver = match.Groups["receiver"].Value;
                var proven = playerVariables.Contains(receiver);
                result.Add(new SharedProviderSourceMatch
                {
                    Provider = definition.Provider,
                    Line = SharedProviderLine(source, match.Index),
                    SourceRecognized = proven,
                    Proof = proven
                        ? "LOCAL_RECEIVER_FROM_GAME_GETPLAYER"
                        : "RECEIVER_NOT_PROVEN_AS_CURRENT_PLAYER"
                });
            }
        }

        foreach (Match match in GenericSharedSystemGetterSourceRegex.Matches(text))
        {
            var getter = match.Groups["getter"].Value;
            var provider = SharedProviderKeyForSystemGetter(getter);

            // Explicit definitions already emitted this exact family above.
            if (SharedProviderDefinitions.Any(x =>
                    x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(new SharedProviderSourceMatch
            {
                Provider = provider,
                Line = SharedProviderLine(source, match.Index),
                SourceRecognized = true,
                Proof = "EXACT_DISCOVERED_SYSTEM_GETTER"
            });
        }

        return result;
    }

    private static int SharedProviderLine(ResolvedSource source, int callbackOffset)
    {
        var baseLine = source.LineStart.GetValueOrDefault(1);
        var text = source.CallbackText;
        var limit = Math.Clamp(callbackOffset, 0, text.Length);
        var lineOffset = 0;
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n') lineOffset++;
        return Math.Max(1, baseLine + lineOffset);
    }

    private static IReadOnlyDictionary<long, SharedProviderDeepCallbackEvidence>
        ReadSharedProviderDeepEvidence(JsonElement root)
    {
        var result = new Dictionary<long, SharedProviderDeepCallbackEvidence>();

        if (!root.TryGetProperty("optimizerEvidence", out var optimizerEvidence) ||
            optimizerEvidence.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var row in optimizerEvidence.EnumerateArray())
        {
            var registrationId = JsonNullableLong(row, "registrationId");
            if (registrationId is null || registrationId <= 0)
                continue;

            if (!row.TryGetProperty("deep", out var deep) ||
                deep.ValueKind != JsonValueKind.Object)
                continue;

            JsonElement providerCallees;
            if (deep.TryGetProperty("sharedProviderCallees", out var untruncated) &&
                untruncated.ValueKind == JsonValueKind.Array)
            {
                providerCallees = untruncated;
            }
            else if (deep.TryGetProperty("hotCallees", out var legacyHotCallees) &&
                     legacyHotCallees.ValueKind == JsonValueKind.Array)
            {
                // Backward compatibility for captures produced before the dedicated
                // untruncated provider evidence field existed.
                providerCallees = legacyHotCallees;
            }
            else
            {
                continue;
            }

            var byProvider = new Dictionary<string, SharedProviderDeepEvidence>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var callee in providerCallees.EnumerateArray())
            {
                var functionName = JsonString(callee, "functionName");
                var functionKey = JsonString(callee, "childFunctionKey");
                var provider = SharedProviderForDeepFunction(functionName, functionKey);
                if (provider is null)
                    continue;

                if (!byProvider.TryGetValue(provider, out var aggregate))
                {
                    aggregate = new SharedProviderDeepEvidence();
                    byProvider[provider] = aggregate;
                }

                aggregate.SampledCalls += (long)JsonDouble(callee, "sampledCalls");
                aggregate.RepeatedSameInvocationCount +=
                    (long)JsonDouble(callee, "repeatedInSampleCount");
                aggregate.MultiCallsiteSampleCount +=
                    (long)JsonDouble(callee, "multiCallsiteSampleCount");
            }

            if (byProvider.Count > 0)
            {
                result[registrationId.Value] = new SharedProviderDeepCallbackEvidence
                {
                    ByProvider = byProvider
                };
            }
        }

        return result;
    }

    private static string? SharedProviderForDeepFunction(
        string functionName,
        string functionKey)
    {
        foreach (var definition in SharedProviderDefinitions)
        {
            if (definition.DeepFunctionNames.Any(name =>
                    name.Equals(functionName, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(functionKey) &&
                     functionKey.Contains(name, StringComparison.OrdinalIgnoreCase))))
                return definition.Provider;
        }

        if (TryFindSharedSystemGetter(functionName, functionKey, out var getter))
            return SharedProviderKeyForSystemGetter(getter);

        return null;
    }

    private static bool TryFindSharedSystemGetter(
        string functionName,
        string functionKey,
        out string getter)
    {
        var direct = GenericSharedSystemGetterNameRegex.Match(functionName ?? "");
        if (direct.Success)
        {
            getter = direct.Groups["getter"].Value;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(functionKey))
        {
            var keyed = GenericSharedSystemGetterNameRegex.Match(functionKey);
            if (keyed.Success)
            {
                getter = keyed.Groups["getter"].Value;
                return true;
            }
        }

        getter = "";
        return false;
    }

    private static string SharedProviderKeyForSystemGetter(string getter)
    {
        var stem = getter;
        if (stem.StartsWith("Get", StringComparison.OrdinalIgnoreCase))
            stem = stem[3..];
        if (stem.EndsWith("System", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^6];

        var acronymSplit = Regex.Replace(
            stem,
            @"([A-Z]+)([A-Z][a-z])",
            "$1_$2",
            RegexOptions.CultureInvariant);
        var wordSplit = Regex.Replace(
            acronymSplit,
            @"([a-z0-9])([A-Z])",
            "$1_$2",
            RegexOptions.CultureInvariant);

        return wordSplit.ToUpperInvariant() + "_SYSTEM";
    }

    private static string SharedProviderCategory(string provider)
    {
        var explicitDefinition = SharedProviderDefinitions.FirstOrDefault(x =>
            x.Provider.Equals(provider, StringComparison.OrdinalIgnoreCase));

        return explicitDefinition?.Category ?? "SYSTEM_HANDLE_CANDIDATE";
    }

    private static DormancyEvidence ResolveDormancyEvidence(
        CallbackMetric callback,
        SourceEvidence? sourceEvidence,
        LiveSourceIndex sourceIndex)
    {
        if (sourceEvidence is null)
            return DormancyEvidence.Unknown("Current deployed callback source was not resolved.");

        var source = sourceIndex.Resolve(callback);
        if (source is null)
            return DormancyEvidence.Unknown("Current deployed callback source was not resolved.");

        var text = source.CallbackText;
        var full = source.FullText;
        var evidence = new List<string>();
        var blockers = new List<string>();

        string[] MatchTokens(string input, params string[] tokens) =>
            tokens.Where(token =>
                    Regex.IsMatch(
                        input,
                        token,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var sensitive = MatchTokens(
            text,
            @"\bAIAction\b",
            @"\bAIBehavior\b",
            @"\bCombatState\b",
            @"\bNPCPuppet\b",
            @"\bCameraSystem\b",
            @"\bGetActiveCameraData\b",
            @"\bFPP\b",
            @"\bTPP\b");

        var active = MatchTokens(
            full,
            @"\b(?:is)?active\b",
            @"\benabled\b",
            @"\brunning\b",
            @"\bscanning\b",
            @"\bsession\b",
            @"\bhandActive\b",
            @"\binWorkspot\b",
            @"\braceActive\b",
            @"\binGame\b",
            @"\bcurrentWorkspot\b",
            @"\bcurrentTarget\b",
            @"\bhubShown\b");

        var wake = MatchTokens(
            full,
            @"registerHotkey\s*\(",
            @"registerInput\s*\(",
            @"registerForEvent\s*\(\s*['""]onInit",
            @"\bOnAction\b",
            @"\bInteract",
            @"\bStart\w*\s*\(",
            @"\bOpen\w*\s*\(",
            @"\bToggle\w*\s*\(");

        var discovery = MatchTokens(
            text,
            @"\bVector4\.Distance\b",
            @"\bGetWorldPosition\b",
            @"\bGetComponentClosestToCrosshair\b",
            @"\bGetTargetingSystem\b",
            @"\bFindEntityByID\b",
            @"\bmappin\b",
            @"\bproximity\b",
            @"\bnearby\b");

        var background = MatchTokens(
            text,
            @"\bqueue\b",
            @"\bpending\b",
            @"\bCron\.Update\b",
            @"\bprocess\w*Queue\b",
            @"\bupdate\w*Queue\b",
            @"\bSMS\b",
            @"\bsave\w*\b",
            @"\bflush\w*\b");

        var explicitEarlyGate = Regex.Match(
            text,
            @"(?m)^\s*if\s+not\s+(?<gate>[A-Za-z_][\w.\[\]:()]*)\s+then\s+return\s+end\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var explicitPositiveGate = Regex.Match(
            text,
            @"(?m)^\s*if\s+(?<gate>[A-Za-z_][\w.\[\]:()]*)\s+then\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var authorDiscoveryCadenceProven = false;
        var authorDiscoveryIntervalSeconds = 0.0;
        var authorDiscoveryAccumulator = "";
        var authorDiscoveryGate = "";
        var discoveryRegionSelfContained = false;
        var authorDiscoveryBlocker = "";

        if (TryResolveAuthorDiscoveryCadence(
                source,
                out var discoveryCadence,
                out authorDiscoveryBlocker))
        {
            authorDiscoveryCadenceProven = true;
            authorDiscoveryIntervalSeconds = discoveryCadence.IntervalSeconds;
            authorDiscoveryAccumulator = discoveryCadence.Accumulator;
            authorDiscoveryGate = discoveryCadence.ActiveGate;
            discoveryRegionSelfContained = discoveryCadence.RegionSelfContained;
            evidence.Add(
                $"Author-written inactive discovery cadence was proven at {discoveryCadence.IntervalSeconds:0.######} s " +
                $"using '{discoveryCadence.Accumulator}'.");
        }

        if (sensitive.Length > 0)
        {
            evidence.Add("Latency-sensitive combat/camera/NPC source signals were found in the measured callback.");
            return new DormancyEvidence
            {
                Class = "NEVER_GATE",
                Confidence = 0.92,
                EvidenceOnly = true,
                ActiveSignals = active,
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                AuthorDiscoveryCadenceProven = authorDiscoveryCadenceProven,
                AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                AuthorDiscoveryGate = authorDiscoveryGate,
                DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                Evidence = evidence.ToArray(),
                Blockers = new[] { "Dormancy transforms are prohibited for this callback class; structural rewrites remain allowed." }
            };
        }

        if (background.Length > 0 && active.Length == 0)
        {
            evidence.Add("Queue/pending/background-service work is present without a proven single active-session state.");
            return new DormancyEvidence
            {
                Class = "BACKGROUND",
                Confidence = 0.78,
                EvidenceOnly = true,
                ActiveSignals = active,
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                AuthorDiscoveryCadenceProven = authorDiscoveryCadenceProven,
                AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                AuthorDiscoveryGate = authorDiscoveryGate,
                DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                Evidence = evidence.ToArray(),
                Blockers = new[] { "Whole-callback sleep is not proven; inspect queue-empty or no-pending-work guards instead." }
            };
        }

        var gate = explicitEarlyGate.Success
            ? explicitEarlyGate.Groups["gate"].Value
            : explicitPositiveGate.Success
                ? explicitPositiveGate.Groups["gate"].Value
                : "";

        var stateWriterSignals = Array.Empty<string>();
        var completeWakePathProven = false;
        if (!string.IsNullOrWhiteSpace(gate))
        {
            var callbackIndex = full.IndexOf(text, StringComparison.Ordinal);
            var outside = callbackIndex >= 0
                ? full.Remove(callbackIndex, text.Length)
                : full;

            var escapedGate = Regex.Escape(gate);
            stateWriterSignals = Regex.Matches(
                    outside,
                    @"(?m)^\s*" + escapedGate + @"\s*=\s*[^=].*$",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(x => x.Value.Trim())
                .Distinct(StringComparer.Ordinal)
                .Take(8)
                .ToArray();

            completeWakePathProven =
                stateWriterSignals.Length > 0 &&
                wake.Length > 0 &&
                background.Length == 0 &&
                sensitive.Length == 0;
        }

        if (authorDiscoveryCadenceProven)
        {
            evidence.Add(
                discoveryRegionSelfContained
                    ? "The author-gated discovery region is source-contained and may be eligible for a future finite extraction recipe."
                    : "The author-gated discovery region depends on callback-local/shared state and remains evidence-only.");
            return new DormancyEvidence
            {
                Class = "DISCOVERY_DORMANT",
                Confidence = discoveryRegionSelfContained ? 0.94 : 0.90,
                EvidenceOnly = true,
                ActiveSignals = active,
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                StateWriterSignals = stateWriterSignals,
                CompleteWakePathProven = false,
                AuthorDiscoveryCadenceProven = true,
                AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                AuthorDiscoveryGate = authorDiscoveryGate,
                DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                Evidence = evidence.ToArray(),
                Blockers = discoveryRegionSelfContained
                    ? new[] { "Evidence-only phase: finite discovery extraction has not yet been authorized for generation." }
                    : new[] { "Discovery region captures callback-local/shared state; preserve it until an extraction-safe dependency proof exists." }
            };
        }

        if (!string.IsNullOrWhiteSpace(gate) && wake.Length > 0)
        {
            evidence.Add($"Current source exposes an explicit activity gate '{gate}' and independent wake/input signals.");
            if (discovery.Length == 0)
            {
                return new DormancyEvidence
                {
                    Class = "HARD_DORMANT",
                    Confidence = 0.88,
                    EvidenceOnly = true,
                    ActiveSignals = active.Concat(new[] { gate }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    WakeSignals = wake,
                    DiscoverySignals = discovery,
                    BackgroundSignals = background,
                    SensitiveSignals = sensitive,
                    AuthorDiscoveryCadenceProven = authorDiscoveryCadenceProven,
                    AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                    AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                    AuthorDiscoveryGate = authorDiscoveryGate,
                    DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                    AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                    StateWriterSignals = stateWriterSignals,
                    CompleteWakePathProven = completeWakePathProven,
                    Evidence = evidence.Concat(completeWakePathProven
                        ? new[] { "The activity gate is written outside the hot callback and an independent wake/input registration exists." }
                        : Array.Empty<string>()).ToArray(),
                    Blockers = completeWakePathProven
                        ? new[] { "Evidence-only phase: complete wake-path evidence is recorded but does not yet authorize generation." }
                        : new[] { "Evidence classification only: a complete state-writer/wake-path proof is still required before generation." }
                };
            }

            evidence.Add("The same callback also contains world/discovery work that may be required while inactive.");
            return new DormancyEvidence
            {
                Class = "DISCOVERY_DORMANT",
                Confidence = 0.84,
                EvidenceOnly = true,
                ActiveSignals = active.Concat(new[] { gate }).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                StateWriterSignals = stateWriterSignals,
                CompleteWakePathProven = false,
                Evidence = evidence.ToArray(),
                Blockers = new[] { "Evidence classification only: inactive discovery work must be isolated before generation." }
            };
        }

        if (active.Length > 0 && discovery.Length > 0)
        {
            evidence.Add("Explicit activity/session vocabulary and world/discovery work coexist in current source.");
            return new DormancyEvidence
            {
                Class = "DISCOVERY_DORMANT",
                Confidence = 0.68,
                EvidenceOnly = true,
                ActiveSignals = active,
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                AuthorDiscoveryCadenceProven = authorDiscoveryCadenceProven,
                AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                AuthorDiscoveryGate = authorDiscoveryGate,
                DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                Evidence = evidence.ToArray(),
                Blockers = new[] { "No complete activity gate boundary was proven in the measured callback." }
            };
        }

        if (active.Length > 0 && wake.Length > 0)
        {
            evidence.Add("Activity/session state and independent wake/input signals are present, but the measured callback boundary is not yet proven.");
            return new DormancyEvidence
            {
                Class = "HARD_DORMANT",
                Confidence = 0.62,
                EvidenceOnly = true,
                ActiveSignals = active,
                WakeSignals = wake,
                DiscoverySignals = discovery,
                BackgroundSignals = background,
                SensitiveSignals = sensitive,
                AuthorDiscoveryCadenceProven = authorDiscoveryCadenceProven,
                AuthorDiscoveryIntervalSeconds = authorDiscoveryIntervalSeconds,
                AuthorDiscoveryAccumulator = authorDiscoveryAccumulator,
                AuthorDiscoveryGate = authorDiscoveryGate,
                DiscoveryRegionSelfContained = discoveryRegionSelfContained,
                AuthorDiscoveryBlocker = authorDiscoveryBlocker,
                Evidence = evidence.ToArray(),
                Blockers = new[] { "No complete source-proven dormant boundary was found." }
            };
        }

        blockers.Add("Source does not yet prove a useful dormant/background classification.");
        return new DormancyEvidence
        {
            Class = "UNKNOWN",
            Confidence = 0.0,
            EvidenceOnly = true,
            ActiveSignals = active,
            WakeSignals = wake,
            DiscoverySignals = discovery,
            BackgroundSignals = background,
            SensitiveSignals = sensitive,
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray()
        };
    }

    private static bool TryResolveAuthorDiscoveryCadence(
        ResolvedSource source,
        out AuthorDiscoveryCadenceResolution resolution,
        out string blocker)
    {
        resolution = new AuthorDiscoveryCadenceResolution();
        blocker = "NO_MATCH";

        var text = source.CallbackText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        if (Regex.IsMatch(
                text,
                @"\b(?:AIAction|AIBehavior|CombatState|NPCPuppet|CameraSystem|GetActiveCameraData|\bFPP\b|\bTPP\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            blocker = "SENSITIVE_CALLBACK";
            return false;
        }

        var opening = Regex.Match(
            text,
            @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\(\s*(?<delta>[A-Za-z_]\w*)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!opening.Success)
        {
            blocker = "NO_DIRECT_ONUPDATE";
            return false;
        }

        var delta = opening.Groups["delta"].Value;
        var body = text[(opening.Index + opening.Length)..];
        var lines = body.Split('\n');

        // Finite V1 proof: explicit "if not ACTIVE then" block.
        var sawInactiveBlock = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var inactive = Regex.Match(
                lines[i],
                @"^(?<indent>\s*)if\s+not\s+(?<active>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!inactive.Success)
                continue;

            sawInactiveBlock = true;
            blocker = "INACTIVE_BLOCK_UNRESOLVED";
            var inactiveIndent = inactive.Groups["indent"].Value.Length;
            var inactiveEnd = FindSameIndentEnd(lines, i, inactiveIndent);
            if (inactiveEnd <= i)
            {
                blocker = "INACTIVE_BLOCK_BOUNDARY";
                continue;
            }

            var inactiveLines = lines.Skip(i + 1).Take(inactiveEnd - i - 1).ToArray();

            // Accumulator must advance by callback delta inside inactive state.
            // Match line-by-line so nested inactive blocks and formatting do not
            // depend on a multi-line backreference.
            var accumulator = "";
            var incrementLine = -1;
            for (var j = 0; j < inactiveLines.Length; j++)
            {
                var assignment = Regex.Match(
                    inactiveLines[j],
                    @"^\s*(?<lhs>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*=\s*(?<rhs>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\+\s*" +
                    Regex.Escape(delta) + @"\s*;?\s*$",
                    RegexOptions.CultureInvariant);
                if (assignment.Success &&
                    assignment.Groups["lhs"].Value.Equals(
                        assignment.Groups["rhs"].Value,
                        StringComparison.Ordinal))
                {
                    accumulator = assignment.Groups["lhs"].Value;
                    incrementLine = j;
                    break;
                }

                var plusEquals = Regex.Match(
                    inactiveLines[j],
                    @"^\s*(?<acc>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\+=\s*" +
                    Regex.Escape(delta) + @"\s*;?\s*$",
                    RegexOptions.CultureInvariant);
                if (plusEquals.Success)
                {
                    accumulator = plusEquals.Groups["acc"].Value;
                    incrementLine = j;
                    break;
                }
            }

            if (incrementLine < 0 || string.IsNullOrWhiteSpace(accumulator))
            {
                blocker = "NO_TIMER_INCREMENT";
                continue;
            }

            var thresholdLine = -1;
            var seconds = 0.0;
            for (var j = incrementLine + 1; j < inactiveLines.Length; j++)
            {
                var threshold = Regex.Match(
                    inactiveLines[j],
                    @"^\s*if\s+" + Regex.Escape(accumulator) +
                    @"\s*(?:>=|>)\s*(?<seconds>\d+(?:\.\d+)?)\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!threshold.Success)
                    continue;

                if (!double.TryParse(
                        threshold.Groups["seconds"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out seconds) ||
                    seconds <= 0 ||
                    !double.IsFinite(seconds))
                    continue;

                thresholdLine = j;
                break;
            }

            if (thresholdLine < 0)
            {
                blocker = "NO_FIXED_THRESHOLD";
                continue;
            }

            var thresholdIndent =
                inactiveLines[thresholdLine].Length -
                inactiveLines[thresholdLine].TrimStart().Length;
            var thresholdEnd = FindSameIndentEnd(inactiveLines, thresholdLine, thresholdIndent);
            if (thresholdEnd <= thresholdLine)
            {
                blocker = "THRESHOLD_BLOCK_BOUNDARY";
                continue;
            }

            var gatedRegion = string.Join(
                "\n",
                inactiveLines.Skip(thresholdLine + 1).Take(thresholdEnd - thresholdLine - 1));

            if (!Regex.IsMatch(
                    gatedRegion,
                    @"\b" + Regex.Escape(accumulator) + @"\s*=\s*0(?:\.0+)?\b",
                    RegexOptions.CultureInvariant))
            {
                blocker = "NO_TIMER_RESET";
                continue;
            }

            if (Regex.IsMatch(
                    gatedRegion,
                    @"\b" + Regex.Escape(delta) + @"\b",
                    RegexOptions.CultureInvariant))
            {
                blocker = "DISCOVERY_REGION_USES_DELTA";
                continue;
            }

            // Require actual discovery/world-query work, not an arbitrary timer.
            if (!Regex.IsMatch(
                    gatedRegion,
                    @"\b(?:Vector4\.Distance|GetWorldPosition|GetTargetingSystem|GetComponentClosestToCrosshair|FindEntityByID|mappin|nearby|proximity)\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                blocker = "NO_DISCOVERY_WORK";
                continue;
            }

            var full = source.FullText
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var callbackIndex = full.IndexOf(text, StringComparison.Ordinal);
            if (callbackIndex < 0)
            {
                blocker = "CALLBACK_NOT_ISOLATED";
                continue;
            }
            var outside = full.Remove(callbackIndex, text.Length);
            var activeGate = inactive.Groups["active"].Value;
            if (!Regex.IsMatch(
                    outside,
                    @"(?m)^\s*" + Regex.Escape(activeGate) + @"\s*=\s*(?:true|false|[^=].*)$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                blocker = "NO_EXTERNAL_ACTIVE_WRITER";
                continue;
            }

            // A region is extraction-safe only when identifiers used by the
            // discovery block are not locals declared earlier in the callback.
            var prefix = body[..Math.Max(0, body.IndexOf(lines[i], StringComparison.Ordinal))];
            var earlierLocals = Regex.Matches(
                    prefix,
                    @"\blocal\s+(?<name>[A-Za-z_]\w*)",
                    RegexOptions.CultureInvariant)
                .Cast<Match>()
                .Select(x => x.Groups["name"].Value)
                .ToHashSet(StringComparer.Ordinal);

            var capturedEarlierLocals = earlierLocals
                .Where(name => Regex.IsMatch(
                    gatedRegion,
                    @"\b" + Regex.Escape(name) + @"\b",
                    RegexOptions.CultureInvariant))
                .ToArray();

            blocker = "";
            resolution = new AuthorDiscoveryCadenceResolution
            {
                ActiveGate = activeGate,
                Accumulator = accumulator,
                IntervalSeconds = seconds,
                RegionSelfContained = capturedEarlierLocals.Length == 0,
                CapturedEarlierLocals = capturedEarlierLocals
            };
            return true;
        }

        if (!sawInactiveBlock)
            blocker = "NO_INACTIVE_BLOCK";
        return false;
    }

    private static GenericResolution ResolveGeneric(
        CallbackMetric callback,
        LiveSourceIndex sourceIndex,
        IReadOnlyDictionary<string, CadenceDecision> cadence)
    {
        var family = ClassifyCallbackFamily(callback.Kind, callback.Target);
        var source = sourceIndex.Resolve(callback);
        var sourceEvidence = source is null
            ? null
            : new SourceEvidence
            {
                RelativeFile = source.RelativeFile,
                Sha256 = source.Sha256,
                LineStart = source.LineStart,
                LineEnd = source.LineEnd,
                MatchMode = source.MatchMode
            };

        if (family == "ONACTION")
            return ResolveOnAction(callback, source, sourceEvidence, sourceIndex);

        if (family == "ONUPDATE")
            return ResolveOnUpdate(callback, source, sourceEvidence, cadence);

        // Structural callback-local rewrites do not depend on the callback
        // delivery family. Unsupported cadence/routing families should still
        // receive safe hotpath analysis instead of terminating immediately.
        if (source is not null &&
            TryResolveStructuralHotpath(source, callback, out var structural))
        {
            return new GenericResolution
            {
                Status = "ANALYSIS_ONLY",
                Automatable = false,
                Pattern = "STRUCTURAL_HOTPATH_EVIDENCE",
                RecipeFamilies = Array.Empty<string>(),
                Facts = new
                {
                    structuralHotpath = true,
                    identicalExpressions = structural.IdenticalExpressions,
                    literalConstructors = structural.LiteralConstructors,
                    staticLiteralTables = structural.StaticLiteralTables,
                    estimatedCallbackPaybackPct = structural.EstimatedCallbackPaybackPct,
                    estimatedGlobalPaybackPct = structural.EstimatedGlobalPaybackPct
                },
                Evidence = structural.Evidence,
                Blockers = new[]
                {
                    "Cross-mod structural rewrites are analysis-only. A semantic per-mod rule must authorize any aggressive rewrite."
                },
                Source = sourceEvidence
            };
        }

        return new GenericResolution
        {
            Status = "NO_GENERIC_RESOLVER",
            Automatable = false,
            Pattern = "UNSUPPORTED_CALLBACK_FAMILY",
            RecipeFamilies = Array.Empty<string>(),
            Evidence = Array.Empty<string>(),
            Blockers = new[] { "No family-specific recipe or material source-proven structural hotpath rewrite was found." },
            Source = sourceEvidence
        };
    }

    private static GenericResolution ResolveOnUpdate(
        CallbackMetric callback,
        ResolvedSource? source,
        SourceEvidence? sourceEvidence,
        IReadOnlyDictionary<string, CadenceDecision> cadence)
    {
        var recipes = new List<string>();
        var evidence = new List<string>();
        var blockers = new List<string>();
        object? facts = null;
        StructuralHotpathResolution? structuralResolution = null;
        HardDormantGuardResolution? hardDormantResolution = null;
        AuthorDiscoveryCadenceResolution? discoveryScheduleResolution = null;
        var effectiveSourceEvidence = sourceEvidence;

        var rawDirectOnUpdate = source is not null &&
            Regex.IsMatch(
                source.CallbackText,
                @"\b(?:registerForEvent|registerRuntimeEvent)\s*\(\s*['""]onUpdate['""]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var generatedRegistrarMatch = source is null
            ? Match.Empty
            : Regex.Match(
                source.CallbackText,
                @"\b(?<registrar>__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var generatedRegistrarPresent = generatedRegistrarMatch.Success;
        var alreadyFrameConsolidated =
            source is not null &&
            generatedRegistrarPresent &&
            HasGeneratedFrameRegistrar(
                source.FullText,
                generatedRegistrarMatch.Groups["registrar"].Value);

        // Semantic analyzers may inspect either raw or already-consolidated
        // onUpdate source. Only a raw registrar authorizes the generic frame
        // transform; the generated registrar is an already-satisfied state.
        var directOnUpdate = rawDirectOnUpdate || generatedRegistrarPresent;

        if (rawDirectOnUpdate)
        {
            recipes.Add("FRAME_DISPATCH_CONSOLIDATION");
            evidence.Add("Raw direct onUpdate registration is present in the current deployed source.");
        }
        else if (alreadyFrameConsolidated)
        {
            evidence.Add("Frame dispatch is already consolidated by a source-proven G-CET registrar; no frame transform is required.");
        }
        else if (generatedRegistrarPresent)
        {
            blockers.Add("A G-CET frame registrar token is present, but its generated helper could not be proven in the current file. Leave the partial state untouched.");
        }
        else
        {
            blockers.Add("Current deployed source could not prove the direct onUpdate registration.");
        }

        var cadenceKey = CadenceKey(callback.Owner, callback.Kind, callback.Target);
        cadence.TryGetValue(cadenceKey, out var cadenceDecision);

        var cadenceBlocker = "";

        // Highest-confidence cadence recipe: the callback itself contains only
        // author-written fixed timer accumulators and their gated work. Runtime
        // data decides whether eliminating the frame-rate entry is materially
        // useful; source decides the cadence. No owner/mod-name knowledge is
        // consulted.
        if (source is not null &&
            directOnUpdate &&
            TryResolveWholeCallbackAuthorCadence(
                source,
                callback,
                cadenceDecision,
                out var authorCadence,
                out cadenceBlocker))
        {
            recipes.Add("AUTHOR_CADENCE_WHOLE_CALLBACK");

            if (source is not null &&
                cadenceDecision is not null &&
                cadenceDecision.RegistrationLine > 0 &&
                cadenceDecision.CallbackBodyEndLine >= cadenceDecision.RegistrationLine)
            {
                effectiveSourceEvidence = new SourceEvidence
                {
                    RelativeFile = source.RelativeFile,
                    Sha256 = source.Sha256,
                    LineStart = cadenceDecision.RegistrationLine,
                    LineEnd = cadenceDecision.CallbackBodyEndLine,
                    MatchMode = "cadence-source-confirmed"
                };
            }

            evidence.Add(
                $"Current source proves whole-callback author cadence at {authorCadence.BaseIntervalSeconds:0.######} s " +
                $"with {authorCadence.TimerIntervalsSeconds.Length} fixed author timer(s).");
            evidence.Add(
                $"Measured callback entry rate is {callback.CallsPerSecond:0.###}/s versus " +
                $"{authorCadence.ExpectedCallsPerSecond:0.###}/s at the preserved author base cadence.");
            evidence.Add(
                $"Estimated avoidable polling work is {authorCadence.EstimatedAvoidablePollingMsPerSecond:0.######} ms/s, " +
                $"{authorCadence.EstimatedCallbackPaybackPct:0.###}% of this callback and " +
                $"{authorCadence.EstimatedGlobalPaybackPct:0.###}% of measured CET work.");
            facts = new
            {
                authorCadenceWholeCallback = true,
                baseIntervalSeconds = authorCadence.BaseIntervalSeconds,
                timerIntervalsSeconds = authorCadence.TimerIntervalsSeconds,
                accumulatorVariables = authorCadence.AccumulatorVariables,
                expectedCallsPerSecond = authorCadence.ExpectedCallsPerSecond,
                runtimeEntryReductionFactor = authorCadence.RuntimeEntryReductionFactor,
                estimatedAvoidablePollingMsPerSecond = authorCadence.EstimatedAvoidablePollingMsPerSecond,
                estimatedCallbackPaybackPct = authorCadence.EstimatedCallbackPaybackPct,
                estimatedGlobalPaybackPct = authorCadence.EstimatedGlobalPaybackPct,
                deltaParameter = authorCadence.DeltaParameter
            };
        }
        else if (!string.IsNullOrWhiteSpace(cadenceBlocker))
        {
            blockers.Add(cadenceBlocker);
        }

        // Structural hot-path recipes are source-proven and callback-local.
        // They never change event cadence or callback delivery. Cost is the
        // first gate: do not rewrite cheap callbacks just because the source is ugly.
        if (!recipes.Contains("AUTHOR_CADENCE_WHOLE_CALLBACK", StringComparer.OrdinalIgnoreCase) &&
            source is not null &&
            TryResolveStructuralHotpath(source, callback, out var structural))
        {
            structuralResolution = structural;
            recipes.Add("STRUCTURAL_HOTPATH_REWRITE");
            evidence.AddRange(structural.Evidence);
        }

        if (!recipes.Contains("AUTHOR_CADENCE_WHOLE_CALLBACK", StringComparer.OrdinalIgnoreCase) &&
            source is not null &&
            directOnUpdate &&
            TryResolveHardDormantGuardHoist(source, callback, out var hardDormant))
        {
            hardDormantResolution = hardDormant;
            recipes.Add("HARD_DORMANT_GUARD_HOIST");
            evidence.AddRange(hardDormant.Evidence);
        }

        if (!recipes.Contains("AUTHOR_CADENCE_WHOLE_CALLBACK", StringComparer.OrdinalIgnoreCase) &&
            source is not null &&
            directOnUpdate &&
            callback.ExclusiveMsPerSecond >= 7.0 &&
            callback.GlobalWorkSharePct >= 1.0 &&
            TryResolveAuthorDiscoveryCadence(
                source,
                out var discoveryCadence,
                out var discoveryScheduleBlocker) &&
            discoveryCadence.RegionSelfContained &&
            Regex.IsMatch(
                source.CallbackText,
                @"\bif\s+" + Regex.Escape(discoveryCadence.ActiveGate) + @"\s+then\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            discoveryScheduleResolution = discoveryCadence;
            recipes.Add("AUTHOR_DISCOVERY_DORMANT_SCHEDULE");
            evidence.Add(
                $"Author-written inactive discovery cadence at {discoveryCadence.IntervalSeconds:0.######} s " +
                "is self-contained and can be moved off the frame loop without changing the active branch.");
        }

        if (!recipes.Contains("AUTHOR_CADENCE_WHOLE_CALLBACK", StringComparer.OrdinalIgnoreCase) &&
            (structuralResolution is not null ||
             hardDormantResolution is not null ||
             discoveryScheduleResolution is not null))
        {
            facts = new
            {
                structuralHotpath = structuralResolution is not null,
                identicalExpressions = structuralResolution?.IdenticalExpressions ?? Array.Empty<StructuralExpression>(),
                literalConstructors = structuralResolution?.LiteralConstructors ?? Array.Empty<StructuralExpression>(),
                staticLiteralTables = structuralResolution?.StaticLiteralTables ?? Array.Empty<StaticLiteralTable>(),
                estimatedCallbackPaybackPct = structuralResolution?.EstimatedCallbackPaybackPct ?? 0,
                estimatedGlobalPaybackPct = structuralResolution?.EstimatedGlobalPaybackPct ?? 0,
                hardDormantGuardHoist = hardDormantResolution is not null,
                hardDormantGateExpression = hardDormantResolution?.GateExpression ?? "",
                hardDormantPreGuardReadCount = hardDormantResolution?.PreGuardReadCount ?? 0,
                hardDormantStateWriters = hardDormantResolution?.StateWriters ?? Array.Empty<string>(),
                hardDormantWakeSignals = hardDormantResolution?.WakeSignals ?? Array.Empty<string>(),
                authorDiscoveryDormantSchedule = discoveryScheduleResolution is not null,
                authorDiscoveryIntervalSeconds = discoveryScheduleResolution?.IntervalSeconds ?? 0,
                authorDiscoveryAccumulator = discoveryScheduleResolution?.Accumulator ?? "",
                authorDiscoveryGate = discoveryScheduleResolution?.ActiveGate ?? ""
            };
        }

        // Keep the broader cadence classifier visible as evidence, but do not
        // let an inferred cadence authorize generation. Only finite generator
        // recipes above are automatable.
        if (cadenceDecision is not null &&
            cadenceDecision.TransformCandidate &&
            !cadenceDecision.Group.Equals("LEAVE_ALONE", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add($"Cadence subset source-classified {cadenceDecision.Group}; generation still requires a finite author-cadence recipe.");
        }

        var automaticRecipes = recipes
            .Where(x => x.Equals(
                "FRAME_DISPATCH_CONSOLIDATION",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var parkedRecipes = recipes
            .Where(x => !x.Equals(
                "FRAME_DISPATCH_CONSOLIDATION",
                StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (parkedRecipes.Length > 0)
        {
            blockers.Add(
                "Analysis-only semantic recipe(s) parked from generic AUTO: " +
                string.Join(", ", parkedRecipes) +
                ". Re-authorize only through a source-validated per-mod semantic rule.");
        }

        var automatable = automaticRecipes.Length > 0;
        var pattern = automatable
            ? "FRAME_DISPATCH_CONSOLIDATION"
            : parkedRecipes.Contains(
                "AUTHOR_CADENCE_WHOLE_CALLBACK",
                StringComparer.OrdinalIgnoreCase)
                ? "AUTHOR_CADENCE_EVIDENCE"
                : parkedRecipes.Contains(
                    "AUTHOR_DISCOVERY_DORMANT_SCHEDULE",
                    StringComparer.OrdinalIgnoreCase)
                    ? "AUTHOR_DISCOVERY_DORMANT_EVIDENCE"
                    : parkedRecipes.Contains(
                        "HARD_DORMANT_GUARD_HOIST",
                        StringComparer.OrdinalIgnoreCase)
                        ? "HARD_DORMANT_EVIDENCE"
                        : parkedRecipes.Contains(
                            "STRUCTURAL_HOTPATH_REWRITE",
                            StringComparer.OrdinalIgnoreCase)
                            ? "STRUCTURAL_HOTPATH_EVIDENCE"
                            : alreadyFrameConsolidated
                                ? "FRAME_DISPATCH_ALREADY_SATISFIED"
                                : "ONUPDATE_UNRESOLVED";

        var status = automatable
            ? "RESOLVED"
            : parkedRecipes.Length > 0
                ? "ANALYSIS_ONLY"
                : alreadyFrameConsolidated
                    ? "ALREADY_SATISFIED"
                    : "SOURCE_UNRESOLVED";

        return new GenericResolution
        {
            Status = status,
            Automatable = automatable,
            Pattern = pattern,
            RecipeFamilies = automaticRecipes,
            Facts = facts,
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = effectiveSourceEvidence
        };
    }

    private static bool HasGeneratedFrameRegistrar(
        string fullText,
        string registrar)
    {
        if (string.IsNullOrWhiteSpace(registrar))
            return false;

        var escaped = Regex.Escape(registrar);
        var hasFallback = Regex.IsMatch(
            fullText,
            @"\blocal\s+" + escaped + @"\s*=\s*registerForEvent\b",
            RegexOptions.CultureInvariant);
        var hasEngineRegistrar = Regex.IsMatch(
            fullText,
            @"\b" + escaped +
            @"\s*=\s*__gcetEngine\.MakeEventRegistrar\s*\(",
            RegexOptions.CultureInvariant);

        return hasFallback && hasEngineRegistrar;
    }

    private static bool TryResolveHardDormantGuardHoist(
        ResolvedSource source,
        CallbackMetric callback,
        out HardDormantGuardResolution resolution)
    {
        resolution = new HardDormantGuardResolution();

        // Guard-hoist is only worth the semantic proof cost on material callbacks.
        if (callback.ExclusiveMsPerSecond < 8.0 ||
            callback.GlobalWorkSharePct < 1.0)
            return false;

        var callbackText = source.CallbackText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        if (Regex.IsMatch(
                callbackText,
                @"\b(?:AIAction|AIBehavior|CombatState|NPCPuppet|CameraSystem|GetActiveCameraData|\bFPP\b|\bTPP\b)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        if (Regex.IsMatch(
                callbackText,
                @"\b(?:process\w*Queue|update\w*Queue|Cron\.Update|pending|queue)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        var opening = Regex.Match(
            callbackText,
            @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*(['""])onUpdate\1\s*,\s*function\s*\((?<args>[^)]*)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!opening.Success)
            return false;

        var body = callbackText[(opening.Index + opening.Length)..];
        var lines = body.Split('\n');
        var gateIndex = -1;
        var gateExpression = "";

        var gateRegex = new Regex(
            @"^\s*if\s+not\s+(?<gate>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s+return\s+end\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        for (var i = 0; i < lines.Length; i++)
        {
            var match = gateRegex.Match(lines[i]);
            if (!match.Success)
                continue;

            gateIndex = i;
            gateExpression = match.Groups["gate"].Value;
            break;
        }

        if (gateIndex <= 0 || string.IsNullOrWhiteSpace(gateExpression))
            return false;

        var preGuardReadCount = 0;
        var meaningfulBeforeGuard = 0;
        var gateRoot = gateExpression.Split('.')[0];

        for (var i = 0; i < gateIndex; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
                continue;

            meaningfulBeforeGuard++;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+" + Regex.Escape(gateRoot) + @"\b",
                    RegexOptions.CultureInvariant))
                return false;

            // Only local setup/read statements may be bypassed while dormant.
            if (!Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*(?:\s*,\s*[A-Za-z_]\w*)*\s*=\s*.+$",
                    RegexOptions.CultureInvariant))
                return false;

            if (!Regex.IsMatch(line, @"[A-Za-z_][\w.:]*\s*\("))
                continue;

            // Any call before the guard must be recognizably read-only.
            if (!Regex.IsMatch(
                    line,
                    @"(?:\bGame\.Get[A-Za-z_]\w*\s*\(|\bGetSingleton\s*\(|[:.]\s*(?:Get|Is|Has)[A-Za-z_]\w*\s*\()",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                return false;

            preGuardReadCount++;
        }

        if (meaningfulBeforeGuard == 0 || preGuardReadCount == 0)
            return false;

        var full = source.FullText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var callbackIndex = full.IndexOf(callbackText, StringComparison.Ordinal);
        if (callbackIndex < 0)
            return false;

        var outside = full.Remove(callbackIndex, callbackText.Length);
        var escapedGate = Regex.Escape(gateExpression);
        var writers = Regex.Matches(
                outside,
                @"(?m)^\s*" + escapedGate + @"\s*=\s*[^=].*$",
                RegexOptions.CultureInvariant)
            .Cast<Match>()
            .Select(x => x.Value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        if (writers.Length == 0)
            return false;

        var wakeSignals = new List<string>();
        foreach (var token in new[] { "registerHotkey", "registerInput" })
        {
            if (Regex.IsMatch(
                    outside,
                    @"\b" + token + @"\s*\([\s\S]{0,1600}?\b" + escapedGate + @"\s*=",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                wakeSignals.Add(token);
            }
        }

        if (wakeSignals.Count == 0)
            return false;

        resolution = new HardDormantGuardResolution
        {
            GateExpression = gateExpression,
            PreGuardReadCount = preGuardReadCount,
            StateWriters = writers,
            WakeSignals = wakeSignals.ToArray(),
            Evidence = new[]
            {
                $"Current source proves inactive guard 'if not {gateExpression} then return end' after {preGuardReadCount} read/setup call(s).",
                $"The same state is written outside the hot callback and is reachable from {string.Join("/", wakeSignals)}.",
                $"Measured callback cost is {callback.ExclusiveMsPerSecond:0.###} ms/s ({callback.GlobalWorkSharePct:0.###}% of measured CET work)."
            }
        };
        return true;
    }

    private static bool TryResolveStructuralHotpath(
        ResolvedSource source,
        CallbackMetric callback,
        out StructuralHotpathResolution resolution)
    {
        resolution = new StructuralHotpathResolution();

        // Relative gates deliberately scale with the measured CET workload.
        // A structurally safe rewrite is still skipped when the callback is not
        // a material consumer in this capture.
        if (callback.ExclusiveMsPerSecond < 5.0 ||
            callback.GlobalWorkSharePct < 0.5)
            return false;

        var text = source.CallbackText;
        var expressions = new List<StructuralExpression>();
        var constructors = new List<StructuralExpression>();

        // Restrict automatic call-scope reuse to CET/Game singleton-style getters
        // whose identity is expected to be stable for one Lua callback invocation.
        var getterPattern = new Regex(
            @"\bGame\.(?:GetPlayer|GetTargetingSystem|GetBlackboardSystem|GetAllBlackboardDefs|GetQuestsSystem|GetTimeSystem|GetStatsSystem|GetStatPoolsSystem|GetSystemRequestsHandler|GetTeleportationFacility|GetCameraSystem)\s*\(\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var group in getterPattern.Matches(text)
                     .Cast<Match>()
                     .GroupBy(x => x.Value, StringComparer.Ordinal))
        {
            var count = group.Count();
            if (count < 2)
                continue;

            expressions.Add(new StructuralExpression
            {
                Expression = group.Key,
                Count = count
            });
        }

        // A narrow set of engine object reads is also stable for the duration
        // of one callback invocation. Keep this whitelist semantic and finite;
        // arbitrary Get*/Is* Lua methods are not assumed side-effect-free.
        var stableMemberReadPattern = new Regex(
            @"(?:\b(?:[A-Za-z_]\w*|Game\.GetPlayer\(\))(?::|\.)(?:IsMovingHorizontally|IsMovingVertically|GetWorldPosition|GetEntityID)\s*\(\s*\)|\bGetSingleton\s*\(\s*(['""])[^'""]+\1\s*\))",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var group in stableMemberReadPattern.Matches(text)
                     .Cast<Match>()
                     .GroupBy(x => x.Value, StringComparer.Ordinal))
        {
            var count = group.Count();
            if (count < 2)
                continue;

            if (expressions.Any(x => x.Expression.Equals(group.Key, StringComparison.Ordinal)))
                continue;

            expressions.Add(new StructuralExpression
            {
                Expression = group.Key,
                Count = count
            });
        }

        // Literal value constructors are safe to reuse when their complete input
        // is embedded in source. Keep this list intentionally narrow.
        var constructorPattern = new Regex(
            @"\b(?:CName|TweakDBID)\.new\s*\(\s*(?<quote>['""])(?<value>(?:\\.|(?!\k<quote>).)*)\k<quote>\s*\)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        foreach (var group in constructorPattern.Matches(text)
                     .Cast<Match>()
                     .GroupBy(x => x.Value, StringComparer.Ordinal))
        {
            var count = group.Count();
            if (count < 1)
                continue;

            constructors.Add(new StructuralExpression
            {
                Expression = group.Key,
                Count = count
            });
        }

        var staticTables = FindSafeStaticLiteralTables(text);

        if (expressions.Count == 0 &&
            constructors.Count == 0 &&
            staticTables.Count == 0)
            return false;

        // Repeated callback-stable getters save occurrences beyond the first
        // within each invocation. Literal CName/TweakDBID construction is
        // different: every callback-time construction is avoidable because the
        // complete immutable input is source-literal and can be hoisted once.
        var avoidableOccurrences =
            expressions.Sum(x => x.Count - 1) +
            constructors.Sum(x => x.Count);
        var totalOccurrences =
            expressions.Sum(x => x.Count) +
            constructors.Sum(x => x.Count);
        var expressionFraction = totalOccurrences > 0
            ? Math.Min(0.75, (double)avoidableOccurrences / totalOccurrences)
            : 0.0;

        // Literal-table allocation is harder to price from source alone. Only
        // admit it as an automatic materiality contribution on genuinely hot,
        // frequently-entered callbacks, and cap the estimate conservatively.
        var tableFraction =
            staticTables.Count > 0 &&
            callback.CallsPerSecond >= 30.0 &&
            callback.ExclusiveMsPerSecond >= 8.0
                ? Math.Min(0.20, staticTables.Count * 0.05)
                : 0.0;

        var localFraction = Math.Min(0.75, expressionFraction + tableFraction);
        var estimatedCallbackPaybackPct = localFraction * 100.0;
        var estimatedGlobalPaybackPct =
            callback.GlobalWorkSharePct * localFraction;

        // Do not generate microscopic structural patches.
        if (estimatedCallbackPaybackPct < 10.0 &&
            estimatedGlobalPaybackPct < 0.25)
            return false;

        var evidence = new List<string>();
        if (expressions.Count > 0)
            evidence.Add(
                $"Current source contains {expressions.Sum(x => x.Count)} calls across " +
                $"{expressions.Count} repeated callback-stable Game getter expression(s).");
        if (constructors.Count > 0)
            evidence.Add(
                $"Current source contains {constructors.Sum(x => x.Count)} calls across " +
                $"{constructors.Count} literal constructor expression(s) hoistable out of the callback.");
        if (staticTables.Count > 0)
            evidence.Add(
                $"Current source contains {staticTables.Count} callback-local literal table(s) " +
                "whose uses are proven read-only and can be hoisted without sharing mutable state.");
        evidence.Add(
            $"Measured callback cost is {callback.ExclusiveMsPerSecond:0.###} ms/s " +
            $"({callback.GlobalWorkSharePct:0.###}% of measured CET work).");

        resolution = new StructuralHotpathResolution
        {
            IdenticalExpressions = expressions.ToArray(),
            LiteralConstructors = constructors.ToArray(),
            StaticLiteralTables = staticTables.ToArray(),
            EstimatedCallbackPaybackPct = estimatedCallbackPaybackPct,
            EstimatedGlobalPaybackPct = estimatedGlobalPaybackPct,
            Evidence = evidence.ToArray()
        };
        return true;
    }

    private static List<StaticLiteralTable> FindSafeStaticLiteralTables(string text)
    {
        var result = new List<StaticLiteralTable>();
        var declarationRegex = new Regex(
            @"\blocal\s+(?<name>[A-Za-z_]\w*)\s*=\s*\{",
            RegexOptions.CultureInvariant);

        foreach (Match declaration in declarationRegex.Matches(text))
        {
            var name = declaration.Groups["name"].Value;
            var braceStart = text.IndexOf('{', declaration.Index + declaration.Length - 1);
            if (braceStart < 0 ||
                !TryFindMatchingLuaBrace(text, braceStart, out var braceEnd))
                continue;

            var literal = text.Substring(braceStart, braceEnd - braceStart + 1);
            if (!IsStaticLiteralTable(literal))
                continue;

            var declarationStart = declaration.Index;
            var declarationText = text.Substring(
                declarationStart,
                braceEnd - declarationStart + 1);

            var remainder =
                text[..declarationStart] +
                new string(' ', declarationText.Length) +
                text[(braceEnd + 1)..];

            if (!IsReadOnlyTableUse(remainder, name))
                continue;

            var elementCount = Regex.Matches(literal, @",", RegexOptions.CultureInvariant).Count + 1;
            result.Add(new StaticLiteralTable
            {
                Variable = name,
                Declaration = declarationText,
                Literal = literal,
                ElementCount = elementCount
            });
        }

        return result;
    }

    private static bool TryFindMatchingLuaBrace(
        string text,
        int start,
        out int end)
    {
        end = -1;
        var depth = 0;
        var quote = '\0';
        var escaped = false;
        var lineComment = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];

            if (lineComment)
            {
                if (ch == '\n')
                    lineComment = false;
                continue;
            }

            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (ch == quote)
                    quote = '\0';
                continue;
            }

            if (ch == '\'' || ch == '"')
            {
                quote = ch;
                continue;
            }

            if (ch == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                lineComment = true;
                i++;
                continue;
            }

            if (ch == '{')
                depth++;
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    end = i;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsStaticLiteralTable(string literal)
    {
        var stripped = Regex.Replace(
            literal,
            @"(['""])(?:\\.|(?!\1).)*\1",
            "\"\"",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        stripped = Regex.Replace(
            stripped,
            @"\b[A-Za-z_]\w*\s*=",
            "=",
            RegexOptions.CultureInvariant);
        stripped = Regex.Replace(
            stripped,
            @"\b(?:true|false|nil)\b",
            "0",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // After removing quoted values and named literal keys, no identifier or
        // call expression may remain. This keeps the hoist to immutable scalar
        // literal data only.
        return !Regex.IsMatch(
            stripped,
            @"[A-Za-z_]|\(",
            RegexOptions.CultureInvariant);
    }

    private static bool IsReadOnlyTableUse(string text, string variable)
    {
        var escaped = Regex.Escape(variable);
        if (Regex.IsMatch(
                text,
                @"\b" + escaped + @"\s*(?:\[[^\]]*\]|\.[A-Za-z_]\w*)?\s*=",
                RegexOptions.CultureInvariant))
            return false;

        if (Regex.IsMatch(
                text,
                @"\btable\s*\.\s*(?:insert|remove|sort|move|clear)\s*\(\s*" + escaped + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;

        foreach (Match use in Regex.Matches(
                     text,
                     @"\b" + escaped + @"\b",
                     RegexOptions.CultureInvariant))
        {
            var lineStart = text.LastIndexOf('\n', Math.Max(0, use.Index - 1));
            var lineEnd = text.IndexOf('\n', use.Index);
            var start = lineStart < 0 ? 0 : lineStart + 1;
            var end = lineEnd < 0 ? text.Length : lineEnd;
            var line = text.Substring(start, end - start).Trim();

            if (Regex.IsMatch(
                    line,
                    @"\b(?:i?pairs)\s*\(\s*" + escaped + @"\s*\)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"#" + escaped + @"\b",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"\b" + escaped + @"\s*\[",
                    RegexOptions.CultureInvariant))
                continue;

            return false;
        }

        return true;
    }

    private static bool TryResolveWholeCallbackAuthorCadence(
        ResolvedSource source,
        CallbackMetric callback,
        CadenceDecision? cadenceDecision,
        out AuthorCadenceResolution resolution,
        out string blocker)
    {
        resolution = new AuthorCadenceResolution();
        blocker = "";

        var normalizedFull = source.FullText
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var callbackText = source.CallbackText;

        if (cadenceDecision is not null &&
            cadenceDecision.RegistrationLine > 0 &&
            cadenceDecision.CallbackBodyEndLine >= cadenceDecision.RegistrationLine)
        {
            var fullLines = normalizedFull.Split('\n');
            if (cadenceDecision.RegistrationLine <= fullLines.Length)
            {
                var endLine = Math.Min(
                    cadenceDecision.CallbackBodyEndLine,
                    fullLines.Length);
                callbackText = string.Join(
                    "\n",
                    fullLines
                        .Skip(cadenceDecision.RegistrationLine - 1)
                        .Take(endLine - cadenceDecision.RegistrationLine + 1));
            }
        }

        var match = Regex.Match(
            callbackText,
            @"(?s)^\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*(?<delta>[A-Za-z_]\w*)\s*\)[ \t]*(?:\n)?(?<body>.*)\bend\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            blocker = "Author cadence: exact single-parameter onUpdate callback shape was not proven.";
            return false;
        }

        var deltaParameter = match.Groups["delta"].Value;
        var body = match.Groups["body"].Value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var lines = body.Split('\n');

        var significant = lines
            .Select((text, index) => new
            {
                Text = text,
                Trimmed = text.Trim(),
                Index = index,
                Indent = text.Length - text.TrimStart().Length
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Trimmed) &&
                        !x.Trimmed.StartsWith("--", StringComparison.Ordinal))
            .ToList();

        if (significant.Count == 0)
        {
            blocker = "Author cadence: callback body is empty.";
            return false;
        }

        var increments = new Dictionary<string, int>(StringComparer.Ordinal);
        var gates = new Dictionary<string, double>(StringComparer.Ordinal);
        var gateRanges = new List<(int Start, int End)>();

        var escapedDelta = Regex.Escape(deltaParameter);
        var incrementRegex = new Regex(
            @"^\s*(?<var>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:" +
            @"=\s*\k<var>\s*\+\s*" + escapedDelta +
            @"|\+=\s*" + escapedDelta + @")\s*;?\s*$",
            RegexOptions.CultureInvariant);
        var gateRegex = new Regex(
            @"^\s*if\s+(?<var>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:>=|>)\s*(?<seconds>\d+(?:\.\d+)?)\s+then\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var earlyReturnGateRegex = new Regex(
            @"^\s*if\s+(?<var>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*<\s*(?<seconds>\d+(?:\.\d+)?)\s+then\s+return\s+end\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var incrementIndents = lines
            .Where(line => incrementRegex.IsMatch(line))
            .Select(line => line.Length - line.TrimStart().Length)
            .ToArray();
        if (incrementIndents.Length == 0)
        {
            blocker = "Author cadence: no fixed delta accumulator increment was proven.";
            return false;
        }

        // Source ranges can include harmless neighboring lines. The author's
        // accumulator statements define the callback-body indentation we care
        // about; do not let a neighboring column-zero line poison the recipe.
        var baseIndent = incrementIndents.Min();

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;

            var indent = lines[i].Length - lines[i].TrimStart().Length;
            if (indent != baseIndent)
                continue;

            var increment = incrementRegex.Match(lines[i]);
            if (increment.Success)
            {
                var incrementVariable = increment.Groups["var"].Value;
                increments[incrementVariable] = i;
                continue;
            }

            var earlyGate = earlyReturnGateRegex.Match(lines[i]);
            if (earlyGate.Success)
            {
                if (gates.Count > 0)
                {
                    blocker = "Author cadence: mixed early-return and block timer gates are not yet a finite supported recipe.";
                    return false;
                }

                var earlyVariable = earlyGate.Groups["var"].Value;
                if (!double.TryParse(
                        earlyGate.Groups["seconds"].Value,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var earlySeconds) ||
                    earlySeconds <= 0 ||
                    !double.IsFinite(earlySeconds))
                {
                    blocker = "Author cadence: early-return timer threshold is not a positive fixed author rate.";
                    return false;
                }

                var remainder = string.Join("\n", lines.Skip(i + 1));
                if (!Regex.IsMatch(
                        remainder,
                        @"\b" + Regex.Escape(earlyVariable) + @"\s*=\s*0(?:\.0+)?\b",
                        RegexOptions.CultureInvariant))
                {
                    blocker = $"Author cadence: timer '{earlyVariable}' is not reset after its early-return gate.";
                    return false;
                }

                gates[earlyVariable] = earlySeconds;
                gateRanges.Add((i, lines.Length - 1));
                break;
            }

            var gate = gateRegex.Match(lines[i]);
            if (!gate.Success)
            {
                blocker = $"Author cadence: top-level frame work remains outside author timer gates (line {i + 1}).";
                return false;
            }

            var variable = gate.Groups["var"].Value;
            if (!double.TryParse(
                    gate.Groups["seconds"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds) ||
                seconds <= 0 ||
                !double.IsFinite(seconds))
            {
                blocker = "Author cadence: timer threshold is not a positive fixed author rate.";
                return false;
            }

            var end = FindSameIndentEnd(lines, i, baseIndent);
            if (end <= i)
            {
                blocker = "Author cadence: timer gate block boundary could not be proven.";
                return false;
            }

            var block = string.Join("\n", lines.Skip(i + 1).Take(end - i - 1));
            if (!Regex.IsMatch(
                    block,
                    @"\b" + Regex.Escape(variable) + @"\s*=\s*0(?:\.0+)?\b",
                    RegexOptions.CultureInvariant))
            {
                blocker = $"Author cadence: timer '{variable}' is not reset inside its author gate.";
                return false;
            }

            gates[variable] = seconds;
            gateRanges.Add((i, end));
            i = end;
        }

        if (gates.Count == 0 || increments.Count == 0)
        {
            blocker = $"Author cadence: no complete fixed accumulator timer was proven (increments={increments.Count}, gates={gates.Count}, baseIndent={baseIndent}).";
            return false;
        }

        if (increments.Keys.Except(gates.Keys, StringComparer.Ordinal).Any() ||
            gates.Keys.Except(increments.Keys, StringComparer.Ordinal).Any())
        {
            blocker = "Author cadence: timer increments and author gates do not form a closed set.";
            return false;
        }

        // The callback may use delta only to advance the proven accumulators.
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"\b" + escapedDelta + @"\b"))
                continue;
            if (incrementRegex.IsMatch(lines[i]))
                continue;

            blocker = "Author cadence: delta is consumed by work other than the proven timer accumulators.";
            return false;
        }

        // Timer state must be private to the cadence mechanism. This avoids
        // changing externally observed continuously-increasing timer values.
        var callbackIndex = normalizedFull.IndexOf(callbackText, StringComparison.Ordinal);
        if (callbackIndex < 0)
        {
            blocker = "Author cadence: callback text could not be isolated from the current source file.";
            return false;
        }

        var outside = normalizedFull.Remove(callbackIndex, callbackText.Length);

        foreach (var variable in gates.Keys)
        {
            if (!HasZeroAuthorTimerInitializer(outside, variable))
            {
                blocker = $"Author cadence: zero initialization for timer '{variable}' was not proven.";
                return false;
            }

            if (HasExternalTimerReference(outside, variable))
            {
                blocker = $"Author cadence: timer '{variable}' is referenced outside the callback.";
                return false;
            }
        }

        var intervalsUs = gates.Values
            .Select(x => checked((long)Math.Round(x * 1_000_000.0, MidpointRounding.AwayFromZero)))
            .Where(x => x > 0)
            .ToArray();
        if (intervalsUs.Length != gates.Count)
        {
            blocker = "Author cadence: one or more author rates could not be represented safely.";
            return false;
        }

        var baseUs = intervalsUs.Aggregate(GreatestCommonDivisor);
        if (baseUs <= 0)
        {
            blocker = "Author cadence: common author cadence could not be resolved.";
            return false;
        }

        var baseSeconds = baseUs / 1_000_000.0;
        var expectedCallsPerSecond = 1.0 / baseSeconds;
        var reductionFactor = expectedCallsPerSecond > 0
            ? callback.CallsPerSecond / expectedCallsPerSecond
            : 0;

        if (reductionFactor < 2.0)
        {
            blocker =
                $"Author cadence is source-proven, but measured entry reduction would be only {reductionFactor:0.##}x.";
            return false;
        }

        if (cadenceDecision is null ||
            cadenceDecision.MedianUsPerCall <= 0 ||
            callback.ExclusiveMsPerSecond <= 0)
        {
            blocker =
                "Author cadence is source-proven, but runtime baseline cost is unavailable, so payback cannot be established.";
            return false;
        }

        var removableCallsPerSecond = Math.Max(
            0,
            callback.CallsPerSecond - expectedCallsPerSecond);
        var estimatedAvoidableMsPerSecond =
            cadenceDecision.MedianUsPerCall * removableCallsPerSecond / 1000.0;
        estimatedAvoidableMsPerSecond = Math.Min(
            estimatedAvoidableMsPerSecond,
            callback.ExclusiveMsPerSecond);

        var callbackPaybackPct =
            100.0 * estimatedAvoidableMsPerSecond /
            Math.Max(0.000001, callback.ExclusiveMsPerSecond);

        var globalPaybackPct =
            callback.GlobalWorkSharePct *
            estimatedAvoidableMsPerSecond /
            Math.Max(0.000001, callback.ExclusiveMsPerSecond);

        if (callbackPaybackPct < AuthorCadenceMinCallbackPaybackPct ||
            globalPaybackPct < AuthorCadenceMinGlobalPaybackPct)
        {
            blocker =
                $"Author cadence is source-proven but low-payback: estimated avoidable polling is " +
                $"{estimatedAvoidableMsPerSecond:0.######} ms/s " +
                $"({callbackPaybackPct:0.###}% of callback, {globalPaybackPct:0.###}% of measured CET work).";
            return false;
        }

        resolution = new AuthorCadenceResolution
        {
            DeltaParameter = deltaParameter,
            BaseIntervalSeconds = baseSeconds,
            TimerIntervalsSeconds = gates.Values
                .OrderBy(x => x)
                .ToArray(),
            AccumulatorVariables = gates.Keys
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            ExpectedCallsPerSecond = expectedCallsPerSecond,
            RuntimeEntryReductionFactor = reductionFactor,
            EstimatedAvoidablePollingMsPerSecond = estimatedAvoidableMsPerSecond,
            EstimatedCallbackPaybackPct = callbackPaybackPct,
            EstimatedGlobalPaybackPct = globalPaybackPct
        };
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
                    @"^end\s*;?\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            var candidateIndent = lines[i].Length - lines[i].TrimStart().Length;
            if (candidateIndent == indent)
                return i;
        }

        return -1;
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var t = left % right;
            left = right;
            right = t;
        }
        return left;
    }

    private static bool HasZeroAuthorTimerInitializer(
        string outside,
        string variable)
    {
        if (!variable.Contains('.', StringComparison.Ordinal))
        {
            return Regex.IsMatch(
                outside,
                @"\b(?:local\s+)?" + Regex.Escape(variable) +
                @"\s*=\s*0(?:\.0+)?\b",
                RegexOptions.CultureInvariant);
        }

        if (Regex.IsMatch(
                outside,
                @"\b" + Regex.Escape(variable) + @"\s*=\s*0(?:\.0+)?\b",
                RegexOptions.CultureInvariant))
            return true;

        var pieces = variable.Split('.');
        if (pieces.Length != 2)
            return false;

        var tableName = pieces[0];
        var fieldName = pieces[1];
        var declaration = Regex.Match(
            outside,
            @"\b(?:local\s+)?" + Regex.Escape(tableName) + @"\s*=\s*\{",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return false;

        var open = outside.IndexOf('{', declaration.Index);
        if (open < 0)
            return false;

        var close = FindBalancedBraceEnd(outside, open);
        if (close <= open)
            return false;

        var body = outside.Substring(open + 1, close - open - 1);
        return Regex.IsMatch(
            body,
            @"\b" + Regex.Escape(fieldName) + @"\s*=\s*0(?:\.0+)?\b",
            RegexOptions.CultureInvariant);
    }

    private static bool HasExternalTimerReference(
        string outside,
        string variable)
    {
        if (variable.Contains('.', StringComparison.Ordinal))
        {
            if (Regex.IsMatch(
                    outside,
                    @"\b" + Regex.Escape(variable) + @"\b",
                    RegexOptions.CultureInvariant))
                return true;

            var pieces = variable.Split('.');
            if (pieces.Length == 2 &&
                Regex.IsMatch(
                    outside,
                    @"\b" + Regex.Escape(pieces[0]) +
                    @"\s*\[\s*['""]" + Regex.Escape(pieces[1]) + @"['""]\s*\]",
                    RegexOptions.CultureInvariant))
                return true;

            return false;
        }

        var initializerRegex = new Regex(
            @"\b(?:local\s+)?" + Regex.Escape(variable) +
            @"\s*=\s*0(?:\.0+)?\b",
            RegexOptions.CultureInvariant);
        var withoutInitializer = initializerRegex.Replace(outside, "", 1);

        return Regex.IsMatch(
            withoutInitializer,
            @"\b" + Regex.Escape(variable) + @"\b",
            RegexOptions.CultureInvariant);
    }

    private static int FindBalancedBraceEnd(string text, int open)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static GenericResolution ResolveOnAction(
        CallbackMetric callback,
        ResolvedSource? source,
        SourceEvidence? sourceEvidence,
        LiveSourceIndex sourceIndex)
    {
        if (source is null)
        {
            return new GenericResolution
            {
                Status = "SOURCE_UNRESOLVED",
                Automatable = false,
                Pattern = "ONACTION_SOURCE_UNRESOLVED",
                RecipeFamilies = Array.Empty<string>(),
                Facts = null,
                Evidence = Array.Empty<string>(),
                Blockers = new[] { "The current deployed OnAction callback source could not be mapped uniquely." },
                Source = sourceEvidence
            };
        }

        var window = source.CallbackText;
        var full = source.FullText;
        var evidence = new List<string>();
        var blockers = new List<string>();
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedActionSelectors = new HashSet<string>(StringComparer.Ordinal);

        var consumerMutation =
            Regex.IsMatch(window, @"\bconsumer\s*[:.]\s*Consume(?:SingleAction)?\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(window, @"\bListenerActionConsumer\s*\.\s*Consume\s*\(", RegexOptions.IgnoreCase);
        if (consumerMutation)
            evidence.Add("The callback mutates the original input consumer; generated routing must preserve the same consumer object and callback ordering.");

        // Name decoding is structurally equivalent whether the callback uses
        // action:GetName(), ListenerAction.GetName(action), a singleton receiver,
        // or another wrapper. The assigned variable is what later filters use.
        var nameVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     window,
                     @"(?m)\b(?<var>[A-Za-z_]\w*)\s*=\s*[^\r\n;]*\bGetName\s*\(",
                     RegexOptions.CultureInvariant))
        {
            nameVars.Add(match.Groups["var"].Value);
        }

        // Literal IsAction forms, including CET's common
        // action:IsAction(action, "Name") shape.
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bIsAction\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?['""](?<action>[^'""]+)['""]\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
        }

        // Variable IsAction selectors are accepted only when all values assigned
        // to the selector in current source are finite string/CName literals.
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bIsAction\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?(?<selector>[A-Za-z_]\w*)\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var selector = match.Groups["selector"].Value;
            var values = ReadFiniteStringValues(full, selector);
            if (values.Count == 0)
            {
                unresolvedActionSelectors.Add(selector);
                continue;
            }

            foreach (var value in values)
                actions.Add(value);
            evidence.Add($"Finite action selector '{selector}' resolved to {values.Count} literal value(s).");
        }

        // Direct CName/string comparisons against GetName().
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bGetName\s*\(\s*\)\s*==\s*(?:CName\.new\s*\(\s*)?['""](?<action>[^'""]+)['""]\s*\)?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
        }

        // Early-return inverse comparison: if action:GetName() ~= turnX then return.
        foreach (Match match in Regex.Matches(
                     window,
                     @"(?m)^\s*if\s+[^\r\n]*\bGetName\s*\(\s*\)\s*~=\s*(?<selector>[A-Za-z_]\w*)\s+then\s+return",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var selector = match.Groups["selector"].Value;
            var values = ReadFiniteStringValues(full, selector);
            if (values.Count == 0)
                unresolvedActionSelectors.Add(selector);
            else
                foreach (var value in values) actions.Add(value);
        }

        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"\b" + Regex.Escape(variable) + @"\s*==\s*['""](?<action>[^'""]+)['""]",
                         RegexOptions.CultureInvariant))
            {
                actions.Add(match.Groups["action"].Value);
            }

            foreach (Match match in Regex.Matches(
                         window,
                         @"(?m)^\s*if\s+" + Regex.Escape(variable) + @"\s*~=\s*['""](?<action>[^'""]+)['""]\s+then\s+return",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                actions.Add(match.Groups["action"].Value);
            }
        }

        var staticTables = new List<string>();
        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"\b(?<table>[A-Za-z_]\w*)\s*\[\s*" + Regex.Escape(variable) + @"\s*\]",
                         RegexOptions.CultureInvariant))
            {
                var table = match.Groups["table"].Value;
                if (TryReadStaticStringSet(full, table, out var tableActions, out var dynamicWrites))
                {
                    foreach (var action in tableActions)
                        actions.Add(action);
                    staticTables.Add(table);
                    evidence.Add($"Static action table '{table}' resolved with {tableActions.Count} entries.");
                    if (dynamicWrites)
                        blockers.Add($"Action table '{table}' has writes outside its literal definition.");
                }
            }
        }

        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"(?:string\.find\s*\(\s*" + Regex.Escape(variable) + @"\s*,\s*['""](?<p>[^'""]+)['""]|" +
                         Regex.Escape(variable) + @"\s*:\s*find\s*\(\s*['""](?<p2>[^'""]+)['""])",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var value = match.Groups["p"].Success
                    ? match.Groups["p"].Value
                    : match.Groups["p2"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                    patterns.Add(value);
            }
        }

        var stateGated = Regex.IsMatch(
            window,
            @"\bif\s+(?:not\s+)?[A-Za-z_][\w.]*\s+then\s+(?:return|[^\n]*\n\s*return)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var dynamicActionForward = HasDynamicActionForward(window);
        var downstreamExpanded = false;
        var downstreamMethods = Array.Empty<string>();
        var downstreamFiles = Array.Empty<string>();
        var downstreamPrefilterGateReceiver = "";
        var downstreamPrefilterGateMember = "";

        // Raw action forwarding can still close over a finite exact set when
        // the callback also forwards its decoded action-name value to an
        // owner-local method whose implementations all use finite literal
        // action selectors. This is source-structural, never a mod-name rule.
        if (dynamicActionForward &&
            TryResolveStaticDownstreamActionInterest(
                window,
                full,
                nameVars,
                callback.Owner,
                sourceIndex,
                out var downstreamActions,
                out downstreamMethods,
                out downstreamFiles,
                out downstreamPrefilterGateReceiver,
                out downstreamPrefilterGateMember))
        {
            foreach (var action in downstreamActions)
                actions.Add(action);

            dynamicActionForward = false;
            downstreamExpanded = true;
            evidence.Add(
                $"Raw action forwarding was closed over {downstreamMethods.Length} owner-local method name(s) " +
                $"and {downstreamActions.Length} finite action name(s).");
        }

        var gatedWildcardResolved = false;
        var dynamicGateExpression = "";

        // Some callbacks genuinely need the full action stream, but only while
        // a simple owner-local state gate is active. If every raw-action
        // forward is enclosed by side-effect-free boolean member checks and all
        // other callback work is either local decoding or already-proven exact
        // action branches, keep wildcard semantics only behind that gate.
        if (dynamicActionForward &&
            TryResolveSimpleDynamicGate(
                window,
                nameVars,
                out dynamicGateExpression))
        {
            dynamicActionForward = false;
            gatedWildcardResolved = true;
            stateGated = true;
            evidence.Add(
                $"Full-stream downstream action forwarding is bounded by a proven simple state gate: {dynamicGateExpression}.");
        }

        if (dynamicActionForward)
        {
            blockers.Add("Raw action data is forwarded to downstream logic, so this callback alone does not prove a finite action set.");
            evidence.Add("A downstream handler receives the raw action; deeper source analysis is required before exact routing.");
        }

        if (unresolvedActionSelectors.Count > 0)
            blockers.Add($"Action selector(s) could not be reduced to literals: {string.Join(", ", unresolvedActionSelectors.OrderBy(x => x))}.");

        if (actions.Count > 0)
            evidence.Add($"Finite action interest was proven from current source ({actions.Count} action name(s)).");
        if (patterns.Count > 0)
            evidence.Add($"Action-name pattern interest was proven from current source ({patterns.Count} pattern(s)).");
        if (stateGated)
            evidence.Add("An early state gate is present before the callback's main work.");

        var hasActionFilter = actions.Count > 0 || patterns.Count > 0 || staticTables.Count > 0;
        var hasRoutableInterest = hasActionFilter || gatedWildcardResolved;
        var prefilterSideEffect = hasActionFilter &&
            HasMeaningfulWorkBeforeFirstActionFilter(window, nameVars, downstreamMethods);

        // A downstream source-proven optional diagnostic gate is preserved by
        // disabling the Override fast-path whenever that flag is active.
        if (callback.Kind.Contains("override", StringComparison.OrdinalIgnoreCase) &&
            downstreamExpanded &&
            !string.IsNullOrWhiteSpace(downstreamPrefilterGateMember))
            prefilterSideEffect = false;
        if (prefilterSideEffect)
            blockers.Add("Observable work occurs before the first proven action-interest filter.");

        // Callback kind is authoritative. A neighboring Override() elsewhere in
        // the same source window must never poison an Observe classification.
        var isOverride = callback.Kind.Contains("override", StringComparison.OrdinalIgnoreCase);
        var overrideWrappedMethodReturns = false;
        var overrideWrappedMethodTakesSelf = false;
        var overridePrefilterProven =
            isOverride &&
            downstreamExpanded &&
            actions.Count > 0 &&
            patterns.Count == 0 &&
            !consumerMutation &&
            TryProveTransparentOverrideWrapper(
                window,
                downstreamMethods,
                out overrideWrappedMethodReturns,
                out overrideWrappedMethodTakesSelf);

        if (isOverride && !overridePrefilterProven)
            blockers.Add("Override semantics require the dedicated override routing template.");
        else if (overridePrefilterProven)
            evidence.Add(
                "Override is a transparent wrapper around source-proven finite downstream action handling; " +
                "irrelevant actions can call wrappedMethod directly without changing relevant-action behavior.");

        string recipe;
        if (dynamicActionForward)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DYNAMIC_DOWNSTREAM"
                : "ACTION_ROUTING_DYNAMIC_DOWNSTREAM";
        else if (gatedWildcardResolved)
            recipe = "ACTION_ROUTING_GATED_WILDCARD";
        else if (downstreamExpanded && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DOWNSTREAM_STATIC_SET_WITH_PATTERN"
                : "ACTION_ROUTING_DOWNSTREAM_STATIC_SET_WITH_PATTERN";
        else if (downstreamExpanded)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DOWNSTREAM_STATIC_SET"
                : "ACTION_ROUTING_DOWNSTREAM_STATIC_SET";
        else if (staticTables.Count > 0 && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_STATIC_SET_WITH_PATTERN"
                : "ACTION_ROUTING_STATIC_SET_WITH_PATTERN";
        else if (staticTables.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_STATIC_SET" : "ACTION_ROUTING_STATIC_SET";
        else if (actions.Count > 0 && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_EXACT_SET_WITH_PATTERN"
                : "ACTION_ROUTING_EXACT_SET_WITH_PATTERN";
        else if (actions.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_EXACT_SET" : "ACTION_ROUTING_EXACT_SET";
        else if (patterns.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_PATTERN" : "ACTION_ROUTING_PATTERN";
        else
            recipe = "ONACTION_FULL_STREAM_OR_UNRESOLVED";

        var actionRouteAutomatable =
            hasRoutableInterest &&
            !dynamicActionForward &&
            unresolvedActionSelectors.Count == 0 &&
            !prefilterSideEffect &&
            !isOverride &&
            blockers.All(x => !x.Contains("writes outside", StringComparison.OrdinalIgnoreCase));

        var overridePrefilterAutomatable =
            overridePrefilterProven &&
            !dynamicActionForward &&
            unresolvedActionSelectors.Count == 0 &&
            blockers.All(x => !x.Contains("writes outside", StringComparison.OrdinalIgnoreCase));

        // Routing semantics and callback-local hotpath semantics are independent.
        // If action routing cannot be proven (notably Override or dynamic
        // downstream callbacks), still allow the same source-proven structural
        // rewrite used by other callback families. This never changes action
        // delivery or Override behavior.
        StructuralHotpathResolution? structural = null;
        if (!actionRouteAutomatable &&
            TryResolveStructuralHotpath(source, callback, out var actionStructural))
        {
            structural = actionStructural;
            evidence.AddRange(actionStructural.Evidence);
        }

        var automatable =
            actionRouteAutomatable ||
            overridePrefilterAutomatable;
        var effectivePattern = actionRouteAutomatable
            ? recipe
            : overridePrefilterAutomatable
                ? "ACTION_OVERRIDE_EXACT_PREFILTER"
                : structural is not null
                    ? "STRUCTURAL_HOTPATH_EVIDENCE"
                    : recipe;

        var recipeFamilies = new List<string>();
        if (overridePrefilterAutomatable)
            recipeFamilies.Add("ACTION_OVERRIDE_EXACT_PREFILTER");
        else if (actionRouteAutomatable)
            recipeFamilies.Add(recipe);

        if (structural is not null)
            blockers.Add("Structural hotpath rewrite is analysis-only until a semantic per-mod rule authorizes it.");

        var facts = new
        {
            actions = actions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            actionPatterns = patterns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            stateGatePresent = stateGated,
            consumerMutation,
            dynamicActionForward,
            gatedWildcardResolved,
            dynamicGateExpression,
            downstreamExpanded,
            downstreamMethods,
            downstreamFiles,
            requiresActionType =
                Regex.IsMatch(window, @"\bGetType\s*\(", RegexOptions.IgnoreCase),
            requiresActionValue =
                Regex.IsMatch(window, @"\bGetValue\s*\(", RegexOptions.IgnoreCase),
            unresolvedActionSelectors = unresolvedActionSelectors.OrderBy(x => x).ToArray(),
            structuralHotpath = structural is not null,
            identicalExpressions = structural?.IdenticalExpressions ?? Array.Empty<StructuralExpression>(),
            literalConstructors = structural?.LiteralConstructors ?? Array.Empty<StructuralExpression>(),
            staticLiteralTables = structural?.StaticLiteralTables ?? Array.Empty<StaticLiteralTable>(),
            estimatedCallbackPaybackPct = structural?.EstimatedCallbackPaybackPct ?? 0,
            estimatedGlobalPaybackPct = structural?.EstimatedGlobalPaybackPct ?? 0,
            overridePrefilterProven,
            overrideWrappedMethodReturns,
            overrideWrappedMethodTakesSelf,
            overridePrefilterGateReceiver = downstreamPrefilterGateReceiver,
            overridePrefilterGateMember = downstreamPrefilterGateMember
        };

        return new GenericResolution
        {
            Status = automatable
                ? "RESOLVED"
                : structural is not null
                    ? "ANALYSIS_ONLY"
                    : hasRoutableInterest || dynamicActionForward ? "RECOGNIZED_WITH_BLOCKER" : "UNRESOLVED",
            Automatable = automatable,
            Pattern = effectivePattern,
            RecipeFamilies = recipeFamilies.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Facts = facts,
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = sourceEvidence
        };
    }

    private static bool TryProveTransparentOverrideWrapper(
        string window,
        IReadOnlyCollection<string> downstreamMethods,
        out bool wrappedMethodReturns,
        out bool wrappedMethodTakesSelf)
    {
        wrappedMethodReturns = false;
        wrappedMethodTakesSelf = false;
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var meaningful = lines
            .Select((text, index) => new { Text = text.Trim(), Index = index })
            .Where(x => !string.IsNullOrWhiteSpace(x.Text) && !x.Text.StartsWith("--", StringComparison.Ordinal))
            .ToList();

        var wrapped = meaningful
            .Where(x => Regex.IsMatch(
                x.Text,
                @"^(?:return\s+)?wrappedMethod\s*\(\s*(?:self\s*,\s*)?action\s*,\s*consumer\s*\)\s*;?\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        if (wrapped.Count != 1)
            return false;

        var wrappedEntry = wrapped[0];
        wrappedMethodReturns = wrappedEntry.Text.StartsWith("return ", StringComparison.OrdinalIgnoreCase);
        wrappedMethodTakesSelf = Regex.IsMatch(
            wrappedEntry.Text,
            @"wrappedMethod\s*\(\s*self\s*,",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // The original-game call must be the final meaningful statement before
        // the Override's closing end). This guarantees the fast path preserves
        // the original-game call count and ordering for irrelevant actions.
        var after = meaningful
            .Where(x => x.Index > wrappedEntry.Index)
            .Select(x => x.Text)
            .Where(x => !Regex.IsMatch(x, @"^end\s*\)\s*;?\s*$", RegexOptions.CultureInvariant))
            .ToArray();
        if (after.Length != 0)
            return false;

        foreach (var entry in meaningful)
        {
            if (entry.Index == wrappedEntry.Index ||
                entry.Text.Contains("Override(", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(entry.Text, @"^end\s*\)\s*;?\s*$", RegexOptions.CultureInvariant) ||
                Regex.IsMatch(entry.Text, @"^if\s+.+\s+then\s*$", RegexOptions.IgnoreCase) ||
                entry.Text.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            if (downstreamMethods.Any(method =>
                    Regex.IsMatch(
                        entry.Text,
                        @"(?:[:.]\s*)?" + Regex.Escape(method) + @"\s*\([^\r\n]*\baction\b",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                continue;

            // Side-effect-free locals are allowed, though the Billiards-style
            // wrapper normally has none.
            if (Regex.IsMatch(
                    entry.Text,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*[^=]+$",
                    RegexOptions.CultureInvariant))
                continue;

            return false;
        }

        return true;
    }

    private static List<string> ReadFiniteStringValues(string fullText, string variable)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     fullText,
                     @"\b" + Regex.Escape(variable) + @"\s*=\s*['""](?<value>[^'""]+)['""]",
                     RegexOptions.CultureInvariant))
        {
            values.Add(match.Groups["value"].Value);
        }

        foreach (Match match in Regex.Matches(
                     fullText,
                     @"\b" + Regex.Escape(variable) + @"\s*=\s*CName\.new\s*\(\s*['""](?<value>[^'""]+)['""]\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            values.Add(match.Groups["value"].Value);
        }

        return values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasDynamicActionForward(string window)
    {
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (IsRawActionForwardLine(line))
                return true;
        }

        return false;
    }

    private static bool TryResolveSimpleDynamicGate(
        string window,
        IReadOnlySet<string> nameVars,
        out string gateExpression)
    {
        gateExpression = "";
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var dynamicLines = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsRawActionForwardLine(lines[i]))
                dynamicLines.Add(i);
        }

        if (dynamicLines.Count == 0)
            return false;

        var protectedRanges = new List<(int Start, int End)>();
        var gateGroups = new List<string>();

        foreach (var index in dynamicLines)
        {
            var enclosing = new List<(int Start, int End, string Expr)>();

            for (var i = 0; i < index; i++)
            {
                var match = Regex.Match(
                    lines[i],
                    @"^\s*if\s+(?<expr>(?:not\s+)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                var end = FindIndentedBlockEnd(lines, i);
                if (end >= index)
                    enclosing.Add((i, end, match.Groups["expr"].Value.Trim()));
            }

            if (enclosing.Count == 0)
                return false;

            var ordered = enclosing.OrderBy(x => x.Start).ToList();
            gateGroups.Add("(" + string.Join(" and ", ordered.Select(x => x.Expr)) + ")");

            // Protect the outermost proven gate. Its complete body is only
            // entered when the same generated prefilter evaluates true.
            protectedRanges.Add((ordered[0].Start, ordered[0].End));
        }

        // Exact action branches outside the dynamic gate remain independently
        // routable and are protected as complete blocks.
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isExactBranch = nameVars.Any(name =>
                Regex.IsMatch(
                    line,
                    @"^\s*if\s+" + Regex.Escape(name) +
                    @"\s*==\s*['""][^'""]+['""]\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"^\s*if\s+['""][^'""]+['""]\s*==\s*" +
                    Regex.Escape(name) + @"\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

            if (!isExactBranch)
                continue;

            var end = FindIndentedBlockEnd(lines, i);
            if (end > i)
                protectedRanges.Add((i, end));
        }

        bool IsProtected(int line) =>
            protectedRanges.Any(range => line >= range.Start && line <= range.End);

        // Outside proven exact/gated branches, permit only wrapper syntax and
        // side-effect-free local decoding/reads. Any other call or write means
        // we cannot move the callback behind a generated prefilter.
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsProtected(i))
                continue;

            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end)", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*[^()]+$",
                    RegexOptions.CultureInvariant))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*.*(?:GetName|GetType|GetValue|NameToString)\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            return false;
        }

        gateExpression = string.Join(
            " or ",
            gateGroups
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
        return !string.IsNullOrWhiteSpace(gateExpression);
    }

    private static int FindIndentedBlockEnd(IReadOnlyList<string> lines, int start)
    {
        if (start < 0 || start >= lines.Count)
            return -1;

        var indent = lines[start].Length - lines[start].TrimStart().Length;
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

    private static bool IsRawActionForwardLine(string raw)
    {
        var line = raw.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
            return false;
        if (!Regex.IsMatch(line, @"\([^\r\n)]*\baction\b", RegexOptions.IgnoreCase))
            return false;

        return
            !line.Contains("GetName", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("GetType", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("GetValue", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("IsAction", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("NameToString", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("Override(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveStaticDownstreamActionInterest(
        string window,
        string fullSource,
        IReadOnlySet<string> nameVars,
        string owner,
        LiveSourceIndex sourceIndex,
        out string[] actions,
        out string[] methods,
        out string[] files,
        out string prefilterGateReceiver,
        out string prefilterGateMember)
    {
        actions = Array.Empty<string>();
        methods = Array.Empty<string>();
        files = Array.Empty<string>();
        prefilterGateReceiver = "";
        prefilterGateMember = "";

        var forwards = new List<ForwardedActionCall>();

        foreach (Match match in Regex.Matches(
                     window,
                     @"(?:(?<receiver>[A-Za-z_]\w*)\s*[:.]\s*)?(?<method>[A-Za-z_]\w*)\s*\((?<args>[^()\r\n]*)\)",
                     RegexOptions.CultureInvariant))
        {
            var method = match.Groups["method"].Value;
            if (method.Equals("GetName", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("GetType", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("GetValue", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("IsAction", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("NameToString", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("Observe", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("ObserveAfter", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("Override", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("function", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("wrappedMethod", StringComparison.OrdinalIgnoreCase))
                continue;

            var callArgs = match.Groups["args"].Value
                .Split(',')
                .Select(x => x.Trim())
                .ToArray();

            var rawActionIndex = Array.FindIndex(
                callArgs,
                x => x.Equals("action", StringComparison.Ordinal));
            if (rawActionIndex < 0)
                continue;

            var nameIndex = Array.FindIndex(
                callArgs,
                x => nameVars.Contains(x));

            forwards.Add(new ForwardedActionCall(
                method,
                nameIndex,
                rawActionIndex,
                match.Groups["receiver"].Value));
        }

        if (forwards.Count == 0)
            return false;

        var allActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var forward in forwards
                     .DistinctBy(
                         x => $"{x.Receiver}|{x.Method}|{x.NameArgumentIndex}|{x.RawActionArgumentIndex}",
                         StringComparer.OrdinalIgnoreCase))
        {
            var receiverType = TryResolveReceiverType(
                window,
                fullSource,
                forward.Receiver);

            var definitions = sourceIndex.FindOwnerMethodDefinitions(
                owner,
                forward.Method,
                receiverType);

            if (definitions.Count == 0)
                return false;

            methodNames.Add(forward.Method);

            foreach (var definition in definitions)
            {
                if (forward.RawActionArgumentIndex >= definition.Parameters.Length)
                    return false;

                var actionParameter = definition.Parameters[forward.RawActionArgumentIndex];
                if (string.IsNullOrWhiteSpace(actionParameter))
                    return false;

                string[] definitionActions;
                var definitionPrefilterGateMember = "";
                if (forward.NameArgumentIndex >= 0)
                {
                    if (forward.NameArgumentIndex >= definition.Parameters.Length)
                        return false;

                    var nameParameter = definition.Parameters[forward.NameArgumentIndex];
                    if (string.IsNullOrWhiteSpace(nameParameter) ||
                        !TryReadFiniteDownstreamActionSet(
                            definition.Body,
                            nameParameter,
                            actionParameter,
                            owner,
                            definition.RelativeFile,
                            sourceIndex,
                            "",
                            out definitionActions))
                        return false;
                }
                else if (!TryReadFiniteRawActionDownstreamSet(
                             definition.Body,
                             actionParameter,
                             owner,
                             definition.RelativeFile,
                             sourceIndex,
                             out definitionActions,
                             out definitionPrefilterGateMember))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(definitionPrefilterGateMember))
                    forward.PrefilterGateMember = definitionPrefilterGateMember;

                foreach (var action in definitionActions)
                    allActions.Add(action);
                sourceFiles.Add(definition.RelativeFile);

                if (!string.IsNullOrWhiteSpace(forward.PrefilterGateMember))
                {
                    if (string.IsNullOrWhiteSpace(forward.Receiver))
                        return false;

                    if (!string.IsNullOrWhiteSpace(prefilterGateMember) &&
                        (!prefilterGateMember.Equals(forward.PrefilterGateMember, StringComparison.Ordinal) ||
                         !prefilterGateReceiver.Equals(forward.Receiver, StringComparison.Ordinal)))
                        return false;

                    prefilterGateMember = forward.PrefilterGateMember;
                    prefilterGateReceiver = forward.Receiver;
                }
            }
        }

        if (allActions.Count == 0)
            return false;

        actions = allActions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        methods = methodNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        files = sourceFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static string TryResolveReceiverType(
        string callbackWindow,
        string fullSource,
        string receiver)
    {
        if (string.IsNullOrWhiteSpace(receiver))
            return "";

        // Example:
        // local current = NightCityBilliardsRuntime
        // NightCityBilliardsRuntime = Runtime
        var localAlias = Regex.Match(
            callbackWindow,
            @"(?m)^\s*local\s+" + Regex.Escape(receiver) +
            @"\s*=\s*(?<alias>[A-Za-z_]\w*)\s*$",
            RegexOptions.CultureInvariant);
        if (!localAlias.Success)
            return "";

        var alias = localAlias.Groups["alias"].Value;

        var assignment = Regex.Match(
            fullSource,
            @"(?m)^\s*" + Regex.Escape(alias) +
            @"\s*=\s*(?<type>[A-Za-z_]\w*)\s*$",
            RegexOptions.CultureInvariant);
        if (!assignment.Success)
            return "";

        return assignment.Groups["type"].Value;
    }

    private static bool TryReadFiniteRawActionDownstreamSet(
        string body,
        string actionParameter,
        string owner,
        string relativeFile,
        LiveSourceIndex sourceIndex,
        out string[] actions,
        out string prefilterGateMember)
    {
        actions = Array.Empty<string>();
        prefilterGateMember = "";
        var action = Regex.Escape(actionParameter);

        var nameVariables = new HashSet<string>(StringComparer.Ordinal);

        // Direct decode:
        // local name = Game.NameToString(action:GetName())
        foreach (Match match in Regex.Matches(
                     body,
                     @"(?m)\blocal\s+(?<name>[A-Za-z_]\w*)\s*=\s*(?:Game\.NameToString\s*\(\s*)?" +
                     action +
                     @"\s*[:.]\s*GetName\s*\(\s*\)\s*\)?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            nameVariables.Add(match.Groups["name"].Value);
        }

        // Protected decode:
        // local ok, name = pcall(function() return Game.NameToString(action:GetName()) end)
        foreach (Match match in Regex.Matches(
                     body,
                     @"(?s)\blocal\s+(?<ok>[A-Za-z_]\w*)\s*,\s*(?<name>[A-Za-z_]\w*)\s*=\s*pcall\s*\(\s*function\s*\(\s*\)\s*return\s+(?:Game\.NameToString\s*\(\s*)?" +
                     action +
                     @"\s*[:.]\s*GetName\s*\(\s*\)\s*\)?\s*end\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            nameVariables.Add(match.Groups["name"].Value);
        }

        foreach (var nameVariable in nameVariables)
        {
            var gateMember = "";
            var diagnosticGate = Regex.Match(
                body,
                @"(?m)^\s*if\s+[A-Za-z_]\w*\s+and\s+self\.(?<member>[A-Za-z_]\w*)\s+then\s+self:[A-Za-z_]\w*\s*\(\s*" +
                Regex.Escape(nameVariable) +
                @"\s*\)\s*end\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (diagnosticGate.Success)
                gateMember = diagnosticGate.Groups["member"].Value;

            if (TryReadFiniteDownstreamActionSet(
                    body,
                    nameVariable,
                    actionParameter,
                    owner,
                    relativeFile,
                    sourceIndex,
                    gateMember,
                    out actions))
            {
                prefilterGateMember = gateMember;
                return true;
            }
        }

        return false;
    }

    private static bool TryReadFiniteDownstreamActionSet(
        string body,
        string nameParameter,
        string actionParameter,
        string owner,
        string relativeFile,
        LiveSourceIndex sourceIndex,
        string allowedPrefilterGateMember,
        out string[] actions)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = Regex.Escape(nameParameter);

        foreach (Match match in Regex.Matches(
                     body,
                     @"\b" + name + @"\s*==\s*['""](?<action>[^'""]+)['""]",
                     RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"['""](?<action>[^'""]+)['""]\s*==\s*\b" + name + @"\b",
                     RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"(?m)^\s*if\s+" + name + @"\s*~=\s*['""](?<action>[^'""]+)['""]\s+then\s+return(?:\s+.*)?\s+end\s*$",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        var downstreamStaticTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     body,
                     @"\b(?<table>[A-Za-z_]\w*)\s*\[\s*" + name + @"\s*\]",
                     RegexOptions.CultureInvariant))
        {
            var table = match.Groups["table"].Value;
            List<string> tableActions;
            bool dynamicWrites;

            var resolvedLocal =
                TryReadStaticStringSet(body, table, out tableActions, out dynamicWrites) &&
                !dynamicWrites;

            if (!resolvedLocal &&
                !sourceIndex.TryResolveOwnerStaticStringSet(
                    owner,
                    relativeFile,
                    table,
                    out tableActions))
            {
                actions = Array.Empty<string>();
                return false;
            }

            downstreamStaticTables.Add(table);
            foreach (var action in tableActions)
                found.Add(action);
        }

        var guardedRanges = new List<(int Start, int End)>();
        var bodyLines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < bodyLines.Length; i++)
        {
            var line = bodyLines[i];
            if (!downstreamStaticTables.Any(table =>
                    Regex.IsMatch(
                        line,
                        @"^\s*if\s+.*\b" + Regex.Escape(table) +
                        @"\s*\[\s*" + name + @"\s*\].*\s+then\s*$",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                continue;

            var indent = line.Length - line.TrimStart().Length;
            var end = FindSameIndentEnd(bodyLines, i, indent);
            if (end > i)
                guardedRanges.Add((i, end));
        }

        if (found.Count == 0)
        {
            actions = Array.Empty<string>();
            return false;
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"\b" + name + @"\s*~=\s*['""][^'""]+['""]",
                     RegexOptions.CultureInvariant))
        {
            var lineStart = body.LastIndexOf('\n', Math.Max(0, match.Index - 1));
            var lineEnd = body.IndexOf('\n', match.Index);
            var lineOffset = lineStart < 0 ? 0 : lineStart + 1;
            var line = body.Substring(
                lineOffset,
                (lineEnd < 0 ? body.Length : lineEnd) - lineOffset);

            if (!Regex.IsMatch(
                    line,
                    @"^\s*if\s+.*~=.*\s+then\s+return(?:\s+.*)?\s+end\s*$",
                    RegexOptions.IgnoreCase))
            {
                actions = Array.Empty<string>();
                return false;
            }
        }

        for (var lineIndex = 0; lineIndex < bodyLines.Length; lineIndex++)
        {
            var line = bodyLines[lineIndex].Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                !Regex.IsMatch(line, @"\b" + name + @"\b"))
                continue;

            if (guardedRanges.Any(x => lineIndex >= x.Start && lineIndex <= x.End))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+" + name + @"\s*=\s*(?:Game\.NameToString\s*\(\s*)?" +
                    Regex.Escape(actionParameter) +
                    @"\s*[:.]\s*GetName\s*\(\s*\)\s*\)?\s*;?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*,\s*" + name +
                    @"\s*=\s*pcall\s*\(\s*function\s*\(\s*\)\s*return\s+(?:Game\.NameToString\s*\(\s*)?" +
                    Regex.Escape(actionParameter) +
                    @"\s*[:.]\s*GetName\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"\b" + name + @"\s*(?:==|~=)\s*['""]",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"['""][^'""]+['""]\s*==\s*\b" + name + @"\b",
                    RegexOptions.CultureInvariant) ||
                (!string.IsNullOrWhiteSpace(allowedPrefilterGateMember) &&
                 Regex.IsMatch(
                     line,
                     @"^if\s+[A-Za-z_]\w*\s+and\s+self\." +
                     Regex.Escape(allowedPrefilterGateMember) +
                     @"\s+then\s+self:[A-Za-z_]\w*\s*\(\s*" + name + @"\s*\)\s*end\s*$",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) ||
                downstreamStaticTables.Any(table =>
                    Regex.IsMatch(
                        line,
                        @"\b" + Regex.Escape(table) + @"\s*\[\s*" + name + @"\s*\]",
                        RegexOptions.CultureInvariant)) ||
                Regex.IsMatch(
                    line,
                    @"^function\b.*\b" + name + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            actions = Array.Empty<string>();
            return false;
        }

        for (var lineIndex = 0; lineIndex < bodyLines.Length; lineIndex++)
        {
            var line = bodyLines[lineIndex].Trim();
            var action = Regex.Escape(actionParameter);
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                !Regex.IsMatch(line, @"\b" + action + @"\b"))
                continue;

            if (guardedRanges.Any(x => lineIndex >= x.Start && lineIndex <= x.End))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^(?:return\s+)?wrappedMethod\s*\(\s*" + action +
                    @"\s*,\s*[A-Za-z_]\w*\s*\)\s*;?\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^function\b.*\b" + action + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"\b" + action + @"\s*[:.]\s*(?:GetName|GetType|GetValue|IsAction)\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"\([^\r\n)]*\b" + action + @"\b",
                    RegexOptions.CultureInvariant))
            {
                actions = Array.Empty<string>();
                return false;
            }
        }

        actions = found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private sealed class ForwardedActionCall
    {
        public ForwardedActionCall(
            string method,
            int nameArgumentIndex,
            int rawActionArgumentIndex,
            string receiver)
        {
            Method = method;
            NameArgumentIndex = nameArgumentIndex;
            RawActionArgumentIndex = rawActionArgumentIndex;
            Receiver = receiver;
        }

        public string Method { get; }
        public int NameArgumentIndex { get; }
        public int RawActionArgumentIndex { get; }
        public string Receiver { get; }
        public string PrefilterGateMember { get; set; } = "";
    }

    private static bool HasMeaningfulWorkBeforeFirstActionFilter(
        string window,
        IReadOnlySet<string> nameVars,
        IReadOnlyCollection<string>? resolvedForwardMethods = null)
    {
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstFilter = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isFilter =
                line.Contains("IsAction(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("GetName()", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("==", StringComparison.Ordinal) || line.Contains("~=", StringComparison.Ordinal)) ||
                nameVars.Any(v =>
                    line.Contains(v + " ==", StringComparison.Ordinal) ||
                    line.Contains(v + " ~=", StringComparison.Ordinal)) ||
                line.Contains("string.find", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(":find(", StringComparison.OrdinalIgnoreCase) ||
                nameVars.Any(v => Regex.IsMatch(line, @"\[[\s]*" + Regex.Escape(v) + @"[\s]*\]"));

            if (isFilter)
            {
                firstFilter = i;
                break;
            }
        }

        if (firstFilter <= 0)
            return false;

        for (var i = 0; i < firstFilter; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ObserveAfter(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Override(", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("function", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s+return(?:\s+.*)?\s+end\s*$", RegexOptions.IgnoreCase))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s*$", RegexOptions.IgnoreCase) ||
                line.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            // A downstream call whose complete action interest was proven by
            // owner-local source inspection is itself the expanded filter.
            if (resolvedForwardMethods is not null &&
                resolvedForwardMethods.Any(method =>
                    Regex.IsMatch(
                        line,
                        @"[:.]\s*" + Regex.Escape(method) + @"\s*\(",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                continue;

            // Local reads/decodes are allowed before the filter. They disappear
            // for unrelated actions after routing but have no externally visible
            // assignment target. Restrict call-shaped locals to getter/read names.
            if (Regex.IsMatch(line, @"^local\s+[A-Za-z_]\w*\s*(?:,\s*[A-Za-z_]\w*)*\s*$"))
                continue;
            if (Regex.IsMatch(line, @"^local\s+[A-Za-z_]\w*\s*=\s*[^()]+$"))
                continue;
            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*(?:Game\.NameToString\s*\(|GetSingleton\s*\(|[A-Za-z_][\w.]*[.:](?:Get|get|Is|is|Has|has)[A-Za-z_\w]*\s*\()",
                    RegexOptions.IgnoreCase))
                continue;

            if (Regex.IsMatch(line, @"[A-Za-z_][\w.:]*\s*\(") ||
                Regex.IsMatch(line, @"^(?!local\b)[A-Za-z_][\w.\[\]]*\s*="))
                return true;
        }

        return false;
    }

    private static bool TryReadStaticStringSet(
        string fullText,
        string tableName,
        out List<string> actions,
        out bool dynamicWrites)
    {
        actions = new List<string>();
        dynamicWrites = false;

        var declaration = Regex.Match(
            fullText,
            @"(?:local\s+)?" + Regex.Escape(tableName) + @"\s*=\s*\{",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return false;

        var open = fullText.IndexOf('{', declaration.Index);
        if (open < 0)
            return false;

        var depth = 0;
        var close = -1;
        var quote = '\0';
        var escaped = false;

        for (var i = open; i < fullText.Length; i++)
        {
            var c = fullText[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == '{') depth++;
            if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close <= open)
            return false;

        var body = fullText.Substring(open + 1, close - open - 1);
        foreach (Match match in Regex.Matches(
                     body,
                     @"\[\s*['""](?<key>[^'""]+)['""]\s*\]\s*=|(?<bare>[A-Za-z_]\w*)\s*=\s*true|['""](?<list>[^'""]+)['""]\s*,",
                     RegexOptions.CultureInvariant))
        {
            var value = match.Groups["key"].Success
                ? match.Groups["key"].Value
                : match.Groups["bare"].Success
                    ? match.Groups["bare"].Value
                    : match.Groups["list"].Value;
            if (!string.IsNullOrWhiteSpace(value))
                actions.Add(value);
        }

        if (actions.Count == 0)
            return false;

        var outside = fullText.Remove(open, close - open + 1);
        dynamicWrites =
            Regex.IsMatch(outside, @"\b" + Regex.Escape(tableName) + @"\s*\[.*?\]\s*=", RegexOptions.Singleline) ||
            Regex.IsMatch(outside, @"\btable\.(?:insert|remove)\s*\(\s*" + Regex.Escape(tableName) + @"\b");

        actions = actions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static IReadOnlyDictionary<string, CadenceDecision> ReadCadenceDecisions(string? path)
    {
        var result = new Dictionary<string, CadenceDecision>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("callbacks", out var callbacks) ||
                callbacks.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in callbacks.EnumerateArray())
            {
                var owner = JsonString(row, "Owner", "owner");
                var kind = JsonString(row, "Kind", "kind");
                var target = JsonString(row, "Target", "target");
                var group = JsonString(row, "Group", "group");
                var transform = JsonBool(row, "TransformCandidate", "transformCandidate");

                var medianUsPerCall = 0.0;
                var baselineWorkSharePct = 0.0;
                if (row.TryGetProperty("Runtime", out var runtime) ||
                    row.TryGetProperty("runtime", out runtime))
                {
                    if (runtime.ValueKind == JsonValueKind.Object)
                    {
                        medianUsPerCall = JsonDouble(runtime, "MedianUsPerCall", "medianUsPerCall");
                        baselineWorkSharePct = JsonDouble(runtime, "BaselineWorkSharePct", "baselineWorkSharePct");
                    }
                }

                var sourceFile = "";
                var registrationLine = 0;
                var callbackBodyEndLine = 0;
                if (row.TryGetProperty("Source", out var source) ||
                    row.TryGetProperty("source", out source))
                {
                    if (source.ValueKind == JsonValueKind.Object)
                    {
                        sourceFile = JsonString(source, "File", "file");
                        registrationLine = (int)(JsonNullableLong(
                            source,
                            "RegistrationLine",
                            "registrationLine") ?? 0);
                        callbackBodyEndLine = (int)(JsonNullableLong(
                            source,
                            "CallbackBodyEndLine",
                            "callbackBodyEndLine") ?? 0);
                    }
                }

                if (!string.IsNullOrWhiteSpace(owner))
                    result[CadenceKey(owner, kind, target)] =
                        new CadenceDecision(
                            group,
                            transform,
                            medianUsPerCall,
                            baselineWorkSharePct,
                            sourceFile,
                            registrationLine,
                            callbackBodyEndLine);
            }
        }
        catch
        {
        }

        return result;
    }

    private static List<CallbackMetric> ReadCallbacks(JsonElement root)
    {
        var result = new List<CallbackMetric>();
        if (!root.TryGetProperty("callbacks", out var callbacks) ||
            callbacks.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var row in callbacks.EnumerateArray())
        {
            var sourceFile = "";
            long? lineStart = null;
            long? lineEnd = null;
            if (row.TryGetProperty("source", out var source) &&
                source.ValueKind == JsonValueKind.Object)
            {
                sourceFile = JsonString(source, "file");
                lineStart = JsonNullableLong(source, "lineStart");
                lineEnd = JsonNullableLong(source, "lineEnd");
            }

            result.Add(new CallbackMetric
            {
                RegistrationId = JsonNullableLong(row, "registrationId"),
                Owner = JsonString(row, "owner", "Owner"),
                Infrastructure = JsonBool(row, "infrastructure", "Infrastructure"),
                Kind = JsonString(row, "kind", "Kind"),
                Target = JsonString(row, "target", "Target"),
                SourceFile = sourceFile,
                SourceLineStart = lineStart,
                SourceLineEnd = lineEnd,
                CallsPerSecond = JsonDouble(row, "callsPerSecond", "CallsPerSecond"),
                ExclusiveMsPerSecond = JsonDouble(row, "exclusiveMsPerSecond", "ExclusiveMsPerSecond"),
                GlobalWorkSharePct = JsonDouble(row, "globalWorkSharePct", "GlobalWorkSharePct"),
                FamilyWorkSharePct = JsonDouble(row, "familyWorkSharePct", "FamilyWorkSharePct"),
                AvgExclusiveUs = JsonDouble(row, "avgExclusiveUs", "AvgExclusiveUs"),
                MaxExclusiveMs = JsonDouble(row, "maxExclusiveMs", "MaxExclusiveMs"),
                SpikeCount = (long)JsonDouble(row, "spikeCount", "SpikeCount"),
                MaxSpikeExclusiveMs = JsonDouble(row, "maxSpikeExclusiveMs", "MaxSpikeExclusiveMs")
            });
        }

        return result;
    }

    private static string ClassifyCallbackFamily(string kind, string target)
    {
        if (target.Equals("onUpdate", StringComparison.OrdinalIgnoreCase))
            return "ONUPDATE";
        if (target.EndsWith("::OnAction", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("OnAction", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("OnAction", StringComparison.OrdinalIgnoreCase))
            return "ONACTION";
        return "OTHER";
    }

    private static string FamilyKey(string kind, string target) =>
        $"{kind.Trim().ToLowerInvariant()}::{target.Trim().ToLowerInvariant()}";

    private static string CadenceKey(string owner, string kind, string target) =>
        $"{owner}\u001f{kind}\u001f{target}";

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

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

    private sealed class LiveSourceIndex
    {
        private readonly string _modsRoot;
        private readonly List<string> _luaFiles;

        public LiveSourceIndex(string modsRoot)
        {
            _modsRoot = Path.GetFullPath(modsRoot);
            try
            {
                _luaFiles = Directory.EnumerateFiles(_modsRoot, "*.lua", SearchOption.AllDirectories)
                    .Take(20000)
                    .ToList();
            }
            catch
            {
                _luaFiles = new List<string>();
            }
        }

        public ResolvedSource? Resolve(CallbackMetric callback)
        {
            var source = (callback.SourceFile ?? "").Trim().TrimStart('@')
                .Replace('/', Path.DirectorySeparatorChar);
            var ownerFolder = ResolveOwnerFolder(callback.Owner);

            // Registration source frequently arrives as only "init.lua". That is
            // ambiguous across the whole CET tree but unambiguous inside the
            // measured owner. Prefer owner-relative provenance before any global
            // suffix search.
            if (!string.IsNullOrWhiteSpace(source) &&
                !Path.IsPathRooted(source) &&
                ownerFolder is not null)
            {
                var ownerRelative = Path.Combine(ownerFolder, source);
                if (File.Exists(ownerRelative))
                    return BuildResolvedSource(ownerRelative, "profiler-owner-relative", callback);
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                if (Path.IsPathRooted(source) && File.Exists(source) && IsInsideMods(source))
                    return BuildResolvedSource(source, "profiler-absolute", callback);

                var normalized = source.Replace('\\', '/');
                var modsIndex = normalized.IndexOf("/mods/", StringComparison.OrdinalIgnoreCase);
                if (modsIndex >= 0)
                {
                    var relative = normalized[(modsIndex + "/mods/".Length)..]
                        .Replace('/', Path.DirectorySeparatorChar);
                    var direct = Path.Combine(_modsRoot, relative);
                    if (File.Exists(direct))
                        return BuildResolvedSource(direct, "profiler-mods-relative", callback);
                }

                var combined = Path.Combine(_modsRoot, source);
                if (File.Exists(combined))
                    return BuildResolvedSource(combined, "profiler-relative", callback);

                var suffix = normalized.TrimStart('/');
                var suffixMatches = _luaFiles
                    .Where(x => x.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                if (suffixMatches.Count == 1)
                    return BuildResolvedSource(suffixMatches[0], "profiler-suffix", callback);
            }

            if (ownerFolder is not null)
            {
                var token = ClassifyCallbackFamily(callback.Kind, callback.Target) == "ONACTION"
                    ? "OnAction"
                    : callback.Target;
                var matches = Directory.EnumerateFiles(ownerFolder, "*.lua", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        try
                        {
                            return File.ReadAllText(path).Contains(token, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .Take(3)
                    .ToList();

                if (matches.Count == 1)
                    return BuildResolvedSource(matches[0], "owner-unique-token", callback);
            }

            return null;
        }

        public IReadOnlyList<OwnerMethodDefinition> FindOwnerMethodDefinitions(
            string owner,
            string methodName,
            string receiverType = "")
        {
            var result = new List<OwnerMethodDefinition>();
            var ownerFolder = ResolveOwnerFolder(owner);
            if (ownerFolder is null)
                return result;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(ownerFolder, "*.lua", SearchOption.AllDirectories);
            }
            catch
            {
                return result;
            }

            var methodPattern =
                @"^(?<indent>\s*)function\s+(?<receiver>[A-Za-z_][\w.]*)\s*[:.]\s*" +
                Regex.Escape(methodName) +
                @"\s*\((?<args>[^)]*)\)";

            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file)
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n');
                }
                catch
                {
                    continue;
                }

                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var match = Regex.Match(
                        lines[i],
                        methodPattern,
                        RegexOptions.CultureInvariant);
                    if (!match.Success)
                        continue;

                    var definitionReceiver = match.Groups["receiver"].Value;
                    if (!string.IsNullOrWhiteSpace(receiverType) &&
                        !definitionReceiver.Equals(receiverType, StringComparison.Ordinal))
                        continue;

                    var indent = match.Groups["indent"].Value.Length;
                    var endLine = -1;

                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var candidate = lines[j];
                        var trimmed = candidate.Trim();
                        if (!Regex.IsMatch(
                                trimmed,
                                @"^end\s*(?:--.*)?$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            continue;

                        var candidateIndent = candidate.Length - candidate.TrimStart().Length;
                        if (candidateIndent == indent)
                        {
                            endLine = j;
                            break;
                        }
                    }

                    if (endLine <= i)
                        continue;

                    var parameters = match.Groups["args"].Value
                        .Split(',')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();

                    result.Add(new OwnerMethodDefinition
                    {
                        Receiver = definitionReceiver,
                        RelativeFile = Path.GetRelativePath(_modsRoot, file).Replace('\\', '/'),
                        Parameters = parameters,
                        Body = string.Join("\n", lines.Skip(i).Take(endLine - i + 1))
                    });

                    i = endLine;
                }
            }

            return result;
        }

        public bool TryResolveOwnerStaticStringSet(
            string owner,
            string relativeFile,
            string tableName,
            out List<string> actions)
        {
            actions = new List<string>();
            var ownerFolder = ResolveOwnerFolder(owner);
            if (ownerFolder is null)
                return false;

            string sourceText;
            try
            {
                var sourcePath = Path.Combine(_modsRoot, relativeFile.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourcePath))
                    return false;
                sourceText = File.ReadAllText(sourcePath)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');
            }
            catch
            {
                return false;
            }

            if (TryReadStaticStringSet(sourceText, tableName, out var direct, out var directWrites) &&
                !directWrites)
            {
                actions = direct;
                return true;
            }

            // Follow a finite module alias such as:
            // local HANDLED_ACTIONS = Input.HANDLED_ACTIONS
            // local Input = needModule("runtime/input")
            var alias = Regex.Match(
                sourceText,
                @"(?m)^\s*local\s+" + Regex.Escape(tableName) +
                @"\s*=\s*(?<module>[A-Za-z_]\w*)\.(?<field>[A-Za-z_]\w*)\s*$",
                RegexOptions.CultureInvariant);
            if (!alias.Success)
                return false;

            var moduleName = alias.Groups["module"].Value;
            var fieldName = alias.Groups["field"].Value;

            var binding = Regex.Match(
                sourceText,
                @"(?m)^\s*local\s+" + Regex.Escape(moduleName) +
                @"\s*=\s*(?:require|needModule)\s*\(\s*['""](?<path>[^'""]+)['""]\s*\)\s*$",
                RegexOptions.CultureInvariant);
            if (!binding.Success)
                return false;

            var moduleRelative = binding.Groups["path"].Value
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (!moduleRelative.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                moduleRelative += ".lua";

            var modulePath = Path.Combine(ownerFolder, moduleRelative);
            if (!File.Exists(modulePath))
                return false;

            string moduleText;
            try
            {
                moduleText = File.ReadAllText(modulePath)
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n');
            }
            catch
            {
                return false;
            }

            if (!TryReadStaticStringSet(
                    moduleText,
                    moduleName + "." + fieldName,
                    out var resolved,
                    out var writes) ||
                writes)
                return false;

            actions = resolved;
            return actions.Count > 0;
        }

        private ResolvedSource? BuildResolvedSource(
            string path,
            string mode,
            CallbackMetric callback)
        {
            try
            {
                var full = File.ReadAllText(path);
                var lines = full.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var lineStart = callback.SourceLineStart is > 0
                    ? (int)Math.Min(callback.SourceLineStart.Value, lines.Length)
                    : FindTokenLine(lines, callback.Target);
                var lineEnd = callback.SourceLineEnd is > 0
                    ? (int)Math.Min(callback.SourceLineEnd.Value, lines.Length)
                    : Math.Min(lines.Length, Math.Max(1, lineStart) + 140);

                var callbackStart = Math.Max(1, lineStart);
                var callbackEnd = Math.Min(lines.Length, Math.Max(lineEnd, callbackStart));
                var callbackText = string.Join("\n", lines.Skip(callbackStart - 1).Take(callbackEnd - callbackStart + 1));

                var windowStart = Math.Max(1, lineStart - 3);
                var windowEnd = Math.Min(lines.Length, Math.Max(lineEnd + 3, windowStart + 40));
                var window = string.Join("\n", lines.Skip(windowStart - 1).Take(windowEnd - windowStart + 1));

                return new ResolvedSource
                {
                    Path = path,
                    RelativeFile = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/'),
                    Sha256 = Sha256(path),
                    FullText = full,
                    CallbackText = callbackText,
                    Window = window,
                    LineStart = lineStart > 0 ? lineStart : null,
                    LineEnd = lineEnd > 0 ? lineEnd : null,
                    MatchMode = mode
                };
            }
            catch
            {
                return null;
            }
        }

        private string? ResolveOwnerFolder(string owner)
        {
            var exact = Directory.EnumerateDirectories(_modsRoot)
                .FirstOrDefault(x => Path.GetFileName(x).Equals(owner, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;

            var normalized = Normalize(owner);
            var matches = Directory.EnumerateDirectories(_modsRoot)
                .Where(x => Normalize(Path.GetFileName(x)) == normalized)
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private bool IsInsideMods(string path)
        {
            var full = Path.GetFullPath(path);
            return full.StartsWith(_modsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int FindTokenLine(IReadOnlyList<string> lines, string target)
        {
            var token = target.Contains("OnAction", StringComparison.OrdinalIgnoreCase)
                ? "OnAction"
                : target;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains(token, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
            return 1;
        }

        private static string Normalize(string value) =>
            NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }


    private sealed class SharedProviderDefinition
    {
        public string Provider { get; init; } = "";
        public string Category { get; init; } = "";
        public Regex SourceRegex { get; init; } = null!;
        public string[] DeepFunctionNames { get; init; } = Array.Empty<string>();
        public bool RequiresPlayerReceiver { get; init; }

        public static SharedProviderDefinition Direct(
            string provider,
            string category,
            string sourcePattern,
            params string[] deepFunctionNames) => new()
        {
            Provider = provider,
            Category = category,
            SourceRegex = new Regex(
                sourcePattern,
                RegexOptions.Compiled | RegexOptions.CultureInvariant),
            DeepFunctionNames = deepFunctionNames,
            RequiresPlayerReceiver = false
        };

        public static SharedProviderDefinition PlayerDerived(
            string provider,
            string category,
            string functionName) => new()
        {
            Provider = provider,
            Category = category,
            SourceRegex = new Regex(
                @"\b[A-Za-z_]\w*\s*:\s*" + Regex.Escape(functionName) + @"\s*\(\s*\)",
                RegexOptions.Compiled | RegexOptions.CultureInvariant),
            DeepFunctionNames = new[] { functionName },
            RequiresPlayerReceiver = true
        };
    }

    private sealed class SharedProviderSourceMatch
    {
        public string Provider { get; init; } = "";
        public int Line { get; init; }
        public bool SourceRecognized { get; init; }
        public string Proof { get; init; } = "";
    }

    private sealed class SharedProviderSourceCallsite
    {
        public int Line { get; init; }
        public string Proof { get; init; } = "";
    }

    private sealed class SharedProviderDeepEvidence
    {
        public long SampledCalls { get; set; }
        public long RepeatedSameInvocationCount { get; set; }
        public long MultiCallsiteSampleCount { get; set; }
    }

    private sealed class SharedProviderDeepCallbackEvidence
    {
        public static SharedProviderDeepCallbackEvidence Empty { get; } = new();

        public IReadOnlyDictionary<string, SharedProviderDeepEvidence> ByProvider { get; init; } =
            new Dictionary<string, SharedProviderDeepEvidence>(
                StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SharedProviderCallbackEvidence
    {
        public long? RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public string SourceSha256 { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public int SourceOccurrences { get; init; }
        public int SourceRecognizedOccurrences { get; init; }
        public int SourceUnresolvedOccurrences { get; init; }
        public SharedProviderSourceCallsite[] SourceCallsites { get; init; } =
            Array.Empty<SharedProviderSourceCallsite>();
        public bool DeepObserved { get; init; }
        public long DeepSampledCalls { get; init; }
        public long DeepRepeatedSameInvocationCount { get; init; }
        public long DeepMultiCallsiteSampleCount { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public long SpikeCount { get; init; }
        public double MaxSpikeExclusiveMs { get; init; }
    }

    private sealed class SharedProviderOpportunity
    {
        public string Provider { get; init; } = "";
        public string Category { get; init; } = "";
        public int MeasuredOwnerCount { get; init; }
        public int MeasuredCallbackCount { get; init; }
        public int SourceOccurrences { get; init; }
        public int SourceRecognizedOccurrences { get; init; }
        public int SourceUnresolvedOccurrences { get; init; }
        public int CallbacksWithRepeatedSourceReads { get; init; }
        public int DeepObservedCallbackCount { get; init; }
        public long DeepSampledCalls { get; init; }
        public long DeepRepeatedSameInvocationCount { get; init; }
        public long DeepMultiCallsiteSampleCount { get; init; }
        public double AffectedCallbackWorkMsPerSecond { get; init; }
        public double AffectedCallbackCallsPerSecond { get; init; }
        public long AffectedSpikeCount { get; init; }
        public double MaxAffectedSpikeExclusiveMs { get; init; }
        public string[] Owners { get; init; } = Array.Empty<string>();
        public SharedProviderCallbackEvidence[] Callbacks { get; init; } =
            Array.Empty<SharedProviderCallbackEvidence>();
    }

    private sealed class CallbackMetric
    {
        public long? RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public bool Infrastructure { get; init; }
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public long? SourceLineStart { get; init; }
        public long? SourceLineEnd { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double GlobalWorkSharePct { get; init; }
        public double FamilyWorkSharePct { get; init; }
        public double AvgExclusiveUs { get; init; }
        public double MaxExclusiveMs { get; init; }
        public long SpikeCount { get; init; }
        public double MaxSpikeExclusiveMs { get; init; }

        public long? registrationId => RegistrationId;
        public string owner => Owner;
        public string kind => Kind;
        public string target => Target;
        public double callsPerSecond => Round(CallsPerSecond);
        public double exclusiveMsPerSecond => Round(ExclusiveMsPerSecond);
        public double globalWorkSharePct => Round(GlobalWorkSharePct);
        public double familyWorkSharePct => Round(FamilyWorkSharePct);
        public double avgExclusiveUs => Round(AvgExclusiveUs);
        public double maxExclusiveMs => Round(MaxExclusiveMs);
        public long spikeCount => SpikeCount;
        public double maxSpikeExclusiveMs => Round(MaxSpikeExclusiveMs);
    }

    private sealed class AuthorDiscoveryCadenceResolution
    {
        public string ActiveGate { get; init; } = "";
        public string Accumulator { get; init; } = "";
        public double IntervalSeconds { get; init; }
        public bool RegionSelfContained { get; init; }
        public string[] CapturedEarlierLocals { get; init; } = Array.Empty<string>();
    }

    private sealed class HardDormantGuardResolution
    {
        public string GateExpression { get; init; } = "";
        public int PreGuardReadCount { get; init; }
        public string[] StateWriters { get; init; } = Array.Empty<string>();
        public string[] WakeSignals { get; init; } = Array.Empty<string>();
        public string[] Evidence { get; init; } = Array.Empty<string>();
    }

    private sealed class StructuralExpression
    {
        public string Expression { get; init; } = "";
        public int Count { get; init; }
    }

    private sealed class StaticLiteralTable
    {
        public string Variable { get; init; } = "";
        public string Declaration { get; init; } = "";
        public string Literal { get; init; } = "";
        public int ElementCount { get; init; }
    }

    private sealed class StructuralHotpathResolution
    {
        public StructuralExpression[] IdenticalExpressions { get; init; } = Array.Empty<StructuralExpression>();
        public StructuralExpression[] LiteralConstructors { get; init; } = Array.Empty<StructuralExpression>();
        public StaticLiteralTable[] StaticLiteralTables { get; init; } = Array.Empty<StaticLiteralTable>();
        public double EstimatedCallbackPaybackPct { get; init; }
        public double EstimatedGlobalPaybackPct { get; init; }
        public string[] Evidence { get; init; } = Array.Empty<string>();
    }

    private sealed class AuthorCadenceResolution
    {
        public string DeltaParameter { get; init; } = "delta";
        public double BaseIntervalSeconds { get; init; }
        public double[] TimerIntervalsSeconds { get; init; } = Array.Empty<double>();
        public string[] AccumulatorVariables { get; init; } = Array.Empty<string>();
        public double ExpectedCallsPerSecond { get; init; }
        public double RuntimeEntryReductionFactor { get; init; }
        public double EstimatedAvoidablePollingMsPerSecond { get; init; }
        public double EstimatedCallbackPaybackPct { get; init; }
        public double EstimatedGlobalPaybackPct { get; init; }
    }

    private sealed record CadenceDecision(
        string Group,
        bool TransformCandidate,
        double MedianUsPerCall,
        double BaselineWorkSharePct,
        string SourceFile,
        int RegistrationLine,
        int CallbackBodyEndLine);

    private sealed class DormancyEvidence
    {
        public string Class { get; init; } = "UNKNOWN";
        public double Confidence { get; init; }
        public bool EvidenceOnly { get; init; } = true;
        public string[] ActiveSignals { get; init; } = Array.Empty<string>();
        public string[] WakeSignals { get; init; } = Array.Empty<string>();
        public string[] DiscoverySignals { get; init; } = Array.Empty<string>();
        public string[] BackgroundSignals { get; init; } = Array.Empty<string>();
        public string[] SensitiveSignals { get; init; } = Array.Empty<string>();
        public string[] StateWriterSignals { get; init; } = Array.Empty<string>();
        public bool CompleteWakePathProven { get; init; }
        public bool AuthorDiscoveryCadenceProven { get; init; }
        public double AuthorDiscoveryIntervalSeconds { get; init; }
        public string AuthorDiscoveryAccumulator { get; init; } = "";
        public string AuthorDiscoveryGate { get; init; } = "";
        public bool DiscoveryRegionSelfContained { get; init; }
        public string AuthorDiscoveryBlocker { get; init; } = "";
        public string[] Evidence { get; init; } = Array.Empty<string>();
        public string[] Blockers { get; init; } = Array.Empty<string>();

        public static DormancyEvidence Unknown(string blocker) => new()
        {
            Class = "UNKNOWN",
            Confidence = 0.0,
            EvidenceOnly = true,
            Blockers = new[] { blocker }
        };
    }

    private sealed class GenericResolution
    {
        public string Status { get; init; } = "";
        public bool Automatable { get; init; }
        public string Pattern { get; init; } = "";
        public string[] RecipeFamilies { get; init; } = Array.Empty<string>();
        public object? Facts { get; init; }
        public string[] Evidence { get; init; } = Array.Empty<string>();
        public string[] Blockers { get; init; } = Array.Empty<string>();
        public SourceEvidence? Source { get; init; }
    }

    private sealed class SourceEvidence
    {
        public string RelativeFile { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public string MatchMode { get; init; } = "";
    }

    private sealed class ResolvedSource
    {
        public string Path { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public string FullText { get; init; } = "";
        public string CallbackText { get; init; } = "";
        public string Window { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public string MatchMode { get; init; } = "";
    }

    private sealed class OwnerMethodDefinition
    {
        public string Receiver { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string[] Parameters { get; init; } = Array.Empty<string>();
        public string Body { get; init; } = "";
    }

}
