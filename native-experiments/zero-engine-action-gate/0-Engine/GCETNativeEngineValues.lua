-- Experimental on-demand 0-Engine service values; NEVER called each frame.
-- Information-only. No new native hooks, timers, subscriptions or GC policy.
local M = {}
local function info(target)
    if type(target) ~= 'table' or type(target.GetInfo) ~= 'function' then return nil end
    local ok, value = pcall(target.GetInfo)
    if not ok or type(value) ~= 'table' then return nil end
    return value
end

function M.Read(engine, nativeActionValues)
    local result = { schemaVersion = 1, evidenceOnly = true, preDispatchHook = false }
    if type(engine) ~= 'table' then return result end
    result.actionRouter = info(engine.ActionRouter)
    result.frameListeners = info(engine.FrameListeners)
    result.workQueue = info(engine.WorkQueue)
    result.phasePlanner = info(engine.PhasePlanner)
    result.stateSignals = info(engine.StateSignals)
    if nativeActionValues and type(nativeActionValues.GetValues) == 'function' then
        local ok, value = pcall(nativeActionValues.GetValues)
        if ok then result.nativeActionInterest = value end
    end
    -- None of these fields authorizes suppressing an onUpdate, OnAction or
    -- game-native event. GetInfo can allocate and must not run on the hot path.
    return result
end
return M
