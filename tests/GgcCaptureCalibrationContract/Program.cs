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
var currentCapture = controllerType.GetMethod(
    "IsCurrentCaptureInstalled", BindingFlags.NonPublic | BindingFlags.Static)!;
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

object Calculate(JsonNode decision, List<object> manifest)
{
    using var doc = JsonDocument.Parse(decision.ToJsonString());
    return estimate.Invoke(null, new object[]{doc.RootElement, manifest})!;
}
double Val(object value, string name) => Convert.ToDouble(
    value.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)!
        .GetValue(value));
int Count(object value, string name) => Convert.ToInt32(
    value.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)!
        .GetValue(value));
System.Collections.IEnumerable Rows(object value,string name) =>
    (System.Collections.IEnumerable)value.GetType().GetProperty(name,
        BindingFlags.Public|BindingFlags.Instance)!.GetValue(value)!;

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

bool CurrentCaptureInstalled(JsonNode decision, string folder)
{
    using var doc = JsonDocument.Parse(decision.ToJsonString());
    return (bool)currentCapture.Invoke(null,[doc.RootElement,folder])!;
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
      observed["assistance"]!["recommendedIntensityPercent"] is null &&
      observed["control"]!["fixedIncrementalStepKiB"]!.GetValue<int>() == 50 &&
      observed["control"]!["fixedIncrementalIntervalMs"]!.GetValue<int>() == 500,
    "ANALYZE wrongly predicted savings without actually applying sources");

