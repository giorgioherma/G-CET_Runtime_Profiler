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


    private static SemanticInjectionResult ApplyIllegalMechanicNpcPositionReuse(SemanticPatchContext context)
    {
        var file = context.FindFile("physical_npc.lua", "function PhysicalNpc.Update(delta)",
            "local function distanceFromPlayer(entry, player)", "state.mechanics",
            "distanceFromPlayer(entry, player)");
        var text = file.Text;
        const string stage = "__gcetMechanicPlayerPosition";
        if (text.Contains(stage, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Synchronous per-mechanic player-position reuse is installed.");
        var signature = new Regex(
            @"(?m)^local\s+function\s+distanceFromPlayer\(\s*entry\s*,\s*player\s*\)\s*$",
            RegexOptions.CultureInvariant);
        var getter = new Regex(
            @"return\s+Vector4\.Distance\(\s*player:GetWorldPosition\(\)\s*,\s*anchorPosition\(entry\)\s*\)",
            RegexOptions.CultureInvariant);
        var loop = new Regex(
            @"(?m)^(?<indent>[ \t]*)for\s+_,\s*entry\s+in\s+ipairs\(state\.mechanics\)\s+do\r?\n(?<body>(?:(?!^\k<indent>end\s*$)[\s\S])*?)^\k<indent>end\s*$",
            RegexOptions.CultureInvariant);
        var call = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+distance\s*=\s*distanceFromPlayer\(\s*entry\s*,\s*player\s*\)\s*$",
            RegexOptions.CultureInvariant);
        if (signature.Matches(text).Count != 1 || getter.Matches(text).Count != 1 ||
            call.Matches(text).Count != 1 ||
            !text.Contains("if entry.spawnPending then", StringComparison.Ordinal) ||
            !text.Contains("resolvePendingSpawn(entry)", StringComparison.Ordinal))
            throw new InvalidOperationException("Physical NPC lifetime/pending/position reads changed.");
        text = signature.Replace(text,
            "local function distanceFromPlayer(entry, player, playerPos)");
        text = getter.Replace(text,
            "local pos = playerPos or player:GetWorldPosition()\n    return Vector4.Distance(pos, anchorPosition(entry)), pos");
        const string priorReturn = "  return ok and distance or nil\nend";
        if (text.Split(priorReturn, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("Physical NPC protected distance fallback changed.");
        text = text.Replace("local ok, distance = pcall(function()", "local ok, distance, pos = pcall(function()", StringComparison.Ordinal);
        text = text.Replace(priorReturn,
            "  return ok and distance or nil, ok and pos or nil\nend", StringComparison.Ordinal);
        var hits = call.Matches(text);
        text = call.Replace(text, m =>
            m.Groups["indent"].Value + "local distance, __gcetPositionObserved = distanceFromPlayer(entry, player, " + stage + ")\n" +
            m.Groups["indent"].Value + "if __gcetPositionObserved then " + stage + " = __gcetPositionObserved end");
        var loopAnchor = new Regex(
            @"(?m)^(?<indent>[ \t]*)if\s+player\s*==\s*nil\s+then\s+return\s+end\r?\n" +
            @"\r?\n\k<indent>for\s+_,\s*entry\s+in\s+ipairs\(state\.mechanics\)\s+do",
            RegexOptions.CultureInvariant);
        if (loopAnchor.Matches(text).Count != 1)
            throw new InvalidOperationException("Physical NPC main scan boundary changed.");
        // Cache belongs to the main periodic scan, not the earlier pending-spawn loop.
        text = loopAnchor.Replace(text, m => m.Groups["indent"].Value +
            "if player == nil then return end\n\n" +
            m.Groups["indent"].Value + "local " + stage + " = nil\n" +
            m.Groups["indent"].Value + "for _, entry in ipairs(state.mechanics) do");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Reused freshly obtained player world position within one NPC supervisor scan; preserved protected getter fallback, native distance math, pending spawn and each lifecycle decision.");
    }
}
