namespace GCETRuntimeProfiler.Core.Services;

internal sealed record BurstSample(double StartMs, double ExclusiveMs);

internal sealed record BurstProfile(
    int SpikeCount,
    double SpikeRatePct,
    double MedianExclusiveMs,
    double P95ExclusiveMs,
    double MaxExclusiveMs,
    double MedianIntervalMs,
    double IntervalMadMs,
    double IntervalJitterPct,
    double PeriodicSupportPct,
    bool SustainedHot,
    bool PeriodicStutter,
    bool BurstHot,
    bool CatastrophicBurst,
    bool StutterMaterial,
    string PrimaryClass,
    string[] Classes);

internal static class BurstAnalysisService
{
    internal const double SustainedHotThresholdMsPerSecond = 3.0;
    internal const double BurstHotP95ThresholdMs = 8.0;
    internal const double CatastrophicBurstThresholdMs = 33.3;
    internal const int PeriodicMinimumSpikes = 4;
    internal const double PeriodicMinimumIntervalMs = 250.0;
    internal const double PeriodicMaximumIntervalMs = 10_000.0;
    internal const double PeriodicMaximumJitterPct = 20.0;
    internal const double PeriodicIntervalTolerancePct = 20.0;
    internal const double PeriodicMinimumSupportPct = 75.0;

    internal static BurstProfile Analyze(
        IEnumerable<BurstSample> samples,
        double sustainedMsPerSecond,
        long totalCalls)
    {
        var ordered = samples
            .Where(x =>
                double.IsFinite(x.StartMs) &&
                double.IsFinite(x.ExclusiveMs) &&
                x.ExclusiveMs > 0)
            .OrderBy(x => x.StartMs)
            .ToArray();

        var durations = ordered
            .Select(x => x.ExclusiveMs)
            .OrderBy(x => x)
            .ToArray();

        var intervals = ordered
            .Zip(ordered.Skip(1), (a, b) => b.StartMs - a.StartMs)
            .Where(x => double.IsFinite(x) && x > 0)
            .OrderBy(x => x)
            .ToArray();

        var medianExclusive = Percentile(durations, 0.50);
        var p95Exclusive = Percentile(durations, 0.95);
        var maxExclusive = durations.Length == 0 ? 0 : durations[^1];
        var medianInterval = Percentile(intervals, 0.50);
        var intervalMad = intervals.Length == 0
            ? 0
            : Percentile(
                intervals
                    .Select(x => Math.Abs(x - medianInterval))
                    .OrderBy(x => x)
                    .ToArray(),
                0.50);
        var intervalJitterPct = medianInterval > 0
            ? intervalMad / medianInterval * 100.0
            : 0.0;
        var intervalTolerance =
            medianInterval * PeriodicIntervalTolerancePct / 100.0;
        var periodicSupportPct =
            intervals.Length > 0 && medianInterval > 0
                ? intervals.Count(x =>
                    Math.Abs(x - medianInterval) <= intervalTolerance) *
                  100.0 / intervals.Length
                : 0.0;

        var sustainedHot =
            sustainedMsPerSecond >= SustainedHotThresholdMsPerSecond;

        var periodicStutter =
            ordered.Length >= PeriodicMinimumSpikes &&
            intervals.Length >= PeriodicMinimumSpikes - 1 &&
            medianInterval >= PeriodicMinimumIntervalMs &&
            medianInterval <= PeriodicMaximumIntervalMs &&
            intervalJitterPct <= PeriodicMaximumJitterPct &&
            periodicSupportPct >= PeriodicMinimumSupportPct;

        var burstHot =
            ordered.Length >= 3 &&
            p95Exclusive >= BurstHotP95ThresholdMs;

        var catastrophic =
            maxExclusive >= CatastrophicBurstThresholdMs;

        var stutterMaterial =
            periodicStutter ||
            burstHot ||
            catastrophic;

        var classes = new List<string>();
        if (sustainedHot) classes.Add("SUSTAINED_HOT");
        if (periodicStutter) classes.Add("PERIODIC_STUTTER");
        if (burstHot) classes.Add("BURST_HOT");
        if (catastrophic) classes.Add("CATASTROPHIC_BURST");
        if (classes.Count == 0)
            classes.Add(ordered.Length > 0 ? "RECORDED_SPIKES" : "STEADY");

        var primary =
            catastrophic ? "CATASTROPHIC_BURST" :
            periodicStutter ? "PERIODIC_STUTTER" :
            burstHot ? "BURST_HOT" :
            sustainedHot ? "SUSTAINED_HOT" :
            ordered.Length > 0 ? "RECORDED_SPIKES" :
            "STEADY";

        return new BurstProfile(
            ordered.Length,
            totalCalls > 0 ? ordered.Length * 100.0 / totalCalls : 0.0,
            medianExclusive,
            p95Exclusive,
            maxExclusive,
            medianInterval,
            intervalMad,
            intervalJitterPct,
            periodicSupportPct,
            sustainedHot,
            periodicStutter,
            burstHot,
            catastrophic,
            stutterMaterial,
            primary,
            classes.ToArray());
    }

    private static double Percentile(
        IReadOnlyList<double> sortedValues,
        double percentile)
    {
        if (sortedValues.Count == 0)
            return 0.0;
        if (sortedValues.Count == 1)
            return sortedValues[0];

        var position =
            Math.Clamp(percentile, 0.0, 1.0) *
            (sortedValues.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper)
            return sortedValues[lower];

        var fraction = position - lower;
        return sortedValues[lower] +
               (sortedValues[upper] - sortedValues[lower]) * fraction;
    }
}
