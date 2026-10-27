-- Run against the worker extracted from the authoritative C# producer.
-- Simulates FPP startup, keybind wake, TPP combat/ADS cleanup, missing
-- capability, rejected API listeners, missing base and 0-Engine fallback.
local file = assert(arg[1], "generated worker path required")
local worker = assert(loadfile(file))()
if arg[2] then
  package.loaded["modules/GCETWorkloadProbe"] = {
    Begin = function() return nil end, End = function() end
  }
end
local frameLibrary = arg[2] and assert(loadfile(arg[2]))() or nil
local function check(ok, why) assert(ok, why) end
local function make(opts)
  opts = opts or {}
  local requested = opts.requested == true
  local pending = false
  local eventFns, calls, frame, fallbackCalls = {}, 0, nil, 0
  local counters = {paused = 0, resumed = 0, registered = 0, cancelled = 0}
  local active = true
  local base = {
    events = {
      on = function(kind, owner, fn)
        eventFns[kind] = fn
        return opts.rejectListener and -3 or 2
      end,
      off = function(kind, owner) eventFns[kind] = nil end
    },
    perspective = {
      get = function()
        return {
          userWantsThirdPerson = requested,
          mode = requested and "third" or "first",
          transitioning = false
        }
      end
    }
  }
  if opts.capability then
    base.gcetDedraWakeReady = function() return true end
  end
  local engine = {
    FrameListeners = {
      MakeRegistrar = function(owner, fallback)
        return function(event, callback)
          check(event == "onUpdate", "wrong worker event")
          check(not frame, "duplicate frame subscription")
          counters.registered = counters.registered + 1
          frame = callback
          return {
            Pause = function()
              if active then active = false; counters.paused = counters.paused + 1 end
            end,
            Resume = function()
              if not active then active = true; counters.resumed = counters.resumed + 1 end
            end,
            Cancel = function() counters.cancelled = counters.cancelled + 1 end
          }
        end
      end
    }
  }
  -- Unlike the lightweight frame mock, this is the actual authoritative
  -- 0-Engine FrameListeners implementation, including physical unsubscribe.
  local pendingTasks = {}
  if opts.realFrame then
    check(frameLibrary and type(frameLibrary.New) == "function", "real module not supplied")
    package.loaded["modules/GCETWorkloadProbe"] = {
      Begin = function() return nil end, End = function() end
    }
    engine.FrameListeners = nil
    engine.MakeEventRegistrar = function(owner, fallback)
      return function(event, callback)
        check(event == "onUpdate", "incorrect adopted event")
        check(not frame, "duplicate active 0-Engine subscribers")
        counters.registered = counters.registered + 1
        frame = callback
        return {unsubscribe = function()
          check(frame == callback, "wrong callback unsubscribed")
          frame = nil
          counters.cancelled = counters.cancelled + 1
        end}
      end
    end
    engine.Schedule = {NextTick = function(_, callback)
      local task = {cancelled = false}
      function task.Cancel() task.cancelled = true end
      pendingTasks[#pendingTasks + 1] = {run = callback, task = task}
      return task
    end}
    engine.FrameListeners = frameLibrary.New(engine)
  end
  if opts.noEngine then engine = nil end
  GetMod = function(name)
    if name == "0-Engine" then return engine end
    if name == "immersive_third_person" then return opts.noBase and nil or base end
    return nil
  end
  local addon = {takeover = false}
  local control = worker.Install(addon, function(dt) calls = calls + 1 end,
    function(event, callback) fallbackCalls = fallbackCalls + 1; frame = callback end)
  control.Register()
  control.Start()
  return {
    state = addon, control = control, base = base, counters = counters,
    isActive = function() return active end,
    setRequested = function(v) requested = v end,
    emit = function(kind) if eventFns[kind] then eventFns[kind]() end end,
    tick = function()
      if (not opts.realFrame or active) and frame then frame(1 / 60) end
    end,
    flush = function()
      local tasks = pendingTasks
      pendingTasks = {}
      for _, entry in ipairs(tasks) do
        if not entry.task.cancelled then entry.run() end
      end
    end,
    hasFrame = function() return frame ~= nil end,
    diagnostics = function() return control.Diagnostics() end,
    calls = function() return calls end,
    fallbacks = function() return fallbackCalls end
  }
end

do
  local t = make({capability = true})
  check(not t.isActive() and t.counters.paused == 1,
    "supported FPP startup must pause without waiting for first toggle")
  local d = t.diagnostics()
  check(d.pausable == true and d.linked == true and d.bridge == true and
        d.requested == false and d.listenerActive == false,
    "read-only diagnostics do not explain valid FPP dormancy")
  t.tick()
  check(t.calls() == 0, "FPP dormancy executed author combat step")
  t.setRequested(true)
  t.control.Signal("__gcetItpToggleWakeV2")
  check(t.isActive() and t.counters.resumed == 1, "CET toggle did not synchronously wake")
  t.tick()
  check(t.calls() == 1, "TPP did not run full author update")
  t.state.takeover = true
  t.setRequested(false)
  t.control.Signal("__gcetItpToggleWakeV2")
  check(t.isActive(), "combat cleanup was prematurely paused")
  t.tick()
  t.state.takeover = false
  t.emit("perspectiveChanged")
  check(not t.isActive(), "completed FPP cleanup did not go dormant")
  t.control.Stop()
  check(t.counters.cancelled == 1 and t.counters.registered == 1,
    "worker has duplicate ownership or missing shutdown cancellation")
end

do
  local t = make({capability = true, requested = true})
  check(t.isActive(), "saved TPP request was wrongly paused on startup")
  t.setRequested(false)
  t.emit("perspectiveChanged")
  check(not t.isActive(), "base perspective OFF event did not pause")
end

do
  local t = make({capability = false})
  check(t.isActive() and t.counters.paused == 0,
    "unverified original/partial ITP was unsafely detached")
end

do
  local t = make({capability = true, rejectListener = true})
  check(t.isActive() and t.counters.paused == 0,
    "rejected event wake subscription allowed unsafe pause")
end

do
  local t = make({capability = false, noBase = true})
  check(t.isActive(), "missing ITP stranded standalone addon")
end

do
  local t = make({capability = true, noEngine = true})
  check(t.fallbacks() == 1, "missing 0-Engine dropped original update fallback")
end

if frameLibrary then
  do
    local t = make({capability = true, realFrame = true})
    check(t.diagnostics().pausable == true and t.fallbacks() == 0,
      "actual 0-Engine frame implementation was not adopted")
    t.flush() -- Pause physically unsubscribes, not merely skips callback work.
    check(not t.hasFrame(), "FPP frame listener remained subscribed after pause")
    t.setRequested(true)
    t.control.Signal("__gcetItpToggleWakeV2")
    check(t.hasFrame() and t.diagnostics().listenerActive == true,
      "original CET toggle did not reattach the real listener")
    t.tick()
    check(t.calls() == 1, "real listener failed to run TPP author step")
    t.setRequested(false)
    t.control.Signal("__gcetItpToggleWakeV2")
    t.flush()
    check(not t.hasFrame(), "TPP OFF did not physically detach the real listener")
    t.control.Stop()
    t.flush()
  end
end

print("Dedra worker runtime: FPP startup pause, CET wake, cleanup, API fail-open, real listener and single ownership PASS")
