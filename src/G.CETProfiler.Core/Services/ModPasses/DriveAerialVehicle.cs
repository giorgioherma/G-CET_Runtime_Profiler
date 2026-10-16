using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyDriveAerialVehicle(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "DAV.core_obj.av_obj.engine_obj:Update(delta)",
            "Def.Situation.Idle",
            "Cron.Update(delta)");

        var initText = init.Text.Contains("local __gcetSituation = __gcetEvent", StringComparison.Ordinal)
            ? init.Text : RegexReplaceOnce(
            init.Text,
            @"(?m)^(?<indent>\s*)DAV\.core_obj\.av_obj\.engine_obj:Update\(delta\)\s*$",
            "${indent}local __gcetEvent = DAV.core_obj.event_obj\n" +
            "${indent}local __gcetSituation = __gcetEvent and __gcetEvent.current_situation or Def.Situation.Idle\n" +
            "${indent}if __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal then\n" +
            "${indent}\tDAV.core_obj.av_obj.engine_obj:Update(delta)\n" +
            "${indent}end",
            "DriveAerialVehicle realtime physics state gate");
        context.Write(init, initText);

        var core = context.FindFile(
            "Modules/core.lua",
            "Cron.Every(DAV.time_resolution",
            "self.event_obj:CheckAllEvents()",
            "self:GetActions()",
            "Def.Situation.Normal");

        var pattern =
            @"Cron\.Every\(DAV\.time_resolution\s*,\s*function\s*\(\s*\)\s*" +
            @"self\.event_obj:CheckAllEvents\(\)\s*" +
            @"self:GetActions\(\)\s*" +
            @"end\s*\)";

        var replacement =
            "local __gcetIdleEventTick = 0\n" +
            "    Cron.Every(DAV.time_resolution, function()\n" +
            "        local __gcetSituation = self.event_obj.current_situation\n" +
            "        local __gcetActiveVehicleState = __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal\n" +
            "        if __gcetActiveVehicleState then\n" +
            "            __gcetIdleEventTick = 0\n" +
            "            self.event_obj:CheckAllEvents()\n" +
            "        elseif __gcetSituation == Def.Situation.Normal then\n" +
            "            __gcetIdleEventTick = __gcetIdleEventTick + 1\n" +
            "            if __gcetIdleEventTick >= 8 then\n" +
            "                __gcetIdleEventTick = 0\n" +
            "                self.event_obj:CheckAllEvents()\n" +
            "            end\n" +
            "        end\n" +
            "        if __gcetActiveVehicleState or not self.queue_obj:IsEmpty() then\n" +
            "            self:GetActions()\n" +
            "        end\n" +
            "    end)";

        // The real installed mod uses CRLF in both core.lua and event.lua.
        // Work on normalized in-memory source so exact callback closures and
        // direct SessionStart/SessionEnd writers match regardless of EOL;
        // restore the owner's original EOL convention before staging files.
        var coreCrLf = core.Text.Contains("\r\n", StringComparison.Ordinal);
        var normalizedCore = coreCrLf
            ? core.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
            : core.Text;
        var coreText = normalizedCore.Contains("local __gcetIdleEventTick", StringComparison.Ordinal)
            ? normalizedCore : RegexReplaceOnce(
            normalizedCore,
            pattern,
            replacement,
            "DriveAerialVehicle active/normal event cadence");

        // Experimental C-stage v2: routed wildcard action source instead of a
        // second CET native Observe, but only where source proves both the
        // WAITING pre-mount state and InVehicle require input.
        var events = context.FindFile(
            "Modules/event.lua",
            "function Event:SetSituation(situation)",
            "GameUI.Observe(\"SessionStart\"",
            "GameUI.Observe(\"SessionEnd\"",
            "self.current_situation = Def.Situation.Waiting",
            "self.current_situation = Def.Situation.InVehicle");
        var eventCrLf = events.Text.Contains("\r\n", StringComparison.Ordinal);
        var eventText = eventCrLf
            ? events.Text.Replace("\r\n", "\n", StringComparison.Ordinal)
            : events.Text;
        if (!coreText.Contains("__gcetDAVActionGateV2", StringComparison.Ordinal))
        {
            if (!coreText.Contains("function Core:SetInputListener()", StringComparison.Ordinal) ||
                !coreText.Contains("self:StorePlayerAction(action_name, action_type, action_value)", StringComparison.Ordinal))
                throw new InvalidOperationException("DAV action handler no longer source-proven.");

            const string sync = """
-- __gcetDAVActionGateV2: the only active wildcard action states are
-- Waiting (pre-boarding) and InVehicle. Everything else uses the shared hook.
function Core:SyncActionListener()
    local h = self.__gcetDAVActionHandle
    if not h or type(h.SetActive) ~= "function" then return end
    local situation = self.event_obj and self.event_obj.current_situation
    local active = situation == Def.Situation.Waiting or situation == Def.Situation.InVehicle
    pcall(h.SetActive, active)
end

""";
            coreText = ReplaceOnce(coreText, "function Core:SetInputListener()", sync +
                "function Core:SetInputListener()", "DAV action handle lifecycle");
            coreText = ReplaceOnce(coreText,
                "Observe(\"PlayerPuppet\", \"OnAction\", function(this, action, consumer)",
                "local function __gcetDAVOnAction(this, action, consumer, routedName, routedType)",
                "DAV existing normal-phase listener");
            coreText = ReplaceOnce(coreText,
                "local action_name = action:GetName(action).value",
                "local action_name = routedName or action:GetName(action).value",
                "DAV consume action identity once in shared route");
            coreText = ReplaceOnce(coreText,
                "local action_type = action:GetType(action).value",
                "local action_type = routedType or action:GetType(action).value",
                "DAV consume action type once in shared route");
            coreText = ReplaceOnce(coreText,
                "    end)\n\n    -- Disable Iconic Cyberware",
                """
    end

    -- Preserve original wildcard forwarding and mutable author JSON exceptions.
    -- The shared 0-Engine before observer already exists; do not create a
    -- second native listener if it accepts our exact callback.
    local __gcetDAVRouted = false
    if type(GetMod) == "function" then
        local found, engine = pcall(GetMod, "0-Engine")
        if found and type(engine) == "table" and type(engine.SubscribeAction) == "function" then
            local subscribed, handle = pcall(engine.SubscribeAction,
                { actions = "*", phase = "before", decodeType = true,
                  active = true, id = "dav-active-aerial-actions" },
                __gcetDAVOnAction, "DriveAerialVehicle")
            if subscribed and type(handle) == "table"
                and type(handle.SetActive) == "function"
                and type(handle.IsActive) == "function" then
                local checked, ready = pcall(handle.IsActive)
                if checked and ready == true then
                    self.__gcetDAVActionHandle = handle
                    self:SyncActionListener()
                    __gcetDAVRouted = true
                end
            end
        end
    end
    if not __gcetDAVRouted then
        Observe("PlayerPuppet", "OnAction", __gcetDAVOnAction)
    end

    -- Disable Iconic Cyberware
""",
                "DAV normal CET Observe fallback");
            // All seven successful author SetSituation writes are a wake or
            // sleep transition; preserve existing input ordering and returns.
            const string token = "function Event:SetSituation(situation)";
            var start = eventText.IndexOf(token, StringComparison.Ordinal);
            if (start < 0) throw new InvalidOperationException("DAV SetSituation missing.");
            var stop = eventText.IndexOf("\nend", start, StringComparison.Ordinal);
            if (stop < 0) throw new InvalidOperationException("DAV SetSituation not bounded.");
            var section = eventText[start..stop];
            var writer = new Regex(
                @"(?m)^(?<indent>[ \t]*)self\.current_situation\s*=\s*(?<state>Def\.Situation\.\w+)[ \t]*\r?\n(?<return>[ \t]*return true)",
                RegexOptions.CultureInvariant);
            if (writer.Matches(section).Count != 7)
                throw new InvalidOperationException(
                    "DAV situation setter changed; expected seven successful wake writers.");
            var patched = writer.Replace(section, match =>
                match.Groups["indent"].Value + "self.current_situation = " +
                match.Groups["state"].Value + "\n" +
                match.Groups["indent"].Value +
                "if DAV.core_obj and DAV.core_obj.SyncActionListener then DAV.core_obj:SyncActionListener() end\n" +
                match.Groups["return"].Value);
            eventText = eventText[..start] + patched + eventText[stop..];
            eventText = ReplaceOnce(eventText,
                "        self.current_situation = Def.Situation.Normal\n\n    end)",
                "        self.current_situation = Def.Situation.Normal\n" +
                "        if DAV.core_obj and DAV.core_obj.SyncActionListener then DAV.core_obj:SyncActionListener() end\n\n    end)",
                "DAV SessionStart direct state reset");
            eventText = ReplaceOnce(eventText,
                "        self.current_situation = Def.Situation.Idle\n    end)",
                "        self.current_situation = Def.Situation.Idle\n" +
                "        if DAV.core_obj and DAV.core_obj.SyncActionListener then DAV.core_obj:SyncActionListener() end\n    end)",
                "DAV SessionEnd direct state reset");
        }
        else if (!eventText.Contains("DAV.core_obj:SyncActionListener()", StringComparison.Ordinal) ||
                 !coreText.Contains("phase = \"before\"", StringComparison.Ordinal) ||
                 !coreText.Contains("Observe(\"PlayerPuppet\", \"OnAction\", __gcetDAVOnAction)", StringComparison.Ordinal))
            throw new InvalidOperationException("DAV partial action gate; fail closed.");

        context.Write(core, coreCrLf
            ? coreText.Replace("\n", "\r\n", StringComparison.Ordinal) : coreText);
        context.Write(events, eventCrLf
            ? eventText.Replace("\n", "\r\n", StringComparison.Ordinal) : eventText);
        return SemanticInjectionResult.Success(
            "Existing flight physics/cadence preserved. New native action C route uses the existing 0-Engine before hook with wildcard delivery and direct CET fallback. " +
            "Source-proven Waiting/InVehicle transitions synchronously wake or suspend the subscriber; full in-game action consumption ordering still needs testing.");

    }

}
