using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    // User-reviewed add-on to Immersive Third Person. Preserve the author's
    // per-frame combat camera, weapon aim and takeover cleanup. The author
    // allows standalone operation when the base API is absent: fail open.
    private static SemanticInjectionResult ApplyDedraThirdPersonCombatWake(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function dedraReadItp()",
            "local function dedraStep(dt)",
            "local function dedraFirstPersonOnly(player)",
            "local function dedraLeaveTakeover(player, backToFirst)",
            "registerForEvent(\"onInit\", function()");
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string stage="__gcetDedraEventWakeV1";
        if(source.Contains(stage,StringComparison.Ordinal))
        {
            if(!source.Contains("local function __gcetDedraDue(dt)",StringComparison.Ordinal) ||
               !source.Contains("events.on",StringComparison.Ordinal) ||
               !source.Contains("perspectiveChanged",StringComparison.Ordinal) ||
               !source.Contains("overrideChanged",StringComparison.Ordinal) ||
               !Regex.IsMatch(source,
                   @"(?m)^[ \t]*if __gcetDedraDue\(dt\) then pcall\(dedraStep, __gcetDedraStepDelta\) end[ \t]*$",
                   RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Partial Dedra combat wake; refusing duplicate edit.");
            return SemanticInjectionResult.Skip("Dedra combat source-owned wake already complete.");
        }

        // The updated source's active Step and author cleanup remain untouched.
        // Only its frame registration is given an early read-only wake predicate.
        var registration=Regex.Matches(source,
            @"(?m)^[ \t]*(?:registerForEvent|__gcetRegisterEvent_[0-9]+)\([""']onUpdate[""'],[ \t]*function\(dt\)\s*\n[ \t]*pcall\(dedraStep,[ \t]*dt\)\s*\n[ \t]*end\)",
            RegexOptions.CultureInvariant);
        if(registration.Count!=1 ||
            !source.Contains("local function dedraReadItp()",StringComparison.Ordinal) ||
            !source.Contains("local function dedraStep(dt)",StringComparison.Ordinal) ||
            !source.Contains("Dedra.cfg.combatTpp and wantsTpp and combatFirst and not blocked",StringComparison.Ordinal) ||
            !source.Contains("if Dedra.takeover then",StringComparison.Ordinal) ||
            !source.Contains("pcall(dedraLeaveTakeover, player, true)",StringComparison.Ordinal) ||
            !source.Contains("Dedra.itp.perspective.release(\"dedra.aim\")",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Dedra ITP combat/aim/cleanup source graph changed; do not insert a wake gate.");

        const string prelude = """
        -- __gcetDedraEventWakeV1: ITP API owns the toggle and combat override.
        -- F7 is a user CET binding, not a second registered hotkey.
        local __gcetDedraWake = true
        local __gcetDedraProbe = 0.10
        local __gcetDedraElapsed = 0
        local __gcetDedraStepDelta = 0
        local __gcetDedraLast = nil
        local __gcetDedraLinked = nil
        local function __gcetDedraNotify()
          __gcetDedraWake = true
        end
        local function __gcetDedraBind()
          local ok, api = pcall(GetMod, "immersive_third_person")
          if not ok or type(api) ~= "table" or
             type(api.perspective) ~= "table" or
             type(api.perspective.get) ~= "function" then return false end
          Dedra.itp = api
          if __gcetDedraLinked == api then return true end
          local events = api.events
          if type(events) ~= "table" or type(events.on) ~= "function" then return false end
          local accepted = true
          for _, kind in ipairs({
            "perspectiveChanged", "overrideChanged", "transitionStart",
            "transitionEnd", "sessionStart", "sessionEnd"
          }) do
            local okEvent, status = pcall(events.on, kind, "gcet.dedra.tpc", __gcetDedraNotify)
            if not okEvent or status ~= 2 then accepted = false end
          end
          if accepted then __gcetDedraLinked = api end
          return accepted
        end
        local function __gcetDedraPending()
          return Dedra.takeover == true or Dedra.easeOut ~= nil
            or Dedra.on == true or Dedra.armSent == true
            or Dedra.aimAsk ~= nil or Dedra.headLookHeld == true
            or Dedra.planeHeld == true or (Dedra.standIn ~= nil and Dedra.standIn ~= "")
        end
        local function __gcetDedraDue(dt)
          local step = math.max(dt or 0, 0)
          __gcetDedraElapsed = __gcetDedraElapsed + step
          __gcetDedraProbe = __gcetDedraProbe + step
          local signal = __gcetDedraWake
          if signal or __gcetDedraProbe >= 0.10 then
            __gcetDedraWake = false
            __gcetDedraProbe = 0
            if not __gcetDedraBind() and Dedra.itp == nil then
              __gcetDedraStepDelta = __gcetDedraElapsed
              __gcetDedraElapsed = 0
              return true  -- Original standalone behavior when ITP is absent.
            end
            local api = Dedra.itp
            local ok, perspective = pcall(function() return api.perspective.get() end)
            if not ok or type(perspective) ~= "table"
              or type(perspective.userWantsThirdPerson) ~= "boolean" then
              __gcetDedraLast = nil
              __gcetDedraStepDelta = __gcetDedraElapsed
              __gcetDedraElapsed = 0
              return true  -- Unknown API state must never strand cleanup.
            end
            __gcetDedraLast = perspective
          end
          local p = __gcetDedraLast
          if not p then
            __gcetDedraStepDelta = __gcetDedraElapsed
            __gcetDedraElapsed = 0
            return true
          end
          local pending = __gcetDedraPending()
          if not p.userWantsThirdPerson and not pending then
            return false  -- No weapon/PSM/native bridge work in FPP.
          end
          local combat = p.userWantsThirdPerson and
            (p.override == "combat" or p.override == 3)
          local realtime = pending or combat or p.transitioning == true
            or p.mode == "transition"
          if not realtime and not signal and __gcetDedraElapsed < 0.25 then
            return false  -- Normal TPP stance/stand-ins at 4 Hz.
          end
          __gcetDedraStepDelta = __gcetDedraElapsed
          __gcetDedraElapsed = 0
          return true  -- Combat takeover/camera/aim retain frame cadence.
        end

        """;

        const string init="registerForEvent(\"onInit\", function()";
        if(source.Split(init,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra onInit owner ambiguous.");
        source=source.Replace(init,prelude+init+"\n  __gcetDedraBind()",StringComparison.Ordinal);

        var match=registration[0];
        var original=match.Value;
        var updated=Regex.Replace(original,
            @"(?m)^([ \t]*)pcall\(dedraStep,[ \t]*dt\)[ \t]*$",
            "$1if __gcetDedraDue(dt) then pcall(dedraStep, __gcetDedraStepDelta) end",
            RegexOptions.CultureInvariant);
        if(updated==original)
            throw new InvalidOperationException("Dedra update registration not replaced.");
        source=source[..match.Index]+updated+source[(match.Index+match.Length)..];

        // Settings toggles can change ownership without a perspective event.
        source=source.Replace("  Dedra.cfg.combatTpp = state\n",
            "  Dedra.cfg.combatTpp = state\n  __gcetDedraWake = true\n",
            StringComparison.Ordinal);
        source=source.Replace("  Dedra.cfg.anims[key] = state\n",
            "  Dedra.cfg.anims[key] = state\n  __gcetDedraWake = true\n",
            StringComparison.Ordinal);

        // Never drop cleanup through a stale session event.
        const string attached="    Dedra.takeover = false\n    Dedra.armSent = nil";
        if(!source.Contains(attached,StringComparison.Ordinal))
            throw new InvalidOperationException("Dedra player attach/reset source changed.");
        source=source.Replace(attached,
            attached+"\n    __gcetDedraWake = true\n    __gcetDedraLast = nil",
            StringComparison.Ordinal);
        if(!source.Contains("local function __gcetDedraDue(dt)",StringComparison.Ordinal) ||
           !source.Contains("if __gcetDedraDue(dt) then pcall(dedraStep, __gcetDedraStepDelta) end",
               StringComparison.Ordinal))
            throw new InvalidOperationException("Dedra wake postcondition missing.");

        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            source=source.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,source);
        return SemanticInjectionResult.Success(
            "ITP-owned CET toggle and perspective/override events wake combat and aim at original frame cadence; " +
            "FPP drops all heavy addon reads, normal noncombat TPP is 4 Hz, pending camera/animation cleanup stays live, " +
            "and unavailable APIs retain original standalone per-frame fallback.");
    }
}
