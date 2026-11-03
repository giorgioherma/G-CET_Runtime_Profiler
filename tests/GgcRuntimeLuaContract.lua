-- Mocked CET runtime test of the actual Resolver-owned controller source.
-- Does not claim real LuaJIT phase latency or in-game gameplay acceptance.
local runtime = assert(arg[1], "runtime .lua path required")
local hooks, marks = {}, {}
local kib, active, stepCount, collectCount = 350 * 1024, true, 0, 0
local stepSizes = {}
local clock, intensity, frequency, uiActive = 0, 0, 1000, false
package.preload["threshold"] = function() return {
  schema = 1, capture = "MOCK_CAPTURE",
  baselineFrameMs = 20, hitchToleranceMs = 45, frameReserveMs = 10,
  pressureStartMiB = 600, pressureHighMiB = 660,
  maxAssistCpuMsPerSecond = 2,
  defaultIntensity = 37, defaultFrequencyMs = 1250
} end
registerForEvent = function(name, fn)
  assert(not hooks[name], "duplicate G-GC hook " .. name)
  hooks[name] = fn
end
Game = { GetPlayer = function() return active and {} or nil end }
CETProfilerMark = function(mark) marks[#marks+1] = mark end
collectgarbage = function(command, size)
  if command == "count" then
    kib = kib + 1024
    return kib
  elseif command == "step" then
    assert(type(size) == "number" and size >= 1 and size <= 32,
      "unbounded incremental work")
    stepCount = stepCount + 1
    stepSizes[#stepSizes + 1] = size
    clock = clock + 0.00005
    return false
  elseif command == "collect" then
    collectCount = collectCount + 1
    error("G-GC must not force full collection")
  end
  error("unsupported GC command " .. tostring(command))
end
local realOs = os
os = { clock = function() return clock end }
ImGui = {
  Begin = function() return true end, End = function() end,
  Text = function() end,
  SliderInt = function(name, value)
    if name == "Intensity (%)" then return intensity end
    if name == "Frequency (ms)" then return frequency end
    error("unexpected slider")
  end
}
assert(loadfile(runtime))()
assert(type(hooks.onUpdate) == "function" and type(hooks.onDraw) == "function")
assert(type(hooks.onOverlayOpen) == "function" and
       type(hooks.onOverlayClose) == "function")
local function advance(count, dt)
  for _ = 1, count do hooks.onUpdate(dt) end
end

-- Resolver's nonzero initial allowance must NOT cause work at low pressure.
advance(100, 0.025)
assert(stepCount == 0, "nonzero default ran GC with no heap pressure")
assert(collectCount == 0, "controller forced full GC")

-- The SAME generated starting cap should deploy modest help as pressure rises,
-- then more assistance when the live heap reaches the high-pressure region.
kib = 630 * 1024
advance(160, 0.025)
assert(stepCount > 0, "calibrated nonzero default did not help rising heap")
local moderateKb = stepSizes[#stepSizes]
local afterModerate = stepCount
kib = 720 * 1024
advance(160, 0.025)
assert(stepCount > afterModerate, "high-pressure region did not request GC assistance")
assert(stepSizes[#stepSizes] > moderateKb,
       "requested effort did not adapt to live pressure")
assert(#marks > 0, "automatic capture-derived setting not marked")
local afterAutomatic = stepCount

-- Player manual 0% is an unconditional off switch even with high pressure.
intensity = 0
hooks.onOverlayOpen()
hooks.onDraw()
hooks.onOverlayClose()
advance(100, 0.025)
assert(stepCount == afterAutomatic, "manual 0% did not disable assistance")

-- Intensity and frequency can be tuned during the SAME capture.
intensity, frequency = 75, 250
hooks.onOverlayOpen()
hooks.onDraw()
hooks.onOverlayClose()
advance(100, 0.025)
assert(stepCount > afterAutomatic, "manual settings did not resume assistance")
local afterGood = stepCount

-- Sudden expensive frames suppress assistance despite high pressure.
advance(100, 0.047)
assert(stepCount == afterGood, "backoff ignored expensive gameplay frames")

-- Loading/no-player state must not permit GC assistance.
active = false
advance(250, 0.025)
assert(stepCount == afterGood, "assistance ran without valid game world")

-- The 0% slider must stay authoritative after loading or transitions.
active, intensity = true, 0
hooks.onOverlayOpen()
hooks.onDraw()
hooks.onOverlayClose()
advance(100, 0.025)
assert(stepCount == afterGood, "intensity 0 did not disable assistance")
assert(collectCount == 0, "full GC was invoked")
os = realOs
print("G-GC capture-derived defaults, heap demand, headroom, sliders and lifecycle PASS")
