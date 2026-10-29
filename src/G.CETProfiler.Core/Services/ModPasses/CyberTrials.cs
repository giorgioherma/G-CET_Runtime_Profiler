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
            var cron=context.FindFile("modules/external/Cron.lua",
                "function Cron.HasActiveTimers()", "local timers = {}");
            if ((worker is not null && (
                !worker.StartsWith("-- G-CET semantic coordinator:cybertrials\n",
                    StringComparison.Ordinal) ||
                !worker.Contains("cybertrials.realtime",StringComparison.Ordinal) ||
                !worker.Contains("cybertrials.race_state",StringComparison.Ordinal))) ||
                !cron.Text.Contains("if timer.active then",StringComparison.Ordinal) ||
                !init.Text.Contains("__gcetCyberTrialsPass4.TryAttach()",StringComparison.Ordinal))
                throw new InvalidOperationException("CyberTrials five-job source/worker incomplete.");
            var completedWorld = context.FindFile("modules/external/world.lua",
                "BaseMappinBaseController", "UpdateRootState");
            var upgradedScan = RewriteCyberTrialsWorldScan(completedWorld.Text);
            var scanNeeded = !string.Equals(upgradedScan, completedWorld.Text,
                StringComparison.Ordinal);
            if (scanNeeded) context.Write(completedWorld, upgradedScan);
            var workerV2 = CyberTrialsWorkerV2();
            var v1 = worker is null ? null : WorkerExecutable(worker);
            var workerNeeded = !string.Equals(v1, WorkerExecutable(workerV2), StringComparison.Ordinal);
            if (worker is not null && workerNeeded && !string.Equals(v1, WorkerExecutable(CyberTrialsWorkerV1),
                StringComparison.Ordinal))
            {
                var expected=workerV2.TrimEnd();
                var actual=v1 ?? string.Empty;
                var n=Math.Min(actual.Length,expected.Length);
                var first=0;
                while (first<n && actual[first]==expected[first]) first++;
                throw new InvalidOperationException(
                    "CyberTrials existing coordinator changed; refuse unproven wake migration. " +
                    "actual="+actual.Length+" canonical="+expected.Length+" firstDiff="+first);
            }
            var initV2 = CyberTrialsProbeSource(init.Text);
            var sourceNeeded = initV2 != init.Text;
            // Complete individually proven partial stages; never require
            // a previous full optimizer overlay as input.
            if (workerNeeded)
                context.WriteCoordinator("cybertrials_engine_worker.lua", workerV2);
            if (sourceNeeded) context.Write(init, initV2);
            if (scanNeeded || workerNeeded || sourceNeeded)
                return SemanticInjectionResult.Success(
                    "CyberTrials source-owned world-scan and physically pausable realtime jobs composed.");
            return SemanticInjectionResult.Skip(
                "CyberTrials five-job scheduler and guarded world scan already complete.");
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
        worldText = RewriteCyberTrialsWorldScan(worldText);
        context.Write(world, worldText);

        return SemanticInjectionResult.Success(
            "Removed global mappin-driven hub refresh, made hub mappins state-driven and idempotent, gated world scans to 10 Hz outside realtime placement/race state, and added an owned-variant mappin prefilter.");
    }


    // This is part of the existing CyberTrials generator, NOT a repair-only
    // semantic rule. It runs after generic/native source composition.
    // Original author code and generic/native source variants have the same
    // exact executable control-flow shape once comments/spacing/API aliases
    // are normalized. Unknown behavioral drift is refused.
    private static string RewriteCyberTrialsWorldScan(string source)
    {
        var updates = Regex.Matches(source,
            @"(?m)^[ \t]*function[ \t]+world[ \t]*\.[ \t]*update[ \t]*\([ \t]*\)[ \t]*(?:--[^\r\n]*)?\r?\n(?<body>[\s\S]*?)^end[ \t]*(?:--[^\r\n]*)?(?=\r?\n|\z)",
            RegexOptions.CultureInvariant);
        if (updates.Count == 0)
        {
            if (Regex.IsMatch(source, @"\bfunction\s+world\s*\.\s*update\s*\(",
                RegexOptions.CultureInvariant))
                throw new InvalidOperationException("CyberTrials world.update source boundary changed.");
            // Older compatible sources may only have the observer.
            return source;
        }
        if (updates.Count != 1)
            throw new InvalidOperationException("CyberTrials has ambiguous world update ownership.");

        var body = updates[0].Groups["body"].Value;
        var ownedSquaredDistance = Regex.IsMatch(source,
            @"\blocal\s+function\s+distanceSquared\s*\(", RegexOptions.CultureInvariant);
        if (body.Contains("local distSq = distanceSquared(", StringComparison.Ordinal))
        {
            // A completed or staged source is recognized by its behavior,
            // never by the semantic owner marker alone.
            if (!ownedSquaredDistance ||
                !Regex.IsMatch(body, @"local\s+player\s*=\s*(?:__gcetGetPlayer|Game\.GetPlayer)\s*\(\s*\)") ||
                !body.Contains("if not player or player.mountedVehicle or player:IsInCombat() then return false end", StringComparison.Ordinal) ||
                !body.Contains("local transform = player:GetWorldTransform()", StringComparison.Ordinal) ||
                !body.Contains("local update = (distSq < interactionRange * interactionRange) and not raceInProgress", StringComparison.Ordinal) ||
                !body.Contains("return nearAny", StringComparison.Ordinal))
                throw new InvalidOperationException("CyberTrials existing squared-distance scan is structurally incomplete.");
            return source;
        }
        if (ownedSquaredDistance)
            throw new InvalidOperationException("CyberTrials partial squared-distance helper ownership changed.");

        // Prove the exact *behavioral* original scan, accepting generic 0-Engine
        // player getters, native on-demand getter, comments, and whitespace.
        // Do not erase new author statements when the state machine changes.
        const string player = "Game.GetPlayer()";
        const string pos = "Game.GetPlayer():GetWorldPosition()";
        const string transform = "Game.GetPlayer():GetWorldTransform()";
        const string transformPos = "Game.GetPlayer():GetWorldTransform():GetWorldPosition():ToVector4()";
        const string fallback = "ToVector4({x=1,y=1,z=1,w=1})";
        var expected = new[]
        {
            "ifnot"+player+"thenreturnend",
            "if"+player+".mountedVehicleor"+player+":IsInCombat()thenreturnend",
            "if"+player+"and"+pos+"then",
            "posPlayer="+pos,
            "posPlayer.z=posPlayer.z+1",
            "elseif"+player+"andnot"+pos+"and"+transform+"and"+transformPos+"then",
            "posPlayer="+transformPos,
            "posPlayer.z=posPlayer.z+1",
            "elseif"+player+"andnot"+pos+"andnot"+transform+"andnot"+transformPos+"then",
            "posPlayer="+fallback,
            "posPlayer.z=posPlayer.z+1",
            "else",
            "posPlayer="+fallback,
            "posPlayer.z=posPlayer.z+1",
            "end",
            "for_,interactioninpairs(world.interactions)do",
            "localupdate=interaction.shown",
            "if(Vector4.Distance(posPlayer,interaction.pos)<interaction.interactionRange)andnotraceLogic.raceInProgressthen",
            "update=true",
            "else",
            "update=false",
            "end",
            "ifupdate~=interaction.shownthen",
            "interaction.shown=update",
            "interaction.callback(interaction.shown)",
            "end",
            "ifinteraction.iconandVector4.Distance(posPlayer,interaction.pos)<interaction.iconRangethen",
            "world.togglePin(interaction,true)",
            "elseifinteraction.iconthen",
            "world.togglePin(interaction,false)",
            "end",
            "end"
        };
        var rows = body.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => Regex.Replace(line, @"--[^\r\n]*", ""))
            .Select(line => Regex.Replace(line, @"\s+", ""))
            .Where(line => line.Length > 0)
            .Select(line => line
                .Replace("__gcetNativePlayerWorldPosition()", pos, StringComparison.Ordinal)
                .Replace("__gcetGetPlayer()", player, StringComparison.Ordinal))
            .ToArray();
        if (!rows.SequenceEqual(expected, StringComparer.Ordinal))
        {
            var mismatch = 0;
            while (mismatch < Math.Min(rows.Length, expected.Length) &&
                string.Equals(rows[mismatch], expected[mismatch], StringComparison.Ordinal))
                mismatch++;
            throw new InvalidOperationException(
                "CyberTrials author world scan source drift at statement " + mismatch +
                " (source=" + rows.Length + ", expected=" + expected.Length +
                "): unsafe to replace existing gameplay.");
        }

        var playerGetter = body.Contains("__gcetGetPlayer()", StringComparison.Ordinal)
            ? "__gcetGetPlayer()" : player;
        var positionGetter = body.Contains("__gcetNativePlayerWorldPosition()", StringComparison.Ordinal)
            ? "__gcetNativePlayerWorldPosition()" : "player:GetWorldPosition()";
        const string function = """
function world.update()
    local player = __GCET_PLAYER__
    if not player or player.mountedVehicle or player:IsInCombat() then return false end

    local playerPos = __GCET_POSITION__
    if not playerPos then
        local transform = player:GetWorldTransform()
        if transform then
            local transformPos = transform:GetWorldPosition()
            if transformPos then playerPos = transformPos:ToVector4() end
        end
    end
    if playerPos then
        posPlayer = playerPos
    else
        posPlayer = ToVector4({ x = 1, y = 1, z = 1, w = 1 })
    end
    posPlayer.z = posPlayer.z + 1

    local nearAny = false
    local raceInProgress = raceLogic.raceInProgress
    for _, interaction in pairs(world.interactions) do
        local distSq = distanceSquared(posPlayer, interaction.pos)
        local interactionRange = interaction.interactionRange or 0
        local update = (distSq < interactionRange * interactionRange) and not raceInProgress
        if update then nearAny = true end
        if update ~= interaction.shown then
            interaction.shown = update
            interaction.callback(interaction.shown)
        end
        if interaction.icon then
            local iconRange = interaction.iconRange or 0
            world.togglePin(interaction, distSq < iconRange * iconRange)
        end
    end
    return nearAny
end
""";
        const string distanceHelper = """
local function distanceSquared(a, b)
    local dx = a.x - b.x
    local dy = a.y - b.y
    local dz = a.z - b.z
    return dx * dx + dy * dy + dz * dz
end

""";
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacement = function
            .Replace("__GCET_PLAYER__", playerGetter, StringComparison.Ordinal)
            .Replace("__GCET_POSITION__", positionGetter, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", newline, StringComparison.Ordinal);
        var helper = distanceHelper.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", newline, StringComparison.Ordinal);
        var match = updates[0];
        return source[..match.Index] + helper + replacement + source[(match.Index + match.Length)..];
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
        context.WriteCoordinator("cybertrials_engine_worker.lua", CyberTrialsWorkerV2());
        s=CyberTrialsProbeSource(s);
        ComposeCyberTrialsCronActiveTimerGate(context);
        return crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s;
    }

    // The pristine mod's Cron does not expose idle timer state.
    // Install the former PASS4 active-timer gate in the same primary
    // CyberTrials generator, never as a second repair semantic.
    private static void ComposeCyberTrialsCronActiveTimerGate(
        SemanticPatchContext context)
    {
        var file=context.FindFile("modules/external/Cron.lua",
            "local timers = {}", "function Cron.Update(delta)", "return Cron");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        if(source.Contains("function Cron.HasActiveTimers()",StringComparison.Ordinal))
        {
            if(!source.Contains("for _, timer in ipairs(timers) do",StringComparison.Ordinal)
                || !source.Contains("if timer.active then",StringComparison.Ordinal))
                throw new InvalidOperationException("CyberTrials Cron timer accessor has drifted.");
            return;
        }
        const string anchor="function Cron.Update(delta)";
        if(source.Split(anchor,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("CyberTrials author Cron.Update is ambiguous.");
        const string accessor="""
---@return boolean
function Cron.HasTimers()
    return #timers > 0
end

---@return boolean
function Cron.HasActiveTimers()
    for _, timer in ipairs(timers) do
        if timer.active then return true end
    end
    return false
end

""";
        var index=source.IndexOf("---@param delta number\n---@return void\n"+anchor,
            StringComparison.Ordinal);
        if(index<0)index=source.IndexOf(anchor,StringComparison.Ordinal);
        var patched=source.Insert(index,accessor);
        context.Write(file,crlf?patched.Replace("\n","\r\n",StringComparison.Ordinal):patched);
    }

    // Canonical V1 source is retained solely to recognize already-generated
    // partial installs. One authoritative generator owns the V2 worker.
    private const string CyberTrialsWorkerV1 = """
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
""";

    // Ignore non-executable leading commentary only. All executable worker
    // statements and ownership remain byte-for-byte proven after normalization.
    private static string WorkerExecutable(string value)
    {
        var text=value.Replace("\r\n","\n",StringComparison.Ordinal).TrimEnd();
        var first=text.IndexOf('\n');
        if(first<0) return text;
        var secondEnd=text.IndexOf('\n',first+1);
        if(secondEnd>first &&
           text.AsSpan(first+1,secondEnd-first-1).TrimStart()
               .StartsWith("-- Authored state logic is retained",StringComparison.Ordinal))
            text=text[..(first+1)]+text[(secondEnd+1)..];
        return text;
    }

    private static string CyberTrialsWorkerV2()
    {
        const string oldFrame = """
    add(client.Schedule.EveryFrame({
        id = "cybertrials.realtime", pause = "never"
    }, function(ctx) realtime(ctx.dt or 0) end))
""";
        const string parkedFrame = """
    local frame = nil
    local active = true
    frame = client.Schedule.EveryFrame({
        id = "cybertrials.realtime", pause = "never"
    }, function(ctx)
        realtime(ctx.dt or 0)
        if not dispatch(0,"realtime_required") then
            frame.Pause()
            active = false
        end
    end)
    add(frame)
    add(client.Schedule.Every(0.10, {
        id = "cybertrials.wake", pause = "never",
        clock = "game", catchUp = false
    }, function()
        local required = dispatch(0,"realtime_required")
        if required and not active then
            frame.Resume()
            active = true
        elseif not required and active then
            frame.Pause()
            active = false
        end
    end))
""";
        if (!CyberTrialsWorkerV1.Contains(oldFrame, StringComparison.Ordinal))
            throw new InvalidOperationException("CyberTrials canonical V1 worker drift.");
        return CyberTrialsWorkerV1.Replace(oldFrame, parkedFrame,
            StringComparison.Ordinal);
    }

    private static string CyberTrialsProbeSource(string source)
    {
        const string anchor = "function __gcetCyberTrialsPass4.Dispatch(dt, mode)\n";
        const string probe = """
    -- Source-owned activity test: never suppress race, editor, timer or hub.
    if mode == "realtime_required" then
        if timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame then
            return false
        end
        if raceLogic.raceInProgress == true or readyToPlace == true
            or placingProp == true or timeTrials.runtimeData.inEditor == true
            or interactionUI.hubShown == true or keyPressed == true
            or centerKeyPressed == true or leftKeyPressed == true
            or rightKeyPressed == true or isPlacingKey1 == true
            or isPlacingKey2 == true or isPlacingKey3 == true
            or isPlacingKey4 == true then return true end
        -- Unknown Cron capability fails open. Real timers need original
        -- render-frame Cron.Update and catch up after menu/session reload.
        return type(Cron.HasActiveTimers) ~= "function" or
            Cron.HasActiveTimers()
    end
""";
        if (source.Contains("if mode == \"realtime_required\" then",
            StringComparison.Ordinal)) return source;
        // Source may be LF or staged CRLF; locate the function line once,
        // never assume the source string's line ending shape.
        var normalized=source.Replace("\r\n","\n",StringComparison.Ordinal);
        var count=normalized.Split(anchor,StringSplitOptions.None).Length - 1;
        if (count != 1)
            throw new InvalidOperationException(
                "CyberTrials runtime dispatcher boundary drift; owner count="+count);
        var updated=normalized.Replace(anchor,anchor+probe+"\n",
            StringComparison.Ordinal);
        return source.Contains("\r\n",StringComparison.Ordinal)
            ? updated.Replace("\n","\r\n",StringComparison.Ordinal) : updated;
    }

}
