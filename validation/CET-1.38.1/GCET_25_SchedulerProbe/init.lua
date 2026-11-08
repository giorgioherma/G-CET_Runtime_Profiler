-- CET 1.38.1 / Cyberpunk 2.5 temporary 0-Engine Scheduler profiler probe.
-- Standalone CET mod: no game hooks, no gameplay writes, no 0-Engine edits.
-- Install only for validation and remove after collecting the capture.

local OWNER = "GCET25SchedulerProbe"
local counters = { frame10 = 0, timed1 = 0, once3 = 0, cancel5 = 0, paused4 = 0 }
local activeHandles = {}
local schedule = nil
local initialized = false
local wasCapturing = false
local captureElapsed = 0
local lastCheckpoint = 0
local checkpointCount = 0
local errors = 0
local cancelHandle = nil
local pausedHandle = nil
local pauseTarget = nil
local oneshotHandle = nil
local MAX_CHECKPOINTS = 60

local function fail(reason)
    errors = errors + 1
    print("[" .. OWNER .. "] " .. tostring(reason))
end

local function remember(handle, label)
    if type(handle) ~= "table" or type(handle.Cancel) ~= "function" then
        fail("Invalid Scheduler handle for " .. label)
        return nil
    end
    activeHandles[#activeHandles + 1] = handle
    return handle
end

local function submit(method, label, ...)
    if type(schedule) ~= "table" or type(schedule[method]) ~= "function" then
        fail("Missing Scheduler API: " .. method)
        return nil
    end
    local ok, result = pcall(schedule[method], ...)
    if not ok then
        fail("Scheduler " .. label .. " registration failed: " .. tostring(result))
        return nil
    end
    return remember(result, label)
end

local function checkpoint()
    if checkpointCount >= MAX_CHECKPOINTS or
        type(CETProfilerMark) ~= "function" then return end
    local label = string.format(
        "GCET25_SCHED_V1_F10_%d_T1_%d_O3_%d_C5_%d_P4_%d_E_%d",
        counters.frame10, counters.timed1, counters.once3,
        counters.cancel5, counters.paused4, errors)
    local ok, result = pcall(CETProfilerMark, label)
    if ok and not (type(result) == "string" and
        result:find("buffer full", 1, true)) then
        checkpointCount = checkpointCount + 1
    end
end

local function cleanCaptureJobs()
    if cancelHandle then cancelHandle.Cancel(); cancelHandle = nil end
    if oneshotHandle then oneshotHandle.Cancel(); oneshotHandle = nil end
    if pausedHandle then pausedHandle.Pause() end
    pauseTarget = nil
end

registerForEvent("onInit", function()
    if initialized then return end
    initialized = true

    if type(GetMod) ~= "function" then
        fail("CET GetMod API unavailable")
        return
    end
    local ok, engine = pcall(GetMod, "0-Engine")
    if not ok or type(engine) ~= "table" then
        fail("0-Engine not loaded; install its supported CET source first")
        return
    end

    -- A 0-Engine scoped handle supplies native job ownership. The profiler's
    -- temporary adaptive bridge adds Mod.Schedule only when the host lacks it.
    if type(engine.Register) == "function" then
        local regOK, mod = pcall(engine.Register, OWNER)
        if regOK and type(mod) == "table" and type(mod.Schedule) == "table" then
            schedule = mod.Schedule
        end
    end
    if not schedule and type(engine.Schedule) == "table" then
        schedule = engine.Schedule
    end
    if not schedule then
        fail("0-Engine Scheduler not integrated; reinstall profiler with 0-Engine enabled")
        return
    end

    submit("EveryFrames", "frame10", 10,
        { owner = OWNER, id = "frame10", offset = 0, spread = false },
        function() counters.frame10 = counters.frame10 + 1 end)

    submit("Every", "timed1", 1.0,
        { owner = OWNER, id = "timed1", spread = false },
        function() counters.timed1 = counters.timed1 + 1 end)

    -- Registered once but deliberately paused until each F11 capture starts.
    pausedHandle = submit("EveryFrames", "paused4", 4,
        { owner = OWNER, id = "paused4", offset = 0, spread = false, active = false },
        function()
            counters.paused4 = counters.paused4 + 1
            if pauseTarget and counters.paused4 >= pauseTarget then
                pausedHandle.Pause()
                pauseTarget = nil
            end
        end)
    print("[" .. OWNER .. "] Scheduler probe registered (frame10/timed1/paused4)")
end)

registerForEvent("onUpdate", function(dt)
    local capturing = false
    if type(CETProfilerIsRunning) == "function" then
        local ok, active = pcall(CETProfilerIsRunning)
        capturing = ok and active == true
    end

    if capturing and not wasCapturing then
        -- One-shot and self-cancelling jobs are created inside the measured
        -- interval, so they appear even if the user spent time in menus.
        cleanCaptureJobs()
        checkpointCount = 0
        captureElapsed = 0
        lastCheckpoint = 0
        checkpoint()
        if schedule then
            oneshotHandle = submit("After", "once3", 3.0,
                { owner = OWNER, id = "once3" },
                function() counters.once3 = counters.once3 + 1 end)

            cancelHandle = submit("EveryFrames", "cancel5", 5,
                { owner = OWNER, id = "cancel5", offset = 0, spread = false },
                function()
                    counters.cancel5 = counters.cancel5 + 1
                    if cancelHandle and counters.cancel5 % 8 == 0 then
                        cancelHandle.Cancel()
                        cancelHandle = nil
                    end
                end)

            if pausedHandle then
                pauseTarget = counters.paused4 + 6
                pausedHandle.Resume()
            end
        end
    elseif not capturing and wasCapturing then
        cleanCaptureJobs()
    end
    wasCapturing = capturing

    if capturing then
        captureElapsed = captureElapsed + (tonumber(dt) or 0)
        if captureElapsed - lastCheckpoint >= 5 then
            lastCheckpoint = captureElapsed
            checkpoint()
        end
    end
end)

registerForEvent("onShutdown", function()
    cleanCaptureJobs()
    for _, handle in ipairs(activeHandles) do
        if type(handle.Cancel) == "function" then handle.Cancel() end
    end
    activeHandles = {}
    print("[" .. OWNER .. "] all Scheduler handles cancelled")
end)
