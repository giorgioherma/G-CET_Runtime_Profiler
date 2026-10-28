using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    // Verify BOTH the author callback bridge and external frame listener.
    // The onInit registrar introduced a reproducible native CET startup
    // crash. Source-proof file-load registration between the author onInit
    // declaration and shutdown, never trust the V2 marker alone.
    internal static bool IsDedraThirdPersonCombatWakeComplete(string ownerFolder)
    {
        var initPath = Path.Combine(ownerFolder,"init.lua");
        var workerPath = Path.Combine(ownerFolder,"GCET_Semantic","dedra_camera_wake.lua");
        if (!File.Exists(initPath) || !File.Exists(workerPath))
            return false;
        var init = File.ReadAllText(initPath).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        var worker = File.ReadAllText(workerPath).Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        return HasDedraThirdPersonCombatAuthorBridge(init) &&
            worker.StartsWith("-- G-CET semantic coordinator:dedra-third-person-combat-wake\n",StringComparison.Ordinal) &&
            worker.Contains("FrameListeners.MakeRegistrar",StringComparison.Ordinal) &&
            worker.Contains("function M.Register()",StringComparison.Ordinal) &&
            worker.Contains("function M.Start()",StringComparison.Ordinal) &&
            worker.Contains("function M.Signal(proof)",StringComparison.Ordinal) &&
            worker.Contains("function M.Stop()",StringComparison.Ordinal) &&
            worker.Contains("function M.Diagnostics()",StringComparison.Ordinal) &&
            worker.Contains("base.gcetDedraWakeReady",StringComparison.Ordinal) &&
            worker.Contains("handle.Pause()",StringComparison.Ordinal) &&
            worker.Contains("handle.Resume()",StringComparison.Ordinal) &&
            worker.Contains("fallbackRegister(\"onUpdate\",update)",StringComparison.Ordinal);
    }

    // Existing author bridge proves independent callback ownership even when
    // only a missing/older external worker needs replacement. Never infer it
    // from the marker alone.
    internal static bool HasDedraThirdPersonCombatAuthorBridge(string init)
    {
        init=init.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        // Verified bootable Test F: Register() is a top-level source
        // statement, AFTER the authored onInit callback was declared and
        // BEFORE onShutdown. Its prior onInit placement crashes CET at startup.
        return Regex.IsMatch(init,
                @"registerForEvent\s*\(\s*[""']onInit[""']\s*,\s*function\s*\(\s*\)\s*" +
                @"Dedra\.__gcetWakeController\.Start\s*\(\s*\)",
                RegexOptions.CultureInvariant) &&
            Regex.IsMatch(init,
                @"(?m)^Dedra\.__gcetWakeController\.Register\(\)[ \t]*\r?\n" +
                @"registerForEvent\s*\(\s*[""']onShutdown[""']",
                RegexOptions.CultureInvariant) &&
            Regex.Matches(init,@"Dedra\.__gcetWakeController\.Register\s*\(\s*\)",
                RegexOptions.CultureInvariant).Count == 1 &&
            init.Contains("dofile(\"GCET_Semantic/dedra_camera_wake.lua\")",StringComparison.Ordinal) &&
            init.Contains("Dedra.__gcetWakeFromBase = Dedra.__gcetWakeController.Signal",StringComparison.Ordinal) &&
            init.Contains("Dedra.__gcetWakeDiagnostics = function()",StringComparison.Ordinal) &&
            init.Contains("Dedra.__gcetWakeController.Stop()",StringComparison.Ordinal) &&
            !Regex.IsMatch(init,
                @"(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']",
                RegexOptions.CultureInvariant);
    }

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
               (owned is not null && !owned.Contains(marker,StringComparison.Ordinal)))
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
            var existingWorker = owned?.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            // User boot isolation: the onInit registration introduced the
            // native CET 0x57A23F null-pointer crash. Test F booted with
            // precisely the same worker and a file-load Register() instead.
            // Keep the original source's lifecycle: declare onInit first,
            // attach the worker at file load, Start/bind onInit, stop onShutdown.
            // This is one authoritative generation path for pristine, staged
            // and already optimized inputs, never a corrective later pass.
            var crashingInit = "registerForEvent(\"onInit\", function()\n" +
                "  Dedra.__gcetWakeController.Register()\n" +
                "  Dedra.__gcetWakeController.Start()";
            var safeInit = "registerForEvent(\"onInit\", function()\n" +
                "  Dedra.__gcetWakeController.Start()";
            var topRegister = "Dedra.__gcetWakeController.Register()\n";
            const string shutdownAnchor = "registerForEvent(\"onShutdown\", function()";
            var sourceChanged = false;
            if(source.Contains(crashingInit,StringComparison.Ordinal))
            {
                if(source.Split(crashingInit,StringSplitOptions.None).Length!=2 ||
                   source.Contains(topRegister,StringComparison.Ordinal) ||
                   source.Split(shutdownAnchor,StringSplitOptions.None).Length!=2)
                    throw new InvalidOperationException("Dedra crashing onInit registration source drift.");
                source=source.Replace(crashingInit,safeInit,StringComparison.Ordinal)
                    .Replace(shutdownAnchor,topRegister+shutdownAnchor,StringComparison.Ordinal);
                sourceChanged = true;
            }
            else if(!source.Contains(safeInit,StringComparison.Ordinal) ||
                    source.Split(topRegister,StringSplitOptions.None).Length!=2 ||
                    source.Split(shutdownAnchor,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra file-load registration source drift.");
            // Upgrades from the old eager-source variant must also expose
            // the new on-demand gate status; this is the same owned bridge,
            // never an additional update subscription or a repair pass.
            const string bridgeOwner = "Dedra.__gcetWakeFromBase = Dedra.__gcetWakeController.Signal";
            const string statusOwner =
                "Dedra.__gcetWakeDiagnostics = function()\n" +
                "  return Dedra.__gcetWakeController.Diagnostics()\n" +
                "end";
            if(!source.Contains(statusOwner,StringComparison.Ordinal))
            {
                if(source.Split(bridgeOwner,StringSplitOptions.None).Length!=2 ||
                   source.Contains("Dedra.__gcetWakeDiagnostics",StringComparison.Ordinal))
                    throw new InvalidOperationException("Dedra diagnostics bridge source drift.");
                source=source.Replace(bridgeOwner,bridgeOwner+"\n"+statusOwner,
                    StringComparison.Ordinal);
                sourceChanged=true;
            }
            if(!HasDedraThirdPersonCombatAuthorBridge(source))
                throw new InvalidOperationException(
                    "Dedra safe eager registration or worker bridge postcondition failed.");
            if(sourceChanged)
                context.Write(file,file.Text.Contains("\r\n",StringComparison.Ordinal)
                    ? source.Replace("\n","\r\n",StringComparison.Ordinal):source);
            if(existingWorker==expectedWorker)
                return sourceChanged
                    ? SemanticInjectionResult.Success("Restored boot-safe file-load registration without duplicate frame ownership.")
                    : SemanticInjectionResult.Skip("Read-only-safe detached wake already installed.");

            // Recognize both the immediately preceding generated worker and
            // the historic read-only-proxy mutation variant. Never require a
            // previous output for generation from untouched originals.
            // On Windows, C# verbatim string constants keep CRLF from checkout.
            // Normalize both operands before comparing known generated worker
            // variants; otherwise a pristine build passes but staged upgrades
            // incorrectly reject our own earlier worker.
            var normalizedDiagnostics = DiagnosticsMethod
                .Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            if (canonicalBody.Split(normalizedDiagnostics,StringSplitOptions.None).Length != 2)
                throw new InvalidOperationException("Dedra diagnostics worker body drift.");
            var priorWorkerBody = canonicalBody.Replace(normalizedDiagnostics,"",StringComparison.Ordinal);
            const string readinessProbe =
                "    -- Source-proven capability exported by the ITP generator. This is\n" +
                "    -- read-only and can be checked during Dedra's own startup, even when\n" +
                "    -- the base onInit proof signal was emitted earlier in load order.\n" +
                "    -- Unmodified/partial ITP stays fail-open until a real toggle proof.\n" +
                "    if not baseBridgeConfirmed and type(base.gcetDedraWakeReady) == \"function\" then\n" +
                "      local okReady, ready = pcall(base.gcetDedraWakeReady)\n" +
                "      if okReady and ready == true then baseBridgeConfirmed = true end\n" +
                "    end\n";
            var previousBody = priorWorkerBody.Replace(readinessProbe,"",StringComparison.Ordinal);
            var previousWorker = coordinatorHeader + priorWorkerBody;
            var preCapabilityWorker = coordinatorHeader + previousBody;
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
            if(owned is not null && existingWorker!=legacyWorker &&
               existingWorker!=previousWorker && existingWorker!=preCapabilityWorker)
                throw new InvalidOperationException(
                    "Dedra worker differs from known generated versions; refuse unsafe overwrite.");
            if(owned is null && !HasDedraThirdPersonCombatAuthorBridge(source))
                throw new InvalidOperationException(
                    "Missing Dedra worker and unproven author bridge; refuse unsafe recovery.");
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
        Dedra.__gcetWakeDiagnostics = function()
          return Dedra.__gcetWakeController.Diagnostics()
        end

        """;
        const string onInit="registerForEvent(\"onInit\", function()";
        if(source.Split(onInit,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra onInit callback ambiguous.");
        source=source.Replace(onInit,bridge+onInit+"\n  Dedra.__gcetWakeController.Start()",
            StringComparison.Ordinal);
        var call=callback[0].Value;
        if(source.Split(call,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Dedra update registration drifted.");
        source=source.Replace(call,"",StringComparison.Ordinal);
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
        // The proven working startup loads the complete external worker at
        // module load, after the author onInit registration. Do not register a
        // native frame listener from within CET's onInit callback.
        source=source.Replace(shutdown,
            "Dedra.__gcetWakeController.Register()\n"+shutdown+
            "\n  Dedra.__gcetWakeController.Stop()",StringComparison.Ordinal);

        if(!HasDedraThirdPersonCombatAuthorBridge(source))
            throw new InvalidOperationException(
                "Dedra pristine generation did not preserve the boot-safe lifecycle.");
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
    // Read-only, on-demand diagnostics: no timer, watcher, or per-frame
    // allocation. Ask the loaded worker why its pausable path is unavailable.
    private const string DiagnosticsMethod = @"  function M.Diagnostics()
    local p = nil
    local ok = false
    if api and type(api.perspective) == ""table"" and
      type(api.perspective.get) == ""function"" then
      ok, p = pcall(api.perspective.get)
    end
    return {
      registered = handle ~= nil, pausable = pausable,
      bridge = baseBridgeConfirmed, linked = linked,
      perspectiveReadable = ok and type(p) == ""table"",
      requested = type(p) == ""table"" and p.userWantsThirdPerson == true or false,
      mode = type(p) == ""table"" and p.mode or nil,
      transitioning = type(p) == ""table"" and p.transitioning == true or false,
      pendingCleanup = pending(p),
      listenerActive = handle and type(handle.IsActive) == ""function"" and handle.IsActive() == true or false
    }
  end
";
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
  function M.Diagnostics()
    local p = nil
    local ok = false
    if api and type(api.perspective) == ""table"" and
      type(api.perspective.get) == ""function"" then
      ok, p = pcall(api.perspective.get)
    end
    return {
      registered = handle ~= nil, pausable = pausable,
      bridge = baseBridgeConfirmed, linked = linked,
      perspectiveReadable = ok and type(p) == ""table"",
      requested = type(p) == ""table"" and p.userWantsThirdPerson == true or false,
      mode = type(p) == ""table"" and p.mode or nil,
      transitioning = type(p) == ""table"" and p.transitioning == true or false,
      pendingCleanup = pending(p),
      listenerActive = handle and type(handle.IsActive) == ""function"" and handle.IsActive() == true or false
    }
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
