using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyBackStepDuo(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "processDirectionAction",
            "getActionName",
            "getActionType",
            "getActionValue",
            "BUTTON_PRESSED",
            "movey",
            "movex");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetBackStepDuoOnAction",
            "BackStepDuo",
            new[] { "Forward", "Back", "Left", "Right", "MoveY", "MoveX" },
            "BackStepDuo PlayerPuppet OnAction",
            "settings.EnableDash",
            "getActionName",
            "getActionType",
            "getActionValue",
            "processDirectionAction");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed BackStepDuo's six source-proven movement actions through 0-Engine; ability, BUTTON_PRESSED, value and double-tap semantics remain in the original callback body.");
    }

}
