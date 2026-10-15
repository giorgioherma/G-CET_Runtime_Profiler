using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    // These are source-stage postconditions, not legacy version markers.
    // The original fallback must remain when the shared native route exists.
    internal static bool IsDriveBusComplete(string ownerFolder)
    {
        try
        {
            var core = File.ReadAllText(Path.Combine(ownerFolder, "Modules", "core.lua"));
            var cron = File.ReadAllText(Path.Combine(ownerFolder, "External", "Cron.lua"));
            var init = File.ReadAllText(Path.Combine(ownerFolder, "init.lua"));
            return IsDriveBusActionRouted(core) &&
                HasDriveBusActiveTimers(cron) && HasDriveBusTimerGate(init);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool IsDriveBusActionRouted(string text)
    {
        if (!text.Contains("exception_in_choice_list", StringComparison.Ordinal) ||
            !text.Contains("exception_in_mount_list", StringComparison.Ordinal) ||
            !text.Contains("self:ChoiceAction(action_name", StringComparison.Ordinal) ||
            !text.Contains("consumer:Consume()", StringComparison.Ordinal))
            return false;

        // Both known historical routes carry exactly six authored actions.
        // Require those names in ONE finite configuration, not scattered
        // throughout the owner's source as unrelated strings.
        var config = Regex.Match(text,
            @"\blocal\s+(?:zeroEngineConfig|__gcetActions_[A-Za-z_0-9]+)\s*=\s*(?<table>\{(?:[^{}]|\{[^{}]*\})*\})",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        if (!config.Success || config.NextMatch().Success)
            return false;
        var configured = Regex.Matches(config.Groups["table"].Value,
                @"['""](?<name>[^'""]+)['""]", RegexOptions.CultureInvariant)
            .Select(match => match.Groups["name"].Value).ToArray();
        var expected = new[] { "QuickExit", "NextWeapon", "PreviousWeapon",
            "ChoiceApply", "ChoiceScrollUp", "ChoiceScrollDown" };
        if (configured.Length != expected.Length ||
            configured.Distinct(StringComparer.Ordinal).Count() != expected.Length ||
            expected.Except(configured, StringComparer.Ordinal).Any())
            return false;

        foreach (var handler in new[] { "zeroEngineOnAction", "gcetDriveBusOnAction" })
        {
            var escaped = Regex.Escape(handler);
            if (Regex.IsMatch(text,
                    @"\blocal\s+function\s+" + escaped +
                    @"\s*\([^)]*\baction\b[^)]*\bconsumer\b[^)]*\)",
                    RegexOptions.CultureInvariant) &&
                Regex.IsMatch(text,
                    @"\.\s*SubscribeAction\s*\([\s\S]*?\b" + escaped + @"\b",
                    RegexOptions.CultureInvariant) &&
                Regex.IsMatch(text,
                    @"\bObserve\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*" +
                    escaped + @"\s*\)",
                    RegexOptions.CultureInvariant))
                return true;
        }
        return false;
    }

    private static bool HasDriveBusActiveTimers(string cron) =>
        Regex.IsMatch(cron,
            @"\bfunction\s+Cron\.HasActiveTimers\s*\(\s*\)\s*[\s\S]*?" +
            @"\bfor\s+_,\s*timer\s+in\s+ipairs\s*\(\s*timers\s*\)\s*do\s*" +
            @"if\s+timer\.active\s+then\s+return\s+true\s+end[\s\S]*?" +
            @"\breturn\s+false\s+end\b",
            RegexOptions.CultureInvariant);

    private static bool HasDriveBusTimerGate(string init) =>
        Regex.IsMatch(init,
            @"\bif\s+Cron\.HasActiveTimers\s*\(\s*\)\s+then\s+" +
            @"Cron\.Update\s*\(\s*delta\s*\)\s+end",
            RegexOptions.CultureInvariant);

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
