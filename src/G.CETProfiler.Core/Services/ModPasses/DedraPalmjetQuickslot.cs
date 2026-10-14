using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDedraPalmjetQuickslot(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function installPalmjetHooks",
            "isUseCombatGadgetPress",
            "UseCombatGadget",
            "BUTTON_PRESSED",
            "activateSelectedPalmjet");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetDedraPalmjetOnAction",
            "DedraPalmjetQuickslot",
            new[] { "UseCombatGadget" },
            "DedraPalmjetQuickslot PlayerPuppet OnAction",
            "isUseCombatGadgetPress",
            "getActivePalmjetVariant",
            "activateSelectedPalmjet");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed Dedra Palmjet's single source-proven UseCombatGadget action through 0-Engine while retaining the original BUTTON_PRESSED and active-variant gates.");
    }

}
