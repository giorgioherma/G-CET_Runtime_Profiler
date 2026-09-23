using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record ResultReportBuildResult(
    string ReportPath,
    string SummaryPath,
    int FindingsCount,
    bool HasCoreData,
    bool HasSchedulerData);

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
            schemaVersion = "1.0",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            scope = "CET-side runtime workload only",
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
            a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null);
    }
}
