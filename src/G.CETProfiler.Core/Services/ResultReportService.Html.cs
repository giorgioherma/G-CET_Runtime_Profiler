using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private static string BuildHtml(ResultAnalysis a, string captureRoot)
    {
        var sb = new StringBuilder(64_000);
        sb.Append("""
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>G-CET Runtime Profiler — Capture Report</title>
<style>
:root{color-scheme:dark;--bg:#0d1117;--panel:#141a22;--panel2:#10151c;--line:#29313d;--text:#edf2f7;--muted:#9aa7b5;--accent:#67d7ff;--accent2:#d2ff72}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);font:14px/1.45 "Segoe UI",Arial,sans-serif}
.wrap{max-width:1260px;margin:auto;padding:24px}.hero{display:flex;justify-content:space-between;gap:20px;align-items:flex-start;margin-bottom:18px}
.hero h1{margin:0 0 5px;font-size:29px}.sub,.muted{color:var(--muted)}.scope{max-width:760px;color:var(--muted);margin-top:8px}
.badge{display:inline-block;border:1px solid var(--line);background:var(--panel);padding:5px 9px;border-radius:999px;color:var(--accent2);font-size:12px;font-weight:700}
.grid{display:grid;grid-template-columns:repeat(4,1fr);gap:10px}.card{background:var(--panel);border:1px solid var(--line);border-radius:9px;padding:14px}
.k{font-size:12px;color:var(--muted);text-transform:uppercase;letter-spacing:.05em}.v{font-size:25px;font-weight:700;margin-top:4px}.s{font-size:12px;color:var(--muted);margin-top:3px}
.section{margin-top:22px}.section h2{font-size:19px;margin:0 0 10px}.section h3{font-size:15px;margin:16px 0 8px;color:#dce7f1}
.findings{display:grid;grid-template-columns:repeat(2,1fr);gap:10px}.finding{background:var(--panel);border:1px solid var(--line);border-left:4px solid var(--accent);border-radius:8px;padding:13px}
.finding h3{margin:0 0 5px;font-size:15px}.evidence{font-weight:650}.why{color:var(--muted);margin-top:5px}
.note{background:var(--panel2);border:1px solid var(--line);border-radius:8px;padding:11px 13px;color:var(--muted);margin:10px 0}.note b{color:var(--text)}
table{width:100%;border-collapse:collapse;background:var(--panel);border:1px solid var(--line);border-radius:8px;overflow:hidden}
th,td{padding:8px 10px;border-bottom:1px solid #232b35;text-align:left;vertical-align:top}th{font-size:12px;color:#b7c3cf;background:#171e27;position:sticky;top:0}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums}.barwrap{width:130px;height:8px;background:#262f3a;border-radius:99px;overflow:hidden;margin-top:5px}.bar{height:100%;background:var(--accent);border-radius:99px}
.mono{font-family:Consolas,"Courier New",monospace}.two{display:grid;grid-template-columns:1fr 1fr;gap:12px}
details{background:var(--panel2);border:1px solid var(--line);border-radius:8px;padding:10px 12px;margin-top:10px}summary{cursor:pointer;font-weight:650}
.links a{color:var(--accent);text-decoration:none}.links a:hover{text-decoration:underline}.footer{color:var(--muted);font-size:12px;margin:28px 0 8px}
@media(max-width:900px){.grid{grid-template-columns:repeat(2,1fr)}.findings,.two{grid-template-columns:1fr}.wrap{padding:14px}}
@media(max-width:520px){.grid{grid-template-columns:1fr}.hero{display:block}}
</style>
</head>
<body><div class="wrap">
""");

        sb.Append("<div class=\"hero\"><div><h1>G-CET Runtime Profiler</h1><div class=\"sub\">CET capture report</div>");
        sb.Append("<div class=\"scope\">What CET was doing during this capture: sustained workload, call volume, callback hotspots, heavy CET windows, recorded callback spikes and 0-Engine Scheduler activity where available.</div></div>");
        sb.Append("<div><span class=\"badge\">CET-SIDE MEASUREMENT</span></div></div>");

        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Capture", a.CaptureSeconds > 0 ? F(a.CaptureSeconds, 1) + " s" : "—", a.Owners.Count + " measured owners");
        MetricCard(sb, "Measured CET work", F(a.TotalMsPerSecond) + " ms/s", F(a.TotalOneCorePct, 2) + "% of one core measured");
        MetricCard(sb, "Measured calls", N(a.TotalCalls), F(a.TotalCallsPerSecond, 0) + " calls/s");
        var top = a.Owners.FirstOrDefault();
        MetricCard(sb, "Largest workload", top is null ? "—" : H(top.Name), top is null ? "No ByMod data" : F(top.ExclusiveMsPerSecond) + " ms/s");
        sb.Append("</div>");

        sb.Append("<div class=\"section\"><h2>What stands out</h2>");
        if (a.Findings.Count == 0)
        {
            sb.Append("<div class=\"note\"><b>No full CET workload tables were available.</b> Raw profiler output was still archived safely under <span class=\"mono\">Data/</span>.</div>");
        }
        else
        {
            sb.Append("<div class=\"findings\">");
            foreach (var finding in a.Findings)
            {
                sb.Append("<div class=\"finding\"><h3>").Append(H(finding.Title)).Append("</h3><div class=\"evidence\">")
                    .Append(H(finding.Evidence)).Append("</div><div class=\"why\">").Append(H(finding.Explanation)).Append("</div></div>");
            }
            sb.Append("</div>");
        }
        sb.Append("</div>");

        if (a.TopOwners.Count > 0)
            AppendOwnerWorkload(sb, a);

        if (a.CallVolume.Count > 0)
            AppendCallVolume(sb, a);

        if (a.TopCallbacks.Count > 0)
            AppendCallbacks(sb, a);

        if (a.TopWindows.Count > 0)
            AppendHeavyWindows(sb, a);

        if (a.Spikes.Count > 0)
            AppendSpikes(sb, a);

        if (a.SchedulerJobs.Count > 0 || a.WorstSchedulerBurst is not null)
            AppendScheduler(sb, a);

        AppendDataLinks(sb, captureRoot);
        sb.Append("<div class=\"footer\">Measured CET/Lua runtime evidence only. This report does not make claims about REDscript, native engine, GPU or whole-frame causation.</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendOwnerWorkload(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>Bulk of the CET workload</h2>");
        sb.Append("<div class=\"note\">Start here. This ranks sustained measured CET work. Being first means <b>largest remaining CET workload in this capture</b>, not automatically a problem.</div>");
        sb.Append("<table><thead><tr><th>Owner</th><th class=\"num\">ms/s</th><th class=\"num\">Share</th><th class=\"num\">Calls/s</th><th class=\"num\">Avg call</th><th class=\"num\">Max call</th><th>Weight</th></tr></thead><tbody>");

        var max = Math.Max(0.000001, a.TopOwners.Max(x => x.ExclusiveMsPerSecond));
        foreach (var x in a.TopOwners)
        {
            var share = EffectiveShare(x, a.TotalMsPerSecond);
            var width = Math.Clamp(x.ExclusiveMsPerSecond / max * 100.0, 0, 100);
            sb.Append("<tr><td><b>").Append(H(x.Name)).Append("</b>");
            if (string.Equals(x.Name, "0-Engine", StringComparison.OrdinalIgnoreCase))
                sb.Append(" <span class=\"muted\">(infrastructure)</span>");

            sb.Append("</td><td class=\"num\">").Append(F(x.ExclusiveMsPerSecond))
                .Append("</td><td class=\"num\">").Append(F(share, 1)).Append("%")
                .Append("</td><td class=\"num\">").Append(F(x.CallsPerSecond, 0))
                .Append("</td><td class=\"num\">").Append(F(x.AvgExclusiveUs, 1)).Append(" µs")
                .Append("</td><td class=\"num\">").Append(F(x.MaxExclusiveMs)).Append(" ms")
                .Append("</td><td><div class=\"barwrap\"><div class=\"bar\" style=\"width:")
                .Append(F(width, 1)).Append("%\"></div></div></td></tr>");
        }

        sb.Append("</tbody></table></div>");
    }

    private static void AppendCallVolume(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>Call volume</h2>");
        sb.Append("<div class=\"note\">This is a different question from time cost. Millions of individually cheap calls can expose polling, repeated subscriptions or routing work that a pure ms/s ranking hides.</div>");
        sb.Append("<table><thead><tr><th>Owner</th><th class=\"num\">Calls</th><th class=\"num\">Call share</th><th class=\"num\">Calls/s</th><th class=\"num\">CET ms/s</th></tr></thead><tbody>");

        foreach (var x in a.CallVolume)
        {
            sb.Append("<tr><td><b>").Append(H(x.Name)).Append("</b></td><td class=\"num\">")
                .Append(N(x.Calls)).Append("</td><td class=\"num\">")
                .Append(F(Percent(x.Calls, a.TotalCalls), 1)).Append("%</td><td class=\"num\">")
                .Append(F(x.CallsPerSecond, 0)).Append("</td><td class=\"num\">")
                .Append(F(x.ExclusiveMsPerSecond)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></div>");
    }

    private static void AppendCallbacks(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>Callback hotspots</h2>");
        sb.Append("<div class=\"note\">Rows with the same CET callback boundary are combined across owners. This shows where the mod stack repeatedly enters CET; it does not assume every mod performs the same work inside that callback.</div>");
        sb.Append("<table><thead><tr><th>Callback</th><th class=\"num\">Mods</th><th class=\"num\">Calls/s</th><th class=\"num\">CET ms/s</th><th class=\"num\">Max call</th><th>Largest owner</th></tr></thead><tbody>");

        foreach (var x in a.TopCallbacks)
        {
            sb.Append("<tr><td><span class=\"mono\">").Append(H(JoinCallback(x.Kind, x.Target)))
                .Append("</span></td><td class=\"num\">").Append(x.OwnerCount)
                .Append("</td><td class=\"num\">").Append(F(x.CallsPerSecond, 0))
                .Append("</td><td class=\"num\">").Append(F(x.ExclusiveMsPerSecond))
                .Append("</td><td class=\"num\">").Append(F(x.MaxExclusiveMs)).Append(" ms")
                .Append("</td><td>").Append(H(x.TopOwner)).Append("</td></tr>");
        }

        sb.Append("</tbody></table>");

        if (a.SharedCallbacks.Count > 0)
        {
            sb.Append("<details><summary>Shared callback candidates</summary>");
            sb.Append("<p class=\"muted\">Callbacks used by at least three measured owners, ranked by combined CET work.</p>");
            sb.Append("<table><thead><tr><th>Callback</th><th class=\"num\">Mods</th><th class=\"num\">Calls/s</th><th class=\"num\">CET ms/s</th></tr></thead><tbody>");

            foreach (var x in a.SharedCallbacks)
            {
                sb.Append("<tr><td><span class=\"mono\">").Append(H(JoinCallback(x.Kind, x.Target)))
                    .Append("</span></td><td class=\"num\">").Append(x.OwnerCount)
                    .Append("</td><td class=\"num\">").Append(F(x.CallsPerSecond, 0))
                    .Append("</td><td class=\"num\">").Append(F(x.ExclusiveMsPerSecond))
                    .Append("</td></tr>");
            }

            sb.Append("</tbody></table></details>");
        }

        sb.Append("</div>");
    }

    private static void AppendHeavyWindows(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>Heavy CET workload windows</h2>");
        sb.Append("<div class=\"note\">The native CET timeline uses 50 ms windows. These are the busiest measured CET windows, not whole-engine frame time.</div>");
        sb.Append("<div class=\"two\"><div><h3>Heaviest windows</h3>");
        sb.Append("<table><thead><tr><th>Time</th><th class=\"num\">CET ms</th><th class=\"num\">Calls</th><th>Largest owner</th></tr></thead><tbody>");

        foreach (var x in a.TopWindows.Take(10))
        {
            sb.Append("<tr><td>").Append(F(x.StartMs, 0)).Append("–").Append(F(x.EndMs, 0))
                .Append(" ms</td><td class=\"num\">").Append(F(x.ExclusiveMs))
                .Append("</td><td class=\"num\">").Append(N(x.Calls))
                .Append("</td><td>").Append(H(x.TopOwner)).Append(" <span class=\"muted\">(")
                .Append(F(x.TopOwnerExclusiveMs)).Append(" ms)</span></td></tr>");
        }

        sb.Append("</tbody></table></div><div><h3>Who keeps appearing</h3>");
        sb.Append("<table><thead><tr><th>Owner</th><th class=\"num\">Top windows</th><th class=\"num\">CET ms inside them</th></tr></thead><tbody>");

        foreach (var x in a.HeavyWindowOwners.Take(10))
        {
            sb.Append("<tr><td><b>").Append(H(x.Owner)).Append("</b></td><td class=\"num\">")
                .Append(x.WindowCount).Append("/").Append(a.TopWindows.Count)
                .Append("</td><td class=\"num\">").Append(F(x.ExclusiveMs)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></div></div></div>");
    }

    private static void AppendSpikes(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>Recorded CET callback spikes</h2>");
        sb.Append("<table><thead><tr><th>Owner</th><th>Callback</th><th class=\"num\">Exclusive</th><th class=\"num\">Inclusive</th><th class=\"num\">Capture time</th></tr></thead><tbody>");

        foreach (var x in a.Spikes)
        {
            sb.Append("<tr><td><b>").Append(H(x.Mod)).Append("</b></td><td><span class=\"mono\">")
                .Append(H(JoinCallback(x.Kind, x.Target))).Append("</span></td><td class=\"num\">")
                .Append(F(x.ExclusiveMs)).Append(" ms</td><td class=\"num\">")
                .Append(F(x.InclusiveMs)).Append(" ms</td><td class=\"num\">")
                .Append(F(x.CaptureStartMs, 1)).Append(" ms</td></tr>");
        }

        sb.Append("</tbody></table></div>");
    }

    private static void AppendScheduler(StringBuilder sb, ResultAnalysis a)
    {
        sb.Append("<div class=\"section\"><h2>0-Engine Scheduler</h2>");
        sb.Append("<div class=\"note\"><b>Scheduler timing is measured inside 0-Engine.</b> It shows client job work carried by the Scheduler and must not be added again to the normal CET mod totals.");

        if (a.ZeroEngine is not null)
        {
            sb.Append(" 0-Engine's CET callback boundary measured <b>")
                .Append(F(a.ZeroEngine.ExclusiveMsPerSecond))
                .Append(" ms/s</b>; scheduled jobs inside it measured <b>")
                .Append(F(a.SchedulerTotalMsPerSecond))
                .Append(" ms/s</b> of Scheduler timing.");
        }

        sb.Append("</div>");

        if (a.WorstSchedulerBurst is not null)
        {
            var b = a.WorstSchedulerBurst;
            sb.Append("<div class=\"finding\"><h3>CET Scheduler spike — ")
                .Append(F(b.TotalJobMs)).Append(" ms</h3><div class=\"evidence\">")
                .Append(b.JobCount).Append(" scheduled jobs ran in one frame.");

            if (b.DominantCadenceJobs >= 2 && !string.IsNullOrWhiteSpace(b.DominantCadence))
            {
                sb.Append(" ").Append(b.DominantCadenceJobs)
                    .Append(" of them belong to the ").Append(H(b.DominantCadence))
                    .Append(" cadence group.");
            }

            sb.Append("</div><div class=\"why\">This identifies a real CET-side pile-up of scheduled work in one frame.</div></div>");
        }

        if (a.SchedulerJobs.Count > 0)
        {
            sb.Append("<h3>Largest scheduled workloads</h3>");
            sb.Append("<table><thead><tr><th>Owner</th><th>Job</th><th>Cadence</th><th class=\"num\">Calls/s</th><th class=\"num\">ms/s</th><th class=\"num\">Max</th></tr></thead><tbody>");

            foreach (var x in a.SchedulerJobs.Take(12))
            {
                sb.Append("<tr><td><b>").Append(H(x.Owner)).Append("</b></td><td><span class=\"mono\">")
                    .Append(H(string.IsNullOrWhiteSpace(x.Job) ? x.JobType : x.Job))
                    .Append("</span></td><td>").Append(H(FormatCadence(x.IntervalValue, x.IntervalUnit)))
                    .Append("</td><td class=\"num\">").Append(F(x.CallsPerSecond, 2))
                    .Append("</td><td class=\"num\">").Append(F(x.MsPerSecond))
                    .Append("</td><td class=\"num\">").Append(F(x.MaxMs)).Append(" ms</td></tr>");
            }

            sb.Append("</tbody></table>");
        }

        if (a.SchedulerSpikes.Count > 0)
        {
            sb.Append("<details><summary>Largest individual Scheduler job spikes</summary>");
            sb.Append("<table><thead><tr><th>Owner</th><th>Job</th><th>Cadence</th><th class=\"num\">Duration</th><th class=\"num\">Frame</th></tr></thead><tbody>");

            foreach (var x in a.SchedulerSpikes)
            {
                sb.Append("<tr><td><b>").Append(H(x.Owner)).Append("</b></td><td><span class=\"mono\">")
                    .Append(H(x.Job)).Append("</span></td><td>")
                    .Append(H(FormatCadence(x.IntervalValue, x.IntervalUnit)))
                    .Append("</td><td class=\"num\">").Append(F(x.DurationMs))
                    .Append(" ms</td><td class=\"num\">").Append(x.Frame).Append("</td></tr>");
            }

            sb.Append("</tbody></table></details>");
        }

        sb.Append("</div>");
    }

    private static void AppendDataLinks(StringBuilder sb, string captureRoot)
    {
        sb.Append("<div class=\"section links\"><h2>Full measurement data</h2>");
        sb.Append("<div class=\"note\">The report is the starting point. Nothing was thrown away: the complete native profiler output remains underneath for deeper analysis and future tooling.</div>");
        sb.Append("<div class=\"two\"><div class=\"card\"><h3>Runtime</h3>");

        foreach (var file in RuntimeFiles.OrderBy(x => x))
        {
            if (!File.Exists(Path.Combine(captureRoot, "Data", "Runtime", file)))
                continue;

            sb.Append("<div><a href=\"").Append(Href("Data/Runtime/" + file)).Append("\">")
                .Append(H(file)).Append("</a></div>");
        }

        sb.Append("</div><div class=\"card\"><h3>Scheduler</h3>");

        var schedulerLinks = 0;
        foreach (var file in SchedulerFiles.OrderBy(x => x))
        {
            if (!File.Exists(Path.Combine(captureRoot, "Data", "Scheduler", file)))
                continue;

            schedulerLinks++;
            sb.Append("<div><a href=\"").Append(Href("Data/Scheduler/" + file)).Append("\">")
                .Append(H(file)).Append("</a></div>");
        }

        if (schedulerLinks == 0)
            sb.Append("<div class=\"muted\">No Scheduler attribution in this capture.</div>");

        sb.Append("</div></div><p class=\"muted\">Machine-readable condensed findings: <a href=\"")
            .Append(Href(SummaryFileName)).Append("\">").Append(SummaryFileName)
            .Append("</a>.</p></div>");
    }

    private static void MetricCard(StringBuilder sb, string key, string value, string sub)
    {
        sb.Append("<div class=\"card\"><div class=\"k\">").Append(H(key))
            .Append("</div><div class=\"v\">").Append(value)
            .Append("</div><div class=\"s\">").Append(H(sub))
            .Append("</div></div>");
    }
}
