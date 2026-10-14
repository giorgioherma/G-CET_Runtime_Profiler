using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAppearanceMenuModSqueeze(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "AMM.currentTarget = AMM:GetTarget()",
            "function AMM:GetTarget()",
            "AMM:CheckCustomDefaults(target)",
            "AMM:CheckSavedAppearance(target)",
            "AMM.Director:SenseTriggers()",
            "AMM.Scan:SenseSavedDespawns()",
            "Cron.Update(deltaTime)",
            "drawWindow or AMM.Props.buildMode",
            "AMM.Tools.currentTarget:Move()");

        var text = file.Text;

        // UI lane: AMM's draw callback does not need to execute at all unless
        // the main window or Decor build-mode UI is visible. No world/runtime
        // work is moved behind this gate.
        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<blank>[ \t]*\r?\n)?(?<indent>[ \t]*)ImGui\.SetNextWindowPos\s*\(",
            "${opening}\n${indent}if not drawWindow and not AMM.Props.buildMode then return end\n${blank}${indent}ImGui.SetNextWindowPos(",
            "AMM hidden UI draw dormancy");

        // Safe local squeeze only: acquire the targeting-system handle once
        // inside the current GetTarget invocation. Do not retain target/entity
        // handles, weak references, or native object identity across frames.
        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>[ \t]*)local\s+target\s*=\s*(?<getter>(?:Game\.GetTargetingSystem|__gcetGetTargetingSystem))\s*\(\s*\)\s*:\s*GetLookAtObject\s*\(\s*player\s*,\s*true\s*,\s*false\s*\)\s+or\s+\k<getter>\s*\(\s*\)\s*:\s*GetLookAtObject\s*\(\s*player\s*,\s*false\s*,\s*false\s*\)\s*$",
            "${indent}local targetingSystem = ${getter}()\n" +
            "${indent}local target = targetingSystem and (targetingSystem:GetLookAtObject(player, true, false) or targetingSystem:GetLookAtObject(player, false, false)) or nil",
            "AMM GetTarget single targeting-system acquisition");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept AMM's hidden UI draw lane dormant and collapsed the duplicated targeting-system acquisition inside each GetTarget call. The author's periodic target refresh, Cron/timers, world sensing, saved-appearance behavior, active camera/light/direct-mode movement and API/input callers remain unchanged; no native target/entity reference is cached across frames.");
    }


}
