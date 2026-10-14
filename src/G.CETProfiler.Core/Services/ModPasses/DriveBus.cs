using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDriveBus(
        SemanticPatchContext context)
    {
        var core = context.FindFile(
            "Modules/core.lua",
            "function Core:SetObserve()",
            "exception_in_choice_list",
            "exception_in_mount_list",
            "self:ChoiceAction(action_name");

        var coreText = RouteExactOnActionObserver(
            core.Text,
            "gcetDriveBusOnAction",
            "DriveBus",
            new[]
            {
                "QuickExit",
                "NextWeapon",
                "PreviousWeapon",
                "ChoiceApply",
                "ChoiceScrollUp",
                "ChoiceScrollDown"
            },
            "DriveBus PlayerPuppet OnAction",
            "exception_in_choice_list",
            "exception_in_mount_list",
            "self:ChoiceAction(action_name");

        var cron = context.FindFile(
            "External/Cron.lua",
            "local timers = {}",
            "function Cron.Update",
            "function Cron.Every");

        var cronText = cron.Text;
        if (!Regex.IsMatch(
                cronText,
                @"(?m)^\s*function\s+Cron\.HasActiveTimers\s*\(",
                RegexOptions.CultureInvariant))
        {
            cronText = RegexReplaceOnce(
                cronText,
                @"(?m)^(?<indent>[ \t]*)function\s+Cron\.Update\s*\(\s*delta\s*\)",
                "${indent}function Cron.HasActiveTimers()\n" +
                "${indent}\tfor _, timer in ipairs(timers) do\n" +
                "${indent}\t\tif timer.active then return true end\n" +
                "${indent}\tend\n" +
                "${indent}\treturn false\n" +
                "${indent}end\n\n" +
                "${indent}function Cron.Update(delta)",
                "DriveBus Cron active-timer probe");
        }

        var init = context.FindFile(
            "init.lua",
            "Cron = require",
            "Cron.Update(delta)",
            "onUpdate");

        var updateRegex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\)\s*\r?\n\k<indent>[ \t]+Cron\.Update\s*\(\s*delta\s*\)\s*\r?\n\k<indent>end\s*\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        var updateMatches = updateRegex.Matches(init.Text);
        if (updateMatches.Count != 1)
            throw new InvalidOperationException(
                "DriveBus Cron onUpdate source no longer uniquely matches the dormant-timer gate.");

        var update = updateMatches[0];
        var updateIndent = update.Groups["indent"].Value;
        var registrar = update.Groups["registrar"].Value;
        var initText = init.Text[..update.Index] +
            updateIndent + registrar + "(\"onUpdate\", function(delta)\n" +
            updateIndent + "    if Cron.HasActiveTimers() then Cron.Update(delta) end\n" +
            updateIndent + "end)" +
            init.Text[(update.Index + update.Length)..];

        context.Write(core, coreText);
        context.Write(cron, cronText);
        context.Write(init, initText);
        return SemanticInjectionResult.Success(
            "Routed DriveBus's six source-proven actions through 0-Engine and made its Cron frame callback hard-dormant whenever no active timer exists.");
    }


    // A pristine Nexus CallList source must receive this complete stage in one pass.
    // Do not turn its localized "moveup" detection into a finite-name router:
    // the original author accepts a localized substring, not a known action ID.
    // Pristine Nexus source -> FINAL lazy input filter in one generated pass.
    // The older experimental display-only stage is recognized solely for safe
    // upgrades of already-patched live copies; it is never a prerequisite.
}
