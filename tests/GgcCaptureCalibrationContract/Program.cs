using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool ok, string why)
{
    if (!ok) throw new InvalidOperationException(why);
}

var assembly = typeof(ResolverService).Assembly;
var calibrationType = assembly.GetType(
    "GCETRuntimeProfiler.Core.Services.GgcCaptureCalibrationService", true)!;
var calibrate = calibrationType.GetMethod(
    "Build", BindingFlags.NonPublic | BindingFlags.Static)!;
var controllerType = assembly.GetType(
    "GCETRuntimeProfiler.Core.Services.GgcControllerSource", true)!;
var stage = controllerType.GetMethod(
    "TryStage", BindingFlags.NonPublic | BindingFlags.Static)!;
var estimate = calibrationType.GetMethod(
    "EstimateAppliedDefaults", BindingFlags.NonPublic | BindingFlags.Static)!;

JsonNode Interpret(string handoff, string name = "CURRENT_CAPTURE")
{
    using var doc = JsonDocument.Parse(handoff);
    return (JsonNode)calibrate.Invoke(null,
        new object[] { doc.RootElement, name })!;
}

const string untouched = """
{
  "capture": { "exclusiveMsPerSecond": 120 },
  "hitchPressure": { "toleranceFrameMs": 43.91805, "baselineFrameMs": 29.2787 },
  "garbageCollection": { "slowOperations": [
    { "heapBeforeMiB": 713, "wallMs": 120 },
    { "heapBeforeMiB": 665, "wallMs": 8 },
    { "heapBeforeMiB": 705, "wallMs": 20 },
    { "heapBeforeMiB": 687, "wallMs": 0 }
  ] }
}
""";

const string callbacks = """
{
  "callbackFamilies": [
    { "resolverFamily": "ONUPDATE", "topConsumers": [
      { "owner": "ModDormant", "registrationId": 101,
        "runtime": { "exclusiveMsPerSecond": 80, "callsPerSecond": 60 } },
      { "owner": "ModGeneric", "registrationId": 201,
        "runtime": { "exclusiveMsPerSecond": 20, "callsPerSecond": 60 } },
      { "owner": "ModGeneric", "registrationId": 202,
        "runtime": { "exclusiveMsPerSecond": 15, "callsPerSecond": 60 } },
      { "owner": "ModGeneric", "registrationId": 203,
        "runtime": { "exclusiveMsPerSecond": 5, "callsPerSecond": 60 } }
    ] }
  ]
}
""";

var applied = new List<object>
{
    new { type="SEMANTIC_RULE", Owner="ModDormant", RuleId="dormant-when-idle",
        Handler="DORMANCY_WITH_WAKE", PolicyClass="SOURCE_PROVEN",
        runtimeMsPerSecond=80.0, sourceProof=true,
        admissionEvidence="CAPTURE_MATCHED_SOURCE_PROVEN" },
    new { type="FRAME_DISPATCH_CONSOLIDATION", owner="ModGeneric",
        registrationId=201L, eventTarget="onUpdate" },
    new { type="FRAME_DISPATCH_CONSOLIDATION", owner="ModGeneric",
        registrationId=202L, eventTarget="onUpdate" },
    new { type="FRAME_DISPATCH_CONSOLIDATION", owner="ModGeneric",
        registrationId=203L, eventTarget="onUpdate" },
    new { type="UI_VISIBILITY_DORMANCY", owner="ModGeneric",
        registrationId=201L },
    new { type="SHARED_PROVIDER_READ", owner="ModGeneric",
        registrationId=201L, replacedOccurrences=5 },
    // Measured callback omitted: cannot claim benefits from arbitrary work.
    new { type="SEMANTIC_RULE", Owner="Unsupported", RuleId="no-evidence",
        Handler="DORMANCY_WITH_WAKE", runtimeMsPerSecond=(double?)null,
        sourceProof=true, admissionEvidence="SOURCE_PROVEN_NO_CAPTURE" }
};

JsonNode Decision(string handoff, bool withCallbacks)
{
    var root = new JsonObject {
        ["gGcCaptureCalibration"] = Interpret(handoff).DeepClone()
    };
    if (withCallbacks)
    {
        var cb = JsonNode.Parse(callbacks)!;
        root["callbackFamilies"] = cb["callbackFamilies"]!.DeepClone();
    }
    return root;
}

dynamic Calculate(JsonNode decision, List<object> manifest)
{
    using var doc = JsonDocument.Parse(decision.ToJsonString());
    return estimate.Invoke(null, new object[]{doc.RootElement, manifest})!;
}

bool Stage(JsonNode decision, string folder,
    Dictionary<string, byte[]> result, List<object> skips,
    List<object>? sourceManifest = null, List<object>? finalManifest = null)
{
    using var doc = JsonDocument.Parse(decision.ToJsonString());
    var composed = new List<object>(sourceManifest ?? new List<object>());
    var ok = (bool)stage.Invoke(null, new object[]{
        doc.RootElement, folder, result, new Dictionary<string,int>(),
        composed, skips
    })!;
    finalManifest?.AddRange(composed);
    return ok;
}

