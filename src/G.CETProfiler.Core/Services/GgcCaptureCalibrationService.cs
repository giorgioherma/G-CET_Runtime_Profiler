using System.Text.Json;
using System.Text.Json.Nodes;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Interprets the ONE capture selected for the current Resolver run.
/// This is measured evidence feeding the one source-owned G-GC generator. The
/// capture alone is not proof of safe runtime GC step wall cost.
/// </summary>
internal static class GgcCaptureCalibrationService
{
    internal static JsonNode Build(JsonElement handoff, string captureName)
    {
        static double? Number(JsonElement element, string name)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(name, out var value) ||
                value.ValueKind != JsonValueKind.Number ||
                !value.TryGetDouble(out var number) ||
                !double.IsFinite(number))
                return null;
            return number;
        }

        static JsonElement Child(JsonElement parent, string name) =>
            parent.ValueKind == JsonValueKind.Object &&
            parent.TryGetProperty(name, out var child) ? child : default;

        var hitch = Child(handoff, "hitchPressure");
        var tolerance = Number(hitch, "toleranceFrameMs");
        var medianFrame = Number(hitch, "baselineFrameMs");
        var validFrames = tolerance is > 0 and < 10000 &&
                          medianFrame is > 0 and < 10000 &&
                          tolerance >= medianFrame;

        // Slow-step snapshots are NOT the instant LuaJIT's natural GC cycle
        // began. They only provide an approximate observed pressure region.
        // Do not invent cycle-trigger timing, live retained heap, or RAM size.
        var collector = Child(handoff, "garbageCollection");
        var snapshots = Child(collector, "slowOperations");
        var observedHeap = new List<double>();
        if (snapshots.ValueKind == JsonValueKind.Array)
            foreach (var step in snapshots.EnumerateArray())
            {
                var heap = Number(step, "heapBeforeMiB");
                var duration = Number(step, "wallMs");
                if (heap is > 0 and < 1048576 && duration is > 0)
                    observedHeap.Add(heap.Value);
            }

        observedHeap.Sort();
        static double Percentile(List<double> values, double p)
        {
            var index = (values.Count - 1) * p;
            var lo = (int)Math.Floor(index);
            var hi = (int)Math.Ceiling(index);
            return values[lo] + (values[hi] - values[lo]) * (index - lo);
        }

        double? medianHeap = observedHeap.Count > 0 ? Percentile(observedHeap, 0.5) : null;
        double? lowerHeap = observedHeap.Count > 0 ? Percentile(observedHeap, 0.25) : null;
        double? upperHeap = observedHeap.Count > 0 ? Percentile(observedHeap, 0.75) : null;
        // Slow GC snapshots are optional FORECAST CONTEXT, never mandatory
        // admission for the constant helper. Capture-derived frame headroom
        // is the prerequisite; no observed collector cycle is not a defect.
        var observedRegion = observedHeap.Count > 0;
        var evidenceValid = validFrames;

        // Measured frame tolerance sets the ceiling, never the demanded GC
        // work. The starting controls are deliberately conservative heuristics
        // proportional to AVAILABLE headroom after a substantial reserve.
        // Actual request size is decided later from the live heap.
        var frameRoom = validFrames ? tolerance!.Value - medianFrame!.Value : 0.0;
        var reserve = validFrames ? Math.Max(4.0, frameRoom * 0.60) : 0.0;
        var usableRoom = Math.Max(0.0, frameRoom - reserve);
        var roomFraction = validFrames
            ? Math.Clamp(usableRoom / (tolerance!.Value * 0.20), 0.0, 1.0)
            : 0.0;
        // ANALYZE has not generated anything yet. It may report the original
        // measured CET callback cost, but must NOT pretend the entire stack
        // receives a 50% reduction. Only GENERATE knows which source-proven
        // semantic stages actually composed successfully.
        var capture = Child(handoff, "capture");
        var originalCetMsPerSecond = Number(capture, "exclusiveMsPerSecond");
        var hasCetWorkload = originalCetMsPerSecond is >= 0 and <= 1000;

        return JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1,
            source = "SELECTED_PROFILER_CAPTURE",
            capture = captureName,
            status = !evidenceValid
                ? "INSUFFICIENT_MEASURED_FRAME_EVIDENCE"
                : observedRegion ? "MEASURED_EXPERIMENTAL_CALIBRATION"
                : "MEASURED_FRAME_ALLOWANCE_GC_REGION_UNOBSERVED",
            available = evidenceValid,
            gameplay = new
            {
                medianFrameMs = validFrames ? medianFrame : null,
                hitchToleranceMs = validFrames ? tolerance : null,
                toleranceSource = validFrames
                    ? "CAPFRAMEX_HITCH_PRESSURE_ADAPTIVE_THRESHOLD"
                    : "NOT_MEASURED",
                // The available tolerance is a frame-stall boundary, NOT all
                // spare CPU time that the controller is allowed to spend.
                headroomReserveRequired = true
            },
            gcPressure = new
            {
                observedSlowStepSamples = observedHeap.Count,
                observedHeapMedianMiB = medianHeap,
                observedHeapP25MiB = lowerHeap,
                observedHeapP75MiB = upperHeap,
                interpretation = observedRegion
                    ? "OPTIONAL_SLOW_STEP_HEAP_REGION_NOT_EXACT_NATURAL_TRIGGER"
                    : "NOT_OBSERVED_FORECAST_OPTIONAL"
            },
            assistance = new
            {
                policy = "MEASURED_CET_WORK_APPLIED_SOURCE_PROOF_ESTIMATES_DEFAULTS",
                frameReserveMs = validFrames ? reserve : (double?)null,
                usableFrameMarginMs = validFrames ? usableRoom : (double?)null,
                measuredOriginalCetMsPerSecond = hasCetWorkload
                    ? originalCetMsPerSecond : null,
                estimatedCoveredCetMsPerSecond = (double?)null,
                estimatedOptimizedCetMsPerSecond = (double?)null,
                estimatedFreedCetMsPerSecond = (double?)null,
                estimatedGgcStartingCpuMsPerSecond = (double?)null,
                historicalCoveredWorkReductionPercent = 50,
                conservativeCreditingFractionPercent = 50,
                estimatedSavingsAllocatedPercent = 8,
                recommendedIntensityPercent = evidenceValid ? 0 : (int?)null,
                recommendedMinFrequencyMs = evidenceValid ? 5000 : (int?)null,
                calibrationStatus = !hasCetWorkload
                    ? "CET_WORKLOAD_NOT_MEASURED_DEFAULTS_OFF"
                    : "AWAITING_ACTUALLY_COMPOSED_SOURCE_PROVEN_RULES"
            },
            systemInstalledRamMiB = (double?)null,
            systemRamStatus = "NOT_MEASURED_IN_RESOLVER_HANDOFF",
            control = new
            {
                intensityAndFrequencyAreRuntimeAdjustable = true,
                enabledByDefault = false, // Only source-proven GENERATE may recommend ON.
                manualOverridesNotCappedByEstimatedSaving = true,
                runtimeCpuProbingRequired = false
            },
            generator = new
            {
                controllerRuntimeAvailable = true,
                installAuthorized = evidenceValid,
                reason = !evidenceValid
                    ? "MISSING_MEASURED_FRAME_TOLERANCE"
                    : observedRegion
                        ? "CAPTURE_FRAME_ALLOWANCE_WITH_OPTIONAL_GC_FORECAST_EXPERIMENTAL"
                        : "CAPTURE_FRAME_ALLOWANCE_WITHOUT_GC_FORECAST_EXPERIMENTAL"
            }
        })!;
    }
    /// <summary>
    /// GENERATE-time, source-proven provisional initial settings. Credit only
    /// measured callback work linked to a SEMANTIC_RULE actually applied by
    /// the authoritative composed pass. The measured owner budget can never
    /// be counted twice, and unknown/unoptimized/previously complete work
    /// receives zero claimed savings.
    /// </summary>
    internal sealed record AppliedDefaults(
        double? MeasuredOriginalCetMsPerSecond,
        double MeasuredCoveredCetMsPerSecond,
        double EstimatedFreedCetMsPerSecond,
        double? EstimatedOptimizedCetMsPerSecond,
        double? EstimatedGgcStartingCpuMsPerSecond,
        int CreditedAppliedRules,
        int CreditedOwners,
        int DefaultIntensityPercent,
        int DefaultFrequencyMs,
        string EstimateStatus);

    internal static AppliedDefaults EstimateAppliedDefaults(
        JsonElement resolverRoot, IReadOnlyCollection<object> appliedManifest)
    {
        static double? Number(JsonElement element, string key)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(key, out var v) ||
                v.ValueKind != JsonValueKind.Number ||
                !v.TryGetDouble(out var number) ||
                !double.IsFinite(number))
                return null;
            return number;
        }

        if (!resolverRoot.TryGetProperty("gGcCaptureCalibration", out var evidence) ||
            !evidence.TryGetProperty("assistance", out var assist) ||
            !evidence.TryGetProperty("gameplay", out var gameplay))
            return new(null, 0, 0, null, null, 0, 0, 0, 5000,
                "MISSING_RESOLVER_CAPTURE_CALIBRATION");

        var original = Number(assist, "measuredOriginalCetMsPerSecond");
        var baseline = Number(gameplay, "medianFrameMs");
        var tolerance = Number(gameplay, "hitchToleranceMs");
        var reserve = Number(assist, "frameReserveMs");
        var coveredByOwner = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);
        var creditedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (original is > 0 and <= 1000)
        {
            foreach (var item in appliedManifest)
            {
                var stage = JsonSerializer.SerializeToElement(item);
                if (stage.ValueKind != JsonValueKind.Object ||
                    !stage.TryGetProperty("type", out var type) ||
                    type.GetString() != "SEMANTIC_RULE" ||
                    !stage.TryGetProperty("sourceProof", out var proof) ||
                    proof.ValueKind != JsonValueKind.True ||
                    !stage.TryGetProperty("admissionEvidence", out var admission) ||
                    admission.GetString() != "CAPTURE_MATCHED_SOURCE_PROVEN" ||
                    !stage.TryGetProperty("runtimeMsPerSecond", out var cost) ||
                    cost.ValueKind != JsonValueKind.Number ||
                    !cost.TryGetDouble(out var measuredCost) ||
                    !double.IsFinite(measuredCost) ||
                    measuredCost <= 0 || measuredCost > original)
                    continue;

                var owner = stage.TryGetProperty("Owner", out var ownerUpper)
                    ? ownerUpper.GetString()
                    : stage.TryGetProperty("owner", out var ownerLower)
                        ? ownerLower.GetString() : null;
                if (string.IsNullOrWhiteSpace(owner))
                    continue;
                // Multiple rules for the same owner can refer to the same
                // measured callback or nested work. Max, never SUM.
                if (!coveredByOwner.TryGetValue(owner, out var previous) ||
                    measuredCost > previous)
                    coveredByOwner[owner] = measuredCost;
                var rule = stage.TryGetProperty("RuleId", out var upperRule)
                    ? upperRule.GetString()
                    : stage.TryGetProperty("ruleId", out var lowerRule)
                        ? lowerRule.GetString() : null;
                if (!string.IsNullOrWhiteSpace(rule))
                    creditedRules.Add(owner + "\0" + rule);
            }
        }

        var covered = original is > 0
            ? Math.Min(original.Value, coveredByOwner.Values.Sum())
            : 0;
        // 50% is the user's historical SEMANTIC improvement reference,
        // NOT a promise for every mod. Credit only half of that for measured,
        // genuinely generated candidate work: at most 25% of covered work.
        // All unsupported/unmeasured/generic-only work stays unchanged.
        var saved = Math.Round(covered * 0.50 * 0.50, 6);
        var projected = original is >= 0
            ? Math.Max(0, original.Value - saved) : (double?)null;
        var startingReference = original is >= 0 ? saved * 0.08 : (double?)null;

        var usableFrame = baseline is > 0 && tolerance > baseline && reserve >= 0
            ? Math.Max(0, tolerance.Value - baseline.Value - reserve.Value) : 0;
        var risk = tolerance is > 0
            ? Math.Clamp(usableFrame / Math.Max(1, tolerance.Value * 0.12), 0, 1)
            : 0;
        var hasCreditableWork = saved > 0 && risk > 0;
        var basis = hasCreditableWork ? startingReference!.Value * risk : 0.0;
        var defaultIntensity = hasCreditableWork
            ? (int)Math.Clamp(Math.Round(12 + 4 * basis), 1, 80) : 0;
        var defaultFrequency = hasCreditableWork
            ? (int)(50 * Math.Round(
                Math.Clamp(1700 - 100 * basis, 300, 3000) / 50)) : 5000;
        var status = original is null
            ? "NO_MEASURED_EXCLUSIVE_CET_WORK"
            : coveredByOwner.Count == 0
                ? "NO_NEW_APPLIED_MEASURED_SEMANTIC_WORK_DEFAULT_OFF"
                : !hasCreditableWork
                    ? "NO_SAFE_STARTING_FRAME_MARGIN_DEFAULT_OFF"
                    : "HEURISTIC_GENERATION_PROVEN_COVERED_WORK_DEFAULTS_ONLY";
        return new(original, Math.Round(covered, 6), saved,
            projected, startingReference, creditedRules.Count,
            coveredByOwner.Count, defaultIntensity, defaultFrequency, status);
    }

}
