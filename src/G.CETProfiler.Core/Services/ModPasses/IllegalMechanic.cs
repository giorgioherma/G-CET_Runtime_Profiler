using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyIllegalMechanic(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "hack_signal_interaction.lua",
            "function HackSignalInteraction.Update(delta)",
            "state.nextInteractionUpdateAt",
            "updatePulseVisual",
            "interactionUI.update()");

        var anchor =
            "  if not state.initialized then\n" +
            "    interactionUI.update()\n" +
            "    return\n" +
            "  end\n\n";

        var gate =
            anchor +
            "  -- G-CET semantic split: an active HACK search pulse retains rendered-frame\n" +
            "  -- visual timing. With no pulse/hub, defer the expensive ScriptableSystem\n" +
            "  -- lookup until the mod's existing interaction polling deadline.\n" +
            "  if not state.pulseActive and not state.hubVisible and currentTime < state.nextInteractionUpdateAt then\n" +
            "    interactionUI.update()\n" +
            "    return\n" +
            "  end\n\n";

        var text = ReplaceOnce(
            file.Text,
            anchor,
            gate,
            "Illegal Mechanic HACK pulse/interaction cadence boundary");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept active HACK pulse visuals frame-responsive while avoiding ScriptableSystem and interaction discovery work between the existing 0.20 s polling deadlines when idle.");
    }

}
