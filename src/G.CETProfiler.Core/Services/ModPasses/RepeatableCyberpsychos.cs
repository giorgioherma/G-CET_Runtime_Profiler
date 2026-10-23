using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Repeatable Cyberpsychos: ONE source-owned pristine rule. A separate native-location
// catalog stage consumes the watchdog boundary produced here in the same pass.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRepeatableCyberpsychos(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "Mappins.sync", "Diagnostics.snapshot");
        const string completed="G-CET semantic:repeatable-cyberpsychos-six-job-v1";
        if(file.Text.Contains(completed,StringComparison.Ordinal))
        {
            var worker=context.TryReadOwnedCoordinator("cyberpsychos_engine_worker.lua");
            if(worker is null || !worker.Contains("cyberpsychos.runtime_combat_reward",StringComparison.Ordinal)
                || !worker.Contains("cyberpsychos.presentation_worker",StringComparison.Ordinal)
                || !worker.Contains("cyberpsychos.scheduler",StringComparison.Ordinal))
                throw new InvalidOperationException("Cyberpsychos completed stage lacks six-job worker.");
            var mappins=context.FindFile("modules/mappins.lua","Mappins.beginSync","Mappins.syncStep");
            var diagnostics=context.FindFile("modules/diagnostics.lua","Diagnostics.beginSnapshot","Diagnostics.snapshotStep");
            var watchdogProof=context.FindFile("modules/boss_watchdog.lua","Watchdog.syncSite","syncSiteWithPosition");
            return SemanticInjectionResult.Skip("Cyberpsychos complete scheduler and incremental source already installed.");
        }
        // The historical two-lane cadence is not completion. Explicitly refuse
        // partial PASS4 sources instead of layering a correction on their output.
        if(file.Text.Contains("TryAttachRepeatablePass4",StringComparison.Ordinal))
        {
            // Older fully integrated PASS4 Lua is a compatible completed source
            // representation, not an input prerequisite for pristine generation.
            // Prove every job and every incremental dependency before adopting it.
            foreach(var id in new[] {
                "runtime_combat_reward", "runtime_watchdog", "presentation_worker",
                "runtime", "presentation", "scheduler"
            })
                if(!file.Text.Contains("id = \"cyberpsychos."+id+"\"",StringComparison.Ordinal))
                    throw new InvalidOperationException("Cyberpsychos partial PASS4 job set drifted: "+id);
            context.FindFile("modules/mappins.lua","Mappins.beginSync","Mappins.syncStep");
            context.FindFile("modules/diagnostics.lua","Diagnostics.beginSnapshot","Diagnostics.snapshotStep");
            context.FindFile("modules/boss_watchdog.lua","Watchdog.syncSite","syncSiteWithPosition");
            const string oldHandoff="    TryAttachRepeatablePass4()\n\n    Mod.tickElapsed = Mod.tickElapsed + delta";
            const string fixedHandoff="    TryAttachRepeatablePass4()\n    if pass4Attached then return end\n\n    Mod.tickElapsed = Mod.tickElapsed + delta";
            if(file.Text.Contains(fixedHandoff,StringComparison.Ordinal))
                return SemanticInjectionResult.Skip("Cyberpsychos older full PASS4 source and handoff verified.");
            if(!file.Text.Contains(oldHandoff,StringComparison.Ordinal))
                throw new InvalidOperationException("Cyberpsychos existing PASS4 handoff drifted.");
            context.Write(file,CyberpsychoReplaceExact(file.Text,oldHandoff,fixedHandoff,"PASS4 handoff"));
            return SemanticInjectionResult.Success("Completed older PASS4 source: repaired only same-frame scheduler takeover.");
        }
        if(file.Text.Contains("cyberpsychos.runtime_combat_reward",StringComparison.Ordinal))
            throw new InvalidOperationException("Cyberpsychos partial PASS4 work stages are not source-proven.");
        var init=NormalizeCyberpsychoLineEndings(file.Text,out var crlfInit);
        // Legacy v1 only gated presentation; the same authoritative rule
        // recognizes that earlier source and creates the complete first-pass form.
        var legacyPresentation=init.Contains("local function runtimeTick(includePresentation)",StringComparison.Ordinal);
        init=CyberpsychoReplaceExact(init,"    tickElapsed = 0.0,","    tickElapsed = 0.0,\n    presentationElapsed = 0.0,","presentation clock");
        init=CyberpsychoReplaceExact(init,"    schedulerElapsed = 0.0,","    schedulerElapsed = 0.0,\n    schedulerIdleElapsed = 20.0,","idle scheduler");
        init=CyberpsychoReplaceExact(init,"    playerAttached = false,",CyberpsychoFields(),"player-state");
        var hasSharedPlayer=init.Contains("local function __gcetGetPlayer()",StringComparison.Ordinal);
        var originalPrelude=CyberpsychoPreludeOld();
        var enhancedPrelude=CyberpsychoPreludeNew();
        if(hasSharedPlayer) enhancedPrelude=enhancedPrelude.Replace("Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
        init=CyberpsychoReplaceBlock(init,"local function processBodyRewards(system)","registerForEvent(\"onInit\"",originalPrelude,enhancedPrelude,"runtime source");
        var initStart=init.IndexOf("registerForEvent(\"onInit\"",StringComparison.Ordinal);
        var initEnd=initStart<0?-1:init.IndexOf("\nend)\n",initStart,StringComparison.Ordinal);
        if(initStart<0||initEnd<0) throw new InvalidOperationException("Cyberpsychos onInit boundaries ambiguous.");
        initEnd += "\nend)\n".Length;
        CyberpsychoAssertEquivalent(init[initStart..initEnd],CyberpsychoOnInitOld(),"onInit lifecycle");
        init=init[..initStart]+CyberpsychoOnInitNew()+init[initEnd..];
        var reg=Regex.Match(init,@"(?m)^(?<registrar>(?:registerForEvent|__gcetRegisterEvent_\d+))\(\s*['""]onUpdate['""]");
        if(!reg.Success) throw new InvalidOperationException("Cyberpsychos update registrar source drifted.");
        var updateStart=reg.Index;
        var updateEnd=init.IndexOf("return Mod",updateStart,StringComparison.Ordinal);
        if(updateEnd<0) throw new InvalidOperationException("Cyberpsychos onUpdate/shutdown boundary drifted.");
        var oldTail=init[updateStart..updateEnd];
        CyberpsychoAssertEquivalent(oldTail,CyberpsychoTailOld(),"onUpdate/shutdown");
        var newTail=CyberpsychoTailNew().Replace("__CYBERPSYCHOS_REGISTER__",reg.Groups["registrar"].Value,StringComparison.Ordinal);
        if(hasSharedPlayer) newTail=newTail.Replace("Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
        init=init[..updateStart]+newTail+init[updateEnd..];
        init="-- "+completed+"\n"+init;
        context.Write(file,RestoreCyberpsychoLineEndings(init,crlfInit));
        CyberpsychoModule(context,"modules/mappins.lua","function Mappins.sync", "function Mappins.clear",CyberpsychoMappinsOld(),CyberpsychoMappinsNew(),
            "    lastSummary = nil,", "    lastSummary = nil,\n    syncState = nil,", "    Mappins.lastSummary = nil", "    Mappins.lastSummary = nil\n    Mappins.syncState = nil");
        CyberpsychoModule(context,"modules/diagnostics.lua","function Diagnostics.snapshot", "function Diagnostics.shutdown",CyberpsychoDiagnosticsOld(),CyberpsychoDiagnosticsNew(),
            "    lastSnapshotAt = 0,", "    lastSnapshotAt = 0,\n    snapshotWork = nil,", "    Diagnostics.event(\"session_end\", {}, true)","    Diagnostics.snapshotWork = nil\n    Diagnostics.event(\"session_end\", {}, true)");
        var watchdog=context.FindFile("modules/boss_watchdog.lua", "function Watchdog.sync", "function Watchdog.clear");
        var dog=NormalizeCyberpsychoLineEndings(watchdog.Text,out var crlfDog);
        dog=CyberpsychoReplaceBlock(dog,"local function distance2D","local function entityIDKey",CyberpsychoDistanceOld(),CyberpsychoDistanceNew(),"watchdog squared distance");
        var newWatchdog=CyberpsychoWatchdogNew();
        if(dog.Contains("local function __gcetGetPlayer()",StringComparison.Ordinal))
            newWatchdog=newWatchdog.Replace("Game.GetPlayer()","__gcetGetPlayer()",StringComparison.Ordinal);
        dog=CyberpsychoReplaceBlock(dog,"function Watchdog.sync", "function Watchdog.clear", CyberpsychoWatchdogOld(),newWatchdog,"per-site watchdog");
        context.Write(watchdog,RestoreCyberpsychoLineEndings(dog,crlfDog));
        context.WriteCoordinator("cyberpsychos_engine_worker.lua",CyberpsychoWorker());
        return SemanticInjectionResult.Success("Cyberpsychos pristine source: source-proven six-job 0-Engine scheduler, two alternating runtime workers, incremental mappins/diagnostics, native wake anchors and author fallback.");
    }

    private static string NormalizeCyberpsychoLineEndings(string s,out bool crlf)
    { crlf=s.Contains("\r\n",StringComparison.Ordinal); return s.Replace("\r\n","\n",StringComparison.Ordinal); }
    private static string RestoreCyberpsychoLineEndings(string s,bool crlf)
        => crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s;

    private static string CyberpsychoCanonicalTokens(string source)
    {
        // Whitespace, comments and equivalent quote delimiters are inert;
        // executable Lua tokens must agree before replacing author-owned code.
        var matches=Regex.Matches(source,@"--[^\n]*|""(?:\\.|[^""\\])*""|'(?:\\.|[^'\\])*'|[A-Za-z_]\w*|\d+(?:\.\d+)?|==|~=|<=|>=|\.\.|[^\s]");
        var tokens=new List<string>();
        foreach(Match m in matches)
        {
            var v=m.Value;
            if(v.StartsWith("--",StringComparison.Ordinal)) continue;
            if(v.Length>=2 && ((v[0]=='"'&&v[^1]=='"') || (v[0]=='\''&&v[^1]=='\'')))
                v="\""+v[1..^1]+"\"";
            tokens.Add(v);
        }
        return string.Join(" ",tokens);
    }
    private static void CyberpsychoAssertEquivalent(string found,string known,string boundary)
    {
        // Shared generic PLAYER getter is an equivalent source-adopted read.
        found=CyberpsychoNormalizeLegacy(found,boundary);
        found=found.Replace("__gcetGetPlayer()","Game.GetPlayer()",StringComparison.Ordinal);
        found=Regex.Replace(found,@"__gcetRegisterEvent_\d+(?=\s*\()", "registerForEvent");
        if(!string.Equals(CyberpsychoCanonicalTokens(found),CyberpsychoCanonicalTokens(known),StringComparison.Ordinal))
            throw new InvalidOperationException("Cyberpsychos "+boundary+" executable source drifted; refusing unknown changes.");
    }
    private static string CyberpsychoReplaceExact(string source,string from,string to,string label)
    {
        if(source.Split(from,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Cyberpsychos "+label+" anchor ambiguous.");
        return source.Replace(from,to,StringComparison.Ordinal);
    }
    private static string CyberpsychoReplaceBlock(string source,string start,string end,string oldSource,string replacement,string label)
    {
        var first=source.IndexOf(start,StringComparison.Ordinal);
        var last=first<0?-1:source.IndexOf(end,first+start.Length,StringComparison.Ordinal);
        if(first<0||last<0 || source.IndexOf(start,first+start.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("Cyberpsychos "+label+" block anchors missing or ambiguous.");
        CyberpsychoAssertEquivalent(source[first..last],oldSource,label);
        return source[..first]+replacement+source[last..];
    }
    private static void CyberpsychoModule(SemanticPatchContext ctx,string path,string start,string end,string oldSource,string newSource,
        string fieldOld,string fieldNew,string clearOld,string clearNew)
    {
        var file=ctx.FindFile(path,start,end);
        var source=NormalizeCyberpsychoLineEndings(file.Text,out var crlf);
        source=CyberpsychoReplaceBlock(source,start,end,oldSource,newSource,path);
        source=CyberpsychoReplaceExact(source,fieldOld,fieldNew,path+" state field");
        source=CyberpsychoReplaceExact(source,clearOld,clearNew,path+" teardown");
        ctx.Write(file,RestoreCyberpsychoLineEndings(source,crlf));
    }

    private static string CyberpsychoNormalizeLegacy(string found,string boundary)
    {
        if(boundary=="runtime source" &&
           found.Contains("runtimeTick(includePresentation)",StringComparison.Ordinal))
        {
            const string gated = """
    if includePresentation then
        Mappins.sync(system, Sites)
        Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)
    end
""";
            const string ungated = """
    Mappins.sync(system, Sites)
    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)
""";
            found=CyberpsychoReplaceExact(found,"local function runtimeTick(includePresentation)",
                "local function runtimeTick()","legacy runtime signature");
            found=CyberpsychoReplaceExact(found,gated.TrimEnd('\n')+"\n",
                ungated.TrimEnd('\n')+"\n","legacy presentation gate");
        }
        if(boundary=="onUpdate/shutdown" &&
           found.Contains("local includePresentation = Mod.presentationElapsed",StringComparison.Ordinal))
        {
            found=CyberpsychoReplaceExact(found,
                "    Mod.presentationElapsed = (Mod.presentationElapsed or 5.0) + delta\n",
                "","legacy presentation accumulator");
            found=CyberpsychoReplaceExact(found,
                "        local includePresentation = Mod.presentationElapsed >= 5.0\n"+
                "        if includePresentation then Mod.presentationElapsed = 0.0 end\n",
                "","legacy presentation selection");
            found=CyberpsychoReplaceExact(found,
                "pcall(runtimeTick, includePresentation)",
                "pcall(runtimeTick)","legacy runtime invocation");
        }
        return found;
    }

    private static string CyberpsychoFields() => """
    playerAttached = false,

    runtimeWorkIndex = nil,
    runtimeWorkStage = nil,
    runtimeWorkElapsed = 0.0,
    runtimeCompletionBudget = 0,
    runtimeQueuedElapsed = 0.0,
    runtimeLastActiveReplayCount = 0,
    runtimeIdleSweepElapsed = 5.0,

    presentationPhase = nil,
    presentationQueued = false,
    presentationFrameFlip = false,
    presentationIdleDiagnosticsCycles = 5,
""";

    private static string CyberpsychoPreludeOld() => """
local function processBodyRewards(system)
    for _, site in ipairs(Sites.list) do
        local boss = Bridge.boss(system, site.id)
        if boss then
            local ok, result = pcall(Rewards.grantBody, boss, Mod.settings)
            if not ok then
                print("[Repeatable Cyberpsychos] Body reward failed at " .. site.id .. ": " .. tostring(result))
                Diagnostics.event("body_reward_failed", { site = site.id, error = tostring(result) })
            else
                pcall(function() system:MarkBodyRewarded(site.id) end)
                Diagnostics.event("body_reward_granted", {
                    site = site.id,
                    bossRecord = site.bossRecord,
                    itemCount = result,
                })
            end
        end
    end
end

local function processCombatSetup(system)
    for _, site in ipairs(Sites.list) do
        local needs = false
        pcall(function() needs = system:NeedsCombatSetup(site.id) end)
        if needs then
            local entityID = nil
            pcall(function() entityID = system:GetBossEntityID(site.id) end)
            local boss = Bridge.entity(entityID)
            if boss then
                local ok, err = pcall(Rewards.applyCombat, boss, Mod.settings)
                if ok then
                    pcall(function() system:MarkCombatPrepared(site.id) end)
                    Diagnostics.event("combat_scaling_applied", {
                        site = site.id,
                        bossRecord = site.bossRecord,
                        combat = Mod.settings.combat,
                    })
                else
                    Diagnostics.event("combat_scaling_failed", { site = site.id, error = tostring(err) })
                end
            end
        end
    end
end

local function processCompletionRewards(system)
    local pending = 0
    pcall(function() pending = system:GetPendingCompletionRewards() end)
    local processed = 0
    while pending > 0 and processed < 5 do
        local ok, granted, itemCount, ruleSource = pcall(Rewards.grantCompletion, Mod.settings)
        if not ok or granted ~= true then break end
        pcall(function() system:AcknowledgeCompletionReward() end)
        Diagnostics.event("completion_reward_processed", {
            enabled = Mod.settings.reward.completionEnabled ~= false,
            itemCount = itemCount or 0,
            ruleSource = ruleSource or "disabled",
        })
        pending = pending - 1
        processed = processed + 1
    end
end

local function applySchedule(system)
    local schedule = Mod.settings.schedule
    pcall(function() system:SetCooldownHours(schedule.cooldownHours) end)
    pcall(function() system:SetLockdownPoolSize(schedule.lockdownPoolSize) end)
    pcall(function() system:SetPoolSize(schedule.poolSize) end)
end

local function runtimeTick()
    local system = Bridge.system()
    local player = Game.GetPlayer()
    if not system or not player then
        Mod.playerAttached = false
        return
    end
    applySchedule(system)
    if not Mod.playerAttached then
        Mod.playerAttached = true
        Diagnostics.event("save_attached_schedule_applied", {
            configured = Mod.settings.schedule,
            applied = {
                poolSize = system:GetPoolSize(),
                lockdownPoolSize = system:GetLockdownPoolSize(),
                effectivePoolSize = system:GetEffectivePoolSize(),
                cooldownHours = system:GetCooldownHours(),
            },
        })
    end
    Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)
    pcall(function() system:SetChangeMappinColor(Mod.settings.general.changeMapMarkerColor) end)
    pcall(function() system:SetHeightenedAwareness(Mod.settings.combat.heightenedAwareness) end)
    pcall(function() system:SetBlockStealthTakedowns(Mod.settings.combat.blockStealthTakedowns) end)
    pcall(function() system:RuntimeTick() end)
    processCombatSetup(system)
    processBodyRewards(system)
    processCompletionRewards(system)
    BossWatchdog.sync(system, Sites, Mod.settings, 1.0)
    Mappins.sync(system, Sites)
    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)
end
""";

    private static string CyberpsychoPreludeNew() => """
local function processBodyRewardSite(system, site)
    local boss = Bridge.boss(system, site.id)
    if not boss then return end

    local ok, result = pcall(Rewards.grantBody, boss, Mod.settings)
    if not ok then
        print("[Repeatable Cyberpsychos] Body reward failed at " .. site.id .. ": " .. tostring(result))
        Diagnostics.event("body_reward_failed", { site = site.id, error = tostring(result) })
    else
        pcall(function() system:MarkBodyRewarded(site.id) end)
        Diagnostics.event("body_reward_granted", {
            site = site.id,
            bossRecord = site.bossRecord,
            itemCount = result,
        })
    end
end

local function processBodyRewards(system)
    for _, site in ipairs(Sites.list) do
        processBodyRewardSite(system, site)
    end
end

local function processCombatSetupSite(system, site)
    local needs = false
    pcall(function() needs = system:NeedsCombatSetup(site.id) end)
    if not needs then return end

    local entityID = nil
    pcall(function() entityID = system:GetBossEntityID(site.id) end)
    local boss = Bridge.entity(entityID)
    if not boss then return end

    local ok, err = pcall(Rewards.applyCombat, boss, Mod.settings)
    if ok then
        pcall(function() system:MarkCombatPrepared(site.id) end)
        Diagnostics.event("combat_scaling_applied", {
            site = site.id,
            bossRecord = site.bossRecord,
            combat = Mod.settings.combat,
        })
    else
        Diagnostics.event("combat_scaling_failed", { site = site.id, error = tostring(err) })
    end
end

local function processCombatSetup(system)
    for _, site in ipairs(Sites.list) do
        processCombatSetupSite(system, site)
    end
end

local function processOneCompletionReward(system)
    local pending = 0
    pcall(function() pending = system:GetPendingCompletionRewards() end)
    if pending <= 0 then return false end

    local ok, granted, itemCount, ruleSource = pcall(Rewards.grantCompletion, Mod.settings)
    if not ok or granted ~= true then return false end

    pcall(function() system:AcknowledgeCompletionReward() end)
    Diagnostics.event("completion_reward_processed", {
        enabled = Mod.settings.reward.completionEnabled ~= false,
        itemCount = itemCount or 0,
        ruleSource = ruleSource or "disabled",
    })
    return true
end

local function processCompletionRewards(system)
    local processed = 0
    while processed < 5 and processOneCompletionReward(system) do
        processed = processed + 1
    end
end

local lastRuntimeSettingsSignature = nil

local function runtimeSettingsSignature()
    local schedule = Mod.settings.schedule
    local general = Mod.settings.general
    local combat = Mod.settings.combat
    return table.concat({
        tostring(schedule.cooldownHours),
        tostring(schedule.lockdownPoolSize),
        tostring(schedule.poolSize),
        tostring(general.changeMapMarkerColor),
        tostring(combat.heightenedAwareness),
        tostring(combat.blockStealthTakedowns),
    }, "|")
end

local function applyRuntimeSettings(system)
    local schedule = Mod.settings.schedule
    pcall(function() system:SetCooldownHours(schedule.cooldownHours) end)
    pcall(function() system:SetLockdownPoolSize(schedule.lockdownPoolSize) end)
    pcall(function() system:SetPoolSize(schedule.poolSize) end)
    pcall(function() system:SetChangeMappinColor(Mod.settings.general.changeMapMarkerColor) end)
    pcall(function() system:SetHeightenedAwareness(Mod.settings.combat.heightenedAwareness) end)
    pcall(function() system:SetBlockStealthTakedowns(Mod.settings.combat.blockStealthTakedowns) end)
    lastRuntimeSettingsSignature = runtimeSettingsSignature()
end

local function runtimeCoreTick(elapsed)
    local system = Bridge.system()
    local player = Game.GetPlayer()
    if not system or not player then
        Mod.playerAttached = false
        lastRuntimeSettingsSignature = nil
        return nil
    end

    local firstAttach = not Mod.playerAttached
    local currentSignature = runtimeSettingsSignature()
    if firstAttach or currentSignature ~= lastRuntimeSettingsSignature then
        applyRuntimeSettings(system)
    end

    if firstAttach then
        Mod.playerAttached = true

        -- Preserve the original immediate attach reconciliation. Regular
        -- presentation sweeps are cooperative below.
        Mappins.sync(system, Sites)

        Diagnostics.event("save_attached_schedule_applied", {
            configured = Mod.settings.schedule,
            applied = {
                poolSize = system:GetPoolSize(),
                lockdownPoolSize = system:GetLockdownPoolSize(),
                effectivePoolSize = system:GetEffectivePoolSize(),
                cooldownHours = system:GetCooldownHours(),
            },
        })
    end

    Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)
    pcall(function() system:RuntimeTick() end)
    return system
end

-- Exact synchronous path retained for the native fallback.
local function runtimeTick(elapsed)
    local system = runtimeCoreTick(elapsed)
    if not system then return end

    processCombatSetup(system)
    processBodyRewards(system)
    processCompletionRewards(system)
    BossWatchdog.sync(system, Sites, Mod.settings, elapsed or 1.0)
end

-- The presentation worker can sleep safely because it has no paired phase.
-- The two alternating runtime workers remain registered/active so their R4
-- even/odd frame separation cannot collapse when a dormant cycle wakes.
local pass4PresentationWorkerJob = nil

local function setPresentationWorkerActive(active)
    local job = pass4PresentationWorkerJob
    if not job then return end
    local fn = active and job.Resume or job.Pause
    if type(fn) == "function" then pcall(fn) end
end

local function runtimeWorkActive()
    return Mod.runtimeWorkStage ~= nil
        or Mod.runtimeWorkIndex ~= nil
        or Mod.runtimeCompletionBudget > 0
end

local function resetRuntimeWork(dropQueued)
    Mod.runtimeWorkIndex = nil
    Mod.runtimeWorkStage = nil
    Mod.runtimeCompletionBudget = 0
    if dropQueued then Mod.runtimeQueuedElapsed = 0.0 end
end

local function beginQueuedRuntimeWork(elapsed)
    Mod.runtimeWorkIndex = 1
    Mod.runtimeWorkStage = "combat_reward"
    Mod.runtimeWorkElapsed = elapsed or 1.0
    Mod.runtimeCompletionBudget = 5
end

local function startRuntimeWork(elapsed)
    if runtimeWorkActive() then
        Mod.runtimeQueuedElapsed = Mod.runtimeQueuedElapsed + (elapsed or 1.0)
        return
    end
    beginQueuedRuntimeWork(elapsed or 1.0)
end

local function beginRuntimeCycle(elapsed)
    local system = runtimeCoreTick(elapsed)
    if not system then
        resetRuntimeWork(true)
        return
    end

    -- RuntimeTick remains exactly 1 Hz. The expensive cooperative 17-site sweep
    -- stays at 1 Hz while a replay is active, on the first tick after it becomes
    -- inactive, or while completion rewards are pending. Fully dormant systems
    -- receive the exact same full sweep as a 5 s safety audit instead.
    local activeReplayCount = 0
    local pendingCompletionRewards = 0
    pcall(function() activeReplayCount = math.max(0, system:GetActiveReplayCount()) end)
    pcall(function() pendingCompletionRewards = math.max(0, system:GetPendingCompletionRewards()) end)

    local wasActive = (Mod.runtimeLastActiveReplayCount or 0) > 0
    Mod.runtimeLastActiveReplayCount = activeReplayCount

    if activeReplayCount > 0 or wasActive or pendingCompletionRewards > 0 then
        Mod.runtimeIdleSweepElapsed = 0.0
        startRuntimeWork(elapsed or 1.0)
        return
    end

    Mod.runtimeIdleSweepElapsed = (Mod.runtimeIdleSweepElapsed or 0.0) + (elapsed or 1.0)
    if Mod.runtimeIdleSweepElapsed >= 5.0 then
        Mod.runtimeIdleSweepElapsed = 0.0
        startRuntimeWork(elapsed or 1.0)
    end
end

-- PASS4-R4: split each site's runtime work across two rendered frames.
-- Combat/body reward work and watchdog work now have separate Scheduler job
-- identities, so a remaining pathological native call is attributable.
local function runRuntimeCombatRewardStep()
    if Mod.runtimeWorkStage ~= "combat_reward" then return end

    local system = Bridge.system()
    if not system or not Game.GetPlayer() then
        resetRuntimeWork(true)
        return
    end

    local site = Mod.runtimeWorkIndex and Sites.list[Mod.runtimeWorkIndex] or nil
    if not site then
        Mod.runtimeWorkIndex = nil
        Mod.runtimeWorkStage = "completion"
        return
    end

    processCombatSetupSite(system, site)
    processBodyRewardSite(system, site)
    Mod.runtimeWorkStage = "watchdog"
end

local function finishRuntimeCycleOrStartQueued()
    Mod.runtimeCompletionBudget = 0
    Mod.runtimeWorkStage = nil
    if Mod.runtimeQueuedElapsed > 0 then
        local queued = Mod.runtimeQueuedElapsed
        Mod.runtimeQueuedElapsed = 0.0
        beginQueuedRuntimeWork(queued)
    end
end

local function runRuntimeWatchdogStep()
    if not runtimeWorkActive() then return end

    local system = Bridge.system()
    if not system or not Game.GetPlayer() then
        resetRuntimeWork(true)
        return
    end

    if Mod.runtimeWorkStage == "watchdog" then
        local site = Mod.runtimeWorkIndex and Sites.list[Mod.runtimeWorkIndex] or nil
        if site then
            BossWatchdog.syncSite(system, site, Mod.settings, Mod.runtimeWorkElapsed)
            Mod.runtimeWorkIndex = Mod.runtimeWorkIndex + 1
            if Sites.list[Mod.runtimeWorkIndex] then
                Mod.runtimeWorkStage = "combat_reward"
            else
                Mod.runtimeWorkIndex = nil
                Mod.runtimeWorkStage = "completion"
            end
        else
            Mod.runtimeWorkIndex = nil
            Mod.runtimeWorkStage = "completion"
        end
        return
    end

    if Mod.runtimeWorkStage == "completion" then
        if Mod.runtimeCompletionBudget > 0 and processOneCompletionReward(system) then
            Mod.runtimeCompletionBudget = Mod.runtimeCompletionBudget - 1
            return
        end
        finishRuntimeCycleOrStartQueued()
    end
end

local function startPresentationCycle()
    if Mod.presentationPhase ~= nil then
        Mod.presentationQueued = true
        return
    end
    Mod.presentationPhase = "mappins"
    setPresentationWorkerActive(true)
end

local function finishPresentationCycle()
    Mod.presentationPhase = nil
    if Mod.presentationQueued then
        Mod.presentationQueued = false
        Mod.presentationPhase = "mappins"
        return
    end
    setPresentationWorkerActive(false)
end

local function runPresentationWorkStep()
    if Mod.presentationPhase == nil then return end

    -- Presentation work deliberately runs every other rendered frame. With
    -- 17 sites this spreads mappin + diagnostic reconciliation across roughly
    -- 34 work steps without changing the 5 s trigger cadence.
    Mod.presentationFrameFlip = not Mod.presentationFrameFlip
    if not Mod.presentationFrameFlip then return end

    local system = Bridge.system()
    if not system or not Game.GetPlayer() then
        Mappins.syncState = nil
        Diagnostics.snapshotWork = nil
        finishPresentationCycle()
        return
    end

    if Mod.presentationPhase == "mappins" then
        if not Mappins.isSyncActive() then
            if not Mappins.beginSync(system, Sites) then
                finishPresentationCycle()
                return
            end
        end

        if Mappins.syncStep(system, Sites, 1) then
            -- Mappin reconciliation remains on the existing 5 s cadence. The
            -- JSON diagnostic snapshot is observational only, so keep 5 s while
            -- replay activity exists and reduce dormant snapshots to 30 s.
            local shouldSnapshot = false
            if (Mappins.schedulerActive or 0) > 0 then
                Mod.presentationIdleDiagnosticsCycles = 0
                shouldSnapshot = true
            else
                Mod.presentationIdleDiagnosticsCycles =
                    (Mod.presentationIdleDiagnosticsCycles or 0) + 1
                if Mod.presentationIdleDiagnosticsCycles >= 6 then
                    Mod.presentationIdleDiagnosticsCycles = 0
                    shouldSnapshot = true
                end
            end

            if shouldSnapshot and Diagnostics.beginSnapshot(system, Sites, Mod.settings, Mappins) then
                Mod.presentationPhase = "diagnostics"
            else
                finishPresentationCycle()
            end
        end
        return
    end

    if Mod.presentationPhase == "diagnostics" then
        if Diagnostics.snapshotStep(system, Sites, 1) then
            finishPresentationCycle()
        end
    end
end

local function presentationTick()
    startPresentationCycle()
end

local repeatableBaseInitialized = false
local TryAttachRepeatablePass4 = nil
""";

    private static string CyberpsychoOnInitOld() => """
registerForEvent("onInit", function()
    math.randomseed(os.time())
    Diagnostics.init(Mod.settings, Mod.version)
    BossWatchdog.init(Bridge, Diagnostics)
    local ok, stats = pcall(Rewards.scan)
    if ok then
        print("[Repeatable Cyberpsychos] Reward catalog ready: " .. tostring(stats.scanned) .. " records scanned.")
    else
        print("[Repeatable Cyberpsychos] Reward catalog scan failed: " .. tostring(stats))
    end
    pcall(Mappins.discover, Sites)
    local registered, settingsError = pcall(NativeSettings.register, Mod)
    if not registered then
        print("[Repeatable Cyberpsychos] Native Settings registration failed: " .. tostring(settingsError))
    end
end)
""";

    private static string CyberpsychoOnInitNew() => """
registerForEvent("onInit", function()
    math.randomseed(os.time())
    Diagnostics.init(Mod.settings, Mod.version)
    BossWatchdog.init(Bridge, Diagnostics)
    local ok, stats = pcall(Rewards.scan)
    if ok then
        print("[Repeatable Cyberpsychos] Reward catalog ready: " .. tostring(stats.scanned) .. " records scanned.")
    else
        print("[Repeatable Cyberpsychos] Reward catalog scan failed: " .. tostring(stats))
    end
    pcall(Mappins.discover, Sites)
    local registered, settingsError = pcall(NativeSettings.register, Mod)
    if not registered then
        print("[Repeatable Cyberpsychos] Native Settings registration failed: " .. tostring(settingsError))
    end

    -- Base mod initialization must complete before PASS4 runtime jobs can run.
    repeatableBaseInitialized = true
    if TryAttachRepeatablePass4 then
        TryAttachRepeatablePass4()
    end
end)
""";

    private static string CyberpsychoTailOld() => """
registerForEvent("onUpdate", function(delta)
    Mod.tickElapsed = Mod.tickElapsed + delta
    Mod.schedulerElapsed = Mod.schedulerElapsed + delta
    if Mod.tickElapsed >= 1.0 then
        Mod.tickElapsed = 0.0
        local ok, err = pcall(runtimeTick)
        if not ok then print("[Repeatable Cyberpsychos] Runtime tick failed: " .. tostring(err)) end
    end
    if Mod.schedulerElapsed >= 10.0 then
        Mod.schedulerElapsed = 0.0
        local system = Bridge.system()
        if system then pcall(function() system:Tick() end) end
    end
end)

registerForEvent("onShutdown", function()
    Mod.saveSettings()
    BossWatchdog.clear()
    Mappins.clear()
    Diagnostics.shutdown()
end)
""";

    private static string CyberpsychoTailNew() => """
local function safeRuntimeTick(elapsed)
    local ok, err = pcall(runtimeTick, elapsed)
    if not ok then print("[Repeatable Cyberpsychos] Runtime tick failed: " .. tostring(err)) end
end

local function safeBeginRuntimeCycle(elapsed)
    local ok, err = pcall(beginRuntimeCycle, elapsed)
    if not ok then print("[Repeatable Cyberpsychos] Runtime cycle failed: " .. tostring(err)) end
end

local function safeRuntimeCombatRewardTick()
    local ok, err = pcall(runRuntimeCombatRewardStep)
    if not ok then
        print("[Repeatable Cyberpsychos] Combat/reward worker failed: " .. tostring(err))
    end
end

local function safeRuntimeWatchdogTick()
    local ok, err = pcall(runRuntimeWatchdogStep)
    if not ok then
        print("[Repeatable Cyberpsychos] Watchdog worker failed: " .. tostring(err))
    end
end

local function safePresentationWorkerTick()
    local ok, err = pcall(runPresentationWorkStep)
    if not ok then
        print("[Repeatable Cyberpsychos] Presentation worker failed: " .. tostring(err))
    end
end

local function safePresentationTick()
    local ok, err = pcall(presentationTick)
    if not ok then print("[Repeatable Cyberpsychos] Presentation tick failed: " .. tostring(err)) end
end

local function schedulerTick(elapsed)
    local system = Bridge.system()
    if not system then return end

    -- PASS6: system:Tick() is a comparatively expensive native scheduling sweep.
    -- Keep the original 10 s service cadence while a replay/reward is live, but
    -- fully dormant saves only need a 60 s eligibility audit. The accumulator is
    -- seeded so the first post-attach 10 s tick is preserved.
    local activeReplayCount = 0
    local pendingCompletionRewards = 0
    pcall(function() activeReplayCount = math.max(0, system:GetActiveReplayCount()) end)
    pcall(function() pendingCompletionRewards = math.max(0, system:GetPendingCompletionRewards()) end)

    if activeReplayCount <= 0 and pendingCompletionRewards <= 0 then
        Mod.schedulerIdleElapsed = (Mod.schedulerIdleElapsed or 0.0) + (elapsed or 10.0)
        if Mod.schedulerIdleElapsed < 60.0 then return end
    end

    Mod.schedulerIdleElapsed = 0.0
    pcall(function() system:Tick() end)
end

local pass4Attached = false
local pass4AttachInProgress = false

TryAttachRepeatablePass4 = function()
    if not repeatableBaseInitialized then return false end
    if pass4Attached then return true end
    if pass4AttachInProgress then return false end
    pass4AttachInProgress = true

    local created = {}
    local okAttach = pcall(function()
        local okEngine, engine = pcall(GetMod, "0-Engine")
        if not okEngine or type(engine) ~= "table" or type(engine.Register) ~= "function" then
            error("0-Engine Register API not ready")
        end

        local client = engine.Register("RepeatableCyberpsychos")
        if type(client) ~= "table"
            or type(client.Schedule) ~= "table"
            or type(client.Schedule.Every) ~= "function"
            or type(client.Schedule.EveryFrame) ~= "function"
            or type(client.Schedule.EveryFrames) ~= "function" then
            error("0-Engine Scheduler API not ready")
        end

        local worker = require("GCET_Semantic/cyberpsychos_engine_worker")
        local presentationWorkerJob = worker.attach(client, created, {
            combatReward = safeRuntimeCombatRewardTick,
            watchdog = safeRuntimeWatchdogTick,
            presentationWorker = safePresentationWorkerTick,
            runtime = safeBeginRuntimeCycle,
            presentation = safePresentationTick,
            scheduler = schedulerTick,
        })
        pass4PresentationWorkerJob = presentationWorkerJob
        pass4Attached = true
    end)

    pass4AttachInProgress = false

    if not okAttach then
        -- Never leave a partially attached job set behind before retrying.
        for _, handle in ipairs(created) do
            if handle and type(handle.Cancel) == "function" then
                pcall(handle.Cancel)
            end
        end
        return false
    end

    print("[Repeatable Cyberpsychos] PASS4-R5 idle-gated Scheduler attached: R4 runtime phases preserved; presentation worker sleeps when idle.")
    return true
end

-- Bootstrap fallback. It retries attachment until 0-Engine Scheduler is ready.
-- Once attached, this native callback becomes a cheap no-op.
__CYBERPSYCHOS_REGISTER__("onUpdate", function(delta)
    if pass4Attached then return end
    if not repeatableBaseInitialized then return end

    TryAttachRepeatablePass4()

    if pass4Attached then return end
    Mod.tickElapsed = Mod.tickElapsed + delta
    Mod.presentationElapsed = Mod.presentationElapsed + delta
    Mod.schedulerElapsed = Mod.schedulerElapsed + delta

    if Mod.tickElapsed >= 1.0 then
        local elapsed = Mod.tickElapsed
        Mod.tickElapsed = 0.0
        safeRuntimeTick(elapsed)
    end

    if Mod.presentationElapsed >= 5.0 then
        Mod.presentationElapsed = 0.0
        safePresentationTick()
    end

    if Mod.schedulerElapsed >= 10.0 then
        local elapsed = Mod.schedulerElapsed
        Mod.schedulerElapsed = 0.0
        schedulerTick(elapsed)
    end
end)

registerForEvent("onShutdown", function()
    Mod.runtimeWorkIndex = nil
    Mod.runtimeWorkStage = nil
    Mod.runtimeCompletionBudget = 0
    Mod.runtimeQueuedElapsed = 0.0
    Mod.runtimeLastActiveReplayCount = 0
    Mod.runtimeIdleSweepElapsed = 5.0
    Mod.schedulerIdleElapsed = 20.0
    Mod.presentationPhase = nil
    Mod.presentationQueued = false
    Mod.presentationIdleDiagnosticsCycles = 5
    setPresentationWorkerActive(false)
    Mod.saveSettings()
    BossWatchdog.clear()
    Mappins.clear()
    Diagnostics.shutdown()
end)
""";

    private static string CyberpsychoMappinsOld() => """
function Mappins.sync(system, sites)
    if not system then return end
    if Mappins.discovered ~= #sites.list then Mappins.discover(sites) end
    local budget = 0
    pcall(function() budget = math.max(0, system:GetEffectivePoolSize()) end)
    Mappins.schedulerActive = 0
    Mappins.eligible = 0
    Mappins.tracked = 0
    pcall(function() Mappins.schedulerActive = math.max(0, system:GetActiveReplayCount()) end)
    pcall(function() Mappins.eligible = math.max(0, system:GetVanillaEligibleReplayCount()) end)
    pcall(function() Mappins.tracked = math.max(0, system:GetTrackedSiteCount()) end)
    Mappins.targetActive = 0
    Mappins.registrationFailures = 0
    for _, site in ipairs(sites.list) do
        local active = false
        pcall(function() active = system:GetStatus(site.id) == 1 end)
        -- A second guard at the presentation layer ensures that purple replay
        -- markers can never exceed the configured replay-marker budget.
        if active and Mappins.targetActive < budget then
            Mappins.targetActive = Mappins.targetActive + 1
        else
            active = false
        end
        if active and not Mappins.owned[site.id] then
            local marker = register(Mappins.positions[site.id])
            if marker then
                Mappins.owned[site.id] = marker
            else
                Mappins.registrationFailures = Mappins.registrationFailures + 1
            end
        elseif not active and Mappins.owned[site.id] then
            unregister(Mappins.owned[site.id])
            Mappins.owned[site.id] = nil
        end
    end
    Mappins.registered = 0
    for _ in pairs(Mappins.owned) do Mappins.registered = Mappins.registered + 1 end
    local summary = tostring(Mappins.registered) .. "/" .. tostring(budget)
        .. "; active=" .. tostring(Mappins.schedulerActive)
        .. "; eligible=" .. tostring(Mappins.eligible)
        .. "; tracked=" .. tostring(Mappins.tracked)
        .. "; failures=" .. tostring(Mappins.registrationFailures)
    if summary ~= Mappins.lastSummary then
        print("[Repeatable Cyberpsychos] Purple replay markers: " .. summary
            .. ". Original blue Regina markers are not included in this count.")
        Mappins.lastSummary = summary
    end
end
""";

    private static string CyberpsychoMappinsNew() => """
local function finishSync(state)
    Mappins.targetActive = state.targetActive
    Mappins.registrationFailures = state.registrationFailures
    Mappins.registered = 0
    for _ in pairs(Mappins.owned) do Mappins.registered = Mappins.registered + 1 end

    local summary = tostring(Mappins.registered) .. "/" .. tostring(state.budget)
        .. "; active=" .. tostring(Mappins.schedulerActive)
        .. "; eligible=" .. tostring(Mappins.eligible)
        .. "; tracked=" .. tostring(Mappins.tracked)
        .. "; failures=" .. tostring(Mappins.registrationFailures)
    if summary ~= Mappins.lastSummary then
        print("[Repeatable Cyberpsychos] Purple replay markers: " .. summary
            .. ". Original blue Regina markers are not included in this count.")
        Mappins.lastSummary = summary
    end

    Mappins.syncState = nil
end

function Mappins.beginSync(system, sites)
    if not system then return false end
    if Mappins.discovered ~= #sites.list then Mappins.discover(sites) end

    local budget = 0
    pcall(function() budget = math.max(0, system:GetEffectivePoolSize()) end)

    Mappins.schedulerActive = 0
    Mappins.eligible = 0
    Mappins.tracked = 0
    pcall(function() Mappins.schedulerActive = math.max(0, system:GetActiveReplayCount()) end)
    pcall(function() Mappins.eligible = math.max(0, system:GetVanillaEligibleReplayCount()) end)
    pcall(function() Mappins.tracked = math.max(0, system:GetTrackedSiteCount()) end)

    Mappins.syncState = {
        index = 1,
        budget = budget,
        targetActive = 0,
        registrationFailures = 0,
    }
    return true
end

function Mappins.isSyncActive()
    return Mappins.syncState ~= nil
end

function Mappins.syncStep(system, sites, maxSites)
    local state = Mappins.syncState
    if not state then return true end
    if not system then
        Mappins.syncState = nil
        return true
    end

    local limit = math.max(1, tonumber(maxSites) or 1)
    local processed = 0

    while state.index <= #sites.list and processed < limit do
        local site = sites.list[state.index]
        state.index = state.index + 1
        processed = processed + 1

        local active = false
        pcall(function() active = system:GetStatus(site.id) == 1 end)

        -- Preserve the original list-order replay-marker budget.
        if active and state.targetActive < state.budget then
            state.targetActive = state.targetActive + 1
        else
            active = false
        end

        if active and not Mappins.owned[site.id] then
            local marker = register(Mappins.positions[site.id])
            if marker then
                Mappins.owned[site.id] = marker
            else
                state.registrationFailures = state.registrationFailures + 1
            end
        elseif not active and Mappins.owned[site.id] then
            unregister(Mappins.owned[site.id])
            Mappins.owned[site.id] = nil
        end
    end

    if state.index > #sites.list then
        finishSync(state)
        return true
    end
    return false
end

function Mappins.sync(system, sites)
    if not Mappins.beginSync(system, sites) then return end
    while Mappins.isSyncActive() do
        Mappins.syncStep(system, sites, #sites.list)
    end
end
""";

    private static string CyberpsychoDiagnosticsOld() => """
function Diagnostics.snapshot(system, sites, settings, mappins)
    if not Diagnostics.enabled or not system then return end
    local now = read(system, 0, function(s) return s:GetNowSeconds() end)
    local payload = {
        gameTimeSeconds = now,
        configuredSchedule = settings.schedule,
        appliedSchedule = {
            poolSize = read(system, -1, function(s) return s:GetPoolSize() end),
            lockdownPoolSize = read(system, -1, function(s) return s:GetLockdownPoolSize() end),
            effectivePoolSize = read(system, -1, function(s) return s:GetEffectivePoolSize() end),
            cooldownHours = read(system, -1, function(s) return s:GetCooldownHours() end),
            lockdown = read(system, false, function(s) return s:IsLockdown() end),
        },
        markerSummary = {
            registered = mappins.registered,
            schedulerActive = mappins.schedulerActive,
            eligible = mappins.eligible,
            tracked = mappins.tracked,
            failures = mappins.registrationFailures,
        },
        sites = {},
    }
    for _, site in ipairs(sites.list) do
        local nextEligibleAt = read(system, 0, function(s) return s:GetNextEligibleAt(site.id) end)
        local bossID = read(system, nil, function(s) return s:GetBossEntityID(site.id) end)
        local bossTracked = false
        if bossID then pcall(function() bossTracked = EntityID.IsDefined(bossID) end) end
        payload.sites[#payload.sites + 1] = {
            id = site.id,
            status = read(system, -1, function(s) return s:GetStatus(site.id) end),
            vanillaEligible = read(system, false, function(s) return s:IsVanillaEligible(site.id) end),
            ownedActivation = read(system, false, function(s) return s:IsOwnedActivation(site.id) end),
            cycle = read(system, 0, function(s) return s:GetCycle(site.id) end),
            nextEligibleAt = nextEligibleAt,
            remainingSeconds = math.max(0, nextEligibleAt - now),
            cleanupReadyAt = read(system, 0, function(s) return s:GetCleanupReadyAt(site.id) end),
            bossTracked = bossTracked,
            bossAttachPending = read(system, false, function(s) return s:HasPendingBoss(site.id) end),
            combatSetupPending = read(system, false, function(s) return s:NeedsCombatSetup(site.id) end),
            bodyRewardReady = read(system, false, function(s) return s:NeedsBodyReward(site.id) end),
            expectedBossRecord = site.bossRecord,
        }
    end

    local raw = encode(payload)
    local wall = os.time()
    if raw ~= Diagnostics.lastSnapshot or wall - Diagnostics.lastSnapshotAt >= 60 then
        Diagnostics.event("state_snapshot", payload)
        Diagnostics.lastSnapshot = raw
        Diagnostics.lastSnapshotAt = wall
    end
end
""";

    private static string CyberpsychoDiagnosticsNew() => """
local function snapshotSignature(payload)
    local applied = payload.appliedSchedule or {}
    local markers = payload.markerSummary or {}
    local parts = {
        tostring(applied.poolSize),
        tostring(applied.lockdownPoolSize),
        tostring(applied.effectivePoolSize),
        tostring(applied.cooldownHours),
        tostring(applied.lockdown),
        tostring(markers.registered),
        tostring(markers.schedulerActive),
        tostring(markers.eligible),
        tostring(markers.tracked),
        tostring(markers.failures),
    }
    for _, site in ipairs(payload.sites or {}) do
        parts[#parts + 1] = tostring(site.id)
        parts[#parts + 1] = tostring(site.status)
        parts[#parts + 1] = tostring(site.vanillaEligible)
        parts[#parts + 1] = tostring(site.ownedActivation)
        parts[#parts + 1] = tostring(site.cycle)
        parts[#parts + 1] = tostring(site.nextEligibleAt)
        parts[#parts + 1] = tostring(site.cleanupReadyAt)
        parts[#parts + 1] = tostring(site.bossTracked)
        parts[#parts + 1] = tostring(site.bossAttachPending)
        parts[#parts + 1] = tostring(site.combatSetupPending)
        parts[#parts + 1] = tostring(site.bodyRewardReady)
    end
    return table.concat(parts, "|")
end

local function finishSnapshot(work)
    -- PASS4-R4: do not JSON-encode and append the entire diagnostic payload
    -- every 5 s merely because gameTimeSeconds/remainingSeconds changed.
    -- Emit on meaningful scheduler/site state change, plus a 60 s heartbeat.
    local signature = snapshotSignature(work.payload)
    local wall = os.time()
    if signature ~= Diagnostics.lastSnapshot or wall - Diagnostics.lastSnapshotAt >= 60 then
        Diagnostics.event("state_snapshot", work.payload)
        Diagnostics.lastSnapshot = signature
        Diagnostics.lastSnapshotAt = wall
    end
    Diagnostics.snapshotWork = nil
end

function Diagnostics.beginSnapshot(system, sites, settings, mappins)
    if not Diagnostics.enabled or not system then
        Diagnostics.snapshotWork = nil
        return false
    end

    local now = read(system, 0, function(s) return s:GetNowSeconds() end)
    Diagnostics.snapshotWork = {
        index = 1,
        now = now,
        payload = {
            gameTimeSeconds = now,
            configuredSchedule = settings.schedule,
            appliedSchedule = {
                poolSize = read(system, -1, function(s) return s:GetPoolSize() end),
                lockdownPoolSize = read(system, -1, function(s) return s:GetLockdownPoolSize() end),
                effectivePoolSize = read(system, -1, function(s) return s:GetEffectivePoolSize() end),
                cooldownHours = read(system, -1, function(s) return s:GetCooldownHours() end),
                lockdown = read(system, false, function(s) return s:IsLockdown() end),
            },
            markerSummary = {
                registered = mappins.registered,
                schedulerActive = mappins.schedulerActive,
                eligible = mappins.eligible,
                tracked = mappins.tracked,
                failures = mappins.registrationFailures,
            },
            sites = {},
        }
    }
    return true
end

function Diagnostics.isSnapshotActive()
    return Diagnostics.snapshotWork ~= nil
end

function Diagnostics.snapshotStep(system, sites, maxSites)
    local work = Diagnostics.snapshotWork
    if not work then return true end
    if not Diagnostics.enabled or not system then
        Diagnostics.snapshotWork = nil
        return true
    end

    local limit = math.max(1, tonumber(maxSites) or 1)
    local processed = 0
    while work.index <= #sites.list and processed < limit do
        local site = sites.list[work.index]
        work.index = work.index + 1
        processed = processed + 1

        local nextEligibleAt = read(system, 0, function(s) return s:GetNextEligibleAt(site.id) end)
        local bossID = read(system, nil, function(s) return s:GetBossEntityID(site.id) end)
        local bossTracked = false
        if bossID then pcall(function() bossTracked = EntityID.IsDefined(bossID) end) end

        work.payload.sites[#work.payload.sites + 1] = {
            id = site.id,
            status = read(system, -1, function(s) return s:GetStatus(site.id) end),
            vanillaEligible = read(system, false, function(s) return s:IsVanillaEligible(site.id) end),
            ownedActivation = read(system, false, function(s) return s:IsOwnedActivation(site.id) end),
            cycle = read(system, 0, function(s) return s:GetCycle(site.id) end),
            nextEligibleAt = nextEligibleAt,
            remainingSeconds = math.max(0, nextEligibleAt - work.now),
            cleanupReadyAt = read(system, 0, function(s) return s:GetCleanupReadyAt(site.id) end),
            bossTracked = bossTracked,
            bossAttachPending = read(system, false, function(s) return s:HasPendingBoss(site.id) end),
            combatSetupPending = read(system, false, function(s) return s:NeedsCombatSetup(site.id) end),
            bodyRewardReady = read(system, false, function(s) return s:NeedsBodyReward(site.id) end),
            expectedBossRecord = site.bossRecord,
        }
    end

    if work.index > #sites.list then
        finishSnapshot(work)
        return true
    end
    return false
end

function Diagnostics.snapshot(system, sites, settings, mappins)
    if not Diagnostics.beginSnapshot(system, sites, settings, mappins) then return end
    while Diagnostics.isSnapshotActive() do
        Diagnostics.snapshotStep(system, sites, #sites.list)
    end
end
""";

    private static string CyberpsychoDistanceOld() => """
local function distance2D(a, b)
    local dx, dy = a.x - b.x, a.y - b.y
    return math.sqrt(dx * dx + dy * dy)
end
""";

    private static string CyberpsychoDistanceNew() => """
local function distanceSquared2D(a, b)
    local dx, dy = a.x - b.x, a.y - b.y
    return dx * dx + dy * dy
end
""";

    private static string CyberpsychoWatchdogOld() => """
function Watchdog.sync(system, sites, settings, elapsed)
    if not system then return end
    local player = Game.GetPlayer()
    if not player then return end
    local playerPosition = player:GetWorldPosition()
    local enabled = settings.diagnostics.missingBossFallback ~= false
    local triggerDistance = settings.diagnostics.fallbackDistance or 180.0
    local graceSeconds = settings.diagnostics.fallbackGraceSeconds or 60.0

    for _, site in ipairs(sites.list) do
        local state = Watchdog.states[site.id] or {
            grace = 0.0, spawnAge = 0.0, fallbackID = nil,
            registered = false, cycle = -1,
        }
        Watchdog.states[site.id] = state

        local status, cycle = -1, 0
        pcall(function()
            status = system:GetStatus(site.id)
            cycle = system:GetCycle(site.id)
        end)
        if state.cycle ~= cycle then
            state.cycle = cycle
            state.grace = 0.0
            state.spawnAge = 0.0
            state.registered = false
        end

        local trackedID, trackedEntity = tracked(system, site.id)
        local pendingOriginal = false
        pcall(function() pendingOriginal = system:HasPendingBoss(site.id) == true end)
        if status == 1 then
            if state.fallbackID and trackedEntity
                and entityIDKey(state.fallbackID) ~= entityIDKey(trackedID) then
                deleteEntity(state.fallbackID)
                Watchdog.diagnostics.event("fallback_removed_for_original_boss", { site = site.id })
                state.fallbackID = nil
                state.registered = false
            end

            if trackedEntity then
                state.grace = 0.0
            elseif state.fallbackID then
                state.spawnAge = state.spawnAge + elapsed
                local fallback = Watchdog.bridge.entity(state.fallbackID)
                if fallback and not state.registered then
                    local accepted = false
                    pcall(function()
                        accepted = system:RegisterFallbackBoss(site.id, fallback:GetEntityID()) == true
                    end)
                    if accepted then
                        state.registered = true
                        Watchdog.diagnostics.event("fallback_boss_registered", {
                            site = site.id,
                            bossRecord = site.bossRecord,
                            entityID = entityIDKey(state.fallbackID),
                        })
                    end
                elseif not fallback and state.spawnAge >= 15.0 then
                    Watchdog.diagnostics.event("fallback_spawn_timeout", {
                        site = site.id,
                        bossRecord = site.bossRecord,
                    })
                    state.fallbackID = nil
                    state.spawnAge = 0.0
                    state.grace = 0.0
                end
            elseif pendingOriginal then
                -- The exact community actor exists but is still completing its
                -- engine attach. Never race it with a fallback copy.
                state.grace = 0.0
            elseif enabled and distance2D(playerPosition, site.position) <= triggerDistance then
                state.grace = state.grace + elapsed
                local siteGrace = site.dynamicBoss and 1.0 or graceSeconds
                if state.grace >= siteGrace then
                    if not spawn(site, state) then
                        Watchdog.diagnostics.event("fallback_spawn_failed", {
                            site = site.id,
                            bossRecord = site.bossRecord,
                        })
                        state.grace = 0.0
                    end
                end
            else
                state.grace = 0.0
            end
        else
            state.grace = 0.0
            if state.fallbackID then
                local fallback = Watchdog.bridge.entity(state.fallbackID)
                local farEnough = distance2D(playerPosition, site.position) > 120.0
                if status < 0 or status == 2 or not fallback or farEnough then
                    deleteEntity(state.fallbackID)
                    Watchdog.diagnostics.event("fallback_boss_cleaned", {
                        site = site.id,
                        status = status,
                        playerWasFarEnough = farEnough,
                    })
                    state.fallbackID = nil
                    state.spawnAge = 0.0
                    state.registered = false
                end
            end
        end
    end
end
""";

    private static string CyberpsychoWatchdogNew() => """
local function syncSiteWithPosition(system, site, settings, elapsed, playerPosition)
    local enabled = settings.diagnostics.missingBossFallback ~= false
    local triggerDistance = settings.diagnostics.fallbackDistance or 180.0
    local graceSeconds = settings.diagnostics.fallbackGraceSeconds or 60.0

    local state = Watchdog.states[site.id] or {
        grace = 0.0, spawnAge = 0.0, fallbackID = nil,
        registered = false, cycle = -1,
    }
    Watchdog.states[site.id] = state

    local status, cycle = -1, 0
    pcall(function()
        status = system:GetStatus(site.id)
        cycle = system:GetCycle(site.id)
    end)
    if state.cycle ~= cycle then
        state.cycle = cycle
        state.grace = 0.0
        state.spawnAge = 0.0
        state.registered = false
    end

    if status == 1 then
        -- PASS4-R4: only resolve the tracked boss and pending-attach state for
        -- an actually active replay. The previous hot path performed an entity
        -- lookup for every inactive site on every watchdog sweep.
        local trackedID, trackedEntity = tracked(system, site.id)
        local pendingOriginal = false
        pcall(function() pendingOriginal = system:HasPendingBoss(site.id) == true end)

        if state.fallbackID and trackedEntity
            and entityIDKey(state.fallbackID) ~= entityIDKey(trackedID) then
            deleteEntity(state.fallbackID)
            Watchdog.diagnostics.event("fallback_removed_for_original_boss", { site = site.id })
            state.fallbackID = nil
            state.registered = false
        end

        if trackedEntity then
            state.grace = 0.0
        elseif state.fallbackID then
            state.spawnAge = state.spawnAge + elapsed
            local fallback = Watchdog.bridge.entity(state.fallbackID)
            if fallback and not state.registered then
                local accepted = false
                pcall(function()
                    accepted = system:RegisterFallbackBoss(site.id, fallback:GetEntityID()) == true
                end)
                if accepted then
                    state.registered = true
                    Watchdog.diagnostics.event("fallback_boss_registered", {
                        site = site.id,
                        bossRecord = site.bossRecord,
                        entityID = entityIDKey(state.fallbackID),
                    })
                end
            elseif not fallback and state.spawnAge >= 15.0 then
                Watchdog.diagnostics.event("fallback_spawn_timeout", {
                    site = site.id,
                    bossRecord = site.bossRecord,
                })
                state.fallbackID = nil
                state.spawnAge = 0.0
                state.grace = 0.0
            end
        elseif pendingOriginal then
            -- The exact community actor exists but is still completing its
            -- engine attach. Never race it with a fallback copy.
            state.grace = 0.0
        elseif enabled and distanceSquared2D(playerPosition, site.position)
            <= triggerDistance * triggerDistance then
            state.grace = state.grace + elapsed
            local siteGrace = site.dynamicBoss and 1.0 or graceSeconds
            if state.grace >= siteGrace then
                if not spawn(site, state) then
                    Watchdog.diagnostics.event("fallback_spawn_failed", {
                        site = site.id,
                        bossRecord = site.bossRecord,
                    })
                    state.grace = 0.0
                end
            end
        else
            state.grace = 0.0
        end
    else
        state.grace = 0.0
        if state.fallbackID then
            local fallback = Watchdog.bridge.entity(state.fallbackID)
            local farEnough = distanceSquared2D(playerPosition, site.position) > (120.0 * 120.0)
            if status < 0 or status == 2 or not fallback or farEnough then
                deleteEntity(state.fallbackID)
                Watchdog.diagnostics.event("fallback_boss_cleaned", {
                    site = site.id,
                    status = status,
                    playerWasFarEnough = farEnough,
                })
                state.fallbackID = nil
                state.spawnAge = 0.0
                state.registered = false
            end
        end
    end
end

function Watchdog.syncSite(system, site, settings, elapsed)
    if not system or not site then return end
    local player = Game.GetPlayer()
    if not player then return end
    syncSiteWithPosition(system, site, settings, elapsed or 1.0, player:GetWorldPosition())
end

function Watchdog.sync(system, sites, settings, elapsed)
    if not system then return end
    local player = Game.GetPlayer()
    if not player then return end
    local playerPosition = player:GetWorldPosition()
    for _, site in ipairs(sites.list) do
        syncSiteWithPosition(system, site, settings, elapsed or 1.0, playerPosition)
    end
end
""";

    private static string CyberpsychoWorker() => """
-- G-CET semantic coordinator:repeatable-cyberpsychos
-- One owner of six 0-Engine jobs. No autonomous CET callbacks.
local M = {}
function M.attach(client, created, fn)
        -- Two alternating runtime lanes keep a full 17-site sweep below the
        -- 1 s trigger cadence at the current ~40+ FPS target while ensuring
        -- watchdog work never shares a frame with combat/body-reward work.
        local combatRewardWorkerJob = client.Schedule.EveryFrames(2, {
            id = "cyberpsychos.runtime_combat_reward",
            pause = "never",
            offset = 0
        }, function()
            fn.combatReward()
        end)
        table.insert(created, combatRewardWorkerJob)

        local watchdogWorkerJob = client.Schedule.EveryFrames(2, {
            id = "cyberpsychos.runtime_watchdog",
            pause = "never",
            offset = 1
        }, function()
            fn.watchdog()
        end)
        table.insert(created, watchdogWorkerJob)

        -- Keep the existing one-site/every-other-frame presentation behavior,
        -- but give it its own profiler identity instead of hiding it inside the
        -- runtime worker.
        local presentationWorkerJob = client.Schedule.EveryFrame({
            id = "cyberpsychos.presentation_worker",
            active = false,
            pause = "never"
        }, function()
            fn.presentationWorker()
        end)
        table.insert(created, presentationWorkerJob)

        local runtimeJob = client.Schedule.Every(1.0, {
            id = "cyberpsychos.runtime",
            pause = "never",
            clock = "game",
            catchUp = false
        }, function(ctx)
            fn.runtime(ctx.elapsed)
        end)
        table.insert(created, runtimeJob)

        local presentationJob = client.Schedule.Every(5.0, {
            id = "cyberpsychos.presentation",
            pause = "never",
            clock = "game",
            catchUp = false
        }, function()
            fn.presentation()
        end)
        table.insert(created, presentationJob)

        local schedulerJob = client.Schedule.Every(10.0, {
            id = "cyberpsychos.scheduler",
            pause = "never",
            clock = "game",
            catchUp = false
        }, function(ctx)
            fn.scheduler(ctx.elapsed)
        end)
        table.insert(created, schedulerJob)


    return presentationWorkerJob
end
return M
""";

}
