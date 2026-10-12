using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // MEGAMIND evidence contract v1.0. A runtime observation is never a
    // semantic source proof or permission for the Resolver to rewrite a mod.
    private sealed record GcTracerMod(
        string Mod, long BytesRank, long OperationsRank,
        bool Selected, bool Focused, double AllocatedMiB,
        double AllocatedMiBPerSecond, long AllocationOperations,
        double AllocationsPerSecond, long NewGCObjects, long NewStrings,
        long SampledObjects, long SampleCollisions, long SampledReclaimedObjects,
        double SampledReclaimedMiB, double AverageReclaimedLifetimeMs,
        long SampledFinalizers, long SampledFreedTables, long SampledFreedStrings,
        long SampledFreedUserdata, long SampledFreedCdata, long SampledFreedOther);

    private sealed record GcTracerFunction(
        long RegistrationId, long ProfileEpoch, string FunctionName,
        string SourceFile, long SourceLineStart, long SourceLineEnd,
        long SampledCalls, long SampledExclusiveAllocatedBytes,
        long SampledExclusiveAllocationOperations,
        string FunctionKey, string LuaCallerFile, long LuaCallerLine,
        string CallsiteResolution, string ExecutionKind);

    private sealed record GcTracerRegistration(
        long RegistrationId, string Mod, string Kind, string Target,
        long AllocatedBytes, long AllocationOperations,
        long NewGCObjects, long NewStrings, long SampledObjects,
        long SampleCollisions, long SampledReclaimedObjects,
        long SampledReclaimedBytes, long SampledFinalizers, bool Focused);

    private sealed class GcTracerTelemetry
    {
        public List<GcTracerMod> Mods { get; init; } = [];
        public List<GcTracerFunction> Functions { get; init; } = [];
        public List<GcTracerRegistration> Registrations { get; init; } = [];
        public long RawGlobalFinalizerCalls { get; init; }
    }

    private static GcTracerTelemetry AnalyzeGcTracer(
        IReadOnlyList<Dictionary<string, string>> rankings,
        IReadOnlyList<Dictionary<string, string>> functions,
        IReadOnlyList<Dictionary<string, string>> registrations)
    {
        return new GcTracerTelemetry
        {
            Mods = rankings.Select(r => new GcTracerMod(
                S(r, "Mod"), L(r, "BytesRank"), L(r, "OperationsRank"),
                L(r, "SelectedTopTenAtDump") > 0, L(r, "FocusedAtDump") > 0,
                D(r, "AllocatedMiB"), D(r, "AllocatedMiBPerSecond"),
                L(r, "AllocationOperations"), D(r, "AllocationsPerSecond"),
                L(r, "NewGCObjects"), L(r, "NewStrings"),
                L(r, "SampledObjects"), L(r, "SampleCollisions"),
                L(r, "SampledReclaimedObjects"), D(r, "SampledReclaimedMiB"),
                D(r, "AverageReclaimedLifetimeMs"), L(r, "SampledFinalizers"),
                L(r, "FreedTablesSample"), L(r, "FreedStringsSample"),
                L(r, "FreedUserdataSample"), L(r, "FreedCdataSample"),
                L(r, "FreedOtherSample")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Mod))
                .OrderBy(x => x.BytesRank).ToList(),
            Functions = functions.Select(r => new GcTracerFunction(
                L(r, "RegistrationId"), L(r, "ProfileEpoch"),
                S(r, "FunctionName"), S(r, "SourceFile"),
                L(r, "SourceLineStart"), L(r, "SourceLineEnd"),
                L(r, "SampledCalls"), L(r, "SampledExclusiveAllocatedBytes"),
                L(r, "SampledExclusiveAllocationOperations"),
                S(r, "FunctionKey"), S(r, "LuaCallerFile"),
                L(r, "LuaCallerLine"),
                string.IsNullOrWhiteSpace(S(r, "CallsiteResolution"))
                    ? "NOT_RECORDED_LEGACY" : S(r, "CallsiteResolution"),
                string.IsNullOrWhiteSpace(S(r, "ExecutionKind"))
                    ? (S(r, "SourceFile") == "=[C]" ? "NATIVE_C" : "LUA_OR_OTHER")
                    : S(r, "ExecutionKind")))
                .Where(x => x.SampledExclusiveAllocatedBytes > 0 ||
                            x.SampledExclusiveAllocationOperations > 0)
                .OrderByDescending(x => x.SampledExclusiveAllocationOperations)
                .ThenByDescending(x => x.SampledExclusiveAllocatedBytes)
                .ToList(),
            Registrations = registrations.Select(r => new GcTracerRegistration(
                L(r, "RegistrationId"), S(r, "Mod"), S(r, "Kind"),
                S(r, "Target"), L(r, "AllocatedBytes"),
                L(r, "AllocationOperations"), L(r, "NewGCObjects"),
                L(r, "NewStrings"), L(r, "SampledObjects"),
                L(r, "SampleCollisions"), L(r, "SampledReclaimedObjects"),
                L(r, "SampledReclaimedBytes"), L(r, "SampledFinalizers"),
                L(r, "FocusedAtDump") > 0))
                .Where(x => x.RegistrationId > 0 &&
                            !string.IsNullOrWhiteSpace(x.Mod))
                .OrderBy(x => x.RegistrationId).ToList(),
            RawGlobalFinalizerCalls = rankings
                .Select(r => L(r, "ActualGlobalFinalizers"))
                .DefaultIfEmpty(0).Max()
        };
    }

    // EXACTLY the same object is emitted in CET_Summary.json and
    // CET_Resolver_Input.json. Both consumers share the metric vocabulary.
    private static object BuildGcTracerInterop(GcTracerTelemetry gc)
    {
        var recordedMiB = gc.Mods.Sum(x => x.AllocatedMiB);
        var unscopedMiB = gc.Mods
            .Where(x => x.Mod is "<unattributed>" or "<registration-overflow>")
            .Sum(x => x.AllocatedMiB);
        var sampledOps = gc.Functions.Sum(x => x.SampledExclusiveAllocationOperations);
        var cOps = gc.Functions
            .Where(x => x.ExecutionKind == "NATIVE_C")
            .Sum(x => x.SampledExclusiveAllocationOperations);
        static bool ProvenObservedCaller(GcTracerFunction x) =>
            x.CallsiteResolution == "SINGLE_OBSERVED_LUA_CALLSITE" &&
            !string.IsNullOrWhiteSpace(x.LuaCallerFile) &&
            x.LuaCallerLine > 0;
        var callerLinkedOps = gc.Functions
            .Where(x => x.ExecutionKind == "NATIVE_C" &&
                        ProvenObservedCaller(x))
            .Sum(x => x.SampledExclusiveAllocationOperations);

        // Callsite resolution is a property of sampled function observations,
        // not a count of exact allocation instructions or attributed bytes.
        // Native 2.15 has no Lua caller columns: distinguish its missing
        // instrumentation from instrumented 2.16 rows with no observed caller.
        static bool Ambiguous(GcTracerFunction x) =>
            x.CallsiteResolution == "MULTIPLE_CALLSITES" ||
            x.CallsiteResolution.StartsWith("AMBIGUOUS", StringComparison.Ordinal);
        static bool Legacy(GcTracerFunction x) =>
            x.CallsiteResolution == "NOT_RECORDED_LEGACY";
        static bool NotObserved(GcTracerFunction x) =>
            x.CallsiteResolution == "NOT_OBSERVED";
        var nativeFunctions = gc.Functions.Where(x => x.ExecutionKind == "NATIVE_C").ToList();

        return new
        {
            contractVersion = "1.0",
            evidenceVocabulary = "MEGAMIND_CET_MULTISIGNAL_V1",
            stage = "EVIDENCE_ONLY",
            measurement = "G_GC_TRACER_POSITIVE_ALLOCATOR_GROWTH_AND_SAMPLED_OBJECTS",
            attributionPolicy = "REGISTRATION_CREATOR_NOT_COLLECTOR_TRIGGER",
            noAutomaticRewrites = true,
            authorizesAutomaticRewrite = false,
            allocationEventsAreNotIdenticalToGCObjectBirths = true,
            dynamicFiveByBytesAndFiveDistinctByAllocationOperations = true,
            captureOnlyNoGCPolicyChanges = true,
            sampledReclamationMustNotBeExtrapolated = true,
            coverage = new
            {
                registrationRows = gc.Registrations.Count,
                rankedModRows = gc.Mods.Count,
                functionSampleRows = gc.Functions.Count,
                unattributedAllocatedMiB = Round(unscopedMiB, 6),
                unattributedAllocatedPct = Round(recordedMiB > 0
                    ? unscopedMiB / recordedMiB * 100 : 0, 3),
                cFunctionSampleOperations = cOps,
                functionSampleOperations = sampledOps,
                cFunctionSampleSharePct = Round(sampledOps > 0
                    ? (double)cOps / sampledOps * 100 : 0, 3),
                nativeOperationsWithSingleObservedLuaCallsite = callerLinkedOps,
                callsiteResolutionRows = new
                {
                    singleObserved = gc.Functions.Count(ProvenObservedCaller),
                    ambiguous = gc.Functions.Count(Ambiguous),
                    notObserved = gc.Functions.Count(NotObserved),
                    legacyNotRecorded = gc.Functions.Count(Legacy),
                    other = gc.Functions.Count(x =>
                        !ProvenObservedCaller(x) &&
                        !Ambiguous(x) && !NotObserved(x) && !Legacy(x))
                },
                nativeCallsiteSampleOperations = new
                {
                    singleObserved = callerLinkedOps,
                    ambiguous = nativeFunctions.Where(Ambiguous)
                        .Sum(x => x.SampledExclusiveAllocationOperations),
                    notObserved = nativeFunctions.Where(NotObserved)
                        .Sum(x => x.SampledExclusiveAllocationOperations),
                    legacyNotRecorded = nativeFunctions.Where(Legacy)
                        .Sum(x => x.SampledExclusiveAllocationOperations),
                    other = nativeFunctions.Where(x =>
                            !ProvenObservedCaller(x) &&
                            !Ambiguous(x) && !NotObserved(x) && !Legacy(x))
                        .Sum(x => x.SampledExclusiveAllocationOperations)
                },
                note = "Callsite counts and operations describe sampled function observations, not exact allocating instructions or allocated-byte ownership. Unattributed remains unowned."
            },
            metricSemantics = new
            {
                allocatedMiB = "GROSS_POSITIVE_LUAJIT_ALLOCATOR_GROWTH_NOT_LIVE_HEAP",
                allocationOperations = "POSITIVE_GROWTH_ALLOCATOR_REQUESTS_NOT_GC_OBJECT_BIRTHS",
                newGCObjects = "OBSERVED_GC_OBJECT_CREATION_NOT_TOTAL_ALLOCATIONS",
                sampledReclaimedObjects = "OBSERVED_SAMPLES_ONLY_NOT_HEAP_RECLAIM_TOTAL",
                averageReclaimedLifetimeMs = "RECLAIMED_SAMPLE_LIFETIMES_ONLY_NOT_RETENTION_PROOF",
                sourceFile = "EXECUTING_FUNCTION_SOURCE_NOT_REGISTRATION_OWNER",
                luaCallerFile = "SAMPLED_CALLSITE_NOT_PROVEN_ALLOCATOR_OWNER",
                functionCounts = "BOUNDED_SAMPLED_EXECUTIONS_NOT_GLOBAL_EXTRAPOLATION"
            },
            finalizerValidation = new
            {
                status = "UNVALIDATED_DO_NOT_USE_FOR_AUTOMATIC_DECISIONS",
                rawGlobalCalls = gc.RawGlobalFinalizerCalls,
                note = "Collector phase observations and callback invocations are different. Reconcile native count with independently instrumented finalizers before optimization."
            },
            // Legacy property names remain available to older consumers.
            rankings = gc.Mods,
            registrations = gc.Registrations,
            functionCandidates = gc.Functions,
            focusedFunctions = gc.Functions
        };
    }

    private static void AppendGcTracer(StringBuilder sb, GcTracerTelemetry gc)
    {
        var unknownMiB = gc.Mods
            .Where(x => x.Mod is "<unattributed>" or "<registration-overflow>")
            .Sum(x => x.AllocatedMiB);
        var totalMiB = gc.Mods.Sum(x => x.AllocatedMiB);
        sb.Append("<div class=\"section\"><h2>G-GC Tracer</h2>")
          .Append("<p>G-GC measurements: allocation-request growth, GC-object births ")
          .Append("and sampled reclamation are different measurements. A native/C frame ")
          .Append("is not the Lua source that called it. No resolver rewrite is ")
          .Append("authorized by these metrics.</p>")
          .Append("<p>Unattributed or overflow allocation volume: ")
          .Append(unknownMiB.ToString("F3"))
          .Append(" MiB (")
          .Append((totalMiB > 0 ? unknownMiB / totalMiB * 100 : 0).ToString("F1"))
          .Append("%). This work is not assigned to a mod.</p>")
          .Append("<table><thead><tr><th>Creator</th><th>Bytes rank</th>")
          .Append("<th>Operations rank</th><th>MiB/s</th><th>Allocations/s</th>")
          .Append("<th>New GC objects</th><th>Sampled reclaimed</th>")
          .Append("<th>Focus</th></tr></thead><tbody>");
        foreach (var x in gc.Mods.Where(x => x.Selected || x.Focused).Take(20))
            sb.Append("<tr><td>").Append(H(x.Mod)).Append("</td><td>")
              .Append(x.BytesRank).Append("</td><td>").Append(x.OperationsRank)
              .Append("</td><td>").Append(x.AllocatedMiBPerSecond.ToString("F3"))
              .Append("</td><td>").Append(x.AllocationsPerSecond.ToString("F1"))
              .Append("</td><td>").Append(x.NewGCObjects)
              .Append("</td><td>").Append(x.SampledReclaimedObjects)
              .Append("</td><td>").Append(x.Focused ? "Focused" : "Ranked")
              .Append("</td></tr>");
        sb.Append("</tbody></table>");
        if (gc.Functions.Count > 0)
        {
            sb.Append("<h3>Sampled function allocations and observed Lua callers</h3>")
              .Append("<p>Native execution, Lua caller and callback owner are separate ")
              .Append("identities. A single observed callsite is not proof of all ")
              .Append("callers. Ambiguous callsites remain unresolved.</p>")
              .Append("<table><thead><tr><th>Registration</th><th>Execution function</th>")
              .Append("<th>Executing source</th><th>Observed Lua caller</th>")
              .Append("<th>Sampled requests</th><th>Sampled bytes</th></tr></thead><tbody>");
            foreach (var x in gc.Functions.Take(25))
                sb.Append("<tr><td>").Append(x.RegistrationId).Append("</td><td>")
                  .Append(H(x.FunctionName)).Append("</td><td>")
                  .Append(H(x.SourceFile)).Append(":").Append(x.SourceLineStart)
                  .Append("</td><td>")
                  .Append(H(x.CallsiteResolution == "SINGLE_OBSERVED_LUA_CALLSITE"
                      ? x.LuaCallerFile + ":" + x.LuaCallerLine
                      : x.CallsiteResolution))
                  .Append("</td><td>").Append(x.SampledExclusiveAllocationOperations)
                  .Append("</td><td>").Append(x.SampledExclusiveAllocatedBytes)
                  .Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("<p class=\"note\">Raw global finalizer calls are unvalidated and ")
          .Append("excluded from automatic optimization decisions.</p></div>");
    }
}
