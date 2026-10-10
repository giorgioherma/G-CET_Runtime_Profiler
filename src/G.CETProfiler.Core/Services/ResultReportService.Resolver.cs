using System.Globalization;
using System.Text.RegularExpressions;

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
        // Analyze() already parsed these shared inputs for the human report.
        // Reuse them: on heavy stacks Timeline alone can be hundreds of thousands
        // of rows, so reparsing it here was pure duplicate collection work.
        var detail = a.DetailRows;
        var spikes = a.SpikeRows;
        var timeline = a.TimelineRows;
        var frameMultiplicity = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_FrameMultiplicity.csv"));
        var markers = a.MarkerRows;
        var deepRegistrations = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Registrations.csv"));
        var deepFunctions = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Functions.csv"));
        var deepEdges = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Edges.csv"));
        var deepSamples = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Samples.csv"));
        var deepLines = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Lines.csv"));
        var deepCallsites = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Deep_Callsites.csv"));

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
                Frame = L(r, "Frame"),
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

        var burstByCallback = callbacks
            .GroupBy(x => ResolverCallbackInstanceKey(
                x.RegistrationId, x.Owner, x.Kind, x.Target),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var callback = g.First();
                    var samples = spikeSamples
                        .Where(x =>
                            ResolverCallbackInstanceKey(
                                x.RegistrationId,
                                x.Owner,
                                x.Kind,
                                x.Target)
                            .Equals(g.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(x => new BurstSample(
                            x.CaptureStartMs,
                            x.ExclusiveMs));
                    return BurstAnalysisService.Analyze(
                        samples,
                        callback.ExclusiveMsPerSecond,
                        callback.Calls);
                },
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

        var frameMultiplicityByRegistration = frameMultiplicity
            .Where(row => L(row, "RegistrationId") > 0)
            .GroupBy(row => L(row, "RegistrationId"))
            .ToDictionary(
                group => group.Key,
                group => group.First());

        var callbackRows = callbacks
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenByDescending(x => x.CallsPerSecond)
            .Select(x =>
            {
                var callbackKey = ResolverCallbackInstanceKey(
                    x.RegistrationId, x.Owner, x.Kind, x.Target);
                var familyKey = ResolverFamilyKey(x.Kind, x.Target);
                var callbackSpikes = spikeByCallback.GetValueOrDefault(callbackKey) ?? new ResolverSpikeAggregate();
                var burst = burstByCallback.GetValueOrDefault(callbackKey) ??
                    BurstAnalysisService.Analyze(Array.Empty<BurstSample>(), x.ExclusiveMsPerSecond, x.Calls);
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
                    burst = new
                    {
                        vocabularyVersion = "1.0",
                        primaryClass = burst.PrimaryClass,
                        classes = burst.Classes,
                        spikeCount = burst.SpikeCount,
                        spikeRatePct = Round(burst.SpikeRatePct, 6),
                        medianExclusiveMs = Round(burst.MedianExclusiveMs, 6),
                        p95ExclusiveMs = Round(burst.P95ExclusiveMs, 6),
                        maxExclusiveMs = Round(burst.MaxExclusiveMs, 6),
                        medianIntervalMs = Round(burst.MedianIntervalMs, 6),
                        intervalMadMs = Round(burst.IntervalMadMs, 6),
                        intervalJitterPct = Round(burst.IntervalJitterPct, 6),
                        periodicSupportPct = Round(burst.PeriodicSupportPct, 6),
                        sustainedHot = burst.SustainedHot,
                        periodicStutter = burst.PeriodicStutter,
                        burstHot = burst.BurstHot,
                        catastrophicBurst = burst.CatastrophicBurst,
                        stutterMaterial = burst.StutterMaterial
                    },
                    ownerActivityBucketPct = ownerActivity is null
                        ? (double?)null
                        : Round(ownerActivity.ActiveBucketPct, 3),
                    ownerBurstRatio = ownerActivity is null
                        ? (double?)null
                        : Round(ownerActivity.BurstRatio, 6),
                    frameMultiplicity =
                        x.RegistrationId > 0 &&
                        frameMultiplicityByRegistration.TryGetValue(
                            x.RegistrationId, out var multiplicity)
                            ? new
                            {
                                totalFrames = L(multiplicity, "TotalFrames"),
                                framesWithCalls = L(multiplicity, "FramesWithCalls"),
                                zeroCallFrames = L(multiplicity, "ZeroCallFrames"),
                                recordedCalls = L(multiplicity, "RecordedCalls"),
                                oneCallFrames = L(multiplicity, "OneCallFrames"),
                                twoCallFrames = L(multiplicity, "TwoCallFrames"),
                                threeCallFrames = L(multiplicity, "ThreeCallFrames"),
                                fourCallFrames = L(multiplicity, "FourCallFrames"),
                                fivePlusCallFrames = L(multiplicity, "FivePlusCallFrames"),
                                maxCallsInFrame = L(multiplicity, "MaxCallsInFrame"),
                                meanCallsPerActiveFrame = D(multiplicity, "MeanCallsPerActiveFrame"),
                                multiCallFramePct = D(multiplicity, "MultiCallFramePct")
                            }
                            : null
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
            spikeSamples,
            a.FrameTime,
            a.CaptureSeconds);

        return new
        {
            schemaVersion = "2.0",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                contractVersion = "1.1",
                producer = "G-CET-Runtime-Profiler",
                consumer = "resolver",
                domain = "cet"
            },
            workloadServices = a.WorkloadServices is null || !a.WorkloadServices.HasEvidence ? null : new
            {
                policy = "MEASURE_ONLY_OPT_IN_NO_AUTHOR_REWRITES",
                nestedIntoParent = true,
                measuredCalls = a.WorkloadServices.MeasuredCalls,
                nestedMsPerSecond = Round(a.WorkloadServices.MeasuredNestedMsPerSecond, 3),
                queuePeakObserved = a.WorkloadServices.PeakQueue,
                phasePendingPeakObserved = a.WorkloadServices.PeakPhasePending,
                latestHealth = a.WorkloadServices.Latest,
                measuredClients = a.WorkloadServices.MeasuredClients
                    .OrderByDescending(x => x.MsPerSecond)
                    .Take(100).Select(x => new {
                        x.Owner, x.JobType, x.Job, x.Calls,
                        msPerSecond = Round(x.MsPerSecond, 3),
                        maxMs = Round(x.MaxMs, 3)
                    })
            },
            garbageCollection = a.ExplicitGc is null && a.CollectorGc is null ? null : new
            {
                attributionPolicy = "MEASUREMENT_ONLY_NO_AUTOMATIC_OPTIMIZATION",
                exactExplicitTiming = a.ExplicitGc is not null,
                automaticLuaJitTiming = a.CollectorGc is not null,
                internalCollectorTotalMs = a.CollectorGc is null ? (double?)null
                    : Round(a.CollectorGc.TotalMs, 3),
                internalCollectorStepCalls = a.CollectorGc?.StepCalls,
                internalCollectorFullCalls = a.CollectorGc?.FullCalls,
                internalCollectorMaxFrameMs = a.CollectorGc is null ? (double?)null
                    : Round(a.CollectorGc.MaxObservedFrameMs, 3),
                automaticVsExplicitOriginSeparated = false,
                collectorFrameBuckets = a.CollectorGc?.Frames
                    .OrderByDescending(x => x.TotalMs).Take(100)
                    .Select(x => new {
                        x.Frame, captureStartMs = Round(x.StartMs, 3),
                        captureEndMs = Round(x.EndMs, 3),
                        durationMs = Round(x.TotalMs, 3),
                        x.IncrementalCalls, x.FullCalls,
                        x.CallbackSpikeOverlap
                    }),
                totalExplicitMs = a.ExplicitGc is null ? 0
                    : Round(a.ExplicitGc.TotalDurationMs, 3),
                operations = (a.ExplicitGc?.Events ?? []).OrderByDescending(x => x.DurationMs)
                    .OrderByDescending(x => x.DurationMs).Take(100)
                    .Select(x => new
                    {
                        captureStartMs = Round(x.StartMs, 3),
                        captureEndMs = Round(x.EndMs, 3),
                        durationMs = Round(x.DurationMs, 3),
                        x.Action, x.SourceFile, x.SourceLine,
                        callbackSpikeOverlap = x.NearRecordedSpike
                    })
            },
            semantics = new
            {
                measurementOnly = true,
                classificationIncluded = true,
                pacingRecommendationIncluded = false,
                note = "The profiler measures and normalizes runtime evidence and classifies sustained/burst pacing patterns. A separate resolver decides whether and how to transform a mod; burst classification never authorizes AUTO by itself."
            },
            burstVocabulary = new
            {
                version = "1.0",
                classes = new[]
                {
                    "STEADY",
                    "RECORDED_SPIKES",
                    "SUSTAINED_HOT",
                    "PERIODIC_STUTTER",
                    "BURST_HOT",
                    "CATASTROPHIC_BURST"
                },
                stutterMaterialClasses = new[]
                {
                    "PERIODIC_STUTTER",
                    "BURST_HOT",
                    "CATASTROPHIC_BURST"
                },
                thresholds = new
                {
                    sustainedHotMsPerSecond = BurstAnalysisService.SustainedHotThresholdMsPerSecond,
                    burstHotP95Ms = BurstAnalysisService.BurstHotP95ThresholdMs,
                    catastrophicBurstMs = BurstAnalysisService.CatastrophicBurstThresholdMs,
                    periodicMinimumSpikes = BurstAnalysisService.PeriodicMinimumSpikes,
                    periodicMinimumIntervalMs = BurstAnalysisService.PeriodicMinimumIntervalMs,
                    periodicMaximumIntervalMs = BurstAnalysisService.PeriodicMaximumIntervalMs,
                    periodicMaximumJitterPct = BurstAnalysisService.PeriodicMaximumJitterPct,
                    periodicIntervalTolerancePct = BurstAnalysisService.PeriodicIntervalTolerancePct,
                    periodicMinimumSupportPct = BurstAnalysisService.PeriodicMinimumSupportPct
                },
                note = "Periodicity is derived only from recorded callback spikes. With the default native threshold, those are callbacks whose exclusive time crossed 5 ms. A higher configured spike threshold makes this evidence correspondingly less complete."
            },
            quality = new
            {
                frameNormalizationAvailable,
                timelineAvailable = timeline.Count > 0,
                frameMultiplicityAvailable = frameMultiplicity.Count > 0,
                schedulerJobsAvailable = a.SchedulerJobs.Count > 0,
                spikesAvailable = spikes.Count > 0,
                burstAnalysisAvailable = callbacks.Count > 0,
                burstMaterialCallbackCount = burstByCallback.Values.Count(x => x.StutterMaterial),
                periodicStutterCallbackCount = burstByCallback.Values.Count(x => x.PeriodicStutter),
                catastrophicBurstCallbackCount = burstByCallback.Values.Count(x => x.CatastrophicBurst),
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
                droppedSpikeEvents,
                callbackRegistrationIdsAvailable = callbacks.Any(x => x.RegistrationId > 0),
                callbackSourceLocationsAvailable = callbacks.Any(x => !string.IsNullOrWhiteSpace(x.SourceFile)),
                callbackSourceLocationCount = callbacks.Count(x => !string.IsNullOrWhiteSpace(x.SourceFile)),
                adaptiveDeepProfilingAvailable = deepRegistrations.Count > 0,
                adaptiveDeepFunctionsAvailable = deepFunctions.Count > 0,
                adaptiveDeepEdgesAvailable = deepEdges.Count > 0,
                adaptiveDeepSamplesAvailable = deepSamples.Count > 0,
                adaptiveDeepLinesAvailable = deepLines.Count > 0,
                adaptiveDeepCallsitesAvailable = deepCallsites.Count > 0,
                adaptiveDeepDroppedSamples = deepSamples
                    .Select(row => L(row, "DroppedSamplesAtDump"))
                    .DefaultIfEmpty(0)
                    .Max(),
                adaptiveDeepDroppedLineRows = deepLines
                    .Select(row => L(row, "DroppedLineRowsAtDump"))
                    .DefaultIfEmpty(0)
                    .Max(),
                adaptiveDeepDroppedCallsiteRows = deepCallsites
                    .Select(row => L(row, "DroppedCallsiteRowsAtDump"))
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
            hitchPressure = a.FrameTime?.HitchPressure is null
                ? null
                : new
                {
                    vocabularyVersion = a.FrameTime.HitchPressure.VocabularyVersion,
                    semantics = "FRAME_EXCESS_OVER_ADAPTIVE_TOLERANCE; RUNTIME_OVERLAP_IS_CORRELATION_NOT_CAUSATION",
                    toleranceFrameMs = Round(a.FrameTime.HitchPressure.ToleranceFrameMs, 6),
                    baselineFrameMs = Round(a.FrameTime.HitchPressure.BaselineFrameMs, 6),
                    episodeMergeGapMs = a.FrameTime.HitchPressure.EpisodeMergeGapMs,
                    episodes = a.FrameTime.HitchPressure.EpisodeCount,
                    severeEpisodes = a.FrameTime.HitchPressure.SevereEpisodeCount,
                    catastrophicEpisodes = a.FrameTime.HitchPressure.CatastrophicEpisodeCount,
                    episodesPerMinute = Round(a.FrameTime.HitchPressure.EpisodesPerMinute, 6),
                    medianEpisodeStartGapMs = Round(a.FrameTime.HitchPressure.MedianEpisodeStartGapMs, 6),
                    p90EpisodeStartGapMs = Round(a.FrameTime.HitchPressure.P90EpisodeStartGapMs, 6),
                    longestQuietMs = Round(a.FrameTime.HitchPressure.LongestQuietMs, 6),
                    hitchTollMs = Round(a.FrameTime.HitchPressure.HitchTollMs, 6),
                    exactRuntimeAttribution = a.FrameTime.HitchPressure.ExactRuntimeAttribution,
                    runtimeSignalEpisodes = a.FrameTime.HitchPressure.RuntimeSignalEpisodes,
                    noRecordedRuntimeSignalEpisodes = a.FrameTime.HitchPressure.NoRecordedRuntimeSignalEpisodes,
                    authorizesTransform = false,
                    owners = a.FrameTime.HitchPressure.Owners.Take(30).Select(x => new
                    {
                        owner = x.Owner,
                        episodes = x.EpisodeCount,
                        hitchFrames = x.HitchFrameCount,
                        recordedSpikes = x.SpikeCount,
                        soleSignalEpisodes = x.SoleSignalEpisodes,
                        episodeSharePct = Round(x.EpisodeSharePct, 6),
                        recordedExclusiveMs = Round(x.RecordedExclusiveMs, 6),
                        maxRecordedExclusiveMs = Round(x.MaxRecordedExclusiveMs, 6)
                    }).ToArray()
                },
            scenarios = scenarioAnalysis.Scenarios,
            deepProfiling = new
            {
                available = deepRegistrations.Count > 0,
                mode = "adaptive-runtime-hotset-sampled-lua-call-return-line-path-callsite",
                note = "Broad callback timing remains authoritative. Deep function timing is composition evidence only; timestamped sample paths, line hits, exact frame ids, and sampled caller/callee callsites are structural evidence for source decisions.",
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
                            frame = L(row, "Frame"),
                            frameInvocationOrdinal = L(row, "FrameInvocationOrdinal"),
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
                callsites = deepCallsites
                    .Select(row => new
                    {
                        sampleSequence = L(row, "SampleSequence"),
                        registrationId = L(row, "RegistrationId"),
                        profileEpoch = L(row, "ProfileEpoch"),
                        frame = L(row, "Frame"),
                        frameInvocationOrdinal = L(row, "FrameInvocationOrdinal"),
                        callerSourceFile = S(row, "CallerSourceFile"),
                        callerLine = L(row, "CallerLine"),
                        parentFunctionKey = S(row, "ParentFunctionKey"),
                        childFunctionKey = S(row, "ChildFunctionKey"),
                        calls = L(row, "Calls"),
                        childInclusiveMs = D(row, "ChildInclusiveMs")
                    })
                    .OrderBy(x => x.sampleSequence)
                    .ThenByDescending(x => x.childInclusiveMs)
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
                    var callsiteRows = deepCallsites
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

                    var sampleCount = Math.Max(1, samples.Count);

                    var pathClusters = samples
                        .Where(row => !string.IsNullOrWhiteSpace(S(row, "PathFingerprint")))
                        .GroupBy(
                            row => S(row, "PathFingerprint"),
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => new
                        {
                            pathFingerprint = group.Key,
                            samples = group.Count(),
                            sampleSharePct = Round(
                                Percent(group.Count(), sampleCount),
                                3),
                            avgApproxOwnWallMs = Round(
                                group.Average(row => D(row, "ApproxOwnWallMs")),
                                6),
                            maxApproxOwnWallMs = Round(
                                group.Select(row => D(row, "ApproxOwnWallMs"))
                                    .DefaultIfEmpty(0)
                                    .Max(),
                                6),
                            scenarios = group
                                .Select(row =>
                                {
                                    var midpointMs =
                                        D(row, "CaptureStartMs") +
                                        Math.Max(
                                            0,
                                            D(row, "CaptureEndMs") -
                                            D(row, "CaptureStartMs")) * 0.5;
                                    return ResolverScenarioAt(midpointMs, scenarioAnalysis);
                                })
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                        })
                        .OrderByDescending(x => x.samples)
                        .ThenByDescending(x => x.avgApproxOwnWallMs)
                        .Take(12)
                        .ToArray();

                    var dominantPathSharePct =
                        pathClusters.Length > 0
                            ? pathClusters[0].sampleSharePct
                            : 0.0;

                    var functionByKey = deepFunctions
                        .Where(row => !string.IsNullOrWhiteSpace(S(row, "FunctionKey")))
                        .GroupBy(
                            row => S(row, "FunctionKey"),
                            StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.First(),
                            StringComparer.OrdinalIgnoreCase);

                    var rankedCallees = callsiteRows
                        .GroupBy(
                            row => S(row, "ChildFunctionKey"),
                            StringComparer.OrdinalIgnoreCase)
                        .Where(group => !string.IsNullOrWhiteSpace(group.Key))
                        .Select(group =>
                        {
                            var perSample = group
                                .GroupBy(row => L(row, "SampleSequence"))
                                .Select(sample => new
                                {
                                    calls = sample.Sum(row => L(row, "Calls")),
                                    childInclusiveMs = sample.Sum(row => D(row, "ChildInclusiveMs")),
                                    callsites = sample
                                        .Select(row =>
                                            S(row, "CallerSourceFile") + ":" +
                                            L(row, "CallerLine").ToString(CultureInfo.InvariantCulture))
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Count()
                                })
                                .ToArray();

                            var distinctCallsites = group
                                .Select(row =>
                                    S(row, "CallerSourceFile") + ":" +
                                    L(row, "CallerLine").ToString(CultureInfo.InvariantCulture))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .Count();

                            functionByKey.TryGetValue(group.Key, out var functionRow);
                            var functionName = functionRow is null
                                ? ""
                                : S(functionRow, "FunctionName");
                            var functionWhat = functionRow is null
                                ? ""
                                : S(functionRow, "What");
                            var functionSource = functionRow is null
                                ? ""
                                : S(functionRow, "SourceFile");

                            return new
                            {
                                childFunctionKey = group.Key,
                                functionName,
                                what = functionWhat,
                                sourceFile = functionSource,
                                telemetryClass = ResolverClassifyHotCallee(
                                    functionName,
                                    functionWhat,
                                    functionSource,
                                    group.Key),
                                sampledCalls = group.Sum(row => L(row, "Calls")),
                                childInclusiveMs = Round(
                                    group.Sum(row => D(row, "ChildInclusiveMs")),
                                    6),
                                samplesPresent = perSample.Length,
                                meanCallsPerPresentSample = Round(
                                    perSample.Length > 0
                                        ? perSample.Average(x => (double)x.calls)
                                        : 0,
                                    3),
                                maxCallsInSample = perSample
                                    .Select(x => x.calls)
                                    .DefaultIfEmpty(0)
                                    .Max(),
                                repeatedInSampleCount = perSample.Count(x => x.calls > 1),
                                multiCallsiteSampleCount = perSample.Count(x => x.callsites > 1),
                                distinctCallsites,
                                callsites = group
                                    .GroupBy(row => new
                                    {
                                        Source = S(row, "CallerSourceFile"),
                                        Line = L(row, "CallerLine")
                                    })
                                    .Select(site => new
                                    {
                                        callerSourceFile = site.Key.Source,
                                        callerLine = site.Key.Line,
                                        sampledCalls = site.Sum(row => L(row, "Calls")),
                                        childInclusiveMs = Round(
                                            site.Sum(row => D(row, "ChildInclusiveMs")),
                                            6)
                                    })
                                    .OrderByDescending(x => x.childInclusiveMs)
                                    .ThenByDescending(x => x.sampledCalls)
                                    .Take(8)
                                    .ToArray()
                            };
                        })
                        .OrderByDescending(x => x.childInclusiveMs)
                        .ThenByDescending(x => x.sampledCalls)
                        .ToArray();

                    // hotCallees stays intentionally presentation-focused. Shared-provider
                    // analysis must not inherit this per-callback top-N gate: a getter can
                    // be individually cheap while still mattering when aggregated across
                    // the measured stack.
                    var hotCallees = rankedCallees
                        .Take(16)
                        .ToArray();

                    var sharedProviderCallees = rankedCallees
                        .Where(x => ResolverIsSharedProviderDiscoveryCallee(
                            x.functionName,
                            x.childFunctionKey))
                        .ToArray();

                    var duplicateCalleeCandidates = hotCallees
                        .Where(x =>
                            x.repeatedInSampleCount > 0 ||
                            x.multiCallsiteSampleCount > 0)
                        .Take(8)
                        .ToArray();

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
                            callback.maxSpikeExclusiveMs,
                            frameMultiplicity = callback.frameMultiplicity
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
                            callsiteEvidenceRows = callsiteRows.Count,
                            repeatedSameCallsiteRows = callsiteRows.Count(row => L(row, "Calls") > 1),
                            repeatedCalleeSampleGroups = callsiteRows
                                .GroupBy(row =>
                                    L(row, "SampleSequence").ToString(CultureInfo.InvariantCulture)
                                    + "\u001f"
                                    + S(row, "ChildFunctionKey"),
                                    StringComparer.OrdinalIgnoreCase)
                                .Count(group => group.Sum(row => L(row, "Calls")) > 1),
                            dominantPathSharePct,
                            pathClusters,
                            hotCallees,
                            sharedProviderCallees,
                            duplicateCalleeCandidates,
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
                            frameMultiplicityReady = callback.frameMultiplicity is not null,
                            callsiteEvidenceReady = callsiteRows.Count > 0,
                            multipleObservedPaths = pathFingerprints.Length > 1,
                            pathClusteringReady = pathClusters.Length > 0,
                            redundantSameInvocationCandidate =
                                duplicateCalleeCandidates.Length > 0,
                            multiInvocationFrameCandidate =
                                callback.frameMultiplicity is not null &&
                                callback.frameMultiplicity.multiCallFramePct > 0
                        }
                    };
                })
                .OrderByDescending(x => x.runtime.exclusiveMsPerSecond)
                .ToArray(),
            scheduler = new
            {
                available = a.SchedulerJobs.Count > 0,
                jobCount = a.SchedulerJobs.Count,
                measuredMsPerSecond = Round(a.SchedulerTotalMsPerSecond, 6),
                note = "Scheduler job timing is inclusive inside 0-Engine and is existing-integration evidence; do not add it to owner totals.",
                jobs = a.SchedulerJobs
                    .Select(job => new
                    {
                        owner = job.Owner,
                        jobType = job.JobType,
                        job = job.Job,
                        intervalValue = job.IntervalValue,
                        intervalUnit = job.IntervalUnit,
                        calls = job.Calls,
                        callsPerSecond = Round(job.CallsPerSecond, 6),
                        msPerSecond = Round(job.MsPerSecond, 6),
                        avgUs = Round(job.AvgUs, 6),
                        maxMs = Round(job.MaxMs, 6)
                    })
                    .ToArray()
            },
            families,
            owners = ownerRows,
            callbacks = callbackRows
        };
    }

    private static string ResolverClassifyHotCallee(
        string functionName,
        string what,
        string sourceFile,
        string functionKey)
    {
        var name = functionName ?? "";
        var source = sourceFile ?? "";
        var key = functionKey ?? "";
        var joined = (name + "\n" + source + "\n" + key).ToLowerInvariant();

        if (joined.Contains("cron.lua", StringComparison.Ordinal) &&
            joined.Contains("update", StringComparison.Ordinal))
            return "CRON_PUMP";

        if (joined.Contains("vector4.distance", StringComparison.Ordinal) ||
            joined.Contains("|distance|", StringComparison.Ordinal) ||
            name.Equals("Distance", StringComparison.OrdinalIgnoreCase))
            return "DISTANCE_QUERY";

        if (name.Equals("new", StringComparison.OrdinalIgnoreCase) ||
            joined.Contains(".new", StringComparison.Ordinal) ||
            joined.Contains("|new|", StringComparison.Ordinal))
            return "CONSTRUCTOR_OR_ALLOCATION_LIKE";

        if (name.StartsWith("Get", StringComparison.OrdinalIgnoreCase) ||
            joined.Contains("getplayer", StringComparison.Ordinal) ||
            Regex.IsMatch(
                name,
                @"^Get[A-Za-z0-9_]*System$",
                RegexOptions.CultureInvariant))
            return "LOOKUP_OR_GETTER";

        if (name.StartsWith("Set", StringComparison.OrdinalIgnoreCase) ||
            joined.Contains("setflat", StringComparison.Ordinal))
            return "WRITE_OR_SETTER";

        if (name.Equals("open", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("write", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("close", StringComparison.OrdinalIgnoreCase) ||
            joined.Contains("io.", StringComparison.Ordinal))
            return "FILE_IO";

        if (what.Equals("C", StringComparison.OrdinalIgnoreCase))
            return "NATIVE_OR_C_CALL";

        return "GENERAL_CALL";
    }

    private static bool ResolverIsSharedProviderDiscoveryCallee(
        string functionName,
        string functionKey)
    {
        // Discovery is intentionally open-ended. Preserve every getter-shaped
        // callee in the untruncated optimizer stream; the Resolver decides
        // whether it maps to an exact Game.Get...() source call and generation
        // remains separately gated by an explicit authorization list.
        if (Regex.IsMatch(
                functionName ?? "",
                @"^Get[A-Za-z0-9_]+$",
                RegexOptions.CultureInvariant))
            return true;

        if (!string.IsNullOrWhiteSpace(functionKey) &&
            Regex.IsMatch(
                functionKey,
                @"(?:^|[^A-Za-z0-9_])Get[A-Za-z0-9_]+(?:$|[^A-Za-z0-9_])",
                RegexOptions.CultureInvariant))
            return true;

        // Additive semantic evidence for the non-Get player-derived state family
        // currently modeled by the Resolver. This never limits generic getters.
        return functionName.Equals("IsInCombat", StringComparison.OrdinalIgnoreCase) ||
               (!string.IsNullOrWhiteSpace(functionKey) &&
                functionKey.Contains("IsInCombat", StringComparison.OrdinalIgnoreCase));
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
        public long Frame { get; init; }
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
