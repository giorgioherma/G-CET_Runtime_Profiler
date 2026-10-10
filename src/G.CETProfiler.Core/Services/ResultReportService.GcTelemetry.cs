using System.Globalization;
using System.Text;
using System.Linq;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record ExplicitGcEvent(
        double StartMs, double EndMs, double DurationMs, string Action,
        string SourceFile, int SourceLine, bool NearRecordedSpike);

    private sealed class ExplicitGcTelemetry
    {
        public List<ExplicitGcEvent> Events { get; init; } = [];
        public double TotalDurationMs => Events.Sum(x => x.DurationMs);
        public double MaximumDurationMs => Events.Max(x => x.DurationMs);
        public int SpikeOverlapCount => Events.Count(x => x.NearRecordedSpike);
    }

    private static ExplicitGcTelemetry? AnalyzeExplicitGc(
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<Dictionary<string, string>> spikes)
    {
        if (rows.Count == 0) return null;
        var ranges = spikes.Select(x => (
            Start: D(x, "CaptureStartMs"), End: D(x, "CaptureEndMs")))
            .Where(x => double.IsFinite(x.Start) && double.IsFinite(x.End))
            .ToArray();
        var events = new List<ExplicitGcEvent>();
        foreach (var row in rows)
        {
            var start = D(row, "CaptureStartMs");
            var end = D(row, "CaptureEndMs");
            var duration = D(row, "DurationMs");
            var action = S(row, "Action");
            if (!double.IsFinite(start) || !double.IsFinite(end) ||
                !double.IsFinite(duration) || start < 0 || end < start ||
                duration < 0 || (action != "collect" && action != "step"))
                continue;
            var overlap = ranges.Any(x => x.Start <= end && x.End >= start);
            var sourceLine = int.TryParse(S(row, "SourceLine"), out var line)
                ? line : 0;
            events.Add(new ExplicitGcEvent(
                start, end, duration, action, S(row, "SourceFile"),
                sourceLine, overlap));
        }
        return events.Count == 0 ? null : new ExplicitGcTelemetry { Events = events };
    }

    private static void AppendExplicitGc(StringBuilder sb, ExplicitGcTelemetry gc)
    {
        sb.Append("<div class=\"section\"><h2>Explicit Lua garbage collection timing</h2>");
        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Measured GC operations", N(gc.Events.Count),
            "Full " + N(gc.Events.Count(x => x.Action == "collect")) +
            " / step " + N(gc.Events.Count(x => x.Action == "step")));
        MetricCard(sb, "Explicit GC wall time", F(gc.TotalDurationMs, 3) + " ms",
            "Nested inside CET callback measurements; never add twice");
        MetricCard(sb, "Longest explicit call", F(gc.MaximumDurationMs, 3) + " ms",
            "Exact native call duration");
        MetricCard(sb, "Overlaps recorded spikes", N(gc.SpikeOverlapCount),
            "Exact capture-time interval overlap");
        sb.Append("</div><div class=\"note\">");
        sb.Append("Measured events are explicit collectgarbage('collect'/'step') calls from CET sandboxed Lua mods. ");
        sb.Append("CMB calls are included if invoked through CET's shared collectgarbage global. ");
        sb.Append("Automatic LuaJIT allocator-driven GC is not independently timed. ");
        sb.Append("A GC operation overlapping a slow callback does not prove it caused an external frametime hitch.");
        sb.Append("</div><table><thead><tr><th>Capture time</th><th>Action</th><th>Duration</th><th>Caller</th><th>Callback spike</th></tr></thead><tbody>");
        foreach (var e in gc.Events.OrderByDescending(x => x.DurationMs).Take(30))
        {
            sb.Append("<tr><td>").Append(F(e.StartMs / 1000, 3)).Append(" s</td><td>")
                .Append(H(e.Action)).Append("</td><td>").Append(F(e.DurationMs, 3))
                .Append(" ms</td><td>").Append(H(e.SourceFile))
                .Append(":").Append(e.SourceLine)
                .Append("</td><td>").Append(e.NearRecordedSpike ? "Yes" : "No")
                .Append("</td></tr>");
        }
        sb.Append("</tbody></table></div>");
    }

    private sealed record LuaHeapSample(
        double CaptureMs, double HeapMiB, bool IsDrop, double DropMiB,
        bool NearRecordedSpike);

    private sealed class LuaHeapTelemetry
    {
        public List<LuaHeapSample> Samples { get; init; } = [];
        public int SampleCount => Samples.Count;
        public int DropCount => Samples.Count(x => x.IsDrop);
        public int DropsNearSpikes => Samples.Count(x => x.IsDrop && x.NearRecordedSpike);
        public double FirstMiB => Samples.Count == 0 ? 0 : Samples[0].HeapMiB;
        public double LastMiB => Samples.Count == 0 ? 0 : Samples[^1].HeapMiB;
        public double MinMiB => Samples.Min(x => x.HeapMiB);
        public double MaxMiB => Samples.Max(x => x.HeapMiB);
        public double MaxDropMiB => Samples.Max(x => x.DropMiB);
    }

    // Native CSV markers supply the same capture-relative clock used by the
    // runtime spikes. Neither the sampler nor this parser calls GC collect/step.
    private static LuaHeapTelemetry? AnalyzeLuaHeap(
        IReadOnlyList<Dictionary<string, string>> markers,
        IReadOnlyList<Dictionary<string, string>> spikes)
    {
        var spikesMs = spikes
            .Select(x => D(x, "CaptureStartMs"))
            .Where(x => double.IsFinite(x) && x >= 0)
            .ToArray();
        var samples = new List<LuaHeapSample>();
        foreach (var row in markers)
        {
            var label = S(row, "Label");
            if (!label.StartsWith("GC_HEAP_V1_KIB_", StringComparison.Ordinal))
                continue;
            var fields = label.Split('_');
            if ((fields.Length != 6 && fields.Length != 7) ||
                fields[0] != "GC" || fields[1] != "HEAP" ||
                fields[2] != "V1" || fields[3] != "KIB" ||
                !long.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out var heapKiB) ||
                heapKiB < 0)
                continue;
            var isDrop = fields[5] == "DROP";
            if ((!isDrop && (fields[5] != "BASE" || fields.Length != 6)) ||
                (isDrop && (fields.Length != 7 ||
                    !long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out _))))
                continue;
            long dropKiB = 0;
            if (isDrop && (!long.TryParse(fields[6], NumberStyles.None, CultureInfo.InvariantCulture, out dropKiB) ||
                dropKiB < 0))
                continue;
            var atMs = D(row, "CaptureMs");
            if (!double.IsFinite(atMs) || atMs < 0)
                continue;
            var near = isDrop && spikesMs.Any(x => Math.Abs(x - atMs) <= 1000.0);
            samples.Add(new LuaHeapSample(
                atMs, heapKiB / 1024.0, isDrop, dropKiB / 1024.0, near));
        }
        if (samples.Count == 0)
            return null;

        samples.Sort((x, y) => x.CaptureMs.CompareTo(y.CaptureMs));
        return new LuaHeapTelemetry { Samples = samples };
    }

    private static void AppendLuaHeap(StringBuilder sb, LuaHeapTelemetry gc)
    {
        sb.Append("<div class=\"section\"><h2>Lua heap &amp; GC indicators</h2>");
        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Heap: start → last",
            F(gc.FirstMiB, 1) + " → " + F(gc.LastMiB, 1) + " MiB",
            "Min " + F(gc.MinMiB, 1) + " / max " + F(gc.MaxMiB, 1) + " MiB");
        MetricCard(sb, "Observed heap shrinks", N(gc.DropCount),
            "≥8 MiB in ~0.5 s · maximum " + F(gc.MaxDropMiB, 1) + " MiB");
        MetricCard(sb, "Drop/spike proximity", N(gc.DropsNearSpikes),
            "Within ±1 s of a recorded CET callback spike");
        MetricCard(sb, "Recorded checkpoints", N(gc.SampleCount),
            "Bounded native-marker budget: 480");
        sb.Append("</div><div class=\"note\"><b>Passive observation only.</b> ");
        sb.Append("The probe reads collectgarbage('count') but never invokes garbage collection. ");
        sb.Append("An observed Lua heap shrink is consistent with reclaimed memory, ");
        sb.Append("not proof of GC timing, a GC pause, or causation of a nearby hitch. ");
        sb.Append("A 0.5-second probe can miss shorter events. ");
        sb.Append("This is CET Lua heap, not system RAM or GPU VRAM.");
        if (gc.SampleCount >= 480)
            sb.Append(" The per-capture marker allowance was exhausted; later events may be missing.");
        sb.Append("</div></div>");
    }
}
