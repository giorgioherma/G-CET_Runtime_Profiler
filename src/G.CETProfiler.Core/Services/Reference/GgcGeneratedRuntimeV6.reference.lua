-- GGC_RUNTIME_V1_RESOLVER_OWNED
-- Single Resolver-generated G-GC runtime. No profiler DLL required.
-- Player controls request 0..256 KiB per step and 50..500 ms interval.
-- The dev experiment overrides both settings for measured 30s phases.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 3
    or type(config.hitchToleranceMs) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local function clamp(x, lo, hi)
  return math.max(lo, math.min(hi, x))
end

-- Resolver's conservative percentage and cadence estimate are starting
-- values only. Map them into explicit, user-editable units.
local manualKiB = clamp(math.floor((config.defaultIntensity or 0) * 2.56 + 0.5), 0, 256)
local manualIntervalMs = clamp(math.floor((config.defaultFrequencyMs or 500) + 0.5), 50, 500)
local overlayOpen = false
local heapSampleAge, sinceStep, sinceActualStep = 0, 0, 0
local haveCompletedStep = false
local heapMiB, previousHeap, growthMiBps = 0, nil, 0
local activeWorld, backoffSeconds = false, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local lastStepKiB, totalRequestedKiB = 0, 0
local lastHeapDropKiB, totalHeapDropKiB = 0, 0
local lastCpuMs, previousCpuMs, maximumCpuMs = 0, 0, 0
local lastIntervalMs, previousIntervalMs = 0, 0
local lastBlock = "awaiting heap sample"

-- Test states: idle -> armed -> countdown -> off -> slow -> fast -> done.
-- All stage durations use actual CET gameplay delta; no timers or worker
-- subscriptions, and no phase clock advancement with the overlay open.
local testState, phaseAge, countdownAge = "idle", 0, 0
local testSaveKiB, testSaveIntervalMs = nil, nil
local testSteps, testRequestedKiB = {0, 0, 0}, {0, 0, 0}
local testCostMs, testMaximumCostMs = {0, 0, 0}, {0, 0, 0}
local testStarted = false

local function mark(label)
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark, label)
  end
end

local function announce(label, message)
  print("[G-GC] " .. message)
  mark(label)
end

local function notifyFinished(failed)
  -- Standard CET HUD warning-message blackboard, guarded for installations
  -- where the game's notification objects are not available.
  local success = pcall(function()
    local message = SimpleScreenMessage.new()
    message.message = failed and "G-GC test ABORTED - stop recording"
      or "G-GC 90s test DONE - stop recording"
    message.isShown = true
    message.duration = 8
    local definitions = Game.GetAllBlackboardDefs()
    local board = Game.GetBlackboardSystem():Get(definitions.UI_Notifications)
    board:SetVariant(definitions.UI_Notifications.WarningMessage,
      ToVariant(message), true)
  end)
  if not success then
    print("[G-GC] HUD notification unavailable; completion is shown in CET.")
  end
end

local function beginPhase(state)
  testState, phaseAge, sinceStep = state, 0, 0
  if state == "off" then
    testStarted = true
    announce("GGC_TEST_P1_OFF",
      "90s test START / phase 1: assistance OFF for 30s.")
  elseif state == "slow" then
    announce("GGC_TEST_P2_128K_100MS",
      "90s test phase 2: 128 KiB every 100 ms for 30s.")
  elseif state == "fast" then
    announce("GGC_TEST_P3_128K_50MS",
      "90s test phase 3: 128 KiB every 50 ms for 30s.")
  elseif state == "done" then
    announce("GGC_TEST_DONE",
      "90s test COMPLETE. Stop capture. Assistance OFF until restored.")
    notifyFinished(false)
  end
end

local function startTest()
  if testState ~= "idle" and testState ~= "done" then return end
  testSaveKiB, testSaveIntervalMs = manualKiB, manualIntervalMs
  testState, countdownAge, phaseAge, testStarted = "armed", 0, 0, false
  testSteps, testRequestedKiB = {0, 0, 0}, {0, 0, 0}
  testCostMs, testMaximumCostMs = {0, 0, 0}, {0, 0, 0}
  sinceStep = 0
  announce("GGC_TEST_ARMED",
    "90s test armed. Close CET overlay; 10s countdown then 30/30/30s.")
end

