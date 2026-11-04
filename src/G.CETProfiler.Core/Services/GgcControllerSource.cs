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

        // Re-running Resolver with the SAME measured capture does not
        // authorize new G-GC defaults just because the second overlay has
        // fewer new rules to credit. Preserve the first source-proven
        // calibration and its runtime byte-for-byte.
        if (IsExistingSameCapture(modsRoot, calibration))
        {
            skipped.Add(new { type = "GC_CONTROLLER",
                category = "ALREADY_IMPLEMENTED", owner = "G-GC",
                reason = "G-GC already installed from the same measured " +
                         "Profiler capture; preserving its calibration unchanged." });
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
            stage = "ADAPTIVE_FIXED_50K_500MS_RUNTIME_V7",
            files = new[] {RuntimePath, CalibrationPath},
            captureEvidence = "CURRENT_CAPTURE_AND_GENERATED_SOURCE_PROOF",
            control = "CAPTURE_ANCHORED_LIVE_WORKLOAD_MANUAL_SENSITIVITY",
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

    // ANALYZE must not advertise the already-calibrated G-GC runtime as
    // new work. The proposed measured evidence is identical to GENERATE's
    // except for pass-specific estimated savings, which are intentionally
    // excluded by the shared proof below.
    internal static bool IsCurrentCaptureInstalled(
        JsonElement resolverRoot, string modsRoot)
    {
        var emptyManifest = new List<object>();
        var defaults = GgcCaptureCalibrationService.EstimateAppliedDefaults(
            resolverRoot, emptyManifest);
        return TryCalibrate(resolverRoot, defaults, out var proposed, out _) &&
            IsExistingSameCapture(modsRoot, proposed);
    }

    private static bool IsExistingSameCapture(string modsRoot, string proposed)
    {
        var runtime = Path.Combine(modsRoot,
            RuntimePath.Replace('/', Path.DirectorySeparatorChar));
        var configuration = Path.Combine(modsRoot,
            CalibrationPath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(runtime) && File.Exists(configuration) &&
            CompatibleLive(modsRoot, RuntimePath, RuntimeSignature,
                "registerForEvent(\"onUpdate\"", "pcall(collectgarbage, \"step\"") &&
            CompatibleLive(modsRoot, CalibrationPath, CalibrationSignature,
                "return {", "pressureReferenceMiB") &&
            File.ReadAllText(runtime).Replace("\r\n", "\n",
                StringComparison.Ordinal).TrimEnd() ==
            Runtime.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() &&
            HasSameCapturedMeasurements(File.ReadAllText(configuration), proposed);
    }

    // Capture-derived inputs establish freshness, not pass-specific savings.
    // Compare measured values AND capture identity: a changed recording that
    // reuses a filename must still recalibrate. Only skip current complete
    // schema/current runtime; proven historical versions remain upgradeable.
    private static bool HasSameCapturedMeasurements(
        string installed, string proposed)
    {
        static string? Field(string source, string name)
        {
            var prefix = "  " + name + " = ";
            var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Where(line => line.StartsWith(prefix, StringComparison.Ordinal) &&
                               line.EndsWith(",", StringComparison.Ordinal))
                .ToArray();
            return lines.Length == 1 ? lines[0][prefix.Length..^1] : null;
        }

        if (Field(installed, "schema") != "3")
            return false;
        foreach (var field in new[] {
            "capture", "baselineFrameMs", "hitchToleranceMs",
            "pressureReferenceMiB", "pressureStartMiB", "pressureHighMiB",
            "pressureSampleCount", "frameReserveMs",
            "estimatedOriginalCetMsPerSecond" })
        {
            var previous = Field(installed, field);
            if (previous is null ||
                !string.Equals(previous, Field(proposed, field),
                    StringComparison.Ordinal))
                return false;
        }
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
            foreach (var legacyVersion in new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V7" })
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
-- Single Resolver-generated G-GC runtime. No profiler DLL required.
-- Adaptive normal maintenance: 50 KiB / 500 ms when workload permits.
-- No runtime Profiler requirement; only the developer test overrides cadence.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 3
    or type(config.hitchToleranceMs) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local function clamp(x, lo, hi)
  return math.max(lo, math.min(hi, x))
end

-- Pre-optimization capture data anchors the initial relative thresholds;
-- live optimized gameplay decides whether optional collection can run.
local manualKiB, manualIntervalMs = 50, 500
local sensitivity, assistanceEnabled = 50, true
local settingsStatus = "defaults"
local function savePreferences()
  local ok = pcall(function()
    local file = assert(io.open("user_settings.txt", "w"))
    file:write(string.format("%d %d\n", sensitivity, assistanceEnabled and 1 or 0))
    file:close()
  end)
  settingsStatus = ok and "saved" or "not saved (CET file I/O unavailable)"
end
local function loadPreferences()
  local ok, data = pcall(function()
    local file = io.open("user_settings.txt", "r")
    if not file then return nil end
    local value = file:read("*a")
    file:close()
    return value
  end)
  if not ok or type(data) ~= "string" then return end
  local percent, enabled = data:match("^(%d+)%s+([01])%s*$")
  percent = tonumber(percent)
  if percent and percent >= 0 and percent <= 100 then
    sensitivity, assistanceEnabled = percent, enabled == "1"
    settingsStatus = "restored"
  end
end
loadPreferences()
local overlayOpen = false
local heapSampleAge, sinceStep, sinceActualStep = 0, 0, 0
local haveCompletedStep = false
local heapMiB, previousHeap, growthMiBps = 0, nil, 0
local activeWorld, backoffSeconds = false, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local lastStepKiB, totalRequestedKiB = 0, 0
local lastHeapDropKiB, totalHeapDropKiB = 0, 0
local lastCpuMs, previousCpuMs, maximumCpuMs = 0, 0, 0
local lastIntervalMs, previousIntervalMs = 0, 0
local lastBlock = "awaiting heap sample"
local workloadState, relaxedReferenceMs = "HEAVY", config.baselineFrameMs
local recentFrameMs, slowFrameShare = config.baselineFrameMs, 0
local heavyFor, busyFor, easierFor, relaxedFor = 0, 0, 0, 0
local moderateCycle, skipOwnStepFrame = 0, false
local validGameplaySeconds, costlySteps = 0, 0
local costWindowSeconds, costWindowMs = 0, 0
local recommendation = "Collecting gameplay evidence"
local function limits()
  local margin = math.max(4, config.hitchToleranceMs - relaxedReferenceMs)
  local bias = (sensitivity - 50) / 50
  return relaxedReferenceMs + margin * (0.40 - bias * 0.10),
         relaxedReferenceMs + margin * (0.75 - bias * 0.20),
         relaxedReferenceMs + margin * (0.22 - bias * 0.08),
         relaxedReferenceMs + margin * (0.48 - bias * 0.12)
end

-- Test states: idle -> armed -> countdown -> off -> slow -> fast -> done.
-- All stage durations use actual CET gameplay delta; no timers or worker
-- subscriptions, and no phase clock advancement with the overlay open.
local testState, phaseAge, countdownAge = "idle", 0, 0
local testSaveKiB, testSaveIntervalMs = nil, nil
local testSteps, testRequestedKiB = {0, 0, 0}, {0, 0, 0}
local testCostMs, testMaximumCostMs = {0, 0, 0}, {0, 0, 0}
local testStarted = false

local function mark(label)
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark, label)
  end
end

local function announce(label, message)
  print("[G-GC] " .. message)
  mark(label)
end

local function notifyFinished(failed)
  -- Standard CET HUD warning-message blackboard, guarded for installations
  -- where the game's notification objects are not available.
  local success = pcall(function()
    local message = SimpleScreenMessage.new()
    message.message = failed and "G-GC test ABORTED - stop recording"
      or "G-GC 90s test DONE - stop recording"
    message.isShown = true
    message.duration = 8
    local definitions = Game.GetAllBlackboardDefs()
    local board = Game.GetBlackboardSystem():Get(definitions.UI_Notifications)
    board:SetVariant(definitions.UI_Notifications.WarningMessage,
      ToVariant(message), true)
  end)
  if not success then
    print("[G-GC] HUD notification unavailable; completion is shown in CET.")
  end
end

local function beginPhase(state)
  testState, phaseAge, sinceStep = state, 0, 0
  if state == "off" then
    testStarted = true
    announce("GGC_TEST_P1_OFF",
      "90s test START / phase 1: assistance OFF for 30s.")
  elseif state == "slow" then
    announce("GGC_TEST_P2_128K_100MS",
      "90s test phase 2: 128 KiB every 100 ms for 30s.")
  elseif state == "fast" then
    announce("GGC_TEST_P3_128K_50MS",
      "90s test phase 3: 128 KiB every 50 ms for 30s.")
  elseif state == "done" then
    announce("GGC_TEST_DONE",
      "90s test COMPLETE. Stop capture. Assistance OFF until restored.")
    notifyFinished(false)
  end
end

local function startTest()
  if testState ~= "idle" and testState ~= "done" then return end
  testSaveKiB, testSaveIntervalMs = manualKiB, manualIntervalMs
  testState, countdownAge, phaseAge, testStarted = "armed", 0, 0, false
  testSteps, testRequestedKiB = {0, 0, 0}, {0, 0, 0}
  testCostMs, testMaximumCostMs = {0, 0, 0}, {0, 0, 0}
  sinceStep = 0
  announce("GGC_TEST_ARMED",
    "90s test armed. Close CET overlay; 10s countdown then 30/30/30s.")
end

local function restoreSettings()
  if testSaveKiB ~= nil then
    manualKiB, manualIntervalMs = testSaveKiB, testSaveIntervalMs
  end
  testState, phaseAge, countdownAge = "idle", 0, 0
  sinceStep = 0
end

local function observe(seconds)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    activeWorld = false
    backoffSeconds = 30
    return
  end
  heapMiB = kib / 1024
  if previousHeap then
    local slope = (heapMiB - previousHeap) / seconds
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
  end
  previousHeap = heapMiB
  activeWorld = false
  if Game ~= nil and type(Game.GetPlayer) == "function" then
    local okPlayer, player = pcall(Game.GetPlayer)
    activeWorld = okPlayer and player ~= nil
  end
end

local function advanceTest(dt)
  if testState == "idle" or testState == "done" or testState == "failed"
      or overlayOpen or not activeWorld then return end

  if testState == "armed" then
    testState, countdownAge = "countdown", 0
    announce("GGC_TEST_COUNTDOWN", "10s test countdown started in gameplay.")
    return
  end
  if testState == "countdown" then
    countdownAge = countdownAge + dt
    if countdownAge >= 10 then beginPhase("off") end
    return
  end

  -- Transition based on gameplay elapsed time, NOT scheduled-step count.
  -- An expensive step or heavy-frame backoff cannot silently lengthen
  -- a phase or alter the preset workload.
  phaseAge = phaseAge + dt
  if phaseAge >= 30 then
    if testState == "off" then
      beginPhase("slow")
    elseif testState == "slow" then
      beginPhase("fast")
    elseif testState == "fast" then
      beginPhase("done")
    end
  end
end

local function requestedStep()
  if testState == "idle" then return manualKiB, manualIntervalMs, 0 end
  if testState == "slow" then return 128, 100, 2 end
  if testState == "fast" then return 128, 50, 3 end
  return 0, 500, 0
end

local function setWorkloadState(value)
  if workloadState == value then return end
  workloadState = value
  heavyFor, busyFor, easierFor, relaxedFor = 0, 0, 0, 0
  moderateCycle = 0
  mark("GGC_WORKLOAD_" .. value)
end

local function measureWorkload(dt)
  if testState ~= "idle" or overlayOpen or not activeWorld then return end
  if skipOwnStepFrame then
    skipOwnStepFrame = false
    return
  end
  local frameMs = dt * 1000
  -- Isolated LuaJIT/natural collector hitches are real but not a sustained
  -- workload classification. Ignore their impact on the relaxed reference.
  if frameMs >= math.max(100, config.hitchToleranceMs * 2.5) then
    backoffSeconds = math.max(backoffSeconds, 1)
    return
  end
  validGameplaySeconds = validGameplaySeconds + dt
  recentFrameMs = recentFrameMs +
    math.min(0.20, dt / 1.2) * (frameMs - recentFrameMs)
  -- Only learn faster relaxed performance, never normalize sustained combat.
  if recentFrameMs < relaxedReferenceMs - 0.5 and slowFrameShare < 0.15 then
    relaxedReferenceMs = relaxedReferenceMs +
      math.min(0.03, dt / 12) * (recentFrameMs - relaxedReferenceMs)
  end
  local medium, high, relaxed, leaveHeavy = limits()
  local slow = frameMs >= medium and 1 or 0
  slowFrameShare = slowFrameShare +
    math.min(0.12, dt / 1.8) * (slow - slowFrameShare)
  local severe = recentFrameMs >= high or slowFrameShare >= 0.55
  local busy = recentFrameMs >= medium or slowFrameShare >= 0.25
  heavyFor = severe and heavyFor + dt or 0
  busyFor = busy and busyFor + dt or 0
  easierFor = (recentFrameMs < leaveHeavy and slowFrameShare < 0.28)
    and easierFor + dt or 0
  relaxedFor = (recentFrameMs < relaxed and slowFrameShare < 0.12)
    and relaxedFor + dt or 0
  if workloadState ~= "HEAVY" and heavyFor >= 1 then
    setWorkloadState("HEAVY")
  elseif workloadState == "RELAXED" and busyFor >= 1.5 then
    setWorkloadState("MODERATE")
  elseif workloadState == "HEAVY" and easierFor >= 3 then
    setWorkloadState("MODERATE")
  elseif workloadState == "MODERATE" and relaxedFor >= 5 then
    setWorkloadState("RELAXED")
  end
end

local function tick(dt)
  if type(dt) ~= "number" or dt <= 0 or dt > 0.50 then
    backoffSeconds = 3
    lastBlock = "invalid/heavy frame"
    return
  end
  if dt * 1000 >= config.hitchToleranceMs then
    backoffSeconds = math.max(backoffSeconds, 2)
  end
  if backoffSeconds > 0 then backoffSeconds = math.max(0, backoffSeconds - dt) end
  heapSampleAge, sinceStep = heapSampleAge + dt, sinceStep + dt
  sinceActualStep = sinceActualStep + dt
  costWindowSeconds = costWindowSeconds + dt
  if costWindowSeconds >= 10 then
    local maxCost = math.max(1.5, math.min(3, config.baselineFrameMs * 0.08))
    if testState == "idle" and costWindowMs / costWindowSeconds > maxCost then
      backoffSeconds = math.max(backoffSeconds, 5)
      costlySteps = costlySteps + 1
      lastBlock = "sustained helper cost"
    end
    costWindowSeconds, costWindowMs = 0, 0
  end
  if heapSampleAge >= 0.75 then
    local seconds = heapSampleAge
    heapSampleAge = 0
    observe(seconds)
  end

  advanceTest(dt)
  measureWorkload(dt)
  if workloadState == "MODERATE" then
    moderateCycle = (moderateCycle + dt) % 5
  end
  local amount, intervalMs, phase = requestedStep()
  -- Opening CET during the experiment pauses BOTH its phase clock and
  -- incremental collector calls, keeping the capture comparisons aligned.
  if testState ~= "idle" and overlayOpen then
    lastBlock = "test paused in CET"
    return
  end
  if previousHeap == nil then lastBlock = "awaiting heap sample"; return end
  if amount <= 0 then lastBlock = "assistance off"; return end
  if not activeWorld then lastBlock = "outside gameplay"; return end
  if testState == "idle" then
    if not assistanceEnabled then lastBlock = "user disabled"; return end
    if workloadState == "HEAVY" then lastBlock = "heavy workload"; return end
    if workloadState == "MODERATE" and moderateCycle >= 2 then
      lastBlock = "moderate scheduled rest"; return
    end
  end
  if backoffSeconds > 0 then lastBlock = "frame/GC cost backoff"; return end
  if sinceStep * 1000 < intervalMs then lastBlock = "step interval"; return end

  -- This is actual gameplay time since the last step attempt, not the
  -- configured nominal interval. os.clock below measures approximate CPU
  -- time of the call, not a hard wall-time deadline.
  sinceStep = 0
  local beforeOK, beforeKiB = pcall(collectgarbage, "count")
  local t0 = os.clock()
  local ok, cycle = pcall(collectgarbage, "step", amount)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  previousCpuMs, lastCpuMs = lastCpuMs, cpuMs
  maximumCpuMs = math.max(maximumCpuMs, cpuMs)
  if testState == "idle" then
    skipOwnStepFrame = true
    costWindowMs = costWindowMs + cpuMs
  end
  if not ok then
    rejectedSteps = rejectedSteps + 1
    backoffSeconds = 30
    if phase > 0 then
      testState = "failed"
      announce("GGC_TEST_FAILED",
        "90s test ABORTED because a GC step failed; stop capture.")
      notifyFinished(true)
    else
      manualKiB = 0
    end
    lastBlock = "GC error / disabled"
    return
  end

  lastStepKiB = amount
  previousIntervalMs = lastIntervalMs
  lastIntervalMs = haveCompletedStep and sinceActualStep * 1000 or 0
  sinceActualStep, haveCompletedStep = 0, true
  stepCount, totalRequestedKiB = stepCount + 1, totalRequestedKiB + amount
  if cycle == true then completedCycles = completedCycles + 1 end
  lastHeapDropKiB = 0
  if beforeOK and type(beforeKiB) == "number" then
    local afterOK, afterKiB = pcall(collectgarbage, "count")
    if afterOK and type(afterKiB) == "number" then
      lastHeapDropKiB = math.max(0, beforeKiB - afterKiB)
      totalHeapDropKiB = totalHeapDropKiB + lastHeapDropKiB
    end
  end

  if phase > 0 then
    testSteps[phase] = testSteps[phase] + 1
    testRequestedKiB[phase] = testRequestedKiB[phase] + amount
    testCostMs[phase] = testCostMs[phase] + cpuMs
    testMaximumCostMs[phase] = math.max(testMaximumCostMs[phase], cpuMs)
  end
  lastBlock = "running"
  -- One indivisible LuaJIT collector phase can exceed the nominal cost.
  -- Cool down AFTER such a step; never force a full collection.
  if cpuMs > math.max(2.5, config.hitchToleranceMs * 0.08) then
    backoffSeconds = 5
    costlySteps = costlySteps + 1
    lastBlock = "expensive-step backoff"
  end
  if validGameplaySeconds >= 300 then
    recommendation = costlySteps >= 3
      and "Consider +10 sensitivity: repeated expensive helper steps"
      or "No tuning change supported by current evidence"
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap = nil
  growthMiBps = 0
  testState = "idle"
  workloadState, recentFrameMs = "HEAVY", config.baselineFrameMs
  heavyFor, busyFor, easierFor, relaxedFor = 0, 0, 0, 0
  skipOwnStepFrame = false
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC")
  if shown then
    ImGui.Text(string.format("Lua heap %.1f MiB  |  net growth %+.2f MiB/s",
      heapMiB, growthMiBps))

    if testState == "idle" then
      local nextEnabled, changed = ImGui.Checkbox("Automatic assistance", assistanceEnabled)
      if changed then
        assistanceEnabled = nextEnabled == true
        savePreferences()
      end
      local nextSensitivity = ImGui.SliderInt("Workload sensitivity", sensitivity, 0, 100)
      if type(nextSensitivity) == "number" and nextSensitivity ~= sensitivity then
        sensitivity = clamp(math.floor(nextSensitivity), 0, 100)
        savePreferences()
      end
      local medium, heavy, relaxed = limits()
      ImGui.Text(string.format("Workload: %s | recent %.1f ms | slow %.0f%%",
        workloadState, recentFrameMs, slowFrameShare * 100))
      ImGui.Text(string.format("Boundaries: moderate %.1f / heavy %.1f / relaxed %.1f ms",
        medium, heavy, relaxed))
      ImGui.Text("Maintenance 50 KiB / 500 ms; moderate: 2s ON / 3s OFF")
      ImGui.Text("Recommendation: " .. recommendation)
      ImGui.Text("Preferences: " .. settingsStatus)
    else
      ImGui.Text("Developer test override; normal: 50 KiB / 500 ms")
    end
    ImGui.Text(string.format("Steps %d | last %d KiB | cycles %d",
      stepCount, lastStepKiB, completedCycles))
    ImGui.Text(string.format("GC requested %.2f MiB | step heap drop ~%.2f MiB",
      totalRequestedKiB / 1024, totalHeapDropKiB / 1024))
    ImGui.Text(string.format("Step CPU %.3f ms current / %.3f ms previous | max %.3f",
      lastCpuMs, previousCpuMs, maximumCpuMs))
    ImGui.Text(string.format("Observed spacing %.0f ms last / %.0f ms previous",
      lastIntervalMs, previousIntervalMs))
    ImGui.Text("Recovery: 3s partial / 5s relaxed; no catch-up")

    if testState == "idle" then
      if ImGui.Button("DEV: Start 90s A/B test") then startTest() end
    elseif testState == "armed" then
      ImGui.Text("TEST ARMED: close CET overlay to start 10s countdown")
      if ImGui.Button("Cancel test") then restoreSettings() end
    elseif testState == "countdown" then
      ImGui.Text(string.format("TEST: starting in %.0f s (after closing overlay)",
        math.max(0, 10 - countdownAge)))
      if ImGui.Button("Cancel test") then restoreSettings() end
    elseif testState == "done" or testState == "failed" then
      ImGui.Text(testState == "failed" and
        "TEST ABORTED: stop capture. Assistance is OFF." or
        "TEST DONE: stop capture. Assistance is OFF.")
      ImGui.Text(string.format("100ms steps %d | 50ms steps %d",
        testSteps[2], testSteps[3]))
      if ImGui.Button("Restore manual settings") then restoreSettings() end
    else
      local phaseName = testState == "off" and "OFF"
        or testState == "slow" and "128 KiB / 100ms"
        or "128 KiB / 50ms"
      ImGui.Text(string.format("TEST: %s | %.0f / 30 s",
        phaseName, phaseAge))
      if ImGui.Button("Abort test") then
        announce("GGC_TEST_ABORTED", "90s test cancelled by user.")
        restoreSettings()
      end
    end

    if rejectedSteps > 0 then
      ImGui.Text(string.format("Failed GC steps: %d", rejectedSteps))
    end
    if lastBlock ~= "running" then
      ImGui.Text("Status: " .. lastBlock)
    end
  end
  ImGui.End()
end)
""";
}