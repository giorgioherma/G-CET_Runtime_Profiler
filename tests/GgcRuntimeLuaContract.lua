-- Execute actual Resolver G-GC generated source inside mocked CET.
-- Regression: direct 0..256KiB, 50..500ms sliders and real 90s A/B phases.
local runtime = assert(arg[1], "generated G-GC Lua runtime required")
local hooks, rendered, marks, logged, steps = {}, {}, {}, {}, {}
local heapKiB, active = 583 * 1024, true
local simulatedCpu, fullCollections, hudNotifications = 0, 0, 0
local selectedSize, selectedInterval, clicked = nil, nil, nil
local sliders = {}
local prevPrint = print

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
    if label == "Step size (KiB)" then
      return selectedSize or value, selectedSize ~= nil
    elseif label == "Interval (ms)" then
      return selectedInterval or value, selectedInterval ~= nil
    end
    error("unknown slider: " .. tostring(label))
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

-- Initial Resolver intensity/cadence are mapped into the user's actual
-- hardware-independent slider units, not a hard CPU budget.
advance(1)
local shown = draw()
assert(sliders["Step size (KiB)"].min == 0 and
       sliders["Step size (KiB)"].max == 256,
  "GC size slider is not 0..256 KiB")
assert(sliders["Interval (ms)"].min == 50 and
       sliders["Interval (ms)"].max == 500,
  "GC interval slider is not 50..500 ms")
assert(shown:find("Lua heap",1,true) and
       shown:find("net growth",1,true) and
       shown:find("Step CPU",1,true) and
       shown:find("Step spacing",1,true) and
       shown:find("step heap drop",1,true),
  "compact CET UI missing useful heap / step statistics")
assert(not shown:find("predicted GC region",1,true) and
       not shown:find("semantic ",1,true) and
       not shown:find("Starting allowance estimate",1,true),
  "unnecessary diagnostic/forecast text still crowds the UI")

-- Zero must make NO collector invocation, unlike LuaJIT step(0).
selectedSize, selectedInterval = 0, 50
draw()
hooks.onOverlayClose()
local beforeOff = stepCount()
advance(1)
assert(stepCount() == beforeOff, "zero-size slider still calls collector")

-- Player selection must give EXACT fixed KiB requests, unaffected by
-- natural GC region and not capped by initial Resolver estimates.
selectedSize, selectedInterval = 256, 50
draw()
hooks.onOverlayClose()
advance(2)
assert(stepCount() >= beforeOff + 25,
  "manual 50ms cadence did not perform frequent steps")
for i = beforeOff + 1, stepCount() do
  assert(steps[i] == 256,
    "the 256-KiB slider still scales step size or exceeds its limit")
end
shown = draw()
assert(shown:find("Step spacing",1,true) and
       shown:find("Step CPU",1,true),
  "current / previous step timings not visible")

-- Configure a different manual preset, then arm the experiment in CET.
-- Nothing starts while the overlay is still open. The ten-second delay
-- begins only after returning to live gameplay.
selectedSize, selectedInterval = 60, 250
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
assert(fastEnd - fastStart > slowEnd - slowStart * 0.90,
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
assert(stepCount() > resumed and steps[#steps] == 60,
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

os, print = actualOs, oldPrint
print("G-GC 0..256KiB / 50..500ms, 90s phase test, timing, HUD and safety PASS")
