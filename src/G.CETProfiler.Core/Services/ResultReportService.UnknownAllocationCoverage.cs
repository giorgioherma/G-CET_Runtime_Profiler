using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // The native v2.15 ID-0 bucket is UNSCOPED, not an arbitrary missing mod.
    // Report byte/operation/object-birth coverage independently. The totals
    // are positive LuaJIT allocator growth, NEVER retained or freed MiB.
    private sealed record AllocationCoverageBucket(
        string Category, bool Present, double MiB, long Operations,
        long ObjectBirths, long StringBirths);

    private sealed class UnknownAllocationCoverage
    {
        public AllocationCoverageBucket Unscoped { get; init; } =
            new("UNSCOPED_REGISTRATION_ID_0", false, 0, 0, 0, 0);
        public AllocationCoverageBucket Overflow { get; init; } =
            new("REGISTRATION_OVERFLOW", false, 0, 0, 0, 0);
        public AllocationCoverageBucket Scoped { get; init; } =
            new("NAMED_CALLBACK_REGISTRATIONS", false, 0, 0, 0, 0);
        public double TotalMiB =>
            Unscoped.MiB + Overflow.MiB + Scoped.MiB;
        public long TotalOperations =>
            Unscoped.Operations + Overflow.Operations + Scoped.Operations;
        public long TotalBirths =>
            Unscoped.ObjectBirths + Overflow.ObjectBirths + Scoped.ObjectBirths;
        public long TotalStrings =>
            Unscoped.StringBirths + Overflow.StringBirths + Scoped.StringBirths;
    }

    private static UnknownAllocationCoverage? AnalyzeUnknownAllocationCoverage(
        ResultAnalysis a)
    {
        var mods = a.GcTracer?.Mods;
        if (mods is null || mods.Count == 0)
            return null;

        static AllocationCoverageBucket Build(
            IEnumerable<GcTracerMod> rows, string category)
        {
            var list = rows.ToList();
            return new AllocationCoverageBucket(
                category, list.Count > 0,
                list.Sum(x => x.AllocatedMiB),
                list.Sum(x => x.AllocationOperations),
                list.Sum(x => x.NewGCObjects),
                list.Sum(x => x.NewStrings));
        }

        var unscoped = Build(mods.Where(x =>
            x.Mod.Equals("<unattributed>", StringComparison.OrdinalIgnoreCase)),
            "UNSCOPED_REGISTRATION_ID_0");
        var overflow = Build(mods.Where(x =>
            x.Mod.Equals("<registration-overflow>", StringComparison.OrdinalIgnoreCase)),
            "REGISTRATION_OVERFLOW");
        var scoped = Build(mods.Where(x =>
            !x.Mod.Equals("<unattributed>", StringComparison.OrdinalIgnoreCase) &&
            !x.Mod.Equals("<registration-overflow>", StringComparison.OrdinalIgnoreCase)),
            "NAMED_CALLBACK_REGISTRATIONS");
        return new UnknownAllocationCoverage {
            Unscoped = unscoped, Overflow = overflow, Scoped = scoped
        };
    }

    private static double? SafeRatio(double numerator, double denominator) =>
        denominator > 0 && double.IsFinite(numerator) &&
        double.IsFinite(denominator) ? numerator / denominator : null;

    private static object? BuildUnknownAllocationCoverageInterop(
        ResultAnalysis a)
    {
        var coverage = AnalyzeUnknownAllocationCoverage(a);
        if (coverage is null) return null;
        var duration = a.CaptureSeconds;
        var totalMiB = coverage.TotalMiB;
        var ops = coverage.TotalOperations;
        var births = coverage.TotalBirths;
        var strings = coverage.TotalStrings;

        static object Bucket(
            AllocationCoverageBucket x,
            double totalMiB, long totalOps,
            long totalBirths, long totalStrings, double seconds)
        {
            double? Fraction(double value, double total) =>
                SafeRatio(value * 100, total);
            return new
            {
                x.Category, x.Present,
                positiveAllocatedMiB = Round(x.MiB, 6),
                positiveAllocatedMiBPerSecond = seconds > 0
                    ? Round(x.MiB / seconds, 6) : (double?)null,
                allocatedByteSharePct = Fraction(x.MiB, totalMiB),
                x.Operations,
                allocationOperationsPerSecond = seconds > 0
                    ? Round(x.Operations / seconds, 3) : (double?)null,
                allocationOperationSharePct = Fraction(x.Operations, totalOps),
                x.ObjectBirths,
                objectBirthSharePct = Fraction(x.ObjectBirths, totalBirths),
                x.StringBirths,
                stringBirthSharePct = Fraction(x.StringBirths, totalStrings),
                meanPositiveBytesPerAllocationRequest = x.Operations > 0
                    ? Round(x.MiB * 1048576 / x.Operations, 3)
                    : (double?)null,
                // This is a cross-metric frequency ratio, NOT the probability
                // an allocation request produced a new GC-managed object.
                objectBirthsPer100AllocatorRequests = x.Operations > 0
                    ? Round(x.ObjectBirths * 100.0 / x.Operations, 3)
                    : (double?)null
            };
        }
        return new
        {
            measurement = "POSITIVE_LUAJIT_GROWTH_SPLIT_BY_REGISTRATION_SCOPE",
            additiveEvidenceOnly = true,
            preservesLegacyCpuAndGcMetrics = true,
            captureSeconds = Round(duration, 3),
            totalPositiveAllocatedMiB = Round(totalMiB, 6),
            totalAllocationOperations = ops,
            totalObservedGcObjectBirths = births,
            totalObservedNewStrings = strings,
            unknownIsNotAnAllocationOwner = true,
            collectorTimeIsNotAllocationOwnership = true,
            unknownByteTotalsAreNotProvenWasteOrRetainedHeap = true,
            newFunctionAttributionFromExistingCsv = false,
            unscopedReasonKnown = false,
            unscopedClassification = "REGISTRATION_SCOPE_ZERO; CAUSE_NOT_RECORDED",
            temporalDistributionMeasured = a.UnscopedDiagnostics?.HasVmTimeSeries == true,
            allocationFunctionSourceForUnscoped = a.UnscopedDiagnostics?.HasNativeSamples == true ? "NATIVE_CALLPATH_SAMPLES_ONLY_NOT_LUA_CALLSITE" : "NOT_MEASURED",
            unscoped = Bucket(coverage.Unscoped, totalMiB, ops, births, strings, duration),
            registrationOverflow = Bucket(coverage.Overflow, totalMiB, ops, births, strings, duration),
            scopedCallbacks = Bucket(coverage.Scoped, totalMiB, ops, births, strings, duration),
            nextCaptureResearch = new[] {
                "CLASSIFY_ID0_BY_EXACT_NATIVE_EXECUTION_CONTEXT_WITHOUT_REASSIGNING_OWNERS",
                "DISTINGUISH_OUTSIDE_CALLBACK_VM_WORK_FROM_REGISTRATION_OVERFLOW",
                "SAMPLE_UNSCOPED_POSITIVE_ALLOCATION_OPERATIONS_AND_OBJECT_BIRTHS_BY_LIFECYCLE",
                "RETAIN_NATIVE_2_15_UNTIL_NEW_CONTEXT_PROBE_PASSES_SAME_SAVE_STARTUP_TEST"
            }
        };
    }

    private static void AppendUnknownAllocationCoverage(StringBuilder sb, ResultAnalysis a)
    {
        var c = AnalyzeUnknownAllocationCoverage(a);
        if (c is null) return;
        sb.Append("<div class=\"section\"><h2>Unknown allocation coverage — separate bytes from objects</h2>");
        sb.Append("<div class=\"note\">0 means the native profiler had no active ")
          .Append("callback registration scope at allocation time; it is NOT ")
          .Append("the name of an unknown mod. A registration overflow has its ")
          .Append("own bucket. Positive allocator growth is NOT retained memory, ")
          .Append("reclaimed memory or proven wasted data. There is no time-of-allocation ")
          .Append("or source-function breakdown from legacy captures; newer captures may ")
          .Append("include independently classified VM/boundary data and bounded native samples.</div>");
        sb.Append("<table><thead><tr><th>Scope</th><th>MiB</th>")
          .Append("<th>Bytes %</th><th>Requests</th><th>Ops/s</th>")
          .Append("<th>Ops %</th><th>GC births</th><th>Births %</th>")
          .Append("<th>New strings</th><th>Bytes/request</th></tr></thead><tbody>");
        var rows = new[] { c.Unscoped, c.Overflow, c.Scoped };
        foreach (var x in rows)
        {
            string pct(double numerator, double denominator) =>
                denominator > 0 ? F(numerator / denominator * 100, 2) + "%" : "n/a";
            sb.Append("<tr><td>").Append(H(x.Category)).Append("</td><td>")
              .Append(x.Present ? F(x.MiB, 3) : "not recorded")
              .Append("</td><td>").Append(pct(x.MiB, c.TotalMiB))
              .Append("</td><td>").Append(x.Present ? N(x.Operations) : "—")
              .Append("</td><td>").Append(a.CaptureSeconds > 0
                  ? F(x.Operations / a.CaptureSeconds, 1) : "n/a")
              .Append("</td><td>").Append(pct(x.Operations, c.TotalOperations))
              .Append("</td><td>").Append(x.Present ? N(x.ObjectBirths) : "—")
              .Append("</td><td>").Append(pct(x.ObjectBirths, c.TotalBirths))
              .Append("</td><td>").Append(x.Present ? N(x.StringBirths) : "—")
              .Append("</td><td>").Append(x.Operations > 0
                  ? F(x.MiB * 1048576 / x.Operations, 1) : "n/a")
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table>");
        sb.Append("<p class=\"note\"><b>Diagnostic:</b> If ID 0 has a very ")
          .Append("large share of positive allocation bytes but a small share ")
          .Append("of new GC-object births, investigate unscoped reallocations, ")
          .Append("VM/internal memory and non-callback execution first. This ")
          .Append("pattern does not identify which function produced them. ")
          .Append("Native collection, allocation and callback timings are ")
          .Append("unchanged; no silent reassignment is performed.</p></div>");
    }
}
