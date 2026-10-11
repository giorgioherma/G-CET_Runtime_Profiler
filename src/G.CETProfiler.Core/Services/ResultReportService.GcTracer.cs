using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
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
        long RegistrationId, string FunctionName, string SourceFile,
        long SourceLineStart, long SourceLineEnd, long SampledCalls,
        long SampledExclusiveAllocatedBytes, long SampledExclusiveAllocationOperations);

    private sealed class GcTracerTelemetry
    {
        public List<GcTracerMod> Mods { get; init; } = [];
        public List<GcTracerFunction> Functions { get; init; } = [];
    }

    private static GcTracerTelemetry AnalyzeGcTracer(
        IReadOnlyList<Dictionary<string, string>> rankings,
        IReadOnlyList<Dictionary<string, string>> functions)
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
                L(r, "RegistrationId"), S(r, "FunctionName"),
                S(r, "SourceFile"), L(r, "SourceLineStart"),
                L(r, "SourceLineEnd"), L(r, "SampledCalls"),
                L(r, "SampledExclusiveAllocatedBytes"),
                L(r, "SampledExclusiveAllocationOperations")))
                .Where(x => x.SampledExclusiveAllocatedBytes > 0 ||
                            x.SampledExclusiveAllocationOperations > 0)
                .OrderByDescending(x => x.SampledExclusiveAllocationOperations)
                .ThenByDescending(x => x.SampledExclusiveAllocatedBytes)
                .ToList()
        };
    }

    private static void AppendGcTracer(StringBuilder sb, GcTracerTelemetry gc)
    {
        sb.Append("<div class=\"section\"><h2>G-GC Tracer</h2>")
          .Append("<p>Two independent rankings: five largest memory allocators and five ")
          .Append("additional frequent allocators. Counts represent positive LuaJIT ")
          .Append("allocation requests; not all requests create new GC objects. ")
          .Append("Lifetime and reclaimed-object metrics are samples, not estimates ")
          .Append("of total reclaimed memory. Unscoped activity stays unattributed.</p>")
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
            sb.Append("<h3>Sampled function-level allocations</h3>")
              .Append("<p>These are sampled debug-hook windows, not full function ")
              .Append("allocation totals. Function names can be unresolved or inlined.</p>")
              .Append("<table><thead><tr><th>Registration</th><th>Function</th>")
              .Append("<th>Source</th><th>Sampled allocations</th>")
              .Append("<th>Sampled bytes</th></tr></thead><tbody>");
            foreach (var x in gc.Functions.Take(25))
                sb.Append("<tr><td>").Append(x.RegistrationId).Append("</td><td>")
                  .Append(H(x.FunctionName)).Append("</td><td>")
                  .Append(H(x.SourceFile)).Append(":").Append(x.SourceLineStart)
                  .Append("</td><td>").Append(x.SampledExclusiveAllocationOperations)
                  .Append("</td><td>").Append(x.SampledExclusiveAllocatedBytes)
                  .Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        sb.Append("<p class=\"note\">Measurement only. GC collection sites are not ")
          .Append("garbage creators; no resolver rewrite is authorized by this ranking.")
          .Append("</p></div>");
    }
}
