param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "G-CET resolver executable not found: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-sixth-stack-residual-semantic-composition'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-070707_SIXTH_STACK_RESIDUAL'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try { $gzip.CopyTo($output); [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray()) }
finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source) {
    $path = Join-Path (Join-Path $mods $Mod) $Relative
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    $Source | Set-Content -LiteralPath $path -Encoding utf8
}

Write-ModFile 'GoodFeelings' 'init.lua' @'
local Event = {
    Observe = Observe,
    RegisterUpdate = function(fn) registerForEvent("onUpdate", fn) end,
    RegisterDraw = function(fn) registerForEvent("onDraw", fn) end,
}
local modulesLoaded = true
local Cron = { Update = function() end }
local SelfFeature = { NoClip = { HandleMouseLook = function(action) end } }
local Utils = {
    Weapon = { HandleInputAction = function(action) end },
    StatModifiers = { UpdateSessionWatcher = function() end }
}
local Handler = { Update = function() end }
local State = { menuOpen = false }
local UI = {
    Notification = { Render = function() end },
    Overlay = { Render = function() end }
}
local WelcomeWindow = { Render = function() end }
local MainMenu = { Initialize = function() end }
registerForEvent("onInit", function()
    Event.Observe("PlayerPuppet", "OnAction", function(_, action)
        if modulesLoaded then
            SelfFeature.NoClip.HandleMouseLook(action)
            if Utils then
                Utils.Weapon.HandleInputAction(action)
            end
        end
    end)
end)
Event.RegisterUpdate(function(dt)
    Cron.Update(dt)
    if not modulesLoaded then return end
    Utils.StatModifiers.UpdateSessionWatcher()
end)
Event.RegisterDraw(function()
    UI.Notification.Render()
    WelcomeWindow.Render()
    UI.Overlay.Render()
    if not modulesLoaded then return end
    MainMenu.Initialize()
    Handler.Update()
    if State.menuOpen then
        MainMenu.Initialize()
    end
end)
'@

Write-ModFile 'GoodFeelings' 'Controls/Handler.lua' @'
local State = {
    menuOpen = false,
    bindingKey = false,
    typingEnabled = false,
    InitializeTracking = function() end,
    ToggleMenu = function() State.menuOpen = not State.menuOpen end,
}
local Bindings = { IsActionDown = function() return false end }
local Restrictions = { Update = function() end }
local Cursor = { Update = function() end }
local BindManager = { Initialize = function() end, Update = function() end }
local Logger = { Log = function() end }
local Handler = { scrollDelayBase = 200 }
local holdStart = { up=0,down=0,left=0,right=0 }
local lastTick = { toggle=0 }
local initialized = false
function Handler.Update()
    local now = os.clock() * 1000
    State.upPressed, State.downPressed = false, false
    State.leftPressed, State.rightPressed = false, false
    State.selectPressed, State.backPressed = false, false
    State.miscPressed = false

    Restrictions.Update()
    Cursor.Update()

    if(not initialized ) then
        State.InitializeTracking()
        BindManager.Initialize()
        initialized = true
    end

    BindManager.Update()

    if State.bindingKey then
        return
    end

    if Bindings.IsActionDown("TOGGLE") and now - lastTick.toggle > Handler.scrollDelayBase then
        State.ToggleMenu()
        Logger.Log("Controls: Menu toggled " .. tostring(State.menuOpen))
        lastTick.toggle = now
    end

    if not State.menuOpen then
        holdStart.up, holdStart.down, holdStart.left, holdStart.right = 0, 0, 0, 0
        return
    end

    if State.typingEnabled then return end
end
return Handler
'@

Write-ModFile 'GoodFeelings' 'UI/Elements/Overlay.lua' @'
local State = {
  showOverlay={value=true}, overlayShowWatermark={value=true},
  overlayShowGameVersion={value=true}, overlayShowTop={value=true}
}
local Overlay = {}
function Overlay.Render()
    if not State.showOverlay or not State.showOverlay.value then return end
    local screenW, screenH = GetDisplayResolution()
    local scale = 1.0
    local baseFontSize = ImGui.GetFontSize() or 18
    local combinedText = "GoodFeelings | 12:34:56"
    local textW, textH = ImGui.CalcTextSize(combinedText)
    if State.overlayShowWatermark.value then
        local urlW = ImGui.CalcTextSize(" https://goodfeelings.cc") * scale
    end
    if State.overlayShowGameVersion.value then
        local gameVerW = ImGui.CalcTextSize("Game: v2.31") * scale
    end
end
return Overlay
'@

