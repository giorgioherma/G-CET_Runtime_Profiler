-- CET stores exactly one event callback per sandbox. Do not model this as a
-- multi-listener dispatcher, or the original adaptive replacement bug passes.
local path, scenario = arg[1], arg[2]
local callbacks, registrations, trace = {}, {}, {}
function registerForEvent(event, callback)
    registrations[event] = (registrations[event] or 0) + 1
    callbacks[event] = callback
end
function record(name, dt)
    trace[#trace + 1] = {name, dt}
end
package.preload["modules/CETProfilerScheduler"] = function()
    return {
        Init = function() end,
        Update = function(ctx)
            record("scheduler", ctx.dt)
            trace[#trace][3] = ctx.state and ctx.state.count
            if ctx.dt == 0.5 then error("scheduler failure") end
        end
    }
end
local engine = assert(loadfile(path))()
registerForEvent = nil -- CET removes the API after initialization
local function expect(...)
    local expected = {...}
    assert(#trace == #expected, "expected " .. #expected .. " actions, got " .. #trace)
    for i, name in ipairs(expected) do
        assert(trace[i][1] == name, "expected " .. name .. ", got " .. trace[i][1])
    end
end
if scenario == "stock" then
    assert(registrations.onUpdate == 1, "original registration must not be replaced")
    callbacks.onInit()
    callbacks.onUpdate(0.125)
    callbacks.onShutdown()
    expect("init", "cron", "state", "scheduler", "shutdown")
    assert(trace[2][2] == 0.125 and trace[3][2] == 0.125 and trace[4][2] == 0.125)
    assert(trace[4][3] == 1, "scheduler must see updated original state")
    trace = {}
    callbacks.onUpdate(0)
    expect("cron", "scheduler")
    assert(trace[2][3] == 1, "early return must retain state and advance scheduler")
    trace = {}
    local ok, message = pcall(callbacks.onUpdate, -1)
    assert(not ok and message:find("host failure", 1, true), "original error must reach CET")
    expect("cron")
    trace = {}
    assert(pcall(callbacks.onUpdate, 0.5), "profiler failure must not replace original error semantics")
    expect("cron", "state", "scheduler")
    assert(trace[3][3] == 2)
elseif scenario == "last" or scenario == "named" then
    assert(registrations.onUpdate == (scenario == "last" and 2 or 1))
    callbacks.onUpdate(0.125)
    expect("last", "scheduler")
elseif scenario == "empty" then
    assert(registrations.onUpdate == 1)
    callbacks.onUpdate(0.125)
    expect("scheduler")
else
    error("unknown test scenario")
end
print("generated adaptive update PASS: " .. scenario)
