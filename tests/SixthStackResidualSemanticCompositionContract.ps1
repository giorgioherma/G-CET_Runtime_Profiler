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
local Event = { Observe = Observe }
local modulesLoaded = true
local SelfFeature = { NoClip = { HandleMouseLook = function(action) end } }
local Utils = { Weapon = { HandleInputAction = function(action) end } }
local Handler = { Update = function() end }
local UI = { Overlay = { Render = function() end } }
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
registerForEvent("onDraw", function()
    UI.Overlay.Render()
    if not modulesLoaded then return end
    Handler.Update()
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
local cfg = { swapWindow=0.90, debugPrint=false }
local t, pendingUntil, dropAt = 0.0, 0.0, 0.0
local pending, oldItemID, oldKey = false, nil, nil
local triggerActions = { Reload=true, Interaction=true, Interact=true, Use=true, ContextualAction=true, Loot=true, PickUp=true, Pickup=true, Take=true }
local function getActionName(action) return Game.NameToString(action:GetName()) end
local function getActiveWeaponItemID(player) return player:GetActiveWeapon():GetItemID() end
local function itemKey(itemID) return tostring(itemID) end
local function isUnarmedItemID(itemID) return false end
local captureArmed, captureStart = false, 0.0
registerInput("ADWOP_CaptureNextAction", "capture", function(isDown)
  if not isDown then return end
  captureArmed = true
  captureStart = t + 0.15
end)
registerInput("ADWOP_ToggleDebug", "debug", function(isDown)
  if not isDown then return end
  cfg.debugPrint = not cfg.debugPrint
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
    end
    if cfg.debugPrint then print(name) end
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

Write-ModFile 'Straight Edged Controls' 'init.lua' @'
local UIBlocking = require('modules/ui_blocking')
local Lean = { update=function() end, pollInput=function() end }
local Inspection = { update=function() end, pollInput=function() end }
local ScrollWalk = { tick=function() end }
local SettingsPoll = { poll=function() end }
local Attachments = { update=function() end }
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
$airLine=Find-Line (Join-Path $mods 'AirBackFlip\init.lua') 'Observe\("PlayerPuppet", "OnAction"'
$autoLine=Find-Line (Join-Path $mods 'AutoDropWeaponOnPickupEquip\init.lua') 'Observe\("PlayerPuppet", "OnAction"'
$droneLine=Find-Line (Join-Path $mods 'Drone Companions (Revamp)\DroneLogic\Drone AI - Mech.lua') "Override\('TweakAIActionAbstract', 'Update'"
$ghostLine=Find-Line (Join-Path $mods 'GhostVoidSystem\init.lua') 'registerForEvent\("onUpdate"'
$straightLine=Find-Line (Join-Path $mods 'Straight Edged Controls\init.lua') "registerForEvent\('onUpdate'"
$inertiaLine=Find-Line (Join-Path $mods 'ImmersiveHeadInertia\init.lua') 'Observe\("PlayerPuppet", "OnAction"'

$handoff=@{
 schemaVersion='1.8'
 callbacks=@(
   (CallbackRow 1 'GoodFeelings' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $goodLine 30.940503 1244),
   (CallbackRow 2 'AirBackFlip' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $airLine 12.077188 1244),
   (CallbackRow 3 'AutoDropWeaponOnPickupEquip' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $autoLine 9.888169 1244),
   (CallbackRow 4 'Drone Companions (Revamp)' 'Override' 'TweakAIActionAbstract::Update' 'DroneLogic/Drone AI - Mech.lua' $droneLine 8.51844 117),
   (CallbackRow 5 'GhostVoidSystem' 'event' 'onUpdate' 'init.lua' $ghostLine 3.452751 52),
   (CallbackRow 6 'Straight Edged Controls' 'event' 'onUpdate' 'init.lua' $straightLine 19.950931 52),
   (CallbackRow 7 'ImmersiveHeadInertia' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $inertiaLine 9.873854 1244)
 )
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Residual semantic pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$rules=@(
 'good-feelings','air-backflip','auto-drop-weapon-on-pickup-equip',
 'drone-companions-revamp','ghost-void-system','straight-edged-controls',
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
    $handler=Read-ZipText ($base+'GoodFeelings/Controls/Handler.lua')
    $overlay=Read-ZipText ($base+'GoodFeelings/UI/Elements/Overlay.lua')
    if($good -notmatch 'G-CET semantic:good-feelings' -or $good -notmatch 'gcetGoodFeelingsOnAction' -or
       $good -notmatch 'CameraMouseX' -or $good -notmatch 'RangedAttack' -or $good -notmatch 'SubscribeAction') {
        throw 'GoodFeelings exact action routing is incomplete.'
    }
    if($handler -notmatch 'G-CET semantic:good-feelings' -or
       $handler -notmatch '__gcetHandlerLastMenuOpen' -or
       $handler -notmatch 'if State\.menuOpen or __gcetMenuChanged then') {
        throw 'GoodFeelings closed-menu gate is incomplete.'
    }
    if($overlay -notmatch 'G-CET semantic:good-feelings' -or
       $overlay -notmatch '__gcetMeasureText' -or $overlay -notmatch '__gcetMeasureCache' -or
       $overlay -match 'local textW, textH = ImGui\.CalcTextSize') {
        throw 'GoodFeelings overlay measurement cache is incomplete.'
    }

    $air=Read-ZipText ($base+'AirBackFlip/init.lua')
    if($air -notmatch 'G-CET semantic:air-backflip' -or $air -notmatch 'gcetAirBackFlipOnAction' -or
       $air -notmatch 'AirBackflip_SwingOver' -or $air -notmatch 'MoveY' -or $air -notmatch 'SubscribeAction') {
        throw 'AirBackFlip exact routing is incomplete.'
    }

    $auto=Read-ZipText ($base+'AutoDropWeaponOnPickupEquip/init.lua')
    if($auto -notmatch 'G-CET semantic:auto-drop-weapon-on-pickup-equip' -or
       $auto -notmatch '__gcetRawName' -or
       $auto -notmatch 'not captureArmed and not cfg\.debugPrint and not triggerActions\[name\]') {
        throw 'AutoDrop action-name prefilter is incomplete.'
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

    $straight=Read-ZipText ($base+'Straight Edged Controls/init.lua')
    $blocking=Read-ZipText ($base+'Straight Edged Controls/modules/ui_blocking.lua')
    if($straight -notmatch 'G-CET semantic:straight-edged-controls' -or
       $straight -notmatch 'UIBlocking\.beginFrame\(\)') {
        throw 'Straight Edged Controls frame snapshot is incomplete.'
    }
    if($blocking -notmatch 'G-CET semantic:straight-edged-controls' -or
       $blocking -notmatch '__gcetComputeBlocked' -or
       $blocking -notmatch '__gcetFrameCacheReady') {
        throw 'Straight Edged Controls UI-blocking cache is incomplete.'
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

Write-Host 'Residual sixth-stack semantic composition contract passed: GoodFeelings + AirBackFlip + AutoDrop + Drone Companions + GhostVoid + Straight Edged Controls + ImmersiveHeadInertia.'
