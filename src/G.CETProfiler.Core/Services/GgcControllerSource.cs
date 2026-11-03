using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// One authoritative G-GC owner generated from THIS admitted Resolver capture.
/// No standalone installation, no historical thresholds and no per-mod GC workers.
/// The bundled runtime is source-independent; all hardware/latency assumptions are
/// generated from current measurements, not encoded into the hot loop.
/// </summary>
internal static class GgcControllerSource
{
    internal const string RuntimePath = "G-GC/init.lua";
    internal const string CalibrationPath = "G-GC/threshold.lua";
    private const string RuntimeSignature = "-- GGC_RUNTIME_V1_RESOLVER_OWNED";
    private const string CalibrationSignature = "-- GGC_CAPTURE_CALIBRATION_V1";

    internal static bool TryStage(
        JsonElement resolverRoot,
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> manifest,
        List<object> skipped)
    {
        // Capture-only evidence; a missing CapFrameX/hitch signal must NOT
        // silently become a made-up threshold or an uncalibrated helper.
        var defaults = GgcCaptureCalibrationService.EstimateAppliedDefaults(
            resolverRoot, manifest);
        if (!TryCalibrate(resolverRoot, defaults, out var calibration, out var refusal))
        {
            skipped.Add(new { type = "GC_CONTROLLER_NOT_GENERATED",
                reason = refusal, owner = "G-GC" });
            return false;
        }

        // An existing foreign G-GC installation must never be replaced.
        // Generated sources are recognized by both their signature and shape.
        if (!CompatibleLive(modsRoot, RuntimePath, RuntimeSignature,
                "registerForEvent(\"onUpdate\"", "pcall(collectgarbage, \"step\"") ||
            !CompatibleLive(modsRoot, CalibrationPath, CalibrationSignature,
                "return {", "pressureReferenceMiB"))
        {
            skipped.Add(new { type = "GC_CONTROLLER_SOURCE_DRIFT",
                reason = "G-GC exists but is not the recognized Resolver-owned runtime. " +
                         "Refusing to overwrite user or modified Lua.",
                owner = "G-GC" });
            return false;
        }

        staged[RuntimePath] = Encoding.UTF8.GetBytes(Runtime);
        staged[CalibrationPath] = Encoding.UTF8.GetBytes(calibration);
        appliedTransformsByFile[RuntimePath] = 0;
        appliedTransformsByFile[CalibrationPath] = 0;
        manifest.Add(new
        {
            type = "GC_CONTROLLER",
            owner = "G-GC",
            stage = "COMPOSED_RULE_ESTIMATED_DEFAULTS_CONTINUOUS_RUNTIME_V6",
            files = new[] {RuntimePath, CalibrationPath},
            captureEvidence = "CURRENT_CAPTURE_AND_GENERATED_SOURCE_PROOF",
            control = "PER_RULE_ESTIMATES_INITIAL_CET_SLIDERS_ONLY",
            measuredOriginalCetMsPerSecond = defaults.MeasuredOriginalCetMsPerSecond,
            estimatedCoveredCetMsPerSecond = defaults.EstimatedCoveredCetMsPerSecond,
            estimatedFreedCetMsPerSecond = defaults.EstimatedFreedCetMsPerSecond,
            estimatedOptimizedCetMsPerSecond = defaults.EstimatedOptimizedCetMsPerSecond,
            estimatedSemanticSavingMsPerSecond = defaults.EstimatedSemanticSavingMsPerSecond,
            estimatedGenericSavingMsPerSecond = defaults.EstimatedGenericSavingMsPerSecond,
            defaults.AppliedSemanticRules,
            defaults.AppliedGenericRules,
            defaults.CreditedOwners,
            defaults.DefaultIntensityPercent,
            defaults.DefaultFrequencyMs,
            defaults.EstimateStatus,
            perRuleEstimates = defaults.RuleEstimates,
            provenGameplay = false
        });
        return true;
    }

