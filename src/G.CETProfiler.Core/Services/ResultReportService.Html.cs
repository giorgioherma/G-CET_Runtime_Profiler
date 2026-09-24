using System.Text;
using System.Text.Json;

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
.mono{font-family:Consolas,"Courier New",monospace}.two{display:grid;grid-template-columns:1fr 1fr;gap:12px}.healthline{display:grid;grid-template-columns:170px 1fr;gap:8px;padding:4px 0;border-bottom:1px solid #202832}.healthline:last-child{border-bottom:0}
details{background:var(--panel2);border:1px solid var(--line);border-radius:8px;padding:10px 12px;margin-top:10px}summary{cursor:pointer;font-weight:650}
.links a{color:var(--accent);text-decoration:none}.links a:hover{text-decoration:underline}.footer{color:var(--muted);font-size:12px;margin:28px 0 8px}
.chartbox{position:relative;background:var(--panel);border:1px solid var(--line);border-radius:9px;padding:12px;margin-top:10px}.chartbox canvas{display:block;width:100%;height:330px;background:#0b1016;border-radius:6px}.toolbar{display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-bottom:10px}.toolbar button,.toolbar select{background:#171e27;color:var(--text);border:1px solid #34404d;border-radius:6px;padding:6px 9px}.legend{display:flex;gap:14px;flex-wrap:wrap;color:var(--muted);font-size:12px;margin-top:8px}.sw{display:inline-block;width:12px;height:3px;vertical-align:middle;margin-right:5px}.sw.ft{background:#67d7ff}.sw.cpu{background:#ffb86b}.sw.gpu{background:#7ee787}.sw.cet{background:#d9a6f5}.charttip{position:absolute;display:none;pointer-events:none;z-index:4;background:#080c10;border:1px solid #43505e;border-radius:6px;padding:7px 9px;white-space:pre-line;font-size:12px;box-shadow:0 6px 24px #0009}.syncgood{color:#9be564}.synccoarse{color:#ffd166}.syncbad{color:#ff7b72}
@media(max-width:900px){.grid{grid-template-columns:repeat(2,1fr)}.findings,.two{grid-template-columns:1fr}.wrap{padding:14px}}
@media(max-width:520px){.grid{grid-template-columns:1fr}.hero{display:block}}
</style>
</head>
<body><div class="wrap">
""");

        sb.Append("<div class=\"hero\"><div><h1>G-CET Runtime Profiler</h1><div class=\"sub\">")
            .Append(a.FrameTime is null ? "CET capture report" : "CET + CapFrameX capture report")
            .Append("</div>");
        sb.Append("<div class=\"scope\">What CET was doing during this capture: sustained workload, call volume, callback hotspots, heavy CET windows, recorded callback spikes and 0-Engine Scheduler activity.");
        if (a.FrameTime is not null)
            sb.Append(" CapFrameX adds actual frametime, CPU/GPU active readings and synchronized stall overlap.");
        sb.Append("</div></div>");
        sb.Append("<div><span class=\"badge\">")
            .Append(a.FrameTime is null ? "CET-SIDE MEASUREMENT" : "CET + CAPFRAMEX")
            .Append("</span></div></div>");

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

        if (a.FrameTime is not null)
            AppendFrameTime(sb, a.FrameTime);

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
        sb.Append("<div class=\"footer\">CET measurements describe observed CET/Lua work. CapFrameX, when present, is the rendered-frametime layer. Synchronized overlap is evidence of timing coincidence, not a subtraction budget or automatic proof of causation.</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void AppendFrameTime(StringBuilder sb, FrameTimeAnalysis ft)
    {
        sb.Append("<div class=\"section\"><h2>Frametime &amp; CET overlap</h2>");
        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Average", F(ft.AverageFps, 1) + " FPS", F(ft.MeanFrameMs) + " ms mean · " + N(ft.FrameCount) + " frames");
        MetricCard(sb, "P95 / P99", F(ft.P95FrameMs) + " / " + F(ft.P99FrameMs) + " ms", "Median " + F(ft.MedianFrameMs) + " ms");
        MetricCard(sb, "Worst frame", F(ft.MaxFrameMs) + " ms", "≥50 ms " + N(ft.FramesOver50Ms) + " · ≥100 ms " + N(ft.FramesOver100Ms));
        MetricCard(sb, "Frames ≥33.3 ms", N(ft.FramesOver33Ms), "≥25 ms " + N(ft.FramesOver25Ms));
        sb.Append("</div>");

        sb.Append("<div class=\"two\"><div class=\"card\"><h3>CapFrameX capture</h3>")
            .Append("<div class=\"healthline\"><span class=\"muted\">Source</span><span>").Append(H(ft.SourceFile)).Append("</span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">Game</span><span>").Append(H(ft.GameName)).Append("</span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">GPU</span><span>").Append(H(ft.GPU)).Append("</span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">CPU active</span><span>").Append(F(ft.MeanCpuActiveMs)).Append(" ms mean · ").Append(F(ft.P95CpuActiveMs)).Append(" ms P95</span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">GPU active</span><span>").Append(F(ft.MeanGpuActiveMs)).Append(" ms mean · ").Append(F(ft.P95GpuActiveMs)).Append(" ms P95</span></div></div>");

        var syncClass = ft.SyncQuality == "GOOD" ? "syncgood" : ft.SyncQuality == "COARSE" ? "synccoarse" : "syncbad";
        var capRecordLocal = ft.CapFrameXRecordUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "unknown";
        var cetStartLocal = ft.CetStartUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "unknown";
        sb.Append("<div class=\"card\"><h3>Synchronization</h3>")
            .Append("<div class=\"healthline\"><span class=\"muted\">Status</span><span class=\"").Append(syncClass).Append("\"><b>").Append(H(ft.SyncQuality)).Append("</b></span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">Method</span><span>").Append(H(ft.AlignmentMethod)).Append("</span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">CET capture start</span><span>").Append(H(cetStartLocal)).Append(" <span class=\"muted\">(local)</span></span></div>")
            .Append("<div class=\"healthline\"><span class=\"muted\">CapFrameX record/save time</span><span>").Append(H(capRecordLocal)).Append(" <span class=\"muted\">(local)</span></span></div>");

        if (double.IsFinite(ft.DurationDeltaMs))
            sb.Append("<div class=\"healthline\"><span class=\"muted\">Duration difference</span><span>").Append(F(ft.DurationDeltaMs, 1)).Append(" ms</span></div>");
        if (double.IsFinite(ft.RecordLagMs))
            sb.Append("<div class=\"healthline\"><span class=\"muted\">CapFrameX save after aligned end</span><span>").Append(F(ft.RecordLagMs, 1)).Append(" ms</span></div>");

        sb.Append("</div></div>");

        if (!ft.Correlated)
        {
            sb.Append("<div class=\"note\"><b>CapFrameX frametime statistics are valid, but CET/frametime correlation is disabled for this capture.</b> A reliable shared-F11 capture match was not established. CapFrameX's record/save timestamp is metadata only and is never treated as its F11 start time. Raw CapFrameX data is still preserved under <span class=\"mono\">FrameTime/</span>.</div></div>");
            return;
        }

        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Slow frames in high CET", ft.SlowFramesHighCet + " / " + ft.FramesOver33Ms, "High CET = top 10% of 50 ms CET windows");
        MetricCard(sb, "Exact CET spike overlap", N(ft.SlowFramesExactCallback), ft.ExactAlignment ? "Frames ≥33.3 ms crossing a recorded CET callback spike" : "Exact overlap disabled at coarse sync");
        MetricCard(sb, "Scheduler burst overlap", N(ft.SlowFramesSchedulerBurst), ft.ExactAlignment ? "Frames ≥33.3 ms crossing a recorded Scheduler burst" : "Exact overlap disabled at coarse sync");
        MetricCard(sb, "CET-normal slow frames", N(ft.SlowFramesCetNormal), "No high CET window or exact recorded CET spike/burst");
        sb.Append("</div>");

        sb.Append("<div class=\"note\"><b>")
            .Append(ft.TopCetWindowsWithSlowFrame).Append(" of the ").Append(ft.TopCetWindowCount)
            .Append(" heaviest CET windows contained a frame ≥33.3 ms.</b> High-CET windows had slow frames in ")
            .Append(F(ft.HighCetWindowSlowRatePct, 1)).Append("% of windows versus ")
            .Append(F(ft.NormalCetWindowSlowRatePct, 1)).Append("% for the rest. ")
            .Append("50 ms window correlation: Spearman <b>").Append(F(ft.SpearmanWindowCorrelation, 3))
            .Append("</b>, Pearson ").Append(F(ft.PearsonWindowCorrelation, 3)).Append(".</div>");

        sb.Append("<div class=\"chartbox\"><div class=\"toolbar\">")
            .Append("<button onclick=\"gcetFtFull()\">Full capture</button>")
            .Append("<button onclick=\"gcetFtWorst()\">Around worst frame</button>")
            .Append("<label class=\"small\">Scale <select id=\"gcetFtScale\" onchange=\"gcetFtDraw()\"><option value=\"auto\">Auto</option><option value=\"50\">0–50 ms</option><option value=\"100\">0–100 ms</option><option value=\"250\">0–250 ms</option><option value=\"500\">0–500 ms</option></select></label>")
            .Append("</div><canvas id=\"gcetFtChart\"></canvas><div id=\"gcetFtTip\" class=\"charttip\"></div>")
            .Append("<div class=\"legend\"><span><i class=\"sw ft\"></i>Max frametime / CET 50 ms window</span><span><i class=\"sw cpu\"></i>CPU active max</span><span><i class=\"sw gpu\"></i>GPU active max</span><span><i class=\"sw cet\"></i>Measured CET work / 50 ms window</span></div></div>")
            .Append("<div class=\"note\"><b>Read the layers together; do not add or subtract them.</b> CapFrameX is actual rendered frametime. CET is observed script-side work aggregated into the same 50 ms clock windows.</div>");

        if (ft.WorstFrames.Count > 0)
        {
            sb.Append("<h3>Worst frametime events</h3><table><thead><tr><th>Time</th><th class=\"num\">Frame</th><th class=\"num\">CPU active</th><th class=\"num\">GPU active</th><th class=\"num\">CET window</th><th>Largest CET owner</th><th>Aligned CET evidence</th></tr></thead><tbody>");
            foreach (var x in ft.WorstFrames.Take(20))
            {
                sb.Append("<tr><td>").Append(F(x.StartMs / 1000.0, 3)).Append(" s</td><td class=\"num\"><b>")
                    .Append(F(x.FrameMs)).Append(" ms</b></td><td class=\"num\">").Append(F(x.CpuActiveMs))
                    .Append(" ms</td><td class=\"num\">").Append(F(x.GpuActiveMs))
                    .Append(" ms</td><td class=\"num\">").Append(F(x.CetWindowMs))
                    .Append(" ms</td><td>").Append(H(x.TopCetOwner));
                if (!string.IsNullOrWhiteSpace(x.TopCetOwner))
                    sb.Append(" <span class=\"muted\">(").Append(F(x.TopCetOwnerMs)).Append(" ms)</span>");
                sb.Append("</td><td>").Append(H(x.Evidence)).Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }

        var timelineJson = JsonSerializer.Serialize(
            ft.Timeline.Select(x => new
            {
                t = Round(x.StartMs, 3),
                e = Round(x.EndMs, 3),
                ft = Round(x.FrameMaxMs, 4),
                cpu = Round(x.CpuActiveMaxMs, 4),
                gpu = Round(x.GpuActiveMaxMs, 4),
                cet = Round(x.CetMs, 4),
                calls = x.CetCalls,
                owner = x.TopOwner,
                ownerMs = Round(x.TopOwnerCetMs, 4),
                slow = x.SlowFrameCount,
                callback = x.HasCallbackSpike,
                scheduler = x.HasSchedulerBurst,
                high = x.HighCet
            }),
            JsonOptions);
        var worstTime = ft.WorstFrames.FirstOrDefault()?.StartMs ?? 0;

        sb.Append("<script>window.__gcetFt=").Append(timelineJson)
            .Append(";window.__gcetFtWorst=").Append(JsonSerializer.Serialize(worstTime))
            .Append(";</script>");

        sb.Append("""
<script>
(function(){
  const data=window.__gcetFt||[];
  const canvas=document.getElementById('gcetFtChart');
  const tip=document.getElementById('gcetFtTip');
  if(!canvas||!data.length)return;
  let xmin=data[0].t, xmax=data[data.length-1].e;
  function resize(){
    const dpr=window.devicePixelRatio||1;
    const w=Math.max(320,canvas.clientWidth),h=330;
    canvas.width=Math.round(w*dpr);canvas.height=Math.round(h*dpr);
    const ctx=canvas.getContext('2d');ctx.setTransform(dpr,0,0,dpr,0,0);gcetFtDraw();
  }
  function visible(){return data.filter(p=>p.e>=xmin&&p.t<=xmax);}
  window.gcetFtFull=function(){xmin=data[0].t;xmax=data[data.length-1].e;gcetFtDraw();};
  window.gcetFtWorst=function(){const w=window.__gcetFtWorst||0;xmin=Math.max(data[0].t,w-5000);xmax=Math.min(data[data.length-1].e,w+5000);gcetFtDraw();};
  window.gcetFtDraw=function(){
    const ctx=canvas.getContext('2d'),w=canvas.clientWidth,h=330,padL=48,padR=18,padT=12,padB=30;
    ctx.clearRect(0,0,w,h);const v=visible();if(!v.length)return;
    const sel=document.getElementById('gcetFtScale');let ymax=sel&&sel.value!=='auto'?Number(sel.value):0;
    if(!ymax){for(const p of v)ymax=Math.max(ymax,p.ft,p.cpu,p.gpu,p.cet);ymax=Math.max(20,Math.ceil(ymax/10)*10);}
    const px=t=>padL+(t-xmin)/(xmax-xmin)*(w-padL-padR);
    const py=y=>padT+(1-Math.min(y,ymax)/ymax)*(h-padT-padB);
    ctx.strokeStyle='#27313c';ctx.lineWidth=1;ctx.fillStyle='#8794a3';ctx.font='11px Segoe UI';
    for(let i=0;i<=5;i++){const y=ymax*i/5,yy=py(y);ctx.beginPath();ctx.moveTo(padL,yy);ctx.lineTo(w-padR,yy);ctx.stroke();ctx.fillText(y.toFixed(0)+' ms',4,yy+4);}
    function line(key,color,width){
      ctx.strokeStyle=color;ctx.lineWidth=width;ctx.beginPath();let started=false;
      for(const p of v){const val=p[key];if(val<=0)continue;const x=px((p.t+p.e)/2),y=py(val);if(!started){ctx.moveTo(x,y);started=true;}else ctx.lineTo(x,y);}
      ctx.stroke();
    }
    line('cpu','#ffb86b',1);line('gpu','#7ee787',1);line('cet','#d9a6f5',1.4);line('ft','#67d7ff',2);
    ctx.fillStyle='#8794a3';ctx.textAlign='left';ctx.fillText((xmin/1000).toFixed(1)+' s',padL,h-8);ctx.textAlign='right';ctx.fillText((xmax/1000).toFixed(1)+' s',w-padR,h-8);ctx.textAlign='left';
  };
  canvas.addEventListener('mousemove',ev=>{
    const r=canvas.getBoundingClientRect(),padL=48,padR=18;
    const ratio=Math.max(0,Math.min(1,(ev.clientX-r.left-padL)/(r.width-padL-padR)));
    const t=xmin+ratio*(xmax-xmin);
    let lo=0,hi=data.length-1;
    while(lo<hi){const m=(lo+hi)>>1;if(data[m].t<t)lo=m+1;else hi=m;}
    const p=data[Math.max(0,lo-1)];
    if(!p){tip.style.display='none';return;}
    let signal=p.scheduler?'Scheduler burst':p.callback?'Callback spike':p.high?'High CET window':'CET normal';
    tip.textContent=(p.t/1000).toFixed(3)+' s\nFrametime max: '+p.ft.toFixed(2)+' ms\nCPU active max: '+p.cpu.toFixed(2)+' ms\nGPU active max: '+p.gpu.toFixed(2)+' ms\nCET work: '+p.cet.toFixed(2)+' ms\nTop CET owner: '+(p.owner||'—')+'\n'+signal;
    tip.style.display='block';tip.style.left=Math.min(r.width-235,Math.max(8,ev.clientX-r.left+12))+'px';tip.style.top=Math.max(8,ev.clientY-r.top-115)+'px';
  });
  canvas.addEventListener('mouseleave',()=>tip.style.display='none');
  window.addEventListener('resize',resize);requestAnimationFrame(resize);
})();
</script>
""");

        sb.Append("</div>");
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

        sb.Append("</div></div>");

        var frameTimeRoot = Path.Combine(captureRoot, "FrameTime");
        if (Directory.Exists(frameTimeRoot))
        {
            sb.Append("<details><summary>Frame-time companion files</summary><div class=\"links\">");
            foreach (var path in Directory.EnumerateFiles(frameTimeRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                         .Take(40))
            {
                var relative = Path.GetRelativePath(captureRoot, path).Replace('\\', '/');
                sb.Append("<div><a href=\"").Append(Href(relative)).Append("\">")
                    .Append(H(relative)).Append("</a></div>");
            }
            sb.Append("</div></details>");
        }

        sb.Append("<p class=\"muted\">Machine-readable condensed findings: <a href=\"")
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