Write-ModFile 'AirBackFlip' 'init.lua' @'
local GAME_ACTIONS = {
    AirBackflip_Backflip=function() end,
    AirBackflip_Frontflip=function() end,
    AirBackflip_SideflipLeft=function() end,
    AirBackflip_SideflipRight=function() end,
    AirBackflip_SwingOver=function() end
}
local moveY = 0
registerForEvent("onInit", function()
    Observe("PlayerPuppet", "OnAction", function(_, action)
        pcall(function()
            local name = Game.NameToString(ListenerAction.GetName(action))
            if name == "MoveY" then
                moveY = ListenerAction.GetValue(action)
                return
            end
            local fn = GAME_ACTIONS[name]
            if not fn then return end
            if ListenerAction.IsButtonJustPressed(action) then
                fn(true)
            elseif ListenerAction.IsButtonJustReleased(action) then
                fn(false)
            end
        end)
    end)
end)
'@

Write-ModFile 'AutoDropWeaponOnPickupEquip' 'init.lua' @'
local cfg = { swapWindow=0.90, dropDelay=0.10, debugPrint=false }
local t, pendingUntil, dropAt = 0.0, 0.0, 0.0
local pending, oldItemID, oldKey = false, nil, nil
local triggerActions = { Reload=true, Interaction=true, Interact=true, Use=true, ContextualAction=true, Loot=true, PickUp=true, Pickup=true, Take=true }
local function log(msg) print("[ADWOP] "..msg) end
local function getActionName(action) return Game.NameToString(action:GetName()) end
local function getActiveWeaponItemID(player) return player:GetActiveWeapon():GetItemID() end
local function itemKey(itemID) return tostring(itemID) end
local function isUnarmedItemID(itemID) return false end
local captureArmed = false
local captureStart = 0.0

registerInput("ADWOP_CaptureNextAction", "capture", function(isDown)
  if not isDown then return end
  captureArmed = true
  captureStart = t + 0.15
  log("Capture armed.")
end)

registerInput("ADWOP_ToggleDebug", "debug", function(isDown)
  if not isDown then return end
  cfg.debugPrint = not cfg.debugPrint
  log("debugPrint = " .. tostring(cfg.debugPrint))
end)

registerForEvent("onInit", function()
  Observe("PlayerPuppet", "OnAction", function(_, action)
    if not action then return end
    local aType = action:GetType()
    if aType ~= gameinputActionType.BUTTON_PRESSED then return end
    local name = getActionName(action)
    if captureArmed and t >= captureStart then
      captureArmed = false
      triggerActions[name] = true
      log("CAPTURED action name: " .. tostring(name) .. "  (added as trigger)")
    end
    if cfg.debugPrint then print(string.format("[ADWOP] Action=%s", tostring(name))) end
    if not triggerActions[name] then return end
    local player = GetPlayer()
    if not player then return end
    local curID = getActiveWeaponItemID(player)
    if not curID or isUnarmedItemID(curID) then return end
    oldItemID = curID
    oldKey = itemKey(curID)
    pending = true
    pendingUntil = t + cfg.swapWindow
    dropAt = 0.0
  end)

  log("Loaded. (Optional) bind ADWOP inputs in CET -> Bindings -> Inputs.")
end)

registerForEvent("onUpdate", function(dt)
  t = t + (dt or 0)
  if not pending then return end
  if t > pendingUntil then
    pending = false
    return
  end
  local player = GetPlayer()
  if not player then return end
  local newID = getActiveWeaponItemID(player)
  local newKey = itemKey(newID)
  if not oldKey or not newKey or newKey == oldKey then return end
  if dropAt == 0.0 then dropAt = t + cfg.dropDelay; return end
  if t < dropAt then return end
  pending = false
end)
'@

