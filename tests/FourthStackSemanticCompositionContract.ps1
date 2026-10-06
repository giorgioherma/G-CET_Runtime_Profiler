param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) {
    throw "G-CET resolver executable not found: $resolverExe"
}

$root = Join-Path $env:RUNNER_TEMP 'gcet-fourth-stack-semantic-composition'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-040506_FOURTH_STACK'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

# Known fixed 0-Engine live fixture.
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())
} finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source) {
    $dir = Join-Path $mods $Mod
    $path = Join-Path $dir $Relative
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    $Source | Set-Content -LiteralPath $path -Encoding utf8
}

Write-ModFile 'EasyTrainer' 'init.lua' @'
local Event = require("Core/Event")
local Utils = {}
local SelfFeature = { NoClip = require("Features/Self/Abilities/NoClip") }
local modulesLoaded = true
    Event.Observe("PlayerPuppet", "OnAction", function(_, action)
        if modulesLoaded then
            SelfFeature.NoClip.HandleMouseLook(action)
            if Utils then
                Utils.Weapon.HandleInputAction(action)
            end
        end
    end)
    Event.RegisterUpdate(function(dt)
        if Utils and Utils.Weapon then Utils.Weapon.Tick(dt) end
    end)
'@
Write-ModFile 'EasyTrainer' 'Features/Self/Abilities/NoClip.lua' @'
local Noclip = {}
function Noclip.HandleMouseLook(action)
    local actionName = Game.NameToString(action:GetName(action))
    if actionName ~= "CameraMouseX" then return end
    local x = action:GetValue(action)
    local sens = Game.GetSettingsSystem():GetVar("/controls/fppcameramouse", "FPP_MouseX"):GetValue() / 2.9
end
return Noclip
'@
Write-ModFile 'EasyTrainer' 'Utils/Weapon.lua' @'
local Weapon = { isAiming=false, snapshotValid=false }
function Weapon.HandleInputAction(action)
    local player = Game.GetPlayer()
    if not player then return end

    Weapon.isAiming = player.isAiming

    local actionName = Game.NameToString(action:GetName(action))
    local actionType = action:GetType(action).value

    if actionName == "RangedAttack" then
        Weapon.isShooting = actionType == "BUTTON_PRESSED"
    end
end
local function ReadEquippedRightHand()
    local player = Game.GetPlayer()
    local ts = Game.GetTransactionSystem()
    if not player or not ts then return nil, nil, nil end

    local item = ts:GetItemInSlot(player, "AttachmentSlots.WeaponRight")
    if not item then return nil, nil, nil end

    local itemData = item:GetItemData()
    if not itemData then return item, nil, item:GetItemID() end

    return item, itemData, item:GetItemID()
end
function Weapon.Tick(deltaTime)
    if not Weapon.snapshotValid then
        local item, itemData, itemID = ReadEquippedRightHand()
        Weapon.currentItem = item
        Weapon.currentItemData = itemData
        Weapon.currentItemID = itemID
        Weapon.snapshotValid = true
    end
end
return Weapon
'@

Write-ModFile 'EasyTrainer' 'Core/Event.lua' @'
local Logger = { Log=function() end }
local Event = {}
function Event.Observe(class, method, fn)
    Observe(class, method, fn)
    Logger.Log(string.format("Event: Observing %s.%s", tostring(class), tostring(method)))
end
return Event
'@
Write-ModFile 'EasyTrainer' 'UI/Registry/OptionRegistry.lua' @'
local OptionRegistry = { Ordered = {} }
OptionRegistry.Kind = { Toggle = "toggle" }
local RefIndex = setmetatable({}, { __mode = "k" })
local HotkeyDown = {}

local function HotkeyAction(id)
    return "OPTION_" .. tostring(id or ""):upper():gsub("[^A-Z0-9]+", "_")
end

function OptionRegistry.SetHotkey(id, hotkey)
    local Bindings = require("Controls/Bindings")
    local entry = { Id=id, Hotkey=HotkeyAction(id) }
    Bindings.Rebind(entry.Hotkey, hotkey)
end

function OptionRegistry.RegisterHotkeyActions()
    local Bindings = require("Controls/Bindings")
    local count = 0
    for _, entry in ipairs(OptionRegistry.Ordered) do
        if entry.Kind == OptionRegistry.Kind.Toggle then
            Bindings.EnsureAction(OptionRegistry.GetHotkeyAction(entry))
            count = count + 1
        end
    end
    return count
end

