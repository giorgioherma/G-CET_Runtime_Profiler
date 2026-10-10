using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record WorkloadStatus(
        double CaptureMs, long Queued, long Active, long DeferredFrames,
        long PhasePending, long PhasePostponed, long Errors,
        long FrameActive, long FramePaused, long StateWatchers);

    private sealed class WorkloadTelemetry
    {
        public List<WorkloadStatus> Statuses { get; init; } = [];
        public List<SchedulerJobMetric> MeasuredClients { get; init; } = [];
        public bool HasEvidence => Statuses.Count > 0 || MeasuredClients.Count > 0;
        public long PeakQueue => Statuses.Count == 0 ? 0 : Statuses.Max(x => x.Queued);
        public long PeakPhasePending => Statuses.Count == 0 ? 0 : Statuses.Max(x => x.PhasePending);
        public double MeasuredNestedMsPerSecond => MeasuredClients.Sum(x => x.MsPerSecond);
        public long MeasuredCalls => MeasuredClients.Sum(x => x.Calls);
        public WorkloadStatus? Latest => Statuses.LastOrDefault();
    }

    private static WorkloadTelemetry AnalyzeWorkload(
        IReadOnlyList<Dictionary<string,string>> markers,
        IReadOnlyList<SchedulerJobMetric> schedulerJobs)
    {
        var samples = new List<WorkloadStatus>();
        foreach (var marker in markers)
        {
            var label = S(marker, "Label");
            if (!label.StartsWith("WORKLOAD_V1_", StringComparison.Ordinal)) continue;
            var parts = label.Split('_');
            if (parts.Length != 20 || parts[0] != "WORKLOAD" || parts[1] != "V1")
                continue;
            var map = new Dictionary<string,long>(StringComparer.Ordinal);
            var valid = true;
            for (var i = 2; i + 1 < parts.Length; i += 2)
            {
                if (!long.TryParse(parts[i+1], out var value) || value < 0)
                {
                    valid = false;
                    break;
                }
                map[parts[i]] = value;
            }
            if (!valid || !new [] { "Q","A","D","P","H","E","F","Z","S" }
                .All(map.ContainsKey))
                continue;
            var time = D(marker, "CaptureMs");
            if (!double.IsFinite(time) || time < 0) continue;
            samples.Add(new WorkloadStatus(time,
                map["Q"], map["A"], map["D"], map["P"], map["H"],
                map["E"], map["F"], map["Z"], map["S"]));
        }
        var jobs = schedulerJobs
            .Where(j => j.JobType.StartsWith("gcet-", StringComparison.Ordinal))
            .OrderByDescending(j => j.MsPerSecond)
            .ToList();
        return new WorkloadTelemetry {
            Statuses = samples.OrderBy(x => x.CaptureMs).ToList(),
            MeasuredClients = jobs
        };
    }

    private static void AppendWorkloadTelemetry(StringBuilder sb, WorkloadTelemetry data)
    {
        if (!data.HasEvidence) return;
        sb.Append("<div class=\"section\"><h2>0-Engine workload services</h2><div class=\"grid\">");
        MetricCard(sb, "Instrumented client calls", N(data.MeasuredCalls),
            "Cooperative work, deferred phase, dormant frame listeners and state signals");
        MetricCard(sb, "Measured nested work", F(data.MeasuredNestedMsPerSecond, 3) + " ms/s",
            "Already nested inside callback or stock Scheduler time; do not add to totals");
        if (data.Statuses.Count > 0)
        {
            MetricCard(sb, "Largest queue backlog", N(data.PeakQueue),
                "15-second status checkpoints; not an instantaneous peak guarantee");
            MetricCard(sb, "Largest deferred phase backlog", N(data.PeakPhasePending),
                "Optional maintenance work only");
            if (data.Latest is { } latest)
                MetricCard(sb, "Most recent listener state",
                    N(latest.FrameActive) + " active / " + N(latest.FramePaused) + " paused",
                    N(latest.StateWatchers) + " state watchers");
        }
        sb.Append("</div><div class=\"note\">All four services are opt-in. ");
        sb.Append("Existing 0-Engine Scheduler timing remains authoritative; work-service ");
        sb.Append("timings measure nested client operations and cannot be summed with parent callbacks. ");
        sb.Append("Status snapshots are sampled every 15 seconds while a capture is running. ");
        sb.Append("No optimization is automatically authorized by these counters.");
        sb.Append("</div><table><thead><tr><th>Owner</th><th>Service</th><th>Job</th>");
        sb.Append("<th>Calls</th><th>ms/s</th><th>Max ms</th></tr></thead><tbody>");
        foreach (var row in data.MeasuredClients.Take(25))
        {
            sb.Append("<tr><td>").Append(H(row.Owner))
              .Append("</td><td>").Append(H(row.JobType))
              .Append("</td><td>").Append(H(row.Job))
              .Append("</td><td>").Append(N(row.Calls))
              .Append("</td><td>").Append(F(row.MsPerSecond, 3))
              .Append("</td><td>").Append(F(row.MaxMs, 3))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table></div>");
    }
}