Write-ModFile 'Drone Companions (Revamp)' 'DroneLogic/Drone AI - Mech.lua' @'
local function __gcetGetPlayer() return Game.GetPlayer() end
local DCO = {}
function DCO:new()
    local mechcount, mechcount2, octantcount, bombuscount = 0, 0, 0, 0
    Override('TweakAIActionAbstract', 'Update', function(self, context, wrappedMethod)
        local owner = ScriptExecutionContext.GetOwner(context)
        if owner and owner.GetRecordID
           and TweakDBInterface.GetCharacterRecord(owner:GetRecordID()):TagsContains(CName.new("Robot")) then
            if StatusEffectSystem.ObjectHasStatusEffectWithTag(__gcetGetPlayer(), CName.new("FistFight")) then
                return wrappedMethod(context)
            end
            local recordID = (self.actionRecord and self.actionRecord:GetID()) or TweakDBID.new("")
            if recordID == TweakDBID.new("MinotaurMech.AimAttackHMG") then
                mechcount = mechcount + 1
                if mechcount > 50 then mechcount = 0; return AIbehaviorUpdateOutcome.SUCCESS end
                local ret = wrappedMethod(context); if ret == AIbehaviorUpdateOutcome.SUCCESS then mechcount = 0 end; return ret
            elseif recordID == TweakDBID.new("MinotaurMech.RotateToTargetNoLimit") then
                mechcount2 = mechcount2 + 1
                if mechcount2 > 20 then mechcount2 = 0; return AIbehaviorUpdateOutcome.SUCCESS end
                local ret = wrappedMethod(context); if ret == AIbehaviorUpdateOutcome.SUCCESS then mechcount2 = 0 end; return ret
            elseif recordID == TweakDBID.new("DroneOctantActions.ShootDefault") then
                octantcount = octantcount + 1
                if octantcount > 50 then octantcount = 0; return AIbehaviorUpdateOutcome.SUCCESS end
                local ret = wrappedMethod(context); if ret == AIbehaviorUpdateOutcome.SUCCESS then octantcount = 0 end; return ret
            elseif recordID == TweakDBID.new("DroneBombusActions.FollowTargetFast") then
                bombuscount = bombuscount + 1
                if bombuscount > 30 then bombuscount = 0; return AIbehaviorUpdateOutcome.SUCCESS end
                local ret = wrappedMethod(context); if ret == AIbehaviorUpdateOutcome.SUCCESS then bombuscount = 0 end; return ret
            end
        end
        return wrappedMethod(context)
    end)
end
return DCO:new()
'@

Write-ModFile 'GhostVoidSystem' 'init.lua' @'
local gvs = {
 corruptionGlitchTimer=0, corruptionGlitchRemaining=0, corruptionGlitchIndex=0,
 corruptionDrainTimer=0, corruptionDrainQueued=false, corruptionDrainLastAmount=0,
 phasePulseRemaining=0, ghostStepCooldownRemaining=0, ghostEchoActive=false,
 ghostEchoRemaining=0, voidEnergyRegenPerSecond=1, stabilityRegenPerSecond=1
}
local function getState()
  return Game.GetScriptableSystemsContainer():Get("GVS.GhostVoid.GVSStateSystem")
end
local function getLiveCombatState() return false end
local function getVoidGlitchLevel(v) return 0 end
local function getVoidHealthDrainPercent(v) return 0 end
local function addVoidEnergy(amount)
  local state = getState()
  if state then state:AddVoidEnergy(amount) end
end
local function addStability(amount)
  local state = getState()
  if state then state:AddStability(amount) end
end
registerForEvent("onUpdate", function(deltaTime)
  local corruptionState = getState()
  local glitchLevel = 0
  if corruptionState then glitchLevel = getVoidGlitchLevel(corruptionState:GetStability()) end
  if glitchLevel > 0 then gvs.corruptionGlitchTimer = gvs.corruptionGlitchTimer + deltaTime end

  local drainState = getState()
  local drainPercent = 0.0
  if drainState then drainPercent = getVoidHealthDrainPercent(drainState:GetStability()) end
  if drainPercent > 0 then gvs.corruptionDrainTimer = gvs.corruptionDrainTimer + deltaTime end

  local liveCombat = getLiveCombatState()
  if liveCombat == false then
    addVoidEnergy(gvs.voidEnergyRegenPerSecond * deltaTime)

  local stabilityState = getState()
  if stabilityState then
    local currentStability = stabilityState:GetStability()
    if currentStability > 0.0 and currentStability < 100.0 then
      addStability(
        gvs.stabilityRegenPerSecond * deltaTime
      )
    end
  end
end
end)
'@

Write-ModFile 'tunnel_rescue' 'init.lua' @'
local Timer={draw=function() end}
local Swimming={frame=function() end,clear=function() end}
local Life={frame=function() end,clear=function() end}
local Street={frame=function() end,clear=function() end}
local Rescue={surface=function() return false end,journal=function() end,weather=function() end,outside=function() end,dispose=function() end}
local JournalOffer={update=function() end}
local H={phase='outside',tick=0,session=true,overlay=false,error=nil,blocked=nil,hooks={generation=1,inputReady=true,TickPresentation=function() end,NextInput=function() end,NextInteract=function() end,NextChoice=function() end,NextTorch=function() end,QuietTunnel=function() end}}
local L={}
local function fact(k)return 0 end
local function set(k,v)end
local function active()return fact('visit')==1 end
local function valid(x)return x~=nil end
local function status()end
local function leave(reason)H.phase='outside'end
local function update(dt)
 H.tick=H.tick+dt
 if H.tick<.15 then return end
 dt=math.min(H.tick,.5);H.tick=0
 local can=true
 H.hooks:TickPresentation(dt,can and not H.overlay)
 if H.phase=='outside'then Rescue.outside(dt,H,L,can,nil)end
 JournalOffer.update(dt,H,can,fact,set,Rescue.journal)
 if H.phase=='outside'and H.questArmed and Rescue.surface(H,L,can,nil,nil)then end
