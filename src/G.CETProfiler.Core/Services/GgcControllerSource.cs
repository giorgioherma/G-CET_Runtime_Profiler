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
        // Generated sources are recognized by exact current or prior emitted source.
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

        // Distinguish an actual recognized installed-runtime upgrade from
        // calibration-only support refreshes and first-time support emission.
        // CompatibleLive above already proved the installed source's identity.
        var runtimeFile = Path.Combine(modsRoot,
            RuntimePath.Replace('/', Path.DirectorySeparatorChar));
        var runtimeUpgrade = File.Exists(runtimeFile) &&
            File.ReadAllText(runtimeFile).Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() !=
                Runtime.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

        var appliedStages = manifest
            .Select(x => JsonSerializer.SerializeToElement(x)).ToArray();
        static bool IsStage(JsonElement row, string kind) =>
            row.ValueKind == JsonValueKind.Object &&
            row.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            type.GetString() == kind;
        var consolidatedCallbacks = appliedStages.Count(row =>
            IsStage(row, "FRAME_DISPATCH_CONSOLIDATION"));
        var appliedNativeReadStages = appliedStages.Count(row =>
            IsStage(row, "NATIVE_READ_REUSE"));
        var appliedNativeGetterSites = appliedStages
            .Where(row => IsStage(row, "NATIVE_READ_REUSE"))
            .Sum(row => row.TryGetProperty("replacedOccurrences", out var value) &&
                        value.ValueKind == JsonValueKind.Number &&
                        value.TryGetInt32(out var count) && count > 0
                ? count : 0);
        var sourceProvenSemanticRules = appliedStages.Count(row =>
            IsStage(row, "SEMANTIC_RULE"));

        staged[RuntimePath] = Encoding.UTF8.GetBytes(Runtime);
        staged[CalibrationPath] = Encoding.UTF8.GetBytes(calibration);
        appliedTransformsByFile[RuntimePath] = 0;
        appliedTransformsByFile[CalibrationPath] = 0;
        manifest.Add(new
        {
            type = "GC_CONTROLLER",
            owner = "G-GC",
            stage = "SELECTABLE_CADENCE_RUNTIME_V12",
            runtimeUpgrade,
            files = new[] {RuntimePath, CalibrationPath},
            captureEvidence = "CURRENT_CAPTURE_AND_GENERATED_SOURCE_PROOF",
            control = "PLAYER_SELECTED_STEP_AND_INTERVAL_NO_WORKLOAD_GATE",
            measuredOriginalCetMsPerSecond = defaults.MeasuredOriginalCetMsPerSecond,
            estimatedCoveredCetMsPerSecond = defaults.EstimatedCoveredCetMsPerSecond,
            estimatedFreedCetMsPerSecond = defaults.EstimatedFreedCetMsPerSecond,
            estimatedOptimizedCetMsPerSecond = defaults.EstimatedOptimizedCetMsPerSecond,
            estimatedSemanticSavingMsPerSecond = defaults.EstimatedSemanticSavingMsPerSecond,
            estimatedGenericSavingMsPerSecond = defaults.EstimatedGenericSavingMsPerSecond,
            defaults.AppliedSemanticRules,
            defaults.AppliedGenericRules,
            defaults.CreditedOwners,
            defaultStepKiB = 50,
            defaultStepIntervalMs = 3000,
            selectableStepKiB = new[] { 0, 1, 10, 50, 200 },
            selectableIntervalMs = new[] { 0, 10000, 5000, 3000, 2000, 1000, 500, 300, 200, 100, 50 },
            defaults.EstimateStatus,
            estimateModel = "CAPTURE_SCOPED_APPLIED_CALLBACK_SCOPE_V2_HEURISTIC",
            estimateConfidence = "UNVALIDATED_FORECAST_REQUIRES_POST_PASS_CAPTURE",
            appliedFrameCallbacksConsolidated = consolidatedCallbacks,
            appliedNativeReadStages,
            appliedNativeGetterSites,
            appliedSourceProvenSemanticRules = sourceProvenSemanticRules,
            creditedMeasuredGenericCallbackRegistrations = defaults.RuleEstimates
                .Where(item => item.Stage == "GENERIC" && item.RegistrationId is > 0 &&
                    item.EstimatedSavingMsPerSecond > 0)
                .Select(item => (item.Owner, item.RegistrationId))
                .Distinct().Count(),
            captureRuntimeInterpretation = resolverRoot.TryGetProperty(
                "captureRuntimeInterpretation", out var runtimeInterpretation)
                ? runtimeInterpretation.Clone() : (JsonElement?)null,
            measuredCaptureCetMsPerSecond = defaults.MeasuredOriginalCetMsPerSecond,
            measuredOptimizedRuntime = GgcCaptureCalibrationService
                .RecognizesPredominantlyOptimizedStack(resolverRoot),
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

        if (Field(installed, "schema") != "4")
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
            foreach (var legacyVersion in new[] { "V1", "V2", "V3", "V4", "V5", "V6", "V7", "V8", "V9", "V10", "V11" })
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
                "schema = 4," => 4,
                "schema = 3," => 3,
                "schema = 2," => 2,
                "schema = 1," => 1,
                _ => 0
            } : 0;
        string[] keys = version == 4
            ? [
                "schema", "capture", "baselineFrameMs", "hitchToleranceMs",
                "pressureReferenceMiB", "pressureStartMiB", "pressureHighMiB",
                "pressureSampleCount", "frameReserveMs",
                "estimatedOriginalCetMsPerSecond",
                "estimatedCoveredCetMsPerSecond",
                "estimatedOptimizedCetMsPerSecond",
                "estimatedFreedCetMsPerSecond",
                "estimatedSemanticSavingMsPerSecond",
                "estimatedGenericSavingMsPerSecond"
              ]
            : version == 3
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
                      (version < 4 && keys[i] == "estimatedGgcStartingCpuMsPerSecond")))
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
               (missingWorkFields == 0 ||
                missingWorkFields == (version >= 4 ? 3 : 4));
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
        var hasMeasuredWork = defaults.MeasuredOriginalCetMsPerSecond is >= 0 &&
            defaults.EstimatedOptimizedCetMsPerSecond is >= 0;
        var originalCet = defaults.MeasuredOriginalCetMsPerSecond ?? 0;
        var optimizedCet = defaults.EstimatedOptimizedCetMsPerSecond ?? 0;
        var freedCet = defaults.EstimatedFreedCetMsPerSecond;
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
                Math.Abs(optimizedCet - (originalCet - freedCet)) > 0.00001)))
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
        // Resolver supplies captured workload evidence only; maintenance
        // step size, interval and optional workload protection are player-owned.
        capture = capture.Replace("\\", "_").Replace("\"", "_")
            .Replace("\n", "_").Replace("\r", "_");

        static string N(double value) =>
            value.ToString("0.######", CultureInfo.InvariantCulture);
        lua = $$"""
-- GGC_CAPTURE_CALIBRATION_V1
-- Generated by G-CET Resolver from the current capture. Do not edit;
-- a new Resolver pass replaces this exact configuration.
return {
  schema = 4,
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
  estimatedSemanticSavingMsPerSecond = {{N(defaults.EstimatedSemanticSavingMsPerSecond)}},
  estimatedGenericSavingMsPerSecond = {{N(defaults.EstimatedGenericSavingMsPerSecond)}},
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
-- Single Resolver-generated G-GC runtime. No Profiler DLL required.
-- Manual incremental GC requests; OFF is an interval, NOT a step size.
-- A 0 KiB step still advances LuaJIT's GC. Requested KiB is not reclaimed memory.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 4
    or type(config.hitchToleranceMs) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local function clamp(x, lo, hi)
  return math.max(lo, math.min(hi, x))
end

local stepSizes = {0, 1, 10, 50, 200}
local stepLabels = {"0 KiB", "1 KiB", "10 KiB", "50 KiB", "200 KiB"}
local intervals = {0, 10000, 5000, 3000, 2000, 1000, 500, 300, 200, 100, 50}
local function choiceIndex(values, value)
  for i = 1, #values do
    if values[i] == value then return i end
  end
  return nil
end

local function safeMigratedSize(value)
  if choiceIndex(stepSizes, value) then return value end
  for _, size in ipairs(stepSizes) do
    if size >= value then return size end
  end
  return stepSizes[#stepSizes]
end

local function safeMigratedInterval(value)
  if value == 0 then return 0 end
  -- Round old unsupported intervals upward (never more frequent) where
  -- possible. Old 20s/30s choices use the new slowest 10s option.
  for i = #intervals, 2, -1 do
    if intervals[i] >= value then return intervals[i] end
  end
  return intervals[2]
end

local manualKiB, manualIntervalMs = 50, 3000
local settingsStatus = "defaults"
local function savePreferences()
  local ok = pcall(function()
    local file = assert(io.open("user_settings.txt", "w"))
    file:write(string.format("v3 %d %d\n", manualKiB, manualIntervalMs))
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
  local size, interval = data:match("^v3%s+(%d+)%s+(%d+)%s*$")
  size, interval = tonumber(size), tonumber(interval)
  if choiceIndex(stepSizes, size) and choiceIndex(intervals, interval) then
    manualKiB, manualIntervalMs = size, interval
    settingsStatus = "restored"
    return
  end
  -- V2 had a third automatic-protection flag. Ignore it completely:
  -- workload-based inhibition is not part of G-GC any more.
  local oldSize, oldInterval = data:match("^v2%s+(%d+)%s+(%d+)%s+[01]%s*$")
  oldSize, oldInterval = tonumber(oldSize), tonumber(oldInterval)
  if oldSize and oldInterval and oldSize >= 50 and oldSize <= 8192
      and oldInterval >= 0 and oldInterval <= 30000 then
    manualKiB = safeMigratedSize(oldSize)
    manualIntervalMs = safeMigratedInterval(oldInterval)
    savePreferences()
    settingsStatus = "v2 preferences migrated"
    return
  end
  -- Pre-V2 sensitivity/enable setting: preserve the explicit OFF choice.
  local percent, enabled = data:match("^(%d+)%s+([01])%s*$")
  percent = tonumber(percent)
  if percent and percent >= 0 and percent <= 100 then
    manualIntervalMs = enabled == "1" and 3000 or 0
    savePreferences()
    settingsStatus = "legacy preferences migrated"
  end
end
loadPreferences()

local overlayOpen = false
local heapSampleAge, sinceStep, sinceActualStep = 0, 0, 0
local haveCompletedStep = false
local heapMiB, previousHeap, growthMiBps = 0, nil, 0
local sampledLowMiB, sampledHighMiB = nil, nil
local activeWorld = false
local stepCount, helperCompletedCycles, rejectedSteps = 0, 0, 0
local lastStepKiB = 0
local lastCpuMs, previousCpuMs, maximumCpuMs, totalCpuMs = 0, 0, 0, 0
local lastIntervalMs, previousIntervalMs = 0, 0
local enabledGameplaySeconds = 0
local lastBlock = "awaiting heap sample"

local function observe(seconds)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    activeWorld = false
    return
  end
  heapMiB = kib / 1024
  sampledLowMiB = sampledLowMiB and math.min(sampledLowMiB, heapMiB) or heapMiB
  sampledHighMiB = sampledHighMiB and math.max(sampledHighMiB, heapMiB) or heapMiB
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

local function tick(dt)
  if type(dt) ~= "number" or dt ~= dt or dt <= 0 or dt > 0.50 then
    lastBlock = "invalid update"
    return
  end
  heapSampleAge = heapSampleAge + dt
  sinceActualStep = sinceActualStep + dt
  if heapSampleAge >= 0.75 then
    local seconds = heapSampleAge
    heapSampleAge = 0
    observe(seconds)
  end
  if previousHeap == nil then
    sinceStep = 0
    lastBlock = "awaiting heap sample"
    return
  end
  if not activeWorld then
    sinceStep = 0
    lastBlock = "outside gameplay"
    return
  end
  if manualIntervalMs == 0 then
    sinceStep = 0
    lastBlock = "OFF (no GC calls)"
    return
  end
  -- Valid gameplay only; no frame-rate workload classification, automatic
  -- HEAVY suspension, costly-step backoff, or catch-up bursts.
  sinceStep = sinceStep + dt
  enabledGameplaySeconds = enabledGameplaySeconds + dt
  if sinceStep * 1000 < manualIntervalMs then
    lastBlock = "waiting for selected interval"
    return
  end

  sinceStep = 0
  local t0 = os.clock()
  local ok, finished = pcall(collectgarbage, "step", manualKiB)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  previousCpuMs, lastCpuMs = lastCpuMs, cpuMs
  maximumCpuMs = math.max(maximumCpuMs, cpuMs)
  if not ok then
    rejectedSteps = rejectedSteps + 1
    manualIntervalMs = 0
    savePreferences()
    lastBlock = "GC error / disabled"
    return
  end

  previousIntervalMs = lastIntervalMs
  lastIntervalMs = haveCompletedStep and sinceActualStep * 1000 or 0
  sinceActualStep, haveCompletedStep = 0, true
  lastStepKiB = manualKiB
  stepCount = stepCount + 1
  totalCpuMs = totalCpuMs + cpuMs
  -- A true return means THIS helper call completed a cycle. Natural
  -- LuaJIT cycles are unobserved here, so zero is not zero overall.
  if finished == true then helperCompletedCycles = helperCompletedCycles + 1 end
  lastBlock = "running"
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap = nil
  heapSampleAge, sinceStep, sinceActualStep = 0, 0, 0
  haveCompletedStep = false
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC")
  if shown then
    ImGui.Text(string.format("Sampled Lua heap: %.1f MiB | net trend %+.2f MiB/s",
      heapMiB, growthMiBps))
    if sampledLowMiB ~= nil then
      ImGui.Text(string.format("Sampled heap low / high: %.1f / %.1f MiB",
        sampledLowMiB, sampledHighMiB))
    end
    ImGui.Text(string.format("Requested GC step argument: %d KiB", manualKiB))
    for i, size in ipairs(stepSizes) do
      if i > 1 then ImGui.SameLine() end
      if ImGui.Button(stepLabels[i]) and manualKiB ~= size then
        manualKiB, sinceStep, sinceActualStep = size, 0, 0
        haveCompletedStep = false
        savePreferences()
      end
    end
    local intervalIndex = choiceIndex(intervals, manualIntervalMs) or 1
    local intervalLabel = manualIntervalMs == 0 and "OFF" or
      string.format("%d ms", manualIntervalMs)
    ImGui.Text("Collection interval: " .. intervalLabel)
    local nextIndex = ImGui.SliderInt("##Collection interval", intervalIndex, 1, #intervals)
    if type(nextIndex) == "number" and nextIndex ~= intervalIndex then
      manualIntervalMs = intervals[clamp(math.floor(nextIndex), 1, #intervals)]
      sinceStep, sinceActualStep, haveCompletedStep = 0, 0, false
      savePreferences()
    end
    ImGui.Text("OFF disables calls. 0 KiB STILL requests a LuaJIT GC step.")
    ImGui.Text("Selected cadence is not modified by FPS or workload.")
    ImGui.Text(string.format("Successful helper calls: %d | rejected: %d",
      stepCount, rejectedSteps))
    ImGui.Text(string.format("Cycles completed BY helper calls: %d",
      helperCompletedCycles))
    ImGui.Text("Natural LuaJIT cycles: NOT MEASURED by helper")
    ImGui.Text(string.format("Last GC request: %d KiB (not bytes reclaimed)",
      lastStepKiB))
    ImGui.Text(string.format("Approx step CPU current / previous: %.3f / %.3f ms",
      lastCpuMs, previousCpuMs))
    ImGui.Text(string.format("Approx step CPU peak / sum: %.3f / %.3f ms",
      maximumCpuMs, totalCpuMs))
    ImGui.Text(string.format("Observed step spacing last / previous: %.0f / %.0f ms",
      lastIntervalMs, previousIntervalMs))
    local delivered = enabledGameplaySeconds > 0 and
      stepCount * 60 / enabledGameplaySeconds or 0
    ImGui.Text(string.format("Helper calls / min of enabled gameplay: %.1f", delivered))
    ImGui.Text("Step CPU excludes callback overhead; heap changes include natural GC.")
    ImGui.Text("Preferences: " .. settingsStatus)
    if lastBlock ~= "running" then ImGui.Text("Status: " .. lastBlock) end
  end
  ImGui.End()
end)

""";
}