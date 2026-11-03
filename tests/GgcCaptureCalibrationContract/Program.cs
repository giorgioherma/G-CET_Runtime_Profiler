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
Check(a["generator"]!["installAuthorized"]!.GetValue<bool>(),
    "Capture-qualified experimental G-GC runtime was not admitted");
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

const string secondCapture = """
{"hitchPressure":{"toleranceFrameMs":28,"baselineFrameMs":20},
 "garbageCollection":{"slowOperations":[{"heapBeforeMiB":512,"wallMs":3}]}}
""";
var runtimeType = typeof(ResolverService).Assembly.GetType(
    "GCETRuntimeProfiler.Core.Services.GgcControllerSource", true)!;
var stage = runtimeType.GetMethod("TryStage", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Stage(string json, string folder, Dictionary<string, byte[]> result, List<object> skipped)
{
    using var doc = JsonDocument.Parse(json);
    var calibration = method.Invoke(null, new object[]
        { doc.RootElement, "CURRENT_CAPTURE" }) as JsonNode;
    var decision = new JsonObject { ["gGcCaptureCalibration"] = calibration!.DeepClone() };
    using var decisionDoc = JsonDocument.Parse(decision.ToJsonString());
    return (bool)stage.Invoke(null, new object[]
    {
        decisionDoc.RootElement, folder, result, new Dictionary<string, int>(),
        new List<object>(), skipped
    })!;
}
var tempRoot = Path.Combine(Path.GetTempPath(), "gcet-ggc-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var generated = new Dictionary<string, byte[]>();
    Check(Stage(first, tempRoot, generated, new List<object>()),
        "Measured selected capture failed to generate G-GC");
    Check(generated.Count == 2, "G-GC must generate exactly two owned files");
    var lua = System.Text.Encoding.UTF8.GetString(generated["G-GC/init.lua"]);
    var threshold = System.Text.Encoding.UTF8.GetString(generated["G-GC/threshold.lua"]);
    foreach (var text in new[] {"registerForEvent(\"onUpdate\"", "ImGui.SliderInt",
        "maxAssistCpuMsPerSecond", "collectgarbage(\"step\"", "backoffSeconds",
        "frameReserveMs", "growthMiBps"})
        Check(lua.Contains(text), "G-GC runtime missing " + text);
    Check(!lua.Contains("collectgarbage(\"collect\""),
        "G-GC must not schedule full garbage collection");
    Check(threshold.Contains("43.91805") && threshold.Contains("29.2787"),
        "Generated calibration does not preserve actual captured frametimes");
    Check(threshold.Contains("defaultIntensity = 0"),
        "Unvalidated G-GC must default to observe-only");
    foreach (var item in generated)
    {
        var file = Path.Combine(tempRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, item.Value);
    }
    var repeated = new Dictionary<string, byte[]>();
    Check(Stage(first, tempRoot, repeated, new List<object>()),
        "G-GC previously generated sources were refused");
    Check(repeated.Count == generated.Count &&
          repeated.All(e => e.Value.SequenceEqual(generated[e.Key])),
        "Repeated same-capture G-GC generation was not identical");
    var fresh = new Dictionary<string, byte[]>();
    Check(Stage(secondCapture, tempRoot, fresh, new List<object>()),
        "New capture refused previously generated runtime");
    Check(!fresh["G-GC/threshold.lua"].SequenceEqual(generated["G-GC/threshold.lua"]),
        "G-GC retained stale calibration on latest capture");
    var missing = new Dictionary<string, byte[]>();
    var skip = new List<object>();
    Check(!Stage("""{"hitchPressure":{"baselineFrameMs":20}}""", tempRoot, missing, skip)
          && missing.Count == 0 && skip.Count == 1,
        "Missing capture evidence silently produced a runtime");
    File.WriteAllText(Path.Combine(tempRoot,"G-GC","init.lua"), "-- foreign G-GC");
    var drift = new Dictionary<string, byte[]>();
    Check(!Stage(first, tempRoot, drift, new List<object>()) && drift.Count == 0,
        "Foreign G-GC was overwritten");
}
finally { Directory.Delete(tempRoot, true); }

Console.WriteLine("G-GC selected-capture calibration evidence contract PASSED");
