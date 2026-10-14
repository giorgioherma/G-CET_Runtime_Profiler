using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRepeatableIncreasedCriminalActivity(
        SemanticPatchContext context)
    {
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

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Applied RICA's validated multi-rate answer without version-locking Scheduler plumbing: rewards at 4 Hz with bounded batches, runtime state and mappins at 1 Hz, diagnostics at 0.2 Hz, and the scriptable-system tick at 0.1 Hz.");
    }

}
