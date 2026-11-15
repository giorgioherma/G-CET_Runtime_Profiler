using System.Globalization;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // GCO owns sealed F11 capture verification and GRSP QPC/Unix clock
    // alignment. CET owns this input contract and ALL existing frame/hitch/
    // scenario/G-GC interpretation. Never imitate a CapFrameX JSON export.
    private const string GcoPresentMonContractFile = "GCO-PresentMon-Frames.v1.json";
    private const string GcoPresentMonSchema = "gco.presentmon.chronology.v1";

    private static ParsedCapFrameX? TryReadGcoPresentMon(string captureRoot)
    {
        var file = Path.Combine(captureRoot, "PresentMon", GcoPresentMonContractFile);
        if (!File.Exists(file))
            return null; // Unchanged standalone CET/CapFrameX behavior.

        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("GCO PresentMon chronology cannot be a link.");
        var length = new FileInfo(file).Length;
        if (length <= 50 || length > 512L * 1024 * 1024)
            throw new InvalidDataException("GCO PresentMon chronology size is invalid.");

        using var stream = File.OpenRead(file);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            MaxDepth = 32,
            AllowTrailingCommas = false
        });
        var root = document.RootElement;

        static string Text(JsonElement item, string key)
        {
            var prop = item.GetProperty(key);
            if (prop.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("GCO PresentMon field has wrong type: " + key);
            return prop.GetString() ?? "";
        }
        static long Whole(JsonElement item, string key)
        {
            var prop = item.GetProperty(key);
            if (prop.ValueKind != JsonValueKind.Number ||
                !prop.TryGetInt64(out var result))
                throw new InvalidDataException("GCO PresentMon field is not a whole number: " + key);
            return result;
        }
        static double Numeric(JsonElement item, string key, bool positive)
        {
            var prop = item.GetProperty(key);
            if (prop.ValueKind != JsonValueKind.Number ||
                !prop.TryGetDouble(out var result) ||
                !double.IsFinite(result) ||
                positive && result <= 0)
                throw new InvalidDataException("Invalid GCO PresentMon metric: " + key);
            return result;
        }
        static double? OptionalNumeric(JsonElement item, string key)
        {
            var prop = item.GetProperty(key);
            if (prop.ValueKind == JsonValueKind.Null) return null;
            if (prop.ValueKind != JsonValueKind.Number ||
                !prop.TryGetDouble(out var value) ||
                !double.IsFinite(value) || value < 0)
                throw new InvalidDataException("Invalid optional GCO PresentMon metric: " + key);
            return value;
        }

        if (Text(root, "schemaVersion") != GcoPresentMonSchema ||
            Text(root, "producer") != "GCO_PRESENTMON_2_6" ||
            Text(root, "clock") != "WINDOWS_QPC_INTERPOLATED_FROM_GRSP_F11_UNIX_ANCHORS" ||
            Text(root, "correlation") != "CET_GRSP_F11_INTERSECTION_VERIFIED" ||
            Text(root, "intervalSemantics") !=
                "MILLISECONDS_BETWEEN_CONSECUTIVE_PRESENT_EVENTS_NOT_DISPLAY_LATENCY")
            throw new InvalidDataException("Unsupported or unverified GCO PresentMon frame contract.");
        var source = Text(root, "sourceFile");
        var hash = Text(root, "sourceSha256");
        if (source.Contains("..", StringComparison.Ordinal) ||
            source.Contains('\\') ||
            !source.StartsWith("PresentMon/", StringComparison.Ordinal) ||
            hash.Length != 64 || hash.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Unsafe GCO frame provenance.");
        var cetStart = Whole(root, "cetStartUnixMs");
        var overlapStart = Whole(root, "overlapStartUnixMs");
        var overlapEnd = Whole(root, "overlapEndUnixMs");
        if (cetStart < 1_000_000_000_000 ||
            overlapStart < cetStart || overlapEnd <= overlapStart)
            throw new InvalidDataException("GCO QPC F11 overlap is invalid.");

        var expectedCount = Whole(root, "sourceFrameCount");
        var rows = root.GetProperty("frames");
        if (rows.ValueKind != JsonValueKind.Array || expectedCount < 2 ||
            expectedCount > 2_000_000 || rows.GetArrayLength() != expectedCount)
            throw new InvalidDataException("GCO frame series is empty, inconsistent or excessive.");
        var frames = new List<CapFrameMetric>((int)expectedCount);
        long previousRow = 0, previousCpuTick = 0, previousPresentTick = 0;
        double previousPresentMs = double.NegativeInfinity;
        var hasCpu = false;
        var hasGpu = false;
        foreach (var row in rows.EnumerateArray())
        {
            var sourceRow = Whole(row, "sourceRow");
            var cpuTick = Whole(row, "cpuStartQpc");
            var presentTick = Whole(row, "presentQpc");
            var startMs = Numeric(row, "relativeStartMs", positive: false);
            var intervalMs = Numeric(row, "intervalMs", positive: true);
            var cpu = OptionalNumeric(row, "cpuBusyMs");
            var gpu = OptionalNumeric(row, "gpuBusyMs");
            _ = OptionalNumeric(row, "gpuTimeMs"); // diagnostic, NOT interchangeable with busy time
            var endMs = startMs + intervalMs;
            if (sourceRow <= previousRow || cpuTick <= 0 || presentTick <= 0 ||
                cpuTick < previousCpuTick || presentTick < previousPresentTick ||
                endMs < previousPresentMs || !double.IsFinite(endMs))
                throw new InvalidDataException("GCO frame series lacks monotonic source/QPC chronology.");
            previousRow = sourceRow;
            previousCpuTick = cpuTick;
            previousPresentTick = presentTick;
            previousPresentMs = endMs;
            hasCpu |= cpu is not null;
            hasGpu |= gpu is not null;
            frames.Add(new CapFrameMetric
            {
                Index = frames.Count,
                RelativeStartMs = startMs,
                FrameMs = intervalMs,
                CpuActiveMs = cpu ?? 0,
                GpuActiveMs = gpu ?? 0,
                PcLatencyMs = 0, // NOT_CAPTURED; never report as a measured zero.
                FrameType = "" // NOT_CAPTURED; PresentMon did not prove dropped/FG flags.
            });
        }

        return new ParsedCapFrameX
        {
            SourceFile = Path.Combine("PresentMon", GcoPresentMonContractFile).Replace('\\', '/'),
            AppVersion = "PresentMon 2.6.0 / GCO chronology v1",
            GameName = "Cyberpunk2077.exe",
            RecordUtc = null,
            Provider = "PRESENTMON_2_6",
            AlignedByGco = true,
            CpuActiveCaptured = hasCpu,
            GpuActiveCaptured = hasGpu,
            Frames = frames
        };
    }
}
