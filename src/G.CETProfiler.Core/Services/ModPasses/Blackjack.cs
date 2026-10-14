using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyBlackjack(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "SpotManager.update",
            "Cron.Update",
            "BlackjackMainMenu.Update",
            "SpotManager.IsPlayerInSpot");

        var opening = FindOnUpdateOpening(init.Text, "dt");
        var initText = ReplaceOnce(
            init.Text,
            opening,
            "local __gcetBlackjackIdleElapsed = 0.0\n\n" +
            opening +
            "    local __gcetFrameDt = math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "    if not SpotManager.IsPlayerInSpot() then\n" +
            "        __gcetBlackjackIdleElapsed = __gcetBlackjackIdleElapsed + __gcetFrameDt\n" +
            "        if __gcetBlackjackIdleElapsed < 0.05 then return end\n" +
            "        dt = __gcetBlackjackIdleElapsed\n" +
            "        __gcetBlackjackIdleElapsed = 0.0\n" +
            "    else\n" +
            "        __gcetBlackjackIdleElapsed = 0.0\n" +
            "    end\n",
            "Blackjack active/idle runtime gate");
        context.Write(init, initText);

        ApplyVisibleSpotMappinPrefilter(
            context,
            "SpotManager.lua",
            "blackjack");

        return SemanticInjectionResult.Success(
            "Preserved frame cadence while seated at blackjack, reduced idle/proximity runtime to 20 Hz with accumulated delta, and rejected global mappin callbacks when no blackjack prompt is visible.");
    }

}