function OptionRegistry.UpdateHotkeys()
    local Bindings = require("Controls/Bindings")
    local changed = false
    for _, entry in ipairs(OptionRegistry.Ordered) do
        if entry.Kind == OptionRegistry.Kind.Toggle and entry.Ref then
            local action = HotkeyAction(entry.Id)
            local down = Bindings.IsActionDown(action)
            if down and not HotkeyDown[action] then changed = true end
            HotkeyDown[action] = down
        end
    end
    return changed
end
return OptionRegistry
'@
Write-ModFile 'EasyTrainer' 'Controls/Restrictions.lua' @'
local Input = require("Core/Input")
local State = require("Controls/State")
local Restrictions = {}
local lastMenuOpen = false
local lastWasController = false
local lastMouseEnabled = false
local lastTypingEnabled = false
function Restrictions.Clear() end
function Restrictions.Update()
    local menuOpen = State.IsMenuOpen()
    Input.UpdateDevice()

    local usingController = Input.IsController()
    local mouseEnabled = State.mouseEnabled
    local typingEnabled = State.typingEnabled

    if not menuOpen and lastMenuOpen then
        Restrictions.Clear()
        lastMenuOpen, lastWasController, lastMouseEnabled, lastTypingEnabled = false, false, false, false
        return
    end

    if not menuOpen then return end
    lastMenuOpen = menuOpen
    lastWasController = usingController
    lastMouseEnabled = mouseEnabled
    lastTypingEnabled = typingEnabled
end
return Restrictions
'@
Write-ModFile 'EasyTrainer' 'Features/Self/Abilities/AdvancedMobility.lua' @'
local AdvancedMobility = {}
AdvancedMobility.toggleDoubleJump = { value = false }
AdvancedMobility.toggleAirHover = { value = false }
AdvancedMobility.toggleChargeJump = { value = false }
local state = { doubleJumpApplied=false, airHoverApplied=false, chargeJumpApplied=false }
local function HandleStatToggle(toggle, statName, appliedFlag, label)
    local stats = Game.GetStatsSystem()
    local player = Game.GetPlayer()
    local entityID = player:GetEntityID()
end
function AdvancedMobility.Tick()
    HandleStatToggle(AdvancedMobility.toggleDoubleJump, "HasDoubleJump", "doubleJumpApplied", "Double Jump")
    HandleStatToggle(AdvancedMobility.toggleAirHover, "HasAirHover", "airHoverApplied", "Air Hover")
    HandleStatToggle(AdvancedMobility.toggleChargeJump, "HasChargeJump", "chargeJumpApplied", "Charge Jump")
end
return AdvancedMobility
'@
Write-ModFile 'EasyTrainer' 'Features/Self/Abilities/SuperSpeed.lua' @'
local SuperSpeed = {}
SuperSpeed.enabled = { value = false }
local applied = false
function SuperSpeed.Tick()
    local timeSystem = Game.GetTimeSystem()
    local isActive = timeSystem:IsTimeDilationActive()
    if SuperSpeed.enabled.value and not applied then applied = true
    elseif not SuperSpeed.enabled.value and applied then applied = false end
end
return SuperSpeed
'@
Write-ModFile 'EasyTrainer' 'Features/Self/Abilities/AirThrusterBoots.lua' @'
local AirThrusterBoots = {}
AirThrusterBoots.enabled = { value = false }
local applied = false
function AirThrusterBoots.Tick()
    local stats = Game.GetStatsSystem()
    local player = Game.GetPlayer()
    local entityID = player:GetEntityID()
    if AirThrusterBoots.enabled.value and not applied then applied = true
    elseif not AirThrusterBoots.enabled.value and applied then applied = false end
end
return AirThrusterBoots
'@

