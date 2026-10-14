using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRoulette(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "SpotManager.update",
            "Cron.Update",
            "TableManager.GetActiveTable",
            "RouletteMainMenu.Update");

        var opening = FindOnUpdateOpening(init.Text, "dt");
        var initText = ReplaceOnce(
            init.Text,
            opening,
            "local __gcetRouletteIdleElapsed = 0.0\n\n" +
            opening +
            "    local __gcetFrameDt = math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "    local __gcetRealtime = SpotManager.IsPlayerInSpot() or TableManager.GetActiveTable() ~= nil\n" +
            "    if not __gcetRealtime then\n" +
            "        __gcetRouletteIdleElapsed = __gcetRouletteIdleElapsed + __gcetFrameDt\n" +
            "        if __gcetRouletteIdleElapsed < 0.10 then return end\n" +
            "        dt = __gcetRouletteIdleElapsed\n" +
            "        __gcetRouletteIdleElapsed = 0.0\n" +
            "    else\n" +
            "        __gcetRouletteIdleElapsed = 0.0\n" +
            "    end\n",
            "Roulette active/idle runtime gate");
        context.Write(init, initText);

        ApplyVisibleSpotMappinPrefilter(
            context,
            "SpotManager.lua",
            "roulette");

        return SemanticInjectionResult.Success(
            "Restored frame cadence while a roulette table is active, reduced far/idle runtime to 10 Hz with accumulated delta, and rejected global mappin callbacks when no roulette prompt is visible.");
    }

}
