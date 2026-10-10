-- GCETWorkQueue.lua: explicit cooperative per-item work, never preempts Lua.
-- Installed as an additive 0-Engine module; no per-frame hook until work exists.
local M = { version = "1.0.0" }

function M.New(engine)
    local api, jobs, pulse = {}, {}, nil
    local cursor, sequence = 1, 0
    local maxOperations, budgetMs = 48, 1.50
    local invoking, generation = false, 0
    local completed, cancelled, errors, deferred = 0, 0, 0, 0
    local ensurePulse, stopPulse
    local pulseStopQueued = false

    local function alive(job)
        return not job.cancelled and job.active
    end

    local function compact()
        local w = 0
        for i = 1, #jobs do
            local job = jobs[i]
            if not job.cancelled then
                w = w + 1
                jobs[w] = job
            end
        end
        for i = w + 1, #jobs do jobs[i] = nil end
        if cursor > #jobs then cursor = 1 end
    end

    local function anyRunnable()
        for i = 1, #jobs do
            if alive(jobs[i]) then return true end
        end
        return false
    end

    stopPulse = function()
        if not pulse or pulseStopQueued then return end
        -- Never unsubscribe from stock EventEmitter during dispatch.
        if engine.Schedule and type(engine.Schedule.NextTick) == "function" then
            pulseStopQueued = true
            engine.Schedule.NextTick({ pause = "never", id = "gcet_work_detach" }, function()
                pulseStopQueued = false
                if not anyRunnable() and pulse then pulse.unsubscribe(); pulse = nil end
            end)
        end
    end

    local function tick(frame)
        if not engine.IsPlaying() then return end
        if invoking then return end
        invoking = true
        local started = os.clock() * 1000
        local operations, visits = 0, 0
        local visitLimit = math.max(1, #jobs * (maxOperations + 1))
        while #jobs > 0 and operations < maxOperations and visits < visitLimit do
            if (os.clock() * 1000 - started) >= budgetMs then
                deferred = deferred + 1
                break
            end
            if cursor > #jobs then cursor = 1 end
            local job = jobs[cursor]
            cursor = cursor + 1
            visits = visits + 1
            if job and alive(job) and
                (not job.generationScoped or job.generation == generation) then
                if job.lastFrame ~= frame then
                    job.lastFrame, job.stepsThisFrame = frame, 0
                end
                if job.stepsThisFrame < job.maxStepsPerFrame then
                    job.stepsThisFrame = job.stepsThisFrame + 1
                    operations = operations + 1
                    -- Each invocation is ONE author-defined safe incremental unit.
                    -- A long individual invocation cannot be interrupted.
                    local ok, finished = pcall(job.step, job.context)
                    job.steps = job.steps + 1
                    if not ok then
                        job.lastError = tostring(finished)
                        job.cancelled = true
                        errors = errors + 1
                    elseif finished == true then
                        job.cancelled = true
                        completed = completed + 1
                    end
                end
            elseif job and job.generationScoped and job.generation ~= generation then
                job.cancelled = true
                cancelled = cancelled + 1
            end
        end
        invoking = false
        compact()
        if not anyRunnable() then stopPulse() end
    end

    ensurePulse = function()
        if pulse or not anyRunnable() then return end
        if type(engine.OnFrame) ~= "function" then
            error("0-Engine OnFrame is required for GCETWorkQueue")
        end
        pulse = engine.OnFrame(1, tick, "GCETWorkQueue")
    end

    function api.Configure(options)
        options = options or {}
        if type(options.maxOperationsPerFrame) == "number" then
            maxOperations = math.max(1, math.min(256, math.floor(options.maxOperationsPerFrame)))
        end
        if type(options.budgetMs) == "number" then
            budgetMs = math.max(0.1, math.min(8, options.budgetMs))
        end
    end

    function api.Submit(options, step)
        options = options or {}
        if type(step) ~= "function" then error("WorkQueue.Submit requires a step function") end
        sequence = sequence + 1
        local job = {
            id = sequence, owner = options.owner or "unscoped",
            label = options.id or ("work_" .. sequence),
            step = step, context = options.context or {},
            maxStepsPerFrame = math.max(1, math.min(128,
                math.floor(tonumber(options.maxStepsPerFrame) or 4))),
            active = options.active ~= false, cancelled = false,
            generation = generation, generationScoped = options.generationScoped == true,
            lastFrame = -1, stepsThisFrame = 0, steps = 0
        }
        jobs[#jobs + 1] = job
        if not invoking then ensurePulse() end
        local handle = {}
        function handle.Cancel()
            if not job.cancelled then
                job.cancelled = true
                cancelled = cancelled + 1
            end
            if not invoking then compact(); if not anyRunnable() then stopPulse() end end
        end
        handle.unsubscribe = handle.Cancel
        function handle.Pause()
            job.active = false
            if not invoking and not anyRunnable() then stopPulse() end
        end
        function handle.Resume()
            if job.cancelled then return end
            job.active = true
            ensurePulse()
        end
        function handle.SetActive(value)
            if value then handle.Resume() else handle.Pause() end
        end
        function handle.IsActive() return alive(job) end
        function handle.GetInfo()
            return { owner = job.owner, label = job.label, steps = job.steps,
                active = alive(job), lastError = job.lastError }
        end
        return handle
    end

    function api.InvalidateGeneration()
        generation = generation + 1
        for i = 1, #jobs do
            local j = jobs[i]
            if j.generationScoped and not j.cancelled then
                j.cancelled = true
                cancelled = cancelled + 1
            end
        end
        if not invoking then compact(); if not anyRunnable() then stopPulse() end end
    end

    function api.GetInfo()
        local pending, active = 0, 0
        for i = 1, #jobs do
            if not jobs[i].cancelled then
                pending = pending + 1
                if jobs[i].active then active = active + 1 end
            end
        end
        return { queued = pending, active = active, completed = completed,
            cancelled = cancelled, errors = errors, deferredFrames = deferred,
            budgetMs = budgetMs, maxOperationsPerFrame = maxOperations,
            generation = generation }
    end

    if type(engine.Subscribe) == "function" then
        -- Existing 0-Engine lifecycle event, not a new native hook.
        engine.Subscribe("PlayerInvalidated", api.InvalidateGeneration, "GCETWorkQueue")
    end

    return api
end

return M