end
registerForEvent('onUpdate',function(dt)
 local ok,e=pcall(update,dt)
 if ok and not H.lifeError then
  local lifeOK,lifeError=pcall(Life.frame,dt,H,L)
  if not lifeOK then H.lifeError=tostring(lifeError);pcall(Life.clear,H.hooks)end
 end
 if ok and valid(H.hooks)then ok,e=pcall(Swimming.frame,dt,H.hooks,H.session and H.phase=='inside'and H.blocked==nil and not H.overlay)end
 if ok and not H.streetError then
  local streetOK,streetError=pcall(Street.frame,dt,H,L)
  if not streetOK then H.streetError=tostring(streetError);pcall(Street.clear)end
 end
 if valid(H.hooks)then H.hooks:QuietTunnel(ok and H.session and not H.error and not H.overlay and H.blocked~='Close the game menu'and (H.phase=='arriving'or H.phase=='inside'))end
 if not ok then
  H.error=tostring(e);H.auto=false;H.manual=false
  if H.session and H.phase~='outside'then pcall(leave,'Tunnel interrupted. Returning to the saved departure.')end
  pcall(status)
 end
end)
registerForEvent('onDraw',function()if H.session and not H.overlay and not H.error then Timer.draw()end end)
'@

Write-ModFile 'Straight Edged Controls' 'init.lua' @'
local UIBlocking = require('modules/ui_blocking')
local Lean = require('modules/lean')
local Inspection = require('modules/inspection')
local SettingsPoll = require('modules/settings_poll')
local ScrollWalk = { tick=function() end, reset=function() end }
local ToggleADS = { reset=function() end }
local CycleGrenades = { reset=function() end }
local Attachments = require('modules/attachments')
registerForEvent('onInit', function()
    Observe('PlayerPuppet', 'OnGameAttached', function()
        Lean.onSessionReset()
        SettingsPoll.invalidateZoom()
        ScrollWalk.reset()
        ToggleADS.reset()
        CycleGrenades.reset()
        Attachments.reset()
    end)
end)
registerForEvent('onUpdate', function(deltaTime)
    Lean.update(deltaTime)
    Lean.pollInput()
    Inspection.update(deltaTime)
    Inspection.pollInput()
    ScrollWalk.tick()
    SettingsPoll.poll()
    Attachments.update(deltaTime)
end)
'@

Write-ModFile 'Straight Edged Controls' 'modules/ui_blocking.lua' @'
local UIBlocking = {}
local shardReading = false
local codexPopupOpen = false
local hooksReady = false
local function isInMenuFlag() return false end
local function isPhoneActive() return false end
local function isDeviceUIActive() return false end
local function isScannerActive() return false end
local function isPhotoModeActive() return false end
function UIBlocking.isBlocked()
    if shardReading or codexPopupOpen then
        return true
    end
    if isInMenuFlag() then
        return true
    end
    if isPhoneActive() then
        return true
    end
    if isDeviceUIActive() then
        return true
    end
    if isScannerActive() then
        return true
    end
    if isPhotoModeActive() then
        return true
    end
    return false
end
function UIBlocking.init() hooksReady = true end
return UIBlocking
'@


Write-ModFile 'Straight Edged Controls' 'modules/lean.lua' @'
local UIBlocking = require('modules/ui_blocking')
local Lean = {}
local state = { currentDirection='none', leftHeld=false, rightHeld=false }
local lastSeq = -1
local wasInMenu = false
local function isUiBlocking() return UIBlocking.isBlocked() end
function Lean.reset()
    state.currentDirection='none'
    state.leftHeld=false
    state.rightHeld=false
end
function Lean.update(deltaTime)
    if state.currentDirection ~= 'none' then return end
end
function Lean.onSessionReset()
    Lean.reset()
    lastSeq = -1
    wasInMenu = false
end
function Lean.onKeyEvent(side, pressed, mode, switchSidesInstantly)
    state.currentDirection = side or 'none'
