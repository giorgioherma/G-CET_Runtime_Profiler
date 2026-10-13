using System.Net;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private sealed record ScalarObservationRow(
        string Label, long Observations, long Unchanged, long Changed);

    private sealed class ScalarObservationReport
    {
        public string Status { get; init; } = "NOT_CAPTURED";
        public List<ScalarObservationRow> Rows { get; init; } = [];
        public long DroppedLabels { get; init; }
    }

    private static ScalarObservationReport AnalyzeScalarObservations(string captureRoot)
    {
        var path = FindProfilerFile(
            captureRoot, "CET_Runtime_Profile_Scalar_Observations.csv");
        if (path is null)
            return new ScalarObservationReport();
        var raw = ReadCsv(path);
        var rows = raw
            .Where(x => !string.IsNullOrWhiteSpace(S(x, "Label")))
            .Select(x => new ScalarObservationRow(
                S(x, "Label"), L(x, "Observations"),
                L(x, "Unchanged"), L(x, "Changed")))
            .Where(x => x.Observations > 0 &&
                x.Unchanged >= 0 && x.Changed >= 0 &&
                x.Unchanged + x.Changed == x.Observations - 1)
            .OrderByDescending(x => x.Observations)
            .Take(128)
            .ToList();
        return new ScalarObservationReport
        {
            Status = rows.Count > 0 ? "OPT_IN_SCALARS_OBSERVED" : "NO_VALID_OBSERVATIONS",
            Rows = rows,
            DroppedLabels = raw.Count > 0 ? L(raw[0], "DroppedLabels") : 0
        };
    }

    private static object BuildScalarObservations(ScalarObservationReport? report)
    {
        report ??= new ScalarObservationReport();
        return new
        {
            status = report.Status,
            observedLabels = report.Rows.Count,
            droppedLabels = report.DroppedLabels,
            observations = report.Rows.Select(x => new
            {
                label = x.Label,
                observations = x.Observations,
                unchanged = x.Unchanged,
                changed = x.Changed,
                comparisonPairs = x.Observations - 1,
                changedRateOfPairs = x.Observations > 1
                    ? (double)x.Changed / (x.Observations - 1)
                    : (double?)null
            }).ToArray(),
            safety = "Exact sequential scalar-string observations only; scoped label identity is caller-supplied. No input identity, cross-NPC equivalence, pointer stability, cache invalidation or safe reuse proved.",
            authorizesAutomaticRewrite = false
        };
    }

    private static void AppendScalarObservations(
        StringBuilder sb, ScalarObservationReport? report)
    {
        if (report is null || report.Status == "NOT_CAPTURED")
            return;
        sb.Append("<div class='section'><h2>Opt-in scalar value change observations</h2>");
        sb.Append("<p>Capture-gated, explicitly instrumented existing reads only. ");
        sb.Append("An unchanged sequential scalar is NOT evidence that a native object, ");
        sb.Append("table, inventory or another NPC is safe to cache.</p>");
        if (report.Rows.Count == 0)
        {
            sb.Append("<p>No valid scalar observations were recorded.</p></div>");
            return;
        }
        sb.Append("<table><thead><tr><th>Caller-supplied probe label</th><th>Observed</th>");
        sb.Append("<th>Equal to previous</th><th>Changed</th></tr></thead><tbody>");
        foreach (var row in report.Rows)
        {
            sb.Append("<tr><td>").Append(WebUtility.HtmlEncode(row.Label)).Append("</td><td>")
                .Append(row.Observations).Append("</td><td>")
                .Append(row.Unchanged).Append("</td><td>")
                .Append(row.Changed).Append("</td></tr>");
        }
        sb.Append("</tbody></table></div>");
    }
}
