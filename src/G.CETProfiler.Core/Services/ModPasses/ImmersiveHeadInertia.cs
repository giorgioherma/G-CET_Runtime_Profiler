using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyImmersiveHeadInertia(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Inertia.onAction",
            "ConsumeSingleAction",
            "Observe(\"PlayerPuppet\", \"OnAction\"");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetImmersiveHeadInertiaOnAction",
            "ImmersiveHeadInertia",
            new[] { "CameraMouseX", "CameraMouseY" },
            "ImmersiveHeadInertia PlayerPuppet OnAction",
            "Inertia.onAction",
            "ConsumeSingleAction");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed ImmersiveHeadInertia's only two source-proven PlayerPuppet actions, CameraMouseX/Y, through 0-Engine while retaining action consumption and all inertia timing.");
    }

}