    private static bool CompatibleLive(
        string modsRoot, string relative, string signature, params string[] required)
    {
        var path = Path.Combine(modsRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path)) return true;
        var source = File.ReadAllText(path).Replace("\r\n", "\n");
        if (!source.StartsWith(signature, StringComparison.Ordinal) ||
            !required.All(token => source.Contains(token, StringComparison.Ordinal)))
            return false;
        if (relative.Equals(RuntimePath, StringComparison.OrdinalIgnoreCase))
        {
            if (source.TrimEnd() == Runtime.Replace("\r\n", "\n").TrimEnd())
                return true;
            // Historical deployed V1/V2 are inert source-equivalence
            // references, never executable fallback workers. Recognize only
            // the complete exact previously emitted source; any manual drift
            // refuses overwrite. All output still has one authoritative owner.
            var assembly = typeof(GgcControllerSource).Assembly;
            foreach (var legacyVersion in new[] { "V1", "V2", "V3", "V4", "V5" })
            {
                using var legacy = assembly.GetManifestResourceStream(
                    "GCETRuntimeProfiler.GgcGeneratedRuntime" + legacyVersion + ".reference.lua");
                if (legacy is null)
                    throw new InvalidOperationException(
                        "G-GC runtime migration reference missing: " + legacyVersion);
                using var reader = new StreamReader(legacy, Encoding.UTF8);
                var previous = reader.ReadToEnd().Replace("\r\n", "\n");
                if (source.TrimEnd() == previous.TrimEnd())
                    return true;
            }
            return false;
        }
        if (!relative.Equals(CalibrationPath, StringComparison.OrdinalIgnoreCase))
            return false;
        // Config values legitimately change on each new capture, but only a
        // complete, inert scalar schema-v1 configuration may be replaced.
        var lines = source.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var missingPriorFields = 0;
        var missingWorkFields = 0;
        var version = lines.Length > 4
            ? lines[4].Trim() switch
            {
                "schema = 3," => 3,
                "schema = 2," => 2,
                "schema = 1," => 1,
                _ => 0
            } : 0;
        string[] keys = version == 3
            ? [
                "schema", "capture", "baselineFrameMs", "hitchToleranceMs",
                "pressureReferenceMiB", "pressureStartMiB", "pressureHighMiB",
                "pressureSampleCount", "frameReserveMs",
                "estimatedOriginalCetMsPerSecond",
                "estimatedCoveredCetMsPerSecond",
                "estimatedOptimizedCetMsPerSecond",
                "estimatedFreedCetMsPerSecond",
                "estimatedGgcStartingCpuMsPerSecond",
                "estimatedSemanticSavingMsPerSecond",
                "estimatedGenericSavingMsPerSecond",
                "defaultIntensity", "defaultFrequencyMs"
              ]
            : version == 2
            ? [
                "schema", "capture", "baselineFrameMs", "hitchToleranceMs",
                "pressureReferenceMiB", "pressureStartMiB", "pressureHighMiB",
                "pressureSampleCount", "frameReserveMs",
                "estimatedOriginalCetMsPerSecond",
                "estimatedOptimizedCetMsPerSecond",
                "estimatedFreedCetMsPerSecond",
                "estimatedGgcStartingCpuMsPerSecond",
                "assumedOptimizedFraction",
                "defaultIntensity", "defaultFrequencyMs"
              ]
            : [
                "schema", "capture", "baselineFrameMs", "hitchToleranceMs",
                "pressureReferenceMiB", "pressureStartMiB", "pressureHighMiB",
                "pressureSampleCount", "frameReserveMs",
                "maxAssistCpuMsPerSecond", "defaultIntensity",
                "defaultFrequencyMs"
              ];
        if (version == 0 || lines.Length != keys.Length + 5 ||
            !lines[1].StartsWith("-- Generated by G-CET Resolver", StringComparison.Ordinal) ||
            !lines[2].StartsWith("-- a new Resolver pass", StringComparison.Ordinal) ||
            lines[3] != "return {" || lines[^1] != "}")
            return false;
        for (var i = 0; i < keys.Length; i++)
        {
            var line = lines[i + 4];
            var prefix = "  " + keys[i] + " = ";
            if (!line.StartsWith(prefix, StringComparison.Ordinal) ||
                !line.EndsWith(",", StringComparison.Ordinal)) return false;
            var value = line.Substring(prefix.Length, line.Length - prefix.Length - 1);
            if (keys[i] == "capture")
            {
                if (value.Length < 2 || value[0] != '"' || value[^1] != '"' ||
                    value[1..^1].Contains('"')) return false;
            }
            else if (value == "nil" &&
                     (keys[i] == "pressureReferenceMiB" ||
                      keys[i] == "pressureStartMiB" ||
                      keys[i] == "pressureHighMiB"))
                missingPriorFields++;
            else if (version >= 2 && value == "nil" &&
                     (keys[i] == "estimatedOriginalCetMsPerSecond" ||
                      keys[i] == "estimatedOptimizedCetMsPerSecond" ||
                      keys[i] == "estimatedFreedCetMsPerSecond" ||
                      keys[i] == "estimatedGgcStartingCpuMsPerSecond"))
                missingWorkFields++;
            else if (!double.TryParse(value,
                         NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                     !double.IsFinite(number) || number < 0 ||
                     (keys[i] == "schema" && number != version))
                return false;
        }
        // Historical source and new scalar configuration both require proof
        // of complete, inert structure, including optional evidence fields.
        return (missingPriorFields is 0 or 3) &&
               (missingWorkFields is 0 or 4);
    }

