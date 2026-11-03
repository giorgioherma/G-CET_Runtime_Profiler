-- GGC_RUNTIME_V1_RESOLVER_OWNED
-- G-GC source-owned controller: actual generated semantic/generic rules
-- estimate post-optimized CET work for STARTING sliders. Player owns tuning.
-- Continuous small assistance scales with live heap pressure and growth.
-- The approximate LuaJIT collection region is timing context, NOT an on/off
-- threshold. No scheduled full GC.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 3
    or type(config.hitchToleranceMs) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local intensity = math.max(0, math.min(100, config.defaultIntensity or 0))
local frequencyMs = math.max(250, math.min(5000, config.defaultFrequencyMs or 1000))
local overlayOpen = false
local age, sinceStep, windowAge, windowUsedMs = 0, 0, 0, 0
local heapMiB, previousHeap, growthMiBps, heapFloorMiB = 0, nil, 0, nil
local backoffSeconds = 0
local lastStepCpuMs, maximumStepCpuMs, totalStepCpuMs = 0, 0, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local settingsDirty, firstMark, heartbeatAge = true, true, 0
local activeWorld, lastStepKb, lastPressure, lastDemand = false, 0, 0, 0
local lastBlock = 1
local blockNames = {
  "waiting for live heap sample", "assist disabled by user",
  "world/player unavailable", "frame backoff",
  "minimum step interval", "maintenance assistance",
  "rising heap assistance", "high live demand",
  "temporary step-cost backoff"
}

-- A captured slow-step heap is NOT necessarily a GC start threshold. It is
-- a rough contextual prior only. Learning a natural peak refines the displayed
-- estimate but can NEVER enable/disable the ongoing incremental helper.
local liveReferenceMiB = nil
local livePeakMiB = nil
local learnedEpisodes, learnCooldown, lastAssistAge = 0, 0, 1e6
local pressureStartMiB, pressureHighMiB =
  config.pressureStartMiB, config.pressureHighMiB

local function updatePressureBand(reference)
  local captureSlack = (config.pressureHighMiB and config.pressureReferenceMiB)
    and (config.pressureHighMiB - config.pressureReferenceMiB)
    or (reference * 0.08)
  local slack = math.max(24, math.min(reference * 0.12,
    math.max(reference * 0.07, captureSlack * 0.7)))
  pressureStartMiB = math.max(64, reference - slack)
  pressureHighMiB = reference + slack
end

local function markSettings()
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark,
      "GGC_V4_I" .. intensity .. "_F" .. frequencyMs ..
      "_S" .. stepCount .. "_L" .. learnedEpisodes)
  end
end

local function observe(dt)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    backoffSeconds = 30
    lastBlock = 4
    return
  end
  heapMiB = kib / 1024
  -- Recent live low-water mark measures how much heap has accumulated
  -- since a local trough. This never depends on the predicted GC trigger.
  if heapFloorMiB == nil or heapMiB < heapFloorMiB then
    heapFloorMiB = heapMiB
  end
  if previousHeap then
    local slope = (heapMiB - previousHeap) / dt
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
    -- Coalesce consecutive sweep/drop samples: the first substantial drop
    -- in an episode learns one approximate natural peak, not many triggers.
    local drop = previousHeap - heapMiB
    if drop >= math.max(8, previousHeap * 0.012) then
      -- Refresh accumulation baseline after a substantial heap reduction;
      -- do not require proof of a natural cycle for ordinary heap accounting.
      heapFloorMiB = heapMiB
    end
    if learnCooldown <= 0 and drop >= math.max(8, previousHeap * 0.012)
        and livePeakMiB and lastAssistAge >= 10 then
      local peak = livePeakMiB
      if peak > 64 and peak < 16384 then
        if liveReferenceMiB then
          liveReferenceMiB = liveReferenceMiB * 0.65 + peak * 0.35
        else
          liveReferenceMiB = peak
        end
        updatePressureBand(liveReferenceMiB)
        learnedEpisodes = learnedEpisodes + 1
      end
      learnCooldown = 8
    end
  end

  -- A peak is valid only for the same unassisted natural episode. Do not
  -- train on the collector steps that this helper has just requested.
  if livePeakMiB == nil or (learnCooldown <= 0 and heapMiB > livePeakMiB) then
    livePeakMiB = heapMiB
  end
  if learnCooldown <= 0 and livePeakMiB and heapMiB < livePeakMiB - 32 then
    -- A completed falling heap episode becomes the baseline for the next.
    livePeakMiB = heapMiB
  end
  previousHeap = heapMiB

  -- One inexpensive player check per sample, never on the per-frame hot path.
  activeWorld = false
  if Game ~= nil and type(Game.GetPlayer) == "function" then
    local playerOk, player = pcall(Game.GetPlayer)
    activeWorld = playerOk and player ~= nil
  end
