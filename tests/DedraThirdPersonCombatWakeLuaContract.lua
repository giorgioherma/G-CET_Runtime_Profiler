-- Execute the actual V3 worker extracted from the authoritative source.
-- CET registration must NEVER occur inside Start() or frame/event callbacks.
local file = assert(arg[1], "generated Dedra scheduler worker required")
local worker = assert(loadfile(file))()
local function check(ok, message) assert(ok, message) end
local function harness(opts)
  opts = opts or {}
  local requested = opts.requested == true
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
          mode=requested and "third" or "first",
          transitioning=false,override="none"}
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
    if name=="0-Engine" then return opts.noEngine and nil or host end
    if name=="immersive_third_person" then return opts.noItp and nil or base end
  end
  registerForEvent=function(...) nativeRegistrations=nativeRegistrations+1 end
  local module=worker.Install(addon,function(dt)
    calls=calls+1
    if dt >= 0.2 then quietCalls=quietCalls+1 end
    -- A real author step reconciles and releases RESET states.
    if opts.autoSettle~=false then
      addon.on=false;addon.armSent=false;addon.standIn=""
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
    start=function() module.Start() end,
    stop=function() module.Stop() end,
    addon=addon,module=module,
    tick=tick,
    run=function(count,dt) for _=1,count do tick(dt or 0.1) end end,
    setRequested=function(v) requested=v end,
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

do
  local t=harness()
  t.start()
  check(#t.jobs()==2,"Dedra did not use two existing schedule lanes")
  check(t.nativeCount()==0,"Dedra registered native callback during onInit")
  t.run(12,0.1)
  local d=t.diagnostics()
  check(d.mode=="quiet" and not d.activeJob and d.quietJob,
    "steady FPP with completed REDscript cleanup did not park active lane")
  local before=t.calls()
  t.run(2,0.1)
  check(t.calls()-before<=1,"quiet FPP performed per-frame combat work")
  t.run(3,0.1)
  check(t.calls()>before,"quiet FPP dropped necessary REDscript reconciliation")
  local slowCount=t.diagnostics().quietCalls
  check(slowCount>0,"quiet maintenance never executed")
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
  check(t.diagnostics().mode=="quiet","event wake never settled into FPP")
  t.stop()
  check(not t.diagnostics().activeJob and not t.diagnostics().quietJob,
    "shutdown left scheduler jobs running")
  check(t.nativeCount()==0,"late CET registration occurred")
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

print("Dedra shared schedule runtime: FPP quiet 0.25s, full TPP wake, cleanup, REDscript audit, fallback and zero late native registrations PASS")
