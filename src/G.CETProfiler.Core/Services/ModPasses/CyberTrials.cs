using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCyberTrials(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "hubs.setupMappins",
            "world.update()",
            "raceLogic.raceInProgress");
        const string completeMarker = "-- G-CET semantic:cybertrials-five-job-v1";
        if(init.Text.Contains(completeMarker,StringComparison.Ordinal))
        {
            var worker=context.TryReadOwnedCoordinator("cybertrials_engine_worker.lua");
            if(worker is null ||
               !worker.StartsWith("-- G-CET semantic coordinator:cybertrials\n",
                    StringComparison.Ordinal) ||
               !worker.Contains("cybertrials.realtime",StringComparison.Ordinal) ||
               !worker.Contains("cybertrials.race_state",StringComparison.Ordinal) ||
               !init.Text.Contains("__gcetCyberTrialsPass4.TryAttach()",StringComparison.Ordinal))
                throw new InvalidOperationException("CyberTrials five-job source/worker incomplete.");
            return SemanticInjectionResult.Skip(
                "CyberTrials five-job scheduler already created by the authoritative primary rule.");
        }

        var initText = ReplaceSemanticObserverBlockOnce(
            init.Text,
            "ObserveAfter",
            "BaseMappinBaseController",
            "UpdateRootState",
            "this",
            "",
            "CyberTrials global mappin refresh observer",
            "hubs.setupMappins(timeTrials.availableRaces)",
            "rootStateUpdateCount");

        var opening = "__gcetRegisterEvent_93(\"onUpdate\", function(dt)\n";
        if (!initText.Contains(opening, StringComparison.Ordinal))
            opening = FindOnUpdateOpening(initText, "dt");

        initText = ReplaceOnce(
            initText,
            opening,
            "local __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "local __gcetCyberTrialsLastRaceMappinState = nil\n" +
            "local __gcetCyberTrialsLastTrackCount = -1\n\n" +
            opening +
            "    local __gcetRaceMappinState = raceLogic.raceInProgress == true\n" +
            "    local __gcetTrackCount = #timeTrials.availableRaces\n" +
            "    if timeTrials.runtimeData.inGame and (__gcetRaceMappinState ~= __gcetCyberTrialsLastRaceMappinState or __gcetTrackCount ~= __gcetCyberTrialsLastTrackCount) then\n" +
            "        hubs.setupMappins(timeTrials.availableRaces)\n" +
            "        __gcetCyberTrialsLastRaceMappinState = __gcetRaceMappinState\n" +
            "        __gcetCyberTrialsLastTrackCount = __gcetTrackCount\n" +
            "    end\n" +
            "    __gcetCyberTrialsWorldElapsed = __gcetCyberTrialsWorldElapsed + dt\n",
            "CyberTrials onUpdate semantic state");

        var earlyWorldReplacement =
            "    if (timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame)\n" +
            "        and #timeTrials.availableRaces > 0 and #world.interactions > 0\n" +
            "        and __gcetCyberTrialsWorldElapsed >= 0.25 then\n" +
            "        interactionUI.update()\n" +
            "        world.update()\n" +
            "        __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "    end";

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^[ \t]*if\s+#timeTrials\.availableRaces\s*>\s*0\s+and\s+#world\.interactions\s*>\s*0\s+then[ \t]*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*interactionUI\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*world\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"^[ \t]*end\s*(?:--[^\r\n]*)?$",
            earlyWorldReplacement,
            "CyberTrials duplicated world/UI update");

        var gameplayWorldReplacement =
            "        Cron.Update(dt)\n" +
            "        interactionUI.update()\n" +
            "        local __gcetWorldRealtime = raceLogic.raceInProgress == true or readyToPlace == true\n" +
            "        if __gcetWorldRealtime or __gcetCyberTrialsWorldElapsed >= 0.10 then\n" +
            "            world.update()\n" +
            "            __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "        end\n" +
            "        if raceLogic.raceInProgress == true then";

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^[ \t]*Cron\.Update\s*\(\s*dt\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*interactionUI\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*world\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*if\s+raceLogic\.raceInProgress\s*==\s*true\s+then\s*(?:--[^\r\n]*)?$",
            gameplayWorldReplacement,
            "CyberTrials gameplay world cadence");

        initText=ComposeCyberTrialsPristineFiveJobScheduler(context,initText,opening);
        context.Write(init, initText);

        var hubsFile = context.FindFile(
            "modules/utils/interactionHubs.lua",
            "interactionHubs.setupMappins",
            "gamedataMappinVariant.Zzz18_RacingVariant",
            "mappinIDs",
            "raceLogic.raceInProgress",
            "RegisterMappin",
            "UnregisterMappin");

        var hubsText = RegexReplaceOnce(
            hubsFile.Text,
            @"(?m)^(?<indent>[ \t]*)(?<opening>function\s+interactionHubs\.setupMappins\s*\(\s*(?<tracks>[A-Za-z_]\w*)\s*\)\s*(?:--[^\r\n]*)?)$",
            "${indent}${opening}\n" +
            "${indent}    ${tracks} = ${tracks} or {}\n\n" +
            "${indent}    local function clearMappins()\n" +
            "${indent}        for index = #mappinIDs, 1, -1 do\n" +
            "${indent}            local mappin = mappinIDs[index]\n" +
            "${indent}            if mappin then Game.GetMappinSystem():UnregisterMappin(mappin) end\n" +
            "${indent}            mappinIDs[index] = nil\n" +
            "${indent}        end\n" +
            "${indent}    end\n\n" +
            "${indent}    if raceLogic.raceInProgress then\n" +
            "${indent}        if #mappinIDs > 0 then clearMappins() end\n" +
            "${indent}        return\n" +
            "${indent}    end\n\n" +
            "${indent}    if #mappinIDs == #${tracks} and #${tracks} > 0 then return end\n" +
            "${indent}    if #mappinIDs > 0 then clearMappins() end\n" +
            "${indent}    if #${tracks} == 0 then return end",
            "CyberTrials idempotent state-driven mappin registry");
        context.Write(hubsFile, hubsText);

        var world = context.FindFile(
            "modules/external/world.lua",
            "BaseMappinBaseController",
            "UpdateRootState",
            "world.interactions");

        var newObserver =
            "    ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\", function(this) -- Custom race pin texture\n" +
            "        local mappin = this:GetMappin()\n" +
            "        if not mappin or mappin:GetVariant() ~= gamedataMappinVariant.Zzz18_RacingVariant then return end\n" +
            "        local record = TweakDBInterface.GetUIIconRecord(\"ChoiceIcon.VehicleIcon\")\n" +
            "        this.iconWidget:SetAtlasResource(record:AtlasResourcePath())\n" +
            "        this.iconWidget:SetTexturePart(record:AtlasPartName())\n" +
            "        this.iconWidget:SetTintColor(HDRColor.new({ Red = 1, Green = 219 / 255, Blue = 78 / 255 }))\n" +
            "    end)\n";

        var worldText = ReplaceSemanticObserverBlockOnce(
            world.Text,
            "ObserveAfter",
            "BaseMappinBaseController",
            "UpdateRootState",
            "this",
            newObserver,
            "CyberTrials owned-mappin prefilter",
            "GetMappin",
            "GetWorldPosition",
            "world.interactions",
            "Vector4.Distance",
            "iconWidget");
        context.Write(world, worldText);

        return SemanticInjectionResult.Success(
            "Removed global mappin-driven hub refresh, made hub mappins state-driven and idempotent, gated world scans to 10 Hz outside realtime placement/race state, and added an owned-variant mappin prefilter.");
    }


    private static SemanticInjectionResult ApplyCyberTrialsClosedOverlayProvider(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua", "DrawEditor()", "BestTimes()",
            "WindowHiderTool", "windowSettings.Current.mywindowhidden");
        var text = file.Text;
        const string tag = "__gcetCyberTrialsOverlayProviderWake";
        if (text.Contains(tag, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Closed overlay WindowHiderTool lookup already gated.");
        var expression = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+WindowHiderTool\s*=\s*GetMod\(\s*['""]WindowHiderTool['""]\s*\)\r?\n" +
            @"\k<indent>if\s+WindowHiderTool\s+and\s+cetopen\s+then\r?\n" +
            @"[ \t]+DrawWindowHider\(\)\r?\n" +
            @"\k<indent>elseif\s+not\s+WindowHiderTool\s+then\r?\n" +
            @"[ \t]+windowSettings\.Current\.mywindowhidden\s*=\s*false\r?\n" +
            @"\k<indent>end",
            RegexOptions.CultureInvariant);
        var matches = expression.Matches(text);
        if (matches.Count != 1 ||
            !Regex.IsMatch(text,
                @"(?m)^\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onDraw['""]\s*,\s*function\s*\("))
            throw new InvalidOperationException("CyberTrials onDraw provider/overlay semantics not uniquely proven.");
        text = expression.Replace(text, m =>
        {
            var i = m.Groups["indent"].Value;
            return i + "-- G-CET " + tag + ": closed/no-hide overlay needs no CET plugin lookup\n" +
                i + "if cetopen or (windowSettings.Current and windowSettings.Current.mywindowhidden) then\n" +
                i + "    local WindowHiderTool = GetMod(\"WindowHiderTool\")\n" +
                i + "    if WindowHiderTool and cetopen then\n" +
                i + "        DrawWindowHider()\n" +
                i + "    elseif not WindowHiderTool then\n" +
                i + "        windowSettings.Current.mywindowhidden = false\n" +
                i + "    end\n" +
                i + "end";
        });
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Skipped repeated CET GetMod lookup when overlay is closed and its hide state is already false; original open/hide recovery unchanged.");
    }
    // One authoritative source transform: original callback content is split
    // by its own source-proven responsibilities, never replaced with a copy
    // of a previous generated ZIP. Generic frame registrar is preserved.
    private static string ComposeCyberTrialsPristineFiveJobScheduler(
        SemanticPatchContext context, string source, string opening)
    {
        var crlf=source.Contains("\r\n",StringComparison.Ordinal);
        var s=source.Replace("\r\n","\n",StringComparison.Ordinal);
        var begin=s.IndexOf(opening,StringComparison.Ordinal);
        if(begin<0 || s.IndexOf(opening,begin+opening.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("CyberTrials update registrar is absent or ambiguous.");
        // A 0-column callback close is a stronger ownership boundary than
        // assuming the author's next unrelated function is named NewMappin.
        // Older compatible versions need not have that function at all.
        var callbackClose=Regex.Match(s[(begin+opening.Length)..],
            @"(?m)^end[ \t]*\)[ \t]*(?:--[^\n]*)?$",
            RegexOptions.CultureInvariant);
        if(!callbackClose.Success)
            throw new InvalidOperationException("CyberTrials onUpdate closing boundary not proven.");
        var close=begin+opening.Length+callbackClose.Index;
        var callback=s[(begin+opening.Length)..close];
        var systemMatch=Regex.Match(callback,
            @"(?m)^    if\s+(?:__gcetGetSystemRequestsHandler\(\)|Game\.GetSystemRequestsHandler\(\)):IsPreGame\(\)\s+then\s*$",
            RegexOptions.CultureInvariant);
        var system=systemMatch.Success?systemMatch.Index:-1;
        var editor=system<0?-1:callback.IndexOf(
            "    if keyPressed == true and readyToPlace == true then",system,StringComparison.Ordinal);
        var gameplay=editor<0?-1:callback.IndexOf(
            "    if not timeTrials.runtimeData.inMenu and timeTrials.runtimeData.inGame then",editor,StringComparison.Ordinal);
        // A deliberately smaller compatible-version callback still receives
        // the safe existing three-file semantic pass. Never fabricate the
        // five runtime responsibilities from absent author code.
        if(!systemMatch.Success &&
           !callback.Contains("    if keyPressed == true and readyToPlace == true then",
               StringComparison.Ordinal) &&
           !callback.Contains("    if rightKeyPressed == true and readyToPlace == true then",
               StringComparison.Ordinal))
            return source;
        if(system<=0||editor<=system||gameplay<=editor ||
           callback.IndexOf("    if keyPressed == true and readyToPlace == true then",editor+1,
               StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("CyberTrials full system/editor/race ownership changed.");
        var setup=callback[..system];
        var systemBody=callback[system..editor];
        var editorBody=callback[editor..gameplay];
        var gameplayBody=callback[gameplay..];
        const string elapsed="    __gcetCyberTrialsWorldElapsed = __gcetCyberTrialsWorldElapsed + dt\n";
        if(setup.Split(elapsed,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("CyberTrials generated hub state/early world clock changed.");
        setup=setup.Replace(elapsed,
            "    if mode == \"fallback\" then\n"+elapsed,StringComparison.Ordinal);
        setup+="    end\n";
        const string gameplayStart=
            "        Cron.Update(dt)\n"+
            "        interactionUI.update()\n"+
            "        local __gcetWorldRealtime = raceLogic.raceInProgress == true or readyToPlace == true\n"+
            "        if __gcetWorldRealtime or __gcetCyberTrialsWorldElapsed >= 0.10 then\n"+
            "            world.update()\n"+
            "            __gcetCyberTrialsWorldElapsed = 0.0\n"+
            "        end\n";
        if(gameplayBody.Split(gameplayStart,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("CyberTrials authored realtime/gameplay split changed.");
        gameplayBody=gameplayBody.Replace(gameplayStart,
            "        if mode == \"fallback\" then\n"+gameplayStart+
            "        end\n",StringComparison.Ordinal);
        if(!gameplayBody.Contains("        if raceLogic.raceInProgress == true then",StringComparison.Ordinal) ||
           !editorBody.Contains("    if rightKeyPressed == true and readyToPlace == true then",StringComparison.Ordinal))
            throw new InvalidOperationException("CyberTrials race/editor callback proof failed.");
        var dispatch= """
-- G-CET semantic:cybertrials-five-job-v1
-- Original callback has a complete source-owned fallback.
local __gcetCyberTrialsPass4 = { attached = false, attaching = false }
function __gcetCyberTrialsPass4.Dispatch(dt, mode)
    if mode == "world_far" or mode == "world_near" then
        if timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame
           or raceLogic.raceInProgress == true then return end
        if mode == "world_far" then
            if readyToPlace or placingProp or timeTrials.runtimeData.inEditor
                or interactionUI.hubShown then return end
            if #timeTrials.availableRaces > 0 and #world.interactions > 0 then
                world.update()
            end
        elseif interactionUI.hubShown then
            world.update()
        end
        return
    end
    if mode == "fallback" or mode == "race" then
"""+
        setup+
        "    end\n"+
        "    if mode == \"fallback\" or mode == \"system\" then\n"+
        systemBody+
        "    end\n"+
        "    if mode == \"fallback\" or mode == \"editor\" then\n"+
        editorBody+
        "    end\n"+
        "    if mode == \"fallback\" or mode == \"race\" then\n"+
        gameplayBody+
        "    end\n"+
        """
end
function __gcetCyberTrialsPass4.TryAttach()
    if __gcetCyberTrialsPass4.attached then return true end
    if __gcetCyberTrialsPass4.attaching then return false end
    __gcetCyberTrialsPass4.attaching = true
    local created = {}
    local ok = pcall(function()
        require("GCET_Semantic/cybertrials_engine_worker").attach(
            created, __gcetCyberTrialsPass4.Dispatch, function()
                if timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame then return end
                if type(Cron.HasActiveTimers) ~= "function" or Cron.HasActiveTimers() then
                    Cron.Update(0.0)
                end
            end,
            function(dt)
                if timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame then return end
                if type(Cron.HasActiveTimers) ~= "function" or Cron.HasActiveTimers() then
                    Cron.Update(dt)
                end
                if interactionUI.hubShown then interactionUI.update() end
                if readyToPlace or placingProp or timeTrials.runtimeData.inEditor
                    or keyPressed or centerKeyPressed or leftKeyPressed or rightKeyPressed
                    or isPlacingKey1 or isPlacingKey2 or isPlacingKey3 or isPlacingKey4 then
                    __gcetCyberTrialsPass4.Dispatch(dt,"editor")
                    if #timeTrials.availableRaces > 0 and #world.interactions > 0 then
                        world.update()
                    end
                end
            end)
        __gcetCyberTrialsPass4.attached = true
    end)
    __gcetCyberTrialsPass4.attaching = false
    if not ok then
        for _, job in ipairs(created) do
            if job and type(job.Cancel) == "function" then pcall(job.Cancel) end
        end
        return false
    end
    return true
end

""";
        var replacement=dispatch+opening+
            "    if __gcetCyberTrialsPass4.attached then return end\n"+
            "    __gcetCyberTrialsPass4.TryAttach()\n"+
            "    __gcetCyberTrialsPass4.Dispatch(dt,\"fallback\")\n"+
            "end)";
        s=s[..begin]+replacement+s[(close+callbackClose.Length)..];
        context.WriteCoordinator("cybertrials_engine_worker.lua", """
-- G-CET semantic coordinator:cybertrials
-- Authored state logic is retained in the single source dispatcher.
local M = {}
function M.attach(created, dispatch, legacyTick, realtime)
    local ok, engine = pcall(GetMod,"0-Engine")
    if not ok or type(engine) ~= "table" or type(engine.Register) ~= "function" then
        error("CyberTrials 0-Engine not ready")
    end
    local client = engine.Register("CyberTrials")
    if type(client) ~= "table" or type(client.Schedule) ~= "table" or
       type(client.Schedule.Every) ~= "function" or
       type(client.Schedule.EveryFrame) ~= "function" then
        error("CyberTrials Scheduler missing")
    end
    local function add(handle)
        if type(handle) ~= "table" then error("Scheduler refused CyberTrials job") end
        created[#created + 1] = handle
    end
    add(client.Schedule.Every(1.0, {
        id = "cybertrials.world_far", pause = "never",
        clock = "game", catchUp = false
    }, function() dispatch(1.0,"world_far") end))
    add(client.Schedule.Every(0.10, {
        id = "cybertrials.world_near", pause = "never",
        clock = "game", catchUp = false
    }, function() dispatch(0.10,"world_near") end))
    add(client.Schedule.Every(0.25, {
        id = "cybertrials.system_state", pause = "never",
        clock = "real", catchUp = false
    }, function() dispatch(0.25,"system") end))
    add(client.Schedule.Every(0.10, {
        id = "cybertrials.race_state", pause = "never",
        clock = "game", catchUp = false
    }, function(ctx) dispatch(ctx.elapsed or 0.10,"race") end))
    add(client.Schedule.EveryFrame({
        id = "cybertrials.realtime", pause = "never"
    }, function(ctx) realtime(ctx.dt or 0) end))
    return true
end
return M
""");
        return crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s;
    }

}
