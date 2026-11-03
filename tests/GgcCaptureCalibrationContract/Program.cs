using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool yes, string message)
{
    if (!yes) throw new InvalidOperationException(message);
}
var cls = typeof(ResolverService).Assembly.GetType(
    "GCETRuntimeProfiler.Core.Services.GgcCaptureCalibrationService", true)!;
var method = cls.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;
JsonNode Interpret(string json, string name)
{
    using var doc = JsonDocument.Parse(json);
    return (JsonNode)method.Invoke(null, new object[] { doc.RootElement, name })!;
}
const string first = """
{
  "hitchPressure": { "toleranceFrameMs": 43.91805, "baselineFrameMs": 29.2787 },
  "garbageCollection": { "slowOperations": [
    { "phaseBefore": "atomic", "heapBeforeMiB": 713, "wallMs": 120 },
    { "phaseBefore": "propagate", "heapBeforeMiB": 665, "wallMs": 8 },
    { "phaseBefore": "sweep", "heapBeforeMiB": 705, "wallMs": 20 },
    { "phaseBefore": "propagate", "heapBeforeMiB": 687, "wallMs": 0 }
  ] }
}
""";
var a = Interpret(first, "CET-20261103-000232_NEW_STACK_PERFORMANCE_TEST");
Check(a["available"]!.GetValue<bool>(), "Measured capture not calibrated");
Check(a["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 43.91805,
    "Ignored the user's actual frame tolerance");
Check(a["gcPressure"]!["observedSlowStepSamples"]!.GetValue<int>() == 3,
    "Invalid slow observation was counted");
Check(a["gcPressure"]!["observedHeapMedianMiB"]!.GetValue<double>() == 705,
    "Median observed pressure calculation incorrect");
Check(a["gcPressure"]!["observedHeapP25MiB"]!.GetValue<double>() == 685,
    "Slack/dispersion context incorrect");
Check(a["systemInstalledRamMiB"] is null, "Process RAM mistaken for installed system RAM");
Check(!a["generator"]!["installAuthorized"]!.GetValue<bool>(),
    "Incomplete native controller was silently shipped");
Check(a["capture"]!.GetValue<string>().Contains("20261103"),
    "Selected capture provenance lost");
var b = Interpret(
    """
    {
      "hitchPressure": {"toleranceFrameMs": 28, "baselineFrameMs": 20},
      "garbageCollection": {"slowOperations":[{"heapBeforeMiB": 512, "wallMs": 3}]}
    }
    """, "NEW_CAPTURE");
Check(b["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 28,
    "Captured limits are incorrectly hardcoded");
Check(b["gcPressure"]!["observedHeapMedianMiB"]!.GetValue<double>() == 512,
    "Pressure context is reused from an old capture");
var missing = Interpret(
    """{"hitchPressure":{"baselineFrameMs":21},"garbageCollection":{"slowOperations":[]}}""",
    "NO_CAPFRAMEX");
Check(!missing["available"]!.GetValue<bool>(), "Missing data was silently admitted");
Check(missing["gameplay"]!["hitchToleranceMs"] is null,
    "Fallback tolerance fabricated from missing CapFrameX evidence");
Check(!missing["generator"]!["installAuthorized"]!.GetValue<bool>(),
    "Missing evidence installed a collector");
Console.WriteLine("G-GC selected-capture calibration evidence contract PASSED");
