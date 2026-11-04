-- Execute actual Resolver G-GC generated source inside mocked CET.
-- Regression: adaptive three-state workload gate, fixed 50KiB/500ms and developer A/B.
local runtime = assert(arg[1], "generated G-GC Lua runtime required")
local hooks, rendered, marks, logged, steps = {}, {}, {}, {}, {}
local heapKiB, active = 583 * 1024, true
local simulatedCpu, fullCollections, hudNotifications = 0, 0, 0
local selectedSensitivity, selectedEnabled, clicked = nil, nil, nil
local sliders, savedSettings = {}, nil
local prevPrint, oldIO = print, io
io = { open = function(path, mode)
  assert(path == "user_settings.txt", "unexpected preference path")
  if mode == "r" then
    if savedSettings == nil then return nil end
    return { read = function() return savedSettings end, close = function() end }
  end
  assert(mode == "w")
  return { write = function(_, value) savedSettings = value end,
           close = function() end }
end }

package.preload["threshold"] = function() return {
  schema = 3, capture = "UNTAMPERED_STOCK_STACK",
  baselineFrameMs = 22.4354, hitchToleranceMs = 33.6531,
  defaultIntensity = 37, defaultFrequencyMs = 1250,
  pressureReferenceMiB = 863, pressureStartMiB = 800,
  pressureHighMiB = 935
} end
registerForEvent = function(name, fn)
  assert(hooks[name] == nil, "duplicate G-GC callback: " .. name)
  hooks[name] = fn
