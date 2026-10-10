-- GCETPhasePlanner.lua: opt-in best-effort maintenance deadline/slack.
-- Delegates cadence to stock 0-Engine Scheduler; never changes existing jobs.
local M = { version = "1.0.0" }

function M.New(engine)
    local api, waiting, wake = {}, {}, nil
    local budgetMs, generation, usedMs, lastFrame = 1.50, 0, 0, -1
    local completed, postponed, coalesced, failures = 0, 0, 0, 0
    local serial = 0
    local function frameNow()
        if type(engine.GetState) ~= "function" then return 0 end
        local ok, state = pcall(engine.GetState)
        return ok and state and tonumber(state.frame) or 0
    end
    local function resetFrame()
        local frame = frameNow()
        if frame ~= lastFrame then
            lastFrame, usedMs = frame, 0
        end
        return frame
    end
    local function compact()
        local write = 0
        for i = 1, #waiting do
            local task = waiting[i]
            if task.pending and not task.cancelled then
                write = write + 1
                waiting[write] = task
            end
        end
        for i = write + 1, #waiting do waiting[i] = nil end
    end
    local function invoke(task, ctx)
        task.pending = false
        local start = os.clock() * 1000
        local ok, err = pcall(task.fn, ctx)
        local duration = math.max(0, os.clock() * 1000 - start)
        usedMs = usedMs + duration
        task.estimateMs = 0.7 * task.estimateMs + 0.3 * duration
        if not ok then
            failures = failures + 1
            task.lastError = tostring(err)
            -- Do not alter Scheduler's existing containment / other jobs.
        else
            completed = completed + 1
        end
    end
    local ensureWake
    local function flush()
        if type(engine.IsPlaying) == "function" and not engine.IsPlaying() then return end
        local frame = resetFrame()
        local now = os.clock()
        for i = 1, #waiting do
            local task = waiting[i]
            if task and task.pending and not task.cancelled then
                local deadline = frame >= task.dueFrame + task.maxDeferFrames or
                    now >= task.dueClock + task.slackSeconds
                if deadline or usedMs == 0 or usedMs + task.estimateMs <= budgetMs then
                    invoke(task, task.ctx)
                else
                    postponed = postponed + 1
                end
            end
        end
        compact()
        if #waiting == 0 and wake then wake.unsubscribe(); wake = nil end
    end
    ensureWake = function()
        if wake then return end
        if type(engine.OnFrame) ~= "function" then
            error("0-Engine OnFrame required for deferred phase work")
        end
        wake = engine.OnFrame(1, flush, "GCETPhasePlanner")
    end

    function api.Configure(options)
        if type(options) == "table" and type(options.budgetMs) == "number" then
            budgetMs = math.max(0.1, math.min(8, options.budgetMs))
        end
    end
    function api.Every(seconds, options, fn)
        if type(options) == "function" then fn, options = options, {} end
        options = options or {}
        if not engine.Schedule or type(engine.Schedule.Every) ~= "function" then
            error("0-Engine stock Schedule.Every is required for phase planning")
        end
        if type(fn) ~= "function" or type(seconds) ~= "number" or seconds <= 0 then
            error("PhasePlanner.Every expects positive interval and callback")
        end
        serial = serial + 1
        local task = {
            label = options.id or ("maintenance_" .. serial),
            fn = fn, cancelled = false, active = options.active ~= false,
            pending = false, generationScoped = options.generationScoped == true,
            generation = generation,
            maxDeferFrames = math.max(1, math.min(60,
                math.floor(tonumber(options.maxDeferFrames) or 6))),
            slackSeconds = math.max(0, math.min(seconds,
                tonumber(options.slackSeconds) or 0.12)),
            estimateMs = math.max(0.01, tonumber(options.estimatedMs) or 0.25)
        }
        local stockOptions = {}
        for k, v in pairs(options) do stockOptions[k] = v end
        stockOptions.active = task.active
        stockOptions.catchUp = false
        local function onDue(ctx)
            if task.cancelled or not task.active then return end
            if task.generationScoped and task.generation ~= generation then return end
            local frame = resetFrame()
            if task.pending then
                coalesced = coalesced + 1
                task.ctx = ctx
                return
            end
            if usedMs == 0 or usedMs + task.estimateMs <= budgetMs then
                invoke(task, ctx)
                return
            end
            task.ctx = ctx
            task.pending = true
            task.dueFrame = frame
            task.dueClock = os.clock()
            waiting[#waiting + 1] = task
            postponed = postponed + 1
            ensureWake()
        end
        local handle = engine.Schedule.Every(seconds, stockOptions, onDue)
        local out = {}
        function out.Cancel()
            task.cancelled, task.pending = true, false
            if handle and handle.Cancel then handle.Cancel() end
        end
        out.unsubscribe = out.Cancel
        function out.Pause()
            task.active, task.pending = false, false
            if handle and handle.Pause then handle.Pause() end
        end
        function out.Resume()
            if task.cancelled then return end
            task.active = true
            if handle and handle.Resume then handle.Resume() end
        end
        function out.SetActive(v) if v then out.Resume() else out.Pause() end end
        function out.IsActive() return task.active and not task.cancelled end
        function out.GetInfo() return {
            label = task.label, estimatedMs = task.estimateMs,
            pending = task.pending, lastError = task.lastError
        } end
        return out
    end
    function api.InvalidateGeneration()
        generation = generation + 1
        for i = 1, #waiting do
            local task = waiting[i]
            if task.generationScoped then task.pending = false end
        end
        compact()
        if #waiting == 0 and wake then wake.unsubscribe(); wake = nil end
    end
    function api.GetInfo()
        return { pending = #waiting, completed = completed, postponed = postponed,
            coalesced = coalesced, errors = failures, budgetMs = budgetMs }
    end
    if type(engine.Subscribe) == "function" then
        engine.Subscribe("PlayerInvalidated", api.InvalidateGeneration, "GCETPhasePlanner")
    end
    return api
end

return M