end

local function tick(dt)
  if type(dt) ~= "number" or dt <= 0 or dt > 0.50 then
    backoffSeconds = 3
    lastBlock = 4
    return
  end
  -- CET already gives us delta time for scheduling. This single severe
  -- frame guard is NOT a game-thread CPU utilisation measurement.
  local frame = dt * 1000
  if frame >= config.hitchToleranceMs then
    backoffSeconds = math.max(backoffSeconds, 2)
  end
  if backoffSeconds > 0 then backoffSeconds = math.max(0, backoffSeconds - dt) end
  if learnCooldown > 0 then learnCooldown = math.max(0, learnCooldown - dt) end
  lastAssistAge = lastAssistAge + dt
  age = age + dt
  sinceStep = sinceStep + dt
  windowAge = windowAge + dt
  heartbeatAge = heartbeatAge + dt
  if windowAge >= 1 then
    windowAge, windowUsedMs = windowAge % 1, 0
  end
  if age >= 0.75 then
    local observedSeconds = age
    age = 0
    observe(observedSeconds)
    if settingsDirty or firstMark or heartbeatAge >= 15 then
      markSettings()
      heartbeatAge, settingsDirty, firstMark = 0, false, false
    end
  end

  if previousHeap == nil then lastBlock = 1; return end
  if intensity <= 0 then lastBlock = 2; return end
  if not activeWorld then lastBlock = 3; return end
  if backoffSeconds > 0 then lastBlock = 4; return end
  if sinceStep * 1000 < frequencyMs then lastBlock = 5; return end

  -- Always offer a small incremental step at the player-selected cadence
  -- while the player is in-world. The captured estimate is DEFAULTS ONLY.
  -- Live accumulation since a heap trough and net growth scale step SIZE.
  -- The approximate LuaJIT collection region is display/prediction ONLY.
  local predictedRegion = 0
  if pressureStartMiB and pressureHighMiB and
      pressureHighMiB > pressureStartMiB then
    local range = pressureHighMiB - pressureStartMiB
    predictedRegion = math.max(0, math.min(1,
      (heapMiB - pressureStartMiB) / range))
  end
  local riseMiB = math.max(0, heapMiB - (heapFloorMiB or heapMiB))
  local riseSignal = math.min(1, riseMiB / math.max(24, heapMiB * 0.10))
  local positiveGrowth = math.max(0, growthMiBps)
  local slopeSignal = math.min(1,
    positiveGrowth / math.max(1, heapMiB * 0.015))
  local demand = math.max(riseSignal * 0.85, slopeSignal * 0.75)
  lastPressure, lastDemand = predictedRegion, demand

  -- The Resolver's conservatively credited applied-rule savings and
  -- 8% reserved starting reference shape the INITIAL sliders only. There is deliberately no fixed budget
  -- throttle from the original pre-optimization capture. CET users may tune.
  -- Ten percent of manually selected intensity is continuous baseline.
  local effort = (intensity / 100) * (0.10 + 0.90 * demand)
  local stepKb = math.max(1, math.min(128, math.floor(1 + effort * 127)))
  sinceStep, lastAssistAge, lastStepKb = 0, 0, stepKb
  -- The step argument is incremental GC work, NOT milliseconds. os.clock is
  -- CPU time, not a guaranteed frame wall-time limit.
  local t0 = os.clock()
  local ok, cycle = pcall(collectgarbage, "step", stepKb)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  windowUsedMs = windowUsedMs + cpuMs
  lastStepCpuMs = cpuMs
  totalStepCpuMs = totalStepCpuMs + cpuMs
  maximumStepCpuMs = math.max(maximumStepCpuMs, cpuMs)
  if not ok then
    rejectedSteps = rejectedSteps + 1
    backoffSeconds = 30
    intensity = 0
    settingsDirty = true
    lastBlock = 4
    return
  end
  lastBlock = demand <= 0.05 and 6 or (demand < 0.50 and 7 or 8)
  stepCount = stepCount + 1
  if cycle == true then completedCycles = completedCycles + 1 end
  -- Emergency-only self-protection after an unexpectedly expensive step.
  -- This is NOT a continuous CPU probe or capture-derived throughput cap.
  if cpuMs > math.max(4, config.hitchToleranceMs * 0.25) then
    backoffSeconds = 5
    lastBlock = 9
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap, heapFloorMiB = nil, nil
  livePeakMiB = nil
  growthMiBps = 0
  learnCooldown, lastAssistAge = 0, 1e6
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC | G-CET adaptive collector")
  if shown then
    ImGui.Text("Capture: " .. config.capture)
    ImGui.Text(string.format("Heap %.1f MiB | predicted GC region %.0f%% | net growth %.2f MiB/s",
      heapMiB, lastPressure * 100, growthMiBps))
    ImGui.Text(string.format("CONTINUOUS assistance | live heap demand %.0f%%",
      lastDemand * 100))
    ImGui.Text(string.format("Capture GC forecast %s | LIVE reference %s | learned %d",
      config.pressureReferenceMiB and
        string.format("%.1f MiB", config.pressureReferenceMiB) or "not observed",
      liveReferenceMiB and string.format("%.1f MiB", liveReferenceMiB) or "awaiting natural GC",
      learnedEpisodes))
    if pressureStartMiB and pressureHighMiB then
      ImGui.Text(string.format("Approximate natural GC region (not a gate): %.1f to %.1f MiB",
        pressureStartMiB, pressureHighMiB))
    else
      ImGui.Text("Natural GC region: NOT OBSERVED (maintenance still active)")
    end
    ImGui.Text(string.format("Original frame %.1f / hitch %.1f ms (reference only)",
      config.baselineFrameMs, config.hitchToleranceMs))
    ImGui.Text("Resolver default: conservative estimate from APPLIED source rules")
    if config.estimatedOriginalCetMsPerSecond then
      ImGui.Text(string.format(
        "CET measured %.1f -> est. optimized %.1f ms/s | estimated saving %.1f ms/s",
        config.estimatedOriginalCetMsPerSecond,
        config.estimatedOptimizedCetMsPerSecond,
        config.estimatedFreedCetMsPerSecond))
      ImGui.Text(string.format(
        "Estimated: semantic %.2f / generic %.2f ms/s (heuristic)",
        config.estimatedSemanticSavingMsPerSecond,
        config.estimatedGenericSavingMsPerSecond))
      ImGui.Text(string.format(
        "Starting allowance estimate %.2f ms/s (8%% of saving; not enforced)",
        config.estimatedGgcStartingCpuMsPerSecond))
    else
      ImGui.Text("CET workload not measured: initial assistance OFF")
    end
    ImGui.Text(string.format("Resolver DEFAULT: intensity %d%% / interval %d ms",
      config.defaultIntensity, config.defaultFrequencyMs))
    local newIntensity = ImGui.SliderInt("Intensity (%)", intensity, 0, 100)
    local newFrequency = ImGui.SliderInt("Frequency (ms)", frequencyMs, 250, 5000)
    if type(newIntensity) == "number" and newIntensity ~= intensity then
      intensity = math.max(0, math.min(100, math.floor(newIntensity)))
      settingsDirty = true
      markSettings()
    end
    if type(newFrequency) == "number" and newFrequency ~= frequencyMs then
      frequencyMs = math.max(250, math.min(5000, math.floor(newFrequency)))
      settingsDirty = true
      markSettings()
    end
    ImGui.Text(string.format("APPLIED: intensity %d%% | interval %d ms", intensity, frequencyMs))
    ImGui.Text("Controller: " .. blockNames[lastBlock])
    ImGui.Text(string.format("Incremental steps %d / last %d KiB | completed %d | failed %d",
      stepCount, lastStepKb, completedCycles, rejectedSteps))
    ImGui.Text(string.format("CPU cost: last %.3f, max %.3f, total %.2f ms (provisional)",
      lastStepCpuMs, maximumStepCpuMs, totalStepCpuMs))
    ImGui.Text(string.format("Observed step CPU this second %.3f ms (diagnostic)",
      windowUsedMs))
    ImGui.Text("0%=off. All >0 settings are user-controlled (no capture cap).")
    ImGui.Text("Only invalid world, severe frames, or costly steps cause backoff.")
  end
  ImGui.End()
end)
