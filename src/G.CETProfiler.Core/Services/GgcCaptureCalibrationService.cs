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
        var canAssist = evidenceValid && usableRoom > 0;
        var permittedBudgetMsPerSecond = canAssist
            ? Math.Clamp(frameRoom * 0.12, 0.15, 2.0) : 0.0;
        var recommendedIntensity = canAssist
            ? (int)Math.Round(20 + 25 * roomFraction) : 0;
        var recommendedFrequencyMs = canAssist
            ? (int)(50 * Math.Round((1700 - 700 * roomFraction) / 50)) : 5000;

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
                policy = "CAPTURE_DEFINES_CEILING_LIVE_HEAP_DEFINES_DEMAND",
                frameReserveMs = validFrames ? reserve : (double?)null,
                usableFrameMarginMs = validFrames ? usableRoom : (double?)null,
                maxAssistCpuMsPerSecond = evidenceValid
                    ? permittedBudgetMsPerSecond : (double?)null,
                recommendedIntensityPercent = evidenceValid
                    ? recommendedIntensity : (int?)null,
                recommendedMinFrequencyMs = evidenceValid
                    ? recommendedFrequencyMs : (int?)null,
                calibrationStatus = "PROVISIONAL_NOT_MEASURED_SAFE_GC_STEP_COST"
            },
            systemInstalledRamMiB = (double?)null,
            systemRamStatus = "NOT_MEASURED_IN_RESOLVER_HANDOFF",
            control = new
            {
                intensityAndFrequencyAreRuntimeAdjustable = true,
                enabledByDefault = canAssist,
                rateLimitAndMeasuredStepCostRequired = true
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
}
