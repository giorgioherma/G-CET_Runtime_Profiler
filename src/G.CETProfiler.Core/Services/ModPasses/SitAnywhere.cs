using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySitAnywhere(
        SemanticPatchContext context)
    {
        // Incremental PASS4 live-stage extension: skip logic-level work
        // when neither scanner nor a workspot nor a pending hide exists.
        // World/UI/timer reconciliation and active workspot frames are unchanged.
        var live = context.FindFile("init.lua", "self.logic:onUpdate()");
        if (live.Text.Contains("local function runSitAnywhereFrame(dt)", StringComparison.Ordinal))
        {
            // A complete current-generation result is already composed from
            // pristine author source. Never treat its scheduler as an
            // intermediate PASS4 input and apply another repair pass.
            const string engineMarker = "-- G-CET semantic:sitanywhere-v1";
            const string legacyEngineMarker = "-- G-CET semantic:sitanywhere-engine-worker-v1";
            var currentStage = live.Text.Contains(engineMarker, StringComparison.Ordinal);
            var legacyStage = live.Text.Contains(legacyEngineMarker, StringComparison.Ordinal);
            if (currentStage || legacyStage)
            {
                var worker = context.TryReadOwnedCoordinator("sitanywhere_engine_worker.lua");
                var expectedOwner = currentStage
                    ? "-- G-CET semantic coordinator:sitanywhere\n"
                    : "-- G-CET semantic coordinator:sitanywhere-engine-worker\n";
                if (worker is null ||
                    !worker.StartsWith(expectedOwner, StringComparison.Ordinal) ||
                    !live.Text.Contains("if pass4Attached then return end", StringComparison.Ordinal) ||
                    !live.Text.Contains("TryAttachSitAnywherePass4()", StringComparison.Ordinal) ||
                    !live.Text.Contains("runSitAnywhereFrame(dt)", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "SitAnywhere scheduler source/worker is partially installed.");
                var partialLogic = context.FindFile("modules/logic.lua",
                    "function logic:onUpdate", "function logic:hideAllWorkspots");
                var partialWorld = context.FindFile("modules/worldInteraction.lua",
                    "world.interactions", "function world.update");
                var completed = PreserveSitAnywhereSourceFastpaths(
                    context, partialLogic.Text, partialWorld.Text);
                var logicV2 = SitAnywhereLogicWakeSource(completed.Logic);
                if (logicV2 != partialLogic.Text) context.Write(partialLogic, logicV2);
                if (completed.World != partialWorld.Text) context.Write(partialWorld, completed.World);
                var prior = worker.Replace("\r\n","\n",StringComparison.Ordinal);
                var workerV2 = SitAnywhereWorkerV2();
                var needsWorker = prior != workerV2;
                if (needsWorker && prior != SitAnywhereWorkerV1)
                    throw new InvalidOperationException(
                        "SitAnywhere prior coordinator drift; refuse unproven wake migration.");
                var initV2 = SitAnywhereRealtimeWakeSource(live.Text);
                var needsInit = initV2 != live.Text;
                if (needsWorker != needsInit)
                    throw new InvalidOperationException(
                        "SitAnywhere realtime source and worker partial state mismatch.");
                if (needsWorker)
                {
                    context.WriteCoordinator("sitanywhere_engine_worker.lua",workerV2);
                    context.Write(live,initV2);
                }
                if (logicV2 != partialLogic.Text || completed.World != partialWorld.Text ||
                    completed.LoggerChanged || needsWorker)
                    return SemanticInjectionResult.Success(
                        "Completed SitAnywhere source-local optimization and pausable frame owner.");
                return SemanticInjectionResult.Skip(
                    "SitAnywhere complete scheduler and frame worker already installed.");
            }
            const string marker = "-- G-CET semantic:sitanywhere-scanner-workspot-only-v1";
            if (live.Text.Contains(marker, StringComparison.Ordinal))
                return SemanticInjectionResult.Skip("SitAnywhere scanner/workspot idle extension already installed.");
            const string before =
                "        -- Active scanner placement remains full rendered-frame cadence.\n" +
                "        self.logic:onUpdate()";
            const string after =
                "        " + marker + "\n" +
                "        -- Keep cleanup and seated exit work, but do not enter the\n" +
                "        -- surface scanner while its presentation is already hidden.\n" +
                "        if scanning or inWorkspot or not self.logic.workspotsHidden then\n" +
                "            self.logic:onUpdate()\n" +
                "        end";
            var normalized = live.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
            if (!normalized.Contains("and not world.hasVisibleState() then", StringComparison.Ordinal)
                || !normalized.Contains("local scanning = self.logic.isScanning or self.runtimeData.forceScan", StringComparison.Ordinal)
                || normalized.Split(before, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("SitAnywhere deployed PASS4 frame boundary changed.");
            var patched = normalized.Replace(before, after, StringComparison.Ordinal);
            context.Write(live, live.Text.Contains("\r\n", StringComparison.Ordinal)
                ? patched.Replace("\n", "\r\n", StringComparison.Ordinal) : patched);
            return SemanticInjectionResult.Success("SitAnywhere skips already-hidden non-scanner surface processing; active scanning, seated states and cleanup remain.");
        }

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

        logicText = SitAnywhereLogicWakeSource(logicText);

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

        var fallbackOpening = indent + registrar + "(\"onUpdate\", function(dt)\n";
        var fallbackClosing = indent + "end)";
        if (!replacementUpdate.StartsWith(fallbackOpening, StringComparison.Ordinal) ||
            !replacementUpdate.EndsWith(fallbackClosing, StringComparison.Ordinal))
            throw new InvalidOperationException("SitAnywhere generated frame callback cannot be extracted safely.");
        // Compose the definitive frame-worker stage from pristine source in the
        // SAME source transformation, not as a repair of an earlier PASS4 ZIP.
        var authoredFrameBody = replacementUpdate[
            fallbackOpening.Length..^fallbackClosing.Length];
        var composedFrame = indent + "local function runSitAnywhereFrame(dt)\n" +
            authoredFrameBody + indent + "end\n\n";
        var schedulerStage = """
    -- G-CET semantic:sitanywhere-v1
    TryAttachSitAnywherePass4 = function()
        if not sitAnywhereBaseInitialized then return false end
        if pass4Attached then return true end
        if pass4AttachInProgress then return false end
        pass4AttachInProgress = true
        local created = {}
        local okAttach = pcall(function()
            pass4FrameJob = require("GCET_Semantic/sitanywhere_engine_worker").attach(
                created, runSitAnywhereFrame)
            pass4Attached = true
        end)
        pass4AttachInProgress = false
        if not okAttach then
            for _, handle in ipairs(created) do
                if handle and type(handle.Cancel) == "function" then
                    pcall(handle.Cancel)
                end
            end
            return false
        end
        return true
    end

""";
        var bootstrap = indent + registrar + "(\"onUpdate\", function(dt)\n" +
            indent + "    if pass4Attached then return end\n" +
            indent + "    if not sitAnywhereBaseInitialized then return end\n" +
            indent + "    if TryAttachSitAnywherePass4() then return end\n" +
            indent + "    runSitAnywhereFrame(dt)\n" +
            indent + "end)";
        var initText = init.Text[..selected.Index] +
            composedFrame + schedulerStage + bootstrap +
            init.Text[(selected.Index + selected.Length)..];
        var originalCrLf = init.Text.Contains("\r\n", StringComparison.Ordinal);
        initText = initText.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string initAnchor = "function sit:new()\n";
        const string initialization = "        self.logic:init()\n";
        if (initText.Split(initAnchor, StringSplitOptions.None).Length != 2 ||
            initText.Split(initialization, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("SitAnywhere owner initialization boundaries changed.");
        initText = initText.Replace(initAnchor,
            initAnchor +
            "    local pass4Attached = false\n" +
            "    local pass4AttachInProgress = false\n" +
            "    local sitAnywhereBaseInitialized = false\n" +
            "    local TryAttachSitAnywherePass4 = nil\n" +
            "    local pass4FrameJob = nil\n",
            StringComparison.Ordinal);
        initText = initText.Replace(initialization,
            initialization +
            "        sitAnywhereBaseInitialized = true\n" +
            "        TryAttachSitAnywherePass4()\n", StringComparison.Ordinal);
        context.WriteCoordinator("sitanywhere_engine_worker.lua", SitAnywhereWorkerV2());
        // All edited files are part of ONE authoritative sitanywhere rule,
        // including the source-owned worker. Keep atomic ownership proof intact.
        const string ownedMarker = "-- G-CET semantic:sitanywhere\n";
        static string MarkOwned(string source, string marker) =>
            source.StartsWith(marker, StringComparison.Ordinal)
                ? source : marker + source;
        initText = SitAnywhereRealtimeWakeSource(initText);
        context.Write(init, MarkOwned(originalCrLf
            ? initText.Replace("\n", "\r\n", StringComparison.Ordinal) : initText,
            originalCrLf ? ownedMarker.Replace("\n", "\r\n", StringComparison.Ordinal) : ownedMarker));
        context.Write(cron, MarkOwned(cronText, ownedMarker));
        var preserved = PreserveSitAnywhereSourceFastpaths(context, logicText, worldText);
        logicText = preserved.Logic;
        worldText = preserved.World;
        context.Write(logic, MarkOwned(logicText, ownedMarker));
        context.Write(world, MarkOwned(worldText, ownedMarker));
        return SemanticInjectionResult.Success(
            "Made Sit Anywhere hard-dormant when scanner/workspot/transition/timer/hub/mappin state is fully idle, while preserving rendered-frame cadence for live scanner, world, timer and workspot work.");
    }


    // Integral to the authoritative SitAnywhere first-pass generator.
    // These source-local fastpaths do not own a second scheduler and are
    // independently upgradeable on a partially optimized installation.
    private static (string Logic,string World,bool LoggerChanged) PreserveSitAnywhereSourceFastpaths(
        SemanticPatchContext context, string logicInput, string worldInput)
    {
        static string NL(string source) => source.Replace("\r\n", "\n", StringComparison.Ordinal);
        static string Restore(string original,string value) =>
            original.Contains("\r\n",StringComparison.Ordinal)
                ? value.Replace("\n","\r\n",StringComparison.Ordinal) : value;
        static string ReplaceUnique(string input,string before,string after,string stage)
        {
            if (input.Split(before,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("SitAnywhere "+stage+" source drift.");
            return input.Replace(before,after,StringComparison.Ordinal);
        }
        static string Method(string input,string signature,Func<string,string> transform)
        {
            var expr=new Regex(@"(?m)^"+Regex.Escape(signature)+
                @"[^\n]*\n[\s\S]*?^end[ \t]*(?=\n|\z)",
                RegexOptions.CultureInvariant);
            var found=expr.Matches(input);
            if(found.Count==0)
            {
                if(input.Contains(signature,StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere "+signature+" boundary drift.");
                return input; // Older supported author source may omit this method.
            }
            if(found.Count!=1)
                throw new InvalidOperationException("SitAnywhere "+signature+" ambiguous.");
            var m=found[0];
            var result=transform(m.Value);
            return input[..m.Index]+result+input[(m.Index+m.Length)..];
        }
        var l=NL(logicInput);
        var w=NL(worldInput);
        var needsLogger = l.Contains("function logic:getHit(",StringComparison.Ordinal) ||
            l.Contains("function logic:getTargetLocation(",StringComparison.Ordinal);
        if(needsLogger)
        {
            l=Method(l,"function logic:isValidEnviroment()",body=>
            {
                if(body.Contains("local player = GetPlayer()",StringComparison.Ordinal))
                {
                    if(!body.Contains("local psm = player:GetPlayerStateMachineBlackboard()",StringComparison.Ordinal) ||
                       !body.Contains("if not player then return false end",StringComparison.Ordinal))
                        throw new InvalidOperationException("SitAnywhere partial environment caching.");
                    return body;
                }
                const string zone="local zone = FromVariant(GetPlayer():GetPlayerStateMachineBlackboard():GetVariant(GetAllBlackboardDefs().PlayerStateMachine.SecurityZoneData))";
                if(!body.Contains(zone,StringComparison.Ordinal) ||
                   !body.Contains("GetPlayer():GetHudManager():IsBraindanceActive()",StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere environment control flow changed.");
                body=body.Replace(zone,
                    "local player = GetPlayer()\n    if not player then return false end\n"+
                    "    local defs = GetAllBlackboardDefs()\n"+
                    "    local psm = player:GetPlayerStateMachineBlackboard()\n"+
                    "    if not psm then return false end\n"+
                    "    local zone = FromVariant(psm:GetVariant(defs.PlayerStateMachine.SecurityZoneData))",
                    StringComparison.Ordinal);
                body=body.Replace("GetPlayer():GetPlayerStateMachineBlackboard()", "psm",
                    StringComparison.Ordinal);
                body=body.Replace("GetAllBlackboardDefs()", "defs",StringComparison.Ordinal);
                // The newly generated local declarations are restored explicitly.
                body=body.Replace("local defs = defs", "local defs = GetAllBlackboardDefs()",StringComparison.Ordinal)
                         .Replace("local psm = psm","local psm = player:GetPlayerStateMachineBlackboard()",StringComparison.Ordinal);
                body=body.Replace("GetPlayer()", "player",StringComparison.Ordinal)
                         .Replace("local player = player", "local player = GetPlayer()",StringComparison.Ordinal);
                return body;
            });
            l=Method(l,"function logic:getTargetLocation()",body=>
            {
                if(!body.Contains("local presetHit = nil",StringComparison.Ordinal))
                    body=ReplaceUnique(body,
                        "    local presetHit = self:getHit(\"preset\")",
                        "    local presetHit = nil\n    if not groupHit then presetHit = self:getHit(\"preset\") end",
                        "optional preset raycast");
                if(!body.Contains("if logger.isActive() then",StringComparison.Ordinal))
                    body=ReplaceUnique(body,
                        "    logger.log(\"Lean yaw\", normal:ToRotation().yaw)\n"+
                        "    logger.log(\"Player yaw\", GetPlayer():GetWorldForward():ToRotation().yaw + 180)",
                        "    if logger.isActive() then\n"+
                        "        logger.log(\"Lean yaw\", normal:ToRotation().yaw)\n"+
                        "        logger.log(\"Player yaw\", GetPlayer():GetWorldForward():ToRotation().yaw + 180)\n"+
                        "    end","lazy target diagnostics");
                return body;
            });
            l=Method(l,"function logic:getHit(castType)",body=>
            {
                if(body.Contains("local valid = result:IsValid()",StringComparison.Ordinal))
                {
                    if(!body.Contains("if not valid then return end",StringComparison.Ordinal) ||
                       !body.Contains("local resultPosition = Vector4.Vector3To4(result.position)",StringComparison.Ordinal) ||
                       body.IndexOf("if not valid then return end",StringComparison.Ordinal)>
                       body.IndexOf("local resultPosition = Vector4.Vector3To4(result.position)",StringComparison.Ordinal))
                        throw new InvalidOperationException("SitAnywhere invalid partial raycast validation.");
                    return body;
                }
                if(!body.Contains("if not result:IsValid() then return end",StringComparison.Ordinal) ||
                   !body.Contains("logger.log(\"Cast normal pitch\",",StringComparison.Ordinal) ||
                   !body.Contains("GetObjectClosestToCrosshair(",StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere raycast author behavior changed.");
                var camera=Regex.Match(body,
                    @"(?<getter>(?:Game\.GetCameraSystem|__gcetGetCameraSystem)\(\)):GetActiveCameraForward\(\)");
                if(!camera.Success)
                    throw new InvalidOperationException("SitAnywhere camera getter ownership changed.");
                var getter=camera.Groups["getter"].Value;
                body=ReplaceUnique(body,
                    "    local playerHead = utils.addVector(GetPlayer():GetWorldPosition(), Vector4.new(0, 0, 1.6, 0))\n"+
                    "    local from = utils.addVector(playerHead, utils.multVector("+getter+":GetActiveCameraForward(), forwardOffset))\n"+
                    "    local to = utils.addVector(playerHead, utils.multVector("+getter+":GetActiveCameraForward(), 100))",
                    "    local player = GetPlayer()\n    if not player then return end\n"+
                    "    local playerPosition = player:GetWorldPosition()\n"+
                    "    local cameraForward = "+getter+":GetActiveCameraForward()\n"+
                    "    local playerHead = utils.addVector(playerPosition, Vector4.new(0, 0, 1.6, 0))\n"+
                    "    local from = utils.addVector(playerHead, utils.multVector(cameraForward, forwardOffset))\n"+
                    "    local to = utils.addVector(playerHead, utils.multVector(cameraForward, 100))",
                    "cached raycast setup");
                var oldLogs=
                    "    logger.log(\"Cast castType\", castType)\n"+
                    "    logger.log(\"Cast succes\", result:IsValid())\n"+
                    "    logger.log(\"Cast distance\", Vector4.Vector3To4(result.position):Distance(GetPlayer():GetWorldPosition()))\n"+
                    "    logger.log(\"Cast yDist\", Vector4.Vector3To4(result.position).z - GetPlayer():GetWorldPosition().z)\n"+
                    "    logger.log(\"Cast normal pitch\", math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch))\n\n"+
                    "    if not result:IsValid() then return end -- No hit";
                var newLogs=
                    "    local valid = result:IsValid()\n"+
                    "    if logger.isActive() then\n"+
                    "        logger.log(\"Cast castType\", castType)\n"+
                    "        logger.log(\"Cast succes\", valid)\n"+
                    "    end\n"+
                    "    if not valid then return end -- No hit\n\n"+
                    "    local resultPosition = Vector4.Vector3To4(result.position)\n"+
                    "    local resultNormal = Vector4.Vector3To4(result.normal)\n"+
                    "    if logger.isActive() then\n"+
                    "        logger.log(\"Cast distance\", resultPosition:Distance(playerPosition))\n"+
                    "        logger.log(\"Cast yDist\", resultPosition.z - playerPosition.z)\n"+
                    "        logger.log(\"Cast normal pitch\", math.abs(resultNormal:ToRotation().pitch))\n"+
                    "    end";
                body=ReplaceUnique(body,oldLogs,newLogs,"raycast validation before debug");
                body=ReplaceUnique(body,
                    "Vector4.Vector3To4(result.position):Distance(GetPlayer():GetWorldPosition()) > 2.5",
                    "resultPosition:Distance(playerPosition) > 2.5","distance cache");
                body=ReplaceUnique(body,
                    "local yDist = Vector4.Vector3To4(result.position).z - GetPlayer():GetWorldPosition().z",
                    "local yDist = resultPosition.z - playerPosition.z","vertical cache");
                body=ReplaceUnique(body,
                    "if math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) < 80 or math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) > 95 then -- Try custom normals, given ones are sometimes faulty",
                    "local normalPitch = math.abs(resultNormal:ToRotation().pitch)\n"+
                    "    if normalPitch < 80 or normalPitch > 95 then -- Try custom normals, given ones are sometimes faulty","normal pitch cache");
                body=ReplaceUnique(body,
                    "        logger.log(\"Custom normal pitch\", math.abs(normal:ToRotation().pitch))\n"+
                    "        logger.log(\"Custom normal z1\", p1.position.z)\n"+
                    "        logger.log(\"Custom normal z2\", p2.position.z)\n"+
                    "        logger.log(\"Custom normal z3\", p3.position.z)",
                    "        if logger.isActive() then\n"+
                    "            logger.log(\"Custom normal pitch\", math.abs(normal:ToRotation().pitch))\n"+
                    "            logger.log(\"Custom normal z1\", p1.position.z)\n"+
                    "            logger.log(\"Custom normal z2\", p2.position.z)\n"+
                    "            logger.log(\"Custom normal z3\", p3.position.z)\n"+
                    "        end","custom normal logging");
                body=body.Replace("Vector4.Vector3To4(result.normal):ToRotation().pitch) < 80",
                    "normal:ToRotation().pitch) < 80",StringComparison.Ordinal)
                    .Replace("Vector4.Vector3To4(result.normal):ToRotation().pitch) > 95",
                    "normal:ToRotation().pitch) > 95",StringComparison.Ordinal);
                body=ReplaceUnique(body,
                    "GetObjectClosestToCrosshair(GetPlayer(), TSQ_NPC())",
                    "GetObjectClosestToCrosshair(player, TSQ_NPC())","NPC proximity player reuse");
                body=ReplaceUnique(body,
                    "target:GetWorldPosition():Distance(Vector4.Vector3To4(result.position))",
                    "target:GetWorldPosition():Distance(resultPosition)","NPC proximity hit reuse");
                return body;
            });
            l=Method(l,"function logic:getForwardNormals(origin, castType)",body=>
            {
                if(body.Contains("local player = GetPlayer()",StringComparison.Ordinal)) return body;
                if(!body.Contains("GetPlayer():GetWorldForward():RotByAngleXY(angle)",StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere angled scan ownership changed.");
                body=ReplaceUnique(body,"    local results = {}",
                    "    local results = {}\n    local player = GetPlayer()\n    if not player then return end",
                    "forward scan player cache");
                return body.Replace("GetPlayer():GetWorldForward():RotByAngleXY(angle)",
                    "player:GetWorldForward():RotByAngleXY(angle)",StringComparison.Ordinal);
            });
            l=Method(l,"function logic:onUpdate()",body=>
            {
                if(body.Contains("if logger.isActive() then",StringComparison.Ordinal)) return body;
                return ReplaceUnique(body,
                    "    logger.log(\"Status\", self.isScanning, self:isValidEnviroment(), self.interface)",
                    "    if logger.isActive() then\n"+
                    "        logger.log(\"Status\", self.isScanning, self:isValidEnviroment(), self.interface)\n"+
                    "    end","expensive per-frame debug arguments");
            });
        }

        var fullWorld=w.Contains("Vector4.Distance(posPlayer, interaction.pos)",StringComparison.Ordinal);
        if(fullWorld)
        {
            w=Method(w,"function world.update()",body=>
            {
                if(body.Contains("local distSq = distanceSquared(",StringComparison.Ordinal))
                    return body;
                if(!body.Contains("local posPlayer = GetPlayer():GetWorldPosition()",StringComparison.Ordinal) ||
                   !body.Contains("local playerForward = GetPlayer():GetWorldForward()",StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere position hotpath changed.");
                body=ReplaceUnique(body,
                    "    local posPlayer = GetPlayer():GetWorldPosition()\n    posPlayer.z = posPlayer.z + 1\n    local playerForward = GetPlayer():GetWorldForward()",
                    "    local player = GetPlayer()\n    if not player then return end\n"+
                    "    local posPlayer = player:GetWorldPosition()\n"+
                    "    posPlayer.z = posPlayer.z + 1\n"+
                    "    local playerForward = player:GetWorldForward()",
                    "world player reuse");
                body=ReplaceUnique(body,
                    "        local update = interaction.shown",
                    "        local update = interaction.shown\n"+
                    "        local distSq = distanceSquared(posPlayer, interaction.pos)\n"+
                    "        local interactionRange = interaction.interactionRange",
                    "world distance once");
                body=ReplaceUnique(body,
                    "Vector4.Distance(posPlayer, interaction.pos) < interaction.interactionRange",
                    "distSq < (interactionRange * interactionRange)",
                    "world look-at range");
                body=ReplaceUnique(body,
                    "Vector4.Distance(posPlayer, interaction.pos) < interaction.iconRange",
                    "distSq < (interaction.iconRange * interaction.iconRange)",
                    "world optional icon range");
                return body;
            });
        }
        else if(w.Contains("local distSq = distanceSquared(",StringComparison.Ordinal))
        {
            if(!w.Contains("local player = GetPlayer()",StringComparison.Ordinal) ||
               !w.Contains("if not player then return end",StringComparison.Ordinal))
                throw new InvalidOperationException("SitAnywhere partial distance scan.");
        }

        if(w.Contains("function world.init()",StringComparison.Ordinal))
        {
            w=Method(w,"function world.init()",body=>
            {
                if(body.Contains("if not pos then pos = mappin:GetWorldPosition() end",StringComparison.Ordinal))
                    return body;
                if(!body.Contains("local pos = mappin:GetWorldPosition()",StringComparison.Ordinal) ||
                   !body.Contains("Vector4.Distance(pos, interaction.pos) < 0.05 and interaction.shown",StringComparison.Ordinal))
                {
                    if(body.Contains("mappin:GetWorldPosition()",StringComparison.Ordinal))
                        throw new InvalidOperationException("SitAnywhere mappin observer behavior drift.");
                    return body;
                }
                body=ReplaceUnique(body,
                    "        local pos = mappin:GetWorldPosition()",
                    "        local pos = nil","defer native mappin position");
                body=ReplaceUnique(body,
                    "            if Vector4.Distance(pos, interaction.pos) < 0.05 and interaction.shown then",
                    "            if interaction.shown then\n"+
                    "                if not pos then pos = mappin:GetWorldPosition() end\n"+
                    "                if distanceSquared(pos, interaction.pos) < 0.0025 then",
                    "observer interaction prefilter");
                var tint=Regex.Match(body,@"(?m)^(?<pad>[ \t]*)this\.iconWidget:SetTintColor\([^\n]*\)\s*$");
                if(!tint.Success)
                    throw new InvalidOperationException("SitAnywhere observer icon write drift.");
                var tail=body[(tint.Index+tint.Length)..];
                if(!tail.StartsWith("\n            end\n        end",StringComparison.Ordinal))
                    throw new InvalidOperationException("SitAnywhere observer loop closure drift.");
                body=body[..(tint.Index+tint.Length)]+
                    "\n                end"+body[(tint.Index+tint.Length)..];
                return body;
            });
        }
        if((w.Contains("local distSq = distanceSquared(",StringComparison.Ordinal) ||
             w.Contains("distanceSquared(pos, interaction.pos)",StringComparison.Ordinal)) &&
           !w.Contains("local function distanceSquared(",StringComparison.Ordinal))
        {
            var i=w.IndexOf("function world.init()",StringComparison.Ordinal);
            if(i<0) throw new InvalidOperationException("SitAnywhere distance helper owner missing.");
            w=w[..i]+
                "local function distanceSquared(a, b)\n"+
                "    local dx = a.x - b.x\n"+
                "    local dy = a.y - b.y\n"+
                "    local dz = a.z - b.z\n"+
                "    return dx * dx + dy * dy + dz * dz\n"+
                "end\n\n"+w[i..];
        }

        var loggerChanged=false;
        if(needsLogger)
        {
            var loggerFile=context.FindFile("modules/logger.lua",
                "function logger.log", "local active");
            var logger=NL(loggerFile.Text);
            if(!logger.Contains("function logger.isActive()",StringComparison.Ordinal))
            {
                logger=ReplaceUnique(logger,
                    "function logger.log(title, ...)",
                    "function logger.isActive()\n    return active\nend\n\n"+
                    "function logger.log(title, ...)","logger active getter");
                loggerChanged=true;
                var output=Restore(loggerFile.Text,logger);
                if(!output.Contains("-- G-CET semantic:sitanywhere",StringComparison.Ordinal))
                    output="-- G-CET semantic:sitanywhere\n"+output;
                context.Write(loggerFile,output);
            }
            else if(!logger.Contains("function logger.isActive()\n    return active\nend",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("SitAnywhere logger completion drift.");
        }

        return (Restore(logicInput,l),Restore(worldInput,w),loggerChanged);
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
    private const string SitAnywhereWorkerV1 = """
-- G-CET semantic coordinator:sitanywhere
-- Author frame logic remains in init.lua; one 0-Engine frame job.
local M = {}
function M.attach(created, onFrame)
    local ok, engine = pcall(GetMod, "0-Engine")
    if not ok or type(engine) ~= "table" or type(engine.Register) ~= "function" then
        error("0-Engine Register API not ready")
    end
    local client = engine.Register("sitAnywhere")
    if type(client) ~= "table" or type(client.Schedule) ~= "table"
        or type(client.Schedule.EveryFrame) ~= "function" then
        error("0-Engine Scheduler API not ready")
    end
    local job = client.Schedule.EveryFrame({
        id = "sitanywhere.realtime", pause = "never"
    }, function(ctx)
        onFrame(ctx.dt or 0)
    end)
    created[#created + 1] = job
    return job
end
return M
""";

    private static string SitAnywhereWorkerV2()
    {
        const string before = """
    local job = client.Schedule.EveryFrame({
        id = "sitanywhere.realtime", pause = "never"
    }, function(ctx)
        onFrame(ctx.dt or 0)
    end)
    created[#created + 1] = job
    return job
""";
        const string after = """
    local job = nil
    local active = true
    job = client.Schedule.EveryFrame({
        id = "sitanywhere.realtime", pause = "never"
    }, function(ctx)
        onFrame(ctx.dt or 0)
        if not required() then job.Pause(); active = false end
    end)
    created[#created + 1] = job
    local probe = client.Schedule.Every(0.10, {
        id = "sitanywhere.wake", pause = "never",
        clock = "game", catchUp = false
    }, function()
        local needed = required()
        if needed and not active then
            job.Resume()
            active = true
        elseif not needed and active then
            job.Pause()
            active = false
        end
    end)
    created[#created + 1] = probe
    return job
""";
        if (!SitAnywhereWorkerV1.Contains(before,StringComparison.Ordinal))
            throw new InvalidOperationException("SitAnywhere worker V1 drift.");
        return SitAnywhereWorkerV1
            .Replace("function M.attach(created, onFrame)",
                "function M.attach(created, onFrame, required)",
                StringComparison.Ordinal)
            .Replace("or type(client.Schedule.EveryFrame) ~= \"function\" then",
                "or type(client.Schedule.EveryFrame) ~= \"function\"\n" +
                "        or type(client.Schedule.Every) ~= \"function\" then",
                StringComparison.Ordinal)
            .Replace(before,after,StringComparison.Ordinal);
    }

    private static string SitAnywhereRealtimeWakeSource(string source)
    {
        const string marker = "local function shouldRunSitAnywhereFrame()";
        if (source.Contains(marker,StringComparison.Ordinal)) return source;
        var t = source.Replace("\r\n","\n",StringComparison.Ordinal);
        const string frame = "local function runSitAnywhereFrame(dt)\n";
        const string probe = """
local function shouldRunSitAnywhereFrame()
    if not sitAnywhereBaseInitialized or not self.logic then return true end
    if self.runtimeData.inMenu or not self.runtimeData.inGame then return false end
    if not self.logic.sittables[0] or not self.logic.sittables[1] then return true end
    if self.logic.isScanning or self.runtimeData.forceScan or
        self.logic:inWorkspot() or self.logic:inTransition() or
        interaction.hubShown or not self.logic.workspotsHidden then return true end
    if type(Cron.HasActiveTimers) ~= "function" or Cron.HasActiveTimers() then return true end
    for _, spot in pairs(self.logic.sittables) do
        local w = spot.workspot
        if w.enableCamera or w.camTransition or w.slide then return true end
    end
    return world.hasVisibleState()
end

""";
        if (t.Split(frame,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("SitAnywhere source-owned frame drift.");
        t=t.Replace(frame,probe+frame,StringComparison.Ordinal);
        const string call="created, runSitAnywhereFrame)";
        if (t.Split(call,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("SitAnywhere worker arguments drift.");
        t=t.Replace(call,"created, runSitAnywhereFrame, shouldRunSitAnywhereFrame)",
            StringComparison.Ordinal);
        const string handle="    local pass4FrameJob = nil\n";
        if (t.Split(handle,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("SitAnywhere worker handle drift.");
        t=t.Replace(handle,handle+
            "    self.__gcetWakeRealtime = function()\n"+
            "        if pass4FrameJob and type(pass4FrameJob.Resume) == \"function\" then\n"+
            "            pass4FrameJob.Resume()\n"+
            "        end\n"+
            "    end\n",StringComparison.Ordinal);
        foreach (var state in new[] {"true","false"})
        {
            var anchor="            self.runtimeData.forceScan = "+state+"\n";
            if (t.Split(anchor,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("SitAnywhere keybind source drift.");
            t=t.Replace(anchor,anchor+
                "            if self.__gcetWakeRealtime then self.__gcetWakeRealtime() end\n",
                StringComparison.Ordinal);
        }
        return source.Contains("\r\n",StringComparison.Ordinal)
            ? t.Replace("\n","\r\n",StringComparison.Ordinal) : t;
    }

    private static string SitAnywhereLogicWakeSource(string source)
    {
        if (source.Contains("self.mod.__gcetWakeRealtime()",StringComparison.Ordinal))
            return source;
        var t=source.Replace("\r\n","\n",StringComparison.Ordinal);
        foreach (var state in new[] {"true","false"})
        {
            var anchor="        self.isScanning = "+state+"\n";
            if (t.Split(anchor,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("SitAnywhere scanner GameUI source drift.");
            t=t.Replace(anchor,anchor+
                "        if self.mod.__gcetWakeRealtime then self.mod.__gcetWakeRealtime() end\n",
                StringComparison.Ordinal);
        }
        return source.Contains("\r\n",StringComparison.Ordinal)
            ? t.Replace("\n","\r\n",StringComparison.Ordinal) : t;
    }

}
