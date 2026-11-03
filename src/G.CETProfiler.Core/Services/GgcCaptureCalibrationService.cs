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
    /// Conservative pre/post CET-work estimate from the ACTUAL composed
    /// transform manifest, not from eligible rules or a fixed mod inventory.
    /// All credit is speculative. Deduplicate callback registration work and
    /// do not add concurrent generic + semantic claims on the same owner.
    /// Unsupported and unmeasured sources contribute zero hypothetical saving.
    /// </summary>
    internal sealed record RuleEstimate(
        string Stage, string Rule, string Owner, long? RegistrationId,
        double MeasuredCallbackMsPerSecond, double EstimatedSavingMsPerSecond,
        double HeuristicFraction, string Evidence);

    internal sealed record AppliedDefaults(
        double? MeasuredOriginalCetMsPerSecond,
        double EstimatedCoveredCetMsPerSecond,
        double EstimatedFreedCetMsPerSecond,
        double? EstimatedOptimizedCetMsPerSecond,
        double? EstimatedGgcStartingCpuMsPerSecond,
        double EstimatedSemanticSavingMsPerSecond,
        double EstimatedGenericSavingMsPerSecond,
        int AppliedSemanticRules,
        int AppliedGenericRules,
        int CreditedOwners,
        int DefaultIntensityPercent,
        int DefaultFrequencyMs,
        string EstimateStatus,
        IReadOnlyList<RuleEstimate> RuleEstimates);

    internal static AppliedDefaults EstimateAppliedDefaults(
        JsonElement resolverRoot, IReadOnlyCollection<object> appliedManifest)
    {
        static double? Number(JsonElement item, string key)
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty(key, out var v) ||
                v.ValueKind != JsonValueKind.Number ||
                !v.TryGetDouble(out var number) ||
                !double.IsFinite(number))
                return null;
            return number;
        }

        static string? String(JsonElement item, string key) =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        static long? Id(JsonElement item) =>
            item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("registrationId", out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var id) && id > 0 ? id : null;

        static double SemanticFraction(string handler, string policy)
        {
            var family = handler + " " + policy;
            if (family.Contains("DORMAN", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("WAKE", StringComparison.OrdinalIgnoreCase))
                return 0.30;
            if (family.Contains("CADENCE", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("SCHEDUL", StringComparison.OrdinalIgnoreCase))
                return 0.22;
            if (family.Contains("FAST_PATH", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("POLL", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("GATE", StringComparison.OrdinalIgnoreCase))
                return 0.18;
            if (family.Contains("CACHE", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("NATIVE", StringComparison.OrdinalIgnoreCase) ||
                family.Contains("READ", StringComparison.OrdinalIgnoreCase))
                return 0.12;
            return 0.10;
        }

        static double GenericFraction(string kind) => kind switch
        {
            "UI_VISIBILITY_DORMANCY" => 0.20,
            "SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL" => 0.16,
            "HARD_DORMANT_GUARD_HOIST" => 0.14,
            "INTERACTION_UI_IDLE_GUARD" => 0.12,
            "CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD" => 0.12,
            "ACTION_OVERRIDE_EXACT_PREFILTER" => 0.10,
            "SHARED_PROVIDER_READ" => 0.025,
            _ when kind.StartsWith("ACTION_ROUTING", StringComparison.Ordinal) => 0.05,
            _ => 0.0
        };

        static AppliedDefaults Empty(string status, double? original) =>
            new(original, 0, 0, original, original is null ? null : 0,
                0, 0, 0, 0, 0, 0, 5000, status,
                Array.Empty<RuleEstimate>());

        if (!resolverRoot.TryGetProperty("gGcCaptureCalibration", out var evidence) ||
            !evidence.TryGetProperty("assistance", out var assistance) ||
            !evidence.TryGetProperty("gameplay", out var gameplay))
            return Empty("MISSING_RESOLVER_CAPTURE_CALIBRATION", null);
        var original = Number(assistance, "measuredOriginalCetMsPerSecond");
        var baseline = Number(gameplay, "medianFrameMs");
        var tolerance = Number(gameplay, "hitchToleranceMs");
        var reserve = Number(assistance, "frameReserveMs");
        if (original is null or < 0 or > 1000)
            return Empty("NO_MEASURED_EXCLUSIVE_CET_WORK", null);

        // These rows are already exclusive sampled callback costs. Each
        // registration is counted once, never per generic transformation.
        var measured = new Dictionary<(string Owner, long Id), (double Cpu, double Calls)>();
        if (resolverRoot.TryGetProperty("callbackFamilies", out var families) &&
            families.ValueKind == JsonValueKind.Array)
            foreach (var family in families.EnumerateArray())
            {
                if (!family.TryGetProperty("topConsumers", out var rows) ||
                    rows.ValueKind != JsonValueKind.Array) continue;
                foreach (var row in rows.EnumerateArray())
                {
                    var owner = String(row, "owner");
                    var id = Id(row);
                    if (string.IsNullOrWhiteSpace(owner) || id is null ||
                        !row.TryGetProperty("runtime", out var runtime)) continue;
                    var cost = Number(runtime, "exclusiveMsPerSecond");
                    var calls = Number(runtime, "callsPerSecond");
                    if (cost is null or < 0 || calls is null or < 0) continue;
                    var key = (owner.ToLowerInvariant(), id.Value);
                    if (!measured.TryGetValue(key, out var old) || cost > old.Cpu)
                        measured[key] = (cost.Value, calls.Value);
                }
            }

        var estimates = new List<RuleEstimate>();
        var appliedRules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var appliedGeneric = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var semanticByOwner = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var semanticMeasuredByOwner = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        // A registration can undergo multiple generic stages. Credit only
        // the single largest hypothesized saving for that registration.
        var genericByRegistration = new Dictionary<(string Owner, long Id), RuleEstimate>();
        var frameRegistrations = new Dictionary<(string Owner, string Event),
            List<(long Id, double Cpu, double Calls)>>();

        foreach (var item in appliedManifest)
        {
            var stage = JsonSerializer.SerializeToElement(item);
            if (stage.ValueKind != JsonValueKind.Object) continue;
            var kind = String(stage, "type") ?? "";
            var owner = String(stage, "Owner") ?? String(stage, "owner");
            if (string.IsNullOrWhiteSpace(owner)) continue;

            if (kind == "SEMANTIC_RULE")
            {
                var rule = String(stage, "RuleId") ?? String(stage, "ruleId") ?? "";
                var key = owner + "\0" + rule;
                if (rule.Length == 0 || !appliedRules.Add(key)) continue;
                var cost = Number(stage, "runtimeMsPerSecond");
                var provenance = String(stage, "admissionEvidence") ?? "NOT_MEASURED";
                var fraction = SemanticFraction(
                    String(stage, "Handler") ?? String(stage, "handler") ?? "",
                    String(stage, "PolicyClass") ?? String(stage, "policyClass") ?? "");
                var credible = provenance == "CAPTURE_MATCHED_SOURCE_PROVEN" &&
                    stage.TryGetProperty("sourceProof", out var proof) &&
                    proof.ValueKind == JsonValueKind.True &&
                    cost is > 0 && cost <= original;
                var saved = credible ? Math.Round(cost!.Value * fraction, 6) : 0;
                estimates.Add(new("SEMANTIC_RULE", rule, owner, null,
                    credible ? cost!.Value : 0, saved, fraction,
                    credible ? provenance : "NO_UNIQUE_MEASURED_APPLIED_WORK"));
                if (credible)
                {
                    semanticByOwner[owner] = Math.Max(
                        semanticByOwner.GetValueOrDefault(owner), saved);
                    semanticMeasuredByOwner[owner] = Math.Max(
                        semanticMeasuredByOwner.GetValueOrDefault(owner), cost!.Value);
                }
                continue;
            }

            var appliedId = Id(stage);
            if (appliedId is not null &&
                (kind == "FRAME_DISPATCH_CONSOLIDATION" ||
                 GenericFraction(kind) > 0))
                appliedGeneric.Add(owner + "\0" + kind + "\0" + appliedId);

            if (kind == "FRAME_DISPATCH_CONSOLIDATION")
            {
                var id = Id(stage);
                if (id is not null &&
                    measured.TryGetValue((owner.ToLowerInvariant(), id.Value),
                        out var measuredRow))
                {
                    var eventName = String(stage, "eventTarget") ?? "onUpdate";
                    var group = (owner.ToLowerInvariant(), eventName.ToLowerInvariant());
                    if (!frameRegistrations.TryGetValue(group, out var members))
                        frameRegistrations[group] = members = new();
                    if (members.All(x => x.Id != id.Value))
                        members.Add((id.Value, measuredRow.Cpu, measuredRow.Calls));
                }
            }

            var registration = Id(stage);
            var fractionGeneric = GenericFraction(kind);
            if (fractionGeneric <= 0 || registration is null ||
                !measured.TryGetValue((owner.ToLowerInvariant(), registration.Value),
                    out var callback)) continue;
            var actualSaving = Math.Round(callback.Cpu * fractionGeneric, 6);
            var entry = new RuleEstimate("GENERIC", kind, owner, registration,
                callback.Cpu, actualSaving, fractionGeneric,
                "ACTUALLY_APPLIED_CAPTURE_MATCHED_REGISTRATION_HEURISTIC");
            var registrationKey = (owner.ToLowerInvariant(), registration.Value);
            if (!genericByRegistration.TryGetValue(registrationKey, out var oldEntry) ||
                entry.EstimatedSavingMsPerSecond > oldEntry.EstimatedSavingMsPerSecond)
                genericByRegistration[registrationKey] = entry;
        }

        // Frame registration savings are dispatch overhead, NOT eliminated
        // callback bodies. Credit only registrations beyond one per owner/event,
        // and cap each at 5% of measured exclusive callback CPU. One 0-Engine
        // registration does not qualify as eliminated dispatch traffic.
        foreach (var (group, registrations) in frameRegistrations)
        {
            foreach (var callback in registrations
                .OrderByDescending(x => x.Calls).ThenBy(x => x.Id).Skip(1))
            {
                var saved = Math.Round(Math.Min(
                    callback.Cpu * 0.05,
                    callback.Calls * 0.002), 6); // 2 microseconds per avoided dispatch
                var entry = new RuleEstimate("GENERIC",
                    "FRAME_DISPATCH_CONSOLIDATION", group.Owner, callback.Id,
                    callback.Cpu, saved, callback.Cpu > 0 ? saved / callback.Cpu : 0,
                    "APPLIED_EXTRA_REGISTRATION_DISPATCH_ONLY_NOT_CALLBACK_BODY");
                var registrationKey = (group.Owner, callback.Id);
                if (!genericByRegistration.TryGetValue(registrationKey, out var oldEntry) ||
                    entry.EstimatedSavingMsPerSecond > oldEntry.EstimatedSavingMsPerSecond)
                    genericByRegistration[registrationKey] = entry;
            }
        }
        estimates.AddRange(genericByRegistration.Values);

        var owners = estimates.Select(x => x.Owner).Distinct(
            StringComparer.OrdinalIgnoreCase).ToArray();
        double semanticTotal = 0, genericTotal = 0;
        var creditedOwners = 0;
        foreach (var owner in owners)
        {
            var genericSaved = genericByRegistration.Values
                .Where(x => x.Owner.Equals(owner, StringComparison.OrdinalIgnoreCase))
                .Sum(x => x.EstimatedSavingMsPerSecond);
            var semanticSaved = semanticByOwner.GetValueOrDefault(owner);
            // Approximate measured owner ceiling from distinct registrations.
            // A semantic matched callback may not be among the topConsumer
            // rows, so its own captured cost is an admissible lower bound.
            var measuredOwner = Math.Max(
                measured.Where(x => x.Key.Owner.Equals(
                    owner, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.Value.Cpu),
                semanticMeasuredByOwner.GetValueOrDefault(owner));
            var credited = Math.Min(measuredOwner, Math.Max(
                semanticSaved, genericSaved)); // overlap is not additive
            if (credited > 0) creditedOwners++;
            if (semanticSaved >= genericSaved)
                semanticTotal += credited;
            else
                genericTotal += credited;
        }

        // A modest additional upper bound prevents overclaiming when
        // callback samples overlap at the owner, family, or native envelope.
        var savedTotal = Math.Round(Math.Min(original.Value * 0.35,
            semanticTotal + genericTotal), 6);
        var originalCredited = Math.Round(
            Math.Min(original.Value, estimates
                .Where(x => x.EstimatedSavingMsPerSecond > 0)
                .GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
                .Sum(group => group.Max(x => x.MeasuredCallbackMsPerSecond))), 6);
        var projected = Math.Round(Math.Max(0, original.Value - savedTotal), 6);
        var startingReference = Math.Round(savedTotal * 0.08, 6);
        var usableFrame = baseline is > 0 && tolerance > baseline && reserve >= 0
            ? Math.Max(0, tolerance.Value - baseline.Value - reserve.Value) : 0;
        var risk = tolerance is > 0
            ? Math.Clamp(usableFrame / Math.Max(1, tolerance.Value * 0.12), 0, 1)
            : 0;
        var usable = savedTotal > 0 && risk > 0;
        var basis = usable ? startingReference * risk : 0;
        var intensity = usable
            ? (int)Math.Clamp(Math.Round(12 + 4 * basis), 1, 80) : 0;
        var frequency = usable
            ? (int)(50 * Math.Round(
                Math.Clamp(1700 - 100 * basis, 300, 3000) / 50)) : 5000;
        var status = !usable
            ? savedTotal <= 0
                ? "NO_CREDITABLE_NEW_MEASURED_OPTIMIZATION_DEFAULT_OFF"
                : "NO_SAFE_INITIAL_FRAME_MARGIN_DEFAULT_OFF"
            : "CONSERVATIVE_ACTUALLY_GENERATED_RULE_HEURISTICS_DEFAULTS_ONLY";
        return new(original, originalCredited, savedTotal, projected,
            startingReference, Math.Round(semanticTotal, 6),
            Math.Round(genericTotal, 6), appliedRules.Count,
            appliedGeneric.Count, creditedOwners, intensity, frequency,
            status, estimates.OrderBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Stage).ThenBy(x => x.Rule).ToArray());
    }

}
