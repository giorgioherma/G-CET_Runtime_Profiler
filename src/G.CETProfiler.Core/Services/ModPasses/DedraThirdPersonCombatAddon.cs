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
    // A single source-owned pass emits a shared scheduler bridge from stock
    // source or upgrades an exact known former G-CET worker. No CET event
    // registration is attempted during onInit, onUpdate or a schedule callback.
    private static SemanticInjectionResult ApplyDedraThirdPersonCombatWake(
        SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function dedraReadItp()",
            "local function dedraStep(dt)",
            "local function dedraLeaveTakeover(player, backToFirst)",
            "registerForEvent(\"onInit\", function()");
        var original=file.Text;
        var crlf=original.Contains("\r\n",StringComparison.Ordinal);
        var source=original.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        const string oldMarker="__gcetDedraDetachedWakeV2";
        const string marker="__gcetDedraSharedScheduleV3";
        const string workerFile="dedra_camera_wake.lua";
        const string workerHeader="-- G-CET semantic coordinator:dedra-third-person-combat-wake\n";
        var canonical=workerHeader+WorkerV3.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        var owned=context.TryReadOwnedCoordinator(workerFile);
        var current=owned?.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');

        // The composed 0-Engine host must actually adopt its ONE original
        // CET frame source. Never trust only module inventory or API names.
        var host=context.TryReadComposedModFile("0-Engine","init.lua");
        var hostProven=host is not null &&
            host.Contains("Engine.__gcetScheduleDriver({",StringComparison.Ordinal) &&
            host.Contains("__host.__gcetScheduleDriver = function(ctx)",StringComparison.Ordinal) &&
            host.Contains("pcall(UpdateFrame, delta)",StringComparison.Ordinal) &&
            host.Contains("registerForEvent(\"onUpdate\", function(delta)",StringComparison.Ordinal);
        if(!hostProven)
        {
            if(source.Contains(marker,StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Installed Dedra shared scheduler has no proven composed 0-Engine driver.");
            return SemanticInjectionResult.Skip(
                "Dedra retains the original file-load CET callback: shared 0-Engine scheduler not source-proven.");
        }

        if(source.Contains(marker,StringComparison.Ordinal))
        {
            if(!HasDedraThirdPersonCombatAuthorBridge(source))
                throw new InvalidOperationException("Dedra V3 bridge drift or duplicate update owner.");
            if(current==canonical)
                return SemanticInjectionResult.Skip("Dedra shared scheduler and external worker complete.");
            if(current is not null)
                throw new InvalidOperationException("Dedra V3 owned coordinator drift; refuse replacement.");
            context.WriteCoordinator(workerFile,canonical);
            return SemanticInjectionResult.Success("Restored missing Dedra-owned scheduler worker.");
        }

        if(source.Contains(oldMarker,StringComparison.Ordinal))
        {
            if(!HasLegacyDedraThirdPersonCombatAuthorBridge(source))
                throw new InvalidOperationException("Dedra historical callback bridge has drifted.");
            if(current is null)
                throw new InvalidOperationException("Prior optimized Dedra worker missing; refuse unsafe migration.");
            // Source-exact compatibility, not marker-only, for all prior
            // G-CET V2 registration/diagnostic/wake versions.
            const string coordinatorHeader =
                "-- G-CET semantic coordinator:dedra-third-person-combat-wake\n";
            var canonicalBody=WorkerBody.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
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
            // One-shot registration status is additive. Exact previous generated
            // workers remain valid upgrade inputs, including old partial workers.
            var currentRegStart=canonicalBody.IndexOf("  function M.Register()\n",StringComparison.Ordinal);
            var currentRegEnd=canonicalBody.IndexOf("  function M.Start()\n",currentRegStart,StringComparison.Ordinal);
            if(currentRegStart<0 || currentRegEnd<=currentRegStart)
                throw new InvalidOperationException("Dedra registration diagnostic source drift.");
            var currentReg=canonicalBody[currentRegStart..currentRegEnd];
            const string enrichedState =
                "  local pausable = false\n" +
                "  -- One-time file-load decision; never queried or allocated in frame callbacks.\n" +
                "  local registrationPath, registrationReason = \"not-attempted\", \"not-attempted\"\n";
            var legacyDiagnostics=PriorDiagnosticsMethod
                .Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
            var oldCanonicalBody=canonicalBody
                .Replace(enrichedState,"  local pausable = false\n",StringComparison.Ordinal)
                .Replace(currentReg,PriorRegisterMethod.Replace("\r\n","\n",StringComparison.Ordinal),StringComparison.Ordinal)
                .Replace(normalizedDiagnostics,legacyDiagnostics,StringComparison.Ordinal);
            if(oldCanonicalBody==canonicalBody ||
               oldCanonicalBody.Contains("registrationReason",StringComparison.Ordinal))
                throw new InvalidOperationException("Dedra prior generated registration shape failed proof.");
            var oldCanonicalWorker=coordinatorHeader+oldCanonicalBody;
            var oldUndiagnosed=oldCanonicalBody.Replace(legacyDiagnostics,"",StringComparison.Ordinal);
            var oldPreviousWorker=coordinatorHeader+oldUndiagnosed;
            var oldPreCapabilityWorker=coordinatorHeader+
                oldUndiagnosed.Replace(readinessProbe,"",StringComparison.Ordinal);
            var oldLegacyWorker=coordinatorHeader+oldUndiagnosed
                .Replace(readinessProbe,"",StringComparison.Ordinal)
                .Replace("  local baseBridgeConfirmed = false\n","",StringComparison.Ordinal)
                .Replace("    if not baseBridgeConfirmed or not linked or not api then",
                    "    if not linked or not api or api.__gcetDedraWakeBridgeV2 ~= true then",
                    StringComparison.Ordinal)
                .Replace("      type(base.perspective.get) ~= \"function\" then",
                    "      type(base.perspective.get) ~= \"function\" or\n" +
                    "      base.__gcetDedraWakeBridgeV2 ~= true then",
                    StringComparison.Ordinal)
                .Replace("  function M.Signal(proof)\n" +
                    "    if proof == \"__gcetItpToggleWakeV2\" then baseBridgeConfirmed = true end",
                    "  function M.Signal()",StringComparison.Ordinal);
            if(owned is not null && existingWorker!=legacyWorker &&
               existingWorker!=previousWorker && existingWorker!=preCapabilityWorker &&
               existingWorker!=oldCanonicalWorker &&
               existingWorker!=oldPreviousWorker &&
               existingWorker!=oldPreCapabilityWorker &&
               existingWorker!=oldLegacyWorker)
                throw new InvalidOperationException(
                    "Dedra worker differs from known generated versions; refuse unsafe overwrite.");

            if(current!=coordinatorHeader+canonicalBody &&
               current!=legacyWorker && current!=previousWorker &&
               current!=preCapabilityWorker && current!=oldCanonicalWorker &&
               current!=oldPreviousWorker && current!=oldPreCapabilityWorker &&
               current!=oldLegacyWorker)
                throw new InvalidOperationException(
                    "Dedra previous coordinator source not recognized; refuse unsafe migration.");
            const string formerBridge=
                "-- __gcetDedraDetachedWakeV2: worker owns pausable frame subscription.";
            if(source.Split(formerBridge,StringSplitOptions.None).Length!=2 ||
               source.Split("Dedra.__gcetWakeController.Register()\n",StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Prior Dedra worker boot-owner layout drift.");
            source=source.Replace(formerBridge,
                "-- G-CET semantic:dedra-third-person-combat-wake\n" +
                "-- __gcetDedraSharedScheduleV3: shared 0-Engine frame execution.",
                StringComparison.Ordinal)
                .Replace("Dedra.__gcetWakeController.Register()\n","",StringComparison.Ordinal);
        }
        else
        {
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

            var callback=Regex.Matches(source,
                @"(?m)^[ \t]*(?:registerForEvent|__gcetRegisterEvent_[0-9]+)\([""']onUpdate[""'],[ \t]*function\(dt\)\s*\n[ \t]*pcall\(dedraStep,[ \t]*dt\)\s*\n[ \t]*end\)",
                RegexOptions.CultureInvariant);
            if(callback.Count!=1 ||
               !source.Contains("Dedra.cfg.combatTpp and wantsTpp and combatFirst and not blocked",StringComparison.Ordinal) ||
               !source.Contains("pcall(dedraLeaveTakeover, player, true)",StringComparison.Ordinal) ||
               !source.Contains("Dedra.itp.perspective.release(\"dedra.aim\")",StringComparison.Ordinal) ||
               !source.Contains("  Observe(\"PlayerPuppet\", \"OnGameAttached\", function()",StringComparison.Ordinal))
                throw new InvalidOperationException("Dedra original gameplay/author callback drift.");

            const string beforeInit="registerForEvent(\"onInit\", function()";
            if(source.Split(beforeInit,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra onInit registration ambiguous.");
            const string bridge= """
-- G-CET semantic:dedra-third-person-combat-wake
-- __gcetDedraSharedScheduleV3: uses existing 0-Engine frame owner.
Dedra.__gcetWakeController = dofile("GCET_Semantic/dedra_camera_wake.lua").Install(
  Dedra, dedraStep)
Dedra.__gcetWakeFromBase = Dedra.__gcetWakeController.Signal
Dedra.__gcetWakeDiagnostics = function()
  return Dedra.__gcetWakeController.Diagnostics()
end

""";
            source=source.Replace(beforeInit,bridge+beforeInit+
                "\n  Dedra.__gcetWakeController.Start()",StringComparison.Ordinal);
            var originalCallback=callback[0].Value;
            if(source.Split(originalCallback,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra author callback duplicated.");
            source=source.Replace(originalCallback,"",StringComparison.Ordinal);
            const string attach="    Dedra.armSent = nil\n  end)";
            if(source.Split(attach,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra source attach reset drift.");
            source=source.Replace(attach,
                "    Dedra.armSent = nil\n    Dedra.__gcetWakeController.Signal()\n  end)",
                StringComparison.Ordinal);
            const string shutdown="registerForEvent(\"onShutdown\", function()";
            if(source.Split(shutdown,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra source shutdown drift.");
            source=source.Replace(shutdown,shutdown+
                "\n  Dedra.__gcetWakeController.Stop()",StringComparison.Ordinal);
        }
        // The author settings callbacks are the highest-fidelity local wake.
        // Do not manufacture a separate UI or native observer.
        const string settingCombat=
            "  Dedra.cfg.combatTpp = state\n  Dedra.reassert = 0\n  dedraSaveConfig()";
        const string settingAnims=
            "  Dedra.cfg.anims[key] = state\n  Dedra.reassert = 0\n  dedraSaveConfig()";
        if(source.Contains("local function dedraSetCombatTpp(state)",StringComparison.Ordinal))
        {
            if(source.Split(settingCombat,StringSplitOptions.None).Length!=2 ||
               source.Split(settingAnims,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("Dedra author settings wake source drift.");
            source=source.Replace(settingCombat,
                settingCombat+"\n  Dedra.__gcetWakeController.Signal()",StringComparison.Ordinal)
                .Replace(settingAnims,
                settingAnims+"\n  Dedra.__gcetWakeController.Signal()",StringComparison.Ordinal);
        }
        if(!HasDedraThirdPersonCombatAuthorBridge(source))
            throw new InvalidOperationException("Dedra new shared scheduler callback ownership failed proof.");
        context.Write(file,crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source);
        context.WriteCoordinator(workerFile,canonical);
        return SemanticInjectionResult.Success(
            "Dedra uses the established 0-Engine driver with exclusive active/quiet lanes; " +
            "all author step/REDscript maintenance remains, with event wake and no late native event registration.");
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
      registrationPath = registrationPath, registrationReason = registrationReason,
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
    private const string PriorRegisterMethod = @"  function M.Register()
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
";
    private const string PriorDiagnosticsMethod = @"  function M.Diagnostics()
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
  -- One-time file-load decision; never queried or allocated in frame callbacks.
  local registrationPath, registrationReason = ""not-attempted"", ""not-attempted""
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
    if not ok then
      registrationReason = ""engine-lookup-error""
    elseif type(engine) ~= ""table"" then
      registrationReason = ""engine-unavailable""
    elseif type(engine.FrameListeners) ~= ""table"" then
      registrationReason = ""frame-service-unavailable""
    elseif type(engine.FrameListeners.MakeRegistrar) ~= ""function"" then
      registrationReason = ""frame-registrar-unavailable""
    else
      local good, registrar = pcall(engine.FrameListeners.MakeRegistrar,
        ""DedraThirdPersonCombatAddon"", fallbackRegister)
      if not good then
        registrationReason = ""make-registrar-error""
      elseif type(registrar) ~= ""function"" then
        registrationReason = ""make-registrar-no-function""
      else
        local made, h = pcall(registrar, ""onUpdate"", update)
        if made and type(h) == ""table"" and
          type(h.Pause) == ""function"" and
          type(h.Resume) == ""function"" then
          handle, pausable = h, true
          registrationPath, registrationReason = ""frame-listener"", ""ok""
          return
        end
        if made then
          registrationPath = ""unmanaged-registration""
          registrationReason = ""no-pausable-handle:"" .. type(h)
          return -- Already registered; never duplicate.
        end
        registrationReason = ""callback-registration-error""
      end
    end
    registrationPath = ""cet-fallback""
    fallbackRegister(""onUpdate"",update) -- No usable 0-Engine: author's original cadence.
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
      registrationPath = registrationPath, registrationReason = registrationReason,
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
