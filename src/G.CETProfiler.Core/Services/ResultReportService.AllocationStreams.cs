using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Allocation stream evidence is ADDITIVE. The legacy CPU/GC reports,
    // callback totals, rate calculations and native payload are unchanged.
    // A routed registration describes the execution envelope, not necessarily
    // the allocating Lua function. Value-change frequency is not measured by
    // native v2.15: do not guess it from read frequency.
    private sealed record AllocationStream(
        long RegistrationId, string Kind, string Target, string SourceFile,
        long Calls, double CallsPerSecond, double AllocatedMiB,
        double MiBPerSecond, long AllocationOperations,
        double OperationsPerSecond, double? OperationsPerCall,
        double? AllocatedBytesPerCall, long NewGCObjects, long NewStrings);

    private sealed record AllocationFunctionCandidate(
        string Function, string SourceFile, long SourceLine,
        bool NativeExecution, long SampledCalls,
        long SampledAllocationOperations, long SampledAllocatedBytes,
        string CallsiteResolution);

    private sealed record ZeroEngineAllocationFlow(
        IReadOnlyList<AllocationStream> Streams,
        IReadOnlyList<AllocationFunctionCandidate> FunctionSamples,
        double AllocatedMiB, long AllocationOperations,
        long NewGCObjects, long NewStrings,
        long SourceSampleOperations, long NativeSampleOperations);

    private static ZeroEngineAllocationFlow? AnalyzeZeroEngineFlow(ResultAnalysis a)
    {
        if (a.GcTracer is null || a.CaptureSeconds <= 0)
            return null;

        var details = a.DetailRows
            .Where(x => string.Equals(S(x, "Mod", "Owner"), "0-Engine",
                StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => L(x, "RegistrationId"))
            .ToDictionary(x => x.Key, x => x.First());

        var streams = a.GcTracer.Registrations
            .Where(x => string.Equals(x.Mod, "0-Engine",
                StringComparison.OrdinalIgnoreCase))
            .Select(x =>
            {
                details.TryGetValue(x.RegistrationId, out var detail);
                var calls = detail is null ? 0 : L(detail, "Calls");
                var rate = detail is null ? 0 : D(detail, "CallsPerSecond");
                return new AllocationStream(
                    x.RegistrationId, x.Kind, x.Target,
                    detail is null ? "" : S(detail, "SourceFile"),
                    calls, rate, x.AllocatedBytes / 1048576.0,
                    x.AllocatedBytes / 1048576.0 / a.CaptureSeconds,
                    x.AllocationOperations,
                    x.AllocationOperations / a.CaptureSeconds,
                    calls > 0 ? (double)x.AllocationOperations / calls : null,
                    calls > 0 ? (double)x.AllocatedBytes / calls : null,
                    x.NewGCObjects, x.NewStrings);
            })
            .Where(x => x.AllocationOperations > 0 || x.AllocatedMiB > 0)
            .OrderByDescending(x => x.AllocationOperations)
            .ToList();

        var ids = streams.Select(x => x.RegistrationId).ToHashSet();
        var fn = a.GcTracer.Functions
            .Where(x => ids.Contains(x.RegistrationId))
            .GroupBy(x => (
                x.FunctionName, x.SourceFile, x.SourceLineStart,
                x.CallsiteResolution,
                Native: x.SourceFile == "=[C]"))
            .Select(g => new AllocationFunctionCandidate(
                g.Key.FunctionName, g.Key.SourceFile, g.Key.SourceLineStart,
                g.Key.Native,
                g.Sum(x => x.SampledCalls),
                g.Sum(x => x.SampledExclusiveAllocationOperations),
                g.Sum(x => x.SampledExclusiveAllocatedBytes),
                g.Key.CallsiteResolution))
            .OrderByDescending(x => x.SampledAllocationOperations)
            .Take(30)
            .ToList();
        return new ZeroEngineAllocationFlow(
            streams, fn,
            streams.Sum(x => x.AllocatedMiB),
            streams.Sum(x => x.AllocationOperations),
            streams.Sum(x => x.NewGCObjects),
            streams.Sum(x => x.NewStrings),
            fn.Sum(x => x.SampledAllocationOperations),
            fn.Where(x => x.NativeExecution)
                .Sum(x => x.SampledAllocationOperations));
    }

    private static object? BuildZeroEngineFlowInterop(ResultAnalysis a)
    {
        var flow = AnalyzeZeroEngineFlow(a);
        if (flow is null) return null;
        return new
        {
            measurement = "OBSERVED_REGISTRATION_ALLOCATIONS_AND_BOUNDED_FUNCTION_SAMPLES",
            additiveEvidenceOnly = true,
            timingMetricsUnchanged = true,
            gcCollectorPolicyUnchanged = true,
            executionRegistrationIsNotAllocationSourceProof = true,
            nativeFunctionSampleIsNotCallsiteAttribution = true,
            valueChangeRate = (double?)null,
            valueChangeStatus = "NOT_MEASURED_BY_NATIVE_2_15",
            readValueEqualityObserved = false,
            allocationTotalsNotLiveHeap = true,
            focus = "0-Engine routed/contained callbacks (not necessarily framework allocations)",
            allocationMiB = Round(flow.AllocatedMiB, 6),
            allocationOperations = flow.AllocationOperations,
            newGcObjects = flow.NewGCObjects,
            newStrings = flow.NewStrings,
            streams = flow.Streams.Select(x => new
            {
                registrationId = x.RegistrationId,
                x.Kind, x.Target, x.SourceFile,
                x.Calls,
                callsPerSecond = Round(x.CallsPerSecond, 3),
                allocatedMiB = Round(x.AllocatedMiB, 6),
                allocatedMiBPerSecond = Round(x.MiBPerSecond, 6),
                x.AllocationOperations,
                allocationOperationsPerSecond = Round(x.OperationsPerSecond, 3),
                allocationOperationsPerCall =
                    x.OperationsPerCall is double c ? Round(c, 6) : (double?)null,
                allocatedBytesPerCall =
                    x.AllocatedBytesPerCall is double b ? Round(b, 3) : (double?)null,
                x.NewGCObjects, x.NewStrings,
                valueReadRate = (double?)null,
                valueChangeRate = (double?)null,
                changeStatus = "NOT_OBSERVED",
                ownership = "ROUTED_REGISTRATION_NOT_EXCLUSIVE_FRAMEWORK_SOURCE"
            }),
            functionSamples = flow.FunctionSamples.Select(x => new
            {
                x.Function, x.SourceFile, x.SourceLine,
                x.NativeExecution, x.SampledCalls,
                x.SampledAllocationOperations, x.SampledAllocatedBytes,
                x.CallsiteResolution,
                samplingIsNotGlobalCallRate = true,
                returnValueChangeRate = (double?)null
            })
        };
    }

    private static void AppendZeroEngineFlow(StringBuilder sb, ResultAnalysis a)
    {
        var flow = AnalyzeZeroEngineFlow(a);
        if (flow is null || flow.Streams.Count == 0) return;
        sb.Append("<div class=\"section\"><h2>0-Engine: data production and frequency</h2>");
        sb.Append("<div class=\"note\">Additive allocation evidence; all existing ")
          .Append("CET ms/s, callback spikes and GC timings are preserved. ")
          .Append("0-Engine carries work from other mods. The execution route ")
          .Append("is NOT proof that 0-Engine generated the data. Positive ")
          .Append("allocations are not retained heap or proven waste.</div>");
        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "0-Engine routed allocations",
            F(flow.AllocatedMiB, 2) + " MiB", "Across captured registrations");
        MetricCard(sb, "Allocation rate",
            F(flow.AllocationOperations / a.CaptureSeconds, 1) + " ops/s",
            "Positive allocator requests, not object births");
        MetricCard(sb, "GC object births", N(flow.NewGCObjects),
            "Different measurement from allocation requests");
        MetricCard(sb, "Return value change rate", "Not measured",
            "Cannot infer stable values from reads or allocation volume");
        sb.Append("</div>");
        sb.Append("<h3>Which routes generate data most frequently?</h3>")
          .Append("<table><thead><tr><th>Registration / route</th>")
          .Append("<th>Calls/s</th><th>MiB</th><th>MiB/s</th>")
          .Append("<th>Alloc. ops/s</th><th>Ops/call</th>")
          .Append("<th>Bytes/call</th><th>Value changes</th></tr></thead><tbody>");
        foreach (var x in flow.Streams.Take(20))
        {
            sb.Append("<tr><td>").Append(x.RegistrationId).Append(" · ")
              .Append(H(JoinCallback(x.Kind, x.Target))).Append("</td><td>")
              .Append(F(x.CallsPerSecond, 1)).Append("</td><td>")
              .Append(F(x.AllocatedMiB, 2)).Append("</td><td>")
              .Append(F(x.MiBPerSecond, 3)).Append("</td><td>")
              .Append(F(x.OperationsPerSecond, 1)).Append("</td><td>")
              .Append(x.OperationsPerCall is double op ? F(op, 2) : "n/a")
              .Append("</td><td>")
              .Append(x.AllocatedBytesPerCall is double bytes ? F(bytes, 1) : "n/a")
              .Append("</td><td>Unknown</td></tr>");
        }
        sb.Append("</tbody></table><h3>Sampled functions / possible read producers</h3>")
          .Append("<p class=\"note\">Bounded sampling identifies executing functions, ")
          .Append("including native getters and property access. These sample counts ")
          .Append("are not full call frequencies. Native v2.15 does not capture ")
          .Append("their returned values or equality across reads, so no function ")
          .Append("is labeled rarely changing without evidence.</p>");
        sb.Append("<table><thead><tr><th>Function</th><th>Executing source</th>")
          .Append("<th>Sampled calls</th><th>Sampled alloc. operations</th>")
          .Append("<th>Value changed?</th></tr></thead><tbody>");
        foreach (var x in flow.FunctionSamples.Take(20))
        {
            sb.Append("<tr><td>").Append(H(x.Function)).Append("</td><td>")
              .Append(H(x.SourceFile)).Append(":").Append(x.SourceLine)
              .Append(x.NativeExecution ? " (native)" : "")
              .Append("</td><td>").Append(N(x.SampledCalls))
              .Append("</td><td>").Append(N(x.SampledAllocationOperations))
              .Append("</td><td>Not observed</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<div class=\"note\">To establish <b>frequently read, rarely changed</b> ")
          .Append("values, a future bounded, opt-in probe must compare actual ")
          .Append("scalar return values at the original read site and record ")
          .Append("read count, change count and source. Do not add extra native ")
          .Append("getter calls or compare mutable native handles by identity.</div></div>");
    }
}
