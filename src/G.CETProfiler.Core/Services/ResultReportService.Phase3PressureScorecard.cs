using System.Globalization;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Single source of truth for the prominent HTML cards and the additive
    // CET_Summary.json phase3PressureScorecard object. All inputs were already
    // captured; this aggregation installs no native/Lua probes or collectors.
    private sealed record PressureProducer(
        string mod, double positiveAllocatedMiB, long allocationRequests);

    private sealed record PressureNativeBoundary(
        string boundary, string methodGroup, long calls,
        double ownerZeroPositiveMiB, long ownerZeroPositiveRequests);

    private sealed record PressureAllocator(
        double positiveAllocatedMiB, double? positiveAllocatedMiBPerSecond,
        long allocationRequests, double? allocationRequestsPerSecond,
        long observedGcObjectBirths, double? observedGcObjectBirthsPerSecond,
        double unscopedAndOverflowMiB, double unscopedAndOverflowSharePct,
        IReadOnlyList<PressureProducer> topScopedProducers);

    private sealed record PressureCollector(
        long completedCycles, double measuredCollectorMs,
        double? measuredCollectorMsPerSecond, double incrementalMs,
        long incrementalCalls, double maximumObservedFrameBucketMs,
        double maximumRecordedIncrementalStepMs,
        double? firstCompletedCycleFrameEndSeconds);

    private sealed record PressureHeap(
        double firstMiB, double lastMiB, double peakMiB,
        double netChangeMiB, int observedShrinkEvents,
        double largestObservedShrinkMiB);

    private sealed record PressureFrameDelivery(
        double p99Ms, double p999Ms, double worstFrameMs,
        int framesOver50Ms, int framesOver100Ms,
        int? hitchEpisodes, double? hitchEpisodesPerMinute,
        double? hitchExcessMs);

    private sealed record Phase3PressureScorecard(
        string measurement, double captureSeconds,
        PressureAllocator? allocation, PressureCollector? collector,
        PressureHeap? heap, PressureFrameDelivery? frameDelivery,
        IReadOnlyList<PressureNativeBoundary> nativePreDispatch,
        string allocationSemantics, string collectionSemantics,
        string nativeSemantics, string absentTelemetryPolicy);

    private static Phase3PressureScorecard BuildPhase3PressureScorecard(
        ResultAnalysis a, string captureRoot)
    {
        var seconds = a.CaptureSeconds;
        PressureAllocator? allocator = null;
        if (a.GcTracer is { Mods.Count: > 0 } gc)
        {
            var totalMiB = gc.Mods.Sum(x => x.AllocatedMiB);
            var requests = gc.Mods.Sum(x => x.AllocationOperations);
            var births = gc.Mods.Sum(x => x.NewGCObjects);
            var unknown = gc.Mods.Where(x =>
                x.Mod is "<unattributed>" or "<registration-overflow>")
                .Sum(x => x.AllocatedMiB);
            allocator = new PressureAllocator(
                Round(totalMiB, 6),
                seconds > 0 ? Round(totalMiB / seconds, 6) : (double?)null,
                requests, seconds > 0 ? Round(requests / seconds, 3) : (double?)null,
                births, seconds > 0 ? Round((double)births / seconds, 3) : (double?)null,
                Round(unknown, 6), totalMiB > 0
                    ? Round(unknown * 100 / totalMiB, 3) : 0,
                gc.Mods.Where(x => x.Mod is not ("<unattributed>" or "<registration-overflow>"))
                    .OrderByDescending(x => x.AllocatedMiB).Take(5)
                    .Select(x => new PressureProducer(x.Mod,
                        Round(x.AllocatedMiB, 6), x.AllocationOperations))
                    .ToArray());
        }

        // CompletedCycles=0 is meaningful ONLY when at least one actual
        // collector frame was recorded. An absent CSV is NOT zero pressure.
        PressureCollector? collector = null;
        if (a.CollectorGc is { Frames.Count: > 0 } c)
        {
            var first = c.Frames.FirstOrDefault(x => x.CompletedCycles > 0);
            collector = new PressureCollector(
                c.CompletedCycles, Round(c.TotalMs, 6),
                seconds > 0 ? Round(c.TotalMs / seconds, 6) : (double?)null,
                Round(c.StepMs, 6), c.StepCalls,
                Round(c.MaxObservedFrameMs, 6),
                Round(c.MaxStepToDateMs, 6),
                first is null ? null : Round(first.EndMs / 1000.0, 6));
        }

        PressureHeap? heap = null;
        if (a.LuaHeap is { SampleCount: > 0 } h)
            heap = new PressureHeap(
                Round(h.FirstMiB, 6), Round(h.LastMiB, 6),
                Round(h.MaxMiB, 6), Round(h.LastMiB - h.FirstMiB, 6),
                h.DropCount, Round(h.MaxDropMiB, 6));

        PressureFrameDelivery? delivery = null;
        if (a.FrameTime is { FrameCount: > 0 } ft)
        {
            var hp = ft.HitchPressure;
            delivery = new PressureFrameDelivery(
                Round(ft.P99FrameMs, 6),
                Round(Percentile(ft.Frames.Select(x => x.FrameMs)
                    .Where(x => x > 0 && double.IsFinite(x)), 0.999), 6),
                Round(ft.MaxFrameMs, 6), ft.FramesOver50Ms, ft.FramesOver100Ms,
                hp?.EpisodeCount,
                hp is null ? null : Round(hp.EpisodesPerMinute, 6),
                hp is null ? null : Round(hp.HitchTollMs, 6));
        }

        // These pre-Lua conversion windows OVERLAP owner-zero G-GC totals.
        // Do not sum their bytes, even across methods or with sampled stages.
        var boundaries = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Native_PreDispatchPressure.csv"))
            .OrderByDescending(x => L(x, "OwnerZeroPositiveBytes"))
            .Take(5)
            .Select(x => new PressureNativeBoundary(
                S(x, "Boundary"), S(x, "MethodGroup"),
                L(x, "Calls"),
                Round(L(x, "OwnerZeroPositiveBytes") / 1048576.0, 6),
                L(x, "OwnerZeroPositiveOps")))
            .ToArray();

        return new Phase3PressureScorecard(
            "OBSERVED_POSITIVE_LUAJIT_ALLOCATION_AND_GC_PRESSURE",
            Round(seconds, 6), allocator, collector, heap, delivery, boundaries,
            "Positive allocator-request growth, NOT total garbage or retained heap; object births are a distinct counter.",
            "Completed cycles count completed cycles within capture, not collector speed. First-cycle time is the end of its recorded frame bucket, not exact GC completion timestamp.",
            "Native pre-dispatch rows overlap unscoped allocation totals; they are NOT additive or per-mod ownership.",
            "Missing source metrics are null/empty, never zero or inferred.");
    }

    private static void AppendPhase3PressureScorecard(
        StringBuilder sb, Phase3PressureScorecard p)
    {
        static string Measured(double? v, string unit, int digits = 2) =>
            v is null ? "NOT MEASURED" : v.Value.ToString("F" + digits, CultureInfo.InvariantCulture) + unit;
        static string MeasuredCount(long? v) =>
            v is null ? "NOT MEASURED" : N(v.Value);

        sb.Append("<div class=\"section\"><h2>Phase 3 — GC &amp; allocation pressure</h2>");
        sb.Append("<p class=\"note\">One non-additive scorecard for this capture. Positive ")
          .Append("LuaJIT allocations, GC object births, completed GC cycles, and retained ")
          .Append("heap are different measurements. Missing telemetry means NOT MEASURED, never zero.</p>");

        sb.Append("<h3>Allocator throughput</h3><div class=\"grid\">");
        MetricCard(sb, "Positive allocated",
            Measured(p.allocation?.positiveAllocatedMiB, " MiB"),
            "Whole capture; not garbage or heap retained");
        MetricCard(sb, "Allocation MiB/s",
            Measured(p.allocation?.positiveAllocatedMiBPerSecond, " MiB/s", 3),
            "Positive LuaJIT allocator growth");
        MetricCard(sb, "Allocator requests/s",
            Measured(p.allocation?.allocationRequestsPerSecond, "/s", 0),
            "Requests; not object births");
        MetricCard(sb, "GC object births/s",
            Measured(p.allocation?.observedGcObjectBirthsPerSecond, "/s", 0),
            "New GC-managed objects");
        sb.Append("</div>");

        sb.Append("<h3>Collector work and completion</h3><div class=\"grid\">");
        MetricCard(sb, "Completed GC cycles",
            MeasuredCount(p.collector?.completedCycles),
            "Zero is measured only when collector frames exist");
        MetricCard(sb, "Collector ms/s",
            Measured(p.collector?.measuredCollectorMsPerSecond, " ms/s", 3),
            "Incremental and full GC execution");
        MetricCard(sb, "Largest GC frame bucket",
            Measured(p.collector?.maximumObservedFrameBucketMs, " ms", 3),
            "Not exact single-step attribution");
        MetricCard(sb, "First completed cycle",
            p.collector is null ? "NOT MEASURED"
              : p.collector.firstCompletedCycleFrameEndSeconds is null ? "NOT OBSERVED"
              : Measured(p.collector.firstCompletedCycleFrameEndSeconds, " s", 3),
            "Recorded frame end; not exact step timestamp");
        sb.Append("</div>");

        sb.Append("<h3>Lua heap indicators</h3><div class=\"grid\">");
        MetricCard(sb, "Heap first → last",
            p.heap is null ? "NOT MEASURED"
              : Measured(p.heap.firstMiB, "", 1) + " → " +
                Measured(p.heap.lastMiB, " MiB", 1),
            "Passive snapshots; not collector duration");
        MetricCard(sb, "Peak observed heap",
            Measured(p.heap?.peakMiB, " MiB", 1),
            "Intermittent sampling");
        MetricCard(sb, "Heap net change",
            Measured(p.heap?.netChangeMiB, " MiB", 1),
            "Last minus first sampled heap");
        MetricCard(sb, "Observed heap shrinks",
            MeasuredCount(p.heap?.observedShrinkEvents),
            "Thresholded sampled events; not total reclaimed bytes");
        sb.Append("</div>");

        sb.Append("<h3>Visible frame delivery</h3><div class=\"grid\">");
        MetricCard(sb, "P99 / P99.9",
            p.frameDelivery is null ? "NOT MEASURED"
              : Measured(p.frameDelivery.p99Ms, "", 1) + " / " +
                Measured(p.frameDelivery.p999Ms, " ms", 1),
            "CapFrameX observed frames");
        MetricCard(sb, "Worst frame",
            Measured(p.frameDelivery?.worstFrameMs, " ms", 2),
            "Not just average FPS");
        MetricCard(sb, "Frames ≥50 / ≥100 ms",
            p.frameDelivery is null ? "NOT MEASURED" :
                N(p.frameDelivery.framesOver50Ms) + " / " +
                N(p.frameDelivery.framesOver100Ms),
            "Whole capture");
        MetricCard(sb, "Hitch episodes / minute",
            Measured(p.frameDelivery?.hitchEpisodesPerMinute, "/min", 2),
            "Adaptive grouped episodes; consistent threshold required");
        sb.Append("</div>");

        sb.Append("<h3>Attribution and native boundary</h3>");
        if (p.allocation is null)
            sb.Append("<p class=\"muted\">Allocation owner coverage: NOT MEASURED.</p>");
        else
        {
            sb.Append("<p>Unscoped and registration-overflow: ")
              .Append(Measured(p.allocation.unscopedAndOverflowMiB, " MiB", 2))
              .Append(" (").Append(Measured(p.allocation.unscopedAndOverflowSharePct, "%", 1))
              .Append("), not proven mod-owned garbage.</p>");
            if (p.allocation.topScopedProducers.Count > 0)
            {
                sb.Append("<table><thead><tr><th>Measured allocation scope</th>")
                  .Append("<th>Positive MiB</th><th>Requests</th></tr></thead><tbody>");
                foreach (var x in p.allocation.topScopedProducers)
                    sb.Append("<tr><td>").Append(H(x.mod)).Append("</td><td>")
                      .Append(Measured(x.positiveAllocatedMiB, "", 3))
                      .Append("</td><td>").Append(N(x.allocationRequests))
                      .Append("</td></tr>");
                sb.Append("</tbody></table>");
            }
        }
        if (p.nativePreDispatch.Count > 0)
        {
            sb.Append("<h3>Observed native pre-dispatch windows (NON-ADDITIVE)</h3>")
              .Append("<table><thead><tr><th>Boundary</th><th>Method</th>")
              .Append("<th>Calls</th><th>Positive MiB (overlap)</th>")
              .Append("<th>Requests (overlap)</th></tr></thead><tbody>");
            foreach (var n in p.nativePreDispatch)
                sb.Append("<tr><td>").Append(H(n.boundary)).Append("</td><td>")
                  .Append(H(n.methodGroup)).Append("</td><td>")
                  .Append(N(n.calls)).Append("</td><td>")
                  .Append(Measured(n.ownerZeroPositiveMiB, "", 3))
                  .Append("</td><td>").Append(N(n.ownerZeroPositiveRequests))
                  .Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        else
            sb.Append("<p class=\"muted\">Native pre-dispatch telemetry: NOT MEASURED.</p>");

        sb.Append("<p class=\"note\">Do not add native windows, scheduled jobs, ")
          .Append("sampled C functions or collector work to the allocator total. ")
          .Append("The collector may perform work without completing a GC cycle; ")
          .Append("zero completed cycles is not a faster completed collection. ")
          .Append("See detailed sections below for phase and source evidence.</p></div>");
    }
}
