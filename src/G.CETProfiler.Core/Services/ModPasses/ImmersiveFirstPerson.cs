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

        if (initText == init.Text && helperText == helpers.Text &&
            gameSessionText == gameSession.Text)
            return SemanticInjectionResult.Skip(
                "All ImmersiveFirstPerson source stages are already complete.");

        context.Write(init, initText);
        context.Write(helpers, helperText);
        context.Write(gameSession, gameSessionText);
        return SemanticInjectionResult.Success(
            "Preserved rendered-frame camera/height work, but sampled expensive mounted/workspot/inspection/crouch/knockdown probes at 20 Hz using the validated optimized reference. Frame registration and action routing remain generic Resolver responsibilities.");
    }

}
