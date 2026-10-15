using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyIllegalMechanic(
        SemanticPatchContext context)
    {
        // Independently complete the existing HACK pulse gate and the
        // immutable mechanic-position cache. Earlier live passes already
        // have the HACK gate; do not rewrite or revert that work.
        var hack = context.FindFile(
            "hack_signal_interaction.lua",
            "function HackSignalInteraction.Update(delta)",
            "state.nextInteractionUpdateAt",
            "updatePulseVisual",
            "interactionUI.update()");

        const string gateCondition =
            "if not state.pulseActive and not state.hubVisible and currentTime < state.nextInteractionUpdateAt then";
        var gatePresent = hack.Text.Contains(gateCondition, StringComparison.Ordinal);
        if (!gatePresent)
        {
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
                "  " + gateCondition + "\n" +
                "    interactionUI.update()\n" +
                "    return\n" +
                "  end\n\n";

            context.Write(hack, ReplaceOnce(
                hack.Text, anchor, gate,
                "Illegal Mechanic HACK pulse/interaction cadence boundary"));
        }
        else if (!hack.Text.Contains(
                     "state.nextInteractionUpdateAt = currentTime + cfg.interactionUpdateInterval",
                     StringComparison.Ordinal) ||
                 !hack.Text.Contains("updatePulseVisual(system, currentTime)",
                     StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Illegal Mechanic HACK gate exists but its original pulse/poll paths are incomplete.");
        }

        var physical = context.FindFile(
            "physical_npc.lua",
            "local function anchorPosition(entry)",
            "Vector4.new(entry.x, entry.y, entry.z, 1.0)",
            "state.mechanics = result");

        const string originalAnchor =
            "local function anchorPosition(entry)\n" +
            "  return Vector4.new(entry.x, entry.y, entry.z, 1.0)\n" +
            "end";
        const string cachedAnchor =
            "local function anchorPosition(entry)\n" +
            "  -- G-CET: positions originate from the immutable per-load mechanic matrix.\n" +
            "  -- Keep the cached value on the matrix entry so a matrix reload invalidates it.\n" +
            "  if entry.__gcetAnchorPosition == nil then\n" +
            "    entry.__gcetAnchorPosition = Vector4.new(entry.x, entry.y, entry.z, 1.0)\n" +
            "  end\n" +
            "  return entry.__gcetAnchorPosition\n" +
            "end";

        if (physical.Text.Contains("__gcetAnchorPosition", StringComparison.Ordinal))
        {
            if (!physical.Text.Contains(cachedAnchor, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Illegal Mechanic existing position-cache stage is incomplete or modified.");
        }
        else
        {
            // Must not freeze dynamically spawned NPC entities, only static
            // matrix coordinates. Reuse only the original author helper.
            context.Write(physical, ReplaceOnce(
                physical.Text, originalAnchor, cachedAnchor,
                "Illegal Mechanic per-matrix-entry anchor cache"));
        }

        return context.Changes.Count == 0
            ? SemanticInjectionResult.Skip(
                "Illegal Mechanic HACK gating and mechanic anchor caching are complete.")
            : SemanticInjectionResult.Success(
                "Preserved HACK pulse and discovery deadlines; cached only immutable " +
                "per-matrix mechanic positions, invalidated by the original matrix reload.");
    }
}
