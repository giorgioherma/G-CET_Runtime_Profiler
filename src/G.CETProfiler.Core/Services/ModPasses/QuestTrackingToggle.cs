using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyQuestTrackingToggle(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "n_ToggleSprint",
            "handlePadAction",
            "handlePadLongPressTrigger",
            "toggleTrackedQuest");

        var text = RouteExactOnActionObserver(
            file.Text,
            "questTrackingOnAction",
            "QuestTrackingToggle",
            new[]
            {
                "LeanFB",
                "ToggleSprint",
                "CameraAim",
                "world_map_menu_rotate_mouse",
                "world_map_menu_zoom_to_mappin",
                "PhoneInteract",
                "Jump",
                "Handbrake",
                "world_map_filter_navigation_down",
                "world_map_menu_track_waypoint"
            },
            "QuestTrackingToggle PlayerPuppet OnAction",
            "handlePadAction",
            "handlePadLongPressTrigger",
            "toggleTrackedQuest");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed QuestTrackingToggle's ten finite source-proven actions through 0-Engine; the original callback body, Consume calls, hold/release handling and LeanFB axis behavior remain intact.");
    }

}
