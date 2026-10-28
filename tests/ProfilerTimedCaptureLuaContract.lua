-- COMBAT-POLICE-60 regression: reuse existing onUpdate, native capture clock,
-- one-key auto start/stop/export, original F11 and scenario-bind behavior.
local filename = assert(arg[1], "CETProfilerControls/init.lua required")
local events, inputs, marks = {}, {}, {}
local capturing, seconds = false, 0
local starts, pauses, dumps = 0, 0, 0
local inputCount = 0
local eventCount = 0

function registerForEvent(name, fn)
    assert(not events[name], "Duplicate profiler event " .. name)
    events[name] = fn
    eventCount = eventCount + 1
end
function registerInput(name, _, fn)
    assert(not inputs[name], "Duplicate input " .. name)
    inputs[name] = fn
    inputCount = inputCount + 1
end
function CETProfilerIsRunning() return capturing end
function CETProfilerStart()
    assert(not capturing, "Start over existing capture")
    capturing, seconds = true, 0
    starts = starts + 1
    return "START"
end
function CETProfilerPause()
    assert(capturing, "Pause without start")
    capturing = false
    pauses = pauses + 1
    return "PAUSE"
end
function CETProfilerDump()
    assert(not capturing, "Dump before pause")
    dumps = dumps + 1
    return "DUMP"
end
function CETProfilerStatus()
    return string.format("capture=active captured=%.6fs", seconds)
end
function CETProfilerMark(label)
    marks[#marks + 1] = label
    return "MARK"
end

assert(loadfile(filename))()
assert(inputCount == 5, "Expected manual, timed, and 3 scenario input bindings")
assert(eventCount == 3, "Timer must reuse existing onInit/onUpdate/onDraw")
assert(events.onUpdate and events.onDraw and events.onInit)
events.onInit()

-- Start 60s benchmark once; repeated start while capturing must do nothing.
inputs.CETProfiler_Police60(true)
assert(capturing and starts == 1 and pauses == 0 and dumps == 0)
assert(marks[1] == "SCENARIO_COMBAT_START", "Combat tag must start automatically")
inputs.CETProfiler_Police60(true)
assert(starts == 1 and capturing, "Repeated start must not restart the timer")
inputs.CETProfiler_WorldTag(true)
assert(marks[2] == nil, "Manual scenario switches must not contaminate timed capture")

-- Native captured seconds, not dt: duration is enforced despite dt jitter.
for i = 1, 60 do
    seconds = i * 0.9
    events.onUpdate(0.01)
end
assert(capturing and starts == 1 and pauses == 0, "Capture stopped too early")
seconds = 59.99
events.onUpdate(20.0)
assert(capturing, "Game dt must not terminate the capture early")
seconds = 60.02
events.onUpdate(0)
assert(not capturing and pauses == 1 and dumps == 1, "Native 60s stop/export failed")
assert(marks[#marks] == "SCENARIO_COMBAT_END", "Auto end marker missing")
events.onUpdate(5)
assert(dumps == 1 and pauses == 1, "Double dump after timer end")

-- Manual F11 behavior must remain unchanged. A timed key cannot take it over.
inputs.CETProfiler_Toggle(true)
assert(capturing and starts == 2)
inputs.CETProfiler_Police60(true)
assert(capturing and starts == 2, "Timed key replaced an existing manual capture")
seconds = 120
events.onUpdate(0.2)
assert(capturing, "Manual capture unexpectedly auto-stopped")
inputs.CETProfiler_Toggle(true)
assert(not capturing and dumps == 2 and pauses == 2, "Manual stop/export regression")

-- F11 may stop a timed capture early; it must cancel the pending 60s stop.
inputs.CETProfiler_Police60(true)
seconds = 12
events.onUpdate(0.03)
inputs.CETProfiler_Toggle(true)
assert(not capturing and starts == 3 and dumps == 3 and pauses == 3)
seconds = 70
events.onUpdate(0.5)
assert(dumps == 3, "Timed capture was left armed after manual stop")

print("COMBAT-POLICE-60 native-time stop/export, scenario markers, manual coexistence, no extra event PASS")
