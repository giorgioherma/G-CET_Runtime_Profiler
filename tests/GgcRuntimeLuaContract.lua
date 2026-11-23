-- Execute the current source-generated G-GC Lua inside mocked CET.
-- Validate manual controls, 0-KiB steps, previous preference migration,
-- real delivered cadence, world guards, honest diagnostics and no A/B.
local runtime = assert(arg[1], "generated G-GC Lua runtime required")
local hooks, rendered, steps, stepTimes = {}, {}, {}, {}
local heapKiB, active = 583 * 1024, true
local simulatedCpu, fullCollections, elapsed = 0, 0, 0
local sliders, savedSettings = {}, nil
local selectedInterval, clicked, expensiveNext = nil, nil, false

local realPrint, realIO, realOS = print, io, os
io = { open = function(path, mode)
  assert(path == "user_settings.txt", "unexpected preference path")
  if mode == "r" then
    if savedSettings == nil then return nil end
    return {read = function() return savedSettings end, close = function() end}
  end
  assert(mode == "w")
  return {write = function(_, value) savedSettings = value end,
          close = function() end}
end }
package.preload["threshold"] = function() return {
  schema=4, capture="STOCK_STACK", baselineFrameMs=32.1,
  hitchToleranceMs=48.2, pressureReferenceMiB=863
} end
registerForEvent = function(name, fn)
  assert(hooks[name] == nil, "duplicate callback: " .. name)
  hooks[name] = fn
