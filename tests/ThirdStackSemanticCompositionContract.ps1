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

$root = Join-Path $env:RUNNER_TEMP 'gcet-third-stack-semantic-composition'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

$capture = Join-Path $root 'CET-20990101-030405_THIRD_STACK'
$mods = Join-Path $root 'mods'
$ojt = Join-Path $mods 'OverclockedJenkinsTendons'
$songs = Join-Path $mods 'songsdeck'
$judy = Join-Path $mods 'DynamicOutfitsJudy'
$fov = Join-Path $mods 'FovSentinel'
$fenix = Join-Path $mods 'FenixMantisBlade'
$fenixModules = Join-Path $fenix 'modules'
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$ojt,$songs,$judy,$fov,$fenixModules,$zeroDir | Out-Null

# Proven bundled 0-Engine runtime.
$encodedInitPath = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
$encodedInit = (Get-Content -LiteralPath $encodedInitPath -Raw).Trim()
$compressedInit = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressedInit)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())
}
finally {
    $output.Dispose(); $gzip.Dispose(); $input.Dispose()
}

# OJT: deliberately retains the dynamic raw OnAction shape the generic resolver
# parks; onUpdate is generic-frame eligible and semantic composes afterwards.
@'
local OverclockedJenkinsTendons = { loaded = true }
function OverclockedJenkinsTendons:New()
    registerForEvent("onInit", function()
        local Input = require("input")
        Observe("PlayerPuppet", "OnAction", function(_, action, consumer)
            if not self.loaded then return end
            local name  = Game.NameToString(action:GetName())
            local atype = action:GetType(action).value
            Input.handleAction(name, atype)
        end)
        self._state = require("state")
        self._Helpers = require("helpers")
        self._Ignition = require("ignition")
        self._Wave = { tickBurningTargets=function() end, tickSmokeVisionStrips=function() end }
        self._Swim = require("swim")
    end)

    registerForEvent("onUpdate", function(delta)
        if not self._climCleared then self._climCleared = true end
        if not self.loaded or not self._Ignition then return end
        local ok, err = pcall(self._Ignition.update, delta)
        if not ok and self._Helpers then self._Helpers.logCritical(tostring(err)) end
        if self._Wave then
            pcall(self._Wave.tickBurningTargets, delta)
            pcall(self._Wave.tickSmokeVisionStrips, delta)
        end
        if self._Swim then
            pcall(self._Swim.update, delta)
        end
    end)
end
return OverclockedJenkinsTendons:New()
'@ | Set-Content -LiteralPath (Join-Path $ojt 'init.lua') -Encoding utf8

@'
local state = require("state")
local Helpers = {}
function Helpers.isSprinting()
    local bb = Game.GetBlackboardSystem():GetLocalInstanced(
        state.player:GetEntityID(), Game.GetAllBlackboardDefs().PlayerStateMachine)
    return bb:GetInt(Game.GetAllBlackboardDefs().PlayerStateMachine.LocomotionDetailed) ==
        EnumInt(gamePSMDetailedLocomotionStates.Sprint)
end
function Helpers.isSliding()
    local bb = Game.GetBlackboardSystem():GetLocalInstanced(
        state.player:GetEntityID(), Game.GetAllBlackboardDefs().PlayerStateMachine)
    return bb:GetInt(Game.GetAllBlackboardDefs().PlayerStateMachine.LocomotionDetailed) ==
        EnumInt(gamePSMDetailedLocomotionStates.Slide)
end
function Helpers.isInWater()
    local defs = Game.GetAllBlackboardDefs().PlayerStateMachine
    local bb = Game.GetBlackboardSystem():GetLocalInstanced(state.player:GetEntityID(), defs)
    local swimState = bb:GetInt(defs.Swimming) or 0
    return swimState == 1 or swimState == 2 or swimState == 3
end
function Helpers.logCritical(_) end
return Helpers
'@ | Set-Content -LiteralPath (Join-Path $ojt 'helpers.lua') -Encoding utf8

