using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAirBackFlip(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "GAME_ACTIONS",
            "AirBackflip_Backflip",
            "AirBackflip_SwingOver",
            "MoveY",
            "ListenerAction.IsButtonJustPressed",
            "ListenerAction.IsButtonJustReleased");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetAirBackFlipOnAction",
            "AirBackFlip",
            new[] {
                "MoveY",
                "AirBackflip_Backflip",
                "AirBackflip_Frontflip",
                "AirBackflip_SideflipLeft",
                "AirBackflip_SideflipRight",
                "AirBackflip_SwingOver"
            },
            "AirBackFlip PlayerPuppet OnAction",
            "GAME_ACTIONS",
            "MoveY",
            "ListenerAction.IsButtonJustPressed",
            "ListenerAction.IsButtonJustReleased");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed only MoveY and AirBackFlip's five game actions through 0-Engine. Airborne eligibility, press/release semantics, flip counters and active-flip frame cadence are unchanged.");
    }

}