    private static bool TryCalibrate(
        JsonElement root, GgcCaptureCalibrationService.AppliedDefaults defaults,
        out string lua, out string refusal)
    {
        lua = "";
        refusal = "";
        // GENERATE reads G-CET_Resolver.json, not CET_Resolver_Input.json.
        // Its gGcCaptureCalibration was derived once by ResolverService
        // from the selected capture. Do not reinterpret a second source.
        if (!root.TryGetProperty("gGcCaptureCalibration", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object ||
            !evidence.TryGetProperty("available", out var admitted) ||
            admitted.ValueKind != JsonValueKind.True ||
            !evidence.TryGetProperty("generator", out var generator) ||
            !generator.TryGetProperty("installAuthorized", out var authorized) ||
            authorized.ValueKind != JsonValueKind.True)
        {
            refusal = "Selected Resolver decision has no valid, admitted G-GC " +
                      "capture calibration. Run ANALYZE on a measured Profiler capture.";
            return false;
        }
        if (!evidence.TryGetProperty("gameplay", out var game) ||
            !evidence.TryGetProperty("gcPressure", out var pressure) ||
            !evidence.TryGetProperty("capture", out var sourceCapture) ||
            !evidence.TryGetProperty("assistance", out var allowance) ||
            !TryNumber(allowance, "frameReserveMs", out var reserveMs) ||
            !TryNumber(game, "medianFrameMs", out var baseline) ||
            !TryNumber(game, "hitchToleranceMs", out var tolerance) ||
            !TryNumber(pressure, "observedSlowStepSamples", out var sampleCount) ||
            sourceCapture.ValueKind != JsonValueKind.String)
        {
            refusal = "G-GC capture calibration is incomplete or structurally invalid.";
            return false;
        }
        var capture = sourceCapture.GetString() ?? "unknown";
        var count = (int)sampleCount;
        var initialIntensity = defaults.DefaultIntensityPercent;
        var initialFrequencyMs = defaults.DefaultFrequencyMs;
        var hasMeasuredWork = defaults.MeasuredOriginalCetMsPerSecond is >= 0 &&
            defaults.EstimatedOptimizedCetMsPerSecond is >= 0 &&
            defaults.EstimatedGgcStartingCpuMsPerSecond is >= 0;
        var originalCet = defaults.MeasuredOriginalCetMsPerSecond ?? 0;
        var optimizedCet = defaults.EstimatedOptimizedCetMsPerSecond ?? 0;
        var freedCet = defaults.EstimatedFreedCetMsPerSecond;
        var initialGgcCpu = defaults.EstimatedGgcStartingCpuMsPerSecond ?? 0;
        if (!hasMeasuredWork && initialIntensity > 0)
        {
            refusal = "Nonzero G-GC defaults require measured CET workload.";
            return false;
        }
        double median = 0, p25 = 0, p75 = 0;
        var hasObservedRegion =
            count > 0 &&
            TryNumber(pressure, "observedHeapMedianMiB", out median) &&
            TryNumber(pressure, "observedHeapP25MiB", out p25) &&
            TryNumber(pressure, "observedHeapP75MiB", out p75);
        if (count < 0 || (count > 0 && !hasObservedRegion))
        {
            refusal = "G-GC optional observed pressure region is inconsistent.";
            return false;
        }
        if (baseline < 2 || baseline >= tolerance || tolerance > 300 ||
            (hasObservedRegion && (median < 64 || median > 16384)) ||
            reserveMs < 0 ||
            (hasMeasuredWork && (originalCet > 1000 ||
                optimizedCet > originalCet ||
                freedCet < 0 || freedCet > originalCet ||
                Math.Abs(optimizedCet - (originalCet - freedCet)) > 0.00001 ||
                Math.Abs(initialGgcCpu - freedCet * 0.08) > 0.00001)) ||
            initialIntensity < 0 || initialIntensity > 100 ||
            initialFrequencyMs < 250 || initialFrequencyMs > 5000 ||
            (tolerance - baseline - reserveMs <= 0 && initialIntensity > 0))
        {
            refusal = "Measured G-GC calibration is outside safe experimental bounds.";
            return false;
        }

        // Broad empirical pressure band, explicitly NOT a trigger time.
        // Capture-specific dispersion enlarges rather than narrows the slack.
        var slack = hasObservedRegion
            ? Math.Max(Math.Max(32.0, median * 0.08), (p75 - p25) * 0.75)
            : 0.0;
        var pressureStart = hasObservedRegion ? Math.Max(64, median - slack) : 0.0;
        var pressureHigh = hasObservedRegion ? median + slack : 0.0;
        // The Resolver's selected-capture allowance is authoritative.
        // This generator writes it verbatim; it cannot invent independent
        // tuning values or reset the recommended assist level to zero.
        capture = capture.Replace("\\", "_").Replace("\"", "_")
            .Replace("\n", "_").Replace("\r", "_");

        static string N(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);
        lua = $$"""
-- GGC_CAPTURE_CALIBRATION_V1
-- Generated by G-CET Resolver from the current capture. Do not edit;
-- a new Resolver pass replaces this exact configuration.
return {
  schema = 3,
  capture = "{{capture}}",
  baselineFrameMs = {{N(baseline)}},
  hitchToleranceMs = {{N(tolerance)}},
  pressureReferenceMiB = {{(hasObservedRegion ? N(median) : "nil")}},
  pressureStartMiB = {{(hasObservedRegion ? N(pressureStart) : "nil")}},
  pressureHighMiB = {{(hasObservedRegion ? N(pressureHigh) : "nil")}},
  pressureSampleCount = {{count}},
  frameReserveMs = {{N(reserveMs)}},
  estimatedOriginalCetMsPerSecond = {{(hasMeasuredWork ? N(originalCet) : "nil")}},
  estimatedCoveredCetMsPerSecond = {{N(defaults.EstimatedCoveredCetMsPerSecond)}},
  estimatedOptimizedCetMsPerSecond = {{(hasMeasuredWork ? N(optimizedCet) : "nil")}},
  estimatedFreedCetMsPerSecond = {{(hasMeasuredWork ? N(freedCet) : "nil")}},
  estimatedGgcStartingCpuMsPerSecond = {{(hasMeasuredWork ? N(initialGgcCpu) : "nil")}},
  estimatedSemanticSavingMsPerSecond = {{N(defaults.EstimatedSemanticSavingMsPerSecond)}},
  estimatedGenericSavingMsPerSecond = {{N(defaults.EstimatedGenericSavingMsPerSecond)}},
  defaultIntensity = {{N(initialIntensity)}},
  defaultFrequencyMs = {{N(initialFrequencyMs)}},
}
""";
        return true;
    }

    private static bool TryNumber(JsonElement json, string key, out double number)
    {
        number = 0;
        return json.ValueKind == JsonValueKind.Object &&
               json.TryGetProperty(key, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetDouble(out number) &&
               double.IsFinite(number);
    }

    // One live controller. onDraw builds UI only while the CET overlay is open.
    // No synchronous full collections, new game observers or mod-owned workers.
    // All numbers here are runtime safety bounds; the user's actual budget is
    // generated above from measured frametime tolerance.
    internal const string Runtime = """
-- GGC_RUNTIME_V1_RESOLVER_OWNED
-- One G-GC runtime generated by Resolver; player sliders own tuning.
-- Incremental steps are scheduled continuously when enabled and in-world.
-- Measured heap growth adjusts step SIZE; natural-GC thresholds are irrelevant.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 3
    or type(config.hitchToleranceMs) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local intensity = math.max(0, math.min(100, config.defaultIntensity or 0))
local frequencyMs = math.max(250, math.min(5000, config.defaultFrequencyMs or 1000))
local overlayOpen = false
local age, sinceStep, heartbeatAge = 0, 0, 0
local heapMiB, previousHeap, growthMiBps, heapFloorMiB = 0, nil, 0, nil
local backoffSeconds = 0
local lastStepCpuMs, maximumStepCpuMs = 0, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local lastStepKb, totalRequestedKiB = 0, 0
local lastReclaimedKiB, totalReclaimedKiB = 0, 0
local settingsDirty, firstMark, activeWorld = true, true, false
local lastBlock = 1
local blockNames = {
  "awaiting heap sample", "disabled", "outside gameplay",
  "frame or step-cost backoff", "step interval", "running"
}

local function markSettings()
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark, "GGC_V5_I" .. intensity .. "_F" .. frequencyMs
      .. "_S" .. stepCount)
  end
end

local function observe(dt)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    backoffSeconds = 30
    lastBlock = 4
    return
  end
  heapMiB = kib / 1024
  if heapFloorMiB == nil or heapMiB < heapFloorMiB then
    heapFloorMiB = heapMiB
  end
  if previousHeap then
    local slope = (heapMiB - previousHeap) / dt
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
    if previousHeap - heapMiB >= math.max(8, previousHeap * 0.012) then
      heapFloorMiB = heapMiB
    end
  end
  previousHeap = heapMiB

  -- Sample player state at heap cadence, never once per rendered frame.
  activeWorld = false
  if Game ~= nil and type(Game.GetPlayer) == "function" then
    local playerOk, player = pcall(Game.GetPlayer)
    activeWorld = playerOk and player ~= nil
  end
end

local function tick(dt)
  if type(dt) ~= "number" or dt <= 0 or dt > 0.50 then
    backoffSeconds = 3
    lastBlock = 4
    return
  end
  -- A cheap severe-frame guard, NOT CPU utilization monitoring.
  if dt * 1000 >= config.hitchToleranceMs then
    backoffSeconds = math.max(backoffSeconds, 2)
  end
  if backoffSeconds > 0 then backoffSeconds = math.max(0, backoffSeconds - dt) end
  age, sinceStep, heartbeatAge = age + dt, sinceStep + dt, heartbeatAge + dt
  if age >= 0.75 then
    local seconds = age
    age = 0
    observe(seconds)
    if settingsDirty or firstMark or heartbeatAge >= 15 then
      markSettings()
      heartbeatAge, settingsDirty, firstMark = 0, false, false
    end
  end

  if previousHeap == nil then lastBlock = 1; return end
  if intensity <= 0 then lastBlock = 2; return end
  if not activeWorld then lastBlock = 3; return end
  if backoffSeconds > 0 then lastBlock = 4; return end
  if sinceStep * 1000 < frequencyMs then lastBlock = 5; return end

  -- Net heap growth is NOT the raw allocation rate. The smoothed rise and
  -- accumulation since the trough are only demand signals; neither one gates
  -- positive-intensity assistance.
  local riseMiB = math.max(0, heapMiB - (heapFloorMiB or heapMiB))
  local riseSignal = math.min(1, riseMiB / math.max(24, heapMiB * 0.10))
  local slopeSignal = math.min(1,
    math.max(0, growthMiBps) / math.max(1, heapMiB * 0.015))
  local demand = math.max(riseSignal * 0.85, slopeSignal * 0.75)

  -- Stronger USER-SELECTED range: at flat heap roughly 25 KiB at 1%,
  -- 160 KiB at 37%, 274 KiB at 50%, and 1024 KiB at 100%. Live demand
  -- can raise the 100% request to 1536 KiB. These are requested collector
  -- step sizes, never a promise that equal KiB of garbage is freed.
  local level = intensity / 100
  local stepKb = math.max(1, math.min(1536,
    math.floor(24 + level * level * (1000 + 512 * demand))))
  sinceStep, lastStepKb = 0, stepKb

  -- Heap-drop metric is net change around THIS call, not total actual
  -- reclaimed objects: allocation and collector accounting can overlap.
  -- count() calls occur only at requested step cadence (not every frame).
  local beforeOk, beforeKiB = pcall(collectgarbage, "count")
  local t0 = os.clock()
  local ok, cycle = pcall(collectgarbage, "step", stepKb)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  lastStepCpuMs = cpuMs
  maximumStepCpuMs = math.max(maximumStepCpuMs, cpuMs)
  if not ok then
    rejectedSteps = rejectedSteps + 1
    backoffSeconds = 30
    intensity = 0
    settingsDirty = true
    lastBlock = 4
    return
  end
  stepCount = stepCount + 1
  totalRequestedKiB = totalRequestedKiB + stepKb
  if cycle == true then completedCycles = completedCycles + 1 end
  lastReclaimedKiB = 0
  if beforeOk and type(beforeKiB) == "number" then
    local afterOk, afterKiB = pcall(collectgarbage, "count")
    if afterOk and type(afterKiB) == "number" then
      lastReclaimedKiB = math.max(0, beforeKiB - afterKiB)
      totalReclaimedKiB = totalReclaimedKiB + lastReclaimedKiB
    end
  end
  lastBlock = 6
  -- The collector can enter an indivisible phase: post-step backoff cannot
  -- guarantee a wall-time deadline. Preserve the existing expensive-step
  -- safety response rather than silently forcing full collection.
  if cpuMs > math.max(4, config.hitchToleranceMs * 0.25) then
    backoffSeconds = 5
    lastBlock = 4
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap, heapFloorMiB = nil, nil
  growthMiBps = 0
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC")
  if shown then
    ImGui.Text(string.format("Lua heap %.1f MiB   Net %+.2f MiB/s",
      heapMiB, growthMiBps))
    local newIntensity = ImGui.SliderInt("Intensity (%)", intensity, 0, 100)
    local newFrequency = ImGui.SliderInt("Frequency (ms)", frequencyMs, 250, 5000)
    if type(newIntensity) == "number" and newIntensity ~= intensity then
      intensity = math.max(0, math.min(100, math.floor(newIntensity)))
      settingsDirty = true
      markSettings()
    end
    if type(newFrequency) == "number" and newFrequency ~= frequencyMs then
      frequencyMs = math.max(250, math.min(5000, math.floor(newFrequency)))
      settingsDirty = true
      markSettings()
    end
    ImGui.Text(string.format("Steps %d   Last %d KiB   Cycles %d",
      stepCount, lastStepKb, completedCycles))
    ImGui.Text(string.format("Requested %.2f MiB   Heap drop at steps ~%.2f MiB",
      totalRequestedKiB / 1024, totalReclaimedKiB / 1024))
    ImGui.Text(string.format("Step CPU %.2f ms last / %.2f ms max",
      lastStepCpuMs, maximumStepCpuMs))
    if lastBlock ~= 6 then
      ImGui.Text("Status: " .. blockNames[lastBlock])
    end
    if rejectedSteps > 0 then
      ImGui.Text("Failed steps: " .. rejectedSteps)
    end
  end
  ImGui.End()
end)
""";
}