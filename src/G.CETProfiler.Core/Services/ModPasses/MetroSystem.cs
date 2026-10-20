using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMetroSystem(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "ts.entrySys:update()",
            "ts.stationSys:update(deltaTime)",
            "ts.Cron.Update(deltaTime)",
            "ts.hud.draw(ts)");

        var entry = context.FindFile(
            "modules/entrySystem.lua",
            "function entrySys:getClosestEntry",
            "self.entries",
            "looksAtEntry",
            "waypointPosition");

        var hud = context.FindFile(
            "modules/ui/hud.lua",
            "function hud.draw(ts)",
            "hud.destVisible",
            "observers.nextStationPoint",
            "drawDestinations");

        var metroOpening = FindOnUpdateOpening(init.Text, "deltaTime");
        var initText = ReplaceOnce(
            init.Text,
            metroOpening,
            "local __gcetMetroIdleElapsed = 0.0\n\n" + metroOpening,
            "Metro System idle cadence state");

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^(?<indent>\s*)if\s+\(not\s+ts\.runtimeData\.inMenu\)\s+and\s+ts\.runtimeData\.inGame\s+and\s+\(math\.floor\(observers\.timeDilation\)\s*~=\s*0\).*then\s*$",
            "${indent}if (not ts.runtimeData.inMenu) and ts.runtimeData.inGame and (math.floor(observers.timeDilation) ~= 0) and ts.archiveInstalled and ts.axlInstalled and ts.cwInstalled then\n" +
            "${indent}    local __gcetMetroActive = ts.runtimeData.cetOpen == true\n" +
            "${indent}        or ts.entrySys.forceRunCron == true\n" +
            "${indent}        or ts.stationSys.currentStation ~= nil\n" +
            "${indent}        or ts.stationSys.activeTrain ~= nil\n" +
            "${indent}        or ts.observers.noSave == true\n" +
            "${indent}        or ts.observers.noTrains == true\n" +
            "${indent}        or ts.input.interactKey == true\n" +
            "${indent}    if __gcetMetroActive then\n" +
            "${indent}        __gcetMetroIdleElapsed = 0.0\n" +
            "${indent}    else\n" +
            "${indent}        __gcetMetroIdleElapsed = __gcetMetroIdleElapsed + math.max(tonumber(deltaTime) or 0.0, 0.0)\n" +
            "${indent}        if __gcetMetroIdleElapsed < 0.20 then return end\n" +
            "${indent}        deltaTime = __gcetMetroIdleElapsed\n" +
            "${indent}        __gcetMetroIdleElapsed = 0.0\n" +
            "${indent}    end",
            "Metro System active/idle world cadence");
        context.Write(init, initText);

        var entryText = RegexReplaceOnce(
            entry.Text,
            @"(?m)^(?<opening>\s*function\s+entrySys:getClosestEntry\s*\(\s*\)\s*)$",
            "${opening}\n" +
            "    local __gcetPlayer = GetPlayer()\n" +
            "    if __gcetPlayer == nil then return nil end\n" +
            "    local __gcetPlayerPos = __gcetPlayer:GetWorldPosition()",
            "Metro System closest entry player cache");

        entryText = RegexReplaceOnce(
            entryText,
            @"utils\.distanceVector\s*\(\s*GetPlayer\(\):GetWorldPosition\(\)\s*,\s*v\.center\s*\)",
            "utils.distanceVector(__gcetPlayerPos, v.center)",
            "Metro System entry sweep cached player position");
        context.Write(entry, entryText);

        var hudText = RegexReplaceOnce(
            hud.Text,
            @"(?m)^(?<indent>\s*)destVisible\s*=\s*false\s*,\s*$",
            "${indent}destVisible = false,\n" +
            "${indent}destinationWasVisible = false,",
            "Metro System destination visibility state");

        hudText = RegexReplaceOnce(
            hudText,
            @"(?ms)^(?<indent>\s*)if\s+hud\.destVisible\s+then\s*\r?\n\s*hud\.drawDestinations\s*\(\s*ts\.stationSys\s*\)\s*\r?\n\s*hud\.destVisible\s*=\s*false\s*\r?\n\s*else\s*\r?\n\s*observers\.nextStationText\s*=\s*[""'][""']\s*\r?\n\s*(?<mappin>Game\.GetMappinSystem\(\)|__gcetGetMappinSystem\(\)):UnregisterMappin\s*\(\s*observers\.nextStationPoint\s*\)\s*\r?\n\s*if\s+ts\.observers\.hudText\s+then\s*\r?\n\s*ts\.observers\.hudText:SetVisible\s*\(\s*false\s*\)\s*\r?\n\s*end\s*\r?\n\s*end\s*$",
            "${indent}if hud.destVisible then\n" +
            "${indent}    hud.drawDestinations(ts.stationSys)\n" +
            "${indent}    hud.destVisible = false\n" +
            "${indent}    hud.destinationWasVisible = true\n" +
            "${indent}elseif hud.destinationWasVisible or observers.nextStationPoint ~= nil or observers.nextStationText ~= \"\" then\n" +
            "${indent}    observers.nextStationText = \"\"\n" +
            "${indent}    if observers.nextStationPoint ~= nil then\n" +
            "${indent}        ${mappin}:UnregisterMappin(observers.nextStationPoint)\n" +
            "${indent}        observers.nextStationPoint = nil\n" +
            "${indent}    end\n" +
            "${indent}    if ts.observers.hudText then\n" +
            "${indent}        ts.observers.hudText:SetVisible(false)\n" +
            "${indent}    end\n" +
            "${indent}    hud.destinationWasVisible = false\n" +
            "${indent}end",
            "Metro System destination cleanup transition");
        context.Write(hud, hudText);

        return SemanticInjectionResult.Success(
            "Kept station/train/elevator activity frame-responsive, reduced idle world discovery to 5 Hz with accumulated delta, cached one player position per entry sweep, and made destination mappin teardown transition-driven.");
    }

}