end
function Lean.pollInput()
    local inMenu = isUiBlocking()
    if wasInMenu and not inMenu then
        state.leftHeld = false
        state.rightHeld = false
        local okSeq, inputStateExit = pcall(function()
            local container = Game.GetScriptableSystemsContainer()
            if not container then return nil end
            return container:Get(CName.new('StraightEdgedControls.SECInputState'))
        end)
        if okSeq and inputStateExit then
            lastSeq = inputStateExit.seq
        end
    end
    wasInMenu = inMenu

    if inMenu then
        local ok, inputState = pcall(function()
            local container = Game.GetScriptableSystemsContainer()
            if not container then return nil end
            return container:Get(CName.new('StraightEdgedControls.SECInputState'))
        end)
        if ok and inputState then
            lastSeq = inputState.seq
        end
        return
    end

    local ok, inputState = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new('StraightEdgedControls.SECInputState'))
    end)

    if not ok or not inputState then
        return
    end

    local seq = inputState.seq
    if seq == lastSeq then
        return
    end
    lastSeq = seq

    Lean.onKeyEvent(inputState.side, inputState.pressed, inputState.mode, inputState.switchSidesInstantly)
end
return Lean
'@

Write-ModFile 'Straight Edged Controls' 'modules/inspection.lua' @'
local UIBlocking = require('modules/ui_blocking')
local Inspection = {}
local state = {
    keyDown=false,
    phase='idle',
    holdTime=0.0,
    lastInspectSeq=0,
}
local function getSettings()
    local ok, settings = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new('StraightEdgedControls.SECSettings'))
    end)
    if ok then return settings end
    return nil
end
function Inspection.onKey(pressed, released)
    if pressed then state.keyDown=true end
    if released then state.keyDown=false end
end
function Inspection.update(deltaTime)
    if not state.keyDown then return end
    state.holdTime = state.holdTime + deltaTime
end
function Inspection.pollInput()
    local ok, inputState = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new('StraightEdgedControls.SECInputState'))
    end)

    if not ok or not inputState then return end

    local seq = inputState.inspectSeq
    if seq == state.lastInspectSeq then return end
    state.lastInspectSeq = seq

    Inspection.onKey(inputState.inspectPressed, inputState.inspectReleased)
end
return Inspection
'@

Write-ModFile 'Straight Edged Controls' 'modules/settings_poll.lua' @'
local FasterAiming = { tick=function() end, invalidate=function() end }
local ScrollWalk = { tick=function() end, reset=function() end, setEnabled=function() end }
local Lean = { reset=function() end, setDisableAutoCover=function() end, setAdsOnly=function() end, setFasterLeaning=function() end }
local Settings = {}
local lastMasterEnabled = nil
local lastWeaponSwayDisabled = nil
local lastFasterAiming = nil
local lastToggleADS = nil
local lastWheelMode = nil
local lastZoomDisabled = nil
local zoomReady = false
local function getSettings()
    local ok, settings = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new('StraightEdgedControls.SECSettings'))
    end)
    if ok then return settings end
    return nil
end
function Settings.invalidateZoom()
    zoomReady = false
    lastZoomDisabled = nil
    FasterAiming.invalidate()
    lastFasterAiming = nil
    ScrollWalk.reset()
    lastWheelMode = nil
end
function Settings.poll()
    local settings = getSettings()
    if not settings then return end
    local masterEnabled = settings.masterEnabled
    local fasterAiming = settings.fasterAiming
    if masterEnabled ~= lastMasterEnabled then
        lastMasterEnabled = masterEnabled
    end
    if masterEnabled then
        FasterAiming.tick(fasterAiming and true or false)
        lastFasterAiming = fasterAiming
        ScrollWalk.tick()
    end
end
return Settings
'@

Write-ModFile 'Straight Edged Controls' 'modules/attachments.lua' @'
local UIBlocking = require('modules/ui_blocking')
local Attachments = {}
local state = {
    lastSuppressorSeq = 0,
    lastSightSeq = 0,
    pending = nil,
    holdArmed = { suppressor = false, sight = false },
    holdTime = { suppressor = 0, sight = 0 },
    holdFired = { suppressor = false, sight = false },
}
local function getSettings()
    local ok, settings = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new("StraightEdgedControls.SECSettings"))
    end)
    if ok then return settings end
    return nil
end
local function featureAllowed()
    local settings = getSettings()
    if not settings then return false end
    if not settings.masterEnabled then return false end
    if UIBlocking.isBlocked() then return false end
    return true
end
local function getAttachmentsBridge()
    local ok, bridge = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new("StraightEdgedControls.SECAttachments"))
    end)
    if ok then return bridge end
    return nil
