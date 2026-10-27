using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record GcCollectorFrame(
        long Frame, double StartMs, double EndMs,
        long IncrementalCalls, long CompletedCycles, long FullCalls,
        double IncrementalMs, double FullMs, double TotalMs,
        bool CallbackSpikeOverlap);

    // Bucket boundaries are precise, but a bucket does not locate individual
    // collector steps within the frame. Coincidence never supplies a GC-free
    // callback duration, a causal owner, or permission for a rewrite.
    private sealed record GcSpikeCoincidence(
        long RegistrationId, string Owner, string Kind, string Target,
        double CaptureStartMs, double CaptureEndMs, double ExclusiveMs,
        int OverlappingFrameBuckets, double LargestOverlappingBucketMs,
        bool PotentiallyConfounded);

    private sealed record GcPhaseRow(string Phase, long Operations, double WallMs);
    private sealed record GcSlowRow(double CaptureStartMs, double DurationMs,
        string PhaseBefore, string PhaseAfter, long RegistrationId,
        string Callback, double HeapBeforeMiB, double HeapAfterMiB,
        double ThresholdMiB, double DebtKiB, long Pause, long StepMul);
    private sealed record GcAllocationRow(string Callback, double MiB, double MiBPerSecond);

    private sealed class GcCollectorTelemetry
    {
        public List<GcPhaseRow> Phases { get; init; } = [];
        public List<GcSlowRow> SlowSteps { get; init; } = [];
        public List<GcAllocationRow> AllocationOwners { get; init; } = [];
        public long SlowDropped { get; init; }

        public List<GcCollectorFrame> Frames { get; init; } = [];
        public List<GcSpikeCoincidence> SpikeCoincidences { get; init; } = [];
        public int PotentiallyConfoundedSpikes => SpikeCoincidences.Count(x => x.PotentiallyConfounded);
        public double TotalMs => Frames.Sum(x => x.TotalMs);
        public double StepMs => Frames.Sum(x => x.IncrementalMs);
        public double FullMs => Frames.Sum(x => x.FullMs);
        public long StepCalls => Frames.Sum(x => x.IncrementalCalls);
        public long FullCalls => Frames.Sum(x => x.FullCalls);
        public long CompletedCycles => Frames.Sum(x => x.CompletedCycles);
        public double MaxObservedFrameMs => Frames.Count > 0 ? Frames.Max(x => x.TotalMs) : 0;
        public long SpikeOverlapFrames => Frames.LongCount(x => x.CallbackSpikeOverlap);
        public double MaxStepToDateMs { get; init; }
        public double MaxFullToDateMs { get; init; }
        public long DroppedFramesAtDump { get; init; }
        public int? AlignedHitchEpisodeOverlap { get; init; }
    }

    private static GcCollectorTelemetry AnalyzeGcCollector(
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<Dictionary<string, string>> spikes,
        FrameTimeAnalysis? frameTime,
        IReadOnlyList<Dictionary<string, string>> phaseRows,
        IReadOnlyList<Dictionary<string, string>> slowRows,
        IReadOnlyList<Dictionary<string, string>> allocationRows,
        IReadOnlyList<Dictionary<string, string>> detailRows)
    {
        var intervals = spikes.Select(x => (
                Start: D(x, "CaptureStartMs"),
                End: D(x, "CaptureEndMs")))
            .Where(x => double.IsFinite(x.Start) && double.IsFinite(x.End))
            .ToArray();
        var frames = new List<GcCollectorFrame>();
        foreach (var r in rows)
        {
            var start = D(r, "CaptureStartMs");
            var end = D(r, "CaptureEndMs");
            var stepMs = D(r, "IncrementalMs");
            var fullMs = D(r, "FullMs");
            var stepCount = L(r, "IncrementalCalls");
            var fullCount = L(r, "FullCalls");
            if (!double.IsFinite(start) || !double.IsFinite(end) ||
                !double.IsFinite(stepMs) || !double.IsFinite(fullMs) ||
                start < 0 || end < start ||
                stepMs < 0 || fullMs < 0 ||
                (stepCount <= 0 && fullCount <= 0))
                continue;
            var overlap = intervals.Any(x => x.Start <= end && x.End >= start);
            frames.Add(new GcCollectorFrame(
                L(r, "Frame"), start, end, stepCount,
                L(r, "CompletedCycles"), fullCount,
                stepMs, fullMs, stepMs + fullMs, overlap));
        }
        // Use binary search over sorted frame buckets rather than a quadratic
        // spike x frame scan (large CET captures can contain both in volume).
        frames.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        var coincidences = new List<GcSpikeCoincidence>();
        foreach (var spike in spikes)
        {
            var start = D(spike, "CaptureStartMs");
            var end = D(spike, "CaptureEndMs");
            var exclusive = D(spike, "ExclusiveMs", "DurationMs");
            if (!double.IsFinite(start) || !double.IsFinite(end) ||
                !double.IsFinite(exclusive) || start < 0 || end < start || exclusive <= 0)
                continue;
            var lo = 0;
            var hi = frames.Count;
            while (lo < hi)
            {
                var mid = lo + (hi - lo) / 2;
                if (frames[mid].EndMs < start) lo = mid + 1;
                else hi = mid;
            }
            var overlaps = 0;
            var largestBucket = 0.0;
            for (var i = lo; i < frames.Count && frames[i].StartMs <= end; i++)
            {
                if (frames[i].EndMs < start) continue;
                overlaps++;
                largestBucket = Math.Max(largestBucket, frames[i].TotalMs);
            }
            if (overlaps == 0) continue;
            // This is explicitly a review flag, NOT inferred ownership.
            // We cannot subtract frame-bucket GC time from an individual spike.
            var review = exclusive >= 20.0 &&
                largestBucket >= Math.Max(15.0, exclusive * 0.70);
            coincidences.Add(new GcSpikeCoincidence(
                L(spike, "RegistrationId"), S(spike, "Mod", "Owner"),
                S(spike, "Kind"), S(spike, "Target"), start, end, exclusive,
                overlaps, largestBucket, review));
        }
        int? hitchOverlap = null;
        if (frameTime?.Correlated == true && frameTime.HitchPressure is not null)
        {
            hitchOverlap = frames.Count(f => frameTime.HitchPressure.Episodes.Any(
                h => h.StartMs <= f.EndMs && h.EndMs >= f.StartMs));
        }
        var callbackNames = detailRows
            .Where(r => L(r, "RegistrationId") > 0)
            .GroupBy(r => L(r, "RegistrationId"))
            .ToDictionary(g => g.Key, g => S(g.First(), "Mod") + " · " +
                S(g.First(), "Kind") + " " + S(g.First(), "Target"));
        string Callback(long id, string fallback) =>
            callbackNames.TryGetValue(id, out var name) ? name : fallback;
        return new GcCollectorTelemetry {
            Frames = frames,
            Phases = phaseRows.Select(r => new GcPhaseRow(S(r, "Phase"),
                L(r, "Operations"), D(r, "WallMs")))
                .Where(x => x.WallMs >= 0 && double.IsFinite(x.WallMs))
                .OrderByDescending(x => x.WallMs).ToList(),
            SlowSteps = slowRows.Select(r => new GcSlowRow(
                D(r, "CaptureStartMs"), D(r, "DurationMs"),
                S(r, "PhaseBefore"), S(r, "PhaseAfter"),
                L(r, "RegistrationId"), Callback(L(r, "RegistrationId"), "unattributed"),
                D(r, "HeapBeforeMiB"), D(r, "HeapAfterMiB"),
                D(r, "ThresholdMiB"), D(r, "DebtKiB"),
                L(r, "Pause"), L(r, "StepMul")))
                .Where(x => x.DurationMs >= 2 && double.IsFinite(x.DurationMs))
                .OrderByDescending(x => x.DurationMs).Take(2048).ToList(),
            AllocationOwners = allocationRows.Select(r => new GcAllocationRow(
                S(r, "Mod") + " · " + S(r, "Kind") + " " + S(r, "Target"),
                D(r, "GrossAllocatedMiB"), D(r, "GrossAllocatedMiBPerSecond")))
                .Where(x => double.IsFinite(x.MiB) && x.MiB > 0)
                .OrderByDescending(x => x.MiB).Take(50).ToList(),
            SlowDropped = slowRows.Count > 0 ? slowRows.Max(r => L(r, "DroppedEventsAtDump")) : 0,
            SpikeCoincidences = coincidences,
            MaxStepToDateMs = rows.Count > 0 ? rows.Max(r => D(r, "MaxStepToDateMs")) : 0,
            MaxFullToDateMs = rows.Count > 0 ? rows.Max(r => D(r, "MaxFullToDateMs")) : 0,
            DroppedFramesAtDump = rows.Count > 0 ? rows.Max(r => L(r, "DroppedFramesAtDump")) : 0,
            AlignedHitchEpisodeOverlap = hitchOverlap
        };
    }

    // Only marks temporal GC exposure. It does not determine allocation owner.
    private static GcSpikeCoincidence? GcCoincidenceFor(
        GcCollectorTelemetry? gc, long registrationId, double captureStartMs) =>
        gc?.SpikeCoincidences.FirstOrDefault(x =>
            x.RegistrationId == registrationId &&
            Math.Abs(x.CaptureStartMs - captureStartMs) <= 0.05);

    private static void AppendGcCollector(StringBuilder sb, GcCollectorTelemetry gc)
    {
        sb.Append("<div class=\"section\"><h2>Internal LuaJIT GC execution</h2><div class=\"grid\">");
        MetricCard(sb, "Measured collector time", F(gc.TotalMs, 3) + " ms",
            "Inside LuaJIT, across this capture; includes explicit and automatic work");
        MetricCard(sb, "Incremental GC steps", N(gc.StepCalls),
            F(gc.StepMs, 3) + " ms total; worst step " + F(gc.MaxStepToDateMs, 3) + " ms");
        MetricCard(sb, "Full collection calls", N(gc.FullCalls),
            F(gc.FullMs, 3) + " ms total; worst full " + F(gc.MaxFullToDateMs, 3) + " ms");
        MetricCard(sb, "Largest measured frame bucket", F(gc.MaxObservedFrameMs, 3) + " ms",
            "GC work occurring between two native frame boundaries");
        MetricCard(sb, "Overlaps callback spike intervals", N(gc.SpikeOverlapFrames),
            "Frame-level temporal overlap, not GC blame");
        MetricCard(sb, "Spikes requiring GC review", N(gc.PotentiallyConfoundedSpikes),
            "Conservative bucket-coincidence review flags; not confirmed GC attribution");
        if (gc.AlignedHitchEpisodeOverlap is int count)
            MetricCard(sb, "Overlaps aligned hitch episodes", N(count),
                "Temporal coincidence, not proof of causation");
        sb.Append("</div><div class=\"note\">");
        sb.Append("These are exact timings of LuaJIT internal incremental-step and full-collection functions, ");
        sb.Append("bucketed between observed rendered-frame boundaries. The probe does not force or tune GC. ");
        sb.Append("Incremental measurements include automatically triggered steps AND explicit API-driven steps: ");
        sb.Append("the two origins are not independently distinguished. ");
        sb.Append("Separate explicit collectgarbage timings are nested in these collector measurements, not additional work. ");
        sb.Append("Collector costs can also be nested inside CET callback and Scheduler time. ");
        sb.Append("Frame overlap identifies temporal coincidence only, not precise GC onset or causation.");
        sb.Append("<div class=\"note\"><b>GC exposure is not allocation ownership.</b> ");
        sb.Append("CET callbacks measure wall time and may include collector execution. ");
        sb.Append("GC frame buckets do not locate exact GC time inside each callback. ");
        sb.Append("Flagged spikes are not proven mod-owned Lua work; never subtract ");
        sb.Append("collector buckets from callbacks, Scheduler jobs or CET totals.</div>");
        if (gc.Phases.Count > 0)
        {
            sb.Append("<h3>Collector phase execution</h3><table><thead><tr>")
                .Append("<th>Phase</th><th>Operations</th><th>Measured ms</th></tr></thead><tbody>");
            foreach (var x in gc.Phases)
                sb.Append("<tr><td>").Append(H(x.Phase)).Append("</td><td>")
                  .Append(x.Operations).Append("</td><td>").Append(F(x.WallMs, 3)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        if (gc.SlowSteps.Count > 0)
        {
            sb.Append("<h3>Long LuaJIT collector operations (at least 2 ms)</h3>");
            sb.Append("<div class=\"note\">This is collector timing and heap state, not a mod ranking. Garbage collected now may have been allocated by unrelated mods long before the operation. The active callback ID remains available as raw execution context in the GC slow-step CSV, but it cannot identify the garbage producer or justify rewriting that callback. Wall time can include thread preemption. Recorded operations are bounded to 2048; dropped: ")
                .Append(gc.SlowDropped).Append(".</div>");
            sb.Append("<table><thead><tr><th>Capture</th><th>Duration</th><th>Phase</th>")
                .Append("<th>Heap before → after</th><th>Pause / stepmul</th></tr></thead><tbody>");
            foreach (var x in gc.SlowSteps.Take(30))
                sb.Append("<tr><td>").Append(F(x.CaptureStartMs / 1000, 3)).Append(" s</td><td>")
                  .Append(F(x.DurationMs, 3)).Append(" ms</td><td>").Append(H(x.PhaseBefore))
                  .Append(" → ").Append(H(x.PhaseAfter)).Append("</td><td>")
                  .Append(F(x.HeapBeforeMiB, 1)).Append(" → ")
                  .Append(F(x.HeapAfterMiB, 1)).Append(" MiB</td><td>").Append(x.Pause)
                  .Append(" / ").Append(x.StepMul).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        if (gc.AllocationOwners.Count > 0)
        {
            sb.Append("<h3>Observed positive LuaJIT allocations by callback</h3>")
                .Append("<div class=\"note\">These are positive allocation-site growth totals, exclusive of nested measured callbacks. Not native allocations or net heap growth; allocation ownership does not imply a particular GC pause.</div>")
                .Append("<table><thead><tr><th>Callback</th><th>Allocated MiB</th><th>MiB/s</th></tr></thead><tbody>");
            foreach (var x in gc.AllocationOwners.Take(25))
                sb.Append("<tr><td>").Append(H(x.Callback)).Append("</td><td>")
                  .Append(F(x.MiB, 2)).Append("</td><td>").Append(F(x.MiBPerSecond, 3))
                  .Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("</div><table><thead><tr><th>Capture</th><th>Frame</th><th>GC ms</th>");
        sb.Append("<th>Incremental calls</th><th>Full calls</th><th>Callback spike</th></tr></thead><tbody>");
        foreach (var f in gc.Frames.OrderByDescending(x => x.TotalMs).Take(25))
        {
            sb.Append("<tr><td>").Append(F(f.EndMs / 1000, 3)).Append(" s</td><td>")
                .Append(f.Frame).Append("</td><td>").Append(F(f.TotalMs, 3))
                .Append("</td><td>").Append(f.IncrementalCalls)
                .Append("</td><td>").Append(f.FullCalls)
                .Append("</td><td>").Append(f.CallbackSpikeOverlap ? "Yes" : "No")
                .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        if (gc.SpikeCoincidences.Count > 0)
        {
            sb.Append("<h3>Callback spikes near collector-heavy frame buckets</h3>");
            sb.Append("<div class=\"note\">These are time coincidences, not per-callback GC durations. ");
            sb.Append("A large collector bucket may overlap only a small portion of the callback. ");
            sb.Append("No GC-free cost can be obtained by subtracting the bucket from the callback.</div>");
            sb.Append("<table><thead><tr><th>Capture</th><th>Callback</th><th>Spike ms</th>");
            sb.Append("<th>Largest GC bucket ms</th><th>Review?</th></tr></thead><tbody>");
            foreach (var x in gc.SpikeCoincidences
                .OrderByDescending(x => x.PotentiallyConfounded)
                .ThenByDescending(x => x.ExclusiveMs).Take(30))
            {
                sb.Append("<tr><td>").Append(F(x.CaptureStartMs / 1000, 3))
                    .Append(" s</td><td>").Append(H(x.Owner)).Append(" · ")
                    .Append(H(x.Kind)).Append(" ").Append(H(x.Target))
                    .Append("</td><td>").Append(F(x.ExclusiveMs, 3))
                    .Append("</td><td>").Append(F(x.LargestOverlappingBucketMs, 3))
                    .Append("</td><td>").Append(x.PotentiallyConfounded ? "Review" : "Overlap only")
                    .Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("</div>");
    }
}
