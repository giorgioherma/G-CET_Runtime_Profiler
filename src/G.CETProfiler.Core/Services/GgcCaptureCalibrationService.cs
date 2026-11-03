using System.Text.Json;
using System.Text.Json.Nodes;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Interprets the ONE capture selected for the current Resolver run.
/// This is measured evidence for a future persistent G-GC controller, not a
/// GC policy, install permission, or a substitute for the native runtime.
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
        // Observed snapshots may be biased toward slow collector phases; no
        // exact GC-trigger region is asserted from these samples.
        var evidenceValid = validFrames && observedHeap.Count > 0;

        return JsonSerializer.SerializeToNode(new
        {
            schemaVersion = 1,
            source = "SELECTED_PROFILER_CAPTURE",
            capture = captureName,
            status = evidenceValid
                ? "MEASURED_EXPERIMENTAL_CALIBRATION"
                : "INSUFFICIENT_MEASURED_EVIDENCE",
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
                interpretation = "SLOW_STEP_HEAP_SNAPSHOTS_NOT_EXACT_NATURAL_TRIGGER"
            },
            systemInstalledRamMiB = (double?)null,
            systemRamStatus = "NOT_MEASURED_IN_RESOLVER_HANDOFF",
            control = new
            {
                intensityAndFrequencyAreRuntimeAdjustable = true,
                enabledByDefault = false,
                rateLimitAndMeasuredStepCostRequired = true
            },
            generator = new
            {
                controllerRuntimeAvailable = false,
                installAuthorized = false,
                reason = "PERSISTENT_NATIVE_G_GC_RUNTIME_NOT_YET_BUILT_OR_VALIDATED"
            }
        })!;
    }
}
