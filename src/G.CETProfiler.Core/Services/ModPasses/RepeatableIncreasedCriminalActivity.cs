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
            var revised=UpgradeRicaRuntimeSettingsCache(liveFile.Text);
            if(revised!=liveFile.Text) context.Write(liveFile,revised);
            var markers=ComposeRicaEmptyMarkerWork(context);
            var diagnosticsCache=ComposeRicaConfigurationIdleCache(context);
            if(revised!=liveFile.Text || markers || diagnosticsCache)
                return SemanticInjectionResult.Success(
                    "RICA complete PASS4: eliminates idle settings materialization and empty marker scans.");
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
            "-- G-CET original-1Hz-safe RICA reward idle prefilter v1\n" +
            "local ricaRewardIdleChecks = 0\n" +
            "local function rewardTick()\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    local ok, bodySite, completionSerial = pcall(function()\n" +
            "        return system:GetPendingBodyRewardSiteID(), system:GetPendingRewardSerial()\n" +
            "    end)\n" +
            "    if ok and (bodySite == nil or bodySite == \"\")\n" +
            "        and type(completionSerial) == \"number\" and completionSerial <= 0 then\n" +
            "        ricaRewardIdleChecks = ricaRewardIdleChecks + 1\n" +
            "        -- Author fallback remains a complete 1Hz poll. Native status\n" +
            "        -- getters are hints only: a missed signal cannot suppress rewards.\n" +
            "        if ricaRewardIdleChecks < 4 then return end\n" +
            "    end\n" +
            "    ricaRewardIdleChecks = 0\n" +
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
        text = ComposeRicaRuntimeSettingsSignature(text);
        text = ComposeRicaPristineScheduler(context, text);
        ComposeRicaIncrementalDiagnostics(context);
        ComposeRicaEmptyMarkerWork(context);
        ComposeRicaConfigurationIdleCache(context);
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "RICA pristine source: composed bounded reward/state/mappin/diagnostic/system lanes and an owned six-job 0-Engine Scheduler coordinator with author callback fallback.");
    }

    // REDscript GetActiveReplayCount() counts vanilla-eligible states whose
    // status is Active. GetStatus(site.id)==1 is the exact marker predicate.
    // A zero active count plus an empty owned registry proves the entire
    // source-order registration/unregistration loop is a no-op. All summary
    // counters and failure recovery still execute. Unknown native read -> scan.
    // This belongs to the existing RICA semantic owner, not another rule.
    private static bool ComposeRicaEmptyMarkerWork(SemanticPatchContext context)
    {
        var file=context.FindFile("modules/mappins.lua",
            "function Mappins.sync(system, sites)","Mappins.owned","Mappins.registered");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string marker="-- G-CET semantic:repeatable-increased-criminal-activity";
        const string changed="-- __gcetRicaEmptyMarkerWorkV1: native zero + empty owned registry";
        var start=text.IndexOf("function Mappins.sync(system, sites)",StringComparison.Ordinal);
        var end=text.IndexOf("\nfunction Mappins.clear()",start,StringComparison.Ordinal);
        if(start<0 || end<=start)
            throw new InvalidOperationException("RICA marker synchronization ownership drift.");
        var body=text[start..end];
        const string oldStart="    local active = 0\n    for _, site in ipairs(sites.list) do\n";
        const string newStart="    local active = 0\n" +
            "    "+changed+"\n" +
            "    local skipEmpty = false\n" +
            "    if next(Mappins.owned) == nil then\n" +
            "        local ok, count = pcall(system.GetActiveReplayCount, system)\n" +
            "        skipEmpty = ok and type(count) == \"number\" and count == 0\n" +
            "    end\n" +
            "    if not skipEmpty then\n" +
            "    for _, site in ipairs(sites.list) do\n";
        const string oldEnd="    end\n    Mappins.registered = 0\n";
        const string newEnd="    end\n    end\n    Mappins.registered = 0\n";
        if(body.Contains(changed,StringComparison.Ordinal))
        {
            if(body.Split(changed,StringSplitOptions.None).Length!=2 ||
                !body.Contains(newStart,StringComparison.Ordinal) ||
                !body.Contains(newEnd,StringComparison.Ordinal) ||
                body.Contains("skipEmpty = true",StringComparison.Ordinal))
                throw new InvalidOperationException("RICA empty marker work postconditions drifted.");
            return false;
        }
        if(body.Split(oldStart,StringSplitOptions.None).Length!=2 ||
            body.Split(oldEnd,StringSplitOptions.None).Length!=2 ||
            !body.Contains("pcall(function() shouldExist = system:GetStatus(site.id) == 1 end)",
                StringComparison.Ordinal) ||
            !body.Contains("if shouldExist and not Mappins.owned[site.id] then",StringComparison.Ordinal) ||
            !body.Contains("elseif not shouldExist and Mappins.owned[site.id] then",StringComparison.Ordinal))
            throw new InvalidOperationException("RICA marker original active/cleanup structure drifted.");
        body=body.Replace(oldStart,newStart,StringComparison.Ordinal)
            .Replace(oldEnd,newEnd,StringComparison.Ordinal);
        var revised=text[..start]+body+text[end..];
        if(!revised.Split('\n').Any(line=>line.Trim().Equals(marker,StringComparison.Ordinal)))
            revised=marker+"\n"+revised;
        context.Write(file,crlf?revised.Replace("\n","\r\n",StringComparison.Ordinal):revised);
        return true;
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
local ricaDiagnosticsQueued = false
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
    if ricaDiagnosticsRequested or Diagnostics.isSnapshotActive() then
        ricaDiagnosticsQueued = true
    else
        ricaDiagnosticsRequested = true
    end
    if pass4DiagnosticsWorkerJob and
       type(pass4DiagnosticsWorkerJob.Resume) == "function" then
        pass4DiagnosticsWorkerJob.Resume()
    end
end

local function diagnosticsWorkerTick()
    if not ricaDiagnosticsRequested and not Diagnostics.isSnapshotActive() then
        if pass4DiagnosticsWorkerJob and
           type(pass4DiagnosticsWorkerJob.Pause) == "function" then
            pass4DiagnosticsWorkerJob.Pause()
        end
        return
    end
    local system = Bridge.system()
    if not system or not Game.GetPlayer() then
        Diagnostics.cancelSnapshot()
        ricaDiagnosticsRequested = false
        ricaDiagnosticsQueued = false
        if pass4DiagnosticsWorkerJob and
           type(pass4DiagnosticsWorkerJob.Pause) == "function" then
            pass4DiagnosticsWorkerJob.Pause()
        end
        return
    end
    if ricaDiagnosticsRequested and not Diagnostics.isSnapshotActive() then
        ricaDiagnosticsRequested = false
        Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)
        Diagnostics.configuration(Mod.settings)
        if not Diagnostics.beginSnapshot(system, Sites, Mod.settings, Mappins) then
            ricaDiagnosticsQueued = false
            if pass4DiagnosticsWorkerJob and
               type(pass4DiagnosticsWorkerJob.Pause) == "function" then
                pass4DiagnosticsWorkerJob.Pause()
            end
        end
        -- Always defer first site read until the next rendered frame.
        return
    end
    if Diagnostics.isSnapshotActive() then
        local done = Diagnostics.snapshotStep(system, Sites, 1)
        if done and ricaDiagnosticsQueued then
            ricaDiagnosticsQueued = false
            ricaDiagnosticsRequested = true
        elseif done and pass4DiagnosticsWorkerJob and
               type(pass4DiagnosticsWorkerJob.Pause) == "function" then
            pass4DiagnosticsWorkerJob.Pause()
        end
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
            "    ricaDiagnosticsQueued = false\n"+
            "    Diagnostics.cancelSnapshot()\n"+
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


    // The author calls Diagnostics.configuration every 1Hz in runtimeTick,
    // including after the runtime settings snapshot was already applied.
    // The original serializes an unchanged nested JSON tree before comparing
    // it with the previous JSON. Compare ALL author config keys without
    // allocation, but still serialize on any change, forced export, unfamiliar
    // table/metatable or excessive nesting. Never treat only schedule settings
    // as complete: reward/diagnostic/site changes also belong to this log.
    private static bool ComposeRicaConfigurationIdleCache(SemanticPatchContext context)
    {
        var file=context.FindFile("modules/diagnostics.lua",
            "function Diagnostics.configuration(settings, force)",
            "Diagnostics.lastConfiguration", "Diagnostics.event");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string marker="-- G-CET semantic:rica-configuration-idle-cache-v1";
        if(source.Contains(marker,StringComparison.Ordinal))
        {
            if(source.Split(marker,StringSplitOptions.None).Length!=2 ||
               !source.Contains("gcetRicaConfigEqual(settings, gcetRicaConfigSnapshot, 0)",
                   StringComparison.Ordinal) ||
               !source.Contains("local snapshot, valid = gcetRicaConfigCopy(settings, 0)",
                   StringComparison.Ordinal) ||
               !source.Contains("gcetRicaConfigSnapshot = valid and snapshot or nil",
                   StringComparison.Ordinal) ||
               !source.Contains("if force or raw ~= Diagnostics.lastConfiguration then",
                   StringComparison.Ordinal))
                throw new InvalidOperationException("RICA configuration cache structurally incomplete.");
            return false;
        }
        const string original="""
function Diagnostics.configuration(settings, force)
    if not Diagnostics.enabled and not force then return end
    local payload = {
        schedule = settings.schedule,
        sites = settings.sites,
        combat = settings.combat,
        reward = settings.reward,
        diagnostics = settings.diagnostics,
    }
    local raw = encode(payload)
    if force or raw ~= Diagnostics.lastConfiguration then
        Diagnostics.event("configuration", payload, force)
        Diagnostics.lastConfiguration = raw
    end
end
""";
        if(source.Split(original,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("RICA author configuration serializer changed: occurrences=" +
                (source.Split(original,StringSplitOptions.None).Length-1) +
                " sourceLength=" + source.Length +
                " hasFunction=" + source.Contains("function Diagnostics.configuration(settings, force)",StringComparison.Ordinal) +
                " hasEncode=" + source.Contains("    local raw = encode(payload)",StringComparison.Ordinal));
        const string replacement="""
-- G-CET semantic:rica-configuration-idle-cache-v1
local gcetRicaConfigSnapshot = nil
local function gcetRicaConfigEqual(a, b, depth)
    if type(a) ~= type(b) then return false end
    if type(a) ~= "table" then return a == b end
    if depth > 8 or getmetatable(a) ~= nil or getmetatable(b) ~= nil then return false end
    for k, v in pairs(a) do
        if not gcetRicaConfigEqual(v, b[k], depth + 1) then return false end
    end
    for k in pairs(b) do
        if a[k] == nil then return false end
    end
    return true
end
local function gcetRicaConfigCopy(value, depth)
    if type(value) ~= "table" then
        if type(value) == "number" or type(value) == "boolean"
            or type(value) == "string" or value == nil then return value, true end
        return nil, false
    end
    if depth > 8 or getmetatable(value) ~= nil then return nil, false end
    local copy = {}
    for key, item in pairs(value) do
        if type(key) ~= "string" and type(key) ~= "number" then return nil, false end
        local cloned, valid = gcetRicaConfigCopy(item, depth + 1)
        if not valid then return nil, false end
        copy[key] = cloned
    end
    return copy, true
end

function Diagnostics.configuration(settings, force)
    if not Diagnostics.enabled and not force then return end
    if not force and gcetRicaConfigSnapshot ~= nil
        and gcetRicaConfigEqual(settings, gcetRicaConfigSnapshot, 0) then return end
    local payload = {
        schedule = settings.schedule,
        sites = settings.sites,
        combat = settings.combat,
        reward = settings.reward,
        diagnostics = settings.diagnostics,
    }
    local raw = encode(payload)
    if force or raw ~= Diagnostics.lastConfiguration then
        Diagnostics.event("configuration", payload, force)
        Diagnostics.lastConfiguration = raw
    end
    local snapshot, valid = gcetRicaConfigCopy(settings, 0)
    gcetRicaConfigSnapshot = valid and snapshot or nil
end
""";
        var result=source.Replace(original,replacement,StringComparison.Ordinal);
        context.Write(file,crlf?result.Replace("\n","\r\n",StringComparison.Ordinal):result);
        return true;
    }

    // Previous emitted exact source is a supported input, never required
    // to generate the optimization from untouched authored source.
    private const string RicaRuntimeSignaturePrevious = """
local function gcetRicaRuntimeSettingsSignature()
    local settings = Mod.settings
    local schedule, combat, general = settings.schedule, settings.combat, settings.general
    local values = {
        tostring(schedule.enabled), tostring(schedule.poolSize),
        tostring(schedule.cooldownHours), tostring(schedule.rosterPercent),
        tostring(schedule.reinforcements), tostring(schedule.cleanupSeconds),
        tostring(schedule.cleanupDistance), tostring(general.changeMapMarkerColor),
        tostring(combat.heightenedAwareness), tostring(combat.regularHealth),
        tostring(combat.regularDamage), tostring(combat.regularArmor),
        tostring(combat.regularQuickhack), tostring(combat.bossHealth),
        tostring(combat.bossDamage), tostring(combat.bossArmor),
        tostring(combat.bossQuickhack), tostring(combat.growthPerClear),
        tostring(combat.growthCap),
    }
    for _, site in ipairs(Sites.list) do
        local config = settings.sites[site.id]
        values[#values+1] = tostring(config.enabled)
        values[#values+1] = tostring(config.cooldownOverrideHours)
    end
    return table.concat(values, ":")
end

""";
    private const string RicaRuntimeSignatureCurrent = """
local function gcetRicaRuntimeSettingsMatches(previous)
    if type(previous) ~= "table" then return false end
    local settings = Mod.settings
    local schedule, combat, general = settings.schedule, settings.combat, settings.general
    if previous[1] ~= schedule.enabled then return false end
    if previous[2] ~= schedule.poolSize then return false end
    if previous[3] ~= schedule.cooldownHours then return false end
    if previous[4] ~= schedule.rosterPercent then return false end
    if previous[5] ~= schedule.reinforcements then return false end
    if previous[6] ~= schedule.cleanupSeconds then return false end
    if previous[7] ~= schedule.cleanupDistance then return false end
    if previous[8] ~= general.changeMapMarkerColor then return false end
    if previous[9] ~= combat.heightenedAwareness then return false end
    if previous[10] ~= combat.regularHealth then return false end
    if previous[11] ~= combat.regularDamage then return false end
    if previous[12] ~= combat.regularArmor then return false end
    if previous[13] ~= combat.regularQuickhack then return false end
    if previous[14] ~= combat.bossHealth then return false end
    if previous[15] ~= combat.bossDamage then return false end
    if previous[16] ~= combat.bossArmor then return false end
    if previous[17] ~= combat.bossQuickhack then return false end
    if previous[18] ~= combat.growthPerClear then return false end
    if previous[19] ~= combat.growthCap then return false end
    if type(previous.sites) ~= "table" or previous.siteCount ~= #Sites.list then return false end
    for i, site in ipairs(Sites.list) do
        local config = settings.sites[site.id]
        if previous.sites[2 * i - 1] ~= config.enabled
            or previous.sites[2 * i] ~= config.cooldownOverrideHours then
            return false
        end
    end
    return true
end

local function gcetRicaRuntimeSettingsSignature()
    -- Capture a new primitive snapshot only after settings have been applied.
    -- Steady gameplay compares primitives without tables or strings.
    local settings = Mod.settings
    local schedule, combat, general = settings.schedule, settings.combat, settings.general
    local values = {
        schedule.enabled,
        schedule.poolSize,
        schedule.cooldownHours,
        schedule.rosterPercent,
        schedule.reinforcements,
        schedule.cleanupSeconds,
        schedule.cleanupDistance,
        general.changeMapMarkerColor,
        combat.heightenedAwareness,
        combat.regularHealth,
        combat.regularDamage,
        combat.regularArmor,
        combat.regularQuickhack,
        combat.bossHealth,
        combat.bossDamage,
        combat.bossArmor,
        combat.bossQuickhack,
        combat.growthPerClear,
        combat.growthCap
    }
    local sites = {}
    for i, site in ipairs(Sites.list) do
        local config = settings.sites[site.id]
        sites[2 * i - 1] = config.enabled
        sites[2 * i] = config.cooldownOverrideHours
    end
    values.sites = sites
    values.siteCount = #Sites.list
    return values
end

""";

    private static string UpgradeRicaRuntimeSettingsCache(string input)
    {
        bool crlf=input.Contains("\r\n",StringComparison.Ordinal);
        var source=input.Replace("\r\n","\n",StringComparison.Ordinal);
        var prior=RicaRuntimeSignaturePrevious.Replace("\r\n","\n",StringComparison.Ordinal);
        var current=RicaRuntimeSignatureCurrent.Replace("\r\n","\n",StringComparison.Ordinal);
        if(source.Contains("local function gcetRicaRuntimeSettingsMatches(previous)",StringComparison.Ordinal))
        {
            if(source.Split(current,StringSplitOptions.None).Length!=2 ||
               !source.Contains("    if force ~= true and gcetRicaRuntimeSettingsMatches(Mod.lastAppliedRuntimeSignature) then",StringComparison.Ordinal) ||
               !source.Contains("    Mod.lastAppliedRuntimeSignature = gcetRicaRuntimeSettingsSignature()",StringComparison.Ordinal))
                throw new InvalidOperationException("RICA primitive settings cache source drift.");
            return input;
        }
        if(!source.Contains("local function gcetRicaRuntimeSettingsSignature()",StringComparison.Ordinal))
            return input;
        if(source.Split(prior,StringSplitOptions.None).Length!=2 ||
           source.Split("    local runtimeSignature=gcetRicaRuntimeSettingsSignature()\n    if force ~= true and Mod.lastAppliedRuntimeSignature == runtimeSignature then",StringSplitOptions.None).Length!=2 ||
           source.Split("    Mod.lastAppliedRuntimeSignature = runtimeSignature",StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("RICA prior generated runtime settings signature source drift.");
        source=source.Replace(prior,current,StringComparison.Ordinal)
            .Replace("    local runtimeSignature=gcetRicaRuntimeSettingsSignature()\n    if force ~= true and Mod.lastAppliedRuntimeSignature == runtimeSignature then","    if force ~= true and gcetRicaRuntimeSettingsMatches(Mod.lastAppliedRuntimeSignature) then",StringComparison.Ordinal)
            .Replace("    Mod.lastAppliedRuntimeSignature = runtimeSignature","    Mod.lastAppliedRuntimeSignature = gcetRicaRuntimeSettingsSignature()",StringComparison.Ordinal);
        return crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source;
    }

    private static string ComposeRicaRuntimeSettingsSignature(string source)
    {
        var crlf=source.Contains("\r\n",StringComparison.Ordinal);
        var s=source.Replace("\r\n","\n",StringComparison.Ordinal);
        const string fields="    lastScheduleSignature = nil,\n    playerAttached = false,";
        const string fn="function Mod.applyRuntimeSettings()\n    local system = Bridge.system()\n    if not system then return false end\n";
        const string success="    Mod.lastSettingsError = nil\n\n    local signature = ";
        const string stateCall="    Mod.applyRuntimeSettings()\n    if not Mod.playerAttached then";
        const string detach="            Mod.playerAttached = false\n        end\n        return\n    end";
        if(s.Split(fields,StringSplitOptions.None).Length!=2 ||
           s.Split(fn,StringSplitOptions.None).Length!=2 ||
           s.Split(success,StringSplitOptions.None).Length!=2 ||
           s.Split(stateCall,StringSplitOptions.None).Length!=2 ||
           s.Split(detach,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException(
                "RICA author settings state/apply/attachment transitions do not prove safe memoization.");
        var signature=RicaRuntimeSignatureCurrent.Replace("\r\n","\n",StringComparison.Ordinal);
        s=s.Replace(fields,
            "    lastScheduleSignature = nil,\n"+
            "    lastAppliedRuntimeSignature = nil,\n"+
            "    playerAttached = false,",StringComparison.Ordinal);
        s=s.Replace(fn,signature+
            "function Mod.applyRuntimeSettings(force)\n"+
            "    local system = Bridge.system()\n"+
            "    if not system then return false end\n"+
            "    if force ~= true and gcetRicaRuntimeSettingsMatches(Mod.lastAppliedRuntimeSignature) then\n"+
            "        return true\n"+
            "    end\n",StringComparison.Ordinal);
        s=s.Replace(success,
            "    Mod.lastSettingsError = nil\n"+
            "    Mod.lastAppliedRuntimeSignature = gcetRicaRuntimeSettingsSignature()\n\n"+
            "    local signature = ",StringComparison.Ordinal);
        s=s.Replace(stateCall,
            "    if not Mod.playerAttached then\n"+
            "        Mod.lastAppliedRuntimeSignature = nil\n"+
            "        Mod.applyRuntimeSettings(true)\n"+
            "    else\n"+
            "        Mod.applyRuntimeSettings(false)\n"+
            "    end\n"+
            "    if not Mod.playerAttached then",StringComparison.Ordinal);
        s=s.Replace(detach,
            "            Mod.playerAttached = false\n"+
            "            Mod.lastAppliedRuntimeSignature = nil\n"+
            "        end\n        return\n    end",StringComparison.Ordinal);
        return crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s;
    }

}
