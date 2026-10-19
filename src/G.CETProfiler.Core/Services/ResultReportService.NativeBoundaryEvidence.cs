using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Two independent non-additive evidence streams. Preserve missing data
    // as null for older native DLLs; never infer zero allocations from absent CSVs.
    private static object? BuildNativeBoundaryEvidence(string captureRoot)
    {
        var c = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_LuaNative_Callsites.csv"));
        var p = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Native_PreDispatchPressure.csv"));
        if (c.Count == 0 && p.Count == 0) return null;
        return new
        {
            measurement = "CAPTURE_GATED_PRE_LUA_NATIVE_CONVERSION_AND_SAMPLED_LUA_C_OWN_PRESSURE",
            rewriteAuthorized = false,
            nativeHeapMeasured = false,
            ownerZeroPreDispatchMeasured = p.Count > 0,
            perSubscriberPreDispatchOwnershipMeasured = false,
            cFunctionMeansGameEngineGetter = false,
            sampledLuaCCallsiteRows = c.Count,
            preDispatchGroups = p.Select(row => new
            {
                boundary = S(row, "Boundary"),
                methodGroup = S(row, "MethodGroup"),
                calls = L(row, "Calls"),
                ownerZeroPositiveBytes = L(row, "OwnerZeroPositiveBytes"),
                ownerZeroPositiveOps = L(row, "OwnerZeroPositiveOps"),
                classAttribution = "NOT_PROVEN",
                subscriberAttribution = "NOT_PROVEN"
            }).ToArray(),
            sampledLuaCCallsites = c.OrderByDescending(row =>
                    L(row, "SampledCOwnPositiveBytes"))
                .Take(200).Select(row => new
                {
                    registrationId = L(row, "RegistrationId"),
                    profileEpoch = L(row, "ProfileEpoch"),
                    sampleSequence = L(row, "SampleSequence"),
                    callerSourceFile = S(row, "CallerSourceFile"),
                    callerLine = L(row, "CallerLine"),
                    childFunctionKey = S(row, "ChildFunctionKey"),
                    sampledCalls = L(row, "SampledCCalls"),
                    sampledPositiveBytes = L(row, "SampledCOwnPositiveBytes"),
                    sampledPositiveOps = L(row, "SampledCOwnPositiveOps"),
                    getterIdentityProven = false
                }).ToArray(),
            interpretation = "Lua C rows are bounded samples, not exact whole-capture costs or necessarily engine getters. Owner-zero argument conversion is before callback dispatch, with unknown class/subscriber. Both overlap existing GGC totals."
        };
    }

    private static void AppendNativeBoundaryEvidence(StringBuilder sb, string captureRoot)
    {
        var c = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_LuaNative_Callsites.csv"));
        var p = ReadCsv(FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Native_PreDispatchPressure.csv"));
        if (c.Count == 0 && p.Count == 0) return;
        sb.Append("<div class=\"section\"><h2>Lua / native allocation boundaries</h2>")
          .Append("<p class=\"note\">Native pre-dispatch rows count positive LuaJIT allocator ")
          .Append("growth before invoking a Lua callback; they do not assign work to a subscriber. ")
          .Append("C-function callsites are bounded deep samples and may be Lua library calls. ")
          .Append("Neither stream is additive to G-GC totals or proof of avoided allocation.</p>");
        if (p.Count > 0) {
            sb.Append("<h3>Pre-dispatch owner-zero conversion</h3><table><thead><tr>")
              .Append("<th>Boundary</th><th>Method</th><th>Calls</th><th>Positive bytes</th>")
              .Append("<th>Operations</th></tr></thead><tbody>");
            foreach (var row in p) {
                sb.Append("<tr><td>").Append(H(S(row, "Boundary")))
                  .Append("</td><td>").Append(H(S(row, "MethodGroup")))
                  .Append("</td><td>").Append(N(L(row, "Calls")))
                  .Append("</td><td>").Append(N(L(row, "OwnerZeroPositiveBytes")))
                  .Append("</td><td>").Append(N(L(row, "OwnerZeroPositiveOps")))
                  .Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        if (c.Count > 0) {
            sb.Append("<h3>Sampled C-function allocation callsites</h3><table><thead><tr>")
              .Append("<th>Lua source:line</th><th>Child key</th><th>Sampled calls</th>")
              .Append("<th>Sampled own bytes</th></tr></thead><tbody>");
            foreach (var row in c.OrderByDescending(x =>
                    L(x, "SampledCOwnPositiveBytes")).Take(25)) {
                sb.Append("<tr><td>").Append(H(S(row, "CallerSourceFile")))
                  .Append(":").Append(N(L(row, "CallerLine")))
                  .Append("</td><td>").Append(H(S(row, "ChildFunctionKey")))
                  .Append("</td><td>").Append(N(L(row, "SampledCCalls")))
                  .Append("</td><td>").Append(N(L(row, "SampledCOwnPositiveBytes")))
                  .Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("</div>");
    }
}