end
local function abortPending() state.pending=nil end
local function startSwap(kind) state.pending={kind=kind,timer=1} end
function Attachments.update(deltaTime)
    if not featureAllowed() then
        abortPending()
        return
    end

    local bridge = getAttachmentsBridge()
    if bridge then
        if bridge.suppressorSeq ~= state.lastSuppressorSeq then
            state.lastSuppressorSeq = bridge.suppressorSeq
            if bridge.suppressorPressed then startSwap("suppressor") end
        end

        if bridge.sightSeq ~= state.lastSightSeq then
            state.lastSightSeq = bridge.sightSeq
            if bridge.sightPressed then startSwap("sight") end
        end

        for _, kind in ipairs({ "suppressor", "sight" }) do
            if state.holdArmed[kind] and not state.holdFired[kind] then
                state.holdTime[kind] = state.holdTime[kind] + deltaTime
            end
        end
    end

    if not state.pending then return end
    state.pending.timer = state.pending.timer - deltaTime
end
function Attachments.reset()
    abortPending()
    state.lastSuppressorSeq = 0
    state.lastSightSeq = 0
end
return Attachments
'@

Write-ModFile 'ImmersiveHeadInertia' 'init.lua' @'
local Inertia = { onAction=function(name,value) return name == "CameraMouseX" or name == "CameraMouseY" end }
registerForEvent("onInit", function()
    Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
        local name = action:GetName()
        if name then
            if Inertia.onAction(name.value, action:GetValue()) then
                consumer:ConsumeSingleAction()
            end
        end
    end)
end)
'@

function Find-Line([string]$Path,[string]$Pattern) {
    $lines=@(Get-Content -LiteralPath $Path)
    for($i=0;$i -lt $lines.Count;$i++){ if($lines[$i] -match $Pattern){ return $i+1 } }
    throw "Pattern not found in $Path : $Pattern"
}
function CallbackRow([int]$Id,[string]$Owner,[string]$Kind,[string]$Target,[string]$File,[int]$Line,[double]$Ms,[double]$Calls) {
    @{ registrationId=$Id; owner=$Owner; infrastructure=$false; kind=$Kind; target=$Target
       source=@{file=$File;lineStart=$Line;lineEnd=$Line+8}
       callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms; globalWorkSharePct=5
       familyWorkSharePct=40; avgExclusiveUs=100; maxExclusiveMs=2; spikeCount=0; maxSpikeExclusiveMs=0 }
}

$goodLine=Find-Line (Join-Path $mods 'GoodFeelings\init.lua') 'Event\.Observe\("PlayerPuppet", "OnAction"'
$goodDrawLine=Find-Line (Join-Path $mods 'GoodFeelings\init.lua') 'Event\.RegisterDraw\(function'
$airLine=Find-Line (Join-Path $mods 'AirBackFlip\init.lua') 'Observe\("PlayerPuppet", "OnAction"'
$autoLine=Find-Line (Join-Path $mods 'AutoDropWeaponOnPickupEquip\init.lua') 'Observe\("PlayerPuppet", "OnAction"'
$droneLine=Find-Line (Join-Path $mods 'Drone Companions (Revamp)\DroneLogic\Drone AI - Mech.lua') "Override\('TweakAIActionAbstract', 'Update'"
$ghostLine=Find-Line (Join-Path $mods 'GhostVoidSystem\init.lua') 'registerForEvent\("onUpdate"'
$tunnelLine=Find-Line (Join-Path $mods 'tunnel_rescue\init.lua') "registerForEvent\('onUpdate'"
$tunnelDrawLine=Find-Line (Join-Path $mods 'tunnel_rescue\init.lua') "registerForEvent\('onDraw'"
$straightLine=Find-Line (Join-Path $mods 'Straight Edged Controls\init.lua') "registerForEvent\('onUpdate'"
$inertiaLine=Find-Line (Join-Path $mods 'ImmersiveHeadInertia\init.lua') 'Observe\("PlayerPuppet", "OnAction"'

