using System;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Mechanical deployment-only extraction of three reviewed small cadence
/// gates. No callback registrations, author timers or gameplay bodies move.
/// The exact pre-extraction source is required; unknown revisions fail closed.
/// </summary>
internal static class CadenceCoordinatorSource
{
    internal static void Apply(SemanticCandidate candidate, SemanticPatchContext context)
    {
        switch (candidate.RuleId)
        {
            case "gta-joyride": JoyRide(context); break;
            case "blackjack": Blackjack(context); break;
        }
    }

    private static string Once(string text, string before, string after, string label)
    {
        var index = text.IndexOf(before, StringComparison.Ordinal);
        if (index < 0 ||
            text.IndexOf(before, index + before.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Coordinator extraction is not uniquely proven: " + label);
        return text[..index] + after + text[(index + before.Length)..];
    }

    private static string MakeGate(string exactBody, string terminalReturn,
        string replacement, string appendedReturn)
    {
        var moved = Once(exactBody, terminalReturn, replacement, "original early-return");
        return moved + "\n" + appendedReturn + "\n";
    }

    private static void JoyRide(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua",
            "__gcetJoyRideElapsed", "__gcetJoyRideWasBusy", "Cron.Update(deltaTime)",
            "pendingSpawns", "SESSION.active");
        const string state = "local __gcetJoyRideElapsed = 0.0\n" +
            "local __gcetJoyRideWasBusy = false\n\n";
        const string gate = """
                __gcetJoyRideElapsed = __gcetJoyRideElapsed + deltaTime
                local __gcetJoyRideBusy = SESSION.active == true
                    or (SESSION.state ~= nil and SESSION.state ~= "idle")
                    or #pendingSpawns > 0
                    or SESSION.npc ~= nil
                    or SESSION.vehicle ~= nil
                    or SESSION.camEntityID ~= nil
                    or SESSION.camEntity ~= nil
                    or SESSION.mountTimeoutTask ~= nil
                    or (SESSION.gracePeriod or 0) > 0
                local __gcetJoyRideWake = __gcetJoyRideBusy and not __gcetJoyRideWasBusy
                __gcetJoyRideWasBusy = __gcetJoyRideBusy
                local __gcetJoyRideInterval = __gcetJoyRideBusy and 0.10 or 5.0
                if not __gcetJoyRideWake and __gcetJoyRideElapsed < __gcetJoyRideInterval then return end
                deltaTime = __gcetJoyRideElapsed
                __gcetJoyRideElapsed = 0.0
            """;
        const string bridge = """
            -- G-CET semantic coordinator bridge:gta-joyride
            local __gcetJoyRideGate=nil
            do
             local ok,make=pcall(dofile,"G-CET/Coordinator.lua")
             if ok and type(make)=="function" then
              local bound,gate=pcall(make)
              if bound and type(gate)=="function" then __gcetJoyRideGate=gate end
             end
            end

            """;
        const string call = """
                if __gcetJoyRideGate then
                    local proceed, elapsed = __gcetJoyRideGate(deltaTime, SESSION, pendingSpawns)
                    if not proceed then return end
                    deltaTime = elapsed
                end
            """;
        var text = Once(file.Text, state, bridge, "JoyRide cadence state");
        text = Once(text, gate + "\n", call + "\n", "JoyRide source-owned cadence");
        var moved = MakeGate(gate, "then return end",
            "then return false end", "    return true, deltaTime");
        var module =
            "-- G-CET semantic coordinator:gta-joyride\n" +
            "return function()\n" +
            " local __gcetJoyRideElapsed=0.0\n" +
            " local __gcetJoyRideWasBusy=false\n" +
            " return function(deltaTime,SESSION,pendingSpawns)\n" +
            moved + " end\nend\n";
        context.Write(file, text);
        context.WriteOwnedModule("G-CET/Coordinator.lua", module);
    }

    private static void Blackjack(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua",
            "__gcetBlackjackIdleElapsed", "SpotManager.IsPlayerInSpot",
            "BlackjackMainMenu.Update");
        const string state = "local __gcetBlackjackIdleElapsed = 0.0\n\n";
        const string gate = """
                local __gcetFrameDt = math.max(tonumber(dt) or 0.0, 0.0)
                if not SpotManager.IsPlayerInSpot() then
                    __gcetBlackjackIdleElapsed = __gcetBlackjackIdleElapsed + __gcetFrameDt
                    if __gcetBlackjackIdleElapsed < 0.05 then return end
                    dt = __gcetBlackjackIdleElapsed
                    __gcetBlackjackIdleElapsed = 0.0
                else
                    __gcetBlackjackIdleElapsed = 0.0
                end
            """;
        const string bridge = """
            -- G-CET semantic coordinator bridge:blackjack
            local __gcetBlackjackGate=nil
            do
             local ok,make=pcall(dofile,"G-CET/Coordinator.lua")
             if ok and type(make)=="function" then
              local bound,gate=pcall(make)
              if bound and type(gate)=="function" then __gcetBlackjackGate=gate end
             end
            end

            """;
        const string call = """
                if __gcetBlackjackGate then
                    local proceed, elapsed = __gcetBlackjackGate(dt, SpotManager)
                    if not proceed then return end
                    dt = elapsed
                end
            """;
        var text = Once(file.Text, state, bridge, "Blackjack cadence state");
        text = Once(text, gate + "\n", call + "\n", "Blackjack table session gate");
        var moved = MakeGate(gate, "then return end",
            "then return false end", "    return true, dt");
        var module =
            "-- G-CET semantic coordinator:blackjack\n" +
            "return function()\n" +
            " local __gcetBlackjackIdleElapsed=0.0\n" +
            " return function(dt,SpotManager)\n" +
            moved + " end\nend\n";
        context.Write(file, text);
        context.WriteOwnedModule("G-CET/Coordinator.lua", module);
    }
}