Write-ModFile 'TeleportGatewaySystem' 'init.lua' @'
local TGS = {
    activated=true,
    player=Game.GetPlayer(),
    cameraSys=Game.GetCameraSystem(),
    teleportFac=Game.GetTeleportationFacility(),
    showMainWindow=false,
    senseInhibited=false
}
local gatewayDB = { {name="fixture",gwx=1,gwy=2,gwz=3,gwr=0.1,spx=4,spy=5,spz=6,yaw=0} }
local senseSystemOff = false
local senseGWCheck = false
local scratchpadPin, scratchpadPinPrev = false, false
local scratchpadPinVis, scratchpadPinVisPrev = false, false
local scratchpadChanged = false
registerForEvent("onUpdate", function(deltaTime)
    if (TGS.activated) then
        playerPos = TGS.player:GetWorldPosition()
        playerAng = TGS.cameraSys:GetActiveCameraForward()
        playerYaw = TGS.player:GetWorldYaw()
        
        senseGWCheck = false
        
        if (#gatewayDB > 0) then
            for index = 1, #gatewayDB, 1 do
                GWDist = math.sqrt(((TGS.player:GetWorldPosition().x-gatewayDB[index].gwx)^2)+((TGS.player:GetWorldPosition().y-gatewayDB[index].gwy)^2)+((TGS.player:GetWorldPosition().z-gatewayDB[index].gwz)^2))
                if (TGS.senseInhibited or senseSystemOff) then
                    if (GWDist <= gatewayDB[index].gwr) then
                        senseGWCheck = true
                    end
                elseif (GWDist <= gatewayDB[index].gwr) then
                    print("TeleportGatewaySystem: Teleport TGS.activated -",gatewayDB[index].name)
                    
                    TGS.teleportFac:Teleport(TGS.player, Vector4.new(gatewayDB[index].spx, gatewayDB[index].spy, gatewayDB[index].spz, 1), EulerAngles.new(playerAng.x, playerAng.y, gatewayDB[index].yaw))
                    TGS.senseInhibited = true
                end
            end
        end
    end
    
    if scratchpadPin ~= scratchpadPinPrev then
        scratchpadChanged = true
        scratchpadPinPrev = scratchpadPin
    end
end)
registerForEvent("onDraw", function()
    ImGui.SetNextWindowPos(0, 0, ImGuiCond.FirstUseEver)
    if ImGui.Begin("Teleport Gateway System", TGS.showMainWindow) then
        ImGui.Text("fixture")
    end
    ImGui.End()
end)
'@

Write-ModFile 'DiscardAmmoOnReload' 'init.lua' @'
local Config = { Init=function() end }
local UI = {}
local ReloadSystem = { weaponSwap=false }
local Utility = { filter=function(list,name) for _,v in ipairs(list) do if v==name then return true end end return false end }
local swapActions = {'PreviousWeapon', 'NextWeapon', 'WeaponSlot1', 'WeaponSlot2', 'WeaponSlot3', 'WeaponWheel',}
local nonSwapActions = {'RangedAttack', 'MeleeAttack'}
registerForEvent('onInit', function()
    Config.Init()

    Observe('PlayerPuppet','OnAction', function(self, action)
        local actionName = Game.NameToString(action:GetName())
        if(ReloadSystem.weaponSwap) then
            if(Utility.filter(nonSwapActions, actionName)) then
                ReloadSystem.weaponSwap = false
            end
        elseif(Utility.filter(swapActions, actionName)) then
            ReloadSystem.weaponSwap = true
        end
    end)
end)
'@

Write-ModFile 'advanced_settings' 'init.lua' @'
registerForEvent("onInit", function()
  ConfigSystem = { OnUpdate=function() end }
  CPS = { setThemeBegin=function() end, setThemeEnd=function() end }
  draw = false
end)
registerForEvent("onUpdate", function()
  ConfigSystem:OnUpdate()
end)
registerForEvent("onDraw", function()
  CPS:setThemeBegin()
  if draw then
    ImGui.Text("Advanced Settings")
  end
  CPS:setThemeEnd()
end)
registerForEvent("onOverlayOpen", function()
        draw = true
end)
registerForEvent("onOverlayClose", function()
        draw = false
end)
'@

Write-ModFile 'Auto Ammo Crafting (I need more bullets)' 'init.lua' @'
local defaultSettings = { autoConvertTime = 6 }
local settings = { autoConvertTime = 6, combatCheck = true }
local firstRun = false
local ts = nil
local function __gcetGetTransactionSystem() return Game.GetTransactionSystem() end
firstRun = true
pauseTime = os.time()
scriptInterval = 0
combatMagsCrafted = 0
lastCraftPos = nil
inCombat = false
registerForEvent("onUpdate", function(deltaTime)
    if pauseTime > os.time() then
        return
    end
    if settings.combatCheck and inCombat then
        pauseTime = os.time() + 3
        return
    end
    if notReady() then
        pauseTime = os.time() + 3
        return
    end
    if playerInMenu() then
        pauseTime = os.time() + 3
        return
    end
    if firstRun then
        firstRun = false
    end
    player = Game.GetPlayerSystem():GetLocalPlayerMainGameObject()
    if not ts then ts = __gcetGetTransactionSystem() end
------------------------------------------------
-- Ready for take off.
------------------------------------------------
    scriptInterval = scriptInterval + deltaTime
    if scriptInterval < settings.autoConvertTime then
        return
    else
        scriptInterval = 0
    end
    if ts then ts:GetItemQuantity(player, TweakDBID.new("Ammo.HandgunAmmo")) end
end)
function notReady()
    inkMenuScenario = GetSingleton('inkMenuScenario'):GetSystemRequestsHandler()
    if inkMenuScenario:IsGamePaused() or inkMenuScenario:IsPreGame() then
        return true
    end
    if Game.GetPlayerSystem() == nil then
        return true
    end
    if Game.GetPlayerSystem():GetLocalPlayerMainGameObject() == nil then
        return true
    end
    if Game.GetPlayer() == nil then
        return true
    end
    if not Game.GetPlayer():IsAttached() then
        return true
    end
    return false
end
function playerInMenu()
    blackboard = Game.GetBlackboardSystem():Get(Game.GetAllBlackboardDefs().UI_System);
    uiSystemBB = (Game.GetAllBlackboardDefs().UI_System);
    return(blackboard:GetBool(uiSystemBB.IsInMenu));
end
'@

function Find-CallbackRange([string]$Path,[string]$OpeningPattern) {
    $lines = @((Get-Content -LiteralPath $Path))
    $start = -1
    for ($i=0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $OpeningPattern) { $start=$i; break }
    }
    if ($start -lt 0) { throw "Opening not found: $OpeningPattern in $Path" }
    for ($i=$start+1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*end\s*\)\s*$') {
            return @{ Start=$start+1; End=$i+1 }
        }
    }
    throw "Callback closing not found: $OpeningPattern in $Path"
}