local function restoreSettings()
  if testSaveKiB ~= nil then
    manualKiB, manualIntervalMs = testSaveKiB, testSaveIntervalMs
  end
  testState, phaseAge, countdownAge = "idle", 0, 0
  sinceStep = 0
end

local function observe(seconds)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    activeWorld = false
    backoffSeconds = 30
    return
  end
  heapMiB = kib / 1024
  if previousHeap then
    local slope = (heapMiB - previousHeap) / seconds
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
  end
  previousHeap = heapMiB
  activeWorld = false
  if Game ~= nil and type(Game.GetPlayer) == "function" then
    local okPlayer, player = pcall(Game.GetPlayer)
    activeWorld = okPlayer and player ~= nil
  end
end

local function advanceTest(dt)
  if testState == "idle" or testState == "done" or testState == "failed"
      or overlayOpen or not activeWorld then return end

  if testState == "armed" then
    testState, countdownAge = "countdown", 0
    announce("GGC_TEST_COUNTDOWN", "10s test countdown started in gameplay.")
    return
  end
  if testState == "countdown" then
    countdownAge = countdownAge + dt
    if countdownAge >= 10 then beginPhase("off") end
    return
  end

  -- Transition based on gameplay elapsed time, NOT scheduled-step count.
  -- An expensive step or heavy-frame backoff cannot silently lengthen
  -- a phase or alter the preset workload.
  phaseAge = phaseAge + dt
  if phaseAge >= 30 then
    if testState == "off" then
      beginPhase("slow")
    elseif testState == "slow" then
      beginPhase("fast")
    elseif testState == "fast" then
      beginPhase("done")
    end
  end
end

local function requestedStep()
  if testState == "idle" then return manualKiB, manualIntervalMs, 0 end
  if testState == "slow" then return 128, 100, 2 end
  if testState == "fast" then return 128, 50, 3 end
  return 0, 50, 0
end

