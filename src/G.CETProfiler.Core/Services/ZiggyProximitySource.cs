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

        // The Ziggy semantic is a TWO-FILE composable pass: navigation and
        // controller must carry the complete revision marker together. The
        // existing v3 navigation logic is retained; this comment records that
        // the v4 passive-lane upgrade belongs to the same installed stage.
        var nav=context.FindFile("navigation.lua",
            "function N.update(dt,path,phase,playerKey,generation,hooks,pinId,runtime,gameplay)",
            "GetQuestMappinPosition(hash)");
        var navigation = nav.Text;
        if (!navigation.Contains("G-CET semantic:ziggy-unaccepted-far-1hz",
                                 StringComparison.Ordinal))
            navigation = "-- G-CET semantic:ziggy-unaccepted-far-1hz\n" + navigation;
        if (!navigation.Contains("G-CET semantic:ziggy-passive-ui-v5",
                                 StringComparison.Ordinal))
            navigation = "-- G-CET semantic:ziggy-passive-ui-v5\n" + navigation;
        context.Write(nav, navigation);

        var file=context.FindFile("init.lua", "local function update(dt)",
            "Navigation.update(dt", "C.giver.position", "H.s", "Dialogue");
        var text=file.Text;
        // A v3-optimized live source already contains the native proximity
        // sentinel. Upgrade that one source token in place rather than
        // injecting an extra callback wrapper (which would double dispatch).
        // Original author archives take the same control-flow path below.
        if (text.Contains("__gcetZiggyOfferedFarAcc", StringComparison.Ordinal))
        {
            // Version-fluid progression: v3 0.25s, v4 1.0s, or missing
            // v5 passive-presentation cleanup. Never nest another update hook.
            var oldCadence=Regex.Matches(text,
                @"__gcetZiggyOfferedFarAcc\s*<\s*0[.]25\s*then\s*return",
                RegexOptions.CultureInvariant);
            var newCadence=Regex.Matches(text,
                @"__gcetZiggyOfferedFarAcc\s*<\s*1[.]0\s*then\s*return",
                RegexOptions.CultureInvariant);
            var hasV4=text.Contains("G-CET semantic:ziggy-unaccepted-far-1hz",
                                    StringComparison.Ordinal);
            if (!text.Contains("if __gcetProbeOK and __gcetFar then",
                               StringComparison.Ordinal) ||
                !text.Contains("H.s.phase=='offered'", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Ziggy game-phase and wake proof changed; refusing update.");
            if (newCadence.Count==1 && oldCadence.Count==0 && hasV4)
            {
                // Existing 1Hz stage; add only the missing UI cleanup.
            }
            else if(oldCadence.Count==1 && newCadence.Count==0 && !hasV4)
            {
                var m=oldCadence[0];
                text=text[..m.Index] +
                    "__gcetZiggyOfferedFarAcc < 1.0 then return" +
                    text[(m.Index+m.Length)..];
                text="-- G-CET semantic:ziggy-unaccepted-far-1hz\n"+text;
            }
            else throw new InvalidOperationException(
                "Ziggy earlier 1Hz/0.25s stages are inconsistent or source drifted.");
            if (!text.Contains("G-CET semantic:ziggy-passive-ui-v5",
                               StringComparison.Ordinal))
                text=ReducePassivePresentation(text);
            text=ReuseZiggyNativeQuestManagerForSave(text);
            text=ReduceProtectedUpdateAllocations(text);
            context.Write(file,text);
            return SemanticInjectionResult.Success(
                "Upgraded existing Ziggy v3/v4 without duplicate callback wrappers: " +
                "far-offered remains 1Hz, active quest remains realtime; " +
                "inactive owned HUD/prompt cleanup now occurs only on first frame " +
                "or an actual presentation-to-hidden transition.");
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
        prelude = SemanticInjectors.PreserveGenericGetters(text,prelude);
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
        // Apply the source-offset replacement BEFORE adding the marker
        // header; prepending first invalidates Regex.Match.Index.
        text=text[..matches[0].Index]+prelude+"\n"+opening+"\n"+gate+
             text[(matches[0].Index+opening.Length+1)..];
        text="-- G-CET semantic:ziggy-unaccepted-far-1hz\n"+text;
        text=ReducePassivePresentation(text);
        text=ReuseZiggyNativeQuestManagerForSave(text);
        text=ReduceProtectedUpdateAllocations(text);
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "V2 navigation preserved; the unaccepted offered/far quest controller runs at 1 Hz " +
            "beyond 130m, while near giver, dialogue, session transitions and all active phases " +
            "keep original frame cadence. State and timer elapsed time are preserved.");
    }

    // The authored error handler only normalizes errors to strings. Reuse
    // pcall's argument passing instead of constructing two closures/frame.
    private static string ReduceProtectedUpdateAllocations(string text)
    {
        const string marker="G-CET semantic:ziggy-no-frame-closure-v7";
        const string oldCall="local ok,err=xpcall(function()update(dt)end,function(e)return tostring(e)end)";
        const string newCall="local ok,err=pcall(update,dt)\n  if not ok then err=tostring(err) end";
        if(text.Contains(marker,StringComparison.Ordinal))
        {
            if(text.Contains(oldCall,StringComparison.Ordinal) ||
               !text.Contains(newCall,StringComparison.Ordinal))
                throw new InvalidOperationException("Partial Ziggy protected update.");
            return text;
        }
        if(text.Split(oldCall,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Ziggy protected update source drifted.");
        return text.Replace(oldCall,"-- "+marker+"\n  "+newCall,StringComparison.Ordinal);
    }

    internal static bool IsNativeSnapshotCompletionSatisfied(string ownerFolder)
    {
        try
        {
            var init = File.ReadAllText(Path.Combine(ownerFolder, "init.lua"));
            var nav = File.ReadAllText(Path.Combine(ownerFolder, "navigation.lua"));
            if (!init.Contains("G-CET semantic:ziggy-unaccepted-far-1hz", StringComparison.Ordinal) ||
                !init.Contains("G-CET semantic:ziggy-passive-ui-v5", StringComparison.Ordinal) ||
                !init.Contains("G-CET semantic:ziggy-no-frame-closure-v7", StringComparison.Ordinal) ||
                !init.Contains("H.__gcetPuzzleWasShown", StringComparison.Ordinal) ||
                !init.Contains("H.__gcetPromptWasShown", StringComparison.Ordinal) ||
                !nav.Contains("G-CET semantic:ziggy-passive-ui-v5", StringComparison.Ordinal) ||
                !nav.Contains("missingScanAge", StringComparison.Ordinal))
                return false;
            // Historical supported test/author versions without the
            // original snapshot-writing function cannot need this B stage.
            var hasSave = init.Contains("local function save()", StringComparison.Ordinal);
            var hasSetter = init.Contains("local function set(k,v)", StringComparison.Ordinal);
            if (!hasSave && !hasSetter)
                return true;
            if (!hasSave || !hasSetter)
                return false;
            return init.Contains("G-CET semantic:ziggy-native-snapshot-manager-v6",
                                 StringComparison.Ordinal) &&
                Regex.IsMatch(init,
                    @"local\s+__gcetZiggySnapshotQuestSystem\s*=\s*(?:Game\.GetQuestsSystem|__gcetGetQuestsSystem)\s*\(\s*\)",
                    RegexOptions.CultureInvariant) &&
                init.Contains("__gcetZiggySnapshotQuestSystem:SetFactStr('zg11_'..k,v)",
                              StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Native lane B: save() writes 15-20+ quest facts every half second.
    // The author-level set(k,v) reacquires Game.GetQuestsSystem() for EACH
    // fact. Reuse the manager only inside ONE synchronous save invocation:
    // never cache engine objects across save loads, missions or frames.
    // Keeps every SetFactStr write, its order and error behavior identical.
    private static string ReuseZiggyNativeQuestManagerForSave(string text)
    {
        const string marker = "G-CET semantic:ziggy-native-snapshot-manager-v6";
        var start = Regex.Matches(text, @"(?m)^local function save\(\)[ \t]*\r?$", RegexOptions.CultureInvariant);
        var end = Regex.Matches(text, @"(?m)^local function trace\(event,details\)[ \t]*\r?$", RegexOptions.CultureInvariant);
        // The author setter is unchanged semantically when generic source
        // rewriting has substituted a PROVEN shared QuestSystem getter.
        // Match the actual receiver, never a historical source version/hash.
        var setter=Regex.Matches(text,
            @"local\s+function\s+set\s*\(\s*k\s*,\s*v\s*\)\s*(?<getter>Game\.GetQuestsSystem\(\)|__gcetGetQuestsSystem\(\))\s*:\s*SetFactStr\s*\(\s*['""]zg11_['""]\s*\.\.\s*k\s*,\s*v\s*\)\s*end",
            RegexOptions.CultureInvariant);
        var authorSetter=setter.Count==1 &&
            (setter[0].Groups["getter"].Value=="Game.GetQuestsSystem()" ||
             SemanticInjectors.PreserveGenericGetters(text,
               "Game.GetQuestsSystem()")=="__gcetGetQuestsSystem()");
        var getter=setter.Count==1
            ? setter[0].Groups["getter"].Value : "Game.GetQuestsSystem()";
        if (start.Count == 0 && end.Count == 0 && !authorSetter)
            return text; // Minimal historical source, has no snapshot writer at all.
        if (start.Count != 1 || end.Count != 1 || start[0].Index >= end[0].Index ||
            !authorSetter)
            throw new InvalidOperationException("Ziggy native snapshot/save source scope changed.");

        var portion = text[start[0].Index..end[0].Index];
        if (portion.Contains(marker, StringComparison.Ordinal))
        {
            if (!portion.Contains("__gcetZiggySnapshotQuestSystem", StringComparison.Ordinal) ||
                !portion.Contains("local function __gcetZiggySnapshotSet(", StringComparison.Ordinal) ||
                Regex.IsMatch(portion, @"(?<![\w_])set\s*\(", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Ziggy native quest manager stage is incomplete.");
            return text;
        }
        if (portion.Contains("__gcetZiggySnapshotQuestSystem", StringComparison.Ordinal) ||
            portion.Contains("__gcetZiggySnapshotSet", StringComparison.Ordinal))
            throw new InvalidOperationException("Ziggy snapshot manager has a partial/foreign source stage.");
        const string guard = " if not H.s or not valid(H.hooks)or not H.hooks:SessionLoaded()then return end";
        if (!portion.Contains(guard, StringComparison.Ordinal) ||
            !portion.Contains(" local s=H.s", StringComparison.Ordinal))
            throw new InvalidOperationException("Ziggy saved quest-session guard changed.");
        var calls = Regex.Matches(portion, @"(?<![\w_])set\s*\(", RegexOptions.CultureInvariant);
        if (calls.Count < 6)
            throw new InvalidOperationException("Ziggy quest snapshot writes changed.");
        portion = Regex.Replace(portion, @"(?<![\w_])set\s*\(",
            "__gcetZiggySnapshotSet(", RegexOptions.CultureInvariant);
        portion = portion.Replace(" local s=H.s", 
            " -- " + marker + "\n" +
            " local __gcetZiggySnapshotQuestSystem=" + getter + "\n" +
            " local function __gcetZiggySnapshotSet(k,v)\n" +
            "  __gcetZiggySnapshotQuestSystem:SetFactStr('zg11_'..k,v)\n" +
            " end\n" +
            " local s=H.s",
            StringComparison.Ordinal);
        if (!portion.Contains(marker, StringComparison.Ordinal) ||
            Regex.IsMatch(portion, @"(?<![\w_])set\s*\(", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("Ziggy native save stage postcondition failed.");
        return text[..start[0].Index]+portion+text[end[0].Index..];
    }

    // The original Starman source unconditionally calls HidePuzzle from
    // onDraw (50-60 times/sec) and ClearPrompt on unprompted update ticks.
    // In matching original Redscript, HidePuzzle only hides its own canvas;
    // ClearPrompt scans the dialog-choice blackboard for Ziggy's prompt ID.
    // Preserve the first cleanup after loading and any visible-to-hidden
    // transition. Never modify actual Draw/ShowPuzzle or StoryChoices paths.
    private static string ReducePassivePresentation(string text)
    {
        const string marker="G-CET semantic:ziggy-passive-ui-v5";
        if (text.Contains(marker,StringComparison.Ordinal))
            throw new InvalidOperationException("Ziggy v5 passive UI already applied.");
        var oldHide=Regex.Matches(text,
            @"(?m)^(?<indent>[ \t]*)if\s+not\s+H[.]hudDrawn\s+and\s+valid\s*\(\s*H[.]hooks\s*\)\s*then\s+H[.]hooks:HidePuzzle\s*\(\s*\)\s*end\s*$",
            RegexOptions.CultureInvariant);
        var oldPrompt=Regex.Matches(text,
            @"(?m)^(?<indent>[ \t]*)if\s+not\s+H[.]prompted\s+then\s+H[.]hooks:ClearPrompt\s*\(\s*\)\s*end\s*$",
            RegexOptions.CultureInvariant);
        if(oldHide.Count!=1 || oldPrompt.Count!=1 ||
           !text.Contains("H.hudDrawn=ok and drawn==true",StringComparison.Ordinal) ||
           !text.Contains("H.prompted=true",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Ziggy HUD/prompt lifecycle no longer matches original; refusing source rewrite.");
        // Rewriting later source first preserves the earlier index.
        var hide=oldHide[0];
        var hi=hide.Groups["indent"].Value;
        var hideNew=string.Join("\n",new [] {
            hi+"if H.hudDrawn then",
            hi+" H.__gcetPuzzleWasShown=true",
            hi+"elseif H.__gcetPuzzleWasShown~=false and valid(H.hooks) then",
            hi+" H.hooks:HidePuzzle()",
            hi+" H.__gcetPuzzleWasShown=false",
            hi+"end"
        });
        text=text[..hide.Index]+hideNew+text[(hide.Index+hide.Length)..];
        var oldPromptNow=Regex.Match(text,
            @"(?m)^(?<indent>[ \t]*)if\s+not\s+H[.]prompted\s+then\s+H[.]hooks:ClearPrompt\s*\(\s*\)\s*end\s*$",
            RegexOptions.CultureInvariant);
        if(!oldPromptNow.Success)throw new InvalidOperationException(
            "Ziggy prompt no longer matches after HUD edit.");
        var pi=oldPromptNow.Groups["indent"].Value;
        var promptNew=string.Join("\n",new [] {
            pi+"if H.prompted then",
            pi+" H.__gcetPromptWasShown=true",
            pi+"elseif H.__gcetPromptWasShown~=false then",
            pi+" H.hooks:ClearPrompt()",
            pi+" H.__gcetPromptWasShown=false",
            pi+"end"
        });
        text=text[..oldPromptNow.Index]+promptNew+
             text[(oldPromptNow.Index+oldPromptNow.Length)..];
        // nil means "first frame"; both calls execute once as an initial
        // stale-widget cleanup, then only on a real state transition.
        return "-- "+marker+"\n"+text;
    }

}