@'
local state = require("state")
local Helpers = require("helpers")
local cfg = { enabled = true }
local Ignition = {}
local function playerIsBurningAny() return false end
function Ignition.update(dt)
    if not state.player then return end
    local sprinting = Helpers.isSprinting()
    local sliding = Helpers.isSliding()
    state.wasSliding = sliding
    if cfg.enabled and Helpers.isSliding() and playerIsBurningAny() and not state.slideEntryActive then
        state.phase = "IDLE"
    elseif cfg.enabled and Helpers.isInWater() and playerIsBurningAny() then
        state.phase = "IDLE"
    elseif state.dashedThisCycle then
        state.dashedThisCycle = false
    end
    if state.phase == "IDLE" then return end
    if sprinting then state.speedBoostActive = true end
end
return Ignition
'@ | Set-Content -LiteralPath (Join-Path $ojt 'ignition.lua') -Encoding utf8

@'
local state = require("state")
local Helpers = require("helpers")
local cfg = { swimBoostEnabled = true, requireTendons = false }
local Swim = {}
local function shouldApply()
    if not cfg.swimBoostEnabled then return false end
    return Helpers.isInWater()
end
function Swim.update(dt)
    if not state.player then return end
    local want = shouldApply()
    state.swimBoostActive = want
end
return Swim
'@ | Set-Content -LiteralPath (Join-Path $ojt 'swim.lua') -Encoding utf8

@'
local state = { pressingSprint=false, phase="IDLE", smokeVisionStrips={} }
return state
'@ | Set-Content -LiteralPath (Join-Path $ojt 'state.lua') -Encoding utf8

@'
local Input = {}
function Input.handleAction(name, atype)
    local lname = string.lower(tostring(name))
    if name == "Sprint" or name == "ToggleSprint" then end
    if name == "Dodge" or name == "Dodge_Z" or name == "DodgeForward"
       or lname:find("dodge") or lname:find("dash") then end
end
return Input
'@ | Set-Content -LiteralPath (Join-Path $ojt 'input.lua') -Encoding utf8

# SongsDeck: exact source domain + all-action debug mode means generic AUTO must
# not invent a normal exact prefilter; semantic preserves the Override.
@'
local actionLog = false
local overdriveOccuring = false
local inCameraPS = nil
local isDeckEquipped = true
local scanning = false
local BlackwallUpload = { Execute=function() end }
local function log(_) end

Override("PlayerPuppet", "OnAction", function(this, action, consumer, wrappedMethod)
  local actionName = Game.NameToString(ListenerAction.GetName(action))
  local actionType = ListenerAction.GetType(action)

  if actionLog then
    log("OnAction: " .. actionName .. " " .. tostring(actionType))
  end
  if inCameraPS and actionName == "StopDeviceControl" then return false end
  if actionName == "VisionHold" then scanning = true end
  if actionName == "IconicCyberware" then isDeckEquipped = true end
  if actionName == "MeleeBlock" and scanning then BlackwallUpload.Execute(nil, true) end
  if overdriveOccuring and (actionName == "RangedAttack" or actionName == "MeleeAttack") then
    BlackwallUpload.Execute()
  end
  return wrappedMethod(action, consumer)
end)
'@ | Set-Content -LiteralPath (Join-Path $songs 'init.lua') -Encoding utf8

# Judy: event-owned spawn state; unspawned background mission polling is the
# semantic idle lane, attached managed NPCs retain every-frame behavior.
@'
local DynamicFramework = {
    isGameLoaded = false,
    globalConfig = { mod_enabled = true },
    states = { judy = { is_spawned=false, npc_entity=nil } },
    timer = 0
}
function DynamicFramework:GetActiveShieldInfo(_) return false, nil end
function DynamicFramework:PrefetchAppearance(_,_) end
function DynamicFramework:ScheduleAppearance(_,_) end

registerForEvent("onInit", function()
    Observe("ScriptedPuppet", "OnGameAttached", function(selfPuppet)
        DynamicFramework.states.judy.is_spawned = true
        DynamicFramework.states.judy.npc_entity = selfPuppet
    end)
    Observe("ScriptedPuppet", "OnDetach", function(selfPuppet)
        DynamicFramework.states.judy.is_spawned = false
        DynamicFramework.states.judy.npc_entity = nil
    end)
    Observe("ScriptedPuppet", "PrefetchAppearanceChange", function(selfPuppet, appearanceName)
        DynamicFramework:PrefetchAppearance(selfPuppet, appearanceName)
    end)
    Observe("ScriptedPuppet", "ScheduleAppearanceChange", function(selfPuppet, appearanceName)
        DynamicFramework:ScheduleAppearance(selfPuppet, appearanceName)
    end)
end)

