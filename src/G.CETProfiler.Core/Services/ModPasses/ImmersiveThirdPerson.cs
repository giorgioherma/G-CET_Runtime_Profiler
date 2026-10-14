using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyImmersiveThirdPerson(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "updateTppMaintenance",
            "updateThirdPersonCamera",
            "updateAutoPerspective",
            "mod.headLookTick");

        var opening = FindOnUpdateOpening(file.Text, "delta");

        var helpers =
            "local __gcetItppSupervisorElapsed = 0.0\n" +
            "local __gcetItppMaintenanceElapsed = 0.0\n\n" +
            "local function __gcetItppHasStandbyWork()\n" +
            "  local ap = state.autoPerspective or {}\n" +
            "  return (ap.userWantsTpp == true and ap.override ~= \"none\")\n" +
            "    or state.quickFppEngaged == true\n" +
            "    or state.pendingSessionTppRestore ~= nil\n" +
            "    or state.pendingSceneTppReapply ~= nil\n" +
            "    or state.pendingFppCleanup ~= nil\n" +
            "    or state.pendingFppRestore == true\n" +
            "    or state.fppRestoreWatchdog ~= nil\n" +
            "    or state.pendingTppRepReassert ~= nil\n" +
            "    or state.pendingMirrorHeadVerify ~= nil\n" +
            "    or state.pendingFacialMute ~= nil\n" +
            "    or state.pendingTppAnimPoke ~= nil\n" +
            "    or state.photoModeWasActive == true\n" +
            "    or state.photoModeHeadRestore ~= nil\n" +
            "    or state.pickupPulseUntil ~= nil\n" +
            "    or state.consumableIdleUntil ~= nil\n" +
            "    or state.consumableGateUntil ~= nil\n" +
            "    or state.consumableGraceUntil ~= nil\n" +
            "end\n\n" +
            opening;

        var text = ReplaceOnce(
            file.Text,
            opening,
            helpers,
            "immersive_third_person cadence declarations");

        var supervisorBlock =
            "  local inMenuNow = isPlayerInAnyMenu()\n" +
            "  if state.menuWasOpen and not inMenuNow then\n" +
            "    mod.clearDigitalMoveLatches(\"menu close\")\n" +
            "  end\n" +
            "  state.menuWasOpen = inMenuNow\n" +
            "  pcall(mod.nativeSettingsSaveTick, delta)\n" +
            "  pcall(mod.pollNativeToggle, delta)\n" +
            "  if (state.frameSeq % 6) == 0 then\n" +
            "    pcall(mod.nativeSettingsComboTick)\n" +
            "  end\n" +
            "  pcall(mod.headLookTick, delta)\n" +
            "  pcall(mod.fallCommitTick, delta)\n" +
            "  if state.pendingFaultNotice then\n" +
            "    state.faultNotifyTimer = (state.faultNotifyTimer or 0) + (delta or 0)\n" +
            "    if state.faultNotifyTimer >= 5.0 then\n" +
            "      state.faultNotifyTimer = 0\n" +
            "      pcall(mod.notifyFaultTick)\n" +
            "    end\n" +
            "  end\n" +
            "  if state.enabled or state.photoModeWasActive then\n" +
            "    state.photoModePollTimer = (state.photoModePollTimer or 0) + (delta or 0)\n" +
            "    if state.photoModePollTimer >= 0.15 then\n" +
            "      state.photoModePollTimer = 0\n" +
            "      local photoModeNow = autoReadPhotoMode()\n" +
            "      if state.photoModeWasActive and not photoModeNow and state.enabled then\n" +
            "        state.photoModeHeadRestore = { at = (state.modClock or 0) + 0.10, passes = 0 }\n" +
            "      end\n" +
            "      state.photoModeWasActive = photoModeNow\n" +
            "    end\n" +
            "  end\n" +
            "  mod.updatePhotoModeHeadRestore()\n\n" +
            "  safeCallQuiet(function() updateSessionGuard(delta) end)\n" +
            "  safeCallQuiet(function() updateAutoPerspective(delta) end)\n";

        var gatedSupervisorBlock =
            "  __gcetItppSupervisorElapsed = __gcetItppSupervisorElapsed + math.max(delta or 0, 0)\n" +
            "  local __gcetItppSupervisorInterval = (state.enabled or __gcetItppHasStandbyWork()) and 0.10 or 0.50\n" +
            "  if __gcetItppSupervisorElapsed >= __gcetItppSupervisorInterval then\n" +
            "    local __gcetItppSupervisorDelta = __gcetItppSupervisorElapsed\n" +
            "    __gcetItppSupervisorElapsed = 0.0\n" +
            "    local inMenuNow = isPlayerInAnyMenu()\n" +
            "    if state.menuWasOpen and not inMenuNow then\n" +
            "      mod.clearDigitalMoveLatches(\"menu close\")\n" +
            "    end\n" +
            "    state.menuWasOpen = inMenuNow\n" +
            "    pcall(mod.nativeSettingsSaveTick, __gcetItppSupervisorDelta)\n" +
            "    pcall(mod.pollNativeToggle, __gcetItppSupervisorDelta)\n" +
            "    pcall(mod.nativeSettingsComboTick)\n" +
            "    pcall(mod.headLookTick, __gcetItppSupervisorDelta)\n" +
            "    if state.pendingFaultNotice then\n" +
            "      state.faultNotifyTimer = (state.faultNotifyTimer or 0) + __gcetItppSupervisorDelta\n" +
            "      if state.faultNotifyTimer >= 5.0 then\n" +
            "        state.faultNotifyTimer = 0\n" +
            "        pcall(mod.notifyFaultTick)\n" +
            "      end\n" +
            "    end\n" +
            "    if state.enabled or state.photoModeWasActive then\n" +
            "      state.photoModePollTimer = (state.photoModePollTimer or 0) + __gcetItppSupervisorDelta\n" +
            "      if state.photoModePollTimer >= 0.15 then\n" +
            "        state.photoModePollTimer = 0\n" +
            "        local photoModeNow = autoReadPhotoMode()\n" +
            "        if state.photoModeWasActive and not photoModeNow and state.enabled then\n" +
            "          state.photoModeHeadRestore = { at = (state.modClock or 0) + 0.10, passes = 0 }\n" +
            "        end\n" +
            "        state.photoModeWasActive = photoModeNow\n" +
            "      end\n" +
            "    end\n" +
            "    mod.updatePhotoModeHeadRestore()\n" +
            "    safeCallQuiet(function() updateSessionGuard(__gcetItppSupervisorDelta) end)\n" +
            "    safeCallQuiet(function() updateAutoPerspective(__gcetItppSupervisorDelta) end)\n" +
            "    guardStep(\"updateDependencyGuard\", updateDependencyGuard, __gcetItppSupervisorDelta)\n" +
            "  end\n" +
            "  pcall(mod.fallCommitTick, delta)\n";

        text = ReplaceOnce(
            text,
            supervisorBlock,
            gatedSupervisorBlock,
            "immersive_third_person supervisor lane");

        text = ReplaceOnce(
            text,
            "  guardStep(\"updateDependencyGuard\", updateDependencyGuard, delta)\n\n" +
            "  if state.enabled then\n" +
            "    guardStep(\"tppMaintenance\", updateTppMaintenance, delta)\n" +
            "  end\n",
            "  if state.enabled then\n" +
            "    __gcetItppMaintenanceElapsed = __gcetItppMaintenanceElapsed + math.max(delta or 0, 0)\n" +
            "    if __gcetItppMaintenanceElapsed >= 0.25 then\n" +
            "      local __gcetMaintenanceDelta = __gcetItppMaintenanceElapsed\n" +
            "      __gcetItppMaintenanceElapsed = 0.0\n" +
            "      guardStep(\"tppMaintenance\", updateTppMaintenance, __gcetMaintenanceDelta)\n" +
            "    end\n" +
            "  else\n" +
            "    __gcetItppMaintenanceElapsed = 0.0\n" +
            "  end\n",
            "immersive_third_person maintenance cadence");

        text = ReplaceOnce(
            text,
            "  guardStep(\"updateCameraTransition\", updateCameraTransition, delta)\n" +
            "  guardStep(\"updateLootAssist\", updateLootAssist, delta)\n" +
            "  guardStep(\"updateThirdPersonCamera\", updateThirdPersonCamera, delta)\n",
            "  if state.enabled or state.cameraTransition then\n" +
            "    guardStep(\"updateCameraTransition\", updateCameraTransition, delta)\n" +
            "    guardStep(\"updateLootAssist\", updateLootAssist, delta)\n" +
            "    guardStep(\"updateThirdPersonCamera\", updateThirdPersonCamera, delta)\n" +
            "  end\n",
            "immersive_third_person camera realtime gate");

        // Realtime camera/transition work remains frame-cadence, but dormant
        // pending-state handlers should not run when their state is absent.
        text = ReplaceOnce(
            text,
            "  pcall(mod.fallCommitTick, delta)\n",
            "  if state.enabled or state.fallCommitSet or state.stepHoldSet or state.stepHoldUntil then\n" +
            "    pcall(mod.fallCommitTick, delta)\n" +
            "  end\n",
            "immersive_third_person fall commit gate");

        var pendingGates = new Dictionary<string, string>
        {
            ["updateHeadGuard"] = "state.enabled or state.headGuardApplied",
            ["updatePendingFppCleanup"] = "state.pendingFppCleanup",
            ["updateFppRestoreWatchdog"] = "state.fppRestoreWatchdog",
            ["updateTppRepReassert"] = "state.pendingTppRepReassert",
            ["updateMirrorHeadVerify"] = "state.pendingMirrorHeadVerify",
            ["updatePostSceneTppReapply"] = "state.pendingSceneTppReapply",
            ["updateFacialMute"] = "state.pendingFacialMute",
            ["updatePendingTppAnimPoke"] = "state.pendingTppAnimPoke",
            ["updateItemPickupPulse"] = "state.enabled or state.pickupPulseUntil",
            ["updateConsumableIdle"] = "state.enabled or state.consumableIdleUntil or state.consumableGateUntil or state.consumableGraceUntil"
        };

        foreach (var pair in pendingGates)
        {
            var call = pair.Key == "updateHeadGuard"
                ? "  guardStep(\"updateHeadGuard\", mod.updateHeadGuard)\n"
                : pair.Key == "updateConsumableIdle"
                    ? "  guardStep(\"updateConsumableIdle\", mod.updateConsumableIdle, delta)\n"
                    : "  guardStep(\"" + pair.Key + "\", " + pair.Key + ", delta)\n";
            var gated =
                "  if " + pair.Value + " then\n" +
                "    " + call.TrimStart() +
                "  end\n";
            text = ReplaceOnce(
                text,
                call,
                gated,
                "immersive_third_person " + pair.Key + " gate");
        }

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept active camera/transition work at rendered-frame cadence, reduced supervisory polling to 10 Hz active/standby and 2 Hz idle, reduced maintenance to 4 Hz, and gated pending-state handlers when their state is absent.");
    }


}
