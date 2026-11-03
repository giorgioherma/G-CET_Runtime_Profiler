-- Mocked CET execution of the real source emitted by the Resolver.
-- A stale October stock-stack heap prior cannot silence the current stack.
local runtime = assert(arg[1], "generated G-GC Lua runtime required")
local hooks, marks, rendered = {}, {}, {}
local kib, active, steps, fulls = 583 * 1024, true, {}, 0
local simulatedCpu, selectedIntensity, selectedFrequency = 0, 37, 1250
local priorStart, priorHigh, priorReference = 794.70336, 932.91264, 863.808

package.preload["threshold"] = function() return {
  schema = 1, capture = "CET-20261023-035144_TEST_RESOLVER_STOCK_STACK",
  baselineFrameMs = 22.4354, hitchToleranceMs = 33.6531,
  frameReserveMs = 6.73062, maxAssistCpuMsPerSecond = 1.346124,
  pressureReferenceMiB = priorReference, pressureStartMiB = priorStart,
  pressureHighMiB = priorHigh,
  defaultIntensity = 37, defaultFrequencyMs = 1250
} end

registerForEvent = function(name, fn)
  assert(hooks[name] == nil, "duplicate G-GC event: " .. name)
  hooks[name] = fn
end
Game = { GetPlayer = function() return active and {} or nil end }
CETProfilerMark = function(label) marks[#marks + 1] = label end
collectgarbage = function(command, amount)
  if command == "count" then return kib end
  if command == "step" then
    assert(type(amount) == "number" and amount >= 1 and amount <= 128,
      "incremental step argument exceeded safety cap")
    steps[#steps + 1] = amount
    simulatedCpu = simulatedCpu + 0.00005
    return false
  end
  if command == "collect" then fulls = fulls + 1 end
  error("unexpected GC command " .. tostring(command))
end
local actualOs = os
os = { clock = function() return simulatedCpu end }
ImGui = {
  Begin = function() return true end,
  End = function() end,
  Text = function(s) rendered[#rendered+1] = s end,
  SliderInt = function(label, current)
    if label == "Intensity (%)" then return selectedIntensity, true end
    if label == "Frequency (ms)" then return selectedFrequency, true end
    error("unexpected slider " .. tostring(label))
  end
}
assert(loadfile(runtime))()
assert(type(hooks.onUpdate) == "function" and type(hooks.onDraw) == "function")
assert(type(hooks.onOverlayOpen) == "function" and
  type(hooks.onOverlayClose) == "function")
local function advance(n, dt)
  for _ = 1,n do hooks.onUpdate(dt or 0.020) end
end
local function draw()
  rendered = {}
  hooks.onOverlayOpen()
  hooks.onDraw()
  hooks.onOverlayClose()
  return table.concat(rendered, "\n")
end
local function getLearned()
  local shown = draw()
  local n = shown:match("learned (%d+)")
  assert(n, "G-GC live learning/diagnostics are not visible")
  return tonumber(n), shown
end

-- The captured October GC region is above the current heap. It must NOT
-- block low-cost maintenance: at >0% the helper regularly does small steps
-- even with a flat, low heap and no natural collection episode.
advance(160)
assert(#steps > 0, "nonzero default skipped continuous maintenance below GC band")
local low37Count, low37Size = #steps, steps[#steps]
assert(low37Size > 1 and low37Size <= 8,
  "low-heap continuous maintenance should request small, intensity-scaled work")
local _, shown = getLearned()
assert(shown:find("CONTINUOUS assistance", 1, true), "UI misstates pressure as gating")
assert(shown:find("not a gate", 1, true), "UI misstates natural collection region")

-- The same low heap and gameplay headroom respond to slider changes. With
-- 100% intensity and 250ms interval, there must be both MORE and LARGER
-- calls. This catches the original 0-vs-100 inert slider regression.
selectedIntensity, selectedFrequency = 100, 250
shown = draw()
assert(shown:find("APPLIED: intensity 100%", 1, true) and
       shown:find("interval 250 ms", 1, true),
       "slider values were not applied by CET")
advance(160)
assert(#steps > low37Count + 5,
  "high frequency did not increase the number of maintenance attempts")
assert(steps[#steps] > low37Size,
  "100% did not request more work than captured default at same low heap")
assert(marks[#marks]:find("GGC_V3_",1,true),
  "slider change did not produce versioned Profiler marker")
local low100Size = steps[#steps]

-- A high live-heap growth slope asks for more work BEFORE the captured
-- collection region; the historical region only supplies urgency context.
kib = 705 * 1024
advance(160)
assert(steps[#steps] > low100Size,
  "high live growth did not scale assistance ahead of estimated GC region")
local afterGrowth = #steps

-- Falling/flat heap is NOT a switch that turns off scheduled maintenance.
kib = 620 * 1024
advance(160)
assert(#steps > afterGrowth,
  "non-growing heap disabled positive-intensity continuous assistance")

-- Optional observed natural GC peaks may refine an approximate prediction,
-- but a new episode must never be required to keep the helper running.
selectedIntensity = 0
draw()
local beforeOff = #steps
-- The estimator admits only genuinely unassisted collection peaks:
-- let 10 seconds elapse after the last helper step before inferring a peak.
advance(650)
assert(#steps == beforeOff, "0% must disable collection")
kib = 680 * 1024
advance(50)
kib = 660 * 1024
advance(50)
local learned = getLearned()
assert(learned >= 1, "natural collection reference stopped updating in off mode")
local beforeRestart = #steps
selectedIntensity = 37
draw()
advance(160)
assert(#steps > beforeRestart,
  "learning cooldown or approximate trigger incorrectly blocked maintenance")

-- CPU/frame budget and loading still override continuous requested work.
selectedIntensity = 100
draw()
local beforeHeavy = #steps
advance(120, 0.047)
assert(#steps == beforeHeavy, "heavy gameplay backoff ignored")
active = false
advance(250)
assert(#steps == beforeHeavy, "assistance ran without valid world")
assert(fulls == 0, "helper forced full GC")
-- Same live heap and same frame-cost allowance MUST produce the same
-- incremental work, even when capture-derived NATURAL collection estimates
-- are wildly different. That estimate is information, not operating policy.
local function withPrior(startMiB, highMiB, referenceMiB)
  priorStart, priorHigh, priorReference = startMiB, highMiB, referenceMiB
  package.loaded["threshold"] = nil
  hooks, marks, steps = {}, {}, {}
  simulatedCpu, selectedIntensity, selectedFrequency = 0, 37, 1250
  active, kib = true, 583 * 1024
  assert(loadfile(runtime))()
  advance(160)
  local quiet = steps[#steps]
  assert(quiet, "constant assistance unexpectedly requires natural trigger")
  kib = 675 * 1024
  advance(160)
  local rising = steps[#steps]
  return quiet, rising
end
local aQuiet, aRising = withPrior(794, 932, 864)
local bQuiet, bRising = withPrior(275, 360, 315)
local cQuiet, cRising = withPrior(nil, nil, nil)
assert(aQuiet == bQuiet and aRising == bRising and
       aQuiet == cQuiet and aRising == cRising,
  "optional or changed natural GC region altered actual assistance")
local noForecast = draw()
assert(noForecast:find("NOT OBSERVED", 1, true),
  "no-GC-cycle helper incorrectly asserts a predicted collection region")
os = actualOs
print("G-GC continuous baseline, live scaling, sliders, GC prediction context, safety PASS")