registerForEvent("onUpdate", function(dt)
    local srh = __gcetGetSystemRequestsHandler()
    if not srh or srh:IsPreGame() then
        DynamicFramework.isGameLoaded = false
        return
    end

    local player = __gcetGetPlayer()
    if not player then
        DynamicFramework.isGameLoaded = false
        return
    end
    local qs = __gcetGetQuestsSystem()
    if not DynamicFramework.isGameLoaded then
        DynamicFramework.isGameLoaded = true
    end
    if not DynamicFramework.globalConfig.mod_enabled then return end
    local isShielded = DynamicFramework:GetActiveShieldInfo("judy")
    DynamicFramework.timer = (DynamicFramework.timer or 0) + dt
end)
'@ | Set-Content -LiteralPath (Join-Path $judy 'init.lua') -Encoding utf8

# FOV Sentinel: preserve draw cadence; only prove/reuse the already-computed TPP state.
@'
local function a9()
  local y=Game.GetPlayer()
  local aa=y and y:FindVehicleCameraManager()
  if aa then return aa:IsTPPActive()==true end
  return false
end
local function ad(ae,af,K) return 90 end
registerForEvent("onDraw",function()
  local ac=a9()
  if ImGui.Begin("##FakeWidget") then
    local aA=ad(a9(),true,0)
    ImGui.Text(tostring(aA))
    ImGui.End()
  end
end)
'@ | Set-Content -LiteralPath (Join-Path $fov 'init.lua') -Encoding utf8

# Fenix: generic action routing is outside the semantic concern here. The
# expensive onUpdate body must return before player/raycast work when inactive.
@'
local wallhang = require("modules/wallhang")
local config = { hangEnabled=true }
registerForEvent("onUpdate", function(dt)
    wallhang.Update(dt, config)
end)
'@ | Set-Content -LiteralPath (Join-Path $fenix 'init.lua') -Encoding utf8

@'
local wallhang = {}
local physics = require("modules/wallhang_physics")
local isHangPressed = false
local isStuck = false
local jumpCount = 0
function IsHighEnough(player)
    local from = player:GetWorldPosition()
    local hit = physics.RayCast(from, from)
    return not hit
end
function wallhang.Init(config)
    Observe("PlayerPuppet", "OnAction", function(self, action)
        local actionName = Game.NameToString(action:GetName(action))
        if actionName == "CameraMouseX" then end
        if actionName == "Jump" then end
        if actionName == "MeleeBlock" then isHangPressed = true end
    end)
end
function wallhang.Update(deltaTime, config)
    if not config.hangEnabled then
        if isStuck then
            isStuck = false
            physics.Release(Game.GetPlayer())
        end
        return
    end

    local player = Game.GetPlayer()
    if not player or not player:IsAttached() then return end

    -- DYNAMIC RESET: If feet are on ground and we aren't currently grabbing, reset jump count.
    if not IsHighEnough(player) and not isStuck then
        jumpCount = 0
    end

    -- STAGE 1: Early out if not trying to hang
    if not isHangPressed and not isStuck then return end

    if IsHighEnough(player) then physics.RayCast(player:GetWorldPosition(), player:GetWorldPosition()) end
end
return wallhang
'@ | Set-Content -LiteralPath (Join-Path $fenixModules 'wallhang.lua') -Encoding utf8
'return { RayCast=function(a,b) return false end, Release=function() end }' |
    Set-Content -LiteralPath (Join-Path $fenixModules 'wallhang_physics.lua') -Encoding utf8

function Find-CallbackRange([string]$Text,[string]$OpeningPattern) {
    $lines = @($Text -split "\r?\n")
    $start = -1
    for ($i=0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $OpeningPattern) { $start=$i; break }
    }
    if ($start -lt 0) { throw "Opening not found: $OpeningPattern" }
    for ($i=$start+1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*end\s*\)\s*$') {
            return @{ Start=$start+1; End=$i+1 }
        }
    }
    throw "Callback closing not found: $OpeningPattern"
}

