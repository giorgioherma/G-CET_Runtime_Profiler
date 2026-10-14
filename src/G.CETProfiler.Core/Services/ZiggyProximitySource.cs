using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Extension of the EXISTING Ziggy navigation semantic. No separate optimizer:
// one v3 pass composes the v2 navigation sentinel and the offered/far controller lane.
internal static class ZiggyProximitySource
{
    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        // Original and v1 are upgraded to v2; already-v2 navigation is preserved.
        SemanticInjectors.ApplyZiggyLastPlayNavigation(context);

        var file=context.FindFile("init.lua", "local function update(dt)",
            "Navigation.update(dt", "C.giver.position", "H.s", "Dialogue");
        var text=file.Text;
        // A v3-optimized live source already contains the native proximity
        // sentinel. Upgrade that one source token in place rather than
        // injecting an extra callback wrapper (which would double dispatch).
        // Original author archives take the same control-flow path below.
        if (text.Contains("__gcetZiggyOfferedFarAcc", StringComparison.Ordinal))
        {
            if (text.Contains("G-CET semantic:ziggy-unaccepted-far-1hz",
                              StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Ziggy far-idle cadence stage already applied.");
            var cadence = Regex.Matches(
                text,
                @"__gcetZiggyOfferedFarAcc\s*<\s*0[.]25\s*then\s*return",
                RegexOptions.CultureInvariant);
            if (cadence.Count != 1 ||
                !text.Contains("if __gcetProbeOK and __gcetFar then",
                               StringComparison.Ordinal) ||
                !text.Contains("H.s.phase=='offered'", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Ziggy offered/far wake and timer semantics drifted; refusing cadence.");
            var m = cadence[0];
            text = text[..m.Index] +
                "__gcetZiggyOfferedFarAcc < 1.0 then return" +
                text[(m.Index+m.Length)..];
            text = "-- G-CET semantic:ziggy-unaccepted-far-1hz\n" + text;
            context.Write(file,text);
            return SemanticInjectionResult.Success(
                "Upgraded existing 4Hz distant, unaccepted Starman passive " +
                "controller to 1Hz; same every-frame near-Milo probe and immediate " +
                "full-speed wake; dialogue, accepted stages and timers unchanged.");
        }

        var matches=Regex.Matches(text,
            @"(?m)^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*dt\s*\)\s*$",
            RegexOptions.CultureInvariant);
        if(matches.Count!=1)throw new InvalidOperationException(
            "Starman author onUpdate is missing or ambiguous.");
        var opening=matches[0].Value;
        var prelude= """
local __gcetZiggyOfferedFarAcc=0.0
-- Keep a tiny near/far probe on every frame only while quest is unaccepted.
local function __gcetZiggyOfferedFar()
 if not H.s or H.s.phase~='offered' or not valid(H.hooks) then return false end
 if not H.hooks:SessionLoaded() or H.generation~=H.hooks.generation then return false end
 if Dialogue.busy(H.dialogue) then return false end
 local p=Game.GetPlayer()
 if not valid(p) or not p:IsAttached() then return false end
 if tostring(p:GetEntityID().hash)~=H.playerKey then return false end
 local pos=p:GetWorldPosition()
 local loc=C.giver and C.giver.position
 if not pos or not loc then return false end
 local dx=pos.x-loc[1];local dy=pos.y-loc[2];local dz=pos.z-loc[3]
 return dx*dx+dy*dy+dz*dz>130*130
end
""";
        var gate= """
 local __gcetProbeOK,__gcetFar=false,false
 if H.s and H.s.phase=='offered' then
  __gcetProbeOK,__gcetFar=pcall(__gcetZiggyOfferedFar)
 end
 if __gcetProbeOK and __gcetFar then
  __gcetZiggyOfferedFarAcc=__gcetZiggyOfferedFarAcc+(dt or 0)
  if __gcetZiggyOfferedFarAcc<1.0 then return end
  dt=__gcetZiggyOfferedFarAcc
  __gcetZiggyOfferedFarAcc=0
 elseif __gcetZiggyOfferedFarAcc>0 then
  dt=(dt or 0)+__gcetZiggyOfferedFarAcc
  __gcetZiggyOfferedFarAcc=0
 end
""";
        text="-- G-CET semantic:ziggy-unaccepted-far-1hz\n" + text;
        text=text[..matches[0].Index]+prelude+"\n"+opening+"\n"+gate+
             text[(matches[0].Index+opening.Length+1)..];
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "V2 navigation preserved; the unaccepted offered/far quest controller runs at 1 Hz " +
            "beyond 130m, while near giver, dialogue, session transitions and all active phases " +
            "keep original frame cadence. State and timer elapsed time are preserved.");
    }
}
