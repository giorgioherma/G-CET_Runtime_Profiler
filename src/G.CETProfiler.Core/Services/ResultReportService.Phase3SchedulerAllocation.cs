using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record Phase3SchedulerFrame(
        long Frames, long FrameBytes, long FrameOps, long RootBytes, long RootOps,
        long ResidualBytes, long ResidualOps, long Dropped, bool Reconciled);

    private static Phase3SchedulerFrame? ReadPhase3SchedulerFrame(string captureRoot)
    {
        var rows = ReadCsv(FindProfilerFile(
            captureRoot, "CET_Runtime_Profile_Scheduler_FrameworkAllocation.csv"));
        if (rows.Count != 1) return null;
        var r = rows[0];
        var required = new[] { "Frames", "FramePositiveBytes", "FrameAllocationOperations",
            "RootJobPositiveBytes", "RootJobAllocationOperations",
            "OutsideRootJobsPositiveBytes", "OutsideRootJobsAllocationOperations",
            "DroppedOrUnpairedScopes" };
        if (!required.All(r.ContainsKey)) return null;
        var frames = L(r, "Frames");
        var bytes = L(r, "FramePositiveBytes");
        var ops = L(r, "FrameAllocationOperations");
        var rootBytes = L(r, "RootJobPositiveBytes");
        var rootOps = L(r, "RootJobAllocationOperations");
        var residualBytes = L(r, "OutsideRootJobsPositiveBytes");
        var residualOps = L(r, "OutsideRootJobsAllocationOperations");
        var dropped = L(r, "DroppedOrUnpairedScopes");
        return new(frames, bytes, ops, rootBytes, rootOps, residualBytes,
            residualOps, dropped, frames > 0 && dropped == 0 &&
            rootBytes <= bytes && rootOps <= ops &&
            rootBytes + residualBytes == bytes && rootOps + residualOps == ops);
    }

    private static object? BuildPhase3SchedulerAllocation(
        string captureRoot, IReadOnlyList<SchedulerJobMetric> jobs)
    {
        var frame = ReadPhase3SchedulerFrame(captureRoot);
        var measured = jobs.Where(x => x.ExclusiveOps.HasValue).ToArray();
        if (frame is null && measured.Length == 0) return null;
        return new
        {
            stage = "PHASE3_SCHEDULER_ALLOCATION_EXPERIMENTAL",
            positiveLuaJitGrowthNotRetainedMemory = true,
            nestedAllocationTotalsNotAdditive = true,
            profilerBridgeOverheadNotSubtracted = true,
            automaticDormancyDecisionAuthorized = false,
            framework = frame is null ? null : new
            {
                frame.Frames, frame.FrameBytes, frame.FrameOps,
                frame.RootBytes, frame.RootOps, frame.ResidualBytes,
                frame.ResidualOps, frame.Dropped, frame.Reconciled,
                interpretation = "SCHEDULER_ENVELOPE_RESIDUAL_UNASSIGNED_NOT_PROVEN_FRAMEWORK_SOURCE_COST"
            },
            measuredJobCount = measured.Length,
            jobs = measured.OrderByDescending(x => x.ExclusiveOps).Select(x => new
            {
                x.Owner, x.JobType, x.Job, x.Calls,
                x.InclusiveBytes, x.InclusiveOps, x.ExclusiveBytes, x.ExclusiveOps,
                exclusiveOpsPerCall = x.Calls > 0
                    ? (double)x.ExclusiveOps!.Value / x.Calls : 0.0
            }).ToArray()
        };
    }

    private static void AppendPhase3SchedulerAllocation(
        StringBuilder sb, string captureRoot, IReadOnlyList<SchedulerJobMetric> jobs)
    {
        var frame = ReadPhase3SchedulerFrame(captureRoot);
        var measured = jobs.Where(x => x.ExclusiveOps.HasValue)
            .OrderByDescending(x => x.ExclusiveOps).ToArray();
        if (frame is null && measured.Length == 0) return;
        sb.Append("<div class='section'><h2>Phase 3 Scheduler allocation evidence</h2>")
          .Append("<p class='note'>Positive LuaJIT allocation requests inside ")
          .Append("Scheduler.Update; nested with existing G-GC totals. ")
          .Append("Outside-job remainder includes scheduler scan, dispatch and profiler bridge. ")
          .Append("No source ownership, retained-heap or dormancy eligibility is inferred.</p>");
        if (frame is not null)
            sb.Append("<p>Frames ").Append(N(frame.Frames))
              .Append("; ops in frame ").Append(N(frame.FrameOps))
              .Append("; root job ops ").Append(N(frame.RootOps))
              .Append("; outside root jobs ").Append(N(frame.ResidualOps))
              .Append("; unmatched scopes ").Append(N(frame.Dropped))
              .Append("; ").Append(frame.Reconciled ? "RECONCILED" : "INCOMPLETE")
              .Append(".</p>");
        if (measured.Length > 0)
        {
            sb.Append("<table><thead><tr><th>Owner</th><th>Job</th><th>Calls</th>")
              .Append("<th>Own ops</th><th>Ops/call</th><th>Own bytes</th>")
              .Append("</tr></thead><tbody>");
            foreach (var j in measured.Take(60))
                sb.Append("<tr><td>").Append(H(j.Owner)).Append("</td><td>")
                  .Append(H(j.Job)).Append("</td><td>").Append(N(j.Calls))
                  .Append("</td><td>").Append(N(j.ExclusiveOps!.Value))
                  .Append("</td><td>").Append(F(j.Calls > 0
                      ? (double)j.ExclusiveOps.Value / j.Calls : 0.0, 2))
                  .Append("</td><td>").Append(N(j.ExclusiveBytes ?? 0))
                  .Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</div>");
    }
}