function CallbackRow(
    [int]$Id,[string]$Owner,[string]$Kind,[string]$Target,
    [string]$File,[int]$LineStart,[int]$LineEnd,[double]$Ms,[double]$Calls=60.0
) {
    @{
        registrationId=$Id; owner=$Owner; infrastructure=$false; kind=$Kind; target=$Target
        source=@{file=$File;lineStart=$LineStart;lineEnd=$LineEnd}
        callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms; globalWorkSharePct=[math]::Max(1.1,$Ms/2)
        familyWorkSharePct=50.0; avgExclusiveUs=[math]::Max(1.0,$Ms*1000/[math]::Max(1.0,$Calls))
        maxExclusiveMs=2.0; spikeCount=0; maxSpikeExclusiveMs=0
    }
}

$ojtInitText = Get-Content -LiteralPath (Join-Path $ojt 'init.lua') -Raw
$songsText = Get-Content -LiteralPath (Join-Path $songs 'init.lua') -Raw
$judyText = Get-Content -LiteralPath (Join-Path $judy 'init.lua') -Raw
$fovText = Get-Content -LiteralPath (Join-Path $fov 'init.lua') -Raw
$fenixText = Get-Content -LiteralPath (Join-Path $fenix 'init.lua') -Raw

$ojtAction = Find-CallbackRange $ojtInitText 'Observe\("PlayerPuppet",\s*"OnAction"'
$ojtUpdate = Find-CallbackRange $ojtInitText 'registerForEvent\("onUpdate"'
$songsAction = Find-CallbackRange $songsText 'Override\("PlayerPuppet",\s*"OnAction"'
$judyUpdate = Find-CallbackRange $judyText 'registerForEvent\("onUpdate"'
$fovDraw = Find-CallbackRange $fovText 'registerForEvent\("onDraw"'
$fenixUpdate = Find-CallbackRange $fenixText 'registerForEvent\("onUpdate"'

