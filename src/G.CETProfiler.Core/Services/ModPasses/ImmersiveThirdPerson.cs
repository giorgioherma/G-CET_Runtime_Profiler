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

        if (file.Text.Contains("mod.pluginHeartbeat(delta)", StringComparison.Ordinal) &&
            file.Text.Contains("mod.move360CaptureNamed = function", StringComparison.Ordinal))
            return ApplyImmersiveThirdPersonUpdatedLoop(context, file);

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



    
    // Current upstream ITP (2026-10): its update loop contains live API,
    // move360, scene, camera and input clocks. We must NOT reuse the former
    // wholesale supervisor/maintenance throttling on those new dependencies.
    // Only hoist null-state guards already proven as first operation of each
    // owner function; all active work still runs every frame with original delta.

    // The paired wake stage is complete only when BOTH author-owned files
    // expose matching control and read-only capability. Old init-only output
    // must remain upgradeable, never misclassified by a lone marker.
    internal static bool IsImmersiveThirdPersonDedraWakeComplete(string ownerFolder)
    {
        var initPath = Path.Combine(ownerFolder, "init.lua");
        var apiPath = Path.Combine(ownerFolder, "api.lua");
        if (!File.Exists(initPath) || !File.Exists(apiPath))
            return false;
        var init = File.ReadAllText(initPath).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        var api = File.ReadAllText(apiPath).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        return init.Contains("mod.__gcetDedraWakeBridgeV2 = true",StringComparison.Ordinal) &&
               init.Contains("gcetDedraWakeReady = function() return mod.__gcetDedraWakeBridgeV2 == true end",StringComparison.Ordinal) &&
               init.Contains("pcall(addon.__gcetWakeFromBase, \"__gcetItpToggleWakeV2\")",StringComparison.Ordinal) &&
               init.Contains("pcall(mod.__gcetDedraToggleSignalV2)",StringComparison.Ordinal) &&
               init.Contains("registerInput(\"tpp_camera_toggle\"",StringComparison.Ordinal) &&
               !init.Contains("mod.api.__gcetDedraWakeBridgeV2",StringComparison.Ordinal) &&
               api.Contains("-- __gcetDedraWakeCapabilityV3: read-only startup proof",StringComparison.Ordinal) &&
               api.Contains("return type(host.gcetDedraWakeReady) == \"function\" and host.gcetDedraWakeReady() == true",StringComparison.Ordinal) &&
               api.Contains("gcetDedraWakeReady = function()",StringComparison.Ordinal);
    }

    // A synchronous, read-only readiness proof solves CET onInit ordering:
    // Dedra can find that the original base toggle owns the wake signal even
    // if the one-shot base onInit notification ran before its own onInit.
    // Export from the author API builder BEFORE its read-only proxy is built;
    // never write a flag onto the public proxy or register another input.
    private static bool EnsureImmersiveThirdPersonDedraWakeCapability(
        SemanticPatchContext context, ref string source)
    {
        const string hostAnchor = "    state = state,\n    system = function()";
        const string hostLine =
            "    gcetDedraWakeReady = function() return mod.__gcetDedraWakeBridgeV2 == true end,\n";
        var changed = false;
        if (!source.Contains(hostLine, StringComparison.Ordinal))
        {
            if (source.Split(hostAnchor, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("ITP API host construction drift; no Dedra wake capability.");
            source = source.Replace(hostAnchor,
                "    state = state,\n" + hostLine + "    system = function()",
                StringComparison.Ordinal);
            changed = true;
        }
        else if (source.Split(hostLine, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("Duplicate ITP Dedra wake capability host.");

        var apiFile = context.FindFile("api.lua",
            "core.public = readOnly(touching({",
            "events = readOnly(touching(events)),");
        var api = apiFile.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r','\n');
        const string marker = "-- __gcetDedraWakeCapabilityV3: read-only startup proof";
        const string apiAnchor = "    events = readOnly(touching(events)),\n";
        const string apiAdd =
            "    gcetDedraWakeReady = function()\n" +
            "      return type(host.gcetDedraWakeReady) == \"function\" and host.gcetDedraWakeReady() == true\n" +
            "    end,\n";
        if (api.Contains(marker, StringComparison.Ordinal))
        {
            if (api.Split(apiAdd, StringSplitOptions.None).Length != 2 ||
                api.Split(marker, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("ITP read-only wake capability is partial or drifted.");
        }
        else
        {
            if (api.Split(apiAnchor, StringSplitOptions.None).Length != 2 ||
                api.Contains("gcetDedraWakeReady", StringComparison.Ordinal))
                throw new InvalidOperationException("ITP public API table drift; refuse new wake capability.");
            api = api.Replace(apiAnchor, apiAnchor + "    " + marker + "\n" + apiAdd,
                StringComparison.Ordinal);
            if (apiFile.Text.Contains("\r\n", StringComparison.Ordinal))
                api = api.Replace("\n", "\r\n", StringComparison.Ordinal);
            context.Write(apiFile, api);
            changed = true;
        }
        return changed;
    }

    // Signal the combat add-on from the base's ORIGINAL CET toggle. This does
    // not register F7, interpret the current camera view, or force a switch:
    // a user request can remain deferred during a combat/scene override.
    private static SemanticInjectionResult ApplyImmersiveThirdPersonDedraToggleSignal(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function autoTppHandleToggle()",
            "registerInput(\"tpp_camera_toggle\"",
            "pcall(autoTppHandleToggle)");
        var src=file.Text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        const string marker="__gcetDedraToggleSignalV2";
        // GetMod("immersive_third_person") returns a read-only public API.
        // The base instead proves wake support through Dedra's mutable worker.
        // This one producer supports pristine and previous partial output.
        const string apiAnchor = "mod.api = mod.apiCore and mod.apiCore.public or nil";
        // ITP's exported public API is a read-only proxy. Never mutate it.
        const string oldApiBridge = "\nif type(mod.api) == \"table\" then\n" +
            "  mod.api.__gcetDedraWakeBridgeV2 = mod.__gcetDedraWakeBridgeV2 == true\nend";
        const string oldProofCall = "pcall(addon.__gcetWakeFromBase)";
        const string proofCall = "pcall(addon.__gcetWakeFromBase, \"__gcetItpToggleWakeV2\")";
        const string initAnchor = "registerForEvent(\"onInit\", function()\n  mod.captureConsumableTiming()";
        const string initSignal = "registerForEvent(\"onInit\", function()\n" +
            "  pcall(mod.__gcetDedraToggleSignalV2)\n  mod.captureConsumableTiming()";
        if(src.Contains(marker,StringComparison.Ordinal))
        {
            if(!src.Contains("pcall(mod.__gcetDedraToggleSignalV2)",StringComparison.Ordinal) ||
               !src.Contains("mod.__gcetDedraWakeBridgeV2 = true",StringComparison.Ordinal))
                throw new InvalidOperationException("Incomplete base ITP toggle signal.");
            if(src.Split(apiAnchor,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("ITP public API assignment changed.");
            var before=src;
            // Upgrade exactly our earlier generated source through the same
            // authoritative producer. Unknown drift remains a hard refusal.
            if(src.Contains("mod.api.__gcetDedraWakeBridgeV2",StringComparison.Ordinal))
            {
                if(src.Split(oldApiBridge,StringSplitOptions.None).Length!=2)
                    throw new InvalidOperationException("Old ITP API bridge drifted.");
                src=src.Replace(oldApiBridge,"",StringComparison.Ordinal);
            }
            if(src.Contains(oldProofCall,StringComparison.Ordinal))
            {
                if(src.Split(oldProofCall,StringSplitOptions.None).Length!=2)
                    throw new InvalidOperationException("Old Dedra signal drifted.");
                src=src.Replace(oldProofCall,proofCall,StringComparison.Ordinal);
            }
            if(!src.Contains(proofCall,StringComparison.Ordinal))
                throw new InvalidOperationException("ITP wake capability proof missing.");
            if(!src.Contains(initSignal,StringComparison.Ordinal))
            {
                if(src.Split(initAnchor,StringSplitOptions.None).Length!=2)
                    throw new InvalidOperationException("ITP onInit lifecycle drifted.");
                src=src.Replace(initAnchor,initSignal,StringComparison.Ordinal);
            }
            var capabilityChanged = EnsureImmersiveThirdPersonDedraWakeCapability(context, ref src);
            if(src==before && !capabilityChanged)
                return SemanticInjectionResult.Skip("Read-only-safe ITP wake already present.");
            if (src != before)
                context.Write(file,file.Text.Contains("\r\n",StringComparison.Ordinal)
                    ? src.Replace("\n","\r\n",StringComparison.Ordinal):src);
            return SemanticInjectionResult.Success("Upgraded ITP wake and read-only startup proof.");
        }
        const string binding =
            "registerInput(\"tpp_camera_toggle\", TEXT.bindToggle[1], function(keypress)\n" +
            "  if not keypress then\n" +
            "    return\n" +
            "  end\n\n" +
            "  pcall(autoTppHandleToggle)\n" +
            "end)";
        if(src.Split(binding,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Base ITP CET toggle callback changed; no safe addon signal.");
        const string helper = """
        -- __gcetDedraToggleSignalV2: original user request may precede camera switch.
        mod.__gcetDedraWakeBridgeV2 = true
        mod.__gcetDedraToggleSignalV2 = function()
          local ok, addon = pcall(GetMod, "DedraThirdPersonCombatAddon")
          if ok and type(addon) == "table" and
             type(addon.__gcetWakeFromBase) == "function" then
            pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")
          end
        end

        """;
        var update=binding.Replace("  pcall(autoTppHandleToggle)\n",
            "  pcall(autoTppHandleToggle)\n" +
            "  pcall(mod.__gcetDedraToggleSignalV2)\n",StringComparison.Ordinal);
        src=src.Replace(binding,helper+update,StringComparison.Ordinal);
        // UI toggle uses the SAME author function, not a second keybind.
        const string ui="    autoTppHandleToggle()\n";
        var menu=src.IndexOf("if UI.wideButton(UI.icon(UI.ICONS.swap)",StringComparison.Ordinal);
        if(menu>=0)
        {
            var toggle=src.IndexOf(ui,menu,StringComparison.Ordinal);
            if(toggle>=0 && toggle-menu<400)
                src=src[..toggle]+ui+"    pcall(mod.__gcetDedraToggleSignalV2)\n"+src[(toggle+ui.Length)..];
        }
        if(src.Split(apiAnchor,StringSplitOptions.None).Length!=2 ||
           src.Split(initAnchor,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("ITP public API/onInit lifecycle changed.");
        src=src.Replace(initAnchor,initSignal,StringComparison.Ordinal);
        EnsureImmersiveThirdPersonDedraWakeCapability(context, ref src);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            src=src.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,src);
        return SemanticInjectionResult.Success(
            "Original CET and UI toggles plus onInit directly signal Dedra, " +
            "preserving the read-only public API and deferred camera wake.");
    }

    private static SemanticInjectionResult ApplyImmersiveThirdPersonUpdatedLoop(
        SemanticPatchContext context, SemanticTextFile file)
    {
        var text = file.Text;
        const string stage = "__gcetItppUpdatedPendingGatesV3";
        if (text.Contains(stage, StringComparison.Ordinal))
        {
            if (!text.Contains("if state.pendingFppCleanup then", StringComparison.Ordinal) ||
                !text.Contains("if state.pendingTppRepReassert then", StringComparison.Ordinal) ||
                !text.Contains("if state.pendingMirrorHeadVerify then", StringComparison.Ordinal) ||
                !text.Contains("if state.pendingFacialMute then", StringComparison.Ordinal) ||
                !text.Contains("if state.pendingTppAnimPoke then", StringComparison.Ordinal) ||
                !text.Contains("if state.pendingFppRestore then", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Updated third-person pending-state gates are incomplete.");
            var upgraded = ApplyImmersiveThirdPersonDetachedDormancy(context,
                ReuseImmersiveThirdPersonLatches(
                    ReuseImmersiveThirdPersonLiteralIds(
                        AddImmersiveThirdPersonFppFastpaths(text))), out var recoveredWorker);
            if (upgraded == text && !recoveredWorker)
                return SemanticInjectionResult.Skip(
                    "Current ITP pending-state and first-person fastpaths already applied.");
            // When only the external worker is missing, generate only that
            // worker; the already-proven author bridge is left byte-identical.
            if (upgraded != text)
                context.Write(file, upgraded);
            return SemanticInjectionResult.Success(
                recoveredWorker && upgraded == text
                    ? "Recovered missing ITP frame worker, preserving the completed author source."
                    : "Upgraded existing ITP source with safe first-person no-work call guards.");
        }

        if (!text.Contains("mod.move360CaptureNamed = function", StringComparison.Ordinal) ||
            !text.Contains("mod.apiTick, delta", StringComparison.Ordinal) ||
            !text.Contains("mod.updateMove360Step, delta", StringComparison.Ordinal) ||
            !text.Contains("mod.updateTppMaintenance, delta", StringComparison.Ordinal) ||
            !text.Contains("mod.pluginHeartbeat(delta)", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Unknown third-person update architecture: fail closed.");

        // Each author function begins with precisely this empty-state return.
        // No Timer, callback or native getter is allowed before that return.
        var gates = new (string Function, string Proof, string State)[]
        {
            ("updatePendingFppCleanup",
                "local function updatePendingFppCleanup(delta)\n  local pending = state.pendingFppCleanup\n  if not pending then",
                "state.pendingFppCleanup"),
            ("updateTppRepReassert",
                "local function updateTppRepReassert(delta)\n  local pending = state.pendingTppRepReassert\n  if not pending then",
                "state.pendingTppRepReassert"),
            ("updateMirrorHeadVerify",
                "local function updateMirrorHeadVerify(delta)\n  local pending = state.pendingMirrorHeadVerify\n  if not pending then",
                "state.pendingMirrorHeadVerify"),
            ("updateFacialMute",
                "local function updateFacialMute(delta)\n  local pending = state.pendingFacialMute\n  if not pending then",
                "state.pendingFacialMute"),
            ("updatePendingTppAnimPoke",
                "local function updatePendingTppAnimPoke(delta)\n  local pending = state.pendingTppAnimPoke\n  if not pending then",
                "state.pendingTppAnimPoke"),
            ("updateFppRestoreWatchdog",
                "local function updateFppRestoreWatchdog(delta)\n  if not state.pendingFppRestore or state.enabled then",
                "state.pendingFppRestore"),
        };
        foreach (var gate in gates)
        {
            if (!text.Contains(gate.Proof, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Third-person null-state early return drift: " + gate.Function);
            var oldCall = "  guardStep(\"" + gate.Function + "\", " +
                          gate.Function + ", delta)\n";
            var newCall = "  if " + gate.State + " then\n" +
                          "    guardStep(\"" + gate.Function + "\", " +
                          gate.Function + ", delta)\n" +
                          "  end\n";
            text = ReplaceOnce(text, oldCall, newCall,
                "Updated third-person " + gate.Function + " source guard");
        }

        // Generic 0-Engine runs before semantic passes and can convert the
        // author registrar into __gcetRegisterEvent_<registrationId>. That
        // changes the entrypoint name, NOT the owned heartbeat/body. Accept
        // exactly one verified original or generated event registration.
        var updateOwners = Regex.Matches(text,
            @"(?m)^[ \t]*(?<registrar>registerForEvent|__gcetRegisterEvent_[0-9]+)\((?<quote>['""])onUpdate\k<quote>,[ \t]*function\(delta\)\r?\n[ \t]*mod\.pluginHeartbeat\(delta\)",
            RegexOptions.CultureInvariant);
        if(updateOwners.Count!=1)
            throw new InvalidOperationException(
                "Updated third-person onUpdate owner marker: expected one author/generic frame entrypoint.");
        var owner=updateOwners[0].Groups["registrar"].Value;
        if(owner.StartsWith("__gcetRegisterEvent_",StringComparison.Ordinal) &&
           !text.Contains("local "+owner+" = registerForEvent",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Updated third-person generic registrar has no source-owned fallback.");
        text = text.Insert(updateOwners[0].Index,
            "-- " + stage + ": author-verified absent-state fast guards only\n");

        text = ApplyImmersiveThirdPersonDetachedDormancy(context,
            ReuseImmersiveThirdPersonLatches(
                ReuseImmersiveThirdPersonLiteralIds(
                    AddImmersiveThirdPersonFppFastpaths(text))), out _);
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Current ITP API, movement, camera, heartbeat, native toggle and " +
            "TPP maintenance remain original per-frame work. Six pending " +
            "handlers and five source-proven idle no-work calls are guarded.");
    }

    // Static source-inspected TweakDBID literals are immutable identifiers.
    // Keep the existing author wrapper as their ONLY constructor/fallback and
    // retain dynamic strings as uncached live lookups. This is part of the
    // existing updated ITP source generator; it adds no new callback/ticker.
    private static string ReuseImmersiveThirdPersonLiteralIds(string source)
    {
        var usesCrLf = source.Contains("\r\n", StringComparison.Ordinal);
        var normalized = usesCrLf ? source.Replace("\r\n", "\n", StringComparison.Ordinal) : source;
        var changed = ReuseImmersiveThirdPersonLiteralIdsLf(normalized);
        return usesCrLf ? changed.Replace("\n", "\r\n", StringComparison.Ordinal) : changed;
    }

    private static string ReuseImmersiveThirdPersonLiteralIdsLf(string source)
    {
        const string cache = "__gcetItppLiteralTweakIdsV1";
        const string current = "local function makeTweakDBID(value)\n" +
            "  if TweakDBID and TweakDBID.new then\n" +
            "    return TweakDBID.new(value)\n" +
            "  end\n\n" +
            "  return value\n" +
            "end";
        // Fixed-value callers may shift between supported author revisions,
        // but their set is bounded by explicit literals in the CURRENT source.
        var literals = Regex.Matches(source,
            @"\bmakeTweakDBID\s*\(\s*(?<quote>['""])(?<id>(?:AttachmentSlots|BaseStatusEffect)\.[A-Za-z0-9_]+)\k<quote>\s*\)",
            RegexOptions.CultureInvariant)
            .Select(x => x.Groups["id"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (source.Contains(cache, StringComparison.Ordinal))
        {
            if (Regex.Matches(source, @"\blocal\s+" + cache + @"\s*=\s*\{\s*\}").Count != 1 ||
                !source.Contains("local id = " + cache + "[value]", StringComparison.Ordinal) ||
                !source.Contains(cache + "[value] = id", StringComparison.Ordinal) ||
                !source.Contains("if __gcetItppLiteralKeySetV1[value] then", StringComparison.Ordinal) ||
                !source.Contains("return TweakDBID.new(value)", StringComparison.Ordinal) ||
                !source.Contains("  return value\nend", StringComparison.Ordinal) ||
                source.Contains(current, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "ITP cached literal TweakDBIDs are partially installed or unsafe.");
            return source;
        }
        if (source.Contains("__gcetItppLiteralKeySetV1", StringComparison.Ordinal))
            throw new InvalidOperationException("ITP TweakDBID key set without cache stage.");
        if (!source.Contains("local function makeTweakDBID(", StringComparison.Ordinal))
            return source; // Historical compact camera architecture.
        if (literals.Length == 0)
            return source; // No static native identifiers on this author revision.
        if (source.Split(current, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException(
                "ITP original TweakDBID fallback owner changed; refusing caching.");

        var keys = string.Join("\n", literals.Select(key => "  [\"" + key + "\"] = true,"));
        var replacement =
            "-- G-CET: source-proven immutable TweakDBID keys, no dynamic caching.\n" +
            "local __gcetItppLiteralKeySetV1 = {\n" + keys + "\n}\n" +
            "local " + cache + " = {}\n" +
            "local function makeTweakDBID(value)\n" +
            "  if TweakDBID and TweakDBID.new then\n" +
            "    if __gcetItppLiteralKeySetV1[value] then\n" +
            "      local id = " + cache + "[value]\n" +
            "      if id == nil then\n" +
            "        id = TweakDBID.new(value)\n" +
            "        " + cache + "[value] = id\n" +
            "      end\n" +
            "      return id\n" +
            "    end\n" +
            "    return TweakDBID.new(value)\n" +
            "  end\n\n" +
            "  return value\n" +
            "end";
        return source.Replace(current, replacement, StringComparison.Ordinal);
    }

    // Source-owned GC reduction: one private table per delay class, populated
    // with current settings on EACH invocation. The original latch equations,
    // branch precedence, source cadence and public state remain untouched.
    private static string ReuseImmersiveThirdPersonLatches(string source)
    {
        var crlf = source.Contains("\r\n", StringComparison.Ordinal);
        var text = crlf ? source.Replace("\r\n", "\n", StringComparison.Ordinal) : source;
        const string marker = "__gcetItppLatchTablesV1";
        const string old = "local function autoUpdateLatches(raw)\n" +
            "  local autoPerspective = state.autoPerspective\n" +
            "  local cfg = config.autoPerspective\n" +
            "  local now = autoPerspective.clock\n" +
            "  local enterDelay = { vehicle = 0, scene = 0, combat = 0, interaction = cfg.interactionEnterDelay or 0.10 }\n" +
            "  local exitDelay = {\n" +
            "    vehicle = cfg.vehicleExitDelay or 0.40,\n" +
            "    scene = cfg.sceneExitDelay or 0.50,\n" +
            "    combat = cfg.combatExitDelay or 1.25,\n" +
            "    interaction = cfg.interactionExitDelay or 1.00\n" +
            "  }\n";
        if (text.Contains(marker, StringComparison.Ordinal))
        {
            if (Regex.Matches(text, @"\blocal\s+" + marker + @"\s*=\s*\{").Count != 1 ||
                !text.Contains("local enterDelay = " + marker + ".enter", StringComparison.Ordinal) ||
                !text.Contains("local exitDelay = " + marker + ".exit", StringComparison.Ordinal) ||
                !text.Contains("enterDelay.interaction = cfg.interactionEnterDelay or 0.10", StringComparison.Ordinal) ||
                !text.Contains("exitDelay.interaction = cfg.interactionExitDelay or 1.00", StringComparison.Ordinal) ||
                !text.Contains("if autoPerspective.combatWeaponOnly then", StringComparison.Ordinal) ||
                !text.Contains("if autoPerspective.sceneQuickExit then", StringComparison.Ordinal) ||
                text.Contains(old, StringComparison.Ordinal))
                throw new InvalidOperationException("ITP latch-table reuse partially installed or author source drifted.");
            return source;
        }
        if (!text.Contains("local function autoUpdateLatches(", StringComparison.Ordinal))
            return source; // Older supported camera without new latch pipeline.
        if (text.Split(old, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("ITP latch delay definitions have changed; refuse rewrite.");
        const string updated =
            "-- G-CET: zero temporary latch tables per camera signal check.\n" +
            "local " + marker + " = { enter = {vehicle = 0, scene = 0, combat = 0, interaction = 0},\n" +
            "    exit = {vehicle = 0, scene = 0, combat = 0, interaction = 0} }\n" +
            "local function autoUpdateLatches(raw)\n" +
            "  local autoPerspective = state.autoPerspective\n" +
            "  local cfg = config.autoPerspective\n" +
            "  local now = autoPerspective.clock\n" +
            "  local enterDelay = " + marker + ".enter\n" +
            "  enterDelay.interaction = cfg.interactionEnterDelay or 0.10\n" +
            "  local exitDelay = " + marker + ".exit\n" +
            "  exitDelay.vehicle = cfg.vehicleExitDelay or 0.40\n" +
            "  exitDelay.scene = cfg.sceneExitDelay or 0.50\n" +
            "  exitDelay.combat = cfg.combatExitDelay or 1.25\n" +
            "  exitDelay.interaction = cfg.interactionExitDelay or 1.00\n";
        text = text.Replace(old, updated, StringComparison.Ordinal);
        return crlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    // V4 is part of the existing ITP update producer, not a correction
    // applied after broken output. Skip only calls whose author functions
    // immediately return under EXACTLY the same source-owned conditions.
    // Input, movement, API, native toggle, auto-perspective and cleanup stay
    // on their original clock and preserve all wake responsibilities.
    private static string AddImmersiveThirdPersonFppFastpaths(string text)
    {
        const string stage = "__gcetItppFppFastpathsV4";
        var gates = new (string Proof, string Call, string Guard)[]
        {
            (
                "mod.updateHeadLookIdleGate = function()\n  if not state.enabled then return end",
                "  guardStep(\"updateHeadLookIdleGate\", mod.updateHeadLookIdleGate)\n",
                "state.enabled"
            ),
            (
                "local function updateCameraTransition(delta)\n  local transition = state.cameraTransition\n  if not transition then\n    return\n  end",
                "    guardStep(\"updateCameraTransition\", updateCameraTransition, delta)\n",
                "state.cameraTransition"
            ),
            (
                "mod.updateProfileBlend = function(delta)\n  local b = state.profileBlend\n  if b == nil then return end",
                "    guardStep(\"updateProfileBlend\", mod.updateProfileBlend, delta)\n",
                "state.profileBlend"
            ),
            (
                "mod.updateHolocallEyes = function()\n  if config.holocallEyes ~= true and not state.holocallEyes.owed then return end",
                "  guardStep(\"updateHolocallEyes\", mod.updateHolocallEyes)\n",
                "config.holocallEyes == true or state.holocallEyes.owed"
            ),
            (
                "mod.updateFppWrapperReassert = function(delta)\n  if not state.fppWrapperOwed or state.sceneWrapperUp then\n    return\n  end",
                "    guardStep(\"fppWrapperReassert\", mod.updateFppWrapperReassert, delta)\n",
                "state.fppWrapperOwed and not state.sceneWrapperUp"
            )
        };
        const string marker = "-- " + stage + ": source-proven no-work guards only\n";
        if (text.Contains(stage, StringComparison.Ordinal))
        {
            if (text.Split(marker, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("ITP FPP fastpath marker drifted.");
            foreach (var gate in gates)
            {
                var indentation = Regex.Match(gate.Call, @"^\s*").Value;
                var guarded = indentation + "if " + gate.Guard + " then\n" +
                              indentation + "  " + gate.Call.TrimStart() +
                              indentation + "end\n";
                if (!text.Contains(gate.Proof, StringComparison.Ordinal) ||
                    text.Split(guarded, StringSplitOptions.None).Length != 2)
                    throw new InvalidOperationException("ITP FPP fastpath partial or author drift: " + gate.Guard);
            }
            return text;
        }
        foreach (var gate in gates)
        {
            if (!text.Contains(gate.Proof, StringComparison.Ordinal) ||
                text.Split(gate.Call, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("ITP FPP fastpath source drift: " + gate.Guard);
            var indent = Regex.Match(gate.Call, @"^\s*").Value;
            var guarded = indent + "if " + gate.Guard + " then\n" +
                          indent + "  " + gate.Call.TrimStart() + indent + "end\n";
            text = text.Replace(gate.Call, guarded, StringComparison.Ordinal);
        }
        const string owner = "-- __gcetItppUpdatedPendingGatesV3: author-verified absent-state fast guards only\n";
        if (text.Split(owner, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException("ITP update owner marker drift; refuse fastpath.");
        return text.Replace(owner, owner + marker, StringComparison.Ordinal);
    }

    // Updated upstream action handler explicitly services game-camera movement
    // and move360 even when TPP is disabled. The previous V2 SetActive(false)
    // contract is UNSAFE here: route exact finite actions but never pause
    // this subscriber on state.enabled alone.
    private static bool IsImmersiveThirdPersonActionRoutingCompleteV3(string text)
    {
        if (!text.Contains("__gcetItppRoutedOnActionV3", StringComparison.Ordinal) ||
            !text.Contains("__gcetItppAlwaysListeningV3", StringComparison.Ordinal))
            return false;

        // Scope this exact author-owned function without interpreting nested
        // Lua closures as separate top-level owners. The generic scanner can
        // fail on this newer upstream nested callback structure.
        const string signature = "local function observePlayerActions()";
        var start = text.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0 || text.IndexOf(signature, start + signature.Length,
                                    StringComparison.Ordinal) >= 0)
            return false;
        var finish = text.IndexOf("\nend", start + signature.Length,
                                  StringComparison.Ordinal);
        if (finish < 0)
            return false;
        var body = text[start..(finish + 4)];

        var currentMap = Regex.Match(text,
            @"(?s)\bmod\.handledActions\s*=\s*\{(?<entries>.*?)\}",
            RegexOptions.CultureInvariant);
        var members = currentMap.Success
            ? Regex.Matches(currentMap.Groups["entries"].Value,
                @"\b[a-zA-Z_][a-zA-Z_0-9]*\s*=\s*true\b",
                RegexOptions.CultureInvariant).Count
            : 0;
        return members == 18 &&
               text.Contains("mod.gameCameraMoveSeen(action)", StringComparison.Ordinal) &&
               text.Contains("mod.move360Capture(action)", StringComparison.Ordinal) &&
               body.Contains("zeroEngineMod.SubscribeAction", StringComparison.Ordinal) &&
               body.Contains("engine.SubscribeAction", StringComparison.Ordinal) &&
               body.Contains("actions = actions, decodeType = false", StringComparison.Ordinal) &&
               body.Contains("Observe('PlayerPuppet', 'OnAction'", StringComparison.Ordinal) &&
               body.Contains("pcall(handlePlayerAction, action)", StringComparison.Ordinal) &&
               body.Contains("if not routed and Observe then", StringComparison.Ordinal) &&
               !body.Contains("SetActive", StringComparison.Ordinal) &&
               !body.Contains("actions = \"*\"", StringComparison.Ordinal);
    }

    private static SemanticInjectionResult ApplyImmersiveThirdPersonUpdatedActions(
        SemanticPatchContext context, SemanticTextFile file)
    {
        var source = file.Text;
        if (IsImmersiveThirdPersonActionRoutingCompleteV3(source))
            return SemanticInjectionResult.Skip(
                "Current ITP exact-action shared subscriber already applied; " +
                "its disabled-camera/move360 callbacks stay active.");
        if (source.Contains("__gcetItppRoutedOnActionV3", StringComparison.Ordinal) ||
            source.Contains("__gcetItppActionDormancyV2", StringComparison.Ordinal) ||
            source.Contains("__gcetItppRoutedOnActionV1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Unexpected mixed/partial third-person action source; fail closed.");

        // The source-owned finite action table is authoritative. Any new
        // name, removed name or dynamic table modification requires re-audit.
        var tableMatch = Regex.Match(source,
            @"(?s)\bmod\.handledActions\s*=\s*\{(?<entries>.*?)\}",
            RegexOptions.CultureInvariant);
        if (!tableMatch.Success)
            throw new InvalidOperationException(
                "Updated third-person finite action table absent.");
        var entries = tableMatch.Groups["entries"].Value;
        var names = Regex.Matches(entries,
            @"\b(?<name>[a-zA-Z_][a-zA-Z_0-9]*)\s*=\s*true\b",
            RegexOptions.CultureInvariant)
            .Select(m => m.Groups["name"].Value).ToArray();
        string[] actions =
        {
            "mouse_x", "mouse_y", "right_stick_x", "right_stick_y",
            "CameraMouseX", "CameraMouseY", "CameraX", "CameraY",
            "Forward", "Back", "Left", "Right",
            "world_map_menu_move_horizontal", "world_map_menu_move_vertical",
            "left_stick_x", "left_stick_y", "MoveX", "MoveY"
        };
        var rest = Regex.Replace(entries,
            @"\b[a-zA-Z_][a-zA-Z_0-9]*\s*=\s*true\b", "",
            RegexOptions.CultureInvariant);
        if (names.Length != actions.Length ||
            names.Distinct(StringComparer.Ordinal).Count() != actions.Length ||
            !names.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(
                actions.OrderBy(x => x, StringComparer.Ordinal)) ||
            Regex.IsMatch(rest, @"[^\s,]", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "Updated ITP action set is not the proved exact 18 names.");

        if (!source.Contains(
                "if not state.enabled or isPlayerInAnyMenu() then\n" +
                "    if state.gameCameraHold ~= nil then\n" +
                "      mod.gameCameraMoveSeen(action)\n" +
                "    end\n" +
                "    if config.camera.move360 then\n" +
                "      mod.move360Capture(action)\n" +
                "    end\n" +
                "    return",
                StringComparison.Ordinal) ||
            !source.Contains("mod.move360CaptureNamed(action, actionName, actionValue)", StringComparison.Ordinal) ||
            !source.Contains("if not actionName or not mod.handledActions[actionName] then", StringComparison.Ordinal) ||
            !source.Contains("pcall(handlePlayerAction, action)", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Updated disabled-camera/360 movement action semantics drifted.");

        // No author action consumption, no additional native observer and no
        // clock/input-value transformation. Fallback retains the exact pcall.
        const string original = """
        local function observePlayerActions()
          if state.observedActions or not Observe then
            return
          end

          local okObs = pcall(function()
            Observe('PlayerPuppet', 'OnAction', function(_, action)
              pcall(handlePlayerAction, action)
            end)
          end)
          if okObs then
            state.observedActions = true
          end
        end
        """;
        var quoted = string.Join(", ", actions.Select(a => "\"" + a + "\""));
        var updated = "local function observePlayerActions()\n" +
            "  if state.observedActions or not Observe then return end\n" +
            "  -- __gcetItppRoutedOnActionV3: exact 18 author action names\n" +
            "  -- __gcetItppAlwaysListeningV3: disabled TPP still tracks camera/move360\n" +
            "  local okEngine, engine = pcall(GetMod, \"0-Engine\")\n" +
            "  local zeroEngineMod = okEngine and type(engine) == \"table\" and " +
            "type(engine.Register) == \"function\" and " +
            "engine.Register(\"immersive_third_person\") or nil\n" +
            "  local actions = { " + quoted + " }\n" +
            "  local callback = function(_, action) pcall(handlePlayerAction, action) end\n" +
            "  local routed = false\n" +
            "  if type(zeroEngineMod) == \"table\" and type(zeroEngineMod.SubscribeAction) == \"function\" then\n" +
            "    local ok, handle = pcall(zeroEngineMod.SubscribeAction, " +
            "{ actions = actions, decodeType = false }, callback)\n" +
            "    routed = ok and type(handle) == \"table\"\n" +
            "  elseif okEngine and type(engine) == \"table\" and " +
            "type(engine.SubscribeAction) == \"function\" then\n" +
            "    local ok, handle = pcall(engine.SubscribeAction, " +
            "{ actions = actions, decodeType = false }, callback, \"immersive_third_person\")\n" +
            "    routed = ok and type(handle) == \"table\"\n" +
            "  end\n" +
            "  if not routed and Observe then\n" +
            "    routed = pcall(function()\n" +
            "      Observe('PlayerPuppet', 'OnAction', function(_, action)\n" +
            "        pcall(handlePlayerAction, action)\n" +
            "      end)\n" +
            "    end)\n" +
            "  end\n" +
            "  if routed then state.observedActions = true end\n" +
            "end";
        var after = ReplaceOnce(source, original.TrimEnd('\n'), updated,
            "Updated upstream ITP action observer");
        if (!IsImmersiveThirdPersonActionRoutingCompleteV3(after))
            throw new InvalidOperationException(
                "Updated third-person action routing postcondition failed.");
        context.Write(file, after);
        return SemanticInjectionResult.Success(
            "Current ITP 18-name action observer consolidated into 0-Engine, " +
            "preserving all author camera/move360 work when TPP is disabled " +
            "and original native fallback; no unsafe TPP-only pause.");
    }

    // Registration count and subscriber dispatch are different layers. This
    // stage pauses only the 0-Engine subscriber. The one shared native hook
    // remains available to other mods and the original CET controls wake TPP.
    internal static bool IsImmersiveThirdPersonActionDormancyComplete(string ownerFolder)
    {
        var path = Path.Combine(ownerFolder, "init.lua");
        return File.Exists(path) &&
            IsImmersiveThirdPersonActionDormancyCompleteText(File.ReadAllText(path));
    }

    private static bool IsImmersiveThirdPersonActionDormancyCompleteText(string text)
    {
        if (IsImmersiveThirdPersonActionRoutingCompleteV3(text))
            return true;
        if (!text.Contains("__gcetItppActionDormancyV2", StringComparison.Ordinal) ||
            !text.Contains("__gcetItppRoutedOnActionV1", StringComparison.Ordinal) ||
            !SemanticScopedStages.TryFunction(text, "observePlayerActions", out var observer) ||
            !SemanticScopedStages.TryFunction(text, "__gcetItppSyncActionDormancy", out var sync) ||
            !SemanticScopedStages.TryFunction(text, "applyTppMode", out var enable) ||
            !SemanticScopedStages.TryFunction(text, "restoreFppMode", out var disable) ||
            !SemanticScopedStages.TryFunction(text, "updateSessionGuard", out var session))
            return false;

        if (!observer.Text.Contains("zeroEngineMod.SubscribeAction", StringComparison.Ordinal) ||
            !observer.Text.Contains("Observe('PlayerPuppet', 'OnAction'", StringComparison.Ordinal) ||
            Regex.Matches(observer.Text, @"__gcetItppSyncActionDormancy\(\)",
                RegexOptions.CultureInvariant).Count != 2 ||
            Regex.Matches(observer.Text, @"__gcetItppActionHandle\s*=\s*handle",
                RegexOptions.CultureInvariant).Count != 2 ||
            !sync.Text.Contains("pcall(handle.SetActive, state.enabled == true)", StringComparison.Ordinal))
            return false;

        return HasItppDormancyStateSync(enable.Text, true) &&
            HasItppDormancyStateSync(disable.Text, false) &&
            HasItppDormancyStateSync(session.Text, false) &&
            Regex.Matches(text, @"\bstate\.enabled\s*=\s*(?:true|false)\b",
                RegexOptions.CultureInvariant).Count == 3;
    }

    private static bool HasItppDormancyStateSync(string functionText, bool active)
    {
        return Regex.Matches(functionText,
            @"\bstate\.enabled\s*=\s*" + (active ? "true" : "false") +
            @"[ \t]*\r?\n[ \t]*__gcetItppSyncActionDormancy\(\)",
            RegexOptions.CultureInvariant).Count == 1;
    }

    private static string AddImmersiveThirdPersonActionDormancy(string source)
    {
        if (IsImmersiveThirdPersonActionDormancyCompleteText(source))
            return source;
        if (source.Contains("__gcetItppActionDormancyV2", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Incomplete existing third-person action dormancy stage; refusing unsafe duplicate rewrite.");

        if (!source.Contains("__gcetItppRoutedOnActionV1", StringComparison.Ordinal) ||
            !SemanticScopedStages.TryFunction(source, "observePlayerActions", out var observer) ||
            !SemanticScopedStages.TryFunction(source, "applyTppMode", out var enable) ||
            !SemanticScopedStages.TryFunction(source, "restoreFppMode", out var disable) ||
            !SemanticScopedStages.TryFunction(source, "updateSessionGuard", out var session))
            throw new InvalidOperationException(
                "Third-person shared action route or author state/wake functions are not source-proven.");

        // Exactly three source-owned state writers were verified in the real
        // mod: TPP activation, FPP restoration, and session-gap deactivation.
        // A new writer could bypass subscriber resumption: fail closed.
        if (Regex.Matches(source, @"\bstate\.enabled\s*=\s*(?:true|false)\b",
                RegexOptions.CultureInvariant).Count != 3 ||
            !Regex.IsMatch(enable.Text, @"\bstate\.enabled\s*=\s*true\b") ||
            !Regex.IsMatch(disable.Text, @"\bstate\.enabled\s*=\s*false\b") ||
            !Regex.IsMatch(session.Text, @"\bstate\.enabled\s*=\s*false\b"))
            throw new InvalidOperationException(
                "Third-person enable/disable/session writer set has changed.");

        var route = observer.Text;
        const string legacyOptions = "{ actions = actions, decodeType = false }";
        var legacyHandle = new Regex(
            @"(?m)^(?<indent>[ \t]*)routed\s*=\s*ok\s+and\s+type\(handle\)\s*==\s*""table""[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        if (route.Split(legacyOptions, StringSplitOptions.None).Length != 3 ||
            legacyHandle.Matches(route).Count != 2 ||
            !route.Contains("__gcetItppRoutedOnActionV1", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Third-person v1 action-route options or handle ownership have changed.");

        // Subscribe in its original active state and immediately sync after
        // obtaining a pausable handle. A compatible older runtime without a
        // SetActive handle retains the author's original active callback;
        // pre-pausing at Subscribe time could strand that older runtime asleep.
        // Legacy user files and CI-generated ZIPs can have LF or CRLF; both
        // routed branches must be proven before introducing handle ownership.
        route = legacyHandle.Replace(route, m =>
            m.Value.TrimEnd('\r') + (m.Value.EndsWith('\r') ? "\r\n" : "\n") +
            m.Groups["indent"].Value + "if routed then __gcetItppActionHandle = handle; __gcetItppSyncActionDormancy() end");

        var prefix =
            "-- __gcetItppActionDormancyV2: author camera state owns subscriber activity\n" +
            "local __gcetItppActionHandle = nil\n" +
            "local function __gcetItppSyncActionDormancy()\n" +
            "  local handle = __gcetItppActionHandle\n" +
            "  if handle and type(handle.SetActive) == \"function\" then\n" +
            "    pcall(handle.SetActive, state.enabled == true)\n" +
            "  end\n" +
            "end\n\n";
        var text = source[..observer.Start] + prefix + route + source[observer.End..];

        foreach (var pair in new[]
        {
            (Name: "applyTppMode", Active: true),
            (Name: "restoreFppMode", Active: false),
            (Name: "updateSessionGuard", Active: false)
        })
        {
            if (!SemanticScopedStages.TryFunction(text, pair.Name, out var span))
                throw new InvalidOperationException("Third-person state writer disappeared: " + pair.Name);
            var state = pair.Active ? "true" : "false";
            var regex = new Regex(
                @"(?m)^(?<indent>[ \t]*)state\.enabled\s*=\s*" + state + @"[ \t]*$",
                RegexOptions.CultureInvariant);
            if (regex.Matches(span.Text).Count != 1)
                throw new InvalidOperationException("Third-person state writer is ambiguous: " + pair.Name);
            var updated = regex.Replace(span.Text, m =>
                m.Groups["indent"].Value + "state.enabled = " + state + "\n" +
                m.Groups["indent"].Value + "__gcetItppSyncActionDormancy()", 1);
            text = text[..span.Start] + updated + text[span.End..];
        }

        if (!IsImmersiveThirdPersonActionDormancyCompleteText(text))
            throw new InvalidOperationException(
                "Third-person subscriber dormancy lifecycle postconditions failed.");
        return text;
    }

    // Finite action-interest proof from the live camera/movement handler.
    // Preserve the original handler and native Observer as fallback.
    private static SemanticInjectionResult ApplyImmersiveThirdPersonActionRouting(
        SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua",
            "local function handlePlayerAction(action)",
            "local function setDigitalMove(actionName, actionValue)",
            "local function observePlayerActions()",
            "Observe('PlayerPuppet', 'OnAction'");
        var source = file.Text;
        if (source.Contains("mod.handledActions = {", StringComparison.Ordinal) &&
            source.Contains("mod.gameCameraMoveSeen = function", StringComparison.Ordinal))
            return ApplyImmersiveThirdPersonUpdatedActions(context, file);
        const string stage = "__gcetItppRoutedOnActionV1";
        if (source.Contains(stage, StringComparison.Ordinal))
        {
            if (!source.Contains("zeroEngineMod.SubscribeAction", StringComparison.Ordinal) ||
                !source.Contains("Observe('PlayerPuppet', 'OnAction'", StringComparison.Ordinal))
                throw new InvalidOperationException("Immersive third-person action stage is inconsistent.");

            var upgraded = AddImmersiveThirdPersonActionDormancy(source);
            if (upgraded == source)
                return SemanticInjectionResult.Skip(
                    "Third-person routing and source-proven action subscriber dormancy already complete.");
            context.Write(file, upgraded);
            return SemanticInjectionResult.Success(
                "Upgraded existing shared action routing with state-driven subscriber pause/resume; preserved original native fallback and author-owned CET keybinds.");
        }

        var handler = source.IndexOf("local function handlePlayerAction(action)", StringComparison.Ordinal);
        var registration = source.IndexOf("local function observePlayerActions()", StringComparison.Ordinal);
        if (handler < 0 || registration < handler)
            throw new InvalidOperationException("Third-person camera input handler is not uniquely proven.");
        var body = source[handler..registration];
        string[] actions =
        {
            "mouse_x", "mouse_y", "right_stick_x", "right_stick_y",
            "CameraMouseX", "CameraMouseY", "CameraX", "CameraY",
            "Forward", "Back", "Left", "Right",
            "world_map_menu_move_horizontal", "world_map_menu_move_vertical",
            "left_stick_x", "left_stick_y", "MoveX", "MoveY"
        };
        // All finite action literals must be proven in this handler or the
        // exclusively called digital-movement predicate. No wildcard routing.
        var digitalStart = source.IndexOf("local function setDigitalMove(actionName, actionValue)", StringComparison.Ordinal);
        if (digitalStart < 0 || digitalStart >= handler)
            throw new InvalidOperationException("Third-person digital movement predicate missing.");
        var evidence = source[digitalStart..registration];
        foreach (var action in actions)
            if (!evidence.Contains("actionName == \"" + action + "\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Third-person action interest changed: " + action);
        if (body.Contains("consumer:Consume", StringComparison.Ordinal) ||
            body.Contains("Consume()", StringComparison.Ordinal))
            throw new InvalidOperationException("Third-person action handler gained consumption behavior.");

        var pattern = new Regex(
            @"(?ms)local function observePlayerActions\(\)\s+if state\.observedActions or not Observe then\s+return\s+end\s+local okObs = pcall\(function\(\)\s+Observe\('PlayerPuppet', 'OnAction', function\(_, action\)\s+handlePlayerAction\(action\)\s+end\)\s+end\)\s+if okObs then\s+state\.observedActions = true\s+end\s+end",
            RegexOptions.CultureInvariant);
        var matches = pattern.Matches(source);
        if (matches.Count != 1)
            throw new InvalidOperationException("Third-person native OnAction lifecycle changed or became ambiguous.");

        var quoted = string.Join(", ", actions.Select(action => "\"" + action + "\""));
        var replacement =
            "local function observePlayerActions()\n" +
            "  if state.observedActions then return end\n" +
            "  -- " + stage + ": exact camera and movement inputs, no wildcard\n" +
            "  local okEngine, engine = pcall(GetMod, \"0-Engine\")\n" +
            "  local zeroEngineMod = okEngine and type(engine) == \"table\" and type(engine.Register) == \"function\" and engine.Register(\"immersive_third_person\") or nil\n" +
            "  local actions = { " + quoted + " }\n" +
            "  local callback = function(_, action) handlePlayerAction(action) end\n" +
            "  local routed = false\n" +
            "  if type(zeroEngineMod) == \"table\" and type(zeroEngineMod.SubscribeAction) == \"function\" then\n" +
            "    local ok, handle = pcall(zeroEngineMod.SubscribeAction, { actions = actions, decodeType = false }, callback)\n" +
            "    routed = ok and type(handle) == \"table\"\n" +
            "  elseif okEngine and type(engine) == \"table\" and type(engine.SubscribeAction) == \"function\" then\n" +
            "    local ok, handle = pcall(engine.SubscribeAction, { actions = actions, decodeType = false }, callback, \"immersive_third_person\")\n" +
            "    routed = ok and type(handle) == \"table\"\n" +
            "  end\n" +
            "  if not routed and Observe then\n" +
            "    routed = pcall(function()\n" +
            "      Observe('PlayerPuppet', 'OnAction', function(_, action) handlePlayerAction(action) end)\n" +
            "    end)\n" +
            "  end\n" +
            "  if routed then state.observedActions = true end\n" +
            "end";
        var m = matches[0];
        var routed = source[..m.Index] + replacement + source[(m.Index + m.Length)..];
        context.Write(file, AddImmersiveThirdPersonActionDormancy(routed));
        return SemanticInjectionResult.Success(
            "One pristine pass provides exact-action native observer consolidation and source-driven TPP enable/FPP disable/session-gap subscriber dormancy; preserves existing CET keybind wake and fallback.");
    }



    // The same author-side structural proof governs worker-only recovery.
    // A missing external coordinator does not invalidate a completed bridge.
    // Use ITP's author-owned relative-to-source module loader rather than
    // plain dofile, whose relative path depends on the CET working directory.
    private const string ItppLegacyRelativeWorkerLoader =
        "    return dofile(\"GCET_Semantic/itp_frame_dormancy.lua\").Install(\n" +
        "      mod, state, config, __gcetItppFrameStepV1, __gcetItppRuntimeV1)";
    private const string ItppAuthorWorkerLoader =
        "    local worker, reason = mod.loadModule(\"GCET_Semantic/itp_frame_dormancy\")\n" +
        "    if type(worker) ~= \"table\" or type(worker.Install) ~= \"function\" then\n" +
        "      error(reason or \"ITP external worker interface missing\")\n" +
        "    end\n" +
        "    return worker.Install(\n" +
        "      mod, state, config, __gcetItppFrameStepV1, __gcetItppRuntimeV1)";

    private const string ItppFileLoadRegisterBridgeV2 =
        "  if ok and controller and type(controller.Register) == \"function\" then\n" +
        "    local registered, ready = pcall(controller.Register)\n" +
        "    if registered and ready == true then mod.__gcetItppDormancyV1 = controller end\n" +
        "  end";

    // Old complete source may be upgraded only when the original author
    // bridge is structurally intact and no duplicate frame owner exists.
    // Prior file-load bridge, only recognized for source-safe migration.
    private static bool HasImmersiveThirdPersonFrameDormancyBridgeV2(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r','\n');
        return normalized.Contains(ItppLegacyRelativeWorkerLoader, StringComparison.Ordinal)
            && !normalized.Contains(ItppAuthorWorkerLoader, StringComparison.Ordinal)
            && HasImmersiveThirdPersonFrameDormancyBridge(normalized.Replace(
                ItppLegacyRelativeWorkerLoader, ItppAuthorWorkerLoader, StringComparison.Ordinal));
    }

    private static bool HasImmersiveThirdPersonFrameDormancyBridgeV1(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return text.Contains("-- __gcetItppDormantFrameV1:", StringComparison.Ordinal)
            && text.Contains("local function __gcetItppFrameStepV1(delta)", StringComparison.Ordinal)
            && text.Contains("mod.__gcetItppDormancyV1.Start()", StringComparison.Ordinal)
            && text.Contains("mod.__gcetItppDormancyV1.Stop()", StringComparison.Ordinal)
            && text.Contains("mod.__gcetItppDormancyV1.Signal(\"toggle\")", StringComparison.Ordinal)
            && text.Contains("  if ok and controller then mod.__gcetItppDormancyV1 = controller end", StringComparison.Ordinal)
            && text.Contains("if not mod.__gcetItppDormancyV1 then", StringComparison.Ordinal)
            && !text.Contains("pcall(controller.Register)", StringComparison.Ordinal)
            && !Regex.IsMatch(text,
                @"(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\(");
    }

    internal static bool HasImmersiveThirdPersonFrameDormancyBridge(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return !(!text.Contains("-- __gcetItppDormantFrameV1:", StringComparison.Ordinal)
            || !text.Contains("local function __gcetItppFrameStepV1(delta)", StringComparison.Ordinal)
            || !text.Contains("mod.__gcetItppDormancyV1.Start()", StringComparison.Ordinal)
            || !text.Contains("mod.__gcetItppDormancyV1.Stop()", StringComparison.Ordinal)
            || !text.Contains("pcall(controller.Register)", StringComparison.Ordinal)
            || !text.Contains("  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end\nend)", StringComparison.Ordinal)
            || text.IndexOf("pcall(controller.Register)", StringComparison.Ordinal) <
               text.IndexOf("  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end\nend)", StringComparison.Ordinal)
            || text.IndexOf("pcall(controller.Register)", StringComparison.Ordinal) >
               text.IndexOf("local UI = {", StringComparison.Ordinal)
            || !text.Contains("mod.__gcetItppDormancyV1.Signal(\"toggle\")", StringComparison.Ordinal)
            || !text.Contains("if not mod.__gcetItppDormancyV1 then", StringComparison.Ordinal)
            || !text.Contains("mod.loadModule(\"GCET_Semantic/itp_frame_dormancy\")", StringComparison.Ordinal)
            || Regex.IsMatch(text,
                @"(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\("));
    }

    // Complete only when the actual detached frame owner, both lifecycle
    // hooks and its external pausable worker coexist. The native callback
    // must not execute in parallel with the shared scheduler.
    internal static bool IsImmersiveThirdPersonFrameDormancyComplete(string ownerFolder)
    {
        var initPath = Path.Combine(ownerFolder, "init.lua");
        if (!File.Exists(initPath)) return false;
        var text = File.ReadAllText(initPath).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        // Older ITP source has its established inline supervisor ownership;
        // never demand the NEW detached worker from the historical author
        // architecture. Its own three independent stages remain the proof.
        if (!text.Contains("mod.pluginHeartbeat(delta)", StringComparison.Ordinal))
            return text.Contains("local __gcetItppSupervisorElapsed = 0.0", StringComparison.Ordinal)
                && text.Contains("local __gcetItppMaintenanceElapsed = 0.0", StringComparison.Ordinal)
                && text.Contains("local function __gcetItppHasStandbyWork()", StringComparison.Ordinal)
                && text.Contains("__gcetItppSupervisorElapsed >= __gcetItppSupervisorInterval",
                    StringComparison.Ordinal)
                && text.Contains("__gcetItppMaintenanceElapsed >= 0.25", StringComparison.Ordinal);

        if (!HasImmersiveThirdPersonFrameDormancyBridge(text))
            return false;

        var hostDir = Directory.GetParent(ownerFolder)?.FullName;
        if (hostDir == null) return false;
        var hostPath = Path.Combine(hostDir, "0-Engine", "init.lua");
        if (!File.Exists(hostPath)) return false;
        var host = File.ReadAllText(hostPath).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        if (!host.Contains("Engine.__gcetScheduleDriver({", StringComparison.Ordinal)
            || !host.Contains("__host.__gcetScheduleDriver = function(ctx)", StringComparison.Ordinal)
            || !host.Contains("registerForEvent(\"onUpdate\", function(delta)", StringComparison.Ordinal))
            return false;

        var workerPath = Path.Combine(ownerFolder, "GCET_Semantic", "itp_frame_dormancy.lua");
        if (!File.Exists(workerPath)) return false;
        var worker = File.ReadAllText(workerPath).Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return worker.Contains("-- G-CET semantic coordinator:immersive-third-person\n",
                   StringComparison.Ordinal)
            && worker.Contains("function M.Install(mod, state, config, step, client)",
                StringComparison.Ordinal)
            && worker.Contains("function M.Register()", StringComparison.Ordinal)
            && worker.Contains("schedule.EveryFrame({ id=\"itp.frame\", pause=\"never\", active=false }", StringComparison.Ordinal)
            && worker.Contains("if handle then handle.Pause() end", StringComparison.Ordinal)
            && !worker.Contains("handle.Cancel()", StringComparison.Ordinal)
            && worker.Contains("handle.Pause()", StringComparison.Ordinal)
            && worker.Contains("handle.Resume()", StringComparison.Ordinal)
            && worker.Contains("sessionWakeProven", StringComparison.Ordinal)
            && worker.Contains("PlayerInvalidated", StringComparison.Ordinal)
            && worker.Contains("PlayerReady", StringComparison.Ordinal)
            && worker.Contains("hasNativeBindings()", StringComparison.Ordinal);
    }

    // This is an owned part of the CURRENT ITP semantic generator, not a
    // second repair semantic. Never run without prior generic/native stages.
    private const string ItppDormancyWorkerV1 = """
    -- G-CET semantic coordinator:immersive-third-person
    -- __gcetItppDormantFrameV1: one shared 0-Engine frame owner, no quiet timer.
    local M = {}
    function M.Install(mod, state, config, step, client)
      local handle, started, stopped = nil, false, false
      local calls, pauses, wakes, wakeReason, warm = 0, 0, 0, "init", 2
      local sessionWarm = 0.0
      local subscriptions = {}
      local sessionWakeProven = false
      local function hasNativeBindings()
        return (config.nativeToggleKey or "") ~= ""
          or (config.nativeQuickFppKey or "") ~= ""
          or (config.nativeFreelookKey or "") ~= ""
          or (config.nativeShoulderKey or "") ~= ""
          or (config.nativeFrontKey or "") ~= ""
          or (config.nativeProfileNextKey or "") ~= ""
          or (config.nativeProfilePrevKey or "") ~= ""
      end
      local function needFrame()
        local ap, camera = state.autoPerspective, config.camera
        local auto = config.autoPerspective
        if state.enabled or state.requestedMode ~= "FPP" then return true end
        -- An authored toggle timestamp is measured in modClock time.
        -- Let its original debounce interval finish before freezing that clock.
        if state.lastToggleAt ~= nil and
          ((state.modClock or 0) - state.lastToggleAt) < 0.25 then return true end
        if ap.userWantsTpp == true or ap.pendingSessionTppRestore then return true end
        if state.cameraTransition or state.quickFppEngaged or state.pendingSessionTppRestore
          or state.pendingSceneTppReapply or state.pendingFppCleanup or state.pendingFppRestore
          or state.fppRestoreWatchdog or state.pendingTppRepReassert
          or state.pendingMirrorHeadVerify or state.pendingFacialMute
          or state.pendingTppAnimPoke or state.pendingTppDeactivate
          or state.pendingTakeoverHandoff or state.pendingTakeoverHeadCheck
          or state.boneResyncPending or state.moveAlignEdgePending
          or state.sprintStopFollowPending or state.vaultSprintPending
          or state.mirrorChoicePending or state.photoModeWasActive
          or state.photoModeHeadRestore or state.pendingFaultNotice
          or state.pickupPulseUntil or state.consumableIdleUntil
          or state.consumableGateUntil or state.consumableGraceUntil
          or state.gameCameraHold or state.cameraDebtRestore or state.cameraDebtPop
          or state.sceneWrapperUp or state.fppWrapperOwed
          or state.takeoverFppHandoff or state.headGuardApplied
          or state.headLookActive or state.headLookHeld or state.headGateOn
          or state.headGateOffAt
          or state.turnWindowLive or state.turnWindowWanted
          or state.spatialAudio.turned or state.bodySliders.on
          or state.groundLevel.owed or state.holocallEyes.owed
          or state.pickupRange.wasOn or state.move360.wasOn
          or state.armHeal.deactivateNext or state.armHeal.dueAt
          or state.consumableTimingPending then return true end
        -- The REDscript native key relay has no proven Lua event wake:
        -- retain original frame polling whenever any native key is configured.
        if hasNativeBindings() then return true end
        -- These independent FPP-capable services need their original cadence.
        if config.apiAccess == true or camera.move360 == true
          or camera.spatialAudio == true or config.headToNpc == true
          or config.idleBreakTalk == true or config.holocallEyes == true
          or auto.sceneTpp == true or auto.interactionTpp == true
          or auto.elevatorTpp == true or auto.alertTpp == true then return true end
        if state.overlayOpen or state.sessionGapActive or not state.turnWindowStartCleared
          or state.nativeSettingsDirty
          or not (state.depGuard and state.depGuard.done == true) then return true end
        return false
      end
      local function route()
        if not started or stopped or not handle then return end
        if not sessionWakeProven or warm > 0 or sessionWarm > 0 or needFrame() then
          if not handle.IsActive() then handle.Resume() end
        elseif handle.IsActive() then
          handle.Pause()
          -- An outstanding toggle debounce keeps the frame job active.
          -- Once settled, modClock can freeze without rejecting later keys.
          pauses = pauses + 1
        end
      end
      function M.Signal(reason)
        if stopped then return end
        wakes = wakes + 1
        wakeReason = reason or "direct"
        warm = math.max(warm, 2)
        if reason == "PlayerInvalidated" then sessionWarm = math.max(sessionWarm, 0.85) end
        route()
      end
      -- All scheduler and event registration happens exactly once at Lua
      -- FILE LOAD, never from CET onInit/onUpdate/onShutdown dispatch.
      function M.Register()
        if handle then return true end
        local schedule = client.Schedule
        assert(type(schedule) == "table" and type(schedule.EveryFrame) == "function",
          "ITP: source-proven shared frame driver unavailable")
        handle = schedule.EveryFrame({ id="itp.frame", pause="never", active=false },
          function(ctx)
            if not started or stopped then return end
            local dt = ctx and ctx.dt or 0
            step(dt)
            calls = calls + 1
            if warm > 0 then warm = warm - 1 end
            if sessionWarm > 0 then sessionWarm = math.max(0, sessionWarm - dt) end
            route() -- Finish author cleanup on the same frame before sleeping.
          end)
        assert(type(handle) == "table" and type(handle.Pause) == "function"
          and type(handle.Resume) == "function" and type(handle.IsActive) == "function",
          "ITP: source-proven pausable scheduler handle unavailable")
        -- Preserve the same persistent event subscriptions across a session
        -- shutdown/restart. No native registrations may occur on restart.
        sessionWakeProven = type(client.Subscribe) == "function"
        if sessionWakeProven then
          for _, name in ipairs({ "PlayerReady", "PlayerInvalidated" }) do
            local ok, h = pcall(client.Subscribe, name,
              function() M.Signal(name) end)
            if ok and type(h) == "table" and type(h.unsubscribe) == "function" then
              subscriptions[#subscriptions + 1] = h
            else
              sessionWakeProven = false
            end
          end
        end
        return true
      end
      function M.Start()
        if started then return end
        -- Never create a shared/native frame registration from onInit.
        assert(handle, "ITP: missing file-load frame registration")
        stopped = false
        warm, sessionWarm = 2, 0.0
        started = true
        route()
      end
      function M.Stop()
        if stopped then return end
        stopped, started = true, false
        if handle then handle.Pause() end
        -- Pausing keeps exactly one registered job through world reload.
        -- M.Signal ignores shutdown signals; the owner is not cancelled or
        -- re-registered from the next onInit.
      end
      function M.Diagnostics()
        return { mode = stopped and "stopped" or
          (handle and handle.IsActive() and "active" or "dormant"),
          activeJob = handle and handle.IsActive() or false,
          started = started, calls = calls, pauses = pauses,
          wakes = wakes, lastWake = wakeReason,
          pendingCleanup = needFrame(), nativeKeyFallback = hasNativeBindings(),
          sessionWakeProven = sessionWakeProven,
          registrationPath = "shared-0-engine-schedule", nativeItpUpdate = false }
      end
      return M
    end
    return M
    """;

    // Previously generated file-load source could schedule from onInit.
    // Recognize only its exact generated source for a safe in-place upgrade.
    // Never deploy or execute this historical worker.
    private const string ItppLateRegisterWorkerV1 = """
    -- G-CET semantic coordinator:immersive-third-person
    -- __gcetItppDormantFrameV1: one shared 0-Engine frame owner, no quiet timer.
    local M = {}
    function M.Install(mod, state, config, step, client)
      local handle, started, stopped = nil, false, false
      local calls, pauses, wakes, wakeReason, warm = 0, 0, 0, "init", 2
      local sessionWarm = 0.0
      local subscriptions = {}
      local sessionWakeProven = false
      local function hasNativeBindings()
        return (config.nativeToggleKey or "") ~= ""
          or (config.nativeQuickFppKey or "") ~= ""
          or (config.nativeFreelookKey or "") ~= ""
          or (config.nativeShoulderKey or "") ~= ""
          or (config.nativeFrontKey or "") ~= ""
          or (config.nativeProfileNextKey or "") ~= ""
          or (config.nativeProfilePrevKey or "") ~= ""
      end
      local function needFrame()
        local ap, camera = state.autoPerspective, config.camera
        local auto = config.autoPerspective
        if state.enabled or state.requestedMode ~= "FPP" then return true end
        -- An authored toggle timestamp is measured in modClock time.
        -- Let its original debounce interval finish before freezing that clock.
        if state.lastToggleAt ~= nil and
          ((state.modClock or 0) - state.lastToggleAt) < 0.25 then return true end
        if ap.userWantsTpp == true or ap.pendingSessionTppRestore then return true end
        if state.cameraTransition or state.quickFppEngaged or state.pendingSessionTppRestore
          or state.pendingSceneTppReapply or state.pendingFppCleanup or state.pendingFppRestore
          or state.fppRestoreWatchdog or state.pendingTppRepReassert
          or state.pendingMirrorHeadVerify or state.pendingFacialMute
          or state.pendingTppAnimPoke or state.pendingTppDeactivate
          or state.pendingTakeoverHandoff or state.pendingTakeoverHeadCheck
          or state.boneResyncPending or state.moveAlignEdgePending
          or state.sprintStopFollowPending or state.vaultSprintPending
          or state.mirrorChoicePending or state.photoModeWasActive
          or state.photoModeHeadRestore or state.pendingFaultNotice
          or state.pickupPulseUntil or state.consumableIdleUntil
          or state.consumableGateUntil or state.consumableGraceUntil
          or state.gameCameraHold or state.cameraDebtRestore or state.cameraDebtPop
          or state.sceneWrapperUp or state.fppWrapperOwed
          or state.takeoverFppHandoff or state.headGuardApplied
          or state.headLookActive or state.headLookHeld or state.headGateOn
          or state.headGateOffAt
          or state.turnWindowLive or state.turnWindowWanted
          or state.spatialAudio.turned or state.bodySliders.on
          or state.groundLevel.owed or state.holocallEyes.owed
          or state.pickupRange.wasOn or state.move360.wasOn
          or state.armHeal.deactivateNext or state.armHeal.dueAt
          or state.consumableTimingPending then return true end
        -- The REDscript native key relay has no proven Lua event wake:
        -- retain original frame polling whenever any native key is configured.
        if hasNativeBindings() then return true end
        -- These independent FPP-capable services need their original cadence.
        if config.apiAccess == true or camera.move360 == true
          or camera.spatialAudio == true or config.headToNpc == true
          or config.idleBreakTalk == true or config.holocallEyes == true
          or auto.sceneTpp == true or auto.interactionTpp == true
          or auto.elevatorTpp == true or auto.alertTpp == true then return true end
        if state.overlayOpen or state.sessionGapActive or not state.turnWindowStartCleared
          or state.nativeSettingsDirty
          or not (state.depGuard and state.depGuard.done == true) then return true end
        return false
      end
      local function route()
        if not started or stopped or not handle then return end
        if not sessionWakeProven or warm > 0 or sessionWarm > 0 or needFrame() then
          if not handle.IsActive() then handle.Resume() end
        elseif handle.IsActive() then
          handle.Pause()
          -- An outstanding toggle debounce keeps the frame job active.
          -- Once settled, modClock can freeze without rejecting later keys.
          pauses = pauses + 1
        end
      end
      function M.Signal(reason)
        if stopped then return end
        wakes = wakes + 1
        wakeReason = reason or "direct"
        warm = math.max(warm, 2)
        if reason == "PlayerInvalidated" then sessionWarm = math.max(sessionWarm, 0.85) end
        route()
      end
      function M.Start()
        if started then return end
        stopped = false
        warm, sessionWarm = 2, 0.0
        local schedule = client.Schedule
        assert(type(schedule) == "table" and type(schedule.EveryFrame) == "function",
          "ITP: source-proven shared frame driver unavailable")
        started = true
        handle = schedule.EveryFrame({ id="itp.frame", pause="never", active=true },
          function(ctx)
            local dt = ctx and ctx.dt or 0
            step(dt)
            calls = calls + 1
            if warm > 0 then warm = warm - 1 end
            if sessionWarm > 0 then sessionWarm = math.max(0, sessionWarm - dt) end
            route() -- Finish author cleanup on the same frame before sleeping.
          end)
        sessionWakeProven = type(client.Subscribe) == "function"
        if sessionWakeProven then
          for _, name in ipairs({ "PlayerReady", "PlayerInvalidated" }) do
            local ok, h = pcall(client.Subscribe, name,
              function() M.Signal(name) end)
            if ok and type(h) == "table" and type(h.unsubscribe) == "function" then
              subscriptions[#subscriptions + 1] = h
            else
              sessionWakeProven = false
            end
          end
        end
        route()
      end
      function M.Stop()
        if stopped then return end
        stopped, started = true, false
        if handle then handle.Cancel() end
        for _, h in ipairs(subscriptions) do
          if h and type(h.unsubscribe) == "function" then pcall(h.unsubscribe) end
        end
        subscriptions, handle = {}, nil
        sessionWakeProven = false
      end
      function M.Diagnostics()
        return { mode = stopped and "stopped" or
          (handle and handle.IsActive() and "active" or "dormant"),
          activeJob = handle and handle.IsActive() or false,
          started = started, calls = calls, pauses = pauses,
          wakes = wakes, lastWake = wakeReason,
          pendingCleanup = needFrame(), nativeKeyFallback = hasNativeBindings(),
          sessionWakeProven = sessionWakeProven,
          registrationPath = "shared-0-engine-schedule", nativeItpUpdate = false }
      end
      return M
    end
    return M
    """;


    private static string ApplyImmersiveThirdPersonDetachedDormancy(
        SemanticPatchContext context, string text, out bool recoveredWorker)
    {
        recoveredWorker = false;
        const string stage = "__gcetItppDormantFrameV1";
        const string coordinator = "itp_frame_dormancy.lua";
        var existing = context.TryReadOwnedCoordinator(coordinator);
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var expected = ItppDormancyWorkerV1.Replace("\r\n", "\n", StringComparison.Ordinal);
        // The stage is never permitted to depend on an earlier generated
        // worker, including partially installed output.
        var knownPrior = ItppLateRegisterWorkerV1.Replace("\r\n", "\n", StringComparison.Ordinal);
        var currentWorker = existing?.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (currentWorker is not null && currentWorker != expected && currentWorker != knownPrior)
            throw new InvalidOperationException("ITP dormant frame worker drifted; refusing overwrite.");
        if (normalized.Contains(stage, StringComparison.Ordinal))
        {
            var hostCurrent = context.TryReadComposedModFile("0-Engine", "init.lua");
            if (hostCurrent is null ||
                !hostCurrent.Contains("Engine.__gcetScheduleDriver({", StringComparison.Ordinal) ||
                !hostCurrent.Contains("__host.__gcetScheduleDriver = function(ctx)", StringComparison.Ordinal) ||
                !hostCurrent.Contains("registerForEvent(\"onUpdate\", function(delta)", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Existing ITP detached frame controller has no proven composed 0-Engine driver.");
            if (!normalized.Contains("local function __gcetItppFrameStepV1(delta)", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Start()", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Stop()", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Signal(\"toggle\")", StringComparison.Ordinal) ||
                !normalized.Contains("GCET_Semantic/itp_frame_dormancy", StringComparison.Ordinal) ||
                !normalized.Contains("if not mod.__gcetItppDormancyV1 then", StringComparison.Ordinal) ||
                Regex.IsMatch(normalized,
                    @"(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\("))
                throw new InvalidOperationException("ITP dormant frame stage incomplete or has duplicate owner.");
            const string oldBridge = "  if ok and controller then mod.__gcetItppDormancyV1 = controller end";
            var isPriorBridge = normalized.Contains(oldBridge, StringComparison.Ordinal);
            var usesOldRelativeLoader = normalized.Contains(ItppLegacyRelativeWorkerLoader, StringComparison.Ordinal);
            var usesAuthorLoader = normalized.Contains(ItppAuthorWorkerLoader, StringComparison.Ordinal);
            if (usesOldRelativeLoader == usesAuthorLoader)
                throw new InvalidOperationException("ITP worker load path incomplete, duplicate or drifted.");
            if (isPriorBridge)
            {
                if (!HasImmersiveThirdPersonFrameDormancyBridgeV1(normalized))
                    throw new InvalidOperationException("ITP prior frame bridge drifted; refusing unsafe migration.");
                normalized = normalized.Replace(oldBridge, ItppFileLoadRegisterBridgeV2, StringComparison.Ordinal);
            }
            else if (usesOldRelativeLoader)
            {
                if (!HasImmersiveThirdPersonFrameDormancyBridgeV2(normalized))
                    throw new InvalidOperationException("ITP prior file-load bridge drifted; refusing loader upgrade.");
            }
            else if (!HasImmersiveThirdPersonFrameDormancyBridge(normalized))
                throw new InvalidOperationException("ITP current frame bridge lacks file-load Register proof.");
            if (usesOldRelativeLoader)
                normalized = normalized.Replace(ItppLegacyRelativeWorkerLoader,
                    ItppAuthorWorkerLoader, StringComparison.Ordinal);
            if (currentWorker != expected)
            {
                context.WriteCoordinator(coordinator, ItppDormancyWorkerV1);
                recoveredWorker = true;
            }
            return isPriorBridge || usesOldRelativeLoader
                ? text.Contains("\r\n", StringComparison.Ordinal)
                    ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
                    : normalized
                : text;
        }
        // Full current original only: compact test/historical input retains
        // its existing conservative frame loop. Unknown layouts fail closed.
        if (!normalized.Contains("mod.loadModule = function(name)", StringComparison.Ordinal) ||
            !normalized.Contains("mod.nativeSettingsSaveTick = function(delta)", StringComparison.Ordinal) ||
            !normalized.Contains("mod.pollNativeToggle = function(delta)", StringComparison.Ordinal) ||
            !normalized.Contains("registerInput(\"tpp_camera_toggle\"", StringComparison.Ordinal) ||
            !normalized.Contains("mod.applyConsumableTiming(\"FPP\", \"init\")", StringComparison.Ordinal) ||
            !normalized.Contains("local UI = {", StringComparison.Ordinal) ||
            !normalized.Contains("mod.apiCore = mod.apiBuild()", StringComparison.Ordinal) ||
            !normalized.Contains("registerForEvent(\"onShutdown\", function()", StringComparison.Ordinal))
            return text;
        var host = context.TryReadComposedModFile("0-Engine", "init.lua");
        if (host == null ||
            !host.Contains("Engine.__gcetScheduleDriver({", StringComparison.Ordinal) ||
            !host.Contains("__host.__gcetScheduleDriver = function(ctx)", StringComparison.Ordinal) ||
            !host.Contains("pcall(UpdateFrame, delta)", StringComparison.Ordinal) ||
            !host.Contains("registerForEvent(\"onUpdate\", function(delta)", StringComparison.Ordinal))
            return text; // Do not detach without the proven shared frame owner.
        if (existing != null)
            throw new InvalidOperationException("ITP dormancy worker exists without its source bridge.");

        var owners = Regex.Matches(normalized,
            @"(?m)^(?<registrar>registerForEvent|__gcetRegisterEvent_\d+)\((?<q>['""])onUpdate\k<q>, function\(delta\)\n  mod\.pluginHeartbeat\(delta\)",
            RegexOptions.CultureInvariant);
        if (owners.Count != 1)
            throw new InvalidOperationException("ITP dormant frame: unique composed onUpdate owner not proven.");
        var owner = owners[0];
        var sourceEnding = "\nend)\n\nlocal UI = {";
        var ending = normalized.IndexOf(sourceEnding, owner.Index + owner.Length, StringComparison.Ordinal);
        if (ending < 0 || normalized.IndexOf(sourceEnding, ending + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("ITP full frame body boundary drifted.");
        var body = normalized[(owner.Index + owner.Groups["registrar"].Length)..ending];
        if (!body.Contains("mod.updateMove360Step, delta", StringComparison.Ordinal) ||
            !body.Contains("mod.apiTick, delta", StringComparison.Ordinal) ||
            !body.Contains("mod.updateTppMaintenance, delta", StringComparison.Ordinal) ||
            !body.Contains("mod.pollNativeToggle, delta", StringComparison.Ordinal))
            throw new InvalidOperationException("ITP full frame source dependency changed.");

        // Only one execution owner: the original body is moved unchanged.
        // Without a verified runtime driver at file load, restore the same
        // author/generic registrar as a native CET fallback (not a second job).
        var cb = normalized[owner.Index..ending];
        var header = owner.Groups["registrar"].Value +
            "(" + owner.Groups["q"].Value + "onUpdate" + owner.Groups["q"].Value +
            ", function(delta)\n";
        if (!cb.StartsWith(header, StringComparison.Ordinal))
            throw new InvalidOperationException("ITP original frame registration prefix drift.");
        var frameBody = cb[header.Length..];
        var emitter = "-- " + stage + ": one pausable shared frame owner\n" +
            "local function __gcetItppFrameStepV1(delta)\n" + frameBody + "\nend\n" +
            "local __gcetItppRuntimeV1 = nil\n" +
            "do\n" +
            "  local ok, host = pcall(GetMod, \"0-Engine\")\n" +
            "  if ok and type(host) == \"table\" and type(host.__gcetScheduleDriver) == \"function\"\n" +
            "     and type(host.Register) == \"function\" then\n" +
            "    local okClient, client = pcall(host.Register, \"immersive_third_person\")\n" +
            "    if okClient and type(client) == \"table\" and type(client.Schedule) == \"table\"\n" +
            "       and type(client.Schedule.EveryFrame) == \"function\" then\n" +
            "      __gcetItppRuntimeV1 = client\n" +
            "    end\n" +
            "  end\n" +
            "end\n" +
            "if __gcetItppRuntimeV1 then\n" +
            "  local ok, controller = pcall(function()\n" +
            ItppAuthorWorkerLoader + "\n" +
            "  end)\n" +
            ItppFileLoadRegisterBridgeV2 + "\n" +
            "end\n" +
            "if not mod.__gcetItppDormancyV1 then\n" +
            "  " + owner.Groups["registrar"].Value +
            "(" + owner.Groups["q"].Value + "onUpdate" + owner.Groups["q"].Value +
            ", __gcetItppFrameStepV1)\n" +
            "end";
        normalized = normalized[..owner.Index] + emitter + normalized[(ending + "\nend)".Length)..];

        string ReplaceOne(string oldValue, string newValue, string purpose)
        {
            if (normalized.Split(oldValue, StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("ITP dormancy " + purpose + " source anchor drift.");
            normalized = normalized.Replace(oldValue, newValue, StringComparison.Ordinal);
            return normalized;
        }
        ReplaceOne("  mod.applyConsumableTiming(\"FPP\", \"init\")\nend)",
            "  mod.applyConsumableTiming(\"FPP\", \"init\")\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Start() end\nend)",
            "onInit startup");
        ReplaceOne("registerForEvent(\"onShutdown\", function()\n",
            "registerForEvent(\"onShutdown\", function()\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Stop() end\n",
            "shutdown");
        ReplaceOne("local function autoTppHandleToggle()\n",
            "local function autoTppHandleToggle()\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"toggle\") end\n",
            "CET/native/UI toggle");
        ReplaceOne("local function applyTppMode()\n",
            "local function applyTppMode()\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"enter\") end\n",
            "TPP activation");
        ReplaceOne("local function restoreFppMode(immediate, headBackImmediate)\n",
            "local function restoreFppMode(immediate, headBackImmediate)\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"restore\") end\n",
            "FPP restoration");
        ReplaceOne("registerForEvent(\"onOverlayOpen\", function()\n",
            "registerForEvent(\"onOverlayOpen\", function()\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"overlay\") end\n",
            "CET overlay wake");
        ReplaceOne("registerForEvent(\"onOverlayClose\", function()\n",
            "registerForEvent(\"onOverlayClose\", function()\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"overlay-close\") end\n",
            "CET overlay close");
        ReplaceOne("mod.onTakeOverRepresentation = function(enable)\n",
            "mod.onTakeOverRepresentation = function(enable)\n" +
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"takeover\") end\n",
            "takeover change");
        // Native Mod Settings UI may change which services are needed while
        // asleep; its original callback is an independent wake.
        var settings = Regex.Match(normalized, @"(?m)^mod\.nativeSettingsApply = function\([^\n]*\)\n");
        if (!settings.Success)
            throw new InvalidOperationException("ITP native settings change callback absent.");
        normalized = normalized.Insert(settings.Index + settings.Length,
            "  if mod.__gcetItppDormancyV1 then mod.__gcetItppDormancyV1.Signal(\"settings\") end\n");
        context.WriteCoordinator(coordinator, ItppDormancyWorkerV1);
        return text.Contains("\r\n", StringComparison.Ordinal)
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
    }
}
