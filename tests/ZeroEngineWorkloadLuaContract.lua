-- Standalone execution contract for additive 0-Engine Lua modules.
-- Uses a fake stock Engine; no Cyberpunk, CMB or real CET installation required.
local root = assert(arg[1], "module source directory required")
package.path = root .. "/../?.lua;" .. root .. "/?.lua;" .. package.path

local Work = require("GCETWorkQueue")
local Phase = require("GCETPhasePlanner")
local Frames = require("GCETFrameListeners")
local Signals = require("GCETStateSignals")
local nativeRegistrationCount, frame, fakeClock = 0, 0, 0
local originalClock = os.clock
os.clock = function() fakeClock = fakeClock + 0.00025; return fakeClock end
local playing = true
local eventMap, frameList, afterJobs, recurring, adopted = {}, {}, {}, {}, {}
local state = { frame = 0, inVehicle = false, inCombat = false, inMenu = false,
    derived = { district = "Watson" } }
local engine = {}
function engine.GetState() return state end
function engine.IsPlaying() return playing end
function engine.Subscribe(event, fn)
    eventMap[event] = eventMap[event] or {}
    local entry = { fn = fn, active = true }
    table.insert(eventMap[event], entry)
    return { unsubscribe = function() entry.active = false end }
end
function engine.OnFrame(interval, fn)
    local h = { active = true, fn = fn, interval = interval }
    frameList[#frameList + 1] = h
    return { unsubscribe = function() h.active = false end }
end
engine.Schedule = {}
function engine.Schedule.NextTick(_, fn)
    local job = { active = true, fn = fn }
    afterJobs[#afterJobs + 1] = job
    return { Cancel = function() job.active = false end }
end
function engine.Schedule.Every(_, opts, fn)
    local h = { active = opts.active ~= false, cancelled = false, fn = fn }
    recurring[#recurring + 1] = h
    return {
        Cancel = function() h.active = false; h.cancelled = true end,
        Pause = function() h.active = false end,
        Resume = function() if not h.cancelled then h.active = true end end
    }
end
function engine.MakeEventRegistrar(owner, fallback)
    return function(event, fn)
        if event ~= "onUpdate" and event ~= "onDraw" then
            return fallback and fallback(event, fn)
        end
        adopted[event] = adopted[event] or {}
        local ent = { fn = fn, active = true, owner = owner }
        adopted[event][#adopted[event] + 1] = ent
        return { unsubscribe = function() ent.active = false end }
    end
end
local function emit(event, ...)
    for _, h in ipairs(eventMap[event] or {}) do
        if h.active then h.fn(...) end
    end
end
local function advance()
    frame = frame + 1
    state.frame = frame
    -- Existing stock 0-Engine onFrame occurs before Scheduler.Update.
    local snapshot = {}
    for i = 1, #frameList do snapshot[i] = frameList[i] end
    for _, h in ipairs(snapshot) do
        if h.active and frame % h.interval == 0 then h.fn(frame) end
    end
    local jobs = afterJobs
    afterJobs = {}
    for _, h in ipairs(jobs) do if h.active then h.fn() end end
    for _, h in ipairs(adopted.onUpdate or {}) do
        if h.active then h.fn(1/60) end
    end
end
local function fireRecurring()
    for _, h in ipairs(recurring) do
        if h.active then h.fn({ frame = frame, dt = 1/60 }) end
    end
end

-- WorkQueue completes explicit small steps and detaches after the last job.
local work = Work.New(engine)
local steps, context = 0, { index = 0 }
local done = work.Submit({ owner = "test", maxStepsPerFrame = 2,
    context = context, generationScoped = true }, function(ctx)
    steps = steps + 1
    ctx.index = ctx.index + 1
    return steps == 7
end)
for _ = 1, 12 do advance() end
assert(steps == 7 and not done.IsActive(), "cooperative queue completion")
assert(work.GetInfo().completed == 1, "queue completion telemetry")

local never = work.Submit({ generationScoped = true }, function() error("should not run") end)
emit("PlayerInvalidated")
assert(not never.IsActive(), "queue must cancel scoped work on session invalidation")
advance()

-- Exact scheduler remains untouched: only PhasePlanner clients may defer.
local planner = Phase.New(engine)
planner.Configure({ budgetMs = 0.1 })
local ranA, ranB = 0, 0
planner.Every(1, { id = "A", estimatedMs = 0.4, maxDeferFrames = 2 }, function() ranA = ranA + 1 end)
planner.Every(1, { id = "B", estimatedMs = 0.4, maxDeferFrames = 2 }, function() ranB = ranB + 1 end)
advance()
fireRecurring()
assert(ranA == 1 and ranB == 0, "optional cost-aware deferral expected")
for _ = 1, 5 do advance() end
assert(ranB == 1, "deferred maintenance must eventually execute")
assert(planner.GetInfo().postponed >= 1, "phase planner must account for deferral")

-- Pause physically unsubscribes at a safe scheduling boundary, resume
-- reattaches and cancellation prevents later dispatch.
local frameApi = Frames.New(engine)
local calls = 0
local listener = frameApi.MakeRegistrar("test", nil)("onUpdate",
    function() calls = calls + 1 end)
advance()
local beforePause = calls
listener.Pause()
advance()
advance()
assert(calls == beforePause and not listener.IsActive(), "frame pause must stop callbacks")
listener.Resume()
advance()
assert(calls == beforePause + 1 and listener.IsActive(), "frame resume must restore callback")
listener.Cancel()
advance()
assert(calls == beforePause + 1 and frameApi.GetInfo().active == 0, "frame cancel")

-- Change-only signals reuse stock bus; duplicates do not retrigger.
local signals = Signals.New(engine)
local seen = {}
local watcher = signals.Watch("inVehicle", function(v, previous, version, gen)
    seen[#seen + 1] = { v, previous, version, gen }
end, { immediate = false })
emit("MountedToVehicleChanged", true)
emit("MountedToVehicleChanged", true)
emit("MountedToVehicleChanged", false)
assert(#seen == 2 and signals.Get("inVehicle") == false, "change deduplication")
emit("PlayerInvalidated")
assert(#seen == 3 and seen[3][3] >= 3, "state invalidation must signal epoch")
watcher.Cancel()
emit("MountedToVehicleChanged", true)
assert(#seen == 3, "cancelled watchers must stay dormant")
assert(nativeRegistrationCount == 0, "no native CET hooks should be registered")
os.clock = originalClock
print("0-Engine additive workload Lua runtime contract PASSED")
