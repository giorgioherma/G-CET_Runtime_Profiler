-- GGC_RUNTIME_V1_RESOLVER_OWNED
-- G-GC source-owned controller: measured capture sets the ceiling,
-- live heap observations determine demand. No scheduled full GC.
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 1
    or type(config.pressureStartMiB) ~= "number"
    or type(config.pressureHighMiB) ~= "number"
    or type(config.hitchToleranceMs) ~= "number"
    or type(config.maxAssistCpuMsPerSecond) ~= "number" then
  print("[G-GC] Invalid Resolver calibration; assistance disabled.")
  return
end

local intensity = math.max(0, math.min(100, config.defaultIntensity or 0))
local frequencyMs = math.max(250, math.min(5000, config.defaultFrequencyMs or 1000))
local overlayOpen = false
local age, sinceStep, windowAge, windowUsedMs = 0, 0, 0, 0
local heapMiB, previousHeap, growthMiBps = 0, nil, 0
local frameEmaMs, backoffSeconds = config.baselineFrameMs, 0
local lastStepCpuMs, maximumStepCpuMs, totalStepCpuMs = 0, 0, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local settingsDirty, firstMark, heartbeatAge = true, true, 0
local activeWorld, lastStepKb, lastPressure = false, 0, 0
local lastBlock = 1
local blockNames = {
  "waiting for live heap sample", "assist disabled by user",
  "world/player unavailable", "frame backoff",
  "minimum step interval", "heap pressure below band",
  "heap not rising", "no spare frametime",
  "CPU assistance allowance exhausted", "assistance executed",
  "natural GC episode cooldown"
}

-- A captured slow-step heap is NOT necessarily a GC start threshold. It is
-- a rough prior only. Observe the current installation's natural heap peak
-- before a significant drop and adapt the pressure band to that peak.
local liveReferenceMiB = nil
local livePeakMiB = nil
local learnedEpisodes, learnCooldown, lastAssistAge = 0, 0, 1e6
local pressureStartMiB, pressureHighMiB =
  config.pressureStartMiB, config.pressureHighMiB

local function updatePressureBand(reference)
  local captureSlack = config.pressureHighMiB - config.pressureReferenceMiB
  local slack = math.max(24, math.min(reference * 0.12,
    math.max(reference * 0.07, captureSlack * 0.7)))
  pressureStartMiB = math.max(64, reference - slack)
  pressureHighMiB = reference + slack
end

local function markSettings()
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark,
      "GGC_V2_I" .. intensity .. "_F" .. frequencyMs ..
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
  if previousHeap then
    local slope = (heapMiB - previousHeap) / dt
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
    -- Coalesce consecutive sweep/drop samples: the first substantial drop
    -- in an episode learns one approximate natural peak, not many triggers.
    local drop = previousHeap - heapMiB
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
  local frame = dt * 1000
  frameEmaMs = frameEmaMs * 0.90 + frame * 0.10
  if frame >= config.hitchToleranceMs * 0.85 then
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
  -- Never push more collector work during the natural drop/settle episode.
  if learnCooldown > 0 then lastBlock = 11; return end
  if sinceStep * 1000 < frequencyMs then lastBlock = 5; return end

  -- CURRENT heap and net growth estimate the requested work. The reference
  -- is an empirical live pressure band when available, never an exact trigger.
  local range = pressureHighMiB - pressureStartMiB
  if range <= 0 then lastBlock = 6; return end
  local pressure = (heapMiB - pressureStartMiB) / range
  if pressure <= 0 then lastPressure = 0; lastBlock = 6; return end
  pressure = math.min(1, pressure)
  lastPressure = pressure
  if growthMiBps <= 0 and pressure < 1 then lastBlock = 7; return end

  -- Capture-derived cost allowance is a hard permission gate, not a demand
  -- signal. Leave most of the measured margin for sudden heavy gameplay.
  local available = config.hitchToleranceMs - config.frameReserveMs - frameEmaMs
  if available <= 0 then lastBlock = 8; return end
  if windowUsedMs >= config.maxAssistCpuMsPerSecond then
    lastBlock = 9
    return
  end

  local nominalHeadroom = config.hitchToleranceMs -
    config.frameReserveMs - config.baselineFrameMs
  local headroomFactor = math.min(1, available / math.max(0.5, nominalHeadroom))
  local effort = (intensity / 100) * pressure * headroomFactor
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
  lastBlock = 10
  stepCount = stepCount + 1
  if cycle == true then completedCycles = completedCycles + 1 end
  if cpuMs > math.min(available * 0.20, config.maxAssistCpuMsPerSecond) then
    backoffSeconds = 5
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap = nil
  livePeakMiB = nil
  growthMiBps = 0
  learnCooldown, lastAssistAge = 0, 1e6
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC | G-CET adaptive collector")
  if shown then
    ImGui.Text("Capture: " .. config.capture)
    ImGui.Text(string.format("Heap %.1f MiB | pressure %.0f%% | net growth %.2f MiB/s",
      heapMiB, lastPressure * 100, growthMiBps))
    ImGui.Text(string.format("Capture prior %.1f | LIVE reference %s | learned %d",
      config.pressureReferenceMiB,
      liveReferenceMiB and string.format("%.1f MiB", liveReferenceMiB) or "awaiting natural GC",
      learnedEpisodes))
    ImGui.Text(string.format("Live pressure band %.1f to %.1f MiB",
      pressureStartMiB, pressureHighMiB))
    ImGui.Text(string.format("Frame baseline %.1f / hitch %.1f / reserved %.1f ms",
      config.baselineFrameMs, config.hitchToleranceMs, config.frameReserveMs))
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
    ImGui.Text(string.format("Current second used %.3f / permitted %.3f CPU ms",
      windowUsedMs, config.maxAssistCpuMsPerSecond))
    ImGui.Text("100% means max allowed assistance, not forced full GC.")
  end
  ImGui.End()
end)
