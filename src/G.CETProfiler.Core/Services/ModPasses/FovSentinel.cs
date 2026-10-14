using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyFovSentinel(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "FindVehicleCameraManager",
            "##FakeWidget",
            "local ac=a9()",
            "ad(a9(),true,0)");

        var text = ReplaceOnce(
            file.Text,
            "local aA=ad(a9(),true,0)",
            "local aA=ad(ac,true,0)",
            "FOV Sentinel duplicate TPP camera lookup");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Reused the already-computed TPP camera-state boolean inside the widget path instead of repeating FindVehicleCameraManager through a second a9() call; rendering cadence and visibility semantics are unchanged.");
    }

}
