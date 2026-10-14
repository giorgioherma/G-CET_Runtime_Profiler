using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Optional native attribution extension. Existing summaries and captures
    // remain valid when these CSVs are missing. No inferred producer identities.
    private sealed record UnscopedDimension(string Name, long Bytes, long Operations);
    private sealed record UnscopedSampleGroup(string CallPath, long Observations,
                                              long SampledPositiveBytes);
    private sealed class UnscopedDiagnosticReport
    {
        public bool HasVmTimeSeries { get; init; }
        public bool HasShapes { get; init; }
        public bool HasNativeSamples { get; init; }
        public long OwnerZeroBytes { get; init; }
        public long OwnerZeroOps { get; init; }
        public long VmBytes { get; init; }
        public long VmOps { get; init; }
        public long VmTimelineBytes { get; init; }
        public long VmTimelineOps { get; init; }
        public long ShapeBytes { get; init; }
        public long ShapeOps { get; init; }
        public long BoundaryBytes { get; init; }
        public long BoundaryOps { get; init; }
        public long PathTimelineBytes { get; init; }
        public long PathTimelineOps { get; init; }
        public long BoundaryTimelineBytes { get; init; }
        public long BoundaryTimelineOps { get; init; }
        public bool VmReconciled { get; init; }
        public bool ExtendedReconciled { get; init; }
        public long NativeSamples { get; init; }
        public long DroppedNativeSamples { get; init; }
        public List<UnscopedDimension> VmStates { get; init; } = [];
        public List<UnscopedDimension> Shapes { get; init; } = [];
        public List<UnscopedDimension> Boundaries { get; init; } = [];
        public List<UnscopedSampleGroup> NativeSampleGroups { get; init; } = [];
    }

    private static UnscopedDiagnosticReport? AnalyzeUnscopedDiagnostics(
        string captureRoot,
        IReadOnlyList<Dictionary<string, string>> rankings)
    {
        var vm = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedContexts.csv"));
        if (vm.Count == 0) return null;
        var vmTimeline = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedTimeline.csv"));
        var shapes = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedShapes.csv"));
        var boundary = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedBoundaries.csv"));
        var timeline = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedPathTimeline.csv"));
        var samples = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv"));

        var owner = rankings.FirstOrDefault(x =>
            string.Equals(S(x, "Mod"), "<unattributed>", StringComparison.OrdinalIgnoreCase));
        if (owner is null) return null;
        // Rankings provide exactly six decimal places in MiB. Allow up to two
        // bytes of rounding, but never silently reassign mismatched allocations.
        var expectedBytes = (long)Math.Round(D(owner, "AllocatedMiB") * 1048576.0);
        var expectedOps = L(owner, "AllocationOperations");
        static long SumBytes(IEnumerable<Dictionary<string, string>> rows) =>
            rows.Sum(x => L(x, "PositiveBytes"));
        static long SumOps(IEnumerable<Dictionary<string, string>> rows) =>
            rows.Sum(x => L(x, "AllocationOperations"));
        bool Match(long bytes, long ops) =>
            Math.Abs(bytes - expectedBytes) <= 2 && ops == expectedOps;

        var vmRows = vm.Select(x => new UnscopedDimension(S(x, "VmExecutionState"),
            L(x, "PositiveBytes"), L(x, "AllocationOperations")))
            .OrderByDescending(x => x.Bytes).ToList();
        var shapeRows = shapes.Select(x => new UnscopedDimension(
            S(x, "AllocatorPath") + " / " + S(x, "PositiveGrowthTier"),
            L(x, "PositiveBytes"), L(x, "AllocationOperations")))
            .OrderByDescending(x => x.Bytes).ToList();
        var boundaryRows = boundary.Select(x => new UnscopedDimension(
            S(x, "NativeExecutionBoundary"), L(x, "PositiveBytes"),
            L(x, "AllocationOperations"))).OrderByDescending(x => x.Bytes).ToList();
        var pathBins = timeline.Where(x =>
            S(x, "Dimension") == "ALLOCATOR_PATH").ToList();
        var boundaryBins = timeline.Where(x =>
            S(x, "Dimension") == "CET_BOUNDARY").ToList();
        var groups = samples
            .GroupBy(x => string.Join(" → ", new[] {
                S(x, "Frame1"), S(x, "Frame2"), S(x, "Frame3"), S(x, "Frame4"),
                S(x, "Frame5"), S(x, "Frame6") }.Where(v =>
                    !string.IsNullOrEmpty(v) && v != "NO_FRAME")))
            .Select(g => new UnscopedSampleGroup(g.Key, g.LongCount(),
                g.Sum(x => L(x, "PositiveBytes"))))
            .OrderByDescending(x => x.Observations)
            .ThenByDescending(x => x.SampledPositiveBytes)
            .Take(20).ToList();
        var vmOk = Match(SumBytes(vm), SumOps(vm)) &&
                   (vmTimeline.Count == 0 || Match(SumBytes(vmTimeline), SumOps(vmTimeline)));
        var extendedPresent = shapes.Count > 0 && boundary.Count > 0 && timeline.Count > 0;
        var extendedOk = extendedPresent && vmOk &&
            Match(SumBytes(shapes), SumOps(shapes)) &&
            Match(SumBytes(boundary), SumOps(boundary)) &&
            Match(SumBytes(pathBins), SumOps(pathBins)) &&
            Match(SumBytes(boundaryBins), SumOps(boundaryBins));
        return new UnscopedDiagnosticReport {
            HasVmTimeSeries = vmTimeline.Count > 0,
            HasShapes = shapes.Count > 0,
            HasNativeSamples = samples.Count > 0,
            OwnerZeroBytes = expectedBytes, OwnerZeroOps = expectedOps,
            VmBytes = SumBytes(vm), VmOps = SumOps(vm),
            VmTimelineBytes = SumBytes(vmTimeline), VmTimelineOps = SumOps(vmTimeline),
            ShapeBytes = SumBytes(shapes), ShapeOps = SumOps(shapes),
            BoundaryBytes = SumBytes(boundary), BoundaryOps = SumOps(boundary),
            PathTimelineBytes = SumBytes(pathBins), PathTimelineOps = SumOps(pathBins),
            BoundaryTimelineBytes = SumBytes(boundaryBins), BoundaryTimelineOps = SumOps(boundaryBins),
            VmReconciled = vmOk, ExtendedReconciled = extendedOk,
            NativeSamples = samples.Count,
            DroppedNativeSamples = samples.Count > 0
                ? L(samples[0], "DroppedSamples") : 0,
            VmStates = vmRows, Shapes = shapeRows,
            Boundaries = boundaryRows, NativeSampleGroups = groups
        };
    }

    private static object? BuildUnscopedDiagnosticsInterop(UnscopedDiagnosticReport? info)
    {
        if (info is null) return null;
        return new {
            measurement = "EXACT_OWNER_ZERO_VM_STATE_ALLOCATION_PATH_AND_OUTER_CET_CONTEXT_WITH_BOUNDED_NATIVE_SAMPLES",
            observedScope = "REGISTRATION_ID_ZERO_ONLY",
            sourceAttribution = "NOT_PROVEN",
            allocationGrowthIsNotRetainedHeap = true,
            nativeSamplesDoNotRepresentExactPopulation = true,
            autoRewriteAuthorized = false,
            info.HasVmTimeSeries, info.HasShapes, info.HasNativeSamples,
            info.VmReconciled, info.ExtendedReconciled,
            referenceUnscopedPositiveBytes = info.OwnerZeroBytes,
            referenceUnscopedOperations = info.OwnerZeroOps,
            vm = new { positiveBytes = info.VmBytes, operations = info.VmOps,
                timelinePositiveBytes = info.VmTimelineBytes,
                timelineOperations = info.VmTimelineOps },
            shapes = info.Shapes.Select(x => new {
                category = x.Name, positiveBytes = x.Bytes,
                allocationOperations = x.Operations
            }),
            outerCETBoundaries = info.Boundaries.Select(x => new {
                category = x.Name, positiveBytes = x.Bytes,
                allocationOperations = x.Operations
            }),
            timeSeries = new {
                allocationPathsPositiveBytes = info.PathTimelineBytes,
                allocationPathsOperations = info.PathTimelineOps,
                boundariesPositiveBytes = info.BoundaryTimelineBytes,
                boundariesOperations = info.BoundaryTimelineOps
            },
            nativeCallpaths = new {
                sampledEvents = info.NativeSamples,
                dropped = info.DroppedNativeSamples,
                interpretation = "NATIVE_UNWIND_SAMPLES_NOT_LUA_SOURCE_OR_TOTAL_ALLOCATION_ESTIMATE",
                topObservedCallpaths = info.NativeSampleGroups.Take(12).Select(x => new {
                    callpath = x.CallPath, sampledEvents = x.Observations,
                    observedSamplePositiveBytes = x.SampledPositiveBytes
                })
            }
        };
    }

    private static void AppendUnscopedDiagnostics(StringBuilder sb, UnscopedDiagnosticReport? r)
    {
        if (r is null) return;
        sb.Append("<div class=\"section\"><h2>Unscoped G-GC: VM, allocation type, CET boundary and native samples</h2>")
          .Append("<p class=\"note\">Only registration-zero positive LuaJIT allocator growth. ")
          .Append("Execution contexts and sampled native callpaths do not identify the originating ")
          .Append("Lua mod or prove useless allocations. Source rewrites are NOT authorized by these counters.</p>");
        sb.Append("<p><b>VM totals reconcile:</b> ")
          .Append(r.VmReconciled ? "YES" : "NO — review raw evidence")
          .Append(" | <b>Extended diagnostic totals reconcile:</b> ")
          .Append(!r.HasShapes ? "not captured" :
                  r.ExtendedReconciled ? "YES" : "NO — review raw evidence")
          .Append("</p>");
        foreach (var section in new[] {
            ("VM execution state", r.VmStates),
            ("Allocation path and size", r.Shapes),
            ("CET outer execution boundary", r.Boundaries) })
        {
            if (section.Item2.Count == 0) continue;
            sb.Append("<h3>").Append(H(section.Item1)).Append("</h3><table><thead><tr>")
              .Append("<th>Category</th><th>Positive MiB</th><th>Operations</th></tr></thead><tbody>");
            foreach (var row in section.Item2.Take(12))
                sb.Append("<tr><td>").Append(H(row.Name)).Append("</td><td>")
                  .Append(F(row.Bytes / 1048576.0, 3)).Append("</td><td>")
                  .Append(N(row.Operations)).Append("</td></tr>");
            sb.Append("</tbody></table>");
        }
        if (r.HasNativeSamples) {
            sb.Append("<h3>Most frequent sampled native callpaths</h3>")
              .Append("<p class=\"note\">Sample count, not population-weighted bytes. ")
              .Append("Module+RVA addresses are for investigation only.</p>")
              .Append("<table><thead><tr><th>Sampled events</th><th>Native path</th></tr></thead><tbody>");
            foreach (var x in r.NativeSampleGroups.Take(10))
                sb.Append("<tr><td>").Append(N(x.Observations))
                  .Append("</td><td>").Append(H(x.CallPath)).Append("</td></tr>");
            sb.Append("</tbody></table><p class=\"note\">Dropped samples: ")
              .Append(N(r.DroppedNativeSamples)).Append("</p>");
        }
        sb.Append("</div>");
    }
}