var observed = Interpret(untouched);
Check(observed["available"]!.GetValue<bool>(),
    "Measured hitch capture not available");
Check(observed["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 43.91805,
    "Hitch tolerance lost");
Check(observed["gcPressure"]!["observedSlowStepSamples"]!.GetValue<int>() == 3 &&
      observed["gcPressure"]!["observedHeapMedianMiB"]!.GetValue<double>() == 705,
    "Slow GC region calculated incorrectly");
Check(observed["assistance"]!["measuredOriginalCetMsPerSecond"]!.GetValue<double>() == 120 &&
      observed["assistance"]!["estimatedFreedCetMsPerSecond"] is null &&
      observed["assistance"]!["recommendedIntensityPercent"]!.GetValue<int>() == 0,
    "ANALYZE wrongly predicted savings without actually applying sources");

var decision = Decision(untouched, true);
dynamic values = Calculate(decision, applied);
Check(Math.Abs((double)values.EstimatedFreedCetMsPerSecond - 26.64) < 1e-6,
    "Per-stage semantic and generic savings not credited conservatively");
Check(Math.Abs((double)values.EstimatedSemanticSavingMsPerSecond - 24) < 1e-6 &&
      Math.Abs((double)values.EstimatedGenericSavingMsPerSecond - 2.64) < 1e-6,
    "Generic/semantic attribution wrong");
Check((int)values.DefaultIntensityPercent == 21 &&
      (int)values.DefaultFrequencyMs == 1500,
    "Estimated savings did not determine conservative initial sliders");
Check((int)values.AppliedSemanticRules == 2 &&
      (int)values.AppliedGenericRules == 3,
    "Applied rule counts lost");
Check(((System.Collections.IEnumerable)values.RuleEstimates).Cast<object>().Count() == 5,
    "Per-rule audit was lost or included unsupported fake savings");

dynamic none = Calculate(decision, new List<object>());
Check((double)none.EstimatedFreedCetMsPerSecond == 0 &&
      (int)none.DefaultIntensityPercent == 0 &&
      (double)none.EstimatedOptimizedCetMsPerSecond == 120,
    "Unsupported/non-generated work was treated as optimized");
var duplicated = new List<object>(applied);
duplicated.AddRange(applied);
dynamic noDouble = Calculate(decision, duplicated);
Check(Math.Abs((double)noDouble.EstimatedFreedCetMsPerSecond - 26.64) < 1e-6,
    "Repeated manifests inflated measured CPU savings");

// Identical generic/semantic rules can hit overlapping callback work.
// The owner-overlap rule must take the greater saving, never their sum.
var sameOwner = new List<object> {
    new { type="SEMANTIC_RULE", Owner="ModGeneric", RuleId="wake",
        Handler="DORMANCY_WITH_WAKE", runtimeMsPerSecond=20.0,
        sourceProof=true, admissionEvidence="CAPTURE_MATCHED_SOURCE_PROVEN" },
    new { type="UI_VISIBILITY_DORMANCY", owner="ModGeneric",
        registrationId=201L }
};
dynamic overlap = Calculate(decision, sameOwner);
Check(Math.Abs((double)overlap.EstimatedFreedCetMsPerSecond - 6) < 1e-6,
    "Overlapping generic and semantic stages were added twice");

// Single frame registration must never be credited as eliminated dispatch.
dynamic singleFrame = Calculate(decision, new List<object> {
    new { type="FRAME_DISPATCH_CONSOLIDATION", owner="ModGeneric",
        registrationId=201L, eventTarget="onUpdate" }
});
Check((double)singleFrame.EstimatedFreedCetMsPerSecond == 0,
    "Consolidation with only one registration invented CPU savings");

var tempRoot = Path.Combine(Path.GetTempPath(),
    "gcet-ggc-rule-estimate-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);
try
{
    var generated = new Dictionary<string,byte[]>();
    var composed = new List<object>();
    Check(Stage(decision,tempRoot,generated,new List<object>(),applied,composed),
        "Capture-qualified applied rule estimate did not generate");
    Check(generated.Count == 2 && composed.Count == applied.Count + 1,
        "G-GC created extra owner or lost manifest");
    var source = Encoding.UTF8.GetString(generated["G-GC/init.lua"]);
    var threshold = Encoding.UTF8.GetString(generated["G-GC/threshold.lua"]);
    foreach (var token in new[] {
        "schema = 3", "estimatedOriginalCetMsPerSecond = 120",
        "estimatedCoveredCetMsPerSecond = 100",
        "estimatedOptimizedCetMsPerSecond = 93.36",
        "estimatedFreedCetMsPerSecond = 26.64",
        "estimatedGgcStartingCpuMsPerSecond = 2.1312",
        "estimatedSemanticSavingMsPerSecond = 24",
        "estimatedGenericSavingMsPerSecond = 2.64",
        "defaultIntensity = 21", "defaultFrequencyMs = 1500"
    })
        Check(threshold.Contains(token), "Missing rule-based config: " + token);
    Check(source.Contains("CONTINUOUS assistance") &&
          source.Contains("ImGui.SliderInt") &&
          !source.Contains("config.maxAssistCpuMsPerSecond") &&
          !source.Contains("frameEmaMs") &&
          !source.Contains("if pressure <= 0 then"),
        "Generated runtime still throttles player sliders or waits on GC region");
    var doc = JsonSerializer.SerializeToElement(composed[^1]);
    Check(doc.GetProperty("type").GetString() == "GC_CONTROLLER" &&
          doc.GetProperty("perRuleEstimates").GetArrayLength() == 5 &&
          Math.Abs(doc.GetProperty("estimatedFreedCetMsPerSecond").GetDouble()-26.64)<1e-6,
        "Pass manifest lacks verifiable per-rule savings evidence");

    foreach (var item in generated)
    {
        var path = Path.Combine(tempRoot, item.Key.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,item.Value);
    }
    // Source-proven upgrade for all previously generated single-controller
    // versions, never marker-only trust or a second independent worker.
    foreach(var version in new[]{"V1","V2","V3","V4"})
    {
        using var stream=assembly.GetManifestResourceStream(
            "GCETRuntimeProfiler.GgcGeneratedRuntime"+version+".reference.lua");
        Check(stream is not null,"Missing historical source "+version);
        using var reader=new StreamReader(stream!);
        File.WriteAllText(Path.Combine(tempRoot,"G-GC","init.lua"),reader.ReadToEnd());
        var upgraded=new Dictionary<string,byte[]>();
        Check(Stage(decision,tempRoot,upgraded,new List<object>(),applied),
            "Source-proven "+version+" upgrade failed");
        Check(upgraded["G-GC/init.lua"].SequenceEqual(generated["G-GC/init.lua"]),
            "Version "+version+" upgrade altered generated ownership");
    }
    File.WriteAllBytes(Path.Combine(tempRoot,"G-GC","init.lua"),generated["G-GC/init.lua"]);
    var repeated=new Dictionary<string,byte[]>();
    Check(Stage(decision,tempRoot,repeated,new List<object>(),applied) &&
        generated.All(x=>x.Value.SequenceEqual(repeated[x.Key])),
        "Same capture and installed source did not regenerate idempotently");

    var noRuleFiles=new Dictionary<string,byte[]>();
    Check(Stage(decision,tempRoot,noRuleFiles,new List<object>()),
        "Fully optimized/current stack should still emit a safe controller");
    var noRuleThreshold=Encoding.UTF8.GetString(noRuleFiles["G-GC/threshold.lua"]);
    Check(noRuleThreshold.Contains("defaultIntensity = 0") &&
          noRuleThreshold.Contains("estimatedFreedCetMsPerSecond = 0") &&
          noRuleThreshold.Contains("estimatedOptimizedCetMsPerSecond = 120"),
        "Already-optimized no-new-rule run fabricated fresh savings");

    var noCycle=Decision(
        """{"capture":{"exclusiveMsPerSecond":100},"hitchPressure":{"baselineFrameMs":19,"toleranceFrameMs":31},"garbageCollection":{"slowOperations":[]}}""",false);
    var noCycleFiles=new Dictionary<string,byte[]>();
    Check(Stage(noCycle,tempRoot,noCycleFiles,new List<object>()),
        "No collector cycle incorrectly prevented safe generation");
    var noCycleThreshold=Encoding.UTF8.GetString(noCycleFiles["G-GC/threshold.lua"]);
    Check(noCycleThreshold.Contains("pressureStartMiB = nil") &&
          noCycleThreshold.Contains("pressureSampleCount = 0"),
        "No-cycle generation fabricated an observed trigger");
    File.WriteAllBytes(Path.Combine(tempRoot,"G-GC","threshold.lua"),
        noCycleFiles["G-GC/threshold.lua"]);
    Check(Stage(noCycle,tempRoot,new Dictionary<string,byte[]>(),new List<object>()),
        "Nullable GC region failed re-generation");

    var missing=Decision("""{"capture":{"exclusiveMsPerSecond":120},"hitchPressure":{"baselineFrameMs":21},"garbageCollection":{"slowOperations":[]}}""",false);
    var missingFiles=new Dictionary<string,byte[]>();var skips=new List<object>();
    Check(!Stage(missing,tempRoot,missingFiles,skips) &&
          missingFiles.Count==0 && skips.Count==1,
        "Missing measured hitch tolerance silently deployed controller");

    var init=Path.Combine(tempRoot,"G-GC","init.lua");
    File.AppendAllText(init,"\nprint('source drift')\n");
    var modified=new Dictionary<string,byte[]>();
    Check(!Stage(decision,tempRoot,modified,new List<object>(),applied) &&
        modified.Count==0,"Marker-bearing manual drift was overwritten");
    File.WriteAllText(init,"-- foreign user controller");
    Check(!Stage(decision,tempRoot,new Dictionary<string,byte[]>(),
          new List<object>(),applied),"Foreign G-GC was overwritten");
}
finally { Directory.Delete(tempRoot,true); }
Console.WriteLine("G-GC applied per-rule estimates, current-source idempotence, drift and defaults PASSED");