end
Game = {
  GetPlayer = function() return active and {} or nil end,
  GetAllBlackboardDefs = function()
    return { UI_Notifications = {WarningMessage = 77} }
  end,
  GetBlackboardSystem = function()
    return { Get = function()
      return {SetVariant = function(_, key, value, notify)
        assert(key == 77 and notify and value.message:find("DONE"),
          "invalid completion HUD notification")
        hudNotifications = hudNotifications + 1
      end}
    end}
  end
}
SimpleScreenMessage = { new = function() return {} end }
ToVariant = function(x) return x end
CETProfilerMark = function(label) marks[#marks + 1] = label end
collectgarbage = function(command, amount)
  if command == "count" then return heapKiB end
  if command == "step" then
    assert(type(amount) == "number" and amount > 0 and amount <= 256,
      "out-of-range / zero / full collector step requested")
    steps[#steps + 1] = amount
    -- Deterministic mock step CPU and net heap reduction; requested GC
    -- work is not assumed equal to memory reclaimed.
    simulatedCpu = simulatedCpu + amount * 0.0000005
    heapKiB = math.max(64, heapKiB - math.min(32, amount / 4))
    return false
  end
  if command == "collect" then fullCollections = fullCollections + 1 end
  error("unexpected GC command " .. tostring(command))
end
local actualOs, oldPrint = os, print
os = { clock = function() return simulatedCpu end }
print = function(message) logged[#logged + 1] = message end
ImGui = {
  Begin = function() return true end, End = function() end,
  Text = function(x) rendered[#rendered + 1] = x end,
  SliderInt = function(label, value, minimum, maximum)
    sliders[label] = {min=minimum, max=maximum}
    assert(label == "Workload sensitivity")
    return selectedSensitivity or value, selectedSensitivity ~= nil
  end,
  Checkbox = function(label, value)
    assert(label == "Automatic assistance")
    if selectedEnabled == nil then return value, false end
    local chosen = selectedEnabled
    selectedEnabled = nil
    return chosen, chosen ~= value
  end,
  Button = function(label)
    if label == clicked then clicked = nil; return true end
    return false
  end
}
assert(loadfile(runtime))()
assert(type(hooks.onUpdate) == "function" and
  type(hooks.onDraw) == "function" and
  type(hooks.onOverlayOpen) == "function" and
  type(hooks.onOverlayClose) == "function" and
  type(hooks.onShutdown) == "function",
  "missing unique G-GC lifecycle hooks")

local function advance(seconds, dt)
  dt = dt or 0.02
  local n = math.floor(seconds / dt + 0.5)
  for _ = 1, n do hooks.onUpdate(dt) end
end
local function draw()
  rendered = {}
  hooks.onOverlayOpen()
  hooks.onDraw()
  return table.concat(rendered, "\n")
end
local function press(label)
  clicked = label
  local shown = draw()
  assert(clicked == nil, "CET action did not execute: " .. label)
  return shown
end
local function stepCount()
  return #steps
end
local function hasMark(label)
  for _, x in ipairs(marks) do
    if x == label then return true end
  end
  return false
end

-- The controller must function without Profiler hooks.
advance(1)
local shown = draw()
assert(sliders["Workload sensitivity"].min == 0 and
       sliders["Workload sensitivity"].max == 100)
assert(not sliders["Step size (KiB)"] and not sliders["Interval (ms)"])
assert(shown:find("Workload: HEAVY", 1, true))
assert(shown:find("Maintenance 50 KiB / 500 ms", 1, true))
hooks.onOverlayClose()
local initial = stepCount()
advance(12)
shown = draw()
assert(shown:find("Workload: RELAXED", 1, true),
  "calm gameplay did not clear five-second recovery")
assert(stepCount() > initial and steps[#steps] == 50,
  "relaxed state did not run fixed 50KiB maintenance")
hooks.onOverlayClose()

-- Ordinary relaxed gameplay has variable frame pacing, not identical
-- 20ms frames. A real garage replay exposed the previous strict recovery
-- threshold as too sensitive to ordinary 20-35ms jitter.
local quietDeltas = {0.020, 0.024, 0.027, 0.030, 0.021, 0.025, 0.023}
for i = 1, 950 do
  hooks.onUpdate(quietDeltas[(i - 1) % #quietDeltas + 1])
end
shown = draw()
assert(shown:find("Workload: RELAXED", 1, true),
  "realistic frame jitter prevented relaxed gameplay recovery")
hooks.onOverlayClose()

-- Sustained heavy load should prohibit helper work.
advance(3, 0.045)
shown = draw()
assert(shown:find("Workload: HEAVY", 1, true))
hooks.onOverlayClose()
local heavyStart = stepCount()
advance(3, 0.045)
assert(stepCount() == heavyStart, "HEAVY allowed helper steps")
advance(5) -- 3s after the smoothed frame trend actually clears HEAVY
shown = draw()
assert(shown:find("Workload: MODERATE", 1, true),
  "HEAVY must recover to MODERATE after 3s")
hooks.onOverlayClose()
advance(6)
shown = draw()
assert(shown:find("Workload: RELAXED", 1, true),
  "MODERATE must recover to RELAXED after 5s")
hooks.onOverlayClose()

-- Demand between the boundaries is stable MODERATE, not oscillating HEAVY.
advance(5, 0.029)
shown = draw()
assert(shown:find("Workload: MODERATE", 1, true),
  "mid-intensity workload did not enter MODERATE")
hooks.onOverlayClose()
local dutyStart = stepCount()
advance(15, 0.029)
local dutyCount = stepCount() - dutyStart
assert(dutyCount > 3 and dutyCount < 22,
  "MODERATE duty cycle misbehaved: " .. dutyCount)
advance(12)
shown = draw()
assert(shown:find("Workload: RELAXED", 1, true))
hooks.onOverlayClose()

-- Settings belong to the player, never to Resolver's threshold.lua.
selectedSensitivity = 70
draw()
assert(savedSettings:match("^70%s+1%s*$"))
selectedSensitivity = nil
selectedEnabled = false
draw()
assert(savedSettings:match("^70%s+0%s*$"))
hooks.onOverlayClose()
local disabled = stepCount()
advance(6)
assert(stepCount() == disabled, "OFF switch did not stop steps")
selectedEnabled = true
draw()
hooks.onOverlayClose()
advance(10)
assert(stepCount() > disabled, "ON switch did not restart maintenance")

-- One large natural collector hitch must not become sustained game pressure.
hooks.onUpdate(0.20)
advance(1)
shown = draw()
assert(not shown:find("Workload: HEAVY", 1, true),
  "single natural collector event caused workload reclassification")
hooks.onOverlayClose()

-- Configure a different manual preset, then arm the experiment in CET.
-- Nothing starts while the overlay is still open. The ten-second delay
-- begins only after returning to live gameplay.
draw()
local baselineCount = stepCount()
press("DEV: Start 90s A/B test")
advance(15)
assert(stepCount() == baselineCount and hasMark("GGC_TEST_ARMED"),
  "armed test ran while player was still in CET overlay")
assert(draw():find("TEST ARMED",1,true),
  "CET does not explain why the test is waiting")
hooks.onOverlayClose()
advance(9.9)
assert(stepCount() == baselineCount and hasMark("GGC_TEST_COUNTDOWN"),
  "countdown leaked assistance or did not begin in gameplay")
advance(0.2)
assert(hasMark("GGC_TEST_P1_OFF"),
  "ten-second countdown did not begin first 30-second phase")
advance(29.8)
assert(stepCount() == baselineCount,
  "baseline OFF phase executed helper work")
advance(0.3)
assert(hasMark("GGC_TEST_P2_128K_100MS"),
  "100ms phase transition did not occur at 30s")
local slowStart = stepCount()
advance(29.7)
local slowEnd = stepCount()
assert(slowEnd - slowStart >= 220 and slowEnd - slowStart <= 305,
  "100ms phase did not deliver approximately 10 requests/s")
for i = slowStart + 1, slowEnd do
  assert(steps[i] == 128,
    "100ms experiment step size deviated from fixed 128KiB")
end
advance(0.4)
assert(hasMark("GGC_TEST_P3_128K_50MS"),
  "50ms phase transition did not occur at 60s")
local fastStart = stepCount()
advance(29.6)
local fastEnd = stepCount()
assert(fastEnd - fastStart > (slowEnd - slowStart) * 1.45,
  "50ms phase did not increase request frequency over 100ms")
assert(fastEnd - fastStart >= 380,
  "50ms phase failed to deliver frequent requested GC steps")
for i = fastStart + 1, fastEnd do
  assert(steps[i] == 128,
    "50ms experiment step size deviated from fixed 128KiB")
end
advance(0.5)
assert(hasMark("GGC_TEST_DONE") and hudNotifications == 1,
  "90s test did not notify the player of completion")
local afterDone = stepCount()
advance(2)
assert(stepCount() == afterDone,
  "test restarted previous manual assistance before the player stopped capture")
shown = draw()
assert(shown:find("TEST DONE",1,true) and
       shown:find("100ms steps",1,true) and
       shown:find("50ms steps",1,true),
  "completion UI lacks stop-capture prompt or delivered-step counts")
assert(#marks >= 6 and marks[1] ~= nil,
  "capture-facing phase markers missing")
assert(fullCollections == 0, "G-GC issued a forced full collection")

-- Player can explicitly restore previous settings only after stopping.
press("Restore manual settings")
hooks.onOverlayClose()
local resumed = stepCount()
advance(1)
assert(stepCount() > resumed and steps[#steps] == 50,
  "test failed to restore saved manual step/cadence on request")

-- World loss and heavy-frame safety remain effective.
active = false
advance(2)
local lostWorld = stepCount()
advance(1)
assert(stepCount() == lostWorld,
  "G-GC executed without an active player")
active = true
advance(1)
local beforeHeavy = stepCount()
advance(3, 0.047)
assert(stepCount() == beforeHeavy,
  "G-GC ignored repeated severe frame backoff")

os, print, io = actualOs, oldPrint, oldIO
print("G-GC adaptive 3-state, fixed maintenance, preferences, 90s developer phases PASS")