$handoff=@{
 schemaVersion='1.8'
 callbacks=@(
   (CallbackRow 1 'GoodFeelings' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $goodLine 30.940503 1244),
   (CallbackRow 8 'GoodFeelings' 'event' 'onDraw' 'init.lua' $goodDrawLine 86.449583 60),
   (CallbackRow 2 'AirBackFlip' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $airLine 12.077188 1244),
   (CallbackRow 3 'AutoDropWeaponOnPickupEquip' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $autoLine 9.888169 1244),
   (CallbackRow 4 'Drone Companions (Revamp)' 'Override' 'TweakAIActionAbstract::Update' 'DroneLogic/Drone AI - Mech.lua' $droneLine 8.51844 117),
   (CallbackRow 5 'GhostVoidSystem' 'event' 'onUpdate' 'init.lua' $ghostLine 3.452751 52),
   (CallbackRow 6 'Straight Edged Controls' 'event' 'onUpdate' 'init.lua' $straightLine 19.950931 52),
   (CallbackRow 9 'tunnel_rescue' 'event' 'onUpdate' 'init.lua' $tunnelLine 7.170041 60),
   (CallbackRow 10 'tunnel_rescue' 'event' 'onDraw' 'init.lua' $tunnelDrawLine 3.100000 60),
   (CallbackRow 7 'ImmersiveHeadInertia' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $inertiaLine 9.873854 1244)
 )
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Residual semantic pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$rules=@(
 'good-feelings','good-feelings-hard-draw','air-backflip','auto-drop-weapon-on-pickup-equip',
 'drone-companions-revamp','ghost-void-system','straight-edged-controls-input-dormancy','tunnel-rescue',
 'immersive-head-inertia'
)
foreach($rule in $rules) {
    $matches=@()
    foreach($family in @($resolver.callbackFamilies)) {
        $matches += @($family.topConsumers | Where-Object { $_.semantic.RuleId -eq $rule })
    }
    if($matches.Count -lt 1 -or @($matches | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
        throw "Residual semantic source proof failed: $rule"
    }
}

$manifest=Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
foreach($rule in $rules) {
    if(@($manifest.transforms | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule }).Count -ne 1) {
        $semanticSkips=@($manifest.skipped | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule })
        $skipText=if($semanticSkips.Count -gt 0){($semanticSkips|ConvertTo-Json -Depth 10 -Compress)}else{'<no semantic skip recorded>'}
        throw "Residual semantic transform missing/duplicated: $rule; $skipText"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$Name) {
        $e=$zip.GetEntry($Name); if($null -eq $e){throw "ZIP entry missing: $Name"}
        $r=[System.IO.StreamReader]::new($e.Open()); try{return $r.ReadToEnd()} finally{$r.Dispose()}
    }
    $base='bin/x64/plugins/cyber_engine_tweaks/mods/'

    $good=Read-ZipText ($base+'GoodFeelings/init.lua')
    if($good -notmatch 'G-CET semantic:good-feelings' -or
       $good -notmatch 'G-CET semantic:good-feelings-hard-draw' -or
       $good -notmatch 'gcetGoodFeelingsOnAction' -or
       $good -notmatch 'CameraMouseX' -or $good -notmatch 'RangedAttack' -or
       $good -notmatch 'decodeType = false' -or
       $good -notmatch 'if not modulesLoaded or not State\.menuOpen then return end') {
        throw 'GoodFeelings exact routing + hard draw dormancy is incomplete.'
    }
    if(([regex]::Matches($good,'Handler\.Update\(\)')).Count -ne 1 -or
       $good -notmatch '(?s)Event\.RegisterUpdate\(function\(dt\).*?Handler\.Update\(\)' -or
       $good -notmatch '(?s)Event\.RegisterDraw\(function\(\)\s+if not modulesLoaded or not State\.menuOpen then return end') {
        throw 'GoodFeelings F4 wake was not moved cleanly from onDraw to onUpdate.'
    }

    $air=Read-ZipText ($base+'AirBackFlip/init.lua')
    if($air -notmatch 'G-CET semantic:air-backflip' -or $air -notmatch 'gcetAirBackFlipOnAction' -or
       $air -notmatch 'AirBackflip_SwingOver' -or $air -notmatch 'MoveY' -or $air -notmatch 'SubscribeAction') {
        throw 'AirBackFlip exact routing is incomplete.'
    }

    $auto=Read-ZipText ($base+'AutoDropWeaponOnPickupEquip/init.lua')
    if($auto -notmatch 'G-CET semantic:auto-drop-weapon-dynamic-routing' -or
       $auto -notmatch '__gcetAutoDropSubscribeExact' -or
       $auto -notmatch '__gcetAutoDropRefreshWildcard' -or
       $auto -notmatch 'actions = "\*"' -or
       $auto -notmatch 'decodeType = false' -or
       $auto -notmatch 'if not pending and not captureArmed then return end') {
        throw 'AutoDrop exact routing / temporary wildcard dormancy is incomplete.'
    }

    $drone=Read-ZipText ($base+'Drone Companions (Revamp)/DroneLogic/Drone AI - Mech.lua')
    if($drone -notmatch 'G-CET semantic:drone-companions-revamp' -or
       $drone -notmatch '__gcetDcoAimHmg' -or $drone -notmatch '__gcetDcoBombusFollow' -or
       $drone -notmatch 'recordID ~= __gcetDcoAimHmg') {
        throw 'Drone Companions exact override prefilter is incomplete.'
    }

    $ghost=Read-ZipText ($base+'GhostVoidSystem/init.lua')
    if($ghost -notmatch 'G-CET semantic:ghost-void-system' -or
       $ghost -notmatch '__gcetGvsState' -or
       $ghost -notmatch 'addVoidEnergy\(amount, state\)' -or
       $ghost -notmatch 'addStability\(amount, state\)') {
        throw 'Ghost Void state reuse is incomplete.'
    }

    $tunnel=Read-ZipText ($base+'tunnel_rescue/init.lua')
    if($tunnel -notmatch 'G-CET semantic:tunnel-rescue' -or
       $tunnel -notmatch '__gcetTunnelOutsideAcc' -or
       $tunnel -notmatch 'if __gcetTunnelOutsideAcc < \.15 then return end' -or
       $tunnel -notmatch "if ok and H\.phase~='outside' then" -or
       $tunnel -notmatch "H\.phase~='outside'.*Timer\.draw") {
        throw 'Tunnel rescue quest-session dormancy is incomplete.'
    }

    $straight=Read-ZipText ($base+'Straight Edged Controls/init.lua')
    $blocking=Read-ZipText ($base+'Straight Edged Controls/modules/ui_blocking.lua')
    $lean=Read-ZipText ($base+'Straight Edged Controls/modules/lean.lua')
    $inspection=Read-ZipText ($base+'Straight Edged Controls/modules/inspection.lua')
    $settings=Read-ZipText ($base+'Straight Edged Controls/modules/settings_poll.lua')
    $attachments=Read-ZipText ($base+'Straight Edged Controls/modules/attachments.lua')
    $straightMarker='G-CET semantic:straight-edged-controls-input-dormancy'
    foreach($text in @($straight,$blocking,$lean,$inspection,$settings,$attachments)) {
        if($text -notmatch [regex]::Escape($straightMarker)) {
            throw 'Straight Edged Controls sequence-dormancy marker missing from a transformed file.'
        }
    }
    if($straight -match 'UIBlocking\.beginFrame\(\)' -or
       $straight -notmatch 'SettingsPoll\.poll\(deltaTime\)' -or
       $straight -notmatch 'Inspection\.onSessionReset\(\)') {
        throw 'Straight Edged Controls entry-point dormancy composition is incomplete.'
    }
    if($lean -notmatch '__gcetGetInputState' -or
       $lean -notmatch 'if seq == lastSeq then return end' -or
       $lean -notmatch 'local inMenu = UIBlocking\.beginFrame\(\)') {
        throw 'Straight Edged Controls lean sequence wake is incomplete.'
    }
    if($inspection -notmatch '__gcetGetInputState' -or
       $inspection -notmatch 'if seq == state\.lastInspectSeq then return end' -or
       $inspection -notmatch 'function Inspection\.onSessionReset') {
        throw 'Straight Edged Controls inspection sequence wake is incomplete.'
    }
    if($settings -notmatch '__gcetPollInterval = 0\.5' -or
       $settings -notmatch 'function Settings\.poll\(deltaTime\)' -or
       $settings -notmatch 'lastMasterEnabled and lastFasterAiming') {
        throw 'Straight Edged Controls low-rate settings maintenance is incomplete.'
    }
    if($attachments -notmatch '__gcetAttachmentsBridge' -or
       $attachments -notmatch 'if not suppressorChanged and not sightChanged and not holdActive and not state\.pending then return end') {
        throw 'Straight Edged Controls attachment sequence gate is incomplete.'
    }
    if($blocking -notmatch 'function UIBlocking\.beginFrame\(\)' ) {
        throw 'Straight Edged Controls explicit UI snapshot compatibility is missing.'
    }

    $inertia=Read-ZipText ($base+'ImmersiveHeadInertia/init.lua')
    if($inertia -notmatch 'G-CET semantic:immersive-head-inertia' -or
       $inertia -notmatch 'gcetImmersiveHeadInertiaOnAction' -or
       $inertia -notmatch 'CameraMouseX' -or $inertia -notmatch 'CameraMouseY' -or
       $inertia -notmatch 'ConsumeSingleAction') {
        throw 'ImmersiveHeadInertia exact routing is incomplete.'
    }
}
finally { $zip.Dispose() }

Write-Host 'Residual sixth-stack semantic composition contract passed: GoodFeelings hard draw + AirBackFlip + AutoDrop exact routing + tunnel quest dormancy + Drone Companions + GhostVoid + Straight sequence dormancy + ImmersiveHeadInertia.'
