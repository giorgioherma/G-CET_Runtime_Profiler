using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record GcCollectorFrame(
        long Frame, double StartMs, double EndMs,
        long IncrementalCalls, long CompletedCycles, long FullCalls,
        double IncrementalMs, double FullMs, double TotalMs,
        bool CallbackSpikeOverlap);

    private sealed class GcCollectorTelemetry
    {
        public List<GcCollectorFrame> Frames { get; init; } = [];
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
        FrameTimeAnalysis? frameTime)
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
        int? hitchOverlap = null;
        if (frameTime?.Correlated == true && frameTime.HitchPressure is not null)
        {
            hitchOverlap = frames.Count(f => frameTime.HitchPressure.Episodes.Any(
                h => h.StartMs <= f.EndMs && h.EndMs >= f.StartMs));
        }
        return new GcCollectorTelemetry {
            Frames = frames,
            MaxStepToDateMs = rows.Count > 0 ? rows.Max(r => D(r, "MaxStepToDateMs")) : 0,
            MaxFullToDateMs = rows.Count > 0 ? rows.Max(r => D(r, "MaxFullToDateMs")) : 0,
            DroppedFramesAtDump = rows.Count > 0 ? rows.Max(r => L(r, "DroppedFramesAtDump")) : 0,
            AlignedHitchEpisodeOverlap = hitchOverlap
        };
    }

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
        sb.Append("</tbody></table></div>");
    }
}
