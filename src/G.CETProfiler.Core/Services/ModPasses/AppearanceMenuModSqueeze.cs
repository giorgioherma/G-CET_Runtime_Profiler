using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    internal static bool IsAppearanceMenuModSqueezeComplete(string ownerFolder)
    {
        try
        {
            var source = File.ReadAllText(Path.Combine(ownerFolder, "init.lua"));
            if (!source.Contains("if not drawWindow and not AMM.Props.buildMode then return end",
                    StringComparison.Ordinal) ||
                !source.Contains("local targetingSystem =", StringComparison.Ordinal))
                return false;
            // Older supported sources without gtaTravel integration need no
            // new stage; real modern AMM source must satisfy this extra proof.
            if (!source.Contains("GetMod(\"gtaTravel\")", StringComparison.Ordinal) &&
                !source.Contains("GetMod('gtaTravel')", StringComparison.Ordinal))
                return true;
            return Regex.IsMatch(source,
                @"local\s+mod\s*=\s*AMM\.TeleportMod\s*==\s*nil\s+and\s+GetMod\s*\(",
                RegexOptions.CultureInvariant);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

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
        if (!Regex.IsMatch(text, @"\bif\s+not\s+drawWindow\s+and\s+not\s+AMM\.Props\.buildMode\s+then\s+return\s+end", RegexOptions.CultureInvariant))
        {
        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<blank>[ \t]*\r?\n)?(?<indent>[ \t]*)ImGui\.SetNextWindowPos\s*\(",
            "${opening}\n${indent}if not drawWindow and not AMM.Props.buildMode then return end\n${blank}${indent}ImGui.SetNextWindowPos(",
            "AMM hidden UI draw dormancy");

        }

        // Safe local squeeze only: acquire the targeting-system handle once
        // inside the current GetTarget invocation. Do not retain target/entity
        // handles, weak references, or native object identity across frames.
        if (!text.Contains("local targetingSystem =", StringComparison.Ordinal))
        {
        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>[ \t]*)local\s+target\s*=\s*(?<getter>(?:Game\.GetTargetingSystem|__gcetGetTargetingSystem))\s*\(\s*\)\s*:\s*GetLookAtObject\s*\(\s*player\s*,\s*true\s*,\s*false\s*\)\s+or\s+\k<getter>\s*\(\s*\)\s*:\s*GetLookAtObject\s*\(\s*player\s*,\s*false\s*,\s*false\s*\)\s*$",
            "${indent}local targetingSystem = ${getter}()\n" +
            "${indent}local target = targetingSystem and (targetingSystem:GetLookAtObject(player, true, false) or targetingSystem:GetLookAtObject(player, false, false)) or nil",
            "AMM GetTarget single targeting-system acquisition");

        }

        // Source-verified B-stage for previously skipped AMM: the only
        // gtaTravel consumer already requires TeleportMod == nil, so when
        // that author cache is bound, skip redundant GetMod every frame.
        // Resetting TeleportMod to nil still permits immediate discovery.
        if (text.Contains("GetMod(\"gtaTravel\")", StringComparison.Ordinal) ||
            text.Contains("GetMod('gtaTravel')", StringComparison.Ordinal))
        {
            var existing = Regex.IsMatch(text,
                @"local\s+mod\s*=\s*AMM\.TeleportMod\s*==\s*nil\s+and\s+GetMod\s*\(",
                RegexOptions.CultureInvariant);
            if (!existing)
            {
                var sourcePattern =
                    @"(?m)^(?<indent>[ \t]*)local\s+mod\s*=\s*GetMod\s*\(\s*['""]gtaTravel['""]\s*\)\s*\r?\n" +
                    @"[ \t]*if\s+mod\s*~=\s*nil\s+and\s+AMM\.TeleportMod\s*==\s*nil\s+then";
                var check = new Regex(sourcePattern, RegexOptions.CultureInvariant);
                if (check.Matches(text).Count != 1 ||
                    !text.Contains("AMM.TeleportMod = mod", StringComparison.Ordinal))
                    throw new InvalidOperationException("AMM gtaTravel late-binding source changed.");
                text = check.Replace(text,
                    m => m.Groups["indent"].Value +
                        "local mod = AMM.TeleportMod == nil and GetMod(\"gtaTravel\") or nil\n" +
                        m.Groups["indent"].Value +
                        "if mod ~= nil and AMM.TeleportMod == nil then", 1);
            }
        }
        if (text == file.Text)
            return SemanticInjectionResult.Skip("AMM UI/target/native lookup source stages already satisfied.");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept AMM's hidden UI draw lane dormant and collapsed the duplicated targeting-system acquisition inside each GetTarget call. The author's periodic target refresh, Cron/timers, world sensing, saved-appearance behavior, active camera/light/direct-mode movement and API/input callers remain unchanged; no native target/entity reference is cached across frames.");
    }


}
