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
  "capture": { "exclusiveMsPerSecond": 120 },
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
Check(a["assistance"]!["policy"]!.GetValue<string>() ==
    "CAPTURE_ESTIMATED_POST_OPTIMIZATION_DEFAULTS_MANUAL_CET_TUNING",
    "Captured pre-optimization performance is still treated as a live runtime cap");
Check(a["assistance"]!["measuredOriginalCetMsPerSecond"]!.GetValue<double>() == 120 &&
      a["assistance"]!["estimatedOptimizedCetMsPerSecond"]!.GetValue<double>() == 60 &&
      a["assistance"]!["estimatedFreedCetMsPerSecond"]!.GetValue<double>() == 60 &&
      Math.Abs(a["assistance"]!["estimatedGgcStartingCpuMsPerSecond"]!.GetValue<double>() - 4.8) < 0.000001,
    "CET-work 50%-reduction and 8%-of-saving estimate is not capture-derived");
Check(a["assistance"]!["recommendedIntensityPercent"]!.GetValue<int>() == 31 &&
      a["assistance"]!["recommendedMinFrequencyMs"]!.GetValue<int>() == 1200,
    "CET workload reduction did not determine starting sliders");
Check(a["control"]!["manualOverridesNotCappedByEstimatedSaving"]!.GetValue<bool>() &&
      !a["control"]!["runtimeCpuProbingRequired"]!.GetValue<bool>(),
    "Manual CET adjustment was subjected to estimated saving as a hard cap");
Check(a["assistance"]!["frameReserveMs"]!.GetValue<double>() > 8,
    "The heavy-gameplay frametime reserve was discarded");
Check(a["control"]!["enabledByDefault"]!.GetValue<bool>(),
    "Qualified capture with room for assistance remained observe-only");
var b = Interpret(
    """
    {
      "capture": {"exclusiveMsPerSecond": 300},
      "hitchPressure": {"toleranceFrameMs": 28, "baselineFrameMs": 20},
      "garbageCollection": {"slowOperations":[{"heapBeforeMiB": 512, "wallMs": 3}]}
    }
    """, "NEW_CAPTURE");
