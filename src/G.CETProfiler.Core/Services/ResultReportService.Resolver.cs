using System.Globalization;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    /// <summary>
    /// Builds a measurement-only handoff for a future optimizer/resolver.
    /// It deliberately makes no pacing or transformation decisions.
    /// Everything here is derived from the already-captured profiler CSVs and
    /// optional aligned frametime data, so the native profiler hot path remains
    /// unchanged. Native callback registration IDs, Lua source ranges, and
    /// adaptive deep-profile evidence are preserved when the capture provides them.
    /// Deep line evidence is considered source-decision-ready only when every
    /// retained line event resolves to a real Lua source instead of a C frame.
    /// </summary>
    private static object BuildResolverInput(string captureRoot, ResultAnalysis a)
    {
        var detail = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Detail.csv"));
        var spikes = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Spikes.csv"));
        var timeline = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Timeline.csv"));
        var onUpdateTimeline = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_OnUpdateTimeline.csv"));
        var markers = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Markers.csv"));
        var deepRegistrations = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Registrations.csv"));
        var deepFunctions = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Functions.csv"));
        var deepEdges = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Edges.csv"));
        var deepSamples = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Samples.csv"));
        var deepLines = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Lines.csv"));

        var callbacks = detail
            .Select(r => new ResolverCallbackMetric
            {
                RegistrationId = L(r, "RegistrationId"),
                Owner = S(r, "Mod", "Owner"),
                Kind = S(r, "Kind"),
                Target = S(r, "Target"),
                SourceFile = S(r, "SourceFile"),
                SourceLineStart = L(r, "SourceLineStart"),
                SourceLineEnd = L(r, "SourceLineEnd"),
                Calls = L(r, "Calls"),
                CallsPerSecond = D(r, "CallsPerSecond"),
                ExclusiveMsPerSecond = D(r, "ExclusiveMsPerSecond", "MsPerSecond"),
                AvgExclusiveUs = D(r, "AvgExclusiveUs", "AvgUs"),
                MaxExclusiveMs = D(r, "MaxExclusiveMs", "MaxMs")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Owner) &&
                        !string.IsNullOrWhiteSpace(x.Target))
            .ToList();

        var spikeSamples = spikes
            .Select(r => new ResolverSpikeSample
            {
                RegistrationId = L(r, "RegistrationId"),
                Owner = S(r, "Mod", "Owner"),
                Kind = S(r, "Kind", "JobType"),
                Target = S(r, "Target", "Job"),
                SourceFile = S(r, "SourceFile"),
                SourceLineStart = L(r, "SourceLineStart"),
                SourceLineEnd = L(r, "SourceLineEnd"),
                CaptureStartMs = D(r, "CaptureStartMs", "CaptureMs"),
                CaptureEndMs = D(r, "CaptureEndMs", "CaptureMs"),
                ExclusiveMs = D(r, "ExclusiveMs", "DurationMs")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Owner) || x.ExclusiveMs > 0)
            .ToList();

        var spikeByCallback = spikeSamples
            .GroupBy(x => ResolverCallbackInstanceKey(
                x.RegistrationId, x.Owner, x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ResolverAggregateSpikes(g),
                StringComparer.OrdinalIgnoreCase);

        var spikeByFamily = spikeSamples
            .GroupBy(x => ResolverFamilyKey(x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ResolverAggregateSpikes(g),
                StringComparer.OrdinalIgnoreCase);

        var spikeByOwner = spikeSamples
            .GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ResolverAggregateSpikes(g),
                StringComparer.OrdinalIgnoreCase);

        var ownerByName = a.Owners.ToDictionary(
            x => x.Name,
            x => x,
            StringComparer.OrdinalIgnoreCase);

        var activity = BuildResolverOwnerActivity(timeline)
            .ToDictionary(x => x.Owner, x => x, StringComparer.OrdinalIgnoreCase);

        var families = callbacks
            .GroupBy(x => ResolverFamilyKey(x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToList();
                var first = rows[0];
                var familyMs = rows.Sum(x => x.ExclusiveMsPerSecond);
                var familyCalls = rows.Sum(x => x.CallsPerSecond);
                var familySpikes = spikeByFamily.GetValueOrDefault(g.Key) ?? new ResolverSpikeAggregate();

                return new
                {
                    kind = first.Kind,
                    target = first.Target,
                    owners = rows.Select(x => x.Owner)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    callsPerSecond = Round(familyCalls, 3),
                    globalCallSharePct = Round(Percent(familyCalls, a.TotalCallsPerSecond), 3),
                    exclusiveMsPerSecond = Round(familyMs, 6),
                    globalWorkSharePct = Round(Percent(familyMs, a.TotalMsPerSecond), 3),
                    maxExclusiveMs = Round(rows.Select(x => x.MaxExclusiveMs).DefaultIfEmpty(0).Max(), 6),
                    spikeCount = familySpikes.Count,
                    spikesPerSecond = Round(ResolverRate(familySpikes.Count, a.CaptureSeconds), 6),
                    spikeExclusiveMsPerSecond = Round(ResolverRate(familySpikes.TotalExclusiveMs, a.CaptureSeconds), 6),
                    maxSpikeExclusiveMs = Round(familySpikes.MaxExclusiveMs, 6),
                    topOwner = rows.OrderByDescending(x => x.ExclusiveMsPerSecond).First().Owner
                };
            })
            .OrderByDescending(x => x.exclusiveMsPerSecond)
            .ThenByDescending(x => x.callsPerSecond)
            .ToArray();

        var familyTotals = callbacks
            .GroupBy(x => ResolverFamilyKey(x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => new ResolverFamilyTotals
                {
                    OwnerCount = g.Select(x => x.Owner).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                    CallsPerSecond = g.Sum(x => x.CallsPerSecond),
                    ExclusiveMsPerSecond = g.Sum(x => x.ExclusiveMsPerSecond)
                },
                StringComparer.OrdinalIgnoreCase);

        var frameNormalizationAvailable =
            a.FrameTime is not null &&
            a.FrameTime.Correlated &&
            a.FrameTime.AverageFps > 0;

        var averageFps = frameNormalizationAvailable
            ? (double?)a.FrameTime!.AverageFps
            : null;

        var callbackRows = callbacks
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenByDescending(x => x.CallsPerSecond)
            .Select(x =>
            {
                var callbackKey = ResolverCallbackInstanceKey(
                    x.RegistrationId, x.Owner, x.Kind, x.Target);
                var familyKey = ResolverFamilyKey(x.Kind, x.Target);
                var callbackSpikes = spikeByCallback.GetValueOrDefault(callbackKey) ?? new ResolverSpikeAggregate();
                var family = familyTotals.GetValueOrDefault(familyKey) ?? new ResolverFamilyTotals();
                ownerByName.TryGetValue(x.Owner, out var owner);
                activity.TryGetValue(x.Owner, out var ownerActivity);

                return new
                {
                    registrationId = x.RegistrationId > 0 ? x.RegistrationId : (long?)null,
                    owner = x.Owner,
                    infrastructure = IsInfrastructureOwner(x.Owner),
                    kind = x.Kind,
                    target = x.Target,
                    source = string.IsNullOrWhiteSpace(x.SourceFile)
                        ? null
                        : new
                        {
                            file = x.SourceFile,
                            lineStart = x.SourceLineStart > 0 ? x.SourceLineStart : (long?)null,
                            lineEnd = x.SourceLineEnd > 0 ? x.SourceLineEnd : (long?)null
                        },
                    calls = x.Calls,
                    callsPerSecond = Round(x.CallsPerSecond, 3),
                    callsPerFrame = averageFps is double fps
                        ? Round(x.CallsPerSecond / fps, 6)
                        : (double?)null,
                    globalCallSharePct = Round(Percent(x.CallsPerSecond, a.TotalCallsPerSecond), 3),
                    familyCallSharePct = Round(Percent(x.CallsPerSecond, family.CallsPerSecond), 3),
                    ownerCallSharePct = Round(Percent(x.CallsPerSecond, owner?.CallsPerSecond ?? 0), 3),
                    exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                    globalWorkSharePct = Round(Percent(x.ExclusiveMsPerSecond, a.TotalMsPerSecond), 3),
                    familyWorkSharePct = Round(Percent(x.ExclusiveMsPerSecond, family.ExclusiveMsPerSecond), 3),
                    ownerWorkSharePct = Round(Percent(x.ExclusiveMsPerSecond, owner?.ExclusiveMsPerSecond ?? 0), 3),
                    avgExclusiveUs = Round(x.AvgExclusiveUs, 6),
                    maxExclusiveMs = Round(x.MaxExclusiveMs, 6),
                    familyOwnerCount = family.OwnerCount,
                    spikeCount = callbackSpikes.Count,
                    spikesPerSecond = Round(ResolverRate(callbackSpikes.Count, a.CaptureSeconds), 6),
                    spikeExclusiveMsPerSecond = Round(ResolverRate(callbackSpikes.TotalExclusiveMs, a.CaptureSeconds), 6),
                    maxSpikeExclusiveMs = Round(callbackSpikes.MaxExclusiveMs, 6),
                    ownerActivityBucketPct = ownerActivity is null
                        ? (double?)null
                        : Round(ownerActivity.ActiveBucketPct, 3),
                    ownerBurstRatio = ownerActivity is null
                        ? (double?)null
                        : Round(ownerActivity.BurstRatio, 6)
                };
            })
            .ToArray();

        var ownerRows = a.Owners
            .Select(x =>
            {
                var ownerSpikes = spikeByOwner.GetValueOrDefault(x.Name) ?? new ResolverSpikeAggregate();
                activity.TryGetValue(x.Name, out var ownerActivity);

                return new
                {
                    owner = x.Name,
                    infrastructure = IsInfrastructureOwner(x.Name),
                    callbacks = callbacks.Count(c => string.Equals(c.Owner, x.Name, StringComparison.OrdinalIgnoreCase)),
                    callsPerSecond = Round(x.CallsPerSecond, 3),
                    globalCallSharePct = Round(Percent(x.CallsPerSecond, a.TotalCallsPerSecond), 3),
                    exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                    globalWorkSharePct = Round(EffectiveShare(x, a.TotalMsPerSecond), 3),
                    avgExclusiveUs = Round(x.AvgExclusiveUs, 6),
                    maxExclusiveMs = Round(x.MaxExclusiveMs, 6),
                    spikeCount = ownerSpikes.Count,
                    spikesPerSecond = Round(ResolverRate(ownerSpikes.Count, a.CaptureSeconds), 6),
                    spikeExclusiveMsPerSecond = Round(ResolverRate(ownerSpikes.TotalExclusiveMs, a.CaptureSeconds), 6),
                    maxSpikeExclusiveMs = Round(ownerSpikes.MaxExclusiveMs, 6),
                    timeline = ownerActivity is null
                        ? null
                        : new
                        {
                            activeBuckets = ownerActivity.ActiveBuckets,
                            observedBuckets = ownerActivity.ObservedBuckets,
                            activeBucketPct = Round(ownerActivity.ActiveBucketPct, 3),
                            meanCallsPerActiveBucket = Round(ownerActivity.MeanCallsPerActiveBucket, 6),
                            meanExclusiveMsPerActiveBucket = Round(ownerActivity.MeanExclusiveMsPerActiveBucket, 6),
                            p95ExclusiveMsPerActiveBucket = Round(ownerActivity.P95ExclusiveMsPerActiveBucket, 6),
                            maxExclusiveMsPerBucket = Round(ownerActivity.MaxExclusiveMsPerBucket, 6),
                            burstRatio = Round(ownerActivity.BurstRatio, 6)
                        }
                };
            })
            .ToArray();

        var timelineBucketWidthMs = timeline
            .Select(r => D(r, "BucketWidthMs"))
            .FirstOrDefault(x => x > 0);

        var droppedTimelineRows = timeline
            .Select(r => L(r, "DroppedTimelineRowsAtDump"))
            .DefaultIfEmpty(0)
            .Max();

        var droppedOnUpdateTimelineRows = onUpdateTimeline
            .Select(r => L(r, "DroppedTimelineRowsAtDump"))
            .DefaultIfEmpty(0)
            .Max();

        var droppedSpikeEvents = spikes
            .Select(r => L(r, "DroppedEventsAtDump"))
            .DefaultIfEmpty(0)
            .Max();

        var spikeThreshold = spikes
            .Select(r => D(r, "ThresholdMs"))
            .FirstOrDefault(x => x > 0);

        var scenarioAnalysis = BuildResolverScenarios(
            markers,
            timeline,
            onUpdateTimeline,
            spikeSamples,
            a.FrameTime,
            a.CaptureSeconds);

        return new
        {
            schemaVersion = "1.5",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                contractVersion = "1.0",
                producer = "G-CET-Runtime-Profiler",
                consumer = "resolver",
                domain = "cet"
            },
            semantics = new
            {
                measurementOnly = true,
                classificationIncluded = false,
                pacingRecommendationIncluded = false,
                note = "The profiler measures and normalizes runtime evidence. A separate resolver decides whether and how to transform a mod."
            },
            quality = new
            {
                frameNormalizationAvailable,
                timelineAvailable = timeline.Count > 0,
                onUpdateTimelineAvailable = onUpdateTimeline.Count > 0,
                spikesAvailable = spikes.Count > 0,
                scenarioMarkersAvailable = scenarioAnalysis.RecognizedMarkers > 0,
                scenarioMarkersComplete = scenarioAnalysis.RecognizedMarkers > 0 && scenarioAnalysis.UnmatchedMarkers == 0,
                recognizedScenarioMarkers = scenarioAnalysis.RecognizedMarkers,
                unmatchedScenarioMarkers = scenarioAnalysis.UnmatchedMarkers,
                taggedScenarioSeconds = Round(scenarioAnalysis.TaggedSeconds, 3),
                untaggedCaptureSeconds = Round(scenarioAnalysis.UntaggedSeconds, 3),
                timelineBucketWidthMs = timelineBucketWidthMs > 0
                    ? Round(timelineBucketWidthMs, 6)
                    : (double?)null,
                spikeThresholdMs = spikeThreshold > 0
                    ? Round(spikeThreshold, 6)
                    : (double?)null,
                droppedTimelineRows,
                droppedOnUpdateTimelineRows,
                droppedSpikeEvents,
                callbackRegistrationIdsAvailable = callbacks.Any(x => x.RegistrationId > 0),
                callbackSourceLocationsAvailable = callbacks.Any(x => !string.IsNullOrWhiteSpace(x.SourceFile)),
                callbackSourceLocationCount = callbacks.Count(x => !string.IsNullOrWhiteSpace(x.SourceFile)),
                adaptiveDeepProfilingAvailable = deepRegistrations.Count > 0,
                adaptiveDeepFunctionsAvailable = deepFunctions.Count > 0,
                adaptiveDeepEdgesAvailable = deepEdges.Count > 0,
                adaptiveDeepSamplesAvailable = deepSamples.Count > 0,
                adaptiveDeepLinesAvailable = deepLines.Count > 0,
                adaptiveDeepDroppedSamples = deepSamples
                    .Select(row => L(row, "DroppedSamplesAtDump"))
                    .DefaultIfEmpty(0)
                    .Max(),
                adaptiveDeepDroppedLineRows = deepLines
                    .Select(row => L(row, "DroppedLineRowsAtDump"))
                    .DefaultIfEmpty(0)
                    .Max()
            },
            capture = new
            {
                title = ReadCaptureTitle(captureRoot),
                durationSeconds = Round(a.CaptureSeconds, 3),
                measuredCalls = a.TotalCalls,
                callsPerSecond = Round(a.TotalCallsPerSecond, 3),
                exclusiveMsPerSecond = Round(a.TotalMsPerSecond, 6),
                measuredOneCorePct = Round(a.TotalOneCorePct, 6),
                averageFps = averageFps is double fps ? Round(fps, 3) : (double?)null
            },
            scenarios = scenarioAnalysis.Scenarios,
            deepProfiling = new
            {
                available = deepRegistrations.Count > 0,
                mode = "adaptive-runtime-hotset-sampled-lua-call-return-line-path",
                note = "Broad callback timing remains authoritative. Deep function timing is composition evidence only; timestamped sample paths and line hits are the primary structural evidence for source decisions.",
                registrations = deepRegistrations
                    .Select(row => new
                    {
                        registrationId = L(row, "RegistrationId"),
                        owner = S(row, "Mod", "Owner"),
                        kind = S(row, "Kind"),
                        target = S(row, "Target"),
                        sourceFile = S(row, "SourceFile"),
                        sourceLineStart = L(row, "SourceLineStart"),
                        sourceLineEnd = L(row, "SourceLineEnd"),
                        luaFunctionIdentity = L(row, "LuaFunctionIdentity"),
                        profileEpoch = L(row, "ProfileEpoch"),
                        selected = L(row, "Selected") != 0,
                        complete = L(row, "Complete") != 0,
                        reusedFromRegistrationId = L(row, "ReusedFromRegistrationId"),
                        samples = L(row, "Samples"),
                        targetSamples = L(row, "TargetSamples"),
                        sampleStride = L(row, "SampleStride"),
                        hookConflicts = L(row, "HookConflicts"),
                        baselineAvgExclusiveUs = D(row, "BaselineAvgExclusiveUs"),
                        spikeArmed = L(row, "SpikeArmed") != 0,
                        spikeProbeStride = L(row, "SpikeProbeStride"),
                        spikeProbeSamples = L(row, "SpikeProbeSamples"),
                        spikeCaptures = L(row, "SpikeCaptures"),
                        driftWindows = L(row, "DriftWindows"),
                        driftDirection = L(row, "DriftDirection")
                    })
                    .OrderBy(x => x.registrationId)
                    .ToArray(),
                functions = deepFunctions
                    .Select(row => new
                    {
                        registrationId = L(row, "RegistrationId"),
                        profileEpoch = L(row, "ProfileEpoch"),
                        functionIdentity = L(row, "FunctionIdentity"),
                        functionKey = S(row, "FunctionKey"),
                        functionName = S(row, "FunctionName"),
                        what = S(row, "What"),
                        sourceFile = S(row, "SourceFile"),
                        sourceLineStart = L(row, "SourceLineStart"),
                        sourceLineEnd = L(row, "SourceLineEnd"),
                        minDepth = L(row, "MinDepth"),
                        calls = L(row, "Calls"),
                        inclusiveMs = D(row, "InclusiveMs"),
                        exclusiveMs = D(row, "ExclusiveMs"),
                        avgExclusiveUs = D(row, "AvgExclusiveUs"),
                        maxInclusiveMs = D(row, "MaxInclusiveMs")
                    })
                    .OrderBy(x => x.registrationId)
                    .ThenBy(x => x.profileEpoch)
                    .ThenByDescending(x => x.exclusiveMs)
                    .ToArray(),
                edges = deepEdges
                    .Select(row => new
                    {
                        registrationId = L(row, "RegistrationId"),
                        profileEpoch = L(row, "ProfileEpoch"),
                        parentFunctionKey = S(row, "ParentFunctionKey"),
                        childFunctionKey = S(row, "ChildFunctionKey"),
                        calls = L(row, "Calls"),
                        childInclusiveMs = D(row, "ChildInclusiveMs")
                    })
                    .OrderBy(x => x.registrationId)
                    .ThenBy(x => x.profileEpoch)
                    .ThenByDescending(x => x.childInclusiveMs)
                    .ToArray(),
                samples = deepSamples
                    .Select(row =>
                    {
                        var midpointMs =
                            D(row, "CaptureStartMs") +
                            Math.Max(0, D(row, "CaptureEndMs") - D(row, "CaptureStartMs")) * 0.5;
                        return new
                        {
                            sampleSequence = L(row, "SampleSequence"),
                            registrationId = L(row, "RegistrationId"),
                            profileEpoch = L(row, "ProfileEpoch"),
                            captureStartMs = D(row, "CaptureStartMs"),
                            captureEndMs = D(row, "CaptureEndMs"),
                            scenario = ResolverScenarioAt(midpointMs, scenarioAnalysis),
                            mode = S(row, "Mode"),
                            approxOwnWallMs = D(row, "ApproxOwnWallMs"),
                            hookEvents = L(row, "HookEvents"),
                            lineEvents = L(row, "LineEvents"),
                            unresolvedLineEvents = L(row, "UnresolvedLineEvents"),
                            uniqueLines = L(row, "UniqueLines"),
                            pathTransitions = L(row, "PathTransitions"),
                            pathFingerprint = S(row, "PathFingerprint"),
                            nestedRegistrationCount = L(row, "NestedRegistrationCount"),
                            nestedRegistrationMs = D(row, "NestedRegistrationMs"),
                            lineRowsTruncated = L(row, "LineRowsTruncated") != 0
                        };
                    })
                    .OrderBy(x => x.captureStartMs)
                    .ThenBy(x => x.sampleSequence)
                    .ToArray(),
                epochs = deepSamples
                    .GroupBy(
                        row => L(row, "RegistrationId").ToString(CultureInfo.InvariantCulture)
                               + "\u001f"
                               + L(row, "ProfileEpoch").ToString(CultureInfo.InvariantCulture),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        var rows = group
                            .OrderBy(row => D(row, "CaptureStartMs"))
                            .ToList();
                        var first = rows[0];
                        var startMs = rows.Min(row => D(row, "CaptureStartMs"));
                        var endMs = rows.Max(row => D(row, "CaptureEndMs"));
                        var fingerprints = rows
                            .Select(row => S(row, "PathFingerprint"))
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToArray();
                        return new
                        {
                            registrationId = L(first, "RegistrationId"),
                            profileEpoch = L(first, "ProfileEpoch"),
                            captureStartMs = startMs,
                            captureEndMs = endMs,
                            scenarios = rows
                                .Select(row =>
                                    ResolverScenarioAt(
                                        D(row, "CaptureStartMs") +
                                        Math.Max(0, D(row, "CaptureEndMs") - D(row, "CaptureStartMs")) * 0.5,
                                        scenarioAnalysis))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray(),
                            hotsetSamples = rows.Count(row =>
                                S(row, "Mode").Equals("HOTSET", StringComparison.OrdinalIgnoreCase)),
                            spikeCaptures = rows.Count(row =>
                                S(row, "Mode").Equals("SPIKE_CAPTURE", StringComparison.OrdinalIgnoreCase)),
                            distinctPathFingerprints = fingerprints.Length,
                            pathFingerprints = fingerprints
                        };
                    })
                    .OrderBy(x => x.registrationId)
                    .ThenBy(x => x.profileEpoch)
                    .ToArray(),
                lineSamples = deepLines
                    .GroupBy(row => L(row, "SampleSequence"))
                    .OrderBy(group => group.Key)
                    .Select(group =>
                    {
                        var first = group.First();
                        return new
                        {
                            sampleSequence = group.Key,
                            registrationId = L(first, "RegistrationId"),
                            profileEpoch = L(first, "ProfileEpoch"),
                            files = group
                                .GroupBy(
                                    row => S(row, "SourceFile"),
                                    StringComparer.OrdinalIgnoreCase)
                                .OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
                                .Select(file => new
                                {
                                    sourceFile = file.Key,
                                    lines = file
                                        .OrderBy(row => L(row, "Line"))
                                        .Select(row => new
                                        {
                                            line = L(row, "Line"),
                                            hits = L(row, "Hits")
                                        })
                                        .ToArray()
                                })
                                .ToArray()
                        };
                    })
                    .ToArray()
            },
            optimizerEvidence = callbackRows
                .Select(callback =>
                {
                    var registrationId = callback.registrationId ?? 0;
                    var registration = deepRegistrations.FirstOrDefault(row =>
                        L(row, "RegistrationId") == registrationId);
                    var samples = deepSamples
                        .Where(row => L(row, "RegistrationId") == registrationId)
                        .ToList();
                    var lineRows = deepLines
                        .Where(row => L(row, "RegistrationId") == registrationId)
                        .ToList();
                    var pathFingerprints = samples
                        .Select(row => S(row, "PathFingerprint"))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var unresolvedLineEvents = samples.Sum(row =>
                        L(row, "UnresolvedLineEvents"));
                    var hasLegacyCLineRows = lineRows.Any(row =>
                        S(row, "SourceFile").Equals("=[C]", StringComparison.OrdinalIgnoreCase));
                    var lineSourceReliable =
                        lineRows.Count > 0 &&
                        unresolvedLineEvents == 0 &&
                        !hasLegacyCLineRows;

                    return new
                    {
                        registrationId = callback.registrationId,
                        owner = callback.owner,
                        kind = callback.kind,
                        target = callback.target,
                        runtime = new
                        {
                            callback.exclusiveMsPerSecond,
                            callback.globalWorkSharePct,
                            callback.avgExclusiveUs,
                            callback.maxExclusiveMs,
                            callback.spikeCount,
                            callback.maxSpikeExclusiveMs
                        },
                        deep = new
                        {
                            sourceMapped = callback.source is not null,
                            complete = registration is not null && L(registration, "Complete") != 0,
                            hotsetSamples = samples.Count(row =>
                                S(row, "Mode").Equals("HOTSET", StringComparison.OrdinalIgnoreCase)),
                            spikeCaptures = samples.Count(row =>
                                S(row, "Mode").Equals("SPIKE_CAPTURE", StringComparison.OrdinalIgnoreCase)),
                            distinctEpochs = samples
                                .Select(row => L(row, "ProfileEpoch"))
                                .Distinct()
                                .Count(),
                            distinctPathFingerprints = pathFingerprints.Length,
                            lineEvidenceRows = lineRows.Count,
                            unresolvedLineEvents,
                            lineSourceReliable,
                            nestedRegistrationsExcluded = samples.Sum(row =>
                                L(row, "NestedRegistrationCount")),
                            hookConflicts = registration is null
                                ? 0
                                : L(registration, "HookConflicts")
                        },
                        readiness = new
                        {
                            sourceDecisionReady =
                                callback.source is not null &&
                                samples.Any(row =>
                                    S(row, "Mode").Equals("HOTSET", StringComparison.OrdinalIgnoreCase)) &&
                                lineSourceReliable,
                            spikePathReady = samples.Any(row =>
                                S(row, "Mode").Equals("SPIKE_CAPTURE", StringComparison.OrdinalIgnoreCase)),
                            multipleObservedPaths = pathFingerprints.Length > 1
                        }
                    };
                })
                .OrderByDescending(x => x.runtime.exclusiveMsPerSecond)
                .ToArray(),
            families,
            owners = ownerRows,
            callbacks = callbackRows
        };
    }

    private static List<ResolverOwnerActivityMetric> BuildResolverOwnerActivity(
        IReadOnlyList<Dictionary<string, string>> timeline)
    {
        var observedBuckets = timeline
            .Select(r => S(r, "BucketIndex"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        if (observedBuckets == 0)
            return [];

        var ownerBuckets = timeline
            .Where(r => !string.IsNullOrWhiteSpace(S(r, "Mod", "Owner")) &&
                        !string.IsNullOrWhiteSpace(S(r, "BucketIndex")))
            .GroupBy(
                r => S(r, "Mod", "Owner") + "\u001f" + S(r, "BucketIndex"),
                StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new ResolverOwnerBucketMetric
                {
                    Owner = S(first, "Mod", "Owner"),
                    BucketIndex = S(first, "BucketIndex"),
                    Calls = g.Sum(r => L(r, "Calls")),
                    ExclusiveMs = g.Sum(r => D(r, "ExclusiveMs"))
                };
            })
            .ToList();

        return ownerBuckets
            .GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var buckets = g.ToList();
                var work = buckets.Select(x => x.ExclusiveMs).OrderBy(x => x).ToArray();
                var meanWork = work.Length > 0 ? work.Average() : 0;
                var maxWork = work.Length > 0 ? work[^1] : 0;

                return new ResolverOwnerActivityMetric
                {
                    Owner = g.Key,
                    ActiveBuckets = buckets.Count,
                    ObservedBuckets = observedBuckets,
                    ActiveBucketPct = Percent(buckets.Count, observedBuckets),
                    MeanCallsPerActiveBucket = buckets.Count > 0 ? buckets.Average(x => (double)x.Calls) : 0,
                    MeanExclusiveMsPerActiveBucket = meanWork,
                    P95ExclusiveMsPerActiveBucket = ResolverPercentile(work, 0.95),
                    MaxExclusiveMsPerBucket = maxWork,
                    BurstRatio = meanWork > 0 ? maxWork / meanWork : 0
                };
            })
            .OrderByDescending(x => x.ActiveBucketPct)
            .ThenByDescending(x => x.MeanExclusiveMsPerActiveBucket)
            .ToList();
    }

    private static ResolverScenarioAnalysis BuildResolverScenarios(
        IReadOnlyList<Dictionary<string, string>> markers,
        IReadOnlyList<Dictionary<string, string>> timeline,
        IReadOnlyList<Dictionary<string, string>> onUpdateTimeline,
        IReadOnlyList<ResolverSpikeSample> spikes,
        FrameTimeAnalysis? frameTime,
        double captureSeconds)
    {
        var definitions = new[]
        {
            new ResolverScenarioDefinition("WORLD", "WORLD / IDLE"),
            new ResolverScenarioDefinition("DRIVING", "DRIVING"),
            new ResolverScenarioDefinition("COMBAT", "COMBAT")
        };

        var segments = definitions.ToDictionary(
            x => x.Name,
            _ => new List<ResolverScenarioSegment>(),
            StringComparer.OrdinalIgnoreCase);
        var open = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        var recognizedMarkers = 0;
        var unmatchedMarkers = 0;

        foreach (var marker in markers
                     .Select(r => new
                     {
                         CaptureMs = D(r, "CaptureMs"),
                         Label = S(r, "Label")
                     })
                     .OrderBy(x => x.CaptureMs))
        {
            foreach (var definition in definitions)
            {
                var startLabel = "SCENARIO_" + definition.Name + "_START";
                var endLabel = "SCENARIO_" + definition.Name + "_END";

                if (marker.Label.Equals(startLabel, StringComparison.OrdinalIgnoreCase))
                {
                    recognizedMarkers++;
                    if (open.ContainsKey(definition.Name))
                    {
                        unmatchedMarkers++;
                    }
                    else
                    {
                        open[definition.Name] = marker.CaptureMs;
                    }
                    break;
                }

                if (marker.Label.Equals(endLabel, StringComparison.OrdinalIgnoreCase))
                {
                    recognizedMarkers++;
                    if (!open.TryGetValue(definition.Name, out var startMs))
                    {
                        unmatchedMarkers++;
                    }
                    else
                    {
                        if (marker.CaptureMs > startMs)
                        {
                            segments[definition.Name].Add(new ResolverScenarioSegment
                            {
                                StartMs = startMs,
                                EndMs = marker.CaptureMs
                            });
                        }
                        else
                        {
                            unmatchedMarkers++;
                        }

                        open.Remove(definition.Name);
                    }
                    break;
                }
            }
        }

        unmatchedMarkers += open.Count;

        var scenarioRows = new List<ResolverScenarioMetric>();
        foreach (var definition in definitions)
        {
            var scenarioSegments = segments[definition.Name]
                .OrderBy(x => x.StartMs)
                .ToList();

            if (scenarioSegments.Count == 0)
                continue;

            var durationMs = scenarioSegments.Sum(x => Math.Max(0, x.EndMs - x.StartMs));
            var durationSeconds = durationMs / 1000.0;
            if (durationSeconds <= 0)
                continue;

            var ownerWork = new Dictionary<string, ResolverScenarioOwnerAccumulator>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in timeline)
            {
                var owner = S(row, "Mod", "Owner");
                if (string.IsNullOrWhiteSpace(owner))
                    continue;

                var bucketStart = D(row, "BucketStartMs");
                var bucketEnd = D(row, "BucketEndMs");
                if (bucketEnd <= bucketStart)
                {
                    var width = D(row, "BucketWidthMs");
                    if (width > 0)
                        bucketEnd = bucketStart + width;
                }

                var bucketWidth = bucketEnd - bucketStart;
                if (bucketWidth <= 0)
                    continue;

                var overlapMs = scenarioSegments.Sum(x =>
                    ResolverOverlapMs(bucketStart, bucketEnd, x.StartMs, x.EndMs));
                if (overlapMs <= 0)
                    continue;

                var fraction = Math.Clamp(overlapMs / bucketWidth, 0, 1);
                if (!ownerWork.TryGetValue(owner, out var accumulator))
                {
                    accumulator = new ResolverScenarioOwnerAccumulator();
                    ownerWork[owner] = accumulator;
                }

                accumulator.Calls += L(row, "Calls") * fraction;
                accumulator.ExclusiveMs += D(row, "ExclusiveMs") * fraction;
                accumulator.ActiveBucketTouches++;
            }

            var scenarioTotalMs = ownerWork.Values.Sum(x => x.ExclusiveMs);

            var onUpdateWork = new Dictionary<string, ResolverScenarioCallbackAccumulator>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var row in onUpdateTimeline)
            {
                var owner = S(row, "Mod", "Owner");
                var kind = S(row, "Kind");
                var target = S(row, "Target");
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(target))
                    continue;

                var bucketStart = D(row, "BucketStartMs");
                var bucketEnd = D(row, "BucketEndMs");
                if (bucketEnd <= bucketStart)
                {
                    var width = D(row, "BucketWidthMs");
                    if (width > 0)
                        bucketEnd = bucketStart + width;
                }

                var bucketWidth = bucketEnd - bucketStart;
                if (bucketWidth <= 0)
                    continue;

                var overlapMs = scenarioSegments.Sum(x =>
                    ResolverOverlapMs(bucketStart, bucketEnd, x.StartMs, x.EndMs));
                if (overlapMs <= 0)
                    continue;

                var fraction = Math.Clamp(overlapMs / bucketWidth, 0, 1);
                var key = ResolverCallbackKey(owner, kind, target);
                if (!onUpdateWork.TryGetValue(key, out var accumulator))
                {
                    accumulator = new ResolverScenarioCallbackAccumulator
                    {
                        Owner = owner,
                        Kind = kind,
                        Target = target
                    };
                    onUpdateWork[key] = accumulator;
                }

                accumulator.Calls += L(row, "Calls") * fraction;
                accumulator.ExclusiveMs += D(row, "ExclusiveMs") * fraction;
                accumulator.ActiveBucketTouches++;
            }

            var scenarioOnUpdateTotalMs = onUpdateWork.Values.Sum(x => x.ExclusiveMs);

            var alignedFrames =
                frameTime is not null &&
                frameTime.Correlated &&
                frameTime.ExactAlignment
                    ? frameTime.Frames
                        .Where(x => ResolverInSegments(
                            x.RelativeStartMs + x.FrameMs * 0.5,
                            scenarioSegments))
                        .ToList()
                    : [];

            var frameTimes = alignedFrames
                .Select(x => x.FrameMs)
                .Where(x => x > 0 && double.IsFinite(x))
                .OrderBy(x => x)
                .ToList();

            var meanFrameMs = frameTimes.Count > 0 ? frameTimes.Average() : 0;
            var scenarioAverageFps = meanFrameMs > 0 ? 1000.0 / meanFrameMs : 0;

            var ownerRows = ownerWork
                .Select(kvp =>
                {
                    var callsPerSecond = kvp.Value.Calls / durationSeconds;
                    return new ResolverScenarioOwnerMetric
                    {
                        Owner = kvp.Key,
                        Infrastructure = IsInfrastructureOwner(kvp.Key),
                        Calls = kvp.Value.Calls,
                        CallsPerSecond = callsPerSecond,
                        CallsPerFrame = scenarioAverageFps > 0
                            ? callsPerSecond / scenarioAverageFps
                            : null,
                        ExclusiveMs = kvp.Value.ExclusiveMs,
                        ExclusiveMsPerSecond = kvp.Value.ExclusiveMs / durationSeconds,
                        WorkSharePct = Percent(kvp.Value.ExclusiveMs, scenarioTotalMs),
                        ActiveBucketTouches = kvp.Value.ActiveBucketTouches
                    };
                })
                .OrderByDescending(x => x.ExclusiveMsPerSecond)
                .ThenByDescending(x => x.CallsPerSecond)
                .ToList();

            var onUpdateRows = onUpdateWork.Values
                .Select(x =>
                {
                    var callsPerSecond = x.Calls / durationSeconds;
                    return new ResolverScenarioCallbackMetric
                    {
                        Owner = x.Owner,
                        Infrastructure = IsInfrastructureOwner(x.Owner),
                        Kind = x.Kind,
                        Target = x.Target,
                        Calls = x.Calls,
                        CallsPerSecond = callsPerSecond,
                        CallsPerFrame = scenarioAverageFps > 0
                            ? callsPerSecond / scenarioAverageFps
                            : null,
                        ExclusiveMs = x.ExclusiveMs,
                        ExclusiveMsPerSecond = x.ExclusiveMs / durationSeconds,
                        WorkSharePct = Percent(x.ExclusiveMs, scenarioOnUpdateTotalMs),
                        ActiveBucketTouches = x.ActiveBucketTouches
                    };
                })
                .OrderByDescending(x => x.ExclusiveMsPerSecond)
                .ThenByDescending(x => x.CallsPerSecond)
                .ToList();

            var scenarioSpikes = spikes
                .Where(x => ResolverInSegments(
                    x.CaptureStartMs + Math.Max(0, x.CaptureEndMs - x.CaptureStartMs) * 0.5,
                    scenarioSegments))
                .GroupBy(
                    x => ResolverCallbackKey(x.Owner, x.Kind, x.Target),
                    StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var first = g.First();
                    var aggregate = ResolverAggregateSpikes(g);
                    return new ResolverScenarioSpikeMetric
                    {
                        Owner = first.Owner,
                        Kind = first.Kind,
                        Target = first.Target,
                        Count = aggregate.Count,
                        SpikesPerSecond = ResolverRate(aggregate.Count, durationSeconds),
                        ExclusiveMsPerSecond = ResolverRate(aggregate.TotalExclusiveMs, durationSeconds),
                        MaxExclusiveMs = aggregate.MaxExclusiveMs
                    };
                })
                .OrderByDescending(x => x.ExclusiveMsPerSecond)
                .ThenByDescending(x => x.Count)
                .ToList();

            scenarioRows.Add(new ResolverScenarioMetric
            {
                Name = definition.Name,
                Label = definition.Label,
                DurationSeconds = durationSeconds,
                CaptureCoveragePct = Percent(durationSeconds, captureSeconds),
                Segments = scenarioSegments,
                Owners = ownerRows,
                OnUpdateCallbacks = onUpdateRows,
                CallbackSpikes = scenarioSpikes,
                FrameTime = frameTimes.Count == 0
                    ? null
                    : new ResolverScenarioFrameTimeMetric
                    {
                        FrameCount = frameTimes.Count,
                        AverageFps = scenarioAverageFps,
                        MeanMs = meanFrameMs,
                        P95Ms = ResolverPercentile(frameTimes, 0.95),
                        P99Ms = ResolverPercentile(frameTimes, 0.99),
                        MaxMs = frameTimes[^1],
                        FramesOver33Ms = frameTimes.Count(x => x >= 33.3),
                        FramesOver50Ms = frameTimes.Count(x => x >= 50.0)
                    }
            });
        }

        var taggedSeconds = scenarioRows.Sum(x => x.DurationSeconds);

        return new ResolverScenarioAnalysis
        {
            RecognizedMarkers = recognizedMarkers,
            UnmatchedMarkers = unmatchedMarkers,
            TaggedSeconds = taggedSeconds,
            UntaggedSeconds = Math.Max(0, captureSeconds - taggedSeconds),
            Scenarios = scenarioRows
        };
    }

    private static string ResolverScenarioAt(
        double captureMs,
        ResolverScenarioAnalysis analysis)
    {
        foreach (var scenario in analysis.Scenarios)
        {
            if (ResolverInSegments(captureMs, scenario.Segments))
                return scenario.Name;
        }

        return "UNTAGGED";
    }

    private static double ResolverOverlapMs(
        double aStart,
        double aEnd,
        double bStart,
        double bEnd)
    {
        var start = Math.Max(aStart, bStart);
        var end = Math.Min(aEnd, bEnd);
        return Math.Max(0, end - start);
    }

    private static bool ResolverInSegments(
        double captureMs,
        IReadOnlyList<ResolverScenarioSegment> segments) =>
        segments.Any(x => captureMs >= x.StartMs && captureMs < x.EndMs);

    private static ResolverSpikeAggregate ResolverAggregateSpikes(IEnumerable<ResolverSpikeSample> spikes)
    {
        var list = spikes.ToList();
        return new ResolverSpikeAggregate
        {
            Count = list.Count,
            TotalExclusiveMs = list.Sum(x => x.ExclusiveMs),
            MaxExclusiveMs = list.Select(x => x.ExclusiveMs).DefaultIfEmpty(0).Max()
        };
    }

    private static double ResolverRate(double value, double seconds) =>
        seconds > 0 ? value / seconds : 0;

    private static double ResolverPercentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return 0;

        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position = Math.Clamp(percentile, 0, 1) * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        var fraction = position - lower;
        return sortedValues[lower] +
               (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static string ResolverCallbackKey(string owner, string kind, string target) =>
        owner + "\u001f" + kind + "\u001f" + target;

    private static string ResolverCallbackInstanceKey(
        long registrationId,
        string owner,
        string kind,
        string target) =>
        registrationId > 0
            ? "registration:" + registrationId.ToString(CultureInfo.InvariantCulture)
            : ResolverCallbackKey(owner, kind, target);

    private static string ResolverFamilyKey(string kind, string target) =>
        kind + "\u001f" + target;

    private sealed class ResolverCallbackMetric
    {
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public long SourceLineStart { get; init; }
        public long SourceLineEnd { get; init; }
        public long Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double AvgExclusiveUs { get; init; }
        public double MaxExclusiveMs { get; init; }
    }

    private sealed class ResolverSpikeSample
    {
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public long SourceLineStart { get; init; }
        public long SourceLineEnd { get; init; }
        public double CaptureStartMs { get; init; }
        public double CaptureEndMs { get; init; }
        public double ExclusiveMs { get; init; }
    }

    private sealed record ResolverScenarioDefinition(string Name, string Label);

    private sealed class ResolverScenarioSegment
    {
        public double StartMs { get; init; }
        public double EndMs { get; init; }
        public double DurationSeconds => Math.Max(0, EndMs - StartMs) / 1000.0;
    }

    private sealed class ResolverScenarioOwnerAccumulator
    {
        public double Calls { get; set; }
        public double ExclusiveMs { get; set; }
        public int ActiveBucketTouches { get; set; }
    }

    private sealed class ResolverScenarioOwnerMetric
    {
        public string Owner { get; init; } = "";
        public bool Infrastructure { get; init; }
        public double Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double? CallsPerFrame { get; init; }
        public double ExclusiveMs { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double WorkSharePct { get; init; }
        public int ActiveBucketTouches { get; init; }
    }

    private sealed class ResolverScenarioCallbackAccumulator
    {
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public double Calls { get; set; }
        public double ExclusiveMs { get; set; }
        public int ActiveBucketTouches { get; set; }
    }

    private sealed class ResolverScenarioCallbackMetric
    {
        public string Owner { get; init; } = "";
        public bool Infrastructure { get; init; }
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public double Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double? CallsPerFrame { get; init; }
        public double ExclusiveMs { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double WorkSharePct { get; init; }
        public int ActiveBucketTouches { get; init; }
    }

    private sealed class ResolverScenarioSpikeMetric
    {
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public int Count { get; init; }
        public double SpikesPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double MaxExclusiveMs { get; init; }
    }

    private sealed class ResolverScenarioFrameTimeMetric
    {
        public int FrameCount { get; init; }
        public double AverageFps { get; init; }
        public double MeanMs { get; init; }
        public double P95Ms { get; init; }
        public double P99Ms { get; init; }
        public double MaxMs { get; init; }
        public int FramesOver33Ms { get; init; }
        public int FramesOver50Ms { get; init; }
    }

    private sealed class ResolverScenarioMetric
    {
        public string Name { get; init; } = "";
        public string Label { get; init; } = "";
        public double DurationSeconds { get; init; }
        public double CaptureCoveragePct { get; init; }
        public List<ResolverScenarioSegment> Segments { get; init; } = [];
        public List<ResolverScenarioOwnerMetric> Owners { get; init; } = [];
        public List<ResolverScenarioCallbackMetric> OnUpdateCallbacks { get; init; } = [];
        public List<ResolverScenarioSpikeMetric> CallbackSpikes { get; init; } = [];
        public ResolverScenarioFrameTimeMetric? FrameTime { get; init; }
    }

    private sealed class ResolverScenarioAnalysis
    {
        public int RecognizedMarkers { get; init; }
        public int UnmatchedMarkers { get; init; }
        public double TaggedSeconds { get; init; }
        public double UntaggedSeconds { get; init; }
        public List<ResolverScenarioMetric> Scenarios { get; init; } = [];
    }

    private sealed class ResolverSpikeAggregate
    {
        public int Count { get; init; }
        public double TotalExclusiveMs { get; init; }
        public double MaxExclusiveMs { get; init; }
    }

    private sealed class ResolverFamilyTotals
    {
        public int OwnerCount { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
    }

    private sealed class ResolverOwnerBucketMetric
    {
        public string Owner { get; init; } = "";
        public string BucketIndex { get; init; } = "";
        public long Calls { get; init; }
        public double ExclusiveMs { get; init; }
    }

    private sealed class ResolverOwnerActivityMetric
    {
        public string Owner { get; init; } = "";
        public int ActiveBuckets { get; init; }
        public int ObservedBuckets { get; init; }
        public double ActiveBucketPct { get; init; }
        public double MeanCallsPerActiveBucket { get; init; }
        public double MeanExclusiveMsPerActiveBucket { get; init; }
        public double P95ExclusiveMsPerActiveBucket { get; init; }
        public double MaxExclusiveMsPerBucket { get; init; }
        public double BurstRatio { get; init; }
    }
}
