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

local function activeKnownConflict()
  -- PunkyCam / Punk yCam is intentionally treated as incompatible instead of
  -- weakening G-CET globally. Both its CET loader and native camera/input plugin
  -- are recognized so the normal capture key cannot start an unsafe session.
  if type(PunkyCamNative) ~= "nil" then
    return "PunkyCam / Punk yCam"
  end

  if type(GetMod) == "function" then
    local ok, mod = pcall(GetMod, "PunkyCam")
    if ok and mod ~= nil then
      return "PunkyCam / Punk yCam"
    end
  end

  return nil
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

  local conflict = activeKnownConflict()
  if conflict then
    say("Capture blocked: known profiler conflict detected (" .. conflict ..
        "). Disable it and restart Cyberpunk before profiling.")
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

local hudDrawConfirmed = false

local function drawCaptureHud()
  if not isRunning() then return end

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
    -- Use the same simple gameplay-ImGui pattern used by established CET HUD
    -- examples: draw every onDraw, position directly, and use Begin(name, flags).
    -- No dependency on the CET console/overlay being open.
    ImGui.SetNextWindowPos(20, 20)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7)
    ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, 10, 7)
    ImGui.PushStyleColor(ImGuiCol.WindowBg, 0xd0000000)
    ImGui.PushStyleColor(ImGuiCol.Border, 0x80ffffff)

    local flags =
      ImGuiWindowFlags.NoDecoration +
      ImGuiWindowFlags.AlwaysAutoResize +
      ImGuiWindowFlags.NoInputs +
      ImGuiWindowFlags.NoSavedSettings +
      ImGuiWindowFlags.NoFocusOnAppearing +
      ImGuiWindowFlags.NoBringToFrontOnFocus

    local shouldDraw = ImGui.Begin("G-CET Profiler Capture##GCETProfilerCaptureHUD", flags)
    if shouldDraw then
      ImGui.TextUnformatted(text)
    end
    ImGui.End()

    ImGui.PopStyleColor(2)
    ImGui.PopStyleVar(2)

    if not hudDrawConfirmed then
      hudDrawConfirmed = true
      say("Capture HUD draw active.")
    end
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


-- Passive whole-session Lua heap observer. collectgarbage("count") only reads
-- the current Lua heap; it never starts, stops, steps or tunes collection.
-- Sample twice per second, but reserve scarce native marker slots: record one
-- baseline checkpoint every 10 seconds and any >=8 MiB single-sample shrink.
-- Only 480 of the native profiler's 1000 markers may belong to this observer.
local gcSampleAge = 0
local gcHeartbeatAge = 10
local gcPreviousKiB = nil
local gcMarked = 0
local gcWasCapturing = false
local gcMarkersUnavailable = false
local GC_MAX_MARKERS = 480

-- Lightweight health snapshots of the four additive 0-Engine services.
-- Uses the *existing* 0.5-second heap observer, without registering another
-- onUpdate and without causing new Scheduler/StateSignals subscriptions.
local workloadAge, workloadMarks = 15, 0
local WORKLOAD_MAX_MARKERS = 160

local function observeWorkload()
  if workloadMarks >= WORKLOAD_MAX_MARKERS or
      type(CETProfilerMark) ~= "function" or
      type(GetMod) ~= "function" then return end
  local ok, engine = pcall(GetMod, "0-Engine")
  if not ok or type(engine) ~= "table" then return end

  local function status(name)
    local service = engine[name]
    if type(service) ~= "table" or type(service.GetInfo) ~= "function" then return nil end
    local yes, info = pcall(service.GetInfo)
    return yes and type(info) == "table" and info or nil
  end
  local w = status("WorkQueue")
  local p = status("PhasePlanner")
  local f = status("FrameListeners")
  local s = status("StateSignals")
  if not (w and p and f and s) then return end

  local function n(value)
    value = tonumber(value) or 0
    return math.max(0, math.floor(value))
  end
  local label = string.format(
    "WORKLOAD_V1_Q_%d_A_%d_D_%d_P_%d_H_%d_E_%d_F_%d_Z_%d_S_%d",
    n(w.queued), n(w.active), n(w.deferredFrames),
    n(p.pending), n(p.postponed),
    n(w.errors) + n(p.errors), n(f.active), n(f.paused),
    n(s.watchers))
  local marked, result = pcall(CETProfilerMark, label)
  if marked and not (type(result) == "string" and result:find("buffer full", 1, true)) then
    workloadMarks = workloadMarks + 1
  else
    workloadMarks = WORKLOAD_MAX_MARKERS
  end
end

local function observeLuaHeap(dt)
  gcSampleAge = gcSampleAge + math.max(0, tonumber(dt) or 0)
  if gcSampleAge < 0.5 then return end
  local elapsed = gcSampleAge
  gcSampleAge = 0

  local capturing = isRunning()
  if not capturing then
    gcWasCapturing = false
    gcPreviousKiB = nil
    gcHeartbeatAge = 10
    gcMarked = 0
    gcMarkersUnavailable = false
    workloadAge, workloadMarks = 15, 0
    return
  end

  workloadAge = workloadAge + elapsed
  if workloadAge >= 15 then
    workloadAge = 0
    observeWorkload()
  end

  if not gcWasCapturing then
    gcWasCapturing = true
    gcHeartbeatAge = 10
    gcPreviousKiB = nil
    gcMarked = 0
    gcMarkersUnavailable = false
  end

  if gcMarkersUnavailable or type(collectgarbage) ~= "function"
      or type(CETProfilerMark) ~= "function" then return end

  local ok, kib = pcall(collectgarbage, "count")
  if not ok or type(kib) ~= "number" or kib ~= kib or kib < 0 then
    gcMarkersUnavailable = true
    return
  end

  local rounded = math.floor(kib + 0.5)
  local drop = gcPreviousKiB and math.max(0, math.floor(gcPreviousKiB - kib + 0.5)) or 0
  gcPreviousKiB = kib
  gcHeartbeatAge = gcHeartbeatAge + elapsed
  if gcMarked >= GC_MAX_MARKERS then return end

  local label = nil
  if drop >= 8192 then
    label = "GC_HEAP_V1_KIB_" .. rounded .. "_DROP_" .. drop
  elseif gcHeartbeatAge >= 10 then
    label = "GC_HEAP_V1_KIB_" .. rounded .. "_BASE"
  end
  if not label then return end

  gcHeartbeatAge = 0
  local marked, result = pcall(CETProfilerMark, label)
  if not marked or (type(result) == "string" and result:find("buffer full", 1, true)) then
    gcMarkersUnavailable = true
    return
  end
  gcMarked = gcMarked + 1
end

registerForEvent("onUpdate", observeLuaHeap)

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