// Varied gameplay supplies a measured (still provisional) workload
// separation, not an invented post-optimization FPS gain. Sparse or
// unvaried older captures retain the original hitch threshold fallback.
var varied = Interpret("""
{"capture":{"exclusiveMsPerSecond":120},
 "hitchPressure":{"baselineFrameMs":26.5,"toleranceFrameMs":39.75,
   "workloadCalibrationStatus":"MEASURED_WINDOW_VARIATION_PROVISIONAL",
   "workloadRelaxedP90Ms":26.9,"workloadDemandingP90Ms":46.5,
   "workloadSuggestedHeavyMs":38.268},
 "garbageCollection":{"slowOperations":[]}}
""");
Check(varied["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 38.268 &&
      varied["gameplay"]!["toleranceSource"]!.GetValue<string>() ==
          "CAPFRAMEX_WORKLOAD_WINDOW_P90_PROVISIONAL",
    "Measured varied-gameplay workload anchor was discarded");
var unvaried = Interpret("""
{"capture":{"exclusiveMsPerSecond":120},
 "hitchPressure":{"baselineFrameMs":26.5,"toleranceFrameMs":39.75,
   "workloadCalibrationStatus":"INSUFFICIENT_VARIATION",
   "workloadRelaxedP90Ms":null,"workloadSuggestedHeavyMs":null},
 "garbageCollection":{"slowOperations":[]}}
""");
Check(unvaried["gameplay"]!["hitchToleranceMs"]!.GetValue<double>() == 39.75,
    "Unvaried workload invented a measured cutoff");

var decision = Decision(untouched, true);
object values = Calculate(decision, applied);
// The established Resolver optimization-coverage counters are authoritative.
// A completed stack's capture is measured optimized workload; the Resolver
// must not predict and subtract historical completed rules a second time.
var alreadyOptimized = (JsonObject)Decision(untouched, true);
alreadyOptimized["optimizationClassCoverage"] = JsonNode.Parse("""
{"AlreadySourceProvenRuleCount":27,"AlreadySatisfiedCallbackCount":46,
 "ReadyRuleCount":0,"GenericReadyModCount":0}
""");
var direct = Calculate(alreadyOptimized, new List<object>());
Check(Val(direct, "EstimatedOptimizedCetMsPerSecond") == 120 &&
      Val(direct, "EstimatedFreedCetMsPerSecond") == 0 &&
      (string)direct.GetType().GetProperty("EstimateStatus")!.GetValue(direct)! ==
         "DIRECT_MEASURED_PREDOMINANTLY_OPTIMIZED_CAPTURE_NO_NEW_RULE_ESTIMATES",
    "Optimized capture was treated as a hypothetical pre-optimization baseline");
// One newly eligible mod can receive an incremental estimate, but only on
// top of the already measured optimized runtime.
var incremental = Calculate(alreadyOptimized, applied);
Check(Val(incremental, "EstimatedOptimizedCetMsPerSecond") < 52 &&
      Val(incremental, "EstimatedOptimizedCetMsPerSecond") > 48 &&
      ((string)incremental.GetType().GetProperty("EstimateStatus")!
          .GetValue(incremental)!).Contains("NEW_RULE_ESTIMATES_ONLY"),
    "Measured optimized capture was not retained as the incremental baseline");
// A completely pristine stack with zero ready work is NOT 'optimized'.
var pristineNoCandidates = (JsonObject)Decision(untouched, true);
pristineNoCandidates["optimizationClassCoverage"] = JsonNode.Parse("""
{"AlreadySourceProvenRuleCount":0,"AlreadySatisfiedCallbackCount":0,
 "ReadyRuleCount":0,"GenericReadyModCount":0}
""");
var notOptimized = Calculate(pristineNoCandidates, new List<object>());
Check(!(bool)calibrationType.GetMethod(
    "RecognizesPredominantlyOptimizedStack",
    BindingFlags.NonPublic | BindingFlags.Static)!
    .Invoke(null, new object[]{JsonSerializer.SerializeToElement(pristineNoCandidates)})!,
    "Zero-work pristine stack was mistaken for an optimized capture");
Check(!((string)notOptimized.GetType().GetProperty("EstimateStatus")!
    .GetValue(notOptimized)!).StartsWith("DIRECT_MEASURED"),
    "Missing source-optimized proof fabricated a direct optimized calibration");
// Pending work dominating completed source-proven stages is not a
// predominantly optimized stack.
var mostlyUntouched = (JsonObject)Decision(untouched, true);
mostlyUntouched["optimizationClassCoverage"] = JsonNode.Parse("""
{"AlreadySourceProvenRuleCount":2,"AlreadySatisfiedCallbackCount":1,
 "ReadyRuleCount":9,"GenericReadyModCount":5}
""");
var mostlyUnoptimizedEstimate = Calculate(mostlyUntouched, new List<object>());
Check(!((string)mostlyUnoptimizedEstimate.GetType()
    .GetProperty("EstimateStatus")!.GetValue(mostlyUnoptimizedEstimate)!)
    .StartsWith("DIRECT_MEASURED"),
    "Mostly unoptimized source was treated as optimized runtime");

Check(Val(values,"EstimatedFreedCetMsPerSecond") > 70 &&
      Val(values,"EstimatedFreedCetMsPerSecond") < 72,
    "Per-stage semantic and generic savings not credited conservatively");
Check(Math.Abs(Val(values,"EstimatedSemanticSavingMsPerSecond") - 57.6) < 1e-5 &&
      Val(values,"EstimatedGenericSavingMsPerSecond") > 13 &&
      Val(values,"EstimatedGenericSavingMsPerSecond") < 14,
    "Generic/semantic attribution wrong");
Check(values.GetType().GetProperty("DefaultIntensityPercent") is null &&
      values.GetType().GetProperty("DefaultFrequencyMs") is null &&
      values.GetType().GetProperty("EstimatedGgcStartingCpuMsPerSecond") is null,
    "Obsolete GC intensity/interval or 8% collector budget survived calibration");
Check(Count(values,"AppliedSemanticRules") == 2 &&
      Count(values,"AppliedGenericRules") == 5 &&
      Count(values,"CreditedOwners") == 2,
    "Applied stage counts or uniquely credited owners were misrepresented");
Check(Rows(values,"RuleEstimates").Cast<object>().Count() == 5,
    "Per-rule audit was lost or included unsupported fake savings");

object none = Calculate(decision, new List<object>());
Check(Val(none,"EstimatedFreedCetMsPerSecond") == 0 &&
      Val(none,"EstimatedOptimizedCetMsPerSecond") == 120,
    "Unsupported/non-generated work was treated as optimized");
var duplicated = new List<object>(applied);
duplicated.AddRange(applied);
object noDouble = Calculate(decision, duplicated);
Check(Math.Abs(Val(noDouble,"EstimatedFreedCetMsPerSecond") -
      Val(values,"EstimatedFreedCetMsPerSecond")) < 1e-5,
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
object overlap = Calculate(decision, sameOwner);
Check(Val(overlap,"EstimatedFreedCetMsPerSecond") >= 14.4 &&
      Val(overlap,"EstimatedFreedCetMsPerSecond") < 26.4,
    "Overlapping generic and semantic stages were added without residual cap");

// Native-read rewrites count only for measured callbacks in the same live
// source file, not every source-only getter elsewhere in the mod.
var measuredNative = (JsonObject)Decision(untouched, true);
var providerCallback = (JsonObject)measuredNative["callbackFamilies"]![0]!
    ["topConsumers"]![1]!;
providerCallback["source"] = JsonNode.Parse("""
{"RelativeFile":"ModGeneric/init.lua","LineStart":12,"LineEnd":58}
""");
var nativeRewrite = new {
    type = "NATIVE_READ_REUSE", file = "ModGeneric/init.lua",
    operation = "GetPlayer", replacedOccurrences = 50
};
var outsideRewrite = new {
    type = "NATIVE_READ_REUSE", file = "ModGeneric/unmeasured.lua",
    operation = "GetPlayer", replacedOccurrences = 999
};
var nativeEstimate = Calculate(measuredNative, new List<object>{nativeRewrite});
Check(Val(nativeEstimate,"EstimatedGenericSavingMsPerSecond") > 0 &&
      Val(nativeEstimate,"EstimatedGenericSavingMsPerSecond") <= 3,
    "Native reads were not bounded to measured callback-file exposure");
Check(Val(Calculate(measuredNative, new List<object>{outsideRewrite}),
        "EstimatedFreedCetMsPerSecond") == 0,
    "Unmeasured native getter rewrite fabricated callback CPU savings");
Check(Math.Abs(Val(Calculate(measuredNative,
        new List<object>{nativeRewrite,nativeRewrite}),
        "EstimatedFreedCetMsPerSecond") -
      Val(nativeEstimate,"EstimatedFreedCetMsPerSecond")) < 1e-6,
    "Duplicate native getter stages inflated estimated savings");

// Single frame registration must never be credited as eliminated dispatch.
object singleFrame = Calculate(decision, new List<object> {
    new { type="FRAME_DISPATCH_CONSOLIDATION", owner="ModGeneric",
        registrationId=201L, eventTarget="onUpdate" }
});
Check(Val(singleFrame,"EstimatedFreedCetMsPerSecond") == 0,
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
        "schema = 4", "estimatedOriginalCetMsPerSecond = 120",
        "estimatedCoveredCetMsPerSecond = 120",
        "estimatedSemanticSavingMsPerSecond = 57.6",
        "estimatedOptimizedCetMsPerSecond = " +
            Val(values,"EstimatedOptimizedCetMsPerSecond").ToString("0.######",
                System.Globalization.CultureInfo.InvariantCulture),
        "estimatedFreedCetMsPerSecond = " +
            Val(values,"EstimatedFreedCetMsPerSecond").ToString("0.######",
                System.Globalization.CultureInfo.InvariantCulture),
        "estimatedGenericSavingMsPerSecond = " +
            Val(values,"EstimatedGenericSavingMsPerSecond").ToString("0.######",
                System.Globalization.CultureInfo.InvariantCulture)
    })
        Check(threshold.Contains(token), "Missing rule-based config: " + token);
    Check(!threshold.Contains("defaultIntensity") &&
          !threshold.Contains("defaultFrequencyMs") &&
          !threshold.Contains("estimatedGgcStartingCpuMsPerSecond") &&
          source.Contains("ImGui.SliderInt(\"Workload sensitivity\", sensitivity, 0, 100)") &&
          source.Contains("ImGui.Checkbox(\"Automatic assistance\", assistanceEnabled)") &&
          source.Contains("local manualKiB, manualIntervalMs = 50, 500") &&
          source.Contains("moderateCycle >= 2") &&
          source.Contains("workloadState == \"HEAVY\"") &&
          !source.Contains("ImGui.SliderInt(\"Step size (KiB)\"") &&
          !source.Contains("ImGui.SliderInt(\"Interval (ms)\"") &&
          source.Contains("DEV: Start 90s A/B test") &&
          source.Contains("beginPhase(\"off\")") &&
          source.Contains("beginPhase(\"slow\")") &&
          source.Contains("beginPhase(\"fast\")") &&
          source.Contains("GGC_TEST_DONE") &&
          source.Contains("SimpleScreenMessage.new()") &&
          source.Contains("Step CPU") &&
          source.Contains("Observed spacing") &&
          !source.Contains("config.maxAssistCpuMsPerSecond") &&
          !source.Contains("frameEmaMs") &&
          !source.Contains("if pressure <= 0 then") &&
          !source.Contains("1536"),
        "Generated G-GC lost fixed step sliders, staged experiment, HUD notice, or no-cap policy");
    var doc = JsonSerializer.SerializeToElement(composed[^1]);
    Check(doc.GetProperty("type").GetString() == "GC_CONTROLLER" &&
          doc.GetProperty("perRuleEstimates").GetArrayLength() == 5 &&
          doc.GetProperty("appliedFrameCallbacksConsolidated").GetInt32() == 3 &&
          doc.GetProperty("appliedSourceProvenSemanticRules").GetInt32() == 2 &&
          doc.GetProperty("appliedNativeGetterSites").GetInt32() == 0 &&
          doc.GetProperty("estimateModel").GetString() ==
              "CAPTURE_SCOPED_APPLIED_CALLBACK_SCOPE_V2_HEURISTIC" &&
          Math.Abs(doc.GetProperty("estimatedFreedCetMsPerSecond").GetDouble() -
              Val(values,"EstimatedFreedCetMsPerSecond")) < 1e-5 &&
          !doc.TryGetProperty("DefaultIntensityPercent", out _) &&
          doc.GetProperty("fixedStepKiB").GetInt32() == 50 &&
          doc.GetProperty("fixedStepIntervalMs").GetInt32() == 500,
        "Pass manifest lacks verifiable per-rule savings evidence");

    foreach (var item in generated)
    {
        var path = Path.Combine(tempRoot, item.Key.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path,item.Value);
    }
    // Source-proven upgrade for all previously generated single-controller
    // versions, never marker-only trust or a second independent worker.
    foreach(var version in new[]{"V1","V2","V3","V4","V5","V6","V7","V8"})
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
    Check(CurrentCaptureInstalled(decision,tempRoot),
        "ANALYZE still offers an unchanged G-GC as a new installation");
    var repeated=new Dictionary<string,byte[]>();
    var repeatedSkips=new List<object>();
    Check(!Stage(decision,tempRoot,repeated,repeatedSkips,applied) &&
          repeated.Count==0 && repeatedSkips.Any(row =>
              JsonSerializer.SerializeToElement(row).GetProperty("category")
                  .GetString()=="ALREADY_IMPLEMENTED"),
        "Same capture should preserve installed GC calibration without overlay");

    // The existing FIRST-PASS calibration remains authoritative. The next
    // run sees zero NEW rule credits but must not reset its initial settings.
    var noRuleFiles=new Dictionary<string,byte[]>();
    Check(!Stage(decision,tempRoot,noRuleFiles,new List<object>()) &&
          noRuleFiles.Count==0 &&
          File.ReadAllBytes(Path.Combine(tempRoot,"G-GC","threshold.lua"))
              .SequenceEqual(generated["G-GC/threshold.lua"]),
        "Second no-new-rule pass reset the previous G-GC calibration");

    // Fresh installs still produce a controller even without credited work.
    var freshRoot=Path.Combine(tempRoot,"fresh");
    Directory.CreateDirectory(freshRoot);
    var freshFiles=new Dictionary<string,byte[]>();
    Check(Stage(decision,freshRoot,freshFiles,new List<object>()) &&
          freshFiles.Count==2 &&
          Encoding.UTF8.GetString(freshFiles["G-GC/threshold.lua"])
              .Contains("schema = 4") &&
          !Encoding.UTF8.GetString(freshFiles["G-GC/threshold.lua"])
              .Contains("defaultIntensity"),
        "First G-GC install unexpectedly required new credited work");

    // Same name but changed measured evidence is a different profile.
    var changedEvidence=Decision(
        """{"capture":{"exclusiveMsPerSecond":120},"hitchPressure":{"baselineFrameMs":20,"toleranceFrameMs":35},"garbageCollection":{"slowOperations":[{"heapBeforeMiB":713,"wallMs":120}]}}""",
        true);
    Check(!CurrentCaptureInstalled(changedEvidence,tempRoot),
        "ANALYZE hid a newly measured profile with reused capture name");
    var changedFiles=new Dictionary<string,byte[]>();
    Check(Stage(changedEvidence,tempRoot,changedFiles,new List<object>(),applied) &&
          changedFiles.ContainsKey("G-GC/threshold.lua") &&
          !changedFiles["G-GC/threshold.lua"]
              .SequenceEqual(generated["G-GC/threshold.lua"]),
        "Changed measurements under the same capture name were ignored");

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
    var sameNoCycle=new Dictionary<string,byte[]>();
    Check(!Stage(noCycle,tempRoot,sameNoCycle,new List<object>()) &&
          sameNoCycle.Count==0,
        "Same no-GC-cycle evidence needlessly regenerated G-GC");

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
