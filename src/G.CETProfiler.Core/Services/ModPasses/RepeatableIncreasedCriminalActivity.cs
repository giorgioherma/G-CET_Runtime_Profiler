using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRepeatableIncreasedCriminalActivity(
        SemanticPatchContext context)
    {
        // The current live RICA source already carries its PASS4 multi-rate
        // workers. Do not apply the original cadence rewrite again.
        // Preserve native scheduling, watchdog, rewards and fallback.
        var liveFile = context.FindFile("init.lua", "Mappins.sync", "Diagnostics.snapshot");
        if (liveFile.Text.Contains("TryAttachRicaPass4", StringComparison.Ordinal))
        {
            if (!liveFile.Text.Contains("if pass4Attached then return end", StringComparison.Ordinal)
                || !liveFile.Text.Contains("scheduledDiagnosticsTick", StringComparison.Ordinal)
                || !liveFile.Text.Contains("rewardTick", StringComparison.Ordinal))
                throw new InvalidOperationException("RICA existing PASS4 stage is incomplete or source has drifted.");
            if(liveFile.Text.Contains("-- G-CET semantic:repeatable-increased-criminal-activity-v2",
                    StringComparison.Ordinal))
            {
                var worker=context.TryReadOwnedCoordinator("rica_engine_worker.lua");
                if(worker is null || !worker.StartsWith(
                        "-- G-CET semantic coordinator:repeatable-increased-criminal-activity\n",
                        StringComparison.Ordinal) ||
                   !worker.Contains("id = \"rica.diagnostics_worker\"",StringComparison.Ordinal) ||
                   !worker.Contains("id = \"rica.system\"",StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "RICA pristine source contains six-lane marker without its owned scheduler worker.");
            }
            return SemanticInjectionResult.Skip("RICA current live PASS4 already provides multi-rate 0-Engine scheduling; preserve existing source.");
        }

        var file = context.FindFile(
            "init.lua",
            "processBodyRewards(system)",
            "processCompletionRewards(system)",
            "Mappins.sync(system, Sites)",
            "Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)");

        var text = ReplaceOnce(
            file.Text,
            "    tickElapsed = 0.0,\n    schedulerElapsed = 0.0,\n",
            "    tickElapsed = 0.0,\n" +
            "    rewardElapsed = 0.0,\n" +
            "    mappinElapsed = 0.0,\n" +
            "    diagnosticsElapsed = 0.0,\n" +
            "    schedulerElapsed = 0.0,\n",
            "RICA multi-rate cadence state");

        text = ReplaceOnce(
            text,
            "local function processBodyRewards(system)\n" +
            "    local processed = 0\n" +
            "    while processed < 64 do\n",
            "local function processBodyRewards(system, limit)\n" +
            "    local processed = 0\n" +
            "    limit = limit or 64\n" +
            "    while processed < limit do\n",
            "RICA bounded body rewards");

        text = ReplaceOnce(
            text,
            "local function processCompletionRewards(system)\n" +
            "    local processed = 0\n" +
            "    while processed < 5 do\n",
            "local function processCompletionRewards(system, limit)\n" +
            "    local processed = 0\n" +
            "    limit = limit or 5\n" +
            "    while processed < limit do\n",
            "RICA bounded completion rewards");

        text = ReplaceOnce(
            text,
            "    processBodyRewards(system)\n" +
            "    processCompletionRewards(system)\n" +
            "    Mappins.sync(system, Sites)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "end\n",
            "end\n\n" +
            "local function rewardTick()\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    processBodyRewards(system, 4)\n" +
            "    processCompletionRewards(system, 1)\n" +
            "end\n\n" +
            "local function mappinTick()\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    Mappins.sync(system, Sites)\n" +
            "end\n\n" +
            "local function diagnosticsTick()\n" +
            "    if Mod.settings.diagnostics.enabled == false then return end\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)\n" +
            "    Diagnostics.configuration(Mod.settings)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "end\n",
            "RICA split runtime lanes");

        var opening = FindOnUpdateOpening(text, "delta");
        text = ReplaceOnce(
            text,
            opening +
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n",
            opening +
            "    Mod.rewardElapsed = Mod.rewardElapsed + delta\n" +
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.mappinElapsed = Mod.mappinElapsed + delta\n" +
            "    Mod.diagnosticsElapsed = Mod.diagnosticsElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    if Mod.rewardElapsed >= 0.25 then\n" +
            "        Mod.rewardElapsed = Mod.rewardElapsed % 0.25\n" +
            "        local ok, err = pcall(rewardTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Reward tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"reward_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n",
            "RICA reward cadence");

        text = ReplaceOnce(
            text,
            "    if Mod.schedulerElapsed >= 10.0 then\n",
            "    if Mod.mappinElapsed >= 1.0 then\n" +
            "        Mod.mappinElapsed = Mod.mappinElapsed % 1.0\n" +
            "        local ok, err = pcall(mappinTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Mappin tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"mappin_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n" +
            "    if Mod.diagnosticsElapsed >= 5.0 then\n" +
            "        Mod.diagnosticsElapsed = Mod.diagnosticsElapsed % 5.0\n" +
            "        local ok, err = pcall(diagnosticsTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Diagnostics tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"diagnostics_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n" +
            "    if Mod.schedulerElapsed >= 10.0 then\n",
            "RICA mappin diagnostics cadence");

        // Build the scheduler and all six jobs as part of this SAME
        // pristine-source rule, not as a repair of a prior PASS4 ZIP.
        text = ComposeRicaPristineScheduler(context, text);
        ComposeRicaIncrementalDiagnostics(context);
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "RICA pristine source: composed bounded reward/state/mappin/diagnostic/system lanes and an owned six-job 0-Engine Scheduler coordinator with author callback fallback.");
    }

    private static string ComposeRicaPristineScheduler(
        SemanticPatchContext context, string source)
    {
        var originalCrLf = source.Contains("\r\n", StringComparison.Ordinal);
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var opening = FindOnUpdateOpening(text, "delta");
        const string initEvent = "registerForEvent(\"onInit\", function()\n";
        const string shutdownEvent = "registerForEvent(\"onShutdown\", function()\n";
        if(text.Split(initEvent,StringSplitOptions.None).Length!=2 ||
           text.Split(shutdownEvent,StringSplitOptions.None).Length!=2 ||
           !text.Contains("local function rewardTick()",StringComparison.Ordinal) ||
           !text.Contains("local function mappinTick()",StringComparison.Ordinal) ||
           !text.Contains("local function diagnosticsTick()",StringComparison.Ordinal) ||
           !text.Contains("local function runtimeTick()",StringComparison.Ordinal) ||
           !text.Contains("local function processCompletionRewards(system, limit)",StringComparison.Ordinal))
            throw new InvalidOperationException("RICA pristine six-lane scheduler ownership is not source-proven.");

        const string installer = """
-- G-CET semantic:repeatable-increased-criminal-activity-v2
-- Six jobs are generated from untouched author source with the existing rule.
-- The original onUpdate is the exact fallback if 0-Engine is unavailable.
local ricaBaseInitialized = false
local pass4Attached = false
local pass4AttachInProgress = false
local TryAttachRicaPass4 = nil
local pass4DiagnosticsWorkerJob = nil
local ricaDiagnosticsRequested = false
local ricaDiagnosticsIdleCycles = 5

local function scheduledDiagnosticsTick()
    if Mod.settings.diagnostics.enabled == false then return end
    local system = Bridge.system()
    if not system or not Game.GetPlayer() then return end
    local active = 0
    pcall(function() active = math.max(0, system:GetActiveReplayCount()) end)
    if active > 0 then
        ricaDiagnosticsIdleCycles = 0
    else
        ricaDiagnosticsIdleCycles = ricaDiagnosticsIdleCycles + 1
        if ricaDiagnosticsIdleCycles < 6 then return end
        ricaDiagnosticsIdleCycles = 0
    end
    ricaDiagnosticsRequested = true
    if pass4DiagnosticsWorkerJob and
       type(pass4DiagnosticsWorkerJob.Resume) == "function" then
        pass4DiagnosticsWorkerJob.Resume()
    end
end

local function diagnosticsWorkerTick()
    if not ricaDiagnosticsRequested then
        if pass4DiagnosticsWorkerJob and
           type(pass4DiagnosticsWorkerJob.Pause) == "function" then
            pass4DiagnosticsWorkerJob.Pause()
        end
        return
    end
    ricaDiagnosticsRequested = false
    -- Full source-author diagnostic fallback until an independently proven
    -- per-site snapshot stage is composed; do not claim incremental parity.
    local ok, err = pcall(diagnosticsTick)
    if not ok then Diagnostics.event("diagnostics_tick_failed", {error=tostring(err)}) end
    if pass4DiagnosticsWorkerJob and
       type(pass4DiagnosticsWorkerJob.Pause) == "function" then
        pass4DiagnosticsWorkerJob.Pause()
    end
end

local function ricaSystemJob()
    local system = Bridge.system()
    if not system then return end
    local ok, err = pcall(function() system:Tick() end)
    if not ok then
        print("[RICA] Scheduler tick failed: " .. tostring(err))
        Diagnostics.event("scheduler_tick_failed", {error=tostring(err)})
    end
end

TryAttachRicaPass4 = function()
    if not ricaBaseInitialized then return false end
    if pass4Attached then return true end
    if pass4AttachInProgress then return false end
    pass4AttachInProgress = true
    local created = {}
    local okAttach = pcall(function()
        local worker = require("GCET_Semantic/rica_engine_worker")
        pass4DiagnosticsWorkerJob = worker.attach(created, {
            diagnosticsWorkerTick = diagnosticsWorkerTick,
            rewardTick = rewardTick,
            runtimeStateTick = runtimeTick,
            mappinTick = mappinTick,
            scheduledDiagnosticsTick = scheduledDiagnosticsTick,
            schedulerTick = ricaSystemJob
        })
        pass4Attached = true
    end)
    pass4AttachInProgress = false
    if not okAttach then
        for _, handle in ipairs(created) do
            if handle and type(handle.Cancel) == "function" then
                pcall(handle.Cancel)
            end
        end
        pass4DiagnosticsWorkerJob = nil
        return false
    end
    return true
end

""";
        text = ReplaceOnce(text, initEvent, installer + initEvent,
            "RICA pristine one-pass scheduler before lifecycle registration");

        // A generic source registrar may live between onInit and onUpdate.
        // Locate the last top-level callback close BEFORE the proven update
        // opening, rather than requiring those two events to be adjacent.
        var updateStart=text.IndexOf(opening,StringComparison.Ordinal);
        var initStart=text.IndexOf(initEvent,StringComparison.Ordinal);
        var initClose=updateStart<0?-1:
            text.LastIndexOf("\nend)\n",updateStart,StringComparison.Ordinal);
        if(initStart<0 || initClose<initStart ||
           text.Split(initEvent,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException(
                "RICA onInit callback close cannot be proven before generated frame routing.");
        text=text.Insert(initClose,"\n    ricaBaseInitialized = true\n    TryAttachRicaPass4()");
        text = ReplaceOnce(text,opening,
            opening+"    if pass4Attached then return end\n"+
            "    if not ricaBaseInitialized then return end\n"+
            "    TryAttachRicaPass4()\n"+
            "    if pass4Attached then return end\n",
            "RICA author fallback and atomic scheduler takeover");
        text = ReplaceOnce(text,shutdownEvent,
            shutdownEvent+
            "    ricaDiagnosticsRequested = false\n"+
            "    if pass4DiagnosticsWorkerJob and type(pass4DiagnosticsWorkerJob.Pause) == \"function\" then\n"+
            "        pass4DiagnosticsWorkerJob.Pause()\n"+
            "    end\n",
            "RICA worker shutdown gate");
        context.WriteCoordinator("rica_engine_worker.lua",RicaEngineWorkerCode(context.RuleId));
        return originalCrLf ? text.Replace("\n","\r\n",StringComparison.Ordinal) : text;
    }

    // Preserve the author's payload and per-site projection while splitting
    // one expensive synchronous snapshot into a source-owned frame cursor.
    // This is generated in the PRIMARY RICA rule from untouched source.
    private static void ComposeRicaIncrementalDiagnostics(SemanticPatchContext context)
    {
        var file=context.FindFile("modules/diagnostics.lua",
            "function Diagnostics.snapshot(system, sites, settings, mappins, force)",
            "local function read(system, fallback, fn)",
            "function Diagnostics.shutdown()");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string opening="function Diagnostics.snapshot(system, sites, settings, mappins, force)\n";
        const string siteOpening="    for _, site in ipairs(sites.list) do\n";
        const string postOpening="\n    local raw = encode(payload)\n";
        const string shutdownOpening="\nfunction Diagnostics.shutdown()\n";
        var start=source.IndexOf(opening,StringComparison.Ordinal);
        var site=start<0?-1:source.IndexOf(siteOpening,start,StringComparison.Ordinal);
        var post=site<0?-1:source.IndexOf(postOpening,site,StringComparison.Ordinal);
        var finish=post<0?-1:source.IndexOf(shutdownOpening,post,StringComparison.Ordinal);
        if(start<0||site<start||post<site||finish<post ||
           source.IndexOf(opening,start+opening.Length,StringComparison.Ordinal)>=0 ||
           source.IndexOf(siteOpening,site+siteOpening.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("RICA diagnostic per-site snapshot is not uniquely source-proven.");
        var begin=source[start..site];
        var sites=source[site..post];
        if(!begin.Contains("local payload = {",StringComparison.Ordinal) ||
           !begin.Contains("        sites = {},\n    }\n",StringComparison.Ordinal) ||
           !sites.Contains("payload.sites[#payload.sites + 1] = {",StringComparison.Ordinal) ||
           !sites.Contains("nextEligibleAt - now",StringComparison.Ordinal) ||
           !source[post..finish].Contains("Diagnostics.lastSnapshot = raw",StringComparison.Ordinal))
            throw new InvalidOperationException("RICA authored diagnostic payload or snapshot completion changed.");
        begin=begin.Replace(opening,
            "function Diagnostics.beginSnapshot(system, sites, settings, mappins, force)\n",
            StringComparison.Ordinal);
        begin=begin.Replace(
            "    if (not Diagnostics.enabled and not force) or not system then return false end",
            "    if (not Diagnostics.enabled and not force) or not system then\n"+
            "        Diagnostics.snapshotWork = nil\n        return false\n    end",
            StringComparison.Ordinal);
        begin=begin.Replace("    local payload = {",
            "    Diagnostics.snapshotWork = {\n"+
            "        index = 1, now = now, force = force == true,\n"+
            "        payload = {",StringComparison.Ordinal);
        begin=begin.Replace("        sites = {},\n    }\n",
            "        sites = {},\n        }\n    }\n    return true\nend\n",
            StringComparison.Ordinal);
        sites=sites.Replace(siteOpening,
            """
function Diagnostics.isSnapshotActive()
    return Diagnostics.snapshotWork ~= nil
end

function Diagnostics.cancelSnapshot()
    Diagnostics.snapshotWork = nil
end

function Diagnostics.snapshotStep(system, sites, maxSites)
    local work = Diagnostics.snapshotWork
    if not work then return true end
    if (not Diagnostics.enabled and not work.force) or not system then
        Diagnostics.snapshotWork = nil
        return true
    end
    local limit = math.max(1, tonumber(maxSites) or 1)
    local processed = 0
    while work.index <= #sites.list and processed < limit do
        local site = sites.list[work.index]
        work.index = work.index + 1
        processed = processed + 1
""",StringComparison.Ordinal)
            .Replace("payload.sites[#payload.sites + 1]",
                "work.payload.sites[#work.payload.sites + 1]",StringComparison.Ordinal)
            .Replace("nextEligibleAt - now","nextEligibleAt - work.now",StringComparison.Ordinal);
        var signature = """
local function gcetRicaSnapshotSignature(payload)
    local schedule = payload.appliedSchedule or {}
    local scheduler = payload.scheduler or {}
    local markers = payload.markers or {}
    local values = {
        tostring(schedule.enabled), tostring(schedule.poolSize),
        tostring(schedule.effectivePoolSize), tostring(schedule.cooldownHours),
        tostring(schedule.rosterPercent), tostring(schedule.reinforcements),
        tostring(schedule.cleanupSeconds), tostring(schedule.cleanupDistance),
        tostring(scheduler.active), tostring(scheduler.eligible),
        tostring(scheduler.tracked), tostring(scheduler.pendingRewards),
        tostring(scheduler.totalClears),
        tostring(markers.discovered), tostring(markers.registered),
        tostring(markers.schedulerActive), tostring(markers.target),
        tostring(markers.eligible), tostring(markers.tracked),
        tostring(markers.registrationFailures)
    }
    for _, site in ipairs(payload.sites or {}) do
        for _, key in ipairs({
            "id", "status", "enabled", "vanillaEligible", "ownedActivation",
            "cycle", "lastClearedAt", "nextEligibleAt", "cleanupReadyAt",
            "cooldownOverrideHours", "trackedActors", "bossLoaded"
        }) do
            values[#values+1] = tostring(site[key])
        end
    end
    return table.concat(values, "|")
end

local function gcetRicaFinishSnapshot(work)
    local signature = gcetRicaSnapshotSignature(work.payload)
    local wall = os.time()
    if work.force or signature ~= Diagnostics.lastSnapshot
        or wall - Diagnostics.lastSnapshotAt >= 60 then
        Diagnostics.event("state_snapshot", work.payload, work.force)
        Diagnostics.lastSnapshot = signature
        Diagnostics.lastSnapshotAt = wall
    end
    Diagnostics.snapshotWork = nil
end

""";
        var afterSites = """
    if work.index > #sites.list then
        gcetRicaFinishSnapshot(work)
        return true
    end
    return false
end

function Diagnostics.snapshot(system, sites, settings, mappins, force)
    if not Diagnostics.beginSnapshot(system, sites, settings, mappins, force) then
        return false
    end
    while Diagnostics.isSnapshotActive() do
        Diagnostics.snapshotStep(system, sites, #sites.list)
    end
    return true
end
""";
        var merged=source[..start]+signature+"\n"+begin+sites+afterSites+source[finish..];
        const string field="    lastSnapshotAt = 0,\n}";
        const string reset="    Diagnostics.lastSnapshotAt = 0\n    rotateIfLarge()";
        const string shutdown="function Diagnostics.shutdown()\n";
        if(merged.Split(field,StringSplitOptions.None).Length!=2 ||
           merged.Split(reset,StringSplitOptions.None).Length!=2 ||
           merged.Split(shutdown,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("RICA diagnostics session lifecycle changed.");
        merged=merged.Replace(field,"    lastSnapshotAt = 0,\n    snapshotWork = nil,\n}",StringComparison.Ordinal)
            .Replace(reset,"    Diagnostics.lastSnapshotAt = 0\n    Diagnostics.snapshotWork = nil\n    rotateIfLarge()",StringComparison.Ordinal)
            .Replace(shutdown,"function Diagnostics.shutdown()\n    Diagnostics.snapshotWork = nil\n",StringComparison.Ordinal);
        context.Write(file,crlf?merged.Replace("\n","\r\n",StringComparison.Ordinal):merged);
    }

}
