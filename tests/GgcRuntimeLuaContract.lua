-- Mocked CET execution of the real source emitted by the Resolver.
-- A stale October stock-stack heap prior cannot silence the current stack.
local runtime = assert(arg[1], "generated G-GC Lua runtime required")
local hooks, marks, rendered = {}, {}, {}
local kib, active, steps, fulls = 583 * 1024, true, {}, 0
local simulatedCpu, selectedIntensity, selectedFrequency = 0, 37, 1250

package.preload["threshold"] = function() return {
  schema = 1, capture = "CET-20261023-035144_TEST_RESOLVER_STOCK_STACK",
  baselineFrameMs = 22.4354, hitchToleranceMs = 33.6531,
  frameReserveMs = 6.73062, maxAssistCpuMsPerSecond = 1.346124,
  pressureReferenceMiB = 863.808, pressureStartMiB = 794.70336,
  pressureHighMiB = 932.91264,
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

-- Stale historical prior, low heap, and rising heap: no premature GC steps.
advance(50)
assert(#steps == 0, "old capture caused unneeded low-pressure collection")
kib = 655 * 1024
advance(50)
assert(#steps == 0, "stale stock-stack threshold was incorrectly treated as live demand")

-- Observe a natural current-stack collection peak around 680 MiB, despite
-- the old 863 MiB prior. Do not require an exact trigger timestamp.
kib = 680 * 1024
advance(50)
kib = 662 * 1024
advance(50)
local learned, shown = getLearned()
assert(learned == 1, "natural current-stack heap drop did not recalibrate")
assert(shown:find("LIVE reference 680.0 MiB",1,true),
       "live heap reference did not replace stale captured region")
assert(#steps == 0, "natural GC drop caused immediate inappropriate assistance")

-- Let the episode settle and the live heap rise into the learned band.
kib = 590 * 1024
advance(450)
kib = 650 * 1024
advance(130)
assert(#steps > 0, "calibrated starting intensity never executed live assistance")
local at37, size37 = #steps, steps[#steps]
assert(size37 > 1, "generated default did not request an incremental step")

-- Slider has a real range. Same heap; manual 100% and faster cadence must
-- request more/larger steps while staying within captured CPU allowance.
selectedIntensity, selectedFrequency = 100, 250
shown = draw()
assert(shown:find("APPLIED: intensity 100%%") and shown:find("interval 250 ms"),
       "CET slider selections did not bind to live settings")
advance(130)
assert(#steps > at37, "100% settings did not increase assistance")
assert(steps[#steps] > size37, "100% intensity requested no more work than 37%")
assert(marks[#marks]:find("GGC_V2_",1,true), "changes were not marked for Profiler")

-- Slider zero suppresses actual GC work even at high pressure.
selectedIntensity = 0
draw()
local beforeDisabled = #steps
advance(140)
assert(#steps == beforeDisabled, "0% slider failed to disable assistance")

-- Even 100% must respect heavy gameplay and loading.
selectedIntensity = 100
draw()
local beforeHeavy = #steps
advance(120, 0.047)
assert(#steps == beforeHeavy, "heavy gameplay was ignored by controller")
active = false
advance(250)
assert(#steps == beforeHeavy, "assist ran with no valid world")
assert(fulls == 0, "G-GC forced a full collection")
os = actualOs
print("G-GC stale capture recovery, real sliders, live heap, cost and loading PASS")
