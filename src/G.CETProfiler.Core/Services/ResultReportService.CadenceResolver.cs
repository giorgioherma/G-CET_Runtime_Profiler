namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Runtime-only stage of the cadence resolver. It intentionally does not inspect
    // or rewrite source yet. Its job is to turn the exact per-callback onUpdate
    // timeline into conservative, machine-readable cadence decisions for the
    // subsequent source pass.
    private const double CadenceMinimumImpactMsPerSecond = 0.25;
    private const double CadenceScenarioSensitiveRatio = 1.80;
    private const double CadenceStrongSupport = 0.55;
    private const double CadenceScenarioSupport = 0.45;
    private const double CadenceMaxIntervalMs = 5000.0;

    private static object BuildCadenceResolution(string captureRoot, ResultAnalysis a)
    {
        var timeline = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_OnUpdateTimeline.csv"));
        var markers = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Markers.csv"));

        var bucketWidthMs = timeline
            .Select(r => D(r, "BucketWidthMs"))
            .FirstOrDefault(x => x > 0);

        var droppedRows = timeline
            .Select(r => L(r, "DroppedTimelineRowsAtDump"))
            .DefaultIfEmpty(0)
            .Max();

        var segments = BuildCadenceSegments(markers, a.CaptureSeconds);
        var markerSegmentsAvailable = segments.Any(x =>
            !x.Name.Equals("CAPTURE", StringComparison.OrdinalIgnoreCase));

        var exactTimelineUsable =
            timeline.Count > 0 &&
            bucketWidthMs > 0 &&
            droppedRows == 0;

        var callbacks = timeline
            .Where(r =>
                S(r, "Kind").Equals("event", StringComparison.OrdinalIgnoreCase) &&
                S(r, "Target").Equals("onUpdate", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(S(r, "Mod", "Owner")))
            .GroupBy(
                r => ResolverCallbackKey(
                    S(r, "Mod", "Owner"),
                    S(r, "Kind"),
                    S(r, "Target")),
                StringComparer.OrdinalIgnoreCase)
            .Select(g => ResolveCadenceCallback(
                g.ToList(),
                segments,
                a,
                bucketWidthMs,
                exactTimelineUsable,
                markerSegmentsAvailable))
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new
        {
            schemaVersion = "0.1",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                producer = "G-CET-Runtime-Profiler",
                stage = "runtime-cadence-resolver",
                source = ResolverInputFileName,
                domain = "cet/onUpdate"
            },
            policy = new
            {
                conservative = true,
                unknownMeansNoChange = true,
                sourceConfirmationRequiredForRewrite = true,
                note = "Exact runtime evidence resolves safe cadence candidates. Source structure must confirm timer/state/split boundaries before any rewrite."
            },
            quality = new
            {
                exactTimelineAvailable = timeline.Count > 0,
                exactTimelineUsable,
                droppedOnUpdateTimelineRows = droppedRows,
                bucketWidthMs = bucketWidthMs > 0 ? Round(bucketWidthMs, 3) : (double?)null,
                scenarioSegmentsAvailable = markerSegmentsAvailable,
                callbackCount = callbacks.Length
            },
            thresholds = new
            {
                minimumImpactMsPerSecond = CadenceMinimumImpactMsPerSecond,
                scenarioSensitiveRatio = CadenceScenarioSensitiveRatio,
                strongCadenceSupportPct = CadenceStrongSupport * 100.0,
                perScenarioCadenceSupportPct = CadenceScenarioSupport * 100.0,
                maximumResolvedIntervalMs = CadenceMaxIntervalMs
            },
            summary = new
            {
                frame = callbacks.Count(x => x.Classification == "FRAME"),
                exact = callbacks.Count(x => x.Classification == "EXACT"),
                activeDormant = callbacks.Count(x => x.Classification == "ACTIVE_DORMANT"),
                mixedSplit = callbacks.Count(x => x.Classification == "MIXED_SPLIT"),
                unknown = callbacks.Count(x => x.Classification == "UNKNOWN"),
                sourcePassCandidates = callbacks.Count(x => x.SourcePassCandidate)
            },
            callbacks
        };
    }

    private static CadenceCallbackResolution ResolveCadenceCallback(
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<CadenceSegment> segments,
        ResultAnalysis a,
        double bucketWidthMs,
        bool exactTimelineUsable,
        bool markerSegmentsAvailable)
    {
        var first = rows[0];
        var owner = S(first, "Mod", "Owner");
        var kind = S(first, "Kind");
        var target = S(first, "Target");

        var samples = rows
            .Select(r =>
            {
                var start = D(r, "BucketStartMs");
                var end = D(r, "BucketEndMs");
                if (end <= start)
                {
                    var width = D(r, "BucketWidthMs");
                    if (width > 0)
                        end = start + width;
                }

                var calls = L(r, "Calls");
                var exclusiveMs = D(r, "ExclusiveMs");

                return new CadenceBucketSample
                {
                    MidMs = start + Math.Max(0, end - start) * 0.5,
                    Calls = calls,
                    ExclusiveMs = exclusiveMs,
                    UsPerCall = calls > 0
                        ? exclusiveMs * 1000.0 / calls
                        : 0
                };
            })
            .Where(x => x.Calls > 0)
            .OrderBy(x => x.MidMs)
            .ToList();

        var totalCalls = samples.Sum(x => (double)x.Calls);
        var totalExclusiveMs = samples.Sum(x => x.ExclusiveMs);
        var callsPerSecond = a.CaptureSeconds > 0
            ? totalCalls / a.CaptureSeconds
            : 0;
        var exclusiveMsPerSecond = a.CaptureSeconds > 0
            ? totalExclusiveMs / a.CaptureSeconds
            : 0;
        var meanUsPerCall = totalCalls > 0
            ? totalExclusiveMs * 1000.0 / totalCalls
            : 0;

        var costValues = samples
            .Select(x => x.UsPerCall)
            .OrderBy(x => x)
            .ToArray();

        var medianUsPerCall = CadenceMedian(costValues);
        var baselineWorkShare = meanUsPerCall > 0
            ? Math.Clamp(medianUsPerCall / meanUsPerCall, 0, 1)
            : 1;

        var expectedBuckets =
            bucketWidthMs > 0 && a.CaptureSeconds > 0
                ? a.CaptureSeconds * 1000.0 / bucketWidthMs
                : 0;
        var observedBucketCoveragePct = expectedBuckets > 0
            ? Percent(samples.Count, expectedBuckets)
            : 0;

        var callsPerFrame =
            a.FrameTime is not null &&
            a.FrameTime.Correlated &&
            a.FrameTime.AverageFps > 0
                ? callsPerSecond / a.FrameTime.AverageFps
                : (double?)null;

        var scenarioEvidence = new List<CadenceScenarioEvidence>();
        foreach (var segment in segments)
        {
            var inSegment = samples
                .Where(x => x.MidMs >= segment.StartMs && x.MidMs < segment.EndMs)
                .ToList();

            if (inSegment.Count < 20)
                continue;

            var scenarioCalls = inSegment.Sum(x => (double)x.Calls);
            var scenarioMs = inSegment.Sum(x => x.ExclusiveMs);
            var scenarioMeanUs = scenarioCalls > 0
                ? scenarioMs * 1000.0 / scenarioCalls
                : 0;

            scenarioEvidence.Add(DetectScenarioCadence(
                segment.Name,
                inSegment,
                bucketWidthMs,
                scenarioMeanUs));
        }

        var scenarioCosts = scenarioEvidence
            .Select(x => x.MeanUsPerCall)
            .Where(x => x > 0 && double.IsFinite(x))
            .ToArray();

        var scenarioCostRatio = scenarioCosts.Length >= 2
            ? scenarioCosts.Max() / Math.Max(0.000001, scenarioCosts.Min())
            : 1.0;

        var usableCadences = scenarioEvidence
            .Where(x =>
                x.CadenceIntervalMs is not null &&
                x.CadenceSupportPct >= CadenceScenarioSupport * 100.0 &&
                x.SupportingGaps >= 4)
            .ToList();

        CadenceAggregate? cadence = null;
        if (usableCadences.Count > 0)
        {
            var seed = usableCadences
                .OrderByDescending(x => usableCadences
                    .Where(y => Math.Abs(
                        y.CadenceIntervalMs!.Value -
                        x.CadenceIntervalMs!.Value) <= bucketWidthMs)
                    .Sum(y => y.SupportingGaps))
                .ThenBy(x => x.CadenceIntervalMs)
                .First();

            var cluster = usableCadences
                .Where(x => Math.Abs(
                    x.CadenceIntervalMs!.Value -
                    seed.CadenceIntervalMs!.Value) <= bucketWidthMs)
                .ToList();

            var intervalGroups = cluster
                .GroupBy(x => x.CadenceIntervalMs!.Value)
                .Select(g => new
                {
                    IntervalMs = g.Key,
                    Repeats = g.Sum(x => x.SupportingGaps)
                })
                .OrderByDescending(x => x.Repeats)
                .ThenBy(x => x.IntervalMs)
                .ToList();

            var resolvedIntervalMs = intervalGroups[0].IntervalMs;
            var eligibleGaps = cluster.Sum(x => x.EligibleGaps);
            var supportingGaps = cluster.Sum(x => x.SupportingGaps);
            var supportPct = eligibleGaps > 0
                ? Percent(supportingGaps, eligibleGaps)
                : 0;

            cadence = new CadenceAggregate
            {
                IntervalMs = resolvedIntervalMs,
                SupportPct = supportPct,
                SupportingScenarios = cluster.Count,
                SupportingGaps = supportingGaps,
                EligibleGaps = eligibleGaps
            };
        }

        var strongCadence =
            cadence is not null &&
            cadence.SupportPct >= CadenceStrongSupport * 100.0 &&
            (cadence.SupportingScenarios >= 2 || cadence.SupportingGaps >= 10);

        var infrastructure = IsInfrastructureOwner(owner);
        var lowImpact = exclusiveMsPerSecond < CadenceMinimumImpactMsPerSecond;

        string classification;
        string recommendation;
        string reason;
        double confidence;

        if (!exactTimelineUsable || samples.Count < 100 || observedBucketCoveragePct < 20)
        {
            classification = "UNKNOWN";
            recommendation = "DO_NOT_CHANGE";
            reason = !exactTimelineUsable
                ? "Exact onUpdate timeline is unavailable, incomplete, or dropped rows."
                : "Too little exact callback coverage for a safe cadence decision.";
            confidence = 0;
        }
        else if (infrastructure)
        {
            classification = "FRAME";
            recommendation = "KEEP_FRAME";
            reason = "Profiler/scheduler infrastructure is excluded from cadence rewriting.";
            confidence = 1;
        }
        else if (lowImpact)
        {
            classification = "FRAME";
            recommendation = "KEEP_FRAME";
            reason = "Measured callback cost is below the resolver impact floor.";
            confidence = 0.95;
        }
        else if (strongCadence && baselineWorkShare <= 0.25)
        {
            classification = "EXACT";
            recommendation = "PACE_EXISTING_BODY";
            reason = "Exact timeline shows a dominant periodic body with a very small frame baseline.";
            confidence = Math.Clamp(cadence!.SupportPct / 100.0, 0, 1);
        }
        else if (strongCadence && baselineWorkShare <= 0.70)
        {
            classification = "MIXED_SPLIT";
            recommendation = "SPLIT_FRAME_AND_PACED_BODY";
            reason = "Exact timeline shows both meaningful per-frame baseline work and a repeatable periodic heavy body.";
            confidence = Math.Clamp(cadence!.SupportPct / 100.0, 0, 1);
        }
        else if (markerSegmentsAvailable &&
                 scenarioCostRatio >= CadenceScenarioSensitiveRatio &&
                 exclusiveMsPerSecond >= 0.50)
        {
            classification = "ACTIVE_DORMANT";
            recommendation = "SOURCE_STATE_GATE_REQUIRED";
            reason = "Normalized callback cost changes materially between WORLD/DRIVING/COMBAT without a safe periodic split.";
            confidence = Math.Clamp(
                0.55 + (scenarioCostRatio - CadenceScenarioSensitiveRatio) * 0.10,
                0.55,
                0.90);
        }
        else
        {
            classification = "FRAME";
            recommendation = "KEEP_FRAME";
            reason = "No repeatable runtime cadence or state split is strong enough to justify changing frame execution.";
            confidence = baselineWorkShare >= 0.75 ? 0.80 : 0.60;
        }

        var sourcePassCandidate =
            classification is "EXACT" or "MIXED_SPLIT" or "ACTIVE_DORMANT";

        var estimatedPeriodicWorkMsPerSecond =
            strongCadence
                ? exclusiveMsPerSecond * Math.Clamp(1.0 - baselineWorkShare, 0, 1)
                : 0;

        return new CadenceCallbackResolution
        {
            Owner = owner,
            Infrastructure = infrastructure,
            Kind = kind,
            Target = target,
            Classification = classification,
            Recommendation = recommendation,
            Confidence = confidence,
            SourcePassCandidate = sourcePassCandidate,
            SourceConfirmationRequired = sourcePassCandidate,
            ResolvedIntervalMs =
                classification is "EXACT" or "MIXED_SPLIT"
                    ? cadence?.IntervalMs
                    : null,
            Reason = reason,
            CallsPerSecond = callsPerSecond,
            CallsPerFrame = callsPerFrame,
            ExclusiveMsPerSecond = exclusiveMsPerSecond,
            MeanUsPerCall = meanUsPerCall,
            MedianUsPerCall = medianUsPerCall,
            BaselineWorkSharePct = baselineWorkShare * 100.0,
            EstimatedPeriodicWorkMsPerSecond = estimatedPeriodicWorkMsPerSecond,
            ObservedBuckets = samples.Count,
            ObservedBucketCoveragePct = observedBucketCoveragePct,
            ScenarioCostRatio = scenarioCostRatio,
            CadenceSupportPct = cadence?.SupportPct,
            CadenceSupportingScenarios = cadence?.SupportingScenarios ?? 0,
            CadenceSupportingGaps = cadence?.SupportingGaps ?? 0,
            ScenarioEvidence = scenarioEvidence
        };
    }

    private static CadenceScenarioEvidence DetectScenarioCadence(
        string name,
        IReadOnlyList<CadenceBucketSample> samples,
        double bucketWidthMs,
        double meanUsPerCall)
    {
        var values = samples
            .Select(x => x.UsPerCall)
            .OrderBy(x => x)
            .ToArray();

        var baseline = CadenceMedian(values);
        var deviations = values
            .Select(x => Math.Abs(x - baseline))
            .OrderBy(x => x)
            .ToArray();
        var mad = CadenceMedian(deviations);

        var threshold = Math.Max(
            baseline * 2.5,
            Math.Max(
                baseline + 6.0 * 1.4826 * mad,
                baseline + 5.0));

        var heavyTimes = samples
            .Where(x => x.UsPerCall >= threshold)
            .Select(x => x.MidMs)
            .OrderBy(x => x)
            .ToArray();

        var gaps = new List<double>();
        for (var i = 1; i < heavyTimes.Length; i++)
        {
            var gap = heavyTimes[i] - heavyTimes[i - 1];
            if (gap >= bucketWidthMs * 2.0 - 0.001 &&
                gap <= CadenceMaxIntervalMs + 0.001)
            {
                gaps.Add(gap);
            }
        }

        double? intervalMs = null;
        var supportPct = 0.0;
        var supportingGaps = 0;

        if (heavyTimes.Length >= 6 && gaps.Count >= 4)
        {
            var rounded = gaps
                .Select(x => Math.Max(
                    bucketWidthMs,
                    Math.Round(x / bucketWidthMs) * bucketWidthMs))
                .ToList();

            var mode = rounded
                .GroupBy(x => x)
                .Select(g => new { IntervalMs = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.IntervalMs)
                .First();

            intervalMs = mode.IntervalMs;
            supportingGaps = gaps.Count(x =>
                Math.Abs(x - intervalMs.Value) <= bucketWidthMs + 0.001);
            supportPct = Percent(supportingGaps, gaps.Count);
        }

        return new CadenceScenarioEvidence
        {
            Scenario = name,
            Buckets = samples.Count,
            MeanUsPerCall = meanUsPerCall,
            MedianUsPerCall = baseline,
            HeavyThresholdUsPerCall = threshold,
            HeavyBuckets = heavyTimes.Length,
            EligibleGaps = gaps.Count,
            SupportingGaps = supportingGaps,
            CadenceIntervalMs = intervalMs,
            CadenceSupportPct = supportPct
        };
    }

    private static List<CadenceSegment> BuildCadenceSegments(
        IReadOnlyList<Dictionary<string, string>> markers,
        double captureSeconds)
    {
        var names = new[] { "WORLD", "DRIVING", "COMBAT" };
        var result = new List<CadenceSegment>();

        foreach (var name in names)
        {
            var starts = markers
                .Where(r => S(r, "Label").Equals(
                    "SCENARIO_" + name + "_START",
                    StringComparison.OrdinalIgnoreCase))
                .Select(r => D(r, "CaptureMs"))
                .OrderBy(x => x)
                .ToList();

            var ends = markers
                .Where(r => S(r, "Label").Equals(
                    "SCENARIO_" + name + "_END",
                    StringComparison.OrdinalIgnoreCase))
                .Select(r => D(r, "CaptureMs"))
                .OrderBy(x => x)
                .ToList();

            var pairs = Math.Min(starts.Count, ends.Count);
            for (var i = 0; i < pairs; i++)
            {
                if (ends[i] > starts[i])
                {
                    result.Add(new CadenceSegment
                    {
                        Name = name,
                        StartMs = starts[i],
                        EndMs = ends[i]
                    });
                }
            }
        }

        if (result.Count == 0 && captureSeconds > 0)
        {
            result.Add(new CadenceSegment
            {
                Name = "CAPTURE",
                StartMs = 0,
                EndMs = captureSeconds * 1000.0
            });
        }

        return result;
    }

    private static double CadenceMedian(IReadOnlyList<double> sortedValues)
    {
        if (sortedValues.Count == 0)
            return 0;

        var middle = sortedValues.Count / 2;
        if ((sortedValues.Count & 1) == 1)
            return sortedValues[middle];

        return (sortedValues[middle - 1] + sortedValues[middle]) * 0.5;
    }

    private sealed class CadenceBucketSample
    {
        public double MidMs { get; init; }
        public long Calls { get; init; }
        public double ExclusiveMs { get; init; }
        public double UsPerCall { get; init; }
    }

    private sealed class CadenceSegment
    {
        public string Name { get; init; } = "";
        public double StartMs { get; init; }
        public double EndMs { get; init; }
    }

    private sealed class CadenceScenarioEvidence
    {
        public string Scenario { get; init; } = "";
        public int Buckets { get; init; }
        public double MeanUsPerCall { get; init; }
        public double MedianUsPerCall { get; init; }
        public double HeavyThresholdUsPerCall { get; init; }
        public int HeavyBuckets { get; init; }
        public int EligibleGaps { get; init; }
        public int SupportingGaps { get; init; }
        public double? CadenceIntervalMs { get; init; }
        public double CadenceSupportPct { get; init; }
    }

    private sealed class CadenceAggregate
    {
        public double IntervalMs { get; init; }
        public double SupportPct { get; init; }
        public int SupportingScenarios { get; init; }
        public int SupportingGaps { get; init; }
        public int EligibleGaps { get; init; }
    }

    private sealed class CadenceCallbackResolution
    {
        public string Owner { get; init; } = "";
        public bool Infrastructure { get; init; }
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string Classification { get; init; } = "UNKNOWN";
        public string Recommendation { get; init; } = "DO_NOT_CHANGE";
        public double Confidence { get; init; }
        public bool SourcePassCandidate { get; init; }
        public bool SourceConfirmationRequired { get; init; }
        public double? ResolvedIntervalMs { get; init; }
        public string Reason { get; init; } = "";
        public double CallsPerSecond { get; init; }
        public double? CallsPerFrame { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double MeanUsPerCall { get; init; }
        public double MedianUsPerCall { get; init; }
        public double BaselineWorkSharePct { get; init; }
        public double EstimatedPeriodicWorkMsPerSecond { get; init; }
        public int ObservedBuckets { get; init; }
        public double ObservedBucketCoveragePct { get; init; }
        public double ScenarioCostRatio { get; init; }
        public double? CadenceSupportPct { get; init; }
        public int CadenceSupportingScenarios { get; init; }
        public int CadenceSupportingGaps { get; init; }
        public List<CadenceScenarioEvidence> ScenarioEvidence { get; init; } = [];
    }
}
