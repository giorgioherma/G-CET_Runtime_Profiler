using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySitAnywhere(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Cron.Update(dt)",
            "interaction.update()",
            "world.update()",
            "self.logic:onUpdate()");

        var cron = context.FindFile(
            "modules/external/Cron.lua",
            "local timers",
            "function Cron.Update",
            "function Cron.Halt");

        var logic = context.FindFile(
            "modules/logic.lua",
            "function logic:new",
            "function logic:hideAllWorkspots",
            "function logic:onUpdate");

        var world = context.FindFile(
            "modules/worldInteraction.lua",
            "world.interactions",
            "function world.update",
            "function world.onSessionStart");

        var cronText = cron.Text;
        if (!Regex.IsMatch(
                cronText,
                @"(?m)^\s*function\s+Cron\.HasActiveTimers\s*\(",
                RegexOptions.CultureInvariant))
        {
            cronText = RegexReplaceOnce(
                cronText,
                @"(?m)^(?<indent>[ \t]*)function\s+Cron\.Halt\s*\(",
                "${indent}function Cron.HasActiveTimers()\n" +
                "${indent}\tfor _, timer in ipairs(timers) do\n" +
                "${indent}\t\tif timer.active then return true end\n" +
                "${indent}\tend\n" +
                "${indent}\treturn false\n" +
                "${indent}end\n\n" +
                "${indent}function Cron.Halt(",
                "sitAnywhere Cron active-timer probe");
        }

        var worldText = world.Text;
        if (!Regex.IsMatch(
                worldText,
                @"(?m)^\s*function\s+world\.hasVisibleState\s*\(",
                RegexOptions.CultureInvariant))
        {
            worldText = RegexReplaceOnce(
                worldText,
                @"(?m)^(?<indent>[ \t]*)function\s+world\.onSessionStart\s*\(",
                "${indent}function world.hasVisibleState()\n" +
                "${indent}    for _, interaction in pairs(world.interactions) do\n" +
                "${indent}        if interaction.shown or interaction.pinID ~= nil then return true end\n" +
                "${indent}    end\n" +
                "${indent}    return false\n" +
                "${indent}end\n\n" +
                "${indent}function world.onSessionStart(",
                "sitAnywhere visible-world sentinel");
        }

        var logicText = logic.Text;
        if (!Regex.IsMatch(
                logicText,
                @"\bworkspotsHidden\s*=",
                RegexOptions.CultureInvariant))
        {
            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)o\.sittables\s*=\s*\{\s*\}\s*$",
                "${indent}o.sittables = {}\n${indent}o.workspotsHidden = true",
                "sitAnywhere parked-workspot state");

            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)function\s+logic:hideAllWorkspots\s*\(\s*\)\s*$",
                "${indent}function logic:hideAllWorkspots()\n" +
                "${indent}    if self.workspotsHidden then return end",
                "sitAnywhere hideAllWorkspots idle guard");

            var hideBlock = new Regex(
                @"(?ms)^(?<indent>[ \t]*)function\s+logic:hideAllWorkspots\s*\(\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*$",
                RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
            var hideMatches = hideBlock.Matches(logicText);
            if (hideMatches.Count != 1)
                throw new InvalidOperationException(
                    "sitAnywhere hideAllWorkspots source no longer uniquely proves the parked-state boundary.");
            var hide = hideMatches[0];
            var hideBody = hide.Groups["body"].Value;
            if (!hideBody.Contains("self.workspotsHidden = true", StringComparison.Ordinal))
            {
                var replacement =
                    hide.Groups["indent"].Value + "function logic:hideAllWorkspots()\n" +
                    hideBody +
                    hide.Groups["indent"].Value + "    self.workspotsHidden = true\n" +
                    hide.Groups["indent"].Value + "end";
                logicText = logicText[..hide.Index] +
                    replacement +
                    logicText[(hide.Index + hide.Length)..];
            }

            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)if\s+position\s+then\s*$",
                "${indent}if position then\n${indent}    self.workspotsHidden = false",
                "sitAnywhere workspot wake state");
        }

        var eventRegex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*dt\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
        var candidates = eventRegex.Matches(init.Text)
            .Cast<Match>()
            .Where(match =>
                match.Value.Contains("Cron.Update(dt)", StringComparison.Ordinal) &&
                match.Value.Contains("interaction.update()", StringComparison.Ordinal) &&
                match.Value.Contains("world.update()", StringComparison.Ordinal) &&
                match.Value.Contains("self.logic:onUpdate()", StringComparison.Ordinal))
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                "sitAnywhere onUpdate source no longer uniquely proves the active/dormant work boundary.");

        var selected = candidates[0];
        var indent = selected.Groups["indent"].Value;
        var registrar = selected.Groups["registrar"].Value;
        var i1 = indent + "    ";
        var i2 = indent + "        ";
        var i3 = indent + "            ";

        var replacementUpdate =
            indent + registrar + "(\"onUpdate\", function(dt)\n" +
            i1 + "if self.runtimeData.inMenu or not self.runtimeData.inGame then return end\n\n" +
            i1 + "local hasTimers = Cron.HasActiveTimers()\n" +
            i1 + "local scanning = self.logic.isScanning == true or self.runtimeData.forceScan == true\n" +
            i1 + "local inWorkspot = self.logic:inWorkspot() == true\n" +
            i1 + "local inTransition = self.logic:inTransition() == true\n" +
            i1 + "local workspotRealtime = false\n" +
            i1 + "for _, spot in pairs(self.logic.sittables) do\n" +
            i2 + "local workspot = spot.workspot\n" +
            i2 + "if workspot.enableCamera or workspot.camTransition or workspot.slide then\n" +
            i3 + "workspotRealtime = true\n" +
            i3 + "break\n" +
            i2 + "end\n" +
            i1 + "end\n\n" +
            i1 + "if not scanning\n" +
            i2 + "and not inWorkspot\n" +
            i2 + "and not inTransition\n" +
            i2 + "and not workspotRealtime\n" +
            i2 + "and not hasTimers\n" +
            i2 + "and not interaction.hubShown\n" +
            i2 + "and self.logic.workspotsHidden\n" +
            i2 + "and not world.hasVisibleState() then\n" +
            i2 + "return\n" +
            i1 + "end\n\n" +
            i1 + "if hasTimers then Cron.Update(dt) end\n" +
            i1 + "if interaction.hubShown then interaction.update() end\n" +
            i1 + "world.update()\n" +
            i1 + "self.logic:onUpdate()\n" +
            i1 + "for _, spot in pairs(self.logic.sittables) do\n" +
            i2 + "local workspot = spot.workspot\n" +
            i2 + "if workspot.enableCamera or workspot.camTransition or workspot.slide then\n" +
            i3 + "workspot.yaw = self.yaw\n" +
            i3 + "workspot.pitch = self.pitch\n" +
            i3 + "spot:update(dt)\n" +
            i2 + "end\n" +
            i1 + "end\n" +
            indent + "end)";

        var initText = init.Text[..selected.Index] +
            replacementUpdate +
            init.Text[(selected.Index + selected.Length)..];

        context.Write(init, initText);
        context.Write(cron, cronText);
        context.Write(logic, logicText);
        context.Write(world, worldText);
        return SemanticInjectionResult.Success(
            "Made Sit Anywhere hard-dormant when scanner/workspot/transition/timer/hub/mappin state is fully idle, while preserving rendered-frame cadence for live scanner, world, timer and workspot work.");
    }


    private static SemanticInjectionResult ApplySitAnywhereUnshownMappinGate(SemanticPatchContext context)
    {
        var file = context.FindFile(
            "modules/worldInteraction.lua",
            "function world.init()",
            "ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\"",
            "local mappin = this:GetMappin()",
            "world.interactions");
        const string marker = "G-CET semantic:sitanywhere-hidden-mappin-getter";
        if (file.Text.Contains(marker, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("SitAnywhere hidden-mappin getter stage already present.");
        var source = file.Text;
        var proof = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+mappin\s*=\s*this:GetMappin\(\)\s*\r?$",
            RegexOptions.CultureInvariant);
        var matches = proof.Matches(source);
        if (matches.Count != 1 ||
            source.IndexOf("function world.init()", StringComparison.Ordinal) >= matches[0].Index ||
            source.IndexOf("function world.update()", StringComparison.Ordinal) <= matches[0].Index ||
            !source.Contains("if interaction.shown then", StringComparison.Ordinal) &&
            !source.Contains("and interaction.shown then", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SitAnywhere world-mappin observer/visible-state ownership changed.");
        var selected = matches[0];
        var i = selected.Groups["indent"].Value;
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var gate =
            i + "-- " + marker + nl +
            i + "-- Never request a native mappin unless our visible interactions need its icon." + nl +
            i + "local __gcetAnyShown = false" + nl +
            i + "for _, interaction in pairs(world.interactions) do" + nl +
            i + "    if interaction.shown then __gcetAnyShown = true; break end" + nl +
            i + "end" + nl +
            i + "if not __gcetAnyShown then return end" + nl;
        context.Write(file, source[..selected.Index] + gate + selected.Value +
            source[(selected.Index + selected.Length)..]);
        return SemanticInjectionResult.Success(
            "Preserved the native mappin observer and shown-interaction icon wake; skipped GetMappin/GetWorldPosition when no custom SitAnywhere interaction is shown.");
    }
}
