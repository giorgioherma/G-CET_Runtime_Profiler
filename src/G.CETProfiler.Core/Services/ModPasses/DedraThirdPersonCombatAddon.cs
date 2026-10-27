using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    // V2 supersedes V1 frame-probe design. Keep the original combat step and
    // use a mod-owned separate coordinator for 0-Engine detach/resume.
    private static SemanticInjectionResult ApplyDedraThirdPersonCombatWake(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function dedraReadItp()",
            "local function dedraStep(dt)",
            "local function dedraLeaveTakeover(player, backToFirst)",
            "registerForEvent(\"onInit\", function()");
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        const string marker="__gcetDedraDetachedWakeV2";
        const string workerFile="dedra_camera_wake.lua";
        var owned=context.TryReadOwnedCoordinator(workerFile);
        if(source.Contains(marker,StringComparison.Ordinal))
        {
            if(!source.Contains("Dedra.__gcetWakeController.Register()",StringComparison.Ordinal) ||
               !source.Contains("Dedra.__gcetWakeController.Start()",StringComparison.Ordinal) ||
               !source.Contains("Dedra.__gcetWakeFromBase",StringComparison.Ordinal) ||
               owned is null || !owned.Contains(marker,StringComparison.Ordinal))
                throw new InvalidOperationException("Dedra V2 partial stage or missing worker.");
            // A previous G-CET pass installed a worker that read an impossible
            // flag from ITP's read-only API. Match the exact authored worker
            // contract, not its marker alone, and upgrade that worker in place.
            // Never require this older output for pristine generation.
            const string coordinatorHeader =
                "-- G-CET semantic coordinator:dedra-third-person-combat-wake\n";
            // Verbatim C# source literals acquire the checkout's line endings;
            // staged Lua is normalized to LF by SemanticTextFile. Compare those
            // normalized representations, not Windows-vs-Unix source formatting.
            var canonicalBody = WorkerBody.Replace("\r\n","\n",StringComparison.Ordinal)
                .Replace('\r','\n');
            var expectedWorker = coordinatorHeader + canonicalBody;
            var existingWorker = owned.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            if(existingWorker==expectedWorker)
                return SemanticInjectionResult.Skip("Read-only-safe detached wake already installed.");

            // Recognize both the immediately preceding generated worker and
            // the historic read-only-proxy mutation variant. Never require a
            // previous output for generation from untouched originals.
            const string readinessProbe =
                "    -- Source-proven capability exported by the ITP generator. This is\n" +
                "    -- read-only and can be checked during Dedra's own startup, even when\n" +
                "    -- the base onInit proof signal was emitted earlier in load order.\n" +
                "    -- Unmodified/partial ITP stays fail-open until a real toggle proof.\n" +
                "    if not baseBridgeConfirmed and type(base.gcetDedraWakeReady) == \"function\" then\n" +
                "      local okReady, ready = pcall(base.gcetDedraWakeReady)\n" +
                "      if okReady and ready == true then baseBridgeConfirmed = true end\n" +
                "    end\n";
            var previousBody = canonicalBody.Replace(readinessProbe,"",StringComparison.Ordinal);
            var previousWorker = coordinatorHeader + previousBody;
            var legacyWorker = coordinatorHeader + previousBody
                .Replace("  local baseBridgeConfirmed = false\n","",StringComparison.Ordinal)
                .Replace(
                    "    if not baseBridgeConfirmed or not linked or not api then",
                    "    if not linked or not api or api.__gcetDedraWakeBridgeV2 ~= true then",
                    StringComparison.Ordinal)
                .Replace(
                    "      type(base.perspective.get) ~= \"function\" then",
                    "      type(base.perspective.get) ~= \"function\" or\n" +
                    "      base.__gcetDedraWakeBridgeV2 ~= true then",
                    StringComparison.Ordinal)
                .Replace(
                    "  function M.Signal(proof)\n" +
                    "    if proof == \"__gcetItpToggleWakeV2\" then baseBridgeConfirmed = true end",
                    "  function M.Signal()",StringComparison.Ordinal);
            if(existingWorker!=legacyWorker && existingWorker!=previousWorker)
                throw new InvalidOperationException(
                    "Dedra worker differs from known generated versions; refuse unsafe overwrite.");
            context.WriteCoordinator(workerFile,expectedWorker);
            return SemanticInjectionResult.Success(
                "Upgraded the existing external Dedra worker in place; " +
                "preserved one wake owner and restored read-only-safe dormancy.");
        }
        if(source.Contains("__gcetDedraEventWakeV1",StringComparison.Ordinal))
        {
            var start=source.IndexOf("-- __gcetDedraEventWakeV1:",StringComparison.Ordinal);
            var end=source.IndexOf("registerForEvent(\"onInit\", function()\n  __gcetDedraBind()",start,StringComparison.Ordinal);
            if(start<0 || end<=start ||
                source.IndexOf("-- __gcetDedraEventWakeV1:",start+1,StringComparison.Ordinal)>=0 ||
                !source[start..end].Contains("local function __gcetDedraDue(dt)",StringComparison.Ordinal))
                throw new InvalidOperationException("Dedra V1 helper changed; refuse unsafe migration.");
            source=source[..start]+source[end..];
            source=source.Replace("registerForEvent(\"onInit\", function()\n  __gcetDedraBind()",
                "registerForEvent(\"onInit\", function()",StringComparison.Ordinal);
            source=source.Replace(
                "if __gcetDedraDue(dt) then pcall(dedraStep, __gcetDedraStepDelta) end",
                "pcall(dedraStep, dt)",StringComparison.Ordinal);
            source=source.Replace("  Dedra.cfg.combatTpp = state\n  __gcetDedraWake = true\n",
                "  Dedra.cfg.combatTpp = state\n",StringComparison.Ordinal)
                .Replace("  Dedra.cfg.anims[key] = state\n  __gcetDedraWake = true\n",
                "  Dedra.cfg.anims[key] = state\n",StringComparison.Ordinal);
            if(source.Contains("__gcetDedraDue",StringComparison.Ordinal) ||
               source.Contains("__gcetDedraBind",StringComparison.Ordinal) ||
               source.Contains("__gcetDedraWake",StringComparison.Ordinal))
                throw new InvalidOperationException("Dedra V1 leftovers prevent clean replacement.");
        }
        // Only the one author callback is adopted. Existing generic 0-Engine
        // registerForEvent helper may remain unused; no duplicate registration.
        var callback=Regex.Matches(source,
            @"(?m)^[ \t]*(?:registerForEvent|__gcetRegisterEvent_[0-9]+)\([""']onUpdate[""'],[ \t]*function\(dt\)\s*\n[ \t]*pcall\(dedraStep,[ \t]*dt\)\s*\n[ \t]*end\)",
            RegexOptions.CultureInvariant);
        if(callback.Count!=1 ||
           !source.Contains("Dedra.cfg.combatTpp and wantsTpp and combatFirst and not blocked",
                StringComparison.Ordinal) ||
           !source.Contains("pcall(dedraLeaveTakeover, player, true)",StringComparison.Ordinal) ||
           !source.Contains("Dedra.itp.perspective.release(\"dedra.aim\")",StringComparison.Ordinal) ||
           !source.Contains("  Observe(\"PlayerPuppet\", \"OnGameAttached\", function()",StringComparison.Ordinal))
            throw new InvalidOperationException("Dedra camera/gameplay lifecycle drift; no detached wake.");
        if(owned is not null && !owned.Contains(marker,StringComparison.Ordinal))
            throw new InvalidOperationException("Different coordinator owns Dedra wake filename.");

        const string bridge = """
        -- __gcetDedraDetachedWakeV2: worker owns pausable frame subscription.
        Dedra.__gcetWakeController = dofile("GCET_Semantic/dedra_camera_wake.lua").Install(
          Dedra, dedraStep, registerForEvent)
        Dedra.__gcetWakeFromBase = Dedra.__gcetWakeController.Signal

        """;
        const string onInit="registerForEvent(\"onInit\", function()";
        if(source.Split(onInit,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra onInit callback ambiguous.");
        source=source.Replace(onInit,bridge+onInit+"\n  Dedra.__gcetWakeController.Start()",
            StringComparison.Ordinal);
        var call=callback[0].Value;
        if(source.Split(call,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra update registration drifted.");
        source=source.Replace(call,"Dedra.__gcetWakeController.Register()",StringComparison.Ordinal);
        // The author OnGameAttached remains the event that refreshes the player
        // state; our worker rebinds/rechecks it without starting a timer.
        const string reset="    Dedra.armSent = nil\n  end)";
        if(source.Split(reset,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra player reset hook changed.");
        source=source.Replace(reset,
            "    Dedra.armSent = nil\n    Dedra.__gcetWakeController.Signal()\n  end)",
            StringComparison.Ordinal);
        const string shutdown="registerForEvent(\"onShutdown\", function()";
        if(source.Split(shutdown,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra shutdown hook changed.");
        source=source.Replace(shutdown,shutdown+"\n  Dedra.__gcetWakeController.Stop()",
            StringComparison.Ordinal);

        context.WriteCoordinator(workerFile,
            "-- G-CET semantic coordinator:dedra-third-person-combat-wake\n" +
            WorkerBody);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            source=source.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,source);
        return SemanticInjectionResult.Success(
            "Dedra combat update is detached from shared frame emitter while " +
            "TPP is not requested and cleanup is complete. Original base CET toggle " +
            "signals user intent even during deferred combat camera; base perspective, " +
            "override and session events provide wake. No dormant timer or frame probes; " +
            "without a proven ITP bridge/0-Engine, author cadence is preserved.");
    }
    private const string WorkerBody = @"-- __gcetDedraDetachedWakeV2: original author step only when user requests TPP or cleanup is pending
local M = {}
function M.Install(Dedra, step, fallbackRegister)
  local handle, api, linked = nil, nil, false
  local baseBridgeConfirmed = false
  local pausable = false
  local kinds = { ""perspectiveChanged"", ""overrideChanged"", ""transitionStart"",
    ""transitionEnd"", ""sessionStart"", ""sessionEnd"" }
  local owner = ""gcet.dedra.detached.v2""
  local function pending(p)
    if Dedra.takeover == true or Dedra.easeOut ~= nil
      or Dedra.aimAsk ~= nil or Dedra.planeHeld == true then return true end
    if p and p.userWantsThirdPerson then return false end
    return Dedra.on == true or Dedra.armSent == true
      or Dedra.headLookHeld == true or (Dedra.standIn ~= nil and Dedra.standIn ~= """")
  end
  local function sync()
    if not pausable or not handle then return end
    if not baseBridgeConfirmed or not linked or not api then
      handle.Resume()  -- Older ITP builds fail open, never strand a toggle.
      return
    end
    local ok, p = pcall(api.perspective.get)
    if not ok or type(p) ~= ""table"" or
      type(p.userWantsThirdPerson) ~= ""boolean"" then
      handle.Resume()
      return
    end
    if p.userWantsThirdPerson == true or p.transitioning == true or
       p.mode == ""transition"" or pending(p) then
      handle.Resume()
    else
      handle.Pause() -- Unsubscribes from 0-Engine shared frame emitter.
    end
  end
  local function notify()
    sync() -- Synchronous wake from original F7/UI or base API events.
  end
  local function bind()
    local ok, base = pcall(GetMod, ""immersive_third_person"")
    if not ok or type(base) ~= ""table"" or
      type(base.events) ~= ""table"" or type(base.events.on) ~= ""function"" or
      type(base.perspective) ~= ""table"" or
      type(base.perspective.get) ~= ""function"" then
      linked = false
      if handle then handle.Resume() end
      return
    end
    -- Source-proven capability exported by the ITP generator. This is
    -- read-only and can be checked during Dedra's own startup, even when
    -- the base onInit proof signal was emitted earlier in load order.
    -- Unmodified/partial ITP stays fail-open until a real toggle proof.
    if not baseBridgeConfirmed and type(base.gcetDedraWakeReady) == ""function"" then
      local okReady, ready = pcall(base.gcetDedraWakeReady)
      if okReady and ready == true then baseBridgeConfirmed = true end
    end
    Dedra.itp = base
    if linked and api == base then sync(); return end
    api, linked = base, false
    local acceptedAll = true
    for _, kind in ipairs(kinds) do
      local okEvent, status = pcall(base.events.on,kind,owner,notify)
      if not okEvent or status ~= 2 then acceptedAll = false end
    end
    linked = acceptedAll
    sync()
  end
  local function update(dt)
    pcall(step, dt) -- Original game-combat and cleanup behavior unchanged.
    if not linked then bind() else sync() end
  end
  function M.Register()
    if handle then return end
    local ok, engine = pcall(GetMod, ""0-Engine"")
    if ok and type(engine) == ""table"" and
      type(engine.FrameListeners) == ""table"" and
      type(engine.FrameListeners.MakeRegistrar) == ""function"" then
      local good, registrar = pcall(engine.FrameListeners.MakeRegistrar,
        ""DedraThirdPersonCombatAddon"", fallbackRegister)
      if good and type(registrar) == ""function"" then
        local made, h = pcall(registrar, ""onUpdate"", update)
        if made and type(h) == ""table"" and
          type(h.Pause) == ""function"" and
          type(h.Resume) == ""function"" then
          handle, pausable = h, true
          return
        end
        if made then return end -- Already registered; never duplicate.
      end
    end
    fallbackRegister(""onUpdate"",update) -- No 0-Engine: author's original cadence.
  end
  function M.Start()
    bind() -- One startup read; no dormant watchdog.
  end
  function M.Signal(proof)
    if proof == ""__gcetItpToggleWakeV2"" then baseBridgeConfirmed = true end
    if not linked then bind() else notify() end
  end
  function M.Stop()
    if api and type(api.events) == ""table"" and
      type(api.events.off) == ""function"" then
      for _, kind in ipairs(kinds) do pcall(api.events.off,kind,owner) end
    end
    if handle and type(handle.Cancel) == ""function"" then handle.Cancel() end
    linked = false
  end
  return M
end
return M
";
}