function CallbackRow([int]$Id,[string]$Owner,[string]$Kind,[string]$Target,[string]$File,[int]$Start,[int]$End,[double]$Ms,[double]$Calls=60) {
    @{
        registrationId=$Id; owner=$Owner; infrastructure=$false; kind=$Kind; target=$Target
        source=@{file=$File;lineStart=$Start;lineEnd=$End}
        callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms; globalWorkSharePct=5
        familyWorkSharePct=40; avgExclusiveUs=100; maxExclusiveMs=2; spikeCount=0; maxSpikeExclusiveMs=0
    }
}

$easyRange = Find-CallbackRange (Join-Path $mods 'EasyTrainer\init.lua') 'Event\.Observe\("PlayerPuppet",\s*"OnAction"'
$teleRange = Find-CallbackRange (Join-Path $mods 'TeleportGatewaySystem\init.lua') 'registerForEvent\("onUpdate"'
$discardRange = Find-CallbackRange (Join-Path $mods 'DiscardAmmoOnReload\init.lua') "Observe\('PlayerPuppet','OnAction'"
$advancedRange = Find-CallbackRange (Join-Path $mods 'advanced_settings\init.lua') 'registerForEvent\("onUpdate"'
$ammoRange = Find-CallbackRange (Join-Path $mods 'Auto Ammo Crafting (I need more bullets)\init.lua') 'registerForEvent\("onUpdate"'

