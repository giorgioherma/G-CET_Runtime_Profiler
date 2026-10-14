using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDynamicOutfitsJudy(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "DynamicFramework.isGameLoaded",
            "ScriptedPuppet",
            "PrefetchAppearanceChange",
            "ScheduleAppearanceChange",
            "DynamicFramework:GetActiveShieldInfo");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetDynamicIdleElapsed = 0.0\n\n" + opening,
            "Dynamic Outfits Judy idle cadence state");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^(?<indent>[ \t]*)local\s+srh\s*=\s*__gcetGetSystemRequestsHandler\s*\(\s*\)\s*\r?\n\s*if\s+not\s+srh\s+or\s+srh:IsPreGame\s*\(\s*\)\s+then\s*\r?\n\s*DynamicFramework\.isGameLoaded\s*=\s*false\s*\r?\n\s*return\s*\r?\n\s*end\s*\r?\n\s*\r?\n\s*local\s+player\s*=\s*__gcetGetPlayer\s*\(\s*\)\s*$",
            "${indent}local srh = __gcetGetSystemRequestsHandler()\n" +
            "${indent}if not srh or srh:IsPreGame() then\n" +
            "${indent}    DynamicFramework.isGameLoaded = false\n" +
            "${indent}    return\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetAnySpawned = false\n" +
            "${indent}for _, __gcetCharState in pairs(DynamicFramework.states) do\n" +
            "${indent}    if __gcetCharState.is_spawned and __gcetCharState.npc_entity then\n" +
            "${indent}        __gcetAnySpawned = true\n" +
            "${indent}        break\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if DynamicFramework.isGameLoaded and not __gcetAnySpawned then\n" +
            "${indent}    __gcetDynamicIdleElapsed = __gcetDynamicIdleElapsed + math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "${indent}    if __gcetDynamicIdleElapsed < 0.25 then return end\n" +
            "${indent}    dt = __gcetDynamicIdleElapsed\n" +
            "${indent}    __gcetDynamicIdleElapsed = 0.0\n" +
            "${indent}else\n" +
            "${indent}    __gcetDynamicIdleElapsed = 0.0\n" +
            "${indent}end\n\n" +
            "${indent}local player = __gcetGetPlayer()",
            "Dynamic Outfits Judy event-woken idle lane");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved full frame-rate scene/appearance processing whenever a managed NPC is attached, while event-driven OnGameAttached/OnDetach state drops the unspawned framework to 4 Hz with accumulated delta; first-load/session initialization still bypasses the idle gate.");
    }

}