end
Game = {GetPlayer = function() return active and {} or nil end}
collectgarbage = function(command, amount)
  if command == "count" then return heapKiB end
  if command == "step" then
    assert(type(amount) == "number" and amount >= 0 and amount <= 200,
      "out-of-range / full collector step requested")
    steps[#steps + 1] = amount
    stepTimes[#stepTimes + 1] = elapsed
    simulatedCpu = simulatedCpu + (expensiveNext and .020 or .00005)
    expensiveNext = false
    heapKiB = math.max(64, heapKiB - math.min(32, amount / 4))
    return false -- Natural collector cycles are not counted by this helper.
  end
  if command == "collect" then fullCollections = fullCollections + 1 end
  error("unexpected GC command: " .. tostring(command))
end
os = {clock = function() return simulatedCpu end}
print = function() end
ImGui = {
  Begin = function() return true end, End = function() end,
  Text = function(s) rendered[#rendered + 1] = s end,
  SliderInt = function(label, value, lo, hi)
    assert(label == "##Collection interval")
    sliders[label] = {lo=lo, hi=hi}
    return selectedInterval or value
  end,
  Checkbox = function() error("Automatic helper inhibition must be removed") end,
  SameLine = function() end,
  Button = function(label)
    assert(not label:find("DEV:") and not label:find("test"),
      "Built-in A/B UI must be removed")
    if clicked == label then clicked = nil; return true end
    return false
  end
}
local function load(prefs)
  hooks, steps, stepTimes, sliders = {}, {}, {}, {}
  heapKiB, active, simulatedCpu, elapsed = 583*1024, true, 0, 0
  savedSettings, selectedInterval, clicked, expensiveNext = prefs, nil, nil, false
  assert(loadfile(runtime))()
  for _, name in ipairs({"onUpdate","onDraw","onOverlayOpen","onOverlayClose","onShutdown"}) do
    assert(type(hooks[name]) == "function", "missing hook " .. name)
  end
end
local function advance(seconds, dt)
  dt = dt or .02
  for _ = 1, math.floor(seconds / dt + .5) do
    elapsed = elapsed + dt
    hooks.onUpdate(dt)
  end
end
local function draw()
  rendered = {}
  hooks.onOverlayOpen()
  hooks.onDraw()
  return table.concat(rendered, "\n")
end
local function close() hooks.onOverlayClose() end
local function press(label)
  clicked = label
  draw()
  assert(clicked == nil, "button missing " .. label)
  close()
end
local function setInterval(index)
  selectedInterval = index
  draw()
  selectedInterval = nil
  close()
end

load(nil)
advance(1)
local output = draw()
assert(sliders["##Collection interval"].lo == 1 and
       sliders["##Collection interval"].hi == 11,
  "unexpected interval control range")
assert(output:find("Collection interval: 3000 ms", 1, true),
  "default must be 3 seconds")
assert(output:find("Requested GC step argument: 50 KiB", 1, true))
assert(output:find("Cycles completed BY helper calls", 1, true))
assert(output:find("Natural LuaJIT cycles: NOT MEASURED", 1, true))
assert(output:find("Sampled heap low / high", 1, true))
assert(not output:find("Workload:", 1, true) and
       not output:find("Developer test", 1, true),
  "obsolete workload or A/B UI is still displayed")
close()
local before = #steps
advance(2.1)
assert(#steps == before + 1 and steps[#steps] == 50,
  "50 KiB / 3 s default failed")
assert(savedSettings == nil, "default should not rewrite preferences")

-- OFF is an interval, whereas size zero is an actual LuaJIT step request.
setInterval(1)
assert(savedSettings == "v3 50 0\n", "OFF must persist unambiguously")
before = #steps
advance(12)
assert(#steps == before, "OFF executed a collector call")
setInterval(4) -- 3 seconds
press("0 KiB")
before = #steps
advance(3.2)
assert(#steps == before + 1 and steps[#steps] == 0,
  "0 KiB did not request an incremental collector step")
assert(draw():find("0 KiB STILL requests", 1, true))
close()

-- All five size choices pass exactly the requested KiB parameter,
-- including zero. No additional / hidden adaptive collector.
setInterval(11) -- 50ms
for _, choice in ipairs({{"0 KiB",0},{"1 KiB",1},{"10 KiB",10},
                        {"50 KiB",50},{"200 KiB",200}}) do
  press(choice[1])
  before = #steps
  advance(.2, .01)
  assert(#steps > before and steps[#steps] == choice[2],
    "incorrect selected size: " .. choice[1])
end
assert(savedSettings == "v3 200 50\n")

-- Interval slider is exact and does not interpolate or create a busy loop.
local periods = {0,10000,5000,3000,2000,1000,500,300,200,100,50}
press("1 KiB")
for index, ms in ipairs(periods) do
  setInterval(index)
  before = #steps
  if ms == 0 then
    advance(1)
    assert(#steps == before, "OFF produced steps")
  else
    advance(ms / 1000 + .12, .01)
    assert(#steps > before, "selected interval failed: " .. ms)
    for i = before + 2, #stepTimes do
      assert(stepTimes[i] - stepTimes[i-1] >= ms/1000 - .000001,
        "period was shorter than requested: " .. ms)
    end
  end
end

-- Repeated slow updates and expensive explicit steps must not silently
-- suspend or retime manual maintenance.
setInterval(8) -- 300ms
before = #steps
advance(3, .047)
assert(#steps - before >= 8, "slow frames inhibited manual cadence")
expensiveNext = true
advance(.36, .02)
local costly = #steps
advance(.65, .02)
assert(#steps - costly >= 2, "expensive step inserted a hidden cooldown")
output = draw()
assert(output:find("Approx step CPU peak / sum", 1, true))
assert(output:find("Observed step spacing", 1, true))
assert(output:find("Helper calls / min of enabled gameplay", 1, true))
assert(output:find("Last GC request: 1 KiB (not bytes reclaimed)", 1, true))
close()

-- Gameplay loss prohibits steps, preserving original source-owned guard.
active = false
advance(1)
before = #steps
advance(2)
assert(#steps == before, "helper ran outside game world")
active = true
advance(1)
assert(#steps > before, "helper failed to recover after world return")
assert(fullCollections == 0, "forced full collection issued")

-- Every successful step(0) still counts as a helper call; zero helper
-- completed cycles does NOT mean zero natural GC cycles.
setInterval(11)
press("0 KiB")
before = #steps
advance(.3, .01)
assert(#steps > before and steps[#steps] == 0)
output = draw()
assert(output:find("Cycles completed BY helper calls: 0", 1, true))
close()

-- Upgrade previous v2 preferences without restoring automatic gating.
load("v2 8192 30000 1\n")
advance(1)
output = draw()
assert(output:find("Requested GC step argument: 200 KiB", 1, true))
assert(output:find("Collection interval: 10000 ms", 1, true))
assert(savedSettings == "v3 200 10000\n")
close()
-- Persist the old user's manual OFF regardless of the obsolete auto flag.
load("v2 50 0 1\n")
advance(1)
assert(savedSettings == "v3 50 0\n")
before = #steps
advance(4)
assert(#steps == before, "old OFF preference was re-enabled")
-- Load current v3 preference, including step(0), without modification.
load("v3 0 1000\n")
advance(2.2)
assert(#steps >= 2 and steps[#steps] == 0)
assert(savedSettings == "v3 0 1000\n")
-- Legacy 2-token preference preserves explicit OFF.
load("70 0\n")
advance(4)
assert(savedSettings == "v3 50 0\n" and #steps == 0)
hooks.onShutdown()

os, print, io = realOS, realPrint, realIO
print("G-GC V12 manual controls, 0-KiB GC nudge, honest reporting, safe preferences, world guard PASS")