@{
    schemaVersion='1.8'
    callbacks=@(
        (CallbackRow 197 'OverclockedJenkinsTendons' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $ojtAction.Start $ojtAction.End 24.0 1500),
        (CallbackRow 198 'OverclockedJenkinsTendons' 'event' 'onUpdate' 'init.lua' $ojtUpdate.Start $ojtUpdate.End 16.0 60),
        (CallbackRow 237 'songsdeck' 'Override' 'PlayerPuppet::OnAction' 'init.lua' $songsAction.Start $songsAction.End 19.9 1500),
        (CallbackRow 85 'DynamicOutfitsJudy' 'event' 'onUpdate' 'init.lua' $judyUpdate.Start $judyUpdate.End 16.9 60),
        (CallbackRow 91 'FovSentinel' 'event' 'onDraw' 'init.lua' $fovDraw.Start $fovDraw.End 8.7 60),
        (CallbackRow 101 'FenixMantisBlade' 'event' 'onUpdate' 'init.lua' $fenixUpdate.Start $fenixUpdate.End 5.0 60)
    )
    optimizerEvidence=@()
} | ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) {
    throw 'Third-stack semantic Resolver/generator composition run failed.'
}

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$expectations=@(
    @{Owner='OverclockedJenkinsTendons';Rule='overclocked-jenkins-tendons'},
    @{Owner='songsdeck';Rule='songsdeck'},
    @{Owner='DynamicOutfitsJudy';Rule='dynamic-outfits-judy'},
    @{Owner='FovSentinel';Rule='fov-sentinel'},
    @{Owner='FenixMantisBlade';Rule='fenix-mantis-blade'}
)
foreach($e in $expectations) {
    $rows=@($resolver.callbackFamilies.topConsumers | Where-Object { $_.owner -eq $e.Owner -and $_.semantic.RuleId -eq $e.Rule })
    if($rows.Count -lt 1) { throw "Semantic rule did not match measured owner: $($e.Rule)" }
    if(@($rows | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
        throw "Semantic source proof failed: $($e.Rule)"
    }
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
foreach($rule in $expectations.Rule) {
    $transforms=@($manifest.transforms | Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule })
    if($transforms.Count -ne 1) { throw "Expected one semantic transform for $rule, got $($transforms.Count)." }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$EntryName) {
        $entry=$zip.GetEntry($EntryName)
        if($null -eq $entry) { throw "ZIP entry not found: $EntryName" }
        $reader=[System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $base='bin/x64/plugins/cyber_engine_tweaks/mods/'

    $ojtInitOut=Read-ZipText ($base+'OverclockedJenkinsTendons/init.lua')
    $ojtHelpersOut=Read-ZipText ($base+'OverclockedJenkinsTendons/helpers.lua')
    $ojtIgnitionOut=Read-ZipText ($base+'OverclockedJenkinsTendons/ignition.lua')
    $ojtSwimOut=Read-ZipText ($base+'OverclockedJenkinsTendons/swim.lua')
    foreach($text in @($ojtInitOut,$ojtHelpersOut,$ojtIgnitionOut,$ojtSwimOut)) {
        if($text -notmatch [regex]::Escape('G-CET semantic:overclocked-jenkins-tendons')) {
            throw 'OJT semantic marker did not cover all changed files.'
        }
    }
    if($ojtInitOut -notmatch 'actions = "\*"' -or
       $ojtInitOut -notmatch '__gcetOjtIdleElapsed' -or
       $ojtInitOut -notmatch 'getLocomotionSnapshot' -or
       $ojtHelpersOut -notmatch 'function Helpers\.getLocomotionSnapshot' -or
       $ojtIgnitionOut -notmatch 'function Ignition\.update\(dt, locomotion\)' -or
       $ojtIgnitionOut -match 'local sprinting = Helpers\.isSprinting\(\)' -or
       $ojtSwimOut -notmatch 'function Swim\.update\(dt, locomotion\)') {
        throw 'OJT semantic activity/snapshot composition is incomplete.'
    }

    $songsOut=Read-ZipText ($base+'songsdeck/init.lua')
    if($songsOut -notmatch [regex]::Escape('G-CET semantic:songsdeck') -or
       $songsOut -notmatch '__gcetSongsDeckActions' -or
       $songsOut -notmatch 'if not actionLog and not __gcetSongsDeckActions\[actionName\] then') {
        throw 'SongsDeck semantic Override prefilter is incomplete.'
    }
    $prefilterIndex=$songsOut.IndexOf('if not actionLog and not __gcetSongsDeckActions[actionName] then')
    $typeIndex=$songsOut.IndexOf('local actionType = ListenerAction.GetType(action)',$prefilterIndex)
    if($prefilterIndex -lt 0 -or $typeIndex -lt 0 -or $prefilterIndex -gt $typeIndex) {
        throw 'SongsDeck still decodes action type before the semantic prefilter.'
    }

    $judyOut=Read-ZipText ($base+'DynamicOutfitsJudy/init.lua')
    if($judyOut -notmatch [regex]::Escape('G-CET semantic:dynamic-outfits-judy') -or
       $judyOut -notmatch '__gcetDynamicIdleElapsed' -or
       $judyOut -notmatch '__gcetAnySpawned' -or
       $judyOut -notmatch '< 0\.25 then return') {
        throw 'DynamicOutfitsJudy semantic idle lane is incomplete.'
    }

    $fovOut=Read-ZipText ($base+'FovSentinel/init.lua')
    if($fovOut -notmatch [regex]::Escape('G-CET semantic:fov-sentinel') -or
       $fovOut -notmatch [regex]::Escape('local aA=ad(ac,true,0)') -or
       $fovOut -match [regex]::Escape('local aA=ad(a9(),true,0)')) {
        throw 'FovSentinel duplicate camera query was not eliminated.'
    }

    $fenixOut=Read-ZipText ($base+'FenixMantisBlade/modules/wallhang.lua')
    if($fenixOut -notmatch [regex]::Escape('G-CET semantic:fenix-mantis-blade')) {
        throw 'Fenix semantic marker missing.'
    }
    $guardIndex=$fenixOut.IndexOf('if not isHangPressed and not isStuck then return end')
    $playerIndex=$fenixOut.IndexOf('local player = Game.GetPlayer()')
    $heightIndex=$fenixOut.IndexOf('if not IsHighEnough(player) and not isStuck then')
    if($guardIndex -lt 0 -or $playerIndex -lt 0 -or $heightIndex -lt 0 -or
       $guardIndex -gt $playerIndex -or $guardIndex -gt $heightIndex) {
        throw 'Fenix inactive guard was not hoisted before player/raycast work.'
    }
}
finally { $zip.Dispose() }

Write-Host 'Third-stack semantic composition contract passed: OJT + SongsDeck + DynamicOutfitsJudy + FovSentinel + FenixMantisBlade.'
