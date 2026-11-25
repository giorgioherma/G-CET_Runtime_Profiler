-- Model CET's actual one-slot registration; a second registration overwrites
-- the earlier callback, not a multi-listener event dispatcher.
local path, scenario, expected = arg[1], arg[2], tonumber(arg[3])
local callbacks, count, trace = {}, 0, {}
local cleanupError = false
function record(value) trace[#trace+1] = value end
function registerForEvent(event, callback)
    if event == "onShutdown" then count = count+1 end
    callbacks[event] = callback
end
package.preload["modules/GCETNativeLocationHost"] = function()
    return {new=function()
        return {shutdown=function()
            record("cleanup")
            if cleanupError then error("cleanup failure", 0) end
        end}
    end}
end
local engine = assert(loadfile(path))()
registerForEvent = nil
assert(count == expected, "unexpected registration count: " .. count)
assert(engine.GCET and engine.GCET.NativeLocationHost, "native host not initialized")
local function run(mode, expectedTrace, expectedError, failCleanup)
    trace = {}
    cleanupError = failCleanup == true
    local ok, err = pcall(callbacks.onShutdown, mode)
    assert(table.concat(trace, ",") == expectedTrace, "incorrect trace: " .. table.concat(trace, ","))
    if expectedError then
        assert(not ok and err == expectedError, "incorrect error: " .. tostring(err))
    else
        assert(ok, tostring(err))
    end
end
if scenario == "stock" then
    run("normal", "save,after-save,cleanup")
    run("early", "save,cleanup")
    run("author-error", "save,cleanup", "author failure")
    run("normal", "save,after-save,cleanup", "cleanup failure", true)
    run("both-error", "save,cleanup", "author failure", true)
elseif scenario == "last" then
    run("normal", "last,cleanup")
else
    run("normal", "cleanup")
end
print("Native single-slot shutdown PASS: " .. scenario)
