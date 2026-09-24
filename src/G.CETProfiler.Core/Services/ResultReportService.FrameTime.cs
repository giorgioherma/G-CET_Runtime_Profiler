using System.Globalization;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed class FrameTimeAnalysis
    {
        public string SourceFile { get; init; } = "";
        public string AppVersion { get; init; } = "";
        public string GameName { get; init; } = "";
        public string GPU { get; init; } = "";
        public string Processor { get; init; } = "";
        public DateTimeOffset? CapFrameXStartUtc { get; init; }
        public DateTimeOffset? CetStartUtc { get; init; }
        public bool Correlated { get; init; }
        public bool ExactAlignment { get; init; }
        public string SyncQuality { get; init; } = "UNAVAILABLE";
        public double StartDeltaMs { get; init; }
        public double DurationDeltaMs { get; init; }
        public int FrameCount { get; init; }
        public double DurationSeconds { get; init; }
        public double MeanFrameMs { get; init; }
        public double MedianFrameMs { get; init; }
        public double P95FrameMs { get; init; }
        public double P99FrameMs { get; init; }
        public double MaxFrameMs { get; init; }
        public double AverageFps { get; init; }
        public int FramesOver25Ms { get; init; }
        public int FramesOver33Ms { get; init; }
        public int FramesOver50Ms { get; init; }
        public int FramesOver100Ms { get; init; }
        public double MeanCpuActiveMs { get; init; }
        public double P95CpuActiveMs { get; init; }
        public double MeanGpuActiveMs { get; init; }
        public double P95GpuActiveMs { get; init; }
        public double HighCetThresholdMs { get; init; }
        public int SlowFramesHighCet { get; init; }
        public int SlowFramesExactCallback { get; init; }
        public int SlowFramesSchedulerBurst { get; init; }
        public int SlowFramesCetNormal { get; init; }
        public int TopCetWindowsWithSlowFrame { get; init; }
        public int TopCetWindowCount { get; init; }
        public double HighCetWindowSlowRatePct { get; init; }
        public double NormalCetWindowSlowRatePct { get; init; }
        public double PearsonWindowCorrelation { get; init; }
        public double SpearmanWindowCorrelation { get; init; }
        public List<FrameTimeBucketMetric> Timeline { get; init; } = [];
        public List<FrameStallMetric> WorstFrames { get; init; } = [];
    }

    private sealed class CapFrameMetric
    {
        public int Index { get; init; }
        public double CapRelativeStartMs { get; init; }
        public double CetRelativeStartMs { get; init; }
        public double FrameMs { get; init; }
        public double CpuActiveMs { get; init; }
        public double GpuActiveMs { get; init; }
        public double PcLatencyMs { get; init; }
        public string FrameType { get; init; } = "";
    }

    private sealed class FrameTimeBucketMetric
    {
        public double StartMs { get; init; }
        public double EndMs { get; init; }
        public double CetMs { get; init; }
        public long CetCalls { get; init; }
        public string TopOwner { get; init; } = "";
        public double TopOwnerCetMs { get; init; }
        public double FrameMaxMs { get; init; }
        public double FrameMeanMs { get; init; }
        public double CpuActiveMaxMs { get; init; }
        public double GpuActiveMaxMs { get; init; }
        public int SlowFrameCount { get; init; }
        public bool HasCallbackSpike { get; init; }
        public bool HasSchedulerBurst { get; init; }
        public bool HighCet { get; init; }
    }

    private sealed class FrameStallMetric
    {
        public int FrameIndex { get; init; }
        public double StartMs { get; init; }
        public double FrameMs { get; init; }
        public double CpuActiveMs { get; init; }
        public double GpuActiveMs { get; init; }
        public double PcLatencyMs { get; init; }
        public string FrameType { get; init; } = "";
        public double CetWindowMs { get; init; }
        public string TopCetOwner { get; init; } = "";
        public double TopCetOwnerMs { get; init; }
        public bool HighCet { get; init; }
        public SpikeMetric? CallbackSpike { get; init; }
        public SchedulerBurstMetric? SchedulerBurst { get; init; }
        public string Evidence { get; init; } = "";
    }

    private sealed class ParsedCapFrameX
    {
        public string SourceFile { get; init; } = "";
        public string AppVersion { get; init; } = "";
        public string GameName { get; init; } = "";
        public string GPU { get; init; } = "";
        public string Processor { get; init; } = "";
        public DateTimeOffset? StartUtc { get; init; }
        public List<CapFrameMetric> Frames { get; init; } = [];
    }

    private static FrameTimeAnalysis? AnalyzeFrameTime(
        string captureRoot,
        IReadOnlyList<Dictionary<string, string>> markers,
        IReadOnlyList<WindowMetric> allWindows,
        IReadOnlyList<SpikeMetric> allSpikes,
        IReadOnlyList<SchedulerBurstMetric> schedulerBursts,
        double cetCaptureSeconds)
    {
        var parsed = TryReadCapFrameX(captureRoot);
        if (parsed is null || parsed.Frames.Count == 0)
            return null;

        var frameTimes = parsed.Frames.Select(x => x.FrameMs).Where(x => x > 0 && double.IsFinite(x)).ToList();
        var cpuTimes = parsed.Frames.Select(x => x.CpuActiveMs).Where(x => x > 0 && double.IsFinite(x)).ToList();
        var gpuTimes = parsed.Frames.Select(x => x.GpuActiveMs).Where(x => x > 0 && double.IsFinite(x)).ToList();

        var capDurationMs = parsed.Frames.Max(x => x.CapRelativeStartMs + x.FrameMs);
        var cetStartUnix = FindCetStartUnixMs(markers);
        DateTimeOffset? cetStartUtc = cetStartUnix is null
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(cetStartUnix.Value));

        var startDeltaMs = parsed.StartUtc is not null && cetStartUnix is not null
            ? parsed.StartUtc.Value.ToUnixTimeMilliseconds() - cetStartUnix.Value
            : double.NaN;

        var durationDeltaMs = cetCaptureSeconds > 0
            ? capDurationMs - cetCaptureSeconds * 1000.0
            : double.NaN;

        var absStart = double.IsFinite(startDeltaMs) ? Math.Abs(startDeltaMs) : double.PositiveInfinity;
        var absDuration = double.IsFinite(durationDeltaMs) ? Math.Abs(durationDeltaMs) : double.PositiveInfinity;

        var correlated = absStart <= 250.0;
        var exactAlignment = correlated && absStart <= 50.0;
        var syncQuality = !double.IsFinite(startDeltaMs)
            ? "NO CET EPOCH"
            : absStart <= 50.0
                ? "GOOD"
                : absStart <= 250.0
                    ? "COARSE"
                    : "NOT ALIGNED";

        var alignedFrames = parsed.Frames
            .Select(x => new CapFrameMetric
            {
                Index = x.Index,
                CapRelativeStartMs = x.CapRelativeStartMs,
                CetRelativeStartMs = correlated ? x.CapRelativeStartMs + startDeltaMs : x.CapRelativeStartMs,
                FrameMs = x.FrameMs,
                CpuActiveMs = x.CpuActiveMs,
                GpuActiveMs = x.GpuActiveMs,
                PcLatencyMs = x.PcLatencyMs,
                FrameType = x.FrameType
            })
            .ToList();

        var orderedWindows = allWindows.OrderBy(x => x.StartMs).ToList();
        var positiveCet = orderedWindows.Select(x => x.ExclusiveMs).Where(x => x > 0).OrderBy(x => x).ToList();
        var highCetThreshold = positiveCet.Count == 0 ? 0 : Percentile(positiveCet, 0.90);

        var timeline = correlated
            ? BuildFrameTimeBuckets(orderedWindows, alignedFrames, allSpikes, schedulerBursts, highCetThreshold, exactAlignment)
            : [];

        var slowFrames = alignedFrames.Where(x => x.FrameMs >= 33.3).ToList();
        var worstFrames = correlated
            ? BuildWorstFrameEvidence(alignedFrames, orderedWindows, allSpikes, schedulerBursts, highCetThreshold, exactAlignment)
            : alignedFrames
                .OrderByDescending(x => x.FrameMs)
                .Take(20)
                .Select(x => new FrameStallMetric
                {
                    FrameIndex = x.Index,
                    StartMs = x.CapRelativeStartMs,
                    FrameMs = x.FrameMs,
                    CpuActiveMs = x.CpuActiveMs,
                    GpuActiveMs = x.GpuActiveMs,
                    PcLatencyMs = x.PcLatencyMs,
                    FrameType = x.FrameType,
                    Evidence = "CapFrameX only — CET clocks were not aligned."
                })
                .ToList();

        var slowHighCet = correlated ? worstFrameCount(alignedFrames, orderedWindows, highCetThreshold, f => f.HighCet) : 0;
        var slowCallback = correlated && exactAlignment
            ? worstFrameCount(alignedFrames, orderedWindows, highCetThreshold, f => f.CallbackSpike is not null)
            : 0;
        var slowScheduler = correlated && exactAlignment
            ? worstFrameCount(alignedFrames, orderedWindows, highCetThreshold, f => f.SchedulerBurst is not null)
            : 0;
        var slowNormal = correlated
            ? worstFrameCount(alignedFrames, orderedWindows, highCetThreshold,
                f => !f.HighCet && f.CallbackSpike is null && f.SchedulerBurst is null)
            : 0;

        var topCetWindows = timeline.OrderByDescending(x => x.CetMs).Take(20).ToList();
        var topWithSlow = topCetWindows.Count(x => x.FrameMaxMs >= 33.3);

        var highWindows = timeline.Where(x => x.HighCet).ToList();
        var normalWindows = timeline.Where(x => !x.HighCet).ToList();
        var highSlowRate = highWindows.Count == 0
            ? 0
            : highWindows.Count(x => x.FrameMaxMs >= 33.3) * 100.0 / highWindows.Count;
        var normalSlowRate = normalWindows.Count == 0
            ? 0
            : normalWindows.Count(x => x.FrameMaxMs >= 33.3) * 100.0 / normalWindows.Count;

        var paired = timeline.Where(x => x.FrameMaxMs > 0).ToList();
        var pearson = paired.Count >= 3
            ? Pearson(paired.Select(x => x.CetMs).ToArray(), paired.Select(x => x.FrameMaxMs).ToArray())
            : 0;
        var spearman = paired.Count >= 3
            ? Spearman(paired.Select(x => x.CetMs).ToArray(), paired.Select(x => x.FrameMaxMs).ToArray())
            : 0;

        return new FrameTimeAnalysis
        {
            SourceFile = parsed.SourceFile,
            AppVersion = parsed.AppVersion,
            GameName = parsed.GameName,
            GPU = parsed.GPU,
            Processor = parsed.Processor,
            CapFrameXStartUtc = parsed.StartUtc,
            CetStartUtc = cetStartUtc,
            Correlated = correlated,
            ExactAlignment = exactAlignment,
            SyncQuality = syncQuality,
            StartDeltaMs = startDeltaMs,
            DurationDeltaMs = durationDeltaMs,
            FrameCount = frameTimes.Count,
            DurationSeconds = capDurationMs / 1000.0,
            MeanFrameMs = frameTimes.Count == 0 ? 0 : frameTimes.Average(),
            MedianFrameMs = Percentile(frameTimes, 0.50),
            P95FrameMs = Percentile(frameTimes, 0.95),
            P99FrameMs = Percentile(frameTimes, 0.99),
            MaxFrameMs = frameTimes.Count == 0 ? 0 : frameTimes.Max(),
            AverageFps = frameTimes.Count == 0 || frameTimes.Average() <= 0 ? 0 : 1000.0 / frameTimes.Average(),
            FramesOver25Ms = frameTimes.Count(x => x >= 25.0),
            FramesOver33Ms = frameTimes.Count(x => x >= 33.3),
            FramesOver50Ms = frameTimes.Count(x => x >= 50.0),
            FramesOver100Ms = frameTimes.Count(x => x >= 100.0),
            MeanCpuActiveMs = cpuTimes.Count == 0 ? 0 : cpuTimes.Average(),
            P95CpuActiveMs = Percentile(cpuTimes, 0.95),
            MeanGpuActiveMs = gpuTimes.Count == 0 ? 0 : gpuTimes.Average(),
            P95GpuActiveMs = Percentile(gpuTimes, 0.95),
            HighCetThresholdMs = highCetThreshold,
            SlowFramesHighCet = slowHighCet,
            SlowFramesExactCallback = slowCallback,
            SlowFramesSchedulerBurst = slowScheduler,
            SlowFramesCetNormal = slowNormal,
            TopCetWindowsWithSlowFrame = topWithSlow,
            TopCetWindowCount = topCetWindows.Count,
            HighCetWindowSlowRatePct = highSlowRate,
            NormalCetWindowSlowRatePct = normalSlowRate,
            PearsonWindowCorrelation = pearson,
            SpearmanWindowCorrelation = spearman,
            Timeline = timeline,
            WorstFrames = worstFrames
        };

        int worstFrameCount(
            IReadOnlyList<CapFrameMetric> frames,
            IReadOnlyList<WindowMetric> windows,
            double threshold,
            Func<FrameStallMetric, bool> predicate)
        {
            return BuildWorstFrameEvidence(
                    frames.Where(x => x.FrameMs >= 33.3).ToList(),
                    windows,
                    allSpikes,
                    schedulerBursts,
                    threshold,
                    exactAlignment,
                    take: int.MaxValue)
                .Count(predicate);
        }
    }

    private static ParsedCapFrameX? TryReadCapFrameX(string captureRoot)
    {
        var frameRoot = Path.Combine(captureRoot, "FrameTime");
        if (!Directory.Exists(frameRoot))
            return null;

        ParsedCapFrameX? best = null;

        foreach (var path in Directory.EnumerateFiles(frameRoot, "*.json", SearchOption.AllDirectories)
                     .Where(x => !string.Equals(Path.GetFileName(x), "CompanionManifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;
                if (!root.TryGetProperty("Info", out var info) ||
                    !root.TryGetProperty("Runs", out var runs) ||
                    runs.ValueKind != JsonValueKind.Array)
                    continue;

                DateTimeOffset? start = null;
                var creation = JsonString(info, "CreationDate");
                if (DateTimeOffset.TryParse(
                        creation,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var parsedStart))
                    start = parsedStart;

                var runBestFrames = new List<CapFrameMetric>();
                foreach (var run in runs.EnumerateArray())
                {
                    if (!run.TryGetProperty("CaptureData", out var data) || data.ValueKind != JsonValueKind.Object)
                        continue;

                    var time = JsonDoubleArray(data, "TimeInSeconds");
                    var frame = JsonDoubleArray(data, "MsBetweenPresents");
                    var cpu = JsonDoubleArray(data, "CpuActive");
                    var gpu = JsonDoubleArray(data, "GpuActive");
                    var latency = JsonDoubleArray(data, "PcLatency");
                    var dropped = JsonBoolArray(data, "Dropped");
                    var frameType = JsonStringArray(data, "FrameType");

                    var count = Math.Min(time.Count, frame.Count);
                    if (count == 0)
                        continue;

                    var parsedFrames = new List<CapFrameMetric>(count);
                    for (var i = 0; i < count; i++)
                    {
                        if (frame[i] <= 0 || !double.IsFinite(frame[i]) || !double.IsFinite(time[i]))
                            continue;
                        if (i < dropped.Count && dropped[i])
                            continue;

                        parsedFrames.Add(new CapFrameMetric
                        {
                            Index = i,
                            CapRelativeStartMs = time[i] * 1000.0,
                            CetRelativeStartMs = time[i] * 1000.0,
                            FrameMs = frame[i],
                            CpuActiveMs = i < cpu.Count && double.IsFinite(cpu[i]) ? Math.Max(0, cpu[i]) : 0,
                            GpuActiveMs = i < gpu.Count && double.IsFinite(gpu[i]) ? Math.Max(0, gpu[i]) : 0,
                            PcLatencyMs = i < latency.Count && double.IsFinite(latency[i]) ? Math.Max(0, latency[i]) : 0,
                            FrameType = i < frameType.Count ? frameType[i] : ""
                        });
                    }

                    if (parsedFrames.Count > runBestFrames.Count)
                        runBestFrames = parsedFrames;
                }

                if (runBestFrames.Count == 0)
                    continue;

                var candidate = new ParsedCapFrameX
                {
                    SourceFile = Path.GetRelativePath(captureRoot, path).Replace('\\', '/'),
                    AppVersion = JsonString(info, "AppVersion"),
                    GameName = JsonString(info, "GameName"),
                    GPU = JsonString(info, "GPU"),
                    Processor = JsonString(info, "Processor"),
                    StartUtc = start,
                    Frames = runBestFrames
                };

                if (best is null ||
                    candidate.Frames.Count > best.Frames.Count ||
                    (candidate.Frames.Count == best.Frames.Count &&
                     (candidate.StartUtc ?? DateTimeOffset.MinValue) > (best.StartUtc ?? DateTimeOffset.MinValue)))
                    best = candidate;
            }
            catch
            {
                // Ignore unrelated/custom JSON files in FrameTime. The raw companion
                // capture remains preserved even when it is not a CapFrameX schema.
            }
        }

        return best;
    }

    private static List<FrameTimeBucketMetric> BuildFrameTimeBuckets(
        IReadOnlyList<WindowMetric> windows,
        IReadOnlyList<CapFrameMetric> frames,
        IReadOnlyList<SpikeMetric> spikes,
        IReadOnlyList<SchedulerBurstMetric> bursts,
        double highCetThreshold,
        bool exactAlignment)
    {
        var output = new List<FrameTimeBucketMetric>(windows.Count);
        var frameIndex = 0;

        foreach (var window in windows)
        {
            while (frameIndex < frames.Count &&
                   frames[frameIndex].CetRelativeStartMs + frames[frameIndex].FrameMs < window.StartMs)
                frameIndex++;

            var inWindow = new List<CapFrameMetric>();
            for (var i = frameIndex; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (frame.CetRelativeStartMs >= window.EndMs)
                    break;

                if (Overlaps(
                        frame.CetRelativeStartMs,
                        frame.CetRelativeStartMs + frame.FrameMs,
                        window.StartMs,
                        window.EndMs))
                    inWindow.Add(frame);
            }

            output.Add(new FrameTimeBucketMetric
            {
                StartMs = window.StartMs,
                EndMs = window.EndMs,
                CetMs = window.ExclusiveMs,
                CetCalls = window.Calls,
                TopOwner = window.TopOwner,
                TopOwnerCetMs = window.TopOwnerExclusiveMs,
                FrameMaxMs = inWindow.Count == 0 ? 0 : inWindow.Max(x => x.FrameMs),
                FrameMeanMs = inWindow.Count == 0 ? 0 : inWindow.Average(x => x.FrameMs),
                CpuActiveMaxMs = inWindow.Count == 0 ? 0 : inWindow.Max(x => x.CpuActiveMs),
                GpuActiveMaxMs = inWindow.Count == 0 ? 0 : inWindow.Max(x => x.GpuActiveMs),
                SlowFrameCount = inWindow.Count(x => x.FrameMs >= 33.3),
                HasCallbackSpike = exactAlignment && spikes.Any(x => Overlaps(x.CaptureStartMs, x.CaptureEndMs, window.StartMs, window.EndMs)),
                HasSchedulerBurst = exactAlignment && bursts.Any(x => Overlaps(x.CaptureStartMs, x.CaptureEndMs, window.StartMs, window.EndMs)),
                HighCet = highCetThreshold > 0 && window.ExclusiveMs >= highCetThreshold
            });
        }

        return output;
    }

    private static List<FrameStallMetric> BuildWorstFrameEvidence(
        IReadOnlyList<CapFrameMetric> frames,
        IReadOnlyList<WindowMetric> windows,
        IReadOnlyList<SpikeMetric> spikes,
        IReadOnlyList<SchedulerBurstMetric> bursts,
        double highCetThreshold,
        bool exactAlignment,
        int take = 20)
    {
        var ordered = frames.OrderByDescending(x => x.FrameMs);
        if (take != int.MaxValue)
            ordered = ordered.Take(take).OrderByDescending(x => x.FrameMs);

        var output = new List<FrameStallMetric>();
        foreach (var frame in ordered)
        {
            var frameEnd = frame.CetRelativeStartMs + frame.FrameMs;
            var midpoint = frame.CetRelativeStartMs + frame.FrameMs * 0.5;
            var window = FindWindow(windows, midpoint);

            var callback = exactAlignment
                ? spikes
                    .Where(x => Overlaps(frame.CetRelativeStartMs, frameEnd, x.CaptureStartMs, x.CaptureEndMs))
                    .OrderByDescending(x => x.ExclusiveMs)
                    .FirstOrDefault()
                : null;

            var scheduler = exactAlignment
                ? bursts
                    .Where(x => Overlaps(frame.CetRelativeStartMs, frameEnd, x.CaptureStartMs, x.CaptureEndMs))
                    .OrderByDescending(x => x.TotalJobMs)
                    .FirstOrDefault()
                : null;

            var highCet = window is not null &&
                          highCetThreshold > 0 &&
                          window.ExclusiveMs >= highCetThreshold;

            var evidence = new List<string>();
            if (scheduler is not null)
                evidence.Add($"Scheduler burst {F(scheduler.TotalJobMs)} ms / {scheduler.JobCount} jobs");
            if (callback is not null)
                evidence.Add($"{callback.Mod} callback spike {F(callback.ExclusiveMs)} ms");
            if (highCet)
                evidence.Add("top-10% CET workload window");
            if (evidence.Count == 0)
                evidence.Add(exactAlignment ? "CET workload normal in aligned evidence" : "coarse CET-window alignment only");

            output.Add(new FrameStallMetric
            {
                FrameIndex = frame.Index,
                StartMs = frame.CetRelativeStartMs,
                FrameMs = frame.FrameMs,
                CpuActiveMs = frame.CpuActiveMs,
                GpuActiveMs = frame.GpuActiveMs,
                PcLatencyMs = frame.PcLatencyMs,
                FrameType = frame.FrameType,
                CetWindowMs = window?.ExclusiveMs ?? 0,
                TopCetOwner = window?.TopOwner ?? "",
                TopCetOwnerMs = window?.TopOwnerExclusiveMs ?? 0,
                HighCet = highCet,
                CallbackSpike = callback,
                SchedulerBurst = scheduler,
                Evidence = string.Join("; ", evidence)
            });
        }

        return output;
    }

    private static WindowMetric? FindWindow(IReadOnlyList<WindowMetric> windows, double timeMs)
    {
        var lo = 0;
        var hi = windows.Count - 1;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            var window = windows[mid];

            if (timeMs < window.StartMs)
            {
                hi = mid - 1;
            }
            else if (timeMs >= window.EndMs)
            {
                lo = mid + 1;
            }
            else
            {
                return window;
            }
        }

        return null;
    }

    private static double? FindCetStartUnixMs(IReadOnlyList<Dictionary<string, string>> markers)
    {
        var start = markers
            .Where(r => S(r, "Label").Contains("START", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => D(r, "CaptureMs"))
            .FirstOrDefault();

        if (start is null)
            return null;

        var epoch = D(start, "UnixEpochMs", "EpochMs");
        return epoch > 0 ? epoch : null;
    }

    private static bool Overlaps(double aStart, double aEnd, double bStart, double bEnd) =>
        aStart < bEnd && aEnd > bStart;

    private static string JsonString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";
        return "";
    }

    private static List<double> JsonDoubleArray(JsonElement element, string name)
    {
        var values = new List<double>();
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out var value))
                values.Add(value);
            else
                values.Add(0);
        }

        return values;
    }

    private static List<bool> JsonBoolArray(JsonElement element, string name)
    {
        var values = new List<bool>();
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in array.EnumerateArray())
            values.Add(item.ValueKind == JsonValueKind.True);

        return values;
    }

    private static List<string> JsonStringArray(JsonElement element, string name)
    {
        var values = new List<string>();
        if (!element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
            return values;

        foreach (var item in array.EnumerateArray())
            values.Add(item.ValueKind == JsonValueKind.String ? item.GetString() ?? "" : "");

        return values;
    }

    private static double Percentile(IEnumerable<double> source, double p)
    {
        var values = source.Where(double.IsFinite).OrderBy(x => x).ToArray();
        if (values.Length == 0) return 0;
        if (values.Length == 1) return values[0];

        p = Math.Clamp(p, 0, 1);
        var position = (values.Length - 1) * p;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        if (lower == upper) return values[lower];

        var fraction = position - lower;
        return values[lower] + (values[upper] - values[lower]) * fraction;
    }

    private static double Pearson(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 2) return 0;

        var mx = x.Take(n).Average();
        var my = y.Take(n).Average();
        double num = 0, dx2 = 0, dy2 = 0;

        for (var i = 0; i < n; i++)
        {
            var dx = x[i] - mx;
            var dy = y[i] - my;
            num += dx * dy;
            dx2 += dx * dx;
            dy2 += dy * dy;
        }

        var denom = Math.Sqrt(dx2 * dy2);
        return denom <= 0 ? 0 : num / denom;
    }

    private static double Spearman(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        var n = Math.Min(x.Count, y.Count);
        if (n < 2) return 0;
        return Pearson(Rank(x.Take(n).ToArray()), Rank(y.Take(n).ToArray()));
    }

    private static double[] Rank(IReadOnlyList<double> values)
    {
        var indexed = values.Select((value, index) => new { value, index })
            .OrderBy(x => x.value)
            .ToArray();
        var ranks = new double[values.Count];

        var i = 0;
        while (i < indexed.Length)
        {
            var j = i + 1;
            while (j < indexed.Length && Math.Abs(indexed[j].value - indexed[i].value) < 1e-12)
                j++;

            var rank = (i + 1 + j) / 2.0;
            for (var k = i; k < j; k++)
                ranks[indexed[k].index] = rank;

            i = j;
        }

        return ranks;
    }
    private static void AddFrameTimeFindings(List<Finding> findings, FrameTimeAnalysis? frameTime)
    {
        if (frameTime is null)
            return;

        if (!frameTime.Correlated)
        {
            findings.Insert(0, new Finding(
                "CapFrameX capture available",
                $"{frameTime.FrameCount:N0} frames · {F(frameTime.MeanFrameMs)} ms average · {F(frameTime.P99FrameMs)} ms P99",
                "Frametime statistics are available, but CET and CapFrameX start clocks were not close enough for CET/stall attribution."));
            return;
        }

        var worst = frameTime.WorstFrames.FirstOrDefault();
        if (worst is not null)
        {
            var cet = worst.CetWindowMs > 0
                ? $"{F(worst.CetWindowMs)} ms of measured CET work in the aligned 50 ms window"
                : "little measured CET work in the aligned 50 ms window";
            var owner = string.IsNullOrWhiteSpace(worst.TopCetOwner)
                ? ""
                : $" Largest CET owner in that window: {worst.TopCetOwner} ({F(worst.TopCetOwnerMs)} ms).";

            findings.Insert(0, new Finding(
                "Worst frametime event",
                $"{F(worst.FrameMs)} ms frame at {F(worst.StartMs / 1000.0, 3)} s; {cet}.{owner}",
                worst.Evidence));
        }

        if (frameTime.FramesOver33Ms > 0)
        {
            findings.Insert(Math.Min(1, findings.Count), new Finding(
                "Slow-frame CET overlap",
                $"{frameTime.SlowFramesHighCet} of {frameTime.FramesOver33Ms} frames ≥33.3 ms occurred during top-10% CET workload windows; {frameTime.SlowFramesCetNormal} occurred while CET evidence was otherwise normal.",
                $"Across 50 ms windows, CET workload vs maximum frametime has Spearman r={F(frameTime.SpearmanWindowCorrelation, 3)}. This is synchronized overlap evidence, not a claim that CET caused every slow frame."));
        }
    }

}
