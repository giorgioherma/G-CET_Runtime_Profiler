-- GCETWorkloadProbe.lua - capture-gated native Scheduler telemetry bridge.
-- No new native hooks, timers or collection work. Jobs are nested inside
-- normal 0-Engine callback measurements: never add their totals to the parent.
local M = {}
local handles = {}
local disabled = false

function M.Begin(owner, kind, label, frame)
    if disabled or type(CETProfilerIsRunning) ~= "function" then return nil end
    local ok, capturing = pcall(CETProfilerIsRunning)
    if not ok or capturing ~= true then return nil end
    if type(CETProfilerSchedulerRegisterJob) ~= "function" or
       type(CETProfilerSchedulerJobBegin) ~= "function" or
       type(CETProfilerSchedulerJobEnd) ~= "function" then return nil end

    local key = tostring(owner or "unscoped") .. "\31" .. kind .. "\31" .. label
    local handle = handles[key]
    if not handle then
        ok, handle = pcall(CETProfilerSchedulerRegisterJob,
            tostring(owner or "unscoped"), kind, label, 0, "cooperative")
        if not ok or handle == nil or handle == 0 then
            disabled = true
            return nil
        end
        handles[key] = handle
    end
    local success, token = pcall(CETProfilerSchedulerJobBegin, handle)
    if not success or not token or token == 0 then return nil end
    return { handle, token, frame or 0 }
end

function M.End(token)
    if not token then return end
    -- A broken/missing profiler must not interrupt the author's callback.
    pcall(CETProfilerSchedulerJobEnd, token[1], token[2], token[3])
end

return M