local function tick(dt)
  if type(dt) ~= "number" or dt <= 0 or dt > 0.50 then
    backoffSeconds = 3
    lastBlock = "invalid/heavy frame"
    return
  end
  if dt * 1000 >= config.hitchToleranceMs then
    backoffSeconds = math.max(backoffSeconds, 2)
  end
  if backoffSeconds > 0 then backoffSeconds = math.max(0, backoffSeconds - dt) end
  heapSampleAge, sinceStep = heapSampleAge + dt, sinceStep + dt
  sinceActualStep = sinceActualStep + dt
  if heapSampleAge >= 0.75 then
    local seconds = heapSampleAge
    heapSampleAge = 0
    observe(seconds)
  end

  advanceTest(dt)
  local amount, intervalMs, phase = requestedStep()
  -- Opening CET during the experiment pauses BOTH its phase clock and
  -- incremental collector calls, keeping the capture comparisons aligned.
  if testState ~= "idle" and overlayOpen then
    lastBlock = "test paused in CET"
    return
  end
  if previousHeap == nil then lastBlock = "awaiting heap sample"; return end
  if amount <= 0 then lastBlock = "assistance off"; return end
  if not activeWorld then lastBlock = "outside gameplay"; return end
  if backoffSeconds > 0 then lastBlock = "frame/GC cost backoff"; return end
  if sinceStep * 1000 < intervalMs then lastBlock = "step interval"; return end

  -- This is actual gameplay time since the last step attempt, not the
  -- configured nominal interval. os.clock below measures approximate CPU
  -- time of the call, not a hard wall-time deadline.
  sinceStep = 0
  local beforeOK, beforeKiB = pcall(collectgarbage, "count")
  local t0 = os.clock()
  local ok, cycle = pcall(collectgarbage, "step", amount)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  previousCpuMs, lastCpuMs = lastCpuMs, cpuMs
  maximumCpuMs = math.max(maximumCpuMs, cpuMs)
  if not ok then
    rejectedSteps = rejectedSteps + 1
    backoffSeconds = 30
    if phase > 0 then
      testState = "failed"
      announce("GGC_TEST_FAILED",
        "90s test ABORTED because a GC step failed; stop capture.")
      notifyFinished(true)
    else
      manualKiB = 0
    end
    lastBlock = "GC error / disabled"
    return
  end

  lastStepKiB = amount
  previousIntervalMs = lastIntervalMs
  lastIntervalMs = haveCompletedStep and sinceActualStep * 1000 or 0
  sinceActualStep, haveCompletedStep = 0, true
  stepCount, totalRequestedKiB = stepCount + 1, totalRequestedKiB + amount
  if cycle == true then completedCycles = completedCycles + 1 end
  lastHeapDropKiB = 0
  if beforeOK and type(beforeKiB) == "number" then
    local afterOK, afterKiB = pcall(collectgarbage, "count")
    if afterOK and type(afterKiB) == "number" then
      lastHeapDropKiB = math.max(0, beforeKiB - afterKiB)
      totalHeapDropKiB = totalHeapDropKiB + lastHeapDropKiB
    end
  end

  if phase > 0 then
    testSteps[phase] = testSteps[phase] + 1
    testRequestedKiB[phase] = testRequestedKiB[phase] + amount
    testCostMs[phase] = testCostMs[phase] + cpuMs
    testMaximumCostMs[phase] = math.max(testMaximumCostMs[phase], cpuMs)
  end
  lastBlock = "running"
  -- One indivisible LuaJIT collector phase can exceed the nominal cost.
  -- Cool down AFTER such a step; never force a full collection.
  if cpuMs > math.max(4, config.hitchToleranceMs * 0.25) then
    backoffSeconds = 5
    lastBlock = "expensive-step backoff"
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap = nil
  growthMiBps = 0
  testState = "idle"
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC")
  if shown then
    ImGui.Text(string.format("Lua heap %.1f MiB  |  net growth %+.2f MiB/s",
      heapMiB, growthMiBps))

    if testState == "idle" then
      local newSize = ImGui.SliderInt("Step size (KiB)", manualKiB, 0, 256)
      local newDelay = ImGui.SliderInt("Interval (ms)", manualIntervalMs, 50, 500)
      if type(newSize) == "number" then
        manualKiB = clamp(math.floor(newSize), 0, 256)
      end
      if type(newDelay) == "number" then
        manualIntervalMs = clamp(math.floor(newDelay), 50, 500)
      end
    else
      ImGui.Text(string.format("Manual setting %d KiB / %d ms (test override)",
        manualKiB, manualIntervalMs))
    end
    ImGui.Text(string.format("Steps %d | last %d KiB | cycles %d",
      stepCount, lastStepKiB, completedCycles))
    ImGui.Text(string.format("GC requested %.2f MiB | step heap drop ~%.2f MiB",
      totalRequestedKiB / 1024, totalHeapDropKiB / 1024))
    ImGui.Text(string.format("Step CPU %.3f ms current / %.3f ms previous | max %.3f",
      lastCpuMs, previousCpuMs, maximumCpuMs))
    ImGui.Text(string.format("Observed spacing %.0f ms last / %.0f ms previous",
      lastIntervalMs, previousIntervalMs))

    if testState == "idle" then
      if ImGui.Button("DEV: Start 90s A/B test") then startTest() end
    elseif testState == "armed" then
      ImGui.Text("TEST ARMED: close CET overlay to start 10s countdown")
      if ImGui.Button("Cancel test") then restoreSettings() end
    elseif testState == "countdown" then
      ImGui.Text(string.format("TEST: starting in %.0f s (after closing overlay)",
        math.max(0, 10 - countdownAge)))
      if ImGui.Button("Cancel test") then restoreSettings() end
    elseif testState == "done" or testState == "failed" then
      ImGui.Text(testState == "failed" and
        "TEST ABORTED: stop capture. Assistance is OFF." or
        "TEST DONE: stop capture. Assistance is OFF.")
      ImGui.Text(string.format("100ms steps %d | 50ms steps %d",
        testSteps[2], testSteps[3]))
      if ImGui.Button("Restore manual settings") then restoreSettings() end
    else
      local phaseName = testState == "off" and "OFF"
        or testState == "slow" and "128 KiB / 100ms"
        or "128 KiB / 50ms"
      ImGui.Text(string.format("TEST: %s | %.0f / 30 s",
        phaseName, phaseAge))
      if ImGui.Button("Abort test") then
        announce("GGC_TEST_ABORTED", "90s test cancelled by user.")
        restoreSettings()
      end
    end

    if rejectedSteps > 0 then
      ImGui.Text(string.format("Failed GC steps: %d", rejectedSteps))
    end
    if lastBlock ~= "running" then
      ImGui.Text("Status: " .. lastBlock)
    end
  end
  ImGui.End()
end)
