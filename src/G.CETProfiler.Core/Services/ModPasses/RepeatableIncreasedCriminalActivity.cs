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

        var initTail = "\nend)\n\n" + opening;
        if(text.Split(initTail,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("RICA onInit/onUpdate scheduling handoff boundary changed.");
        text = ReplaceOnce(text,initTail,
            "\n    ricaBaseInitialized = true\n    TryAttachRicaPass4()\nend)\n\n"+opening,
            "RICA one-time scheduler initialization");
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

}
