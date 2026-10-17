using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// G-HoneyTrap: native-location dormancy opportunity selection INSIDE the existing
/// Resolver. Source recognition, economic approval and overlay generation are
/// independent decisions. This first integration never rewrites user code.
/// </summary>
internal static class HoneyTrapService
{
    private sealed class Lane
    {
        public required string Owner { get; init; }
        public required string Job { get; init; }
        public string JobType { get; set; } = "";
        public double CallsPerSecond { get; set; }
        public double MeasuredNestedMsPerSecond { get; set; }
        public bool JobTokenFoundInOwnerSource { get; set; }
        public bool CallbackLocationLead { get; set; }
        public bool HasMatchedSemanticRule { get; set; }
        public bool SemanticLuaAlreadySatisfied { get; set; }
        public bool SemanticNativeStageComplete { get; set; }
    }

    private static bool Child(JsonElement parent, string name, out JsonElement child)
    {
        if (parent.ValueKind == JsonValueKind.Object)
            return parent.TryGetProperty(name, out child);
        child = default;
        return false;
    }

    private static bool Flag(JsonElement obj, string name) =>
        Child(obj, name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string String(JsonElement obj, string key) =>
        Child(obj, key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";

    private static double Number(JsonElement obj, string key)
    {
        if (Child(obj, key, out var v) && v.ValueKind == JsonValueKind.Number &&
            v.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0)
            return n;
        return 0;
    }

    private static bool LocationLead(JsonElement consumer)
    {
        if (!Child(consumer, "dormancy", out var dormancy))
            return false;
        if (String(dormancy, "Class") is "DISCOVERY_DORMANT" or "HARD_DORMANT")
            return true;
        if (Child(dormancy, "DiscoverySignals", out var signals) &&
            signals.ValueKind == JsonValueKind.Array && signals.GetArrayLength() > 0)
            return true;
        return false;
    }

    internal static object Analyze(JsonElement resolver)
    {
        // Reuse the Resolver's already source-linked scheduler evidence.
        // Do not reinterpret the shared 0-Engine callback or parent callback
        // time as an individual mod's scheduler cost.
        var byKey = new Dictionary<string, Lane>(StringComparer.OrdinalIgnoreCase);
        if (Child(resolver, "callbackFamilies", out var families) &&
            families.ValueKind == JsonValueKind.Array)
        {
            foreach (var family in families.EnumerateArray())
            {
                if (!Child(family, "topConsumers", out var consumers) ||
                    consumers.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var consumer in consumers.EnumerateArray())
                {
                    if (!Child(consumer, "existingOptimization", out var existing) ||
                        !Flag(existing, "sourceProven") ||
                        !Child(existing, "jobs", out var jobs) ||
                        jobs.ValueKind != JsonValueKind.Array)
                        continue;

                    var owner = String(consumer, "owner");
                    if (string.IsNullOrWhiteSpace(owner) ||
                        owner.Equals("0-Engine", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var lead = LocationLead(consumer);
                    var semanticMatched = Child(consumer, "semantic", out var semantic) &&
                        Flag(semantic, "Matched");
                    var luaComplete = semanticMatched &&
                        (Flag(semantic, "AlreadySatisfied") ||
                         (Child(semantic, "lanes", out var lanes) &&
                          Child(lanes, "lua", out var lua) &&
                          Flag(lua, "complete")));
                    var nativeComplete = semanticMatched &&
                        Child(semantic, "lanes", out var l) &&
                        Child(l, "native", out var n) &&
                        Flag(n, "complete");

                    foreach (var job in jobs.EnumerateArray())
                    {
                        var name = String(job, "Job");
                        if (string.IsNullOrWhiteSpace(name))
                            continue;
                        var key = owner + "\n" + name;
                        if (!byKey.TryGetValue(key, out var found))
                        {
                            found = new Lane
                            {
                                Owner = owner,
                                Job = name,
                                JobType = String(job, "JobType"),
                                JobTokenFoundInOwnerSource = true
                            };
                            byKey[key] = found;
                        }

                        // Same Scheduler job is reported under several owner
                        // callbacks: do NOT sum its nested CPU repeatedly.
                        found.CallsPerSecond = Math.Max(
                            found.CallsPerSecond, Number(job, "callsPerSecond"));
                        found.MeasuredNestedMsPerSecond = Math.Max(
                            found.MeasuredNestedMsPerSecond, Number(job, "msPerSecond"));
                        found.CallbackLocationLead |= lead;
                        found.HasMatchedSemanticRule |= semanticMatched;
                        found.SemanticLuaAlreadySatisfied |= luaComplete;
                        found.SemanticNativeStageComplete |= nativeComplete;
                    }
                }
            }
        }

        var rows = byKey.Values
            .OrderByDescending(x => x.MeasuredNestedMsPerSecond)
            .ThenBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Job, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                owner = x.Owner,
                job = x.Job,
                jobType = x.JobType,
                measuredCallsPerSecond = Math.Round(x.CallsPerSecond, 6),
                measuredNestedMsPerSecond = Math.Round(x.MeasuredNestedMsPerSecond, 6),
                sourceLink = "JOB_ID_QUOTED_IN_CURRENT_OWNER_SOURCE",
                locationLeadFromOwnerCallback = x.CallbackLocationLead,
                existingSemantic = new
                {
                    matched = x.HasMatchedSemanticRule,
                    luaStageComplete = x.SemanticLuaAlreadySatisfied,
                    nativeStageComplete = x.SemanticNativeStageComplete
                },
                status = x.SemanticLuaAlreadySatisfied
                    ? "ALREADY_OPTIMIZED_RESIDUAL_REVIEW"
                    : x.CallbackLocationLead ? "SOURCE_CALLEE_REVIEW"
                    : "UNPROVEN_LOCATION_DEPENDENCY",
                measurementMeaning =
                    "Measured nested scheduler CPU, not source-proven removable CPU.",
                approval = "NOT_ADMITTED",
                activeOverlay = false
            })
            .ToArray();

        return new
        {
            schemaVersion = "1.0",
            module = "G-HoneyTrap",
            integratedIn = "G-CET_RESOLVER",
            stage = "MEASURED_OPPORTUNITY_SCREEN",
            productionTransformsAuthorized = 0,
            candidateSchedulerLanes = rows.Length,
            sourceProvenRemovableLanes = 0,
            aggregateJobWorkMsPerSecond = Math.Round(
                byKey.Values.Sum(x => x.MeasuredNestedMsPerSecond), 6),
            existingSemanticsCannotBeVetoed = true,
            economicPolicy =
                "RISK_ADJUSTED_AGGREGATE_PAYBACK; NO_PER_MOD_FIXED_FLOOR; " +
                "REMOVABLE_WORK_MUST_BE_SOURCE_PROVEN; SHARED_COST_CHARGED_ONCE",
            measurementsPresent = new[]
            {
                "SOURCE_LINKED_SCHEDULER_JOB_CPU_AND_CALL_RATE",
                "EXISTING_SEMANTIC_STAGE_STATUS",
                "OWNER_CALLBACK_DORMANCY_LEADS"
            },
            unprovenForAdmission = new[]
            {
                "EXACT_JOB_CALLEE_LOCATION_ONLY_CLOSURE",
                "ATTRIBUTABLE_REMOVABLE_JOB_OR_CALLBACK_FRACTION",
                "INCREMENTAL_SHARED_WAKE_HOST_AND_NATIVE_POSITION_COST",
                "SPARSE_DENSE_ACTIVATION_DUTY_CYCLE_AND_GAMEPLAY_WAKE"
            },
            note =
                "This is the integrated selection front end, not a parallel optimizer or " +
                "a newly enabled source rewrite. No job/owner is admitted from name, " +
                "location hints, high CPU, or a completed semantic stage alone.",
            opportunities = rows
        };
    }
}
