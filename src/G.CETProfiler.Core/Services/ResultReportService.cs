using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record ResultReportBuildResult(
    string ReportPath,
    string SummaryPath,
    int FindingsCount,
    bool HasCoreData,
    bool HasSchedulerData,
    bool HasFrameTimeData);

/// <summary>
/// Human-first post-capture interpretation for the standalone CET profiler.
/// Native profiler measurement remains unchanged; this service only organizes and
/// interprets the verified CSV output after collection.
/// </summary>
public static partial class ResultReportService
{
    public const string ReportFileName = "CET_Report.html";
    public const string SummaryFileName = "CET_Summary.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly HashSet<string> SchedulerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "CET_Runtime_Profile_Scheduler_ByJob.csv",
        "CET_Runtime_Profile_Scheduler_Spikes.csv",
        "CET_Runtime_Profile_Scheduler_FrameBursts.csv"
    };

    private static readonly HashSet<string> RuntimeFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "CET_Runtime_Profile_ByMod.csv",
        "CET_Runtime_Profile_ByModKind.csv",
        "CET_Runtime_Profile_Detail.csv",
        "CET_Runtime_Profile_Spikes.csv",
        "CET_Runtime_Profile_Timeline.csv",
        "CET_Runtime_Profile_Markers.csv"
    };

    public static string GetArchiveRelativePath(string fileName)
    {
        if (SchedulerFiles.Contains(fileName))
            return Path.Combine("Data", "Scheduler", fileName);

        if (RuntimeFiles.Contains(fileName))
            return Path.Combine("Data", "Runtime", fileName);

        return Path.Combine("Data", "Metadata", fileName);
    }

    public static ResultReportBuildResult Generate(string captureRoot)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));

        captureRoot = Path.GetFullPath(captureRoot);
        Directory.CreateDirectory(captureRoot);

        var a = Analyze(captureRoot);
        var summaryPath = Path.Combine(captureRoot, SummaryFileName);
        var reportPath = Path.Combine(captureRoot, ReportFileName);

        var summary = new
        {
            schemaVersion = "1.2",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            scope = a.FrameTime is null
                ? "CET-side runtime workload only"
                : "CET-side runtime workload with optional CapFrameX frametime correlation",
            capture = new
            {
                elapsedSeconds = Round(a.CaptureSeconds, 3),
                activeOwners = a.Owners.Count,
                measuredCalls = a.TotalCalls,
                measuredCallsPerSecond = Round(a.TotalCallsPerSecond, 3),
                measuredExclusiveMsPerSecond = Round(a.TotalMsPerSecond, 6),
                measuredOneCorePct = Round(a.TotalOneCorePct, 6)
            },
            topOwners = a.TopOwners.Select(x => new
            {
                owner = x.Name,
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                measuredSharePct = Round(EffectiveShare(x, a.TotalMsPerSecond), 3),
                maxExclusiveMs = Round(x.MaxExclusiveMs, 6)
            }),
            callVolume = a.CallVolume.Select(x => new
            {
                owner = x.Name,
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                callSharePct = Round(Percent(x.Calls, a.TotalCalls), 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6)
            }),
            callbackHotspots = a.TopCallbacks.Select(x => new
            {
                kind = x.Kind,
                target = x.Target,
                owners = x.OwnerCount,
                calls = x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                exclusiveMsPerSecond = Round(x.ExclusiveMsPerSecond, 6),
                maxExclusiveMs = Round(x.MaxExclusiveMs, 6),
                topOwner = x.TopOwner
            }),
            heavyCetWindows = a.TopWindows.Take(10).Select(x => new
            {
                bucketIndex = x.BucketIndex,
                startMs = Round(x.StartMs, 3),
                endMs = Round(x.EndMs, 3),
                exclusiveMs = Round(x.ExclusiveMs, 6),
                calls = x.Calls,
                topOwner = x.TopOwner,
                topOwnerExclusiveMs = Round(x.TopOwnerExclusiveMs, 6)
            }),
            spikes = a.Spikes.Select(x => new
            {
                x.Sequence,
                startMs = Round(x.CaptureStartMs, 3),
                endMs = Round(x.CaptureEndMs, 3),
                exclusiveMs = Round(x.ExclusiveMs, 6),
                inclusiveMs = Round(x.InclusiveMs, 6),
                owner = x.Mod,
                kind = x.Kind,
                target = x.Target
            }),
            scheduler = new
            {
                present = a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null,
                zeroEngineBoundaryMsPerSecond = a.ZeroEngine is null
                    ? (double?)null
                    : Round(a.ZeroEngine.ExclusiveMsPerSecond, 6),
                scheduledJobMsPerSecond = Round(a.SchedulerTotalMsPerSecond, 6),
                jobs = a.SchedulerJobs.Count,
                topJobs = a.SchedulerJobs.Take(12).Select(x => new
                {
                    owner = x.Owner,
                    jobType = x.JobType,
                    job = x.Job,
                    cadence = FormatCadence(x.IntervalValue, x.IntervalUnit),
                    callsPerSecond = Round(x.CallsPerSecond, 3),
                    msPerSecond = Round(x.MsPerSecond, 6),
                    maxMs = Round(x.MaxMs, 6)
                }),
                worstBurst = a.WorstSchedulerBurst is null
                    ? null
                    : new
                    {
                        burstSequence = a.WorstSchedulerBurst.Sequence,
                        frame = a.WorstSchedulerBurst.Frame,
                        totalJobMs = Round(a.WorstSchedulerBurst.TotalJobMs, 6),
                        schedulerWallMs = Round(a.WorstSchedulerBurst.SchedulerWallMs, 6),
                        jobCount = a.WorstSchedulerBurst.JobCount,
                        dominantCadence = a.WorstSchedulerBurst.DominantCadence,
                        dominantCadenceJobs = a.WorstSchedulerBurst.DominantCadenceJobs
                    },
                topJobSpikes = a.SchedulerSpikes.Select(x => new
                {
                    x.Sequence,
                    x.Frame,
                    durationMs = Round(x.DurationMs, 6),
                    owner = x.Owner,
                    jobType = x.JobType,
                    job = x.Job,
                    cadence = FormatCadence(x.IntervalValue, x.IntervalUnit)
                })
            },
            frameTime = a.FrameTime is null
                ? null
                : new
                {
                    source = a.FrameTime.SourceFile,
                    appVersion = a.FrameTime.AppVersion,
                    game = a.FrameTime.GameName,
                    gpu = a.FrameTime.GPU,
                    processor = a.FrameTime.Processor,
                    sync = new
                    {
                        quality = a.FrameTime.SyncQuality,
                        correlated = a.FrameTime.Correlated,
                        exactAlignment = a.FrameTime.ExactAlignment,
                        alignmentMethod = a.FrameTime.AlignmentMethod,
                        capFrameXRecordUtc = a.FrameTime.CapFrameXRecordUtc?.ToString("O"),
                        cetStartUtc = a.FrameTime.CetStartUtc?.ToString("O"),
                        startDeltaMs = double.IsFinite(a.FrameTime.StartDeltaMs) ? Round(a.FrameTime.StartDeltaMs, 3) : (double?)null,
                        durationDeltaMs = double.IsFinite(a.FrameTime.DurationDeltaMs) ? Round(a.FrameTime.DurationDeltaMs, 3) : (double?)null,
                        recordLagMs = double.IsFinite(a.FrameTime.RecordLagMs) ? Round(a.FrameTime.RecordLagMs, 3) : (double?)null
                    },
                    frames = new
                    {
                        count = a.FrameTime.FrameCount,
                        durationSeconds = Round(a.FrameTime.DurationSeconds, 3),
                        averageFps = Round(a.FrameTime.AverageFps, 3),
                        meanMs = Round(a.FrameTime.MeanFrameMs, 6),
                        medianMs = Round(a.FrameTime.MedianFrameMs, 6),
                        p95Ms = Round(a.FrameTime.P95FrameMs, 6),
                        p99Ms = Round(a.FrameTime.P99FrameMs, 6),
                        maxMs = Round(a.FrameTime.MaxFrameMs, 6),
                        over25Ms = a.FrameTime.FramesOver25Ms,
                        over33_3Ms = a.FrameTime.FramesOver33Ms,
                        over50Ms = a.FrameTime.FramesOver50Ms,
                        over100Ms = a.FrameTime.FramesOver100Ms,
                        meanCpuActiveMs = Round(a.FrameTime.MeanCpuActiveMs, 6),
                        p95CpuActiveMs = Round(a.FrameTime.P95CpuActiveMs, 6),
                        meanGpuActiveMs = Round(a.FrameTime.MeanGpuActiveMs, 6),
                        p95GpuActiveMs = Round(a.FrameTime.P95GpuActiveMs, 6)
                    },
                    correlation = new
                    {
                        highCetThresholdMsPer50msWindow = Round(a.FrameTime.HighCetThresholdMs, 6),
                        slowFramesHighCet = a.FrameTime.SlowFramesHighCet,
                        slowFramesExactCallback = a.FrameTime.SlowFramesExactCallback,
                        slowFramesSchedulerBurst = a.FrameTime.SlowFramesSchedulerBurst,
                        slowFramesCetNormal = a.FrameTime.SlowFramesCetNormal,
                        topCetWindowsWithSlowFrame = a.FrameTime.TopCetWindowsWithSlowFrame,
                        topCetWindowCount = a.FrameTime.TopCetWindowCount,
                        highCetWindowSlowRatePct = Round(a.FrameTime.HighCetWindowSlowRatePct, 3),
                        normalCetWindowSlowRatePct = Round(a.FrameTime.NormalCetWindowSlowRatePct, 3),
                        pearson50ms = Round(a.FrameTime.PearsonWindowCorrelation, 6),
                        spearman50ms = Round(a.FrameTime.SpearmanWindowCorrelation, 6)
                    },
                    worstFrames = a.FrameTime.WorstFrames.Select(x => new
                    {
                        frameIndex = x.FrameIndex,
                        startMs = Round(x.StartMs, 3),
                        frameMs = Round(x.FrameMs, 6),
                        cpuActiveMs = Round(x.CpuActiveMs, 6),
                        gpuActiveMs = Round(x.GpuActiveMs, 6),
                        pcLatencyMs = Round(x.PcLatencyMs, 6),
                        frameType = x.FrameType,
                        cetWindowMs = Round(x.CetWindowMs, 6),
                        topCetOwner = x.TopCetOwner,
                        topCetOwnerMs = Round(x.TopCetOwnerMs, 6),
                        highCet = x.HighCet,
                        evidence = x.Evidence
                    })
                },
            findings = a.Findings.Select(x => new
            {
                x.Title,
                x.Evidence,
                x.Explanation
            }),
            data = new
            {
                runtime = RuntimeFiles
                    .Where(x => File.Exists(Path.Combine(captureRoot, "Data", "Runtime", x)))
                    .OrderBy(x => x)
                    .Select(x => "Data/Runtime/" + x),
                scheduler = SchedulerFiles
                    .Where(x => File.Exists(Path.Combine(captureRoot, "Data", "Scheduler", x)))
                    .OrderBy(x => x)
                    .Select(x => "Data/Scheduler/" + x)
            }
        };

        File.WriteAllText(
            summaryPath,
            JsonSerializer.Serialize(summary, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        File.WriteAllText(
            reportPath,
            BuildHtml(a, captureRoot),
            new UTF8Encoding(false));

        return new ResultReportBuildResult(
            reportPath,
            summaryPath,
            a.Findings.Count,
            a.Owners.Count > 0 || a.TopCallbacks.Count > 0,
            a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null,
            a.FrameTime is not null);
    }
}