Check(b["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 28,
    "Captured limits are incorrectly hardcoded");
Check(b["gcPressure"]!["observedHeapMedianMiB"]!.GetValue<double>() == 512,
    "Pressure context is reused from an old capture");
Check(b["assistance"]!["recommendedIntensityPercent"]!.GetValue<int>() == 58 &&
      b["assistance"]!["recommendedMinFrequencyMs"]!.GetValue<int>() == 550 &&
      b["assistance"]!["estimatedFreedCetMsPerSecond"]!.GetValue<double>() == 150,
    "300ms/s measured CET -> 150ms/s estimated CET did not affect defaults");
var littleRoom = Interpret(
    """
    {"capture":{"exclusiveMsPerSecond":120},
     "hitchPressure":{"baselineFrameMs":24,"toleranceFrameMs":26},
     "garbageCollection":{"slowOperations":[{"heapBeforeMiB":600,"wallMs":8}]}}
    """, "NO_SPARE_FRAME_BUDGET");
Check(littleRoom["available"]!.GetValue<bool>() &&
      littleRoom["assistance"]!["recommendedIntensityPercent"]!.GetValue<int>() == 0 &&
      littleRoom["assistance"]!["estimatedFreedCetMsPerSecond"]!.GetValue<double>() == 60,
    "Measured CET savings should remain documented despite no safe default frame margin");
// No natural GC cycle in a valid frame capture must not block the ongoing
// helper. The GC region is informational; never invent a MiB threshold.
const string frameOnlyCapture = """
{"capture":{"exclusiveMsPerSecond":100},
 "hitchPressure":{"baselineFrameMs":19,"toleranceFrameMs":31},
 "garbageCollection":{"slowOperations":[]}}
""";
var frameOnly = Interpret(frameOnlyCapture, "VALID_FRAME_NO_GC_CYCLE");
Check(frameOnly["available"]!.GetValue<bool>() &&
      frameOnly["gcPressure"]!["observedSlowStepSamples"]!.GetValue<int>() == 0 &&
      frameOnly["gcPressure"]!["observedHeapMedianMiB"] is null &&
      frameOnly["generator"]!["installAuthorized"]!.GetValue<bool>() &&
      frameOnly["assistance"]!["recommendedIntensityPercent"]!.GetValue<int>() > 0,
    "Optional GC forecast wrongly blocks a capture-calibrated continuous helper");
var missing = Interpret(
    """{"hitchPressure":{"baselineFrameMs":21},"garbageCollection":{"slowOperations":[]}}""",
    "NO_CAPFRAMEX");
Check(!missing["available"]!.GetValue<bool>(), "Missing data was silently admitted");
Check(missing["gameplay"]!["hitchToleranceMs"] is null,
    "Fallback tolerance fabricated from missing CapFrameX evidence");
Check(!missing["generator"]!["installAuthorized"]!.GetValue<bool>(),
    "Missing evidence installed a collector");

const string secondCapture = """
{"capture":{"exclusiveMsPerSecond":300},
 "hitchPressure":{"toleranceFrameMs":28,"baselineFrameMs":20},
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
        "estimatedOriginalCetMsPerSecond", "pcall(collectgarbage, \"step\"",
        "backoffSeconds", "growthMiBps"})
        Check(lua.Contains(text), "G-GC runtime missing " + text);
    Check(lua.Contains("CONTINUOUS assistance") &&
          lua.Contains("0.10 + 0.90 * demand") &&
          !lua.Contains("if pressure <= 0 then") &&
          !lua.Contains("if learnCooldown > 0 then lastBlock"),
          "Generated worker still gates assistance on an approximate GC threshold");
    Check(!lua.Contains("collectgarbage(\"collect\""),
        "G-GC must not schedule full garbage collection");
    Check(threshold.Contains("43.91805") && threshold.Contains("29.2787"),
        "Generated calibration does not preserve actual captured frametimes");
    Check(threshold.Contains("schema = 2") &&
          threshold.Contains("defaultIntensity = 31") &&
          threshold.Contains("defaultFrequencyMs = 1200") &&
          threshold.Contains("estimatedOriginalCetMsPerSecond = 120") &&
          threshold.Contains("estimatedOptimizedCetMsPerSecond = 60") &&
          threshold.Contains("estimatedFreedCetMsPerSecond = 60") &&
          threshold.Contains("estimatedGgcStartingCpuMsPerSecond = 4.8") &&
          threshold.Contains("assumedOptimizedFraction = 0.5"),
        "Generator discarded the selected-capture savings estimate and defaults");
    Check(!lua.Contains("if windowUsedMs >=") &&
          !lua.Contains("headroomFactor") &&
          !lua.Contains("frameEmaMs") &&
          !lua.Contains("config.maxAssistCpuMsPerSecond"),
        "Old pre-optimization CPU throttle still overrides user CET sliders");
    foreach (var item in generated)
    {
        var file = Path.Combine(tempRoot, item.Key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, item.Value);
    }
    // Recognize both exact previously deployed G-GC runtimes directly in
    // the authoritative generator: V1 first release and V2 pressure-gated.
    // No duplicate worker, marker-only trust or manual removal required.
    var assembly = typeof(ResolverService).Assembly;
    foreach (var version in new[] { "V1", "V2", "V3" })
    {
        using var priorStream = assembly.GetManifestResourceStream(
            "GCETRuntimeProfiler.GgcGeneratedRuntime" + version + ".reference.lua");
        Check(priorStream is not null, "Resolver lacks " + version + " source proof");
        using var priorReader = new StreamReader(priorStream!);
        var priorSource = priorReader.ReadToEnd();
        var deployedInit = Path.Combine(tempRoot, "G-GC", "init.lua");
        File.WriteAllText(deployedInit, priorSource);
        var upgraded = new Dictionary<string, byte[]>();
        Check(Stage(first, tempRoot, upgraded, new List<object>()),
            "Previously deployed " + version + " G-GC cannot be upgraded");
        Check(upgraded["G-GC/init.lua"].SequenceEqual(generated["G-GC/init.lua"]),
            version + " migration emitted a duplicate or outdated runtime");
        File.WriteAllBytes(deployedInit, upgraded["G-GC/init.lua"]);
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
    var noCycleOverlay = new Dictionary<string, byte[]>();
    Check(Stage(frameOnlyCapture, tempRoot, noCycleOverlay, new List<object>()),
        "No-GC-cycle capture failed to deploy continuous frame-bounded G-GC");
    var noCycleThreshold = System.Text.Encoding.UTF8.GetString(
        noCycleOverlay["G-GC/threshold.lua"]);
    foreach (var field in new[] { "pressureReferenceMiB = nil",
        "pressureStartMiB = nil", "pressureHighMiB = nil",
        "pressureSampleCount = 0" })
        Check(noCycleThreshold.Contains(field),
            "Missing GC observation was fabricated instead of set to nil: " + field);
    // Install the nullable threshold, then prove regeneration still recognizes
    // it as owned G-GC output without requiring a prior capture.
    File.WriteAllBytes(Path.Combine(tempRoot, "G-GC", "threshold.lua"),
        noCycleOverlay["G-GC/threshold.lua"]);
    Check(Stage(frameOnlyCapture, tempRoot,
        new Dictionary<string, byte[]>(), new List<object>()),
        "Nullable collector forecast broke same-source re-generation");
    var missingOverlay = new Dictionary<string, byte[]>();
    var skip = new List<object>();
    Check(!Stage("""{"hitchPressure":{"baselineFrameMs":20}}""", tempRoot, missingOverlay, skip)
          && missingOverlay.Count == 0 && skip.Count == 1,
        "Missing capture evidence silently produced a runtime");
    var generatedInit = Path.Combine(tempRoot, "G-GC", "init.lua");
    File.AppendAllText(generatedInit, "\nprint('source drift')\n");
    var modified = new Dictionary<string, byte[]>();
    Check(!Stage(first, tempRoot, modified, new List<object>()) && modified.Count == 0,
        "Marker-bearing, modified G-GC code was silently overwritten");
    File.WriteAllText(Path.Combine(tempRoot,"G-GC","init.lua"), "-- foreign G-GC");
    var drift = new Dictionary<string, byte[]>();
    Check(!Stage(first, tempRoot, drift, new List<object>()) && drift.Count == 0,
        "Foreign G-GC was overwritten");
}
finally { Directory.Delete(tempRoot, true); }

Console.WriteLine("G-GC selected-capture calibration evidence contract PASSED");
