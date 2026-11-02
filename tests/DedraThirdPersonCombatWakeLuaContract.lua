-- Execute the actual V3 worker extracted from the authoritative source.
-- CET registration must NEVER occur inside Start() or frame/event callbacks.
local file = assert(arg[1], "generated Dedra scheduler worker required")
local worker = assert(loadfile(file))()
local function check(ok, message) assert(ok, message) end
local function harness(opts)
  opts = opts or {}
  local requested = opts.requested == true
  local cameraMode, cameraOverride, transitioning = opts.mode, opts.override, false
  local events, engineEvents, jobs = {}, {}, {}
  local frames, calls, quietCalls, nativeRegistrations = 0, 0, 0, 0
  local addon = {takeover=false,on=nil,armSent=nil,standIn=nil}
  local base = {
    events={
      on=function(kind,owner,fn)
        if opts.rejectListener then return -3 end
        events[kind]=fn
        return 2
      end,
      off=function(kind,owner) events[kind]=nil end
    },
    perspective={
      get=function()
        return {userWantsThirdPerson=requested,
          mode=cameraMode or (requested and "third" or "first"),
          transitioning=transitioning,override=cameraOverride or "none"}
      end
    },
    gcetDedraWakeReady=function() return opts.noCapability~=true end
  }
  local schedule={}
  local function makeJob(kind,interval,options,fn)
    local job={kind=kind,interval=interval,fn=fn,
      active=options.active~=false,elapsed=0,next=interval,cancelled=false}
    function job.Pause() job.active=false end
    function job.Resume() if not job.cancelled then job.active=true end end
    function job.Cancel() job.active=false;job.cancelled=true end
    function job.IsActive() return job.active and not job.cancelled end
    jobs[#jobs+1]=job
    return job
  end
  schedule.EveryFrame=function(options,fn) return makeJob("frame",0,options,fn) end
  schedule.Every=function(interval,options,fn) return makeJob("timed",interval,options,fn) end
  local client={Schedule=schedule,
    Subscribe=function(name,fn)
      engineEvents[name]=fn
      return {unsubscribe=function() engineEvents[name]=nil end}
    end}
  local host={__gcetScheduleDriver=function() end,
    Register=function(name)
      check(name=="DedraThirdPersonCombatAddon","unexpected scheduler owner")
      return client
    end}
  GetMod=function(name)
    if name=="0-Engine" then if opts.noEngine then return nil end; return host end
    if name=="immersive_third_person" then if opts.noItp then return nil end; return base end
  end
  registerForEvent=function(...) nativeRegistrations=nativeRegistrations+1 end
  local module=worker.Install(addon,function(dt)
    calls=calls+1
    if dt >= 0.2 then quietCalls=quietCalls+1 end
    -- A real author step reconciles and releases RESET states.
    if opts.autoSettle~=false then
      addon.on=false;addon.armSent=false;addon.standIn=""
      addon.core=(opts.coreUnready and 0 or 1); addon.mode="first"
    end
  end)
  local function tick(dt)
    frames=frames+1
    for _, job in ipairs(jobs) do
      if job.IsActive() then
        job.elapsed=job.elapsed+dt
        if job.kind=="frame" or job.elapsed>=job.interval-1e-6 then
          local elapsed=job.elapsed
          job.elapsed=0
          job.fn({frame=frames,dt=dt,elapsed=elapsed})
        end
      end
    end
  end
  return {
    start=function()
      module.Start()
      -- Existing tests model a fully loaded world unless explicitly testing
      -- the real post-menu warmup boundary below.
      if not opts.deferPlayerReady then
        check(engineEvents.PlayerReady~=nil,"missing canonical PlayerReady subscription")
        engineEvents.PlayerReady()
      end
    end,
    stop=function() module.Stop() end,
    addon=addon,module=module,
    tick=tick,
    run=function(count,dt) for _=1,count do tick(dt or 0.1) end end,
    setRequested=function(v) requested=v end,
    setMode=function(v) cameraMode=v end,
    setOverride=function(v) cameraOverride=v end,
    setTransitioning=function(v) transitioning=v end,
    event=function(name)
      if events[name] then events[name]() end
    end,
    engineEvent=function(name)
      check(engineEvents[name]~=nil,"missing engine wake "..name)
      engineEvents[name]()
    end,
    calls=function() return calls end,
    quietCalls=function() return quietCalls end,
    nativeCount=function() return nativeRegistrations end,
    jobs=function() return jobs end,
    diagnostics=function() return module.Diagnostics() end
  }
end


-- Regression for the actual crash window: CET onInit can run in the
-- main menu, and a REDscript Core=1 / FPP state is not proof that the newly
-- loading game world finished 0-Engine's PlayerReady warmup. Retain the
-- conservative 0.25s reconciler until PlayerReady and resume on teardown.
do
  local t=harness({deferPlayerReady=true})
  t.start()
  t.run(16,0.1)
  local loading=t.diagnostics()
  check(loading.mode=="quiet" and loading.quietJob and
      not loading.activeJob and not loading.playerReady and
      t.calls()>0 and t.nativeCount()==0,
      "main-menu-to-world load parked Dedra before PlayerReady")
  t.engineEvent("PlayerReady")
  t.run(12,0.1)
  local ready=t.diagnostics()
  check(ready.playerReady and ready.mode=="dormant" and
      not ready.quietJob and not ready.activeJob,
      "after warmup Dedra could not enter confirmed idle")
  t.engineEvent("PlayerInvalidated")
  check(not t.diagnostics().playerReady and
      t.diagnostics().mode=="quiet" and t.diagnostics().quietJob,
      "session invalidation did not restore the safety audit")
  t.run(12,0.1)
  check(t.diagnostics().mode=="quiet",
      "unready player incorrectly permitted re-dormancy")
  t.engineEvent("PlayerReady")
  t.run(12,0.1)
  check(t.diagnostics().mode=="dormant",
      "new game world readiness did not permit dormancy")
  t.event("sessionEnd")
  check(t.diagnostics().mode=="quiet" and
      not t.diagnostics().playerReady,
      "ITP session end left the worker dormant with a stale player")
  t.stop()
  check(t.nativeCount()==0,"lifecycle gating introduced late CET native registration")
end

-- Real Dedra author state: OnGameAttached sets on=nil, armSent=nil.
-- Both are NOT active holds. An unavailable Core can leave nil in place,
-- so the original 0.25s reconciliation must continue without a frame job.
do
  local t=harness({autoSettle=false})
  t.start()
  t.run(15,0.1)
  local d=t.diagnostics()
  check(d.mode=="quiet" and d.quietJob and not d.activeJob,
    "author nil state kept Dedra active forever while FPP was idle")
  check(d.initialState.on and d.initialState.armSent and not d.pendingCleanup,
    "unconfirmed native writer state was incorrectly classified as a live hold")
  local prior=t.calls()
  t.run(10,0.1)
  check(t.calls()>prior and t.diagnostics().quietCalls>0,
    "unconfirmed Core state lost the conservative full step audit")
  check(t.nativeCount()==0,"nil-author recovery registered a late native hook")
  t.stop()
end

-- Ordinary FPP combat/aim/weapon events must not revive the full author step.
do
  local t=harness()
  t.start()
  t.run(12,0.1)
  check(t.diagnostics().mode=="dormant","FPP startup failed to detach both workers")
  local prior=t.calls()
  for i=1,100 do
    t.engineEvent("WeaponStateChanged")
    t.engineEvent("AimStateChanged")
    t.engineEvent("CombatStateChanged")
    t.event("overrideChanged")
    t.tick(0.05)
  end
  check(t.calls()==prior and t.diagnostics().mode=="dormant" and
    not t.diagnostics().quietJob and not t.diagnostics().activeJob,
    "routine FPP combat reactivated the full Dedra author step")
  t.addon.on=nil;t.addon.armSent=nil
  t.module.Signal("attach")
  check(t.diagnostics().mode=="quiet" and t.diagnostics().quietJob,
    "new player attachment did not restore required unconfirmed recovery")
  t.run(12,0.1)
  check(t.diagnostics().mode=="dormant",
    "Core/arm recovery never returned to physical sleep")
  t.stop()
end

-- Missing/unready REDscript Core may not be misclassified as confirmed idle.
do
  local t=harness({coreUnready=true})
  t.start()
  t.run(20,0.1)
  local d=t.diagnostics()
  check(d.mode=="quiet" and d.quietJob and d.quietCalls>0,
    "unready Core lost the original conservative audit")
  t.stop()
end

-- A *confirmed active* REDscript/animation hold, unlike nil, must keep
-- full-frame cleanup until the author marks it released.
do
  local t=harness({autoSettle=false})
  t.addon.on=true
  t.addon.armSent=true
  t.start()
  t.run(15,0.1)
  check(t.diagnostics().mode=="active" and t.diagnostics().pendingCleanup,
    "live animation/arm hold slept before releasing Core inputs")
  t.addon.on=false
  t.addon.armSent=false
  t.engineEvent("WeaponStateChanged")
  t.run(15,0.1)
  check(t.diagnostics().mode=="quiet",
    "confirmed released hold did not permit FPP dormancy")
  t.stop()
end

do
  local t=harness()
  t.start()
  check(#t.jobs()==2,"Dedra did not use two existing schedule lanes")
  check(t.nativeCount()==0,"Dedra registered native callback during onInit")
  t.run(12,0.1)
  local d=t.diagnostics()
  check(d.mode=="dormant" and not d.activeJob and not d.quietJob,
    "confirmed FPP still executes the original full quiet worker")
  local before=t.calls()
  t.run(20,0.1)
  check(t.calls()==before,"confirmed dormant FPP executed author reconciliation")
  local slowCount=t.diagnostics().quietCalls
  check(slowCount>0,"initial Core reconciliation was omitted")
  t.setRequested(true)
  t.event("perspectiveChanged")
  check(t.diagnostics().activeJob and not t.diagnostics().quietJob,
    "ITP F7/slider request did not wake full-frame lane")
  local wakeBefore=t.calls()
  t.tick(0.1)
  check(t.calls()==wakeBefore+1,"TPP wake did not execute author step")
  t.addon.takeover=true
  t.setRequested(false)
  t.event("transitionEnd")
  t.run(10,0.1)
  check(t.diagnostics().mode=="active",
    "combat takeover cleanup incorrectly entered dormant FPP")
  t.addon.takeover=false
  t.engineEvent("WeaponStateChanged")
  t.run(15,0.1)
  check(t.diagnostics().mode=="dormant","event wake never settled into dormant FPP")
  t.stop()
  check(not t.diagnostics().activeJob and not t.diagnostics().quietJob,
    "shutdown left scheduler jobs running")
  check(t.nativeCount()==0,"late CET registration occurred")
end

-- Captured regression: sustained FPP combat emitted OnAction/weapon/aim
-- wakes for most of 60s; no user TPP request, no camera takeover or debt.
-- An ITP combat override does NOT mean Dedra owns a third-person camera.
do
  local t=harness({override="combat",autoSettle=false})
  t.start()
  t.run(12,0.1)
  local start=t.calls()
  for i=1,120 do
    t.engineEvent("WeaponStateChanged")
    t.engineEvent("AimStateChanged")
    t.engineEvent("UpperBodyStateChanged")
    t.engineEvent("CombatStateChanged")
    t.event("overrideChanged")
    t.tick(0.05)
    local d=t.diagnostics()
    check(d.mode=="quiet" and not d.activeJob and d.quietJob,
      "FPP combat wakes reactivated full-frame Dedra without a camera request")
    check(d.activeReason=="quiet" and d.settle==0,
      "FPP combat override or event extended the full-rate settle window")
  end
  local d=t.diagnostics()
  check(t.calls()-start<=30 and d.quietCalls>0,
    "FPP combat performed more than the bounded 0.25s Core audit")
  check(d.signals.engine>=480 and d.signals.itp>=120,
    "FPP combat signal coverage missing in runtime test")
  check(d.perspective and d.perspective.requested==false and
    d.perspective.override=="combat","FPP override proof missing")
  check(t.nativeCount()==0,"FPP combat registered a native callback")
  t.stop()
end

-- ADS may force the CAMERA to FPP while the player still requests TPP.
-- That must continue to run every frame for Dedra's combat takeover.
do
  local t=harness({requested=true,mode="first",override="combat"})
  t.start()
  t.run(8,0.1)
  check(t.diagnostics().activeReason=="third-person-requested" and
    t.diagnostics().activeJob,"ADS-forced first-person hid TPP intent")
  t.setRequested(false)
  t.engineEvent("AimStateChanged")
  t.run(15,0.1)
  check(t.diagnostics().mode=="dormant",
    "explicit FPP request did not release TPP combat cadence")
  t.stop()
end

-- Only an actual camera toggle gets the short pre-transition wake bridge.
do
  local t=harness()
  t.start()
  t.run(12,0.1)
  t.module.Signal("__gcetItpToggleWakeV2")
  check(t.diagnostics().mode=="active" and
    t.diagnostics().activeReason=="explicit-toggle-settle",
    "ITP user-toggle signal did not bridge transient camera state")
  t.run(15,0.1)
  check(t.diagnostics().mode=="dormant" and
    t.diagnostics().signals.toggle==1,
    "explicit camera transition failed to settle back into FPP")
  t.stop()
end

-- Unknown ITP state and confirmed hold/transition still fail OPEN to
-- full author cadence, not a forced schedule sleep.
do
  local t=harness()
  t.start()
  t.setMode("transition")
  t.event("transitionStart")
  check(t.diagnostics().activeReason=="camera-transition" and
    t.diagnostics().activeJob,"real camera transition slept")
  t.setMode("first")
  t.setTransitioning(false)
  t.event("transitionEnd")
  t.run(12,0.1)
  check(t.diagnostics().mode=="dormant","completed camera transition stayed active")
  t.setMode("unknown")
  t.event("perspectiveChanged")
  check(t.diagnostics().activeJob and
    t.diagnostics().activeReason=="non-first-person-camera",
    "unknown camera mode did not fail open")
  t.stop()
end

do
  local t=harness({noItp=true})
  t.start()
  t.run(20)
  check(t.diagnostics().mode=="active","standalone mod failed open to original cadence")
  check(t.calls()==20,"missing ITP dropped author updates")
  t.stop()
end

do
  local t=harness({rejectListener=true})
  t.start()
  t.run(15)
  check(t.diagnostics().mode=="active","partially rejecting ITP listener slept unsafely")
  t.stop()
end

do
  local t=harness({noCapability=true})
  t.start()
  t.run(15)
  check(t.diagnostics().mode=="active","unproven read-only ITP capability slept unsafely")
  t.stop()
end

do
  local t=harness({noEngine=true})
  local ok=pcall(t.start)
  check(not ok,"missing composed 0-Engine silently lost execution")
  check(t.nativeCount()==0,"worker attempted crashing late registration fallback")
end

print("Dedra runtime: confirmed FPP parked; unready Core retries; TPP and lifecycle wake PASS")
