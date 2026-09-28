local function say(message)
  print("[CETProfilerControls] " .. tostring(message))
end

local function invoke(label, fn, ...)
  if type(fn) ~= "function" then
    say(label .. " unavailable. Install the matching profiler ASI first.")
    return nil, false
  end

  local ok, result = pcall(fn, ...)
  if not ok then
    say(label .. " failed: " .. tostring(result))
    return nil, false
  end

  if result ~= nil then say(result) end
  return result, true
end

local function isRunning()
  if type(CETProfilerIsRunning) ~= "function" then return false end
  local ok, running = pcall(CETProfilerIsRunning)
  return ok and running == true
end

local scenarios = {
  WORLD = { label = "WORLD / IDLE", marker = "WORLD" },
  DRIVING = { label = "DRIVING", marker = "DRIVING" },
  COMBAT = { label = "COMBAT", marker = "COMBAT" }
}

local captureStartedAt = nil
local captureBaseSeconds = 0
local activeScenario = nil
local hudErrorReported = false

local function nowSeconds()
  if ImGui and type(ImGui.GetTime) == "function" then
    local ok, value = pcall(ImGui.GetTime)
    if ok and type(value) == "number" then return value end
  end
  return os.clock()
end

local function readCapturedSeconds()
  if type(CETProfilerStatus) ~= "function" then return 0 end
  local ok, status = pcall(CETProfilerStatus)
  if not ok or type(status) ~= "string" then return 0 end
  local value = status:match("captured=([%d%.]+)s")
  return tonumber(value) or 0
end

local function formatTimer(seconds)
  seconds = math.max(0, math.floor((seconds or 0) + 0.5))
  local hours = math.floor(seconds / 3600)
  local minutes = math.floor((seconds % 3600) / 60)
  local secs = seconds % 60

  if hours > 0 then
    return string.format("%02d:%02d:%02d", hours, minutes, secs)
  end
  return string.format("%02d:%02d", minutes, secs)
end

local function marker(label)
  local _, ok = invoke("CETProfilerMark", CETProfilerMark, label)
  return ok
end

local function finishActiveScenario()
  if not activeScenario then return true end

  local scenario = scenarios[activeScenario]
  if not scenario then
    activeScenario = nil
    return true
  end

  local ok = marker("SCENARIO_" .. scenario.marker .. "_END")
  if ok then
    say(scenario.label .. " section finished.")
    activeScenario = nil
  end
  return ok
end

local function toggleScenario(name)
  local scenario = scenarios[name]
  if not scenario then return end

  if not isRunning() then
    say(scenario.label .. " tag ignored: start the profiler capture first.")
    return
  end

  if activeScenario == name then
    finishActiveScenario()
    return
  end

  if activeScenario ~= nil then
    say("Finish the active " .. scenarios[activeScenario].label ..
        " section before starting " .. scenario.label .. ".")
    return
  end

  if marker("SCENARIO_" .. scenario.marker .. "_START") then
    activeScenario = name
    say(scenario.label .. " section started.")
  end
end

local function toggleCapture()
  if isRunning() then
    -- Keep scenario ranges structurally valid even if the user stops the
    -- capture while one of the three tags is still active.
    finishActiveScenario()

    local _, pausedOk = invoke("CETProfilerPause", CETProfilerPause)
    if not pausedOk then return end

    local _, dumpOk = invoke("CETProfilerDump", CETProfilerDump)
    if dumpOk then
      say("Capture stopped. CSV results exported automatically.")
    end

    captureStartedAt = nil
    captureBaseSeconds = 0
    activeScenario = nil
    return
  end

  local _, ok = invoke("CETProfilerStart", CETProfilerStart)
  if ok then
    captureBaseSeconds = 0
    captureStartedAt = nowSeconds()
    activeScenario = nil
    say("Fresh capture started.")
  end
end

local function imguiFlag(name)
  if not ImGuiWindowFlags then return 0 end
  local value = ImGuiWindowFlags[name]
  return type(value) == "number" and value or 0
end

local hudFlags =
  imguiFlag("NoTitleBar") +
  imguiFlag("NoResize") +
  imguiFlag("NoMove") +
  imguiFlag("NoScrollbar") +
  imguiFlag("NoCollapse") +
  imguiFlag("AlwaysAutoResize") +
  imguiFlag("NoInputs") +
  imguiFlag("NoNav")

local function drawCaptureHud()
  if not isRunning() then return end
  if not ImGui or type(ImGui.Begin) ~= "function" then return end

  if captureStartedAt == nil then
    captureBaseSeconds = readCapturedSeconds()
    captureStartedAt = nowSeconds()
  end

  local elapsed = captureBaseSeconds + math.max(0, nowSeconds() - captureStartedAt)
  local text = "G-CET CAPTURE  " .. formatTimer(elapsed)

  if activeScenario and scenarios[activeScenario] then
    text = text .. "   |   " .. scenarios[activeScenario].label
  end

  local ok, err = pcall(function()
    if type(ImGui.SetNextWindowPos) == "function" then
      local cond = (ImGuiCond and ImGuiCond.Always) or 0
      ImGui.SetNextWindowPos(20, 20, cond)
    end
    if type(ImGui.SetNextWindowBgAlpha) == "function" then
      ImGui.SetNextWindowBgAlpha(0.62)
    end

    ImGui.Begin("##GCETProfilerCaptureHUD", true, hudFlags)
    if type(ImGui.TextUnformatted) == "function" then
      ImGui.TextUnformatted(text)
    else
      ImGui.Text(text)
    end
    ImGui.End()
  end)

  if not ok and not hudErrorReported then
    hudErrorReported = true
    say("Capture HUD unavailable: " .. tostring(err))
  end
end

registerForEvent("onInit", function()
  if isRunning() then
    captureBaseSeconds = readCapturedSeconds()
    captureStartedAt = nowSeconds()
  end

  say("loaded. F11 capture remains START / STOP + AUTO EXPORT. " ..
      "Three optional scenario tags are available in CET bindings: " ..
      "WORLD / IDLE, DRIVING and COMBAT.")
end)

registerForEvent("onDraw", drawCaptureHud)

registerInput("CETProfiler_Toggle", "Profiler: START / STOP + AUTO EXPORT", function(isKeyDown)
  if isKeyDown then toggleCapture() end
end)

registerInput("CETProfiler_WorldTag", "Profiler tag: WORLD / IDLE start / finish", function(isKeyDown)
  if isKeyDown then toggleScenario("WORLD") end
end)

registerInput("CETProfiler_DrivingTag", "Profiler tag: DRIVING start / finish", function(isKeyDown)
  if isKeyDown then toggleScenario("DRIVING") end
end)

registerInput("CETProfiler_CombatTag", "Profiler tag: COMBAT start / finish", function(isKeyDown)
  if isKeyDown then toggleScenario("COMBAT") end
end)
