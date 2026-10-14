using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGoodFeelings(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Event.Observe(\"PlayerPuppet\", \"OnAction\"",
            "SelfFeature.NoClip.HandleMouseLook",
            "Utils.Weapon.HandleInputAction");

        var text = RouteExactOnActionObserver(
            init.Text,
            "gcetGoodFeelingsOnAction",
            "GoodFeelings",
            new[] { "CameraMouseX", "RangedAttack" },
            "GoodFeelings PlayerPuppet OnAction",
            "modulesLoaded",
            "SelfFeature.NoClip.HandleMouseLook",
            "Utils.Weapon.HandleInputAction");

        context.Write(init, text);
        return SemanticInjectionResult.Success(
            "Routed GoodFeelings' two source-proven PlayerPuppet actions through 0-Engine with routed action-type decoding disabled; UI draw dormancy is handled independently.");
    }

    private static SemanticInjectionResult ApplyGoodFeelingsHardDraw(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Event.RegisterUpdate(function(dt)",
            "Event.RegisterDraw(function()",
            "Handler.Update()",
            "State.menuOpen",
            "UI.Notification.Render()",
            "UI.Overlay.Render()");

        var text = init.Text;

        // F4/controller wake currently lives in Handler.Update(), which the author
        // calls from onDraw. Move that tiny input/menu handler to onUpdate so a
        // closed draw callback can sleep completely without losing its wake path.
        text = RegexReplaceOnce(
            text,
            @"(?m)^[ \t]*Handler\.Update\s*\(\s*\)\s*$",
            "",
            "GoodFeelings remove Handler.Update from onDraw");

        text = RegexReplaceOnce(
            text,
            @"(?ms)(Event\.RegisterUpdate\s*\(\s*function\s*\(\s*dt\s*\)\s*\r?\n[ \t]*Cron\.Update\s*\(\s*dt\s*\)\s*\r?\n\s*[ \t]*if\s+not\s+modulesLoaded\s+then\s+return\s+end\s*\r?\n)",
            "$1    Handler.Update()\n",
            "GoodFeelings F4 wake onUpdate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*Event\.RegisterDraw\s*\(\s*function\s*\(\s*\)\s*)\r?\n",
            "${opening}\n    if not modulesLoaded or not State.menuOpen then return end\n",
            "GoodFeelings hard closed draw gate");

        context.Write(init, text);
        return SemanticInjectionResult.Success(
            "Moved GoodFeelings' F4/controller menu wake to onUpdate and hard-gated the entire onDraw callback on State.menuOpen. Notifications, welcome UI, status overlay, debug draw and cheat menu now do zero draw work while the F4 UI is closed.");
    }

}
