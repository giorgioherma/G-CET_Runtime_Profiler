using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyImmersiveFirstPerson(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Helpers.RefreshPlayerState",
            "collectPlayerState",
            "CameraCore.Update");

        var helpers = context.FindFile(
            "Modules/Helpers.lua",
            "function Helpers.RefreshPlayerState",
            "GetInspectionComponent",
            "IsActorInWorkspot",
            "StatusEffectSystem.ObjectHasStatusEffectOfType");

        var initText = ReplaceOrSkipCompletedStage(
            init.Text,
            "local function collectPlayerState()\n" +
            "    return Helpers.RefreshPlayerState(cachedPlayerState)\n" +
            "end\n",
            "local function collectPlayerState(delta)\n" +
            "    return Helpers.RefreshPlayerState(cachedPlayerState, delta)\n" +
            "end\n",
            "ImmersiveFirstPerson collectPlayerState delta");

        // Source-specific native -> Lua subscriber dormancy and author FreeLook wake.
        // It belongs to this semantic, not to a generic profiler eligibility gate.
        initText = ExtendImmersiveFirstPersonActionDormancy(initText);

        initText = ReplaceOrSkipCompletedStage(
            initText,
            "        local playerState = isPaused and nil or collectPlayerState()\n",
            "        local playerState = isPaused and nil or collectPlayerState(delta)\n",
            "ImmersiveFirstPerson onUpdate player-state probe");

        var helperText = ReplaceOrSkipCompletedStage(
            helpers.Text,
            "    weaponSlot = nil,\n",
            "    weaponSlot = nil,\n" +
            "    slowProbeElapsed = 1.0,\n" +
            "    slowProbe = {\n" +
            "        mountedVehicle = false,\n" +
            "        knockedStatus = false,\n" +
            "        inspecting = false,\n" +
            "        inWorkspot = false,\n" +
            "        crouching = false,\n" +
            "    },\n",
            "ImmersiveFirstPerson slow-probe session state");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "    session.weaponSlot = nil\n",
            "    session.weaponSlot = nil\n" +
            "    session.slowProbeElapsed = 1.0\n" +
            "    session.slowProbe.mountedVehicle = false\n" +
            "    session.slowProbe.knockedStatus = false\n" +
            "    session.slowProbe.inspecting = false\n" +
            "    session.slowProbe.inWorkspot = false\n" +
            "    session.slowProbe.crouching = false\n",
            "ImmersiveFirstPerson slow-probe reset");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "local function refreshPlayerState(target)\n",
            "local function refreshPlayerState(target, delta)\n",
            "ImmersiveFirstPerson refreshPlayerState signature");

        var hotProbeBlock =
            "    local inspectionComponent = player:GetInspectionComponent()\n" +
            "    local playerState = player:GetPS()\n" +
            "    local mountedVehicle = vehicleState == 0\n" +
            "        and Game['GetMountedVehicle;GameObject'](player)\n" +
            "        or nil\n" +
            "    local knockedDown = detailedLocomotion == 29\n" +
            "        or detailedLocomotion == 31\n" +
            "        or landingState > 1\n" +
            "        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"VehicleKnockdown\")\n" +
            "        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"BikeKnockdown\")\n";

        var sampledProbeBlock =
            "    session.slowProbeElapsed = session.slowProbeElapsed + math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "    if session.slowProbeElapsed >= 0.05 then\n" +
            "        session.slowProbeElapsed = session.slowProbeElapsed % 0.05\n\n" +
            "        local mountedVehicle = vehicleState == 0\n" +
            "            and Game['GetMountedVehicle;GameObject'](player)\n" +
            "            or nil\n" +
            "        session.slowProbe.mountedVehicle = mountedVehicle ~= nil\n\n" +
            "        session.slowProbe.knockedStatus =\n" +
            "            StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"VehicleKnockdown\")\n" +
            "            or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"BikeKnockdown\")\n\n" +
            "        local inspectionComponent = player:GetInspectionComponent()\n" +
            "        session.slowProbe.inspecting = inspectionComponent ~= nil\n" +
            "            and inspectionComponent:GetIsPlayerInspecting() == true\n\n" +
            "        session.slowProbe.inWorkspot = session.workspotSystem ~= nil\n" +
            "            and session.workspotSystem:IsActorInWorkspot(player) == true\n\n" +
            "        local playerState = player:GetPS()\n" +
            "        session.slowProbe.crouching = playerState ~= nil and playerState:IsCrouch() == true\n" +
            "    end\n\n" +
            "    local knockedDown = detailedLocomotion == 29\n" +
            "        or detailedLocomotion == 31\n" +
            "        or landingState > 1\n" +
            "        or session.slowProbe.knockedStatus\n";

        // The live/manual 20 Hz reference uses a named interval constant rather
        // than the inline 0.05 literal. Both forms are already complete; do not
        // require an obsolete G-CET pass or reject that established source.
        // Verify the entire sampled block and the fixed 0.05 interval before
        // recognizing it; a mere marker or variable name is insufficient.
        var namedIntervalStage =
            Regex.IsMatch(helperText,
                @"(?m)^\s*local\s+SLOW_PROBE_INTERVAL\s*=\s*0\.05\s*$",
                RegexOptions.CultureInvariant) &&
            Regex.IsMatch(helperText,
                @"if\s+session\.slowProbeElapsed\s*>=\s*SLOW_PROBE_INTERVAL\s+then",
                RegexOptions.CultureInvariant) &&
            helperText.Contains(
                "session.slowProbeElapsed = session.slowProbeElapsed % SLOW_PROBE_INTERVAL",
                StringComparison.Ordinal) &&
            helperText.Contains("session.slowProbe.mountedVehicle = mountedVehicle ~= nil",
                StringComparison.Ordinal) &&
            helperText.Contains("session.slowProbe.knockedStatus =", StringComparison.Ordinal) &&
            helperText.Contains("session.slowProbe.inspecting = inspectionComponent ~= nil",
                StringComparison.Ordinal) &&
            helperText.Contains("session.slowProbe.inWorkspot = session.workspotSystem ~= nil",
                StringComparison.Ordinal) &&
            helperText.Contains("session.slowProbe.crouching = playerState ~= nil",
                StringComparison.Ordinal) &&
            helperText.Contains("or session.slowProbe.knockedStatus",
                StringComparison.Ordinal) &&
            !helperText.Contains(hotProbeBlock, StringComparison.Ordinal);
        if (!namedIntervalStage)
            helperText = ReplaceOrSkipCompletedStage(
                helperText,
                hotProbeBlock,
                sampledProbeBlock,
                "ImmersiveFirstPerson expensive player-state probes");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "    target.inVehicle = vehicleState ~= 0 or mountedVehicle ~= nil\n",
            "    target.inVehicle = vehicleState ~= 0 or session.slowProbe.mountedVehicle\n",
            "ImmersiveFirstPerson mounted-vehicle cache");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "    target.inspecting = inspectionComponent ~= nil\n" +
            "        and inspectionComponent:GetIsPlayerInspecting() == true\n",
            "    target.inspecting = session.slowProbe.inspecting\n",
            "ImmersiveFirstPerson inspection cache");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "    target.inWorkspot = session.workspotSystem ~= nil\n" +
            "        and session.workspotSystem:IsActorInWorkspot(player) == true\n",
            "    target.inWorkspot = session.slowProbe.inWorkspot\n",
            "ImmersiveFirstPerson workspot cache");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "    target.crouching = playerState ~= nil and playerState:IsCrouch() == true\n",
            "    target.crouching = session.slowProbe.crouching\n",
            "ImmersiveFirstPerson crouch cache");

        helperText = ReplaceOrSkipCompletedStage(
            helperText,
            "function Helpers.RefreshPlayerState(target)\n" +
            "    target = target or {}\n" +
            "    local ok, refreshed = pcall(refreshPlayerState, target)\n",
            "function Helpers.RefreshPlayerState(target, delta)\n" +
            "    target = target or {}\n" +
            "    local ok, refreshed = pcall(refreshPlayerState, target, delta)\n",
            "ImmersiveFirstPerson public RefreshPlayerState signature");

        // Same-invocation native provider reuse: resolve the blackboard system once
        // for UI and PhotoMode. Do not retain the game handle across frames.
        // This is part of the authoritative pristine-source rule, not an
        // additional patch which requires the older optimization first.
        var gameSession = context.FindFile(
            "Modules/GameSession.lua",
            "local function refreshCurrentState()",
            "blackboardDefs.UI_System",
            "blackboardDefs.PhotoMode",
            "menuActive");
        var gameSessionText = ReplaceOrSkipCompletedStage(
            gameSession.Text,
            "\tlocal blackboardUI = Game.GetBlackboardSystem():Get(blackboardDefs.UI_System)\n" +
            "\tlocal blackboardPM = Game.GetBlackboardSystem():Get(blackboardDefs.PhotoMode)",
            "\tlocal __gcetIFPBlackboardSystem = Game.GetBlackboardSystem()\n" +
            "\tlocal blackboardUI = __gcetIFPBlackboardSystem:Get(blackboardDefs.UI_System)\n" +
            "\tlocal blackboardPM = __gcetIFPBlackboardSystem:Get(blackboardDefs.PhotoMode)",
            "ImmersiveFirstPerson single invocation UI and PhotoMode provider");

        // CameraCore's public EvaluateBody still returns a fresh table when
        // called by external consumers. Only the existing internal hot path
        // supplies a reusable output, so nested callers keep author semantics.
        var camera = context.FindFile(
            "Modules/CameraCore.lua",
            "function CameraCore.EvaluateBody(nativePitch, crouching)",
            "local function composeAndWrite(");
        var cameraText = ReuseImmersiveFirstPersonCameraMath(camera.Text);

        if (initText == init.Text && helperText == helpers.Text &&
            gameSessionText == gameSession.Text &&
            cameraText == camera.Text)
            return SemanticInjectionResult.Skip(
                "All ImmersiveFirstPerson source stages are already complete.");

        context.Write(init, initText);
        context.Write(helpers, helperText);
        context.Write(gameSession, gameSessionText);
        if (cameraText != camera.Text)
            context.Write(camera, cameraText);
        return SemanticInjectionResult.Success(
            "Preserved rendered-frame camera/height work, but sampled expensive mounted/workspot/inspection/crouch/knockdown probes at 20 Hz using the validated optimized reference. Frame registration and action routing remain generic Resolver responsibilities.");
    }

    // Source-owned allocation reduction in the authoritative IFP generator:
    // eliminate two fresh Lua tables/frame on the active FPP camera path.
    // CameraCore.EvaluateBody remains public and returns a new table by default;
    // only composeAndWrite supplies a scratch table, never retained by author
    // state or the frame dispatcher. Neutral free-offset values are immutable.
    private static string ReuseImmersiveFirstPersonCameraMath(string source)
    {
        var usesCrLf = source.Contains("\r\n", StringComparison.Ordinal);
        var normalized = usesCrLf ? source.Replace("\r\n", "\n", StringComparison.Ordinal) : source;
        var changed = ReuseImmersiveFirstPersonCameraMathLf(normalized);
        return usesCrLf ? changed.Replace("\n", "\r\n", StringComparison.Ordinal) : changed;
    }

    private static string ReuseImmersiveFirstPersonCameraMathLf(string source)
    {
        const string marker = "__gcetIfpBodyScratchV1";
        const string neutral = "__gcetIfpNeutralFreeV1";
        if (source.Contains(marker, StringComparison.Ordinal) ||
            source.Contains(neutral, StringComparison.Ordinal))
        {
            if (Regex.Matches(source, @"local " + marker + @"\s*=\s*\{\s*\}",
                    RegexOptions.CultureInvariant).Count != 1 ||
                Regex.Matches(source, @"local " + neutral + @"\s*=\s*\{",
                    RegexOptions.CultureInvariant).Count != 1 ||
                !source.Contains("function CameraCore.EvaluateBody(nativePitch, crouching, out)", StringComparison.Ordinal) ||
                !source.Contains("out = out or {}", StringComparison.Ordinal) ||
                !source.Contains("return out", StringComparison.Ordinal) ||
                !source.Contains("local body = CameraCore.EvaluateBody(", StringComparison.Ordinal) ||
                !source.Contains("        context.crouching,", StringComparison.Ordinal) ||
                !source.Contains("        " + marker, StringComparison.Ordinal) ||
                !source.Contains("local freeOffset = " + neutral, StringComparison.Ordinal) ||
                source.Contains("local freeOffset = {", StringComparison.Ordinal))
                throw new InvalidOperationException("IFP CameraCore scratch table stage is incomplete; refusing source drift.");
            return source;
        }

        const string entry = "function CameraCore.EvaluateBody(nativePitch, crouching)\n";
        const string replacement = "function CameraCore.EvaluateBody(nativePitch, crouching, out)\n";
        if (source.Split(entry, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("IFP public body evaluation source changed.");
        var bodyReturn = string.Join("\n", new[] {
            "    return {",
            "        progress = progress,",
            "        lateral = 0.0,",
            "        forward = forward,",
            "        vertical = vertical,",
            "        pitch = pitchCorrection,",
            "    }",
            "end",
            "",
            "local function getFreeLookLimits"
        });
        var outReturn = string.Join("\n", new[] {
            "    out = out or {}",
            "    out.progress = progress",
            "    out.lateral = 0.0",
            "    out.forward = forward",
            "    out.vertical = vertical",
            "    out.pitch = pitchCorrection",
            "    return out",
            "end",
            "",
            "local function getFreeLookLimits"
        });
        if (source.Split(bodyReturn, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("IFP EvaluateBody output structure changed.");
        source = source.Replace(entry, replacement, StringComparison.Ordinal)
                       .Replace(bodyReturn, outReturn, StringComparison.Ordinal);
        const string call = "    local body = CameraCore.EvaluateBody(\n" +
                            "        bodyDriverPitch,\n" +
                            "        context.crouching\n" +
                            "    )";
        const string hot = "    local body = CameraCore.EvaluateBody(\n" +
                           "        bodyDriverPitch,\n" +
                           "        context.crouching,\n" +
                           "        " + marker + "\n" +
                           "    )";
        if (source.Split(call, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("IFP internal EvaluateBody call drifted.");
        source = source.Replace(call, hot, StringComparison.Ordinal);
        const string free = "    local freeOffset = {\n" +
                            "        yaw = 0.0,\n" +
                            "        pitch = 0.0,\n" +
                            "        sideProgress = 0.0,\n" +
                            "        backProgress = 0.0,\n" +
                            "        lateral = 0.0,\n" +
                            "        forward = 0.0,\n" +
                            "        vertical = 0.0,\n" +
                            "        roll = 0.0,\n" +
                            "    }";
        if (source.Split(free, StringSplitOptions.None).Length != 2 ||
            source.Split("local function composeAndWrite(", StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("IFP free-offset source structure changed.");
        source = source.Replace(free, "    local freeOffset = " + neutral, StringComparison.Ordinal);
        var declaration = string.Join("\n", new[] {
            "-- G-CET: reusable internal camera arithmetic only; external APIs still return fresh values.",
            "local " + marker + " = {}",
            "local " + neutral + " = {",
            "    yaw = 0.0, pitch = 0.0, sideProgress = 0.0, backProgress = 0.0,",
            "    lateral = 0.0, forward = 0.0, vertical = 0.0, roll = 0.0,",
            "}",
            "",
        });
        source = source.Replace("local function composeAndWrite(",
            declaration + "local function composeAndWrite(", StringComparison.Ordinal);
        return source;
    }

    internal static bool IsImmersiveFirstPersonActionDormancyComplete(string ownerFolder)
    {
        try
        {
            var source = File.ReadAllText(Path.Combine(ownerFolder, "init.lua"));
            // An old supported camera-only source lacks the authored FreeLook
            // input pipeline; preserve its independent A/B rule completion.
            if (!source.Contains("local function registerPlayerInput(", StringComparison.Ordinal))
                return true;
            return source.Contains("-- __gcetIfpActionDormancyV2", StringComparison.Ordinal) &&
                source.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) &&
                source.Contains("__gcetIfpActionHandle = zeroEngineMod.SubscribeAction", StringComparison.Ordinal) &&
                source.Contains("__gcetIfpActionHandle = zeroEngine.SubscribeAction", StringComparison.Ordinal) &&
                source.Contains("registerInput(\"ifp_freelook\"", StringComparison.Ordinal) &&
                source.Contains("CameraCore.BeginFreeLook(context)", StringComparison.Ordinal) &&
                source.Contains("CameraCore.EndFreeLook(false)", StringComparison.Ordinal) &&
                source.Contains("Observe('PlayerPuppet', 'OnAction'", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Native handoff C: the player's CET FreeLook input and game-session events
    // independently wake the shared action subscriber. Its original native
    // OnAction fallback remains on runtimes lacking the 0-Engine action API.
    private static string ExtendImmersiveFirstPersonActionDormancy(string text)
    {
        const string marker = "-- __gcetIfpActionDormancyV2";
        if (text.Contains(marker, StringComparison.Ordinal))
        {
            if (!text.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) ||
                !text.Contains("__gcetIfpActionHandle = zeroEngineMod.SubscribeAction", StringComparison.Ordinal) ||
                !text.Contains("__gcetIfpActionHandle = zeroEngine.SubscribeAction", StringComparison.Ordinal) ||
                !text.Contains("__gcetIfpSyncActionDormancy()", StringComparison.Ordinal))
                throw new InvalidOperationException("ImmersiveFirstPerson subscriber stage drifted.");
            return text;
        }
        if (text.Contains("__gcetIfpActionHandle", StringComparison.Ordinal) ||
            text.Contains("__gcetIfpSyncActionDormancy", StringComparison.Ordinal))
            throw new InvalidOperationException("ImmersiveFirstPerson subscriber stage is incomplete.");
        // Some older/fixture sources implement only the camera-state scheduler,
        // not the author's current FreeLook action/input pipeline. Preserve
        // their existing independent A/B stages rather than inventing wake.
        if (!text.Contains("local function registerPlayerInput(", StringComparison.Ordinal))
            return text;

        if (!text.Contains("zeroEngineMod.SubscribeAction", StringComparison.Ordinal))
        {
            // The pristine author source has the original CET OnAction
            // observer. Compose the same seven exact actions, in ONE pass.
            var observe = new Regex(
                @"(?ms)^(?<indent>[ \t]*)Observe\(\s*'PlayerPuppet'\s*,\s*'OnAction'\s*,\s*function\s*\(\s*first\s*,\s*second\s*,\s*third\s*\)\s*\r?\n(?<body>.*?)(?<close>^\k<indent>end\s*\))",
                RegexOptions.CultureInvariant | RegexOptions.Multiline);
            var matches = observe.Matches(text);
            if (matches.Count != 1)
                throw new InvalidOperationException("IFP original action observer is missing or ambiguous.");
            var m = matches[0];
            var ind = m.Groups["indent"].Value;
            var body = m.Groups["body"].Value;
            body = ReplaceOnce(body,
                "        local action = cetVersion >= 1.14 and second or first\n" +
                "        local consumer = cetVersion >= 1.14 and third or second\n",
                "",
                "IFP original CET-v1.14 argument adaptation");
            foreach (var name in new[] { "CameraMouseX", "CameraMouseY", "CameraX",
                         "CameraY", "right_stick_x", "right_stick_y", "mouse_left" })
            {
                if (!text.Contains("player:RegisterInputListener(player, \"" + name + "\")",
                    StringComparison.Ordinal))
                    throw new InvalidOperationException("IFP authored action set changed: " + name);
            }
            if (!body.Contains("CameraCore.IsFreeLooking()", StringComparison.Ordinal) ||
                !body.Contains("CameraCore.OnAction(actionName, actionValue)", StringComparison.Ordinal) ||
                !body.Contains("consumeSingleAction", StringComparison.Ordinal) ||
                !body.Contains("CameraCore.AbortFreeLook()", StringComparison.Ordinal))
                throw new InvalidOperationException("IFP CET observer action/consume semantics changed.");
            var replacement =
                ind + "local function zeroEngineOnAction(_, action, consumer)\n" +
                body + ind + "end\n\n" +
                ind + "local zeroEngineOk, zeroEngine = pcall(GetMod, \"0-Engine\")\n" +
                ind + "local zeroEngineMod = zeroEngineOk and type(zeroEngine) == \"table\" and type(zeroEngine.Register) == \"function\" and zeroEngine.Register(\"ImmersiveFirstPerson\") or nil\n" +
                ind + "local zeroEngineActions = { \"CameraMouseX\", \"CameraMouseY\", \"CameraX\", \"CameraY\", \"right_stick_x\", \"right_stick_y\", \"mouse_left\" }\n" +
                ind + "if type(zeroEngineMod) == \"table\" and type(zeroEngineMod.SubscribeAction) == \"function\" then\n" +
                ind + "    zeroEngineMod.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction)\n" +
                ind + "elseif zeroEngineOk and type(zeroEngine) == \"table\" and type(zeroEngine.SubscribeAction) == \"function\" then\n" +
                ind + "    zeroEngine.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction, \"ImmersiveFirstPerson\")\n" +
                ind + "else\n" +
                ind + "    Observe('PlayerPuppet', 'OnAction', function(first, second, third)\n" +
                ind + "        local action = cetVersion >= 1.14 and second or first\n" +
                ind + "        local consumer = cetVersion >= 1.14 and third or second\n" +
                ind + "        zeroEngineOnAction(nil, action, consumer)\n" +
                ind + "    end)\n" + ind + "end";
            text = text[..m.Index] + replacement + text[(m.Index + m.Length)..];
        }

        text = ReplaceOnce(text,
            "local API = {}\n",
            "local API = {}\n\n" +
            " " + marker + ": author CET input and session own wake\n" +
            "local __gcetIfpActionHandle = nil\n" +
            "local __gcetIfpActionState = nil\n" +
            "local function __gcetIfpSyncActionDormancy()\n" +
            "    local handle = __gcetIfpActionHandle\n" +
            "    if type(handle) ~= \"table\" or type(handle.SetActive) ~= \"function\" then return end\n" +
            "    local needed = isLoaded and isEnabled and not isDisabledByApi and not isPaused and CameraCore.IsFreeLooking()\n" +
            "    if needed == __gcetIfpActionState then return end\n" +
            "    if pcall(handle.SetActive, needed) then __gcetIfpActionState = needed end\n" +
            "end\n",
            "IFP action lifecycle storage and subscriber state");
        text = ReplaceOnce(text,
            "zeroEngineMod.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction)",
            "__gcetIfpActionHandle = zeroEngineMod.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction)",
            "IFP registered 0-Engine client subscriber");
        text = ReplaceOnce(text,
            "zeroEngine.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction, \"ImmersiveFirstPerson\")",
            "__gcetIfpActionHandle = zeroEngine.SubscribeAction({ actions = zeroEngineActions }, zeroEngineOnAction, \"ImmersiveFirstPerson\")",
            "IFP registered shared-engine subscriber");
        text = ReplaceOnce(text,
            "    -- Reload All Mods can happen after OnGameAttached.",
            "    __gcetIfpSyncActionDormancy()\n\n" +
            "    -- Reload All Mods can happen after OnGameAttached.",
            "IFP initial subscriber state");
        text = ReplaceOnce(text,
            "        if context then\n            CameraCore.Update(delta, context)\n        end",
            "        if context then\n            CameraCore.Update(delta, context)\n        end\n" +
            "        __gcetIfpSyncActionDormancy()",
            "IFP per-frame CameraCore mode exit/deferred FreeLook wake");
        text = ReplaceOnce(text,
            "                CameraCore.BeginFreeLook(context)\n",
            "                CameraCore.BeginFreeLook(context)\n" +
            "                __gcetIfpSyncActionDormancy()\n",
            "IFP FreeLook CET keydown wake");
        text = ReplaceOnce(text,
            "            CameraCore.EndFreeLook(false)\n",
            "            CameraCore.EndFreeLook(false)\n" +
            "            __gcetIfpSyncActionDormancy()\n",
            "IFP FreeLook CET keyup pause");
        text = ReplaceOnce(text,
            "            CameraCore.AbortFreeLook()\n            return",
            "            CameraCore.AbortFreeLook()\n            __gcetIfpSyncActionDormancy()\n            return",
            "IFP mouse abort pause");
        text = ReplaceOnce(text,
            "    CameraCore.Suspend(\"disabled by API\")\n",
            "    CameraCore.Suspend(\"disabled by API\")\n" +
            "    __gcetIfpSyncActionDormancy()\n",
            "IFP API disable subscriber pause");
        text = ReplaceOnce(text,
            "            CameraCore.Suspend(\"disabled by hotkey\")\n",
            "            CameraCore.Suspend(\"disabled by hotkey\")\n" +
            "            __gcetIfpSyncActionDormancy()\n",
            "IFP author CET enabled hotkey");
        text = ReplaceOnce(text,
            "        CameraCore.Suspend(\"player detached\")\n",
            "        CameraCore.Suspend(\"player detached\")\n" +
            "        __gcetIfpSyncActionDormancy()\n",
            "IFP native player-detach observer pause");
        text = ReplaceOnce(text,
            "        CameraCore.Suspend(\"CET shutdown\")\n",
            "        CameraCore.Suspend(\"CET shutdown\")\n" +
            "        __gcetIfpSyncActionDormancy()\n",
            "IFP shutdown subscriber pause");
        text = ReplaceOnce(text,
            "        Helpers.DetachPlayer()\n    end\nend\n\nlocal function setSessionPaused",
            "        Helpers.DetachPlayer()\n    end\n" +
            "    __gcetIfpSyncActionDormancy()\nend\n\nlocal function setSessionPaused",
            "IFP session loaded/unloaded wake");
        text = ReplaceOnce(text,
            "        RuntimeHeight.MarkDirty()\n    end\nend\n\nfunction API.Enable()",
            "        RuntimeHeight.MarkDirty()\n    end\n" +
            "    __gcetIfpSyncActionDormancy()\nend\n\nfunction API.Enable()",
            "IFP pause/resume wake");
        text = ReplaceOnce(text,
            "            CameraCore.Suspend(\"disabled in overlay\")\n",
            "            CameraCore.Suspend(\"disabled in overlay\")\n" +
            "            __gcetIfpSyncActionDormancy()\n",
            "IFP overlay disabled fallback");
        // This optional author UI toggle exists in modern IFP v2.1.x;
        // the separate original CET key remains the primary wake mechanism.
        // Do not mistake the mouse abort within OnAction for this UI toggle.
        if (text.Contains("if not Config.inner.freeLookEnabled then", StringComparison.Ordinal))
            text = ReplaceOnce(text,
                "                CameraCore.AbortFreeLook()\n",
                "                CameraCore.AbortFreeLook()\n" +
                "                __gcetIfpSyncActionDormancy()\n",
                "IFP overlay FreeLook disabled");
        if (!text.Contains(marker, StringComparison.Ordinal) ||
            !text.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) ||
            !text.Contains("__gcetIfpActionHandle = zeroEngine.SubscribeAction", StringComparison.Ordinal))
            throw new InvalidOperationException("IFP native subscriber C-stage postconditions failed.");
        return text;
    }

}