$handoff=@{
 schemaVersion='1.8'
 callbacks=@(
    (CallbackRow 458 'EasyTrainer' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $easyRange.Start $easyRange.End 35.64 1485),
    (CallbackRow 429 'TeleportGatewaySystem' 'event' 'onUpdate' 'init.lua' $teleRange.Start $teleRange.End 17.16 60),
    (CallbackRow 457 'DiscardAmmoOnReload' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $discardRange.Start $discardRange.End 15.30 1485),
    (CallbackRow 21 'advanced_settings' 'event' 'onUpdate' 'init.lua' $advancedRange.Start $advancedRange.End 5.56 60),
    (CallbackRow 61 'Auto Ammo Crafting (I need more bullets)' 'event' 'onUpdate' 'init.lua' $ammoRange.Start $ammoRange.End 5.31 60)
 )
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Fourth-stack semantic pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$rules=@('easytrainer','teleport-gateway-system','discard-ammo-on-reload','advanced-settings','auto-ammo-crafting')
foreach($rule in $rules) {
    $matches=@()
    foreach($family in @($resolver.callbackFamilies)) {
        $matches += @($family.topConsumers | Where-Object { $_.semantic.RuleId -eq $rule })
    }
    if($matches.Count -lt 1 -or @($matches | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
        throw "Fourth-stack semantic source proof failed: $rule"
    }
}

$manifest=Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
foreach($rule in $rules) {
    if(@($manifest.transforms | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule }).Count -ne 1) {
        throw "Fourth-stack semantic transform missing/duplicated: $rule"
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

    $easyInit=Read-ZipText ($base+'EasyTrainer/init.lua')
    $easyNoClip=Read-ZipText ($base+'EasyTrainer/Features/Self/Abilities/NoClip.lua')
    $easyWeapon=Read-ZipText ($base+'EasyTrainer/Utils/Weapon.lua')
    $easyRegistry=Read-ZipText ($base+'EasyTrainer/UI/Registry/OptionRegistry.lua')
    $easyRestrictions=Read-ZipText ($base+'EasyTrainer/Controls/Restrictions.lua')
    $easyMobility=Read-ZipText ($base+'EasyTrainer/Features/Self/Abilities/AdvancedMobility.lua')
    $easySuperSpeed=Read-ZipText ($base+'EasyTrainer/Features/Self/Abilities/SuperSpeed.lua')
    $easyThrusters=Read-ZipText ($base+'EasyTrainer/Features/Self/Abilities/AirThrusterBoots.lua')
    if($easyInit -notmatch 'SubscribeAction' -or
       $easyInit -notmatch 'CameraMouseX' -or
       $easyInit -notmatch 'RangedAttack' -or
       $easyInit -notmatch 'decodeType = false' -or
       $easyInit -notmatch [regex]::Escape('Event.Observe("PlayerPuppet", "OnAction"') -or
       $easyNoClip -notmatch 'HandleMouseLook\(action, routedName\)' -or
       $easyWeapon -notmatch 'HandleInputAction\(action, routedName\)' -or
       $easyWeapon -notmatch 'Weapon\.isAiming = player and player\.isAiming or false' -or
       $easyRegistry -notmatch 'local __gcetBindings = nil' -or
       $easyRegistry -notmatch 'entry\.Hotkey or HotkeyAction\(entry\.Id\)' -or
       $easyRestrictions -notmatch 'if not menuOpen then' -or
       $easyMobility -notmatch 'and not state\.chargeJumpApplied then return end' -or
       $easySuperSpeed -notmatch 'if not SuperSpeed\.enabled\.value and not applied then return end' -or
       $easyThrusters -notmatch 'if not AirThrusterBoots\.enabled\.value and not applied then return end') {
        throw 'EasyTrainer routed-action/dormancy semantic composition is incomplete.'
    }

    $tele=Read-ZipText ($base+'TeleportGatewaySystem/init.lua')
    if($tele -match [regex]::Escape('TGS.player:GetWorldPosition().x-gatewayDB[index].gwx') -or
       $tele -notmatch '__gcetGatewayFarInterval = 0\.10' -or
       $tele -notmatch '__gcetGatewayNearMargin = 30\.0' -or
       $tele -notmatch 'local __gcetDx = playerPos\.x - gatewayDB\[index\]\.gwx' -or
       $tele -notmatch 'TGS\.showMainWindow == true' -or
       $tele -notmatch 'if not TGS\.showMainWindow then return end') {
        throw 'Teleport Gateway proximity-sentinel semantic composition is incomplete.'
    }

    $discard=Read-ZipText ($base+'DiscardAmmoOnReload/init.lua')
    if($discard -notmatch 'SubscribeAction' -or
       $discard -notmatch 'PreviousWeapon' -or
       $discard -notmatch 'MeleeAttack' -or
       $discard -notmatch 'decodeType = false') {
        throw 'Discard Ammo exact action routing is incomplete.'
    }

    $advanced=Read-ZipText ($base+'advanced_settings/init.lua')
    if([regex]::Matches($advanced,[regex]::Escape('if not draw then return end')).Count -ne 2) {
        throw 'Advanced Settings overlay-closed gates were not applied to update and draw.'
    }

    $ammo=Read-ZipText ($base+'Auto Ammo Crafting (I need more bullets)/init.lua')
    if($ammo -notmatch '__gcetAutoAmmoReadyProbeElapsed = 0\.10' -or
       $ammo -notmatch '__gcetAutoAmmoProbeDue' -or
       $ammo -notmatch 'or __gcetAutoAmmoCraftDue' -or
       $ammo -notmatch 'if not __gcetAutoAmmoCraftDue then' -or
       $ammo -notmatch 'local __gcetPlayerSystem = Game\.GetPlayerSystem\(\)' -or
       $ammo -notmatch 'local __gcetPlayer = Game\.GetPlayer\(\)' -or
       $ammo -notmatch 'local __gcetUIBB = Game\.GetAllBlackboardDefs\(\)\.UI_System') {
        throw 'Auto Ammo readiness-sentinel/author-cadence semantic composition is incomplete.'
    }
}
finally { $zip.Dispose() }

Write-Host 'Fourth-stack semantic composition contract passed: EasyTrainer + TeleportGatewaySystem + DiscardAmmoOnReload + advanced_settings + Auto Ammo Crafting.'
