using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
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
        if warm > 0 or sessionWarm > 0 or needFrame() then
          if not handle.IsActive() then handle.Resume() end
        elseif handle.IsActive() then
          handle.Pause()
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
        if type(client.Subscribe) == "function" then
          for _, name in ipairs({ "PlayerReady", "PlayerInvalidated" }) do
            local ok, h = pcall(client.Subscribe, name,
              function() M.Signal(name) end)
            if ok and h then subscriptions[#subscriptions + 1] = h end
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
      end
      function M.Diagnostics()
        return { mode = stopped and "stopped" or
          (handle and handle.IsActive() and "active" or "dormant"),
          activeJob = handle and handle.IsActive() or false,
          started = started, calls = calls, pauses = pauses,
          wakes = wakes, lastWake = wakeReason,
          pendingCleanup = needFrame(), nativeKeyFallback = hasNativeBindings(),
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
        if (existing != null && existing.Replace("\r\n", "\n", StringComparison.Ordinal) != expected)
            throw new InvalidOperationException("ITP dormant frame worker drifted; refusing overwrite.");
        if (normalized.Contains(stage, StringComparison.Ordinal))
        {
            if (!normalized.Contains("local function __gcetItppFrameStepV1(delta)", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Start()", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Stop()", StringComparison.Ordinal) ||
                !normalized.Contains("mod.__gcetItppDormancyV1.Signal(\"toggle\")", StringComparison.Ordinal) ||
                !normalized.Contains("itp_frame_dormancy.lua", StringComparison.Ordinal) ||
                !normalized.Contains("if not mod.__gcetItppDormancyV1 then", StringComparison.Ordinal) ||
                Regex.IsMatch(normalized,
                    @"(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\("))
                throw new InvalidOperationException("ITP dormant frame stage incomplete or has duplicate owner.");
            if (existing == null)
            {
                context.WriteCoordinator(coordinator, ItppDormancyWorkerV1);
                recoveredWorker = true;
            }
            return text;
        }
        // Full current original only: compact test/historical input retains
        // its existing conservative frame loop. Unknown layouts fail closed.
        if (!normalized.Contains("mod.nativeSettingsSaveTick = function(delta)", StringComparison.Ordinal) ||
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
            "    return dofile(\"GCET_Semantic/itp_frame_dormancy.lua\").Install(\n" +
            "      mod, state, config, __gcetItppFrameStepV1, __gcetItppRuntimeV1)\n" +
            "  end)\n" +
            "  if ok and controller then mod.__gcetItppDormancyV1 = controller end\n" +
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
