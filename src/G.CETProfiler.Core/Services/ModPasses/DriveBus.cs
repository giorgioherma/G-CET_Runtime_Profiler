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
            var events = File.ReadAllText(Path.Combine(ownerFolder, "Modules", "event.lua"));
            return IsDriveBusActionRouted(core) &&
                HasDriveBusActionWakeGate(core) &&
                HasDriveBusExistingDiscoveryWake(events) &&
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
            !Regex.IsMatch(text, @"\bconsumer\s*:\s*Consume\s*\(",
                RegexOptions.CultureInvariant))
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

    // Stage C: only the mod's optional 0-Engine subscriber sleeps.
    // The author's already-running 100 ms bus-discovery timer is the
    // independent wake path; no additional native observer is installed.
    // A missing SetActive handle leaves the original route/fallback alive.
    private static bool HasDriveBusActionWakeGate(string core) =>
        core.Contains("self.__gcetSyncActionDormancy = function()", StringComparison.Ordinal) &&
        core.Contains("local __gcetDriveBusActionHandle = nil", StringComparison.Ordinal) &&
        core.Contains("status == Def.VehicleStatus.PlayerIn", StringComparison.Ordinal) &&
        core.Contains("status == Def.VehicleStatus.Mounted", StringComparison.Ordinal) &&
        core.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) &&
        Regex.Matches(core,
            @"__gcetDriveBusActionHandle\s*=\s*(?:zeroEngine(?:Mod)?|__gcet(?:Client|Engine)_gcetDriveBusOnAction)\.SubscribeAction\s*\(",
            RegexOptions.CultureInvariant).Count == 2;

    private static bool HasDriveBusExistingDiscoveryWake(string events) =>
        events.Contains("__gcetDriveBusDiscoveryWakeV2", StringComparison.Ordinal) &&
        Regex.IsMatch(events,
            @"self:CheckAllEvents\s*\(\s*\)\s*\r?\n\s*-- __gcetDriveBusDiscoveryWakeV2\s*\r?\n\s*if\s+DAB\.core_obj\s+and\s+type\s*\(\s*DAB\.core_obj\.__gcetSyncActionDormancy\s*\)\s*==\s*['""]function['""]",
            RegexOptions.CultureInvariant);

    private static string AddDriveBusActionWakeGate(string core)
    {
        if (HasDriveBusActionWakeGate(core)) return core;
        if (core.Contains("__gcetSyncActionDormancy", StringComparison.Ordinal) ||
            core.Contains("__gcetDriveBusActionHandle", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "DriveBus action subscriber dormancy stage is partial/drifted.");

        // The existing original (zeroEngineOnAction) and generated
        // (gcetDriveBusOnAction) source shapes each have exactly two
        // SubscribeAction choices plus their existing native Observe fallback.
        var direct = new Regex(
            @"(?<call>(?:zeroEngineMod|zeroEngine)\.SubscribeAction\([^\r\n]*\))",
            RegexOptions.CultureInvariant);
        var routed = new Regex(
            @"(?<call>__gcet(?:Client|Engine)_gcetDriveBusOnAction\.SubscribeAction\([^\r\n]*\))",
            RegexOptions.CultureInvariant);
        var selected = direct.Matches(core).Count == 2 ? direct : routed;
        if (selected.Matches(core).Count != 2 ||
            (selected == direct && routed.IsMatch(core)) ||
            (selected == routed && direct.IsMatch(core)))
            throw new InvalidOperationException(
                "DriveBus existing shared OnAction subscriber calls changed.");

        core = selected.Replace(core,
            m => "__gcetDriveBusActionHandle = " + m.Groups["call"].Value);

        var open = new Regex(
            @"(?m)^function\s+Core:SetObserve\s*\(\s*\)\s*$",
            RegexOptions.CultureInvariant);
        if (open.Matches(core).Count != 1)
            throw new InvalidOperationException("DriveBus SetObserve is ambiguous.");
        const string gate =
            "\n    -- __gcetDriveBusActionDormancyV2: cached author bus state, no native getters\n" +
            "    local __gcetDriveBusActionHandle = nil\n" +
            "    local __gcetDriveBusLastActionState = nil\n" +
            "    self.__gcetSyncActionDormancy = function()\n" +
            "        local handle = __gcetDriveBusActionHandle\n" +
            "        if type(handle) ~= \"table\" or type(handle.SetActive) ~= \"function\" then return end\n" +
            "        local event = self.available_event_obj\n" +
            "        local status = event and event:GetStatus()\n" +
            "        local needed = status == Def.VehicleStatus.PlayerIn or status == Def.VehicleStatus.Mounted\n" +
            "        if needed == __gcetDriveBusLastActionState then return end\n" +
            "        if pcall(handle.SetActive, needed) then __gcetDriveBusLastActionState = needed end\n" +
            "    end\n";
        core = open.Replace(core, m => m.Value + gate, 1);
        if (!HasDriveBusActionWakeGate(core))
            throw new InvalidOperationException("DriveBus subscriber stage postconditions failed.");
        return core;
    }

    private static string AddDriveBusDiscoveryWake(string events)
    {
        if (HasDriveBusExistingDiscoveryWake(events)) return events;
        if (events.Contains("__gcetDriveBusDiscoveryWakeV2", StringComparison.Ordinal))
            throw new InvalidOperationException("DriveBus discovery wake stage drifted.");
        // The original timer discovers NoExistance -> Summoned -> PlayerIn
        // -> Mounted without depending on OnAction. Piggyback on it rather
        // than adding another always-on native listener.
        if (!events.Contains("Cron.Every(0.1", StringComparison.Ordinal))
            throw new InvalidOperationException("DriveBus 100 ms discovery timer missing.");
        var check = new Regex(
            @"(?m)^(?<indent>[ \t]*)self:CheckAllEvents\s*\(\s*\)[ \t]*$",
            RegexOptions.CultureInvariant);
        if (check.Matches(events).Count != 1)
            throw new InvalidOperationException("DriveBus discovery wake is ambiguous.");
        events = check.Replace(events, m =>
            m.Value + "\n" + m.Groups["indent"].Value +
            "-- __gcetDriveBusDiscoveryWakeV2: cached state, independent of OnAction\n" +
            m.Groups["indent"].Value +
            "if DAB.core_obj and type(DAB.core_obj.__gcetSyncActionDormancy) == \"function\" then\n" +
            m.Groups["indent"].Value + "    DAB.core_obj.__gcetSyncActionDormancy()\n" +
            m.Groups["indent"].Value + "end", 1);
        if (!HasDriveBusExistingDiscoveryWake(events))
            throw new InvalidOperationException("DriveBus discovery-stage postconditions failed.");
        return events;
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

        // Preserve an independently verified earlier G-CET/native route.
        // Re-route only the pristine author observer, never an unrecognized
        // subscriber/fallback or a mismatched action-selector table.
        var routed = IsDriveBusActionRouted(core.Text);
        if (!routed &&
            Regex.IsMatch(core.Text, @"\.\s*SubscribeAction\s*\(",
                RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "DriveBus already has an unknown action route; refusing to replace it.");

        var coreText = routed ? core.Text : RouteExactOnActionObserver(
            core.Text,
            "gcetDriveBusOnAction",
            "DriveBus",
            new[]
            {
                "QuickExit", "NextWeapon", "PreviousWeapon",
                "ChoiceApply", "ChoiceScrollUp", "ChoiceScrollDown"
            },
            "DriveBus PlayerPuppet OnAction",
            "exception_in_choice_list",
            "exception_in_mount_list",
            "self:ChoiceAction(action_name");
        if (!IsDriveBusActionRouted(coreText))
            throw new InvalidOperationException(
                "DriveBus exact action route and original fallback were not proven.");

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

        if (!HasDriveBusActiveTimers(cronText))
            throw new InvalidOperationException(
                "DriveBus Cron active-timer function is incomplete or changed.");

        var init = context.FindFile(
            "init.lua",
            "Cron = require",
            "Cron.Update(delta)",
            "onUpdate");
        var initText = init.Text;
        if (!HasDriveBusTimerGate(initText))
        {
            var updateRegex = new Regex(
                @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\)\s*\r?\n\k<indent>[ \t]+Cron\.Update\s*\(\s*delta\s*\)\s*\r?\n\k<indent>end\s*\)\s*$",
                RegexOptions.CultureInvariant | RegexOptions.Multiline);

            var updateMatches = updateRegex.Matches(initText);
            if (updateMatches.Count != 1)
                throw new InvalidOperationException(
                    "DriveBus Cron onUpdate no longer uniquely matches the dormant-timer gate.");

            var update = updateMatches[0];
            var updateIndent = update.Groups["indent"].Value;
            var registrar = update.Groups["registrar"].Value;
            initText = initText[..update.Index] +
                updateIndent + registrar + "(\"onUpdate\", function(delta)\n" +
                updateIndent + "    if Cron.HasActiveTimers() then Cron.Update(delta) end\n" +
                updateIndent + "end)" +
                initText[(update.Index + update.Length)..];
        }
        if (!HasDriveBusTimerGate(initText))
            throw new InvalidOperationException(
                "DriveBus Cron active-timer guard failed source verification.");

        // Route, dormancy and timer stages compose against pristine,
        // historical-routed and partially optimized user source.
        var events = context.FindFile(
            "Modules/event.lua",
            "Cron.Every(0.1", "self:CheckAllEvents()");
        var eventText = AddDriveBusDiscoveryWake(events.Text);
        coreText = AddDriveBusActionWakeGate(coreText);
        context.Write(core, coreText);
        context.Write(events, eventText);
        context.Write(cron, cronText);
        context.Write(init, initText);
        return SemanticInjectionResult.Success(
            "Extended existing DriveBus route with state-driven subscriber pause/resume from its original 100 ms discovery wake; preserved native Observe fallback, Input/Key/Axis and all Cron timers.");
    }


    // A pristine Nexus CallList source must receive this complete stage in one pass.
    // Do not turn its localized "moveup" detection into a finite-name router:
    // the original author accepts a localized substring, not a known action ID.
    // Pristine Nexus source -> FINAL lazy input filter in one generated pass.
    // The older experimental display-only stage is recognized solely for safe
    // upgrades of already-patched live copies; it is never a prerequisite.
}
