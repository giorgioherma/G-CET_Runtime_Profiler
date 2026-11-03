-- GGC_RUNTIME_V1_RESOLVER_OWNED
local config = require("threshold")
if type(config) ~= "table" or config.schema ~= 1
    or type(config.pressureStartMiB) ~= "number"
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
local lastStepCpuMs, maximumStepCpuMs = 0, 0
local stepCount, completedCycles, rejectedSteps = 0, 0, 0
local settingsDirty, firstMark = true, true
local activeWorld = false

local function markSettings()
  if type(CETProfilerMark) == "function" then
    pcall(CETProfilerMark, "GGC_V1_I" .. intensity .. "_F" .. frequencyMs)
  end
end

local function observe(dt)
  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    backoffSeconds = 30
    return
  end
  heapMiB = kib / 1024
  if previousHeap then
    local slope = (heapMiB - previousHeap) / dt
    if slope > -4096 and slope < 4096 then
      growthMiBps = growthMiBps * 0.70 + slope * 0.30
    end
  end
  previousHeap = heapMiB

  -- Once per observation, not per frame. No native player handle retained.
  activeWorld = false
  if type(Game) == "table" and type(Game.GetPlayer) == "function" then
    local playerOk, player = pcall(Game.GetPlayer)
    activeWorld = playerOk and player ~= nil
  end
end

local function tick(dt)
  if type(dt) ~= "number" or dt <= 0 or dt > 0.50 then
    -- A loading stall must not be converted into imaginary spare budget.
    backoffSeconds = 3
    return
  end
  local frame = dt * 1000 -- CET onUpdate delta is seconds.
  frameEmaMs = frameEmaMs * 0.90 + frame * 0.10
  if frame >= config.hitchToleranceMs * 0.85 then
    backoffSeconds = math.max(backoffSeconds, 2)
  end
  if backoffSeconds > 0 then backoffSeconds = math.max(0, backoffSeconds - dt) end
  age = age + dt
  sinceStep = sinceStep + dt
  windowAge = windowAge + dt
  if windowAge >= 1 then
    windowAge, windowUsedMs = 0, 0
  end
  if age >= 0.75 then
    local observedSeconds = age
    age = 0
    observe(observedSeconds)
    if settingsDirty or firstMark then
      markSettings()
      settingsDirty, firstMark = false, false
    end
  end
  -- Do not couple assistance frequency to heap observation frequency:
  -- a cached pressure sample is sufficient between cheap observations.
  if intensity <= 0 or not activeWorld or backoffSeconds > 0 then return end
  if sinceStep * 1000 < frequencyMs then return end

  -- Physical pressure takes priority over a speculative time-to-GC forecast.
  local pressureRange = config.pressureHighMiB - config.pressureStartMiB
  if pressureRange <= 0 then return end
  local pressure = (heapMiB - config.pressureStartMiB) / pressureRange
  if pressure <= 0 then return end
  if pressure > 1 then pressure = 1 end
  if growthMiBps <= 0 and pressure < 1 then return end

  -- Leave most of the user's measured margin available for heavier gameplay.
  local available = config.hitchToleranceMs - config.frameReserveMs - frameEmaMs
  if available <= 0 then return end
  if windowUsedMs >= config.maxAssistCpuMsPerSecond then return end

  local effort = (intensity / 100) * pressure
  local stepKb = math.max(1, math.floor(1 + effort * 31))
  sinceStep = 0
  local t0 = os.clock()
  local ok, cycle = pcall(collectgarbage, "step", stepKb)
  local cpuMs = math.max(0, (os.clock() - t0) * 1000)
  windowUsedMs = windowUsedMs + cpuMs
  lastStepCpuMs = cpuMs
  maximumStepCpuMs = math.max(maximumStepCpuMs, cpuMs)
  if not ok then
    rejectedSteps = rejectedSteps + 1
    backoffSeconds = 30
    intensity = 0
    settingsDirty = true
    return
  end
  stepCount = stepCount + 1
  if cycle == true then completedCycles = completedCycles + 1 end
  if cpuMs > math.min(available * 0.20, config.maxAssistCpuMsPerSecond) then
    -- os.clock gives CPU, not a hard wall-time bound. The native profiler
    -- must separately validate GC step duration and visible frame tails.
    backoffSeconds = 5
  end
end

registerForEvent("onUpdate", tick)
registerForEvent("onOverlayOpen", function() overlayOpen = true end)
registerForEvent("onOverlayClose", function() overlayOpen = false end)
registerForEvent("onShutdown", function()
  activeWorld = false
  previousHeap = nil
  growthMiBps = 0
end)

registerForEvent("onDraw", function()
  if not overlayOpen then return end
  local shown = ImGui.Begin("G-GC | G-CET adaptive collector")
  if shown then
    ImGui.Text("Capture: " .. config.capture)
    ImGui.Text(string.format("Heap %.1f MiB | Pressure starts %.1f MiB",
      heapMiB, config.pressureStartMiB))
    ImGui.Text(string.format("Baseline %.1f ms | Tolerance %.1f ms | Reserve %.1f ms",
      config.baselineFrameMs, config.hitchToleranceMs, config.frameReserveMs))
    local newIntensity = ImGui.SliderInt("Intensity (%)", intensity, 0, 100)
    local newFrequency = ImGui.SliderInt("Frequency (ms)", frequencyMs, 250, 5000)
    if type(newIntensity) == "number" and newIntensity ~= intensity then
      intensity = math.floor(newIntensity)
      settingsDirty = true
    end
    if type(newFrequency) == "number" and newFrequency ~= frequencyMs then
      frequencyMs = math.floor(newFrequency)
      settingsDirty = true
    end
    ImGui.Text(string.format("Steps %d | Cycles %d | Failed %d",
      stepCount, completedCycles, rejectedSteps))
    ImGui.Text(string.format("Last step CPU %.3f ms | Max %.3f ms | Growth %.2f MiB/s",
      lastStepCpuMs, maximumStepCpuMs, growthMiBps))
    if intensity == 0 then ImGui.Text("Assistance OFF (observe-only)") end
    if backoffSeconds > 0 then ImGui.Text("Headroom/cost backoff active") end
    ImGui.Text("Adjust live; next Resolver pass refreshes calibration.")
  end
  ImGui.End()
end)
