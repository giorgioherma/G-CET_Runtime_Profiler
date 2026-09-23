using System.Globalization;
using System.Net;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed class ResultAnalysis
    {
        public List<OwnerMetric> Owners { get; init; } = [];
        public List<OwnerMetric> TopOwners { get; init; } = [];
        public List<OwnerMetric> CallVolume { get; init; } = [];
        public List<CallbackMetric> TopCallbacks { get; init; } = [];
        public List<CallbackMetric> SharedCallbacks { get; init; } = [];
        public List<SpikeMetric> Spikes { get; init; } = [];
        public List<WindowMetric> TopWindows { get; init; } = [];
        public List<WindowOwnerMetric> HeavyWindowOwners { get; init; } = [];
        public List<SchedulerJobMetric> SchedulerJobs { get; init; } = [];
        public List<SchedulerSpikeMetric> SchedulerSpikes { get; init; } = [];
        public SchedulerBurstMetric? WorstSchedulerBurst { get; init; }
        public OwnerMetric? ZeroEngine { get; init; }
        public List<Finding> Findings { get; init; } = [];
        public double CaptureSeconds { get; init; }
        public double TotalMsPerSecond { get; init; }
        public double TotalOneCorePct { get; init; }
        public long TotalCalls { get; init; }
        public double TotalCallsPerSecond { get; init; }
        public double SchedulerTotalMsPerSecond { get; init; }
    }

    private static ResultAnalysis Analyze(string captureRoot)
    {
        var byMod = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_ByMod.csv"));
        var detail = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Detail.csv"));
        var timeline = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Timeline.csv"));
        var spikes = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Spikes.csv"));
        var schedulerJobs = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Scheduler_ByJob.csv"));
        var schedulerSpikes = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Scheduler_Spikes.csv"));
        var schedulerBursts = ReadCsv(FindProfilerFile(captureRoot, "CET_Runtime_Profile_Scheduler_FrameBursts.csv"));

        var owners = byMod
            .Select(r => new OwnerMetric
            {
                Name = S(r, "Mod", "Owner"),
                Calls = L(r, "Calls"),
                CallsPerSecond = D(r, "CallsPerSecond"),
                ExclusiveMsPerSecond = D(r, "ExclusiveMsPerSecond", "MsPerSecond"),
                MeasuredOneCorePct = D(r, "MeasuredOneCorePct"),
                AvgExclusiveUs = D(r, "AvgExclusiveUs", "AvgUs"),
                MaxExclusiveMs = D(r, "MaxExclusiveMs", "MaxMs"),
                SharePct = D(r, "MeasuredExclusiveSharePct"),
                ElapsedSeconds = D(r, "ElapsedSeconds")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ToList();

        var totalMsPerSecond = owners.Sum(x => x.ExclusiveMsPerSecond);
        var totalOneCorePct = owners.Sum(x => x.MeasuredOneCorePct);
        var totalCalls = owners.Sum(x => x.Calls);
        var totalCallsPerSecond = owners.Sum(x => x.CallsPerSecond);
        var captureSeconds = owners.Select(x => x.ElapsedSeconds).DefaultIfEmpty(0).Max();

        var callbackGroups = detail
            .Where(r => !string.IsNullOrWhiteSpace(S(r, "Target")))
            .GroupBy(r => S(r, "Kind") + "\u001f" + S(r, "Target"), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToList();
                var first = rows[0];
                var topOwnerRow = rows.OrderByDescending(r => D(r, "ExclusiveMsPerSecond", "MsPerSecond")).First();
                return new CallbackMetric
                {
                    Kind = S(first, "Kind"),
                    Target = S(first, "Target"),
                    OwnerCount = rows.Select(r => S(r, "Mod", "Owner"))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count(),
                    Calls = rows.Sum(r => L(r, "Calls")),
                    CallsPerSecond = rows.Sum(r => D(r, "CallsPerSecond")),
                    ExclusiveMsPerSecond = rows.Sum(r => D(r, "ExclusiveMsPerSecond", "MsPerSecond")),
                    MaxExclusiveMs = rows.Select(r => D(r, "MaxExclusiveMs", "MaxMs")).DefaultIfEmpty(0).Max(),
                    TopOwner = S(topOwnerRow, "Mod", "Owner")
                };
            })
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ToList();

        var spikeMetrics = spikes
            .Select(r => new SpikeMetric
            {
                Sequence = L(r, "Sequence"),
                CaptureStartMs = D(r, "CaptureStartMs", "CaptureMs"),
                CaptureEndMs = D(r, "CaptureEndMs", "CaptureMs"),
                InclusiveMs = D(r, "InclusiveMs", "DurationMs"),
                ExclusiveMs = D(r, "ExclusiveMs", "DurationMs"),
                Mod = S(r, "Mod", "Owner"),
                Kind = S(r, "Kind", "JobType"),
                Target = S(r, "Target", "Job")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Mod) || x.ExclusiveMs > 0)
            .OrderByDescending(x => x.ExclusiveMs)
            .Take(12)
            .ToList();

        var topWindows = BuildHeavyWindows(timeline)
            .OrderByDescending(x => x.ExclusiveMs)
            .Take(20)
            .ToList();
        var heavyWindowOwners = BuildHeavyWindowOwnerPresence(timeline, topWindows);

        var schedulerJobMetrics = schedulerJobs
            .Select(r => new SchedulerJobMetric
            {
                Owner = S(r, "Owner"),
                JobType = S(r, "JobType"),
                Job = S(r, "Job"),
                IntervalValue = D(r, "IntervalValue"),
                IntervalUnit = S(r, "IntervalUnit"),
                Calls = L(r, "Calls"),
                CallsPerSecond = D(r, "CallsPerSecond"),
                MsPerSecond = D(r, "MsPerSecond"),
                AvgUs = D(r, "AvgUs"),
                MaxMs = D(r, "MaxMs")
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Owner) || !string.IsNullOrWhiteSpace(x.Job))
            .OrderByDescending(x => x.MsPerSecond)
            .ToList();

        var schedulerSpikeMetrics = schedulerSpikes
            .Select(r => new SchedulerSpikeMetric
            {
                Sequence = L(r, "Sequence"),
                Frame = L(r, "Frame"),
                DurationMs = D(r, "DurationMs"),
                Owner = S(r, "Owner"),
                JobType = S(r, "JobType"),
                Job = S(r, "Job"),
                IntervalValue = D(r, "IntervalValue"),
                IntervalUnit = S(r, "IntervalUnit")
            })
            .OrderByDescending(x => x.DurationMs)
            .Take(12)
            .ToList();

        var schedulerBurstMetrics = BuildSchedulerBursts(schedulerBursts);
        var worstSchedulerBurst = schedulerBurstMetrics
            .OrderByDescending(x => x.TotalJobMs)
            .FirstOrDefault();
        var schedulerTotalMsPerSecond = schedulerJobMetrics.Sum(x => x.MsPerSecond);
        var zeroEngine = owners.FirstOrDefault(x =>
            string.Equals(x.Name, "0-Engine", StringComparison.OrdinalIgnoreCase));

        var topOwners = owners.Take(12).ToList();
        var callVolume = owners
            .OrderByDescending(x => x.CallsPerSecond)
            .Take(12)
            .ToList();
        var topCallbacks = callbackGroups.Take(12).ToList();
        var sharedCallbacks = callbackGroups
            .Where(x => x.OwnerCount >= 3)
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenByDescending(x => x.CallsPerSecond)
            .Take(10)
            .ToList();

        var findings = BuildFindings(
            owners,
            totalMsPerSecond,
            totalCalls,
            topWindows,
            heavyWindowOwners,
            spikeMetrics,
            sharedCallbacks,
            worstSchedulerBurst);

        return new ResultAnalysis
        {
            Owners = owners,
            TopOwners = topOwners,
            CallVolume = callVolume,
            TopCallbacks = topCallbacks,
            SharedCallbacks = sharedCallbacks,
            Spikes = spikeMetrics,
            TopWindows = topWindows,
            HeavyWindowOwners = heavyWindowOwners,
            SchedulerJobs = schedulerJobMetrics,
            SchedulerSpikes = schedulerSpikeMetrics,
            WorstSchedulerBurst = worstSchedulerBurst,
            ZeroEngine = zeroEngine,
            Findings = findings,
            CaptureSeconds = captureSeconds,
            TotalMsPerSecond = totalMsPerSecond,
            TotalOneCorePct = totalOneCorePct,
            TotalCalls = totalCalls,
            TotalCallsPerSecond = totalCallsPerSecond,
            SchedulerTotalMsPerSecond = schedulerTotalMsPerSecond
        };
    }

    private static List<Finding> BuildFindings(
        IReadOnlyList<OwnerMetric> owners,
        double totalMsPerSecond,
        long totalCalls,
        IReadOnlyList<WindowMetric> topWindows,
        IReadOnlyList<WindowOwnerMetric> heavyWindowOwners,
        IReadOnlyList<SpikeMetric> spikes,
        IReadOnlyList<CallbackMetric> sharedCallbacks,
        SchedulerBurstMetric? worstSchedulerBurst)
    {
        var findings = new List<Finding>();

        var top = owners.FirstOrDefault();
        if (top is not null)
        {
            var explanation = string.Equals(top.Name, "0-Engine", StringComparison.OrdinalIgnoreCase)
                ? "0-Engine carries routed and scheduled work for client mods. Check the Scheduler breakdown before reading this total as framework overhead."
                : "This is the largest sustained CET-side workload measured in this capture.";

            findings.Add(new(
                "Largest measured CET workload",
                $"{top.Name}: {F(top.ExclusiveMsPerSecond)} ms/s ({F(EffectiveShare(top, totalMsPerSecond), 1)}% of measured CET work)",
                explanation));
        }

        if (worstSchedulerBurst is not null)
        {
            var cadence = worstSchedulerBurst.DominantCadenceJobs >= 2 &&
                          !string.IsNullOrWhiteSpace(worstSchedulerBurst.DominantCadence)
                ? $" {worstSchedulerBurst.DominantCadenceJobs} of them belong to the {worstSchedulerBurst.DominantCadence} cadence group."
                : "";

            findings.Add(new(
                "CET Scheduler spike",
                $"{F(worstSchedulerBurst.TotalJobMs)} ms of measured Scheduler job time; {worstSchedulerBurst.JobCount} jobs ran in one frame.{cadence}",
                "Scheduled CET work piled up on the same frame. The Scheduler section shows what was inside that spike."));
        }

        var topCall = owners.OrderByDescending(x => x.CallsPerSecond).FirstOrDefault();
        if (topCall is not null && topCall.Calls > 0)
        {
            findings.Add(new(
                "Highest CET call volume",
                $"{topCall.Name}: {N(topCall.Calls)} calls ({F(Percent(topCall.Calls, totalCalls), 1)}% of all measured calls; {F(topCall.CallsPerSecond, 0)} calls/s)",
                "High call volume can matter even when each individual call is cheap."));
        }

        var worstSpike = spikes.FirstOrDefault();
        if (worstSpike is not null)
        {
            findings.Add(new(
                "Largest recorded callback spike",
                $"{worstSpike.Mod} · {JoinCallback(worstSpike.Kind, worstSpike.Target)} · {F(worstSpike.ExclusiveMs)} ms exclusive",
                "This is the largest individual CET callback spike recorded above the profiler threshold."));
        }

        var shared = sharedCallbacks.FirstOrDefault();
        if (shared is not null)
        {
            findings.Add(new(
                "Shared hot callback",
                $"{JoinCallback(shared.Kind, shared.Target)}: {shared.OwnerCount} mods, {F(shared.CallsPerSecond, 0)} calls/s, {F(shared.ExclusiveMsPerSecond)} ms/s",
                "Several mods are entering the same CET callback boundary. This is a useful place to inspect repeated routing, filtering or shared work."));
        }

        var recurring = heavyWindowOwners.FirstOrDefault();
        if (recurring is not null &&
            topWindows.Count >= 4 &&
            recurring.WindowCount >= Math.Max(3, (int)Math.Ceiling(topWindows.Count * 0.5)))
        {
            findings.Add(new(
                "Keeps showing up in heavy CET windows",
                $"{recurring.Owner}: present in {recurring.WindowCount} of the {topWindows.Count} heaviest measured CET windows",
                "This owner is not only high on average; it repeatedly appears when measured CET workload is at its highest."));
        }

        return findings.Take(6).ToList();
    }

    private static List<WindowMetric> BuildHeavyWindows(IReadOnlyList<Dictionary<string, string>> timeline)
    {
        return timeline
            .Where(r => !string.IsNullOrWhiteSpace(S(r, "BucketIndex")))
            .GroupBy(r => S(r, "BucketIndex"), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var rows = g.ToList();
                var top = rows.OrderByDescending(r => D(r, "ExclusiveMs")).First();
                return new WindowMetric
                {
                    BucketIndex = S(rows[0], "BucketIndex"),
                    StartMs = rows.Select(r => D(r, "BucketStartMs")).DefaultIfEmpty(0).Min(),
                    EndMs = rows.Select(r => D(r, "BucketEndMs")).DefaultIfEmpty(0).Max(),
                    ExclusiveMs = rows.Sum(r => D(r, "ExclusiveMs")),
                    Calls = rows.Sum(r => L(r, "Calls")),
                    TopOwner = S(top, "Mod", "Owner"),
                    TopOwnerExclusiveMs = D(top, "ExclusiveMs")
                };
            })
            .ToList();
    }

    private static List<WindowOwnerMetric> BuildHeavyWindowOwnerPresence(
        IReadOnlyList<Dictionary<string, string>> timeline,
        IReadOnlyList<WindowMetric> topWindows)
    {
        var topIds = topWindows.Select(x => x.BucketIndex)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return timeline
            .Where(r => topIds.Contains(S(r, "BucketIndex")))
            .GroupBy(r => S(r, "Mod", "Owner"), StringComparer.OrdinalIgnoreCase)
            .Where(g => !string.IsNullOrWhiteSpace(g.Key))
            .Select(g => new WindowOwnerMetric
            {
                Owner = g.Key,
                WindowCount = g.Select(r => S(r, "BucketIndex"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                ExclusiveMs = g.Sum(r => D(r, "ExclusiveMs"))
            })
            .OrderByDescending(x => x.WindowCount)
            .ThenByDescending(x => x.ExclusiveMs)
            .ToList();
    }

    private static List<SchedulerBurstMetric> BuildSchedulerBursts(
        IReadOnlyList<Dictionary<string, string>> rows)
    {
        return rows
            .Where(r => !string.IsNullOrWhiteSpace(S(r, "BurstSequence")))
            .GroupBy(r => S(r, "BurstSequence"), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var list = g.ToList();
                var first = list[0];
                var cadence = list
                    .Select(r => FormatCadence(D(r, "IntervalValue"), S(r, "IntervalUnit")))
                    .Where(x => !string.IsNullOrWhiteSpace(x) && x != "—")
                    .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Select(x => new { Cadence = x.Key, Count = x.Count() })
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => x.Cadence, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                var totalJobMs = D(first, "TotalJobMs");
                if (totalJobMs <= 0)
                    totalJobMs = list.Sum(r => D(r, "JobMs"));

                var jobCount = (int)L(first, "JobCount");
                if (jobCount <= 0)
                    jobCount = list.Count;

                return new SchedulerBurstMetric
                {
                    Sequence = L(first, "BurstSequence"),
                    Frame = L(first, "Frame"),
                    SchedulerWallMs = D(first, "SchedulerWallMs"),
                    TotalJobMs = totalJobMs,
                    JobCount = jobCount,
                    DominantCadence = cadence?.Cadence ?? "",
                    DominantCadenceJobs = cadence?.Count ?? 0
                };
            })
            .ToList();
    }

    private static List<Dictionary<string, string>> ReadCsv(string? path)
    {
        var rows = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return rows;

        using var reader = new StreamReader(
            path,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            return rows;

        var headers = ParseCsvLine(headerLine.TrimStart('\uFEFF'))
            .Select(x => x.Trim())
            .ToArray();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var values = ParseCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length; i++)
                row[headers[i]] = i < values.Count ? values[i] : "";

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (ch == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        values.Add(current.ToString());
        return values;
    }

    private static string? FindProfilerFile(string root, string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(root, "Data", "Runtime", fileName),
            Path.Combine(root, "Data", "Scheduler", fileName),
            Path.Combine(root, "Data", "Metadata", fileName),
            Path.Combine(root, fileName)
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string S(Dictionary<string, string> row, params string[] names)
    {
        foreach (var name in names)
            if (row.TryGetValue(name, out var value))
                return value?.Trim() ?? "";

        return "";
    }

    private static double D(Dictionary<string, string> row, params string[] names)
    {
        var text = S(row, names);
        if (double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out var value))
            return double.IsFinite(value) ? value : 0;

        return 0;
    }

    private static long L(Dictionary<string, string> row, params string[] names)
    {
        var text = S(row, names);
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) &&
            double.IsFinite(d))
            return (long)Math.Round(d);

        return 0;
    }

    private static string FormatCadence(double value, string unit)
    {
        if (value <= 0 && string.IsNullOrWhiteSpace(unit))
            return "—";

        var v = Math.Abs(value - Math.Round(value)) < 0.000001
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);

        unit = unit.Trim();
        if (unit.Equals("second", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("seconds", StringComparison.OrdinalIgnoreCase) ||
            unit.Equals("sec", StringComparison.OrdinalIgnoreCase))
            unit = "s";
        if (unit.Equals("frame", StringComparison.OrdinalIgnoreCase))
            unit = "frames";

        return string.IsNullOrWhiteSpace(unit) ? v : v + " " + unit;
    }

    private static string JoinCallback(string kind, string target)
    {
        if (string.IsNullOrWhiteSpace(kind)) return target;
        if (string.IsNullOrWhiteSpace(target)) return kind;
        return kind + " · " + target;
    }

    private static double EffectiveShare(OwnerMetric metric, double totalMsPerSecond) =>
        metric.SharePct > 0
            ? metric.SharePct
            : Percent(metric.ExclusiveMsPerSecond, totalMsPerSecond);

    private static double Percent(long value, long total) =>
        total <= 0 ? 0 : value * 100.0 / total;

    private static double Percent(double value, double total) =>
        total <= 0 ? 0 : value * 100.0 / total;

    private static double Round(double value, int digits) =>
        Math.Round(value, digits, MidpointRounding.AwayFromZero);

    private static string F(double value, int digits = 2) =>
        value.ToString("N" + digits, CultureInfo.InvariantCulture);

    private static string N(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);

    private static string H(string? value) =>
        WebUtility.HtmlEncode(value ?? "");

    private static string Href(string value) =>
        WebUtility.HtmlEncode(value.Replace('\\', '/'));

    private sealed record Finding(string Title, string Evidence, string Explanation);

    private sealed class OwnerMetric
    {
        public string Name { get; init; } = "";
        public long Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double MeasuredOneCorePct { get; init; }
        public double AvgExclusiveUs { get; init; }
        public double MaxExclusiveMs { get; init; }
        public double SharePct { get; init; }
        public double ElapsedSeconds { get; init; }
    }

    private sealed class CallbackMetric
    {
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public int OwnerCount { get; init; }
        public long Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double MaxExclusiveMs { get; init; }
        public string TopOwner { get; init; } = "";
    }

    private sealed class SpikeMetric
    {
        public long Sequence { get; init; }
        public double CaptureStartMs { get; init; }
        public double CaptureEndMs { get; init; }
        public double InclusiveMs { get; init; }
        public double ExclusiveMs { get; init; }
        public string Mod { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
    }

    private sealed class WindowMetric
    {
        public string BucketIndex { get; init; } = "";
        public double StartMs { get; init; }
        public double EndMs { get; init; }
        public double ExclusiveMs { get; init; }
        public long Calls { get; init; }
        public string TopOwner { get; init; } = "";
        public double TopOwnerExclusiveMs { get; init; }
    }

    private sealed class WindowOwnerMetric
    {
        public string Owner { get; init; } = "";
        public int WindowCount { get; init; }
        public double ExclusiveMs { get; init; }
    }

    private sealed class SchedulerJobMetric
    {
        public string Owner { get; init; } = "";
        public string JobType { get; init; } = "";
        public string Job { get; init; } = "";
        public double IntervalValue { get; init; }
        public string IntervalUnit { get; init; } = "";
        public long Calls { get; init; }
        public double CallsPerSecond { get; init; }
        public double MsPerSecond { get; init; }
        public double AvgUs { get; init; }
        public double MaxMs { get; init; }
    }

    private sealed class SchedulerBurstMetric
    {
        public long Sequence { get; init; }
        public long Frame { get; init; }
        public double SchedulerWallMs { get; init; }
        public double TotalJobMs { get; init; }
        public int JobCount { get; init; }
        public string DominantCadence { get; init; } = "";
        public int DominantCadenceJobs { get; init; }
    }

    private sealed class SchedulerSpikeMetric
    {
        public long Sequence { get; init; }
        public long Frame { get; init; }
        public double DurationMs { get; init; }
        public string Owner { get; init; } = "";
        public string JobType { get; init; } = "";
        public string Job { get; init; } = "";
        public double IntervalValue { get; init; }
        public string IntervalUnit { get; init; } = "";
    }
}
