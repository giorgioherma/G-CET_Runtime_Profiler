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

$root = Join-Path $env:RUNNER_TEMP 'gcet-second-stack-semantic-composition'
if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}

$capture = Join-Path $root 'CET-20990101-020304_SECOND_STACK'
$mods = Join-Path $root 'mods'
$midair = Join-Path $mods 'Alternative Midair Movement'
$metro = Join-Path $mods 'trainSystem'
$metroUi = Join-Path $metro 'modules\ui'
$pizza = Join-Path $mods 'NightCityPizza'
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$midair,$metroUi,$pizza,$zeroDir | Out-Null

# Use the exact fixed runtime so generated-pass composition exercises the real runtime gate.
$encodedInitPath = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
$encodedInit = (Get-Content -LiteralPath $encodedInitPath -Raw).Trim()
$compressedInit = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressedInit)
$gzip = [System.IO.Compression.GZipStream]::new(
    $input,
    [System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes(
        (Join-Path $zeroDir 'init.lua'),
        $output.ToArray())
}
finally {
    $output.Dispose()
    $gzip.Dispose()
    $input.Dispose()
}

# ---------------------------------------------------------------------------
# Alternative Midair Movement: clean/raw source shape, deliberately not using
# generated registrar syntax. Generic AUTO must frame-consolidate it first;
# semantic then composes on the staged result.
# ---------------------------------------------------------------------------
$midairInit = @'
local AltJump = { loaded = true, inFlight = false }
local player = { object = {}, maxRunSet = true, maxSprintSet = true, canWallbounce = false }
local loc = { currentState = gamePSMLocomotionStates.Default, isWallbouncing = false }
local input = require("input")
local config = { lowersprintaccel = { enabled = false, duration = 1.5 } }

function AltJump:new()
    return self
end

registerForEvent ( "onInit" , function()
    Observe ( "PlayerPuppet" , "OnAction" , function( _, action )
        input:SetInputData(action)
    end )

    Observe("PlayerPuppet", "OnLocomotionStateChanged", function()
        if AltJump.loaded then
            loc:SetLocomotionStates(player.object)
        end
    end)
end)

registerForEvent ( "onUpdate" , function( delta )
        if AltJump.loaded then
            AltJump:SetTimers(delta)
            loc:SetDetailedLocomotionStates(player.object, input)
            if not player.maxRunSet or not player.maxSprintSet then
                player:SetMaxSpeeds(loc, 1, 2, 3, 4)
            end
            AltJump:TransitionProcessor()
            if loc:IsJumpingOrDoubleJumpingOrFalling() then
                AltJump:MidairMovementProcessor(delta)
            end
        end
end )

function AltJump:SetTimers(delta) end
function AltJump:TransitionProcessor() end
function AltJump:MidairMovementProcessor(delta) end
'@
$midairInitPath = Join-Path $midair 'init.lua'
$midairInit | Set-Content -LiteralPath $midairInitPath -Encoding utf8

$midairInput = @'
local Input = {}

function Input:SetInputData(action)
    local actionName = Game.NameToString(action:GetName(action))
    self.actionName = actionName
    local actionType = action:GetType(action).value
    local pressed = actionType == "BUTTON_PRESSED"
    local released = actionType == "BUTTON_RELEASED"
    if actionName == "MoveX" then
        self.analogX = action:GetValue(action)
    elseif actionName == "MoveY" then
        self.analogY = action:GetValue(action)
    end
    self.analogRotation = Vector4.ToRotation(Vector4.new(self.analogX, self.analogY, 0, 1))
    if actionName == "Jump" then
        self.jumpPressed = pressed and not released
    elseif actionName == "Left" then
        self.moveLeft = pressed
    elseif actionName == "Right" then
        self.moveRight = pressed
    elseif actionName == "Forward" then
        self.moveForward = pressed
    elseif actionName == "Back" then
        self.moveBack = pressed
    end
end

return Input
'@
$midairInput | Set-Content -LiteralPath (Join-Path $midair 'input.lua') -Encoding utf8

# ---------------------------------------------------------------------------
# Metro / trainSystem: source retains the author's public method shapes.
# Semantic must reduce idle world work, cache one player position per entry
# sweep, and make destination teardown transition-driven.
# ---------------------------------------------------------------------------
$metroInit = @'
local ts = {
    archiveInstalled = true,
    axlInstalled = true,
    cwInstalled = true,
    runtimeData = { inMenu = false, inGame = true, cetOpen = false },
    observers = {
        timeDilation = 1,
        noSave = false,
        noTrains = false,
        radioPopupActive = false,
        update = function() end
    },
    entrySys = { forceRunCron = false, update = function() end },
    stationSys = { currentStation = nil, activeTrain = nil, update = function() end },
    objectSys = { run = function() end },
    Cron = { Update = function() end },
    input = { interactKey = false },
    debug = { baseUI = { utilUI = { update = function() end } } },
    hud = { draw = function() end }
}
local observers = ts.observers

registerForEvent("onUpdate", function(deltaTime)
    if not ts.archiveInstalled or not ts.axlInstalled or not ts.cwInstalled then return end

    if (not ts.runtimeData.inMenu) and ts.runtimeData.inGame and (math.floor(observers.timeDilation) ~= 0) and ts.archiveInstalled and ts.axlInstalled and ts.cwInstalled then
        ts.observers.update()
        ts.entrySys:update()
        ts.stationSys:update(deltaTime)
        ts.objectSys.run()
        ts.Cron.Update(deltaTime)
        ts.input.interactKey = false
        ts.debug.baseUI.utilUI.update()
    elseif ts.entrySys.forceRunCron and ts.archiveInstalled and ts.axlInstalled and ts.cwInstalled then
        ts.Cron.Update(deltaTime)
    elseif ts.stationSys.activeTrain and observers.radioPopupActive then
        ts.stationSys.activeTrain:updateEntity()
    end
end)

registerForEvent("onDraw", function()
    ts.hud.draw(ts)
end)
'@
$metroInit | Set-Content -LiteralPath (Join-Path $metro 'init.lua') -Encoding utf8

$entryDir = Join-Path $metro 'modules'
New-Item -ItemType Directory -Force $entryDir | Out-Null
$entrySource = @'
local entrySys = { entries = {} }
local utils = { distanceVector = function(a,b) return 0 end }

function entrySys:update()
    local closest = self:getClosestEntry()
    if closest then
        local dist = utils.distanceVector(GetPlayer():GetWorldPosition(), closest.center)
        if dist < closest.radius and self:looksAtEntry(closest) then
            self.ts.hud.entryVisible = true
        end
    end
end

function entrySys:looksAtEntry(entry)
    return entry.waypointPosition ~= nil
end

function entrySys:getClosestEntry()
    local closestEntry = nil
    local dist = 999999999999
    for _, v in pairs(self.entries) do
        local x = utils.distanceVector(GetPlayer():GetWorldPosition(), v.center)
        if x < dist then
            dist = x
            closestEntry = v
        end
    end
    return closestEntry
end

return entrySys
'@
$entrySource | Set-Content -LiteralPath (Join-Path $entryDir 'entrySystem.lua') -Encoding utf8

$hudSource = @'
local hud = {
    entryVisible = false,
    exitVisible = false,
    doorVisible = false,
    trainVisible = false,
    destVisible = false,
    nextStationPoint = nil,
    nextStationText = ""
}

function hud.draw(ts)
    hud.drawTrain()
    hud.drawExit()
    hud.drawDoor()
    hud.drawEntry()
    if hud.destVisible then
        hud.drawDestinations(ts.stationSys)
        hud.destVisible = false
    else
        observers.nextStationText = ""
        Game.GetMappinSystem():UnregisterMappin(observers.nextStationPoint)
        if ts.observers.hudText then
            ts.observers.hudText:SetVisible(false)
        end
    end
end

function hud.drawDestinations(stationSystem) end
function hud.drawTrain() end
function hud.drawExit() end
function hud.drawDoor() end
function hud.drawEntry() end

return hud
'@
$hudSource | Set-Content -LiteralPath (Join-Path $metroUi 'hud.lua') -Encoding utf8

# ---------------------------------------------------------------------------
# NightCityPizza: global/off-shift maintenance only. Active ctx runtime remains
# separate and must survive semantic generation unchanged.
# ---------------------------------------------------------------------------
$pizzaInit = @'
local Pizza = {
    disabled = false,
    ctx = nil,
    clockPhoneAcc = 0
}
local GIG_ID = "night_city_pizza"
local GigCore = { Bus = { requestClockIn = function(id) end } }

local function tickBossTexts(delta)
    Pizza.bossTimer = (Pizza.bossTimer or 0) + delta
end

local function phoneBridge()
    return {
        TakeClockIn = function() return false end,
        SetClockedIn = function(value) end
    }
end

local function onActivate(ctx)
    Pizza.ctx = ctx
    ctx:onUpdate(function(dt)
        Pizza.activeDeliveryDelta = dt
    end)
end

registerForEvent("onUpdate", function(delta)
  if Pizza.disabled then return end
  tickBossTexts(delta)
  if Pizza.ctx then return end
  Pizza.clockPhoneAcc = (Pizza.clockPhoneAcc or 0) + (delta or 0)
  if Pizza.clockPhoneAcc >= 0.4 then
    Pizza.clockPhoneAcc = 0
    local bridge = phoneBridge()
    if bridge then
      local ok, want = pcall(function() return bridge:TakeClockIn() end)
      if ok and want and GigCore then
        pcall(function() GigCore.Bus.requestClockIn(GIG_ID) end)
      else
        pcall(function() bridge:SetClockedIn(false) end)
      end
    end
  end
end)
'@
$pizzaInit | Set-Content -LiteralPath (Join-Path $pizza 'init.lua') -Encoding utf8

function Find-CallbackRange(
    [string]$Text,
    [string]$OpeningPattern
) {
    $lines = @($Text -split "\r?\n")
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $OpeningPattern) {
            $start = $i
            break
        }
    }
    if ($start -lt 0) { throw "Opening not found: $OpeningPattern" }

    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\s*end\s*\)\s*$') {
            return @{ Start = $start + 1; End = $i + 1 }
        }
    }
    throw "Callback closing not found after: $OpeningPattern"
}

$midairActionRange = Find-CallbackRange $midairInit 'Observe\s*\(\s*"PlayerPuppet"\s*,\s*"OnAction"'
$midairUpdateRange = Find-CallbackRange $midairInit 'registerForEvent\s*\(\s*"onUpdate"'
$metroUpdateRange = Find-CallbackRange $metroInit 'registerForEvent\s*\(\s*"onUpdate"'
$metroDrawRange = Find-CallbackRange $metroInit 'registerForEvent\s*\(\s*"onDraw"'
$pizzaUpdateRange = Find-CallbackRange $pizzaInit 'registerForEvent\s*\(\s*"onUpdate"'

function CallbackRow(
    [int]$Id,
    [string]$Owner,
    [string]$Kind,
    [string]$Target,
    [string]$File,
    [int]$LineStart,
    [int]$LineEnd,
    [double]$Ms,
    [double]$Calls = 60.0
) {
    @{
        registrationId = $Id
        owner = $Owner
        infrastructure = $false
        kind = $Kind
        target = $Target
        source = @{ file = $File; lineStart = $LineStart; lineEnd = $LineEnd }
        callsPerSecond = $Calls
        exclusiveMsPerSecond = $Ms
        globalWorkSharePct = [math]::Max(1.1, $Ms / 2.0)
        familyWorkSharePct = 50.0
        avgExclusiveUs = [math]::Max(1.0, $Ms * 1000.0 / [math]::Max(1.0, $Calls))
        maxExclusiveMs = 2.0
        spikeCount = 0
        maxSpikeExclusiveMs = 0
    }
}

@{
    schemaVersion = '1.8'
    callbacks = @(
        (CallbackRow 1200 'Alternative Midair Movement' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' $midairActionRange.Start $midairActionRange.End 30.4 1400),
        (CallbackRow 1201 'Alternative Midair Movement' 'event' 'onUpdate' 'init.lua' $midairUpdateRange.Start $midairUpdateRange.End 11.4 60),
        (CallbackRow 1300 'trainSystem' 'event' 'onUpdate' 'init.lua' $metroUpdateRange.Start $metroUpdateRange.End 13.0 60),
        (CallbackRow 1301 'trainSystem' 'event' 'onDraw' 'init.lua' $metroDrawRange.Start $metroDrawRange.End 9.3 60),
        (CallbackRow 1400 'NightCityPizza' 'event' 'onUpdate' 'init.lua' $pizzaUpdateRange.Start $pizzaUpdateRange.End 6.6 60)
    )
    optimizerEvidence = @()
} | ConvertTo-Json -Depth 30 |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok) { throw 'Second-stack semantic Resolver/generator composition run failed.' }
if ($null -eq $resolved.pass) { throw 'Second-stack semantic composition returned no generated pass.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json

$expectations = @(
    @{ Owner='Alternative Midair Movement'; Rule='alternative-midair-movement' },
    @{ Owner='trainSystem'; Rule='metro-system' },
    @{ Owner='NightCityPizza'; Rule='nightcitypizza' }
)
foreach ($expectation in $expectations) {
    $rows = @(
        $resolver.callbackFamilies.topConsumers |
        Where-Object {
            $_.owner -eq $expectation.Owner -and
            $_.semantic.RuleId -eq $expectation.Rule
        }
    )
    if ($rows.Count -lt 1) {
        throw "Semantic rule did not match measured owner: $($expectation.Rule)"
    }
    if (@($rows | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
        throw "Semantic source proof failed for: $($expectation.Rule)"
    }
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$targetOwners = @('Alternative Midair Movement','trainSystem','NightCityPizza')
$targetSkips = @(
    $manifest.skipped |
    Where-Object {
        ([string]$_.Owner -in $targetOwners) -or
        ([string]$_.owner -in $targetOwners)
    }
)
if ($targetSkips.Count -ne 0) {
    throw "Second-stack semantic composition unexpectedly skipped: $($targetSkips.reason -join '; ')"
}

foreach ($rule in @('alternative-midair-movement','metro-system','nightcitypizza')) {
    $semanticTransforms = @(
        $manifest.transforms |
        Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule }
    )
    if ($semanticTransforms.Count -ne 1) {
        throw "Expected exactly one semantic transform for $rule, got $($semanticTransforms.Count)."
    }
}

$frameTransforms = @(
    $manifest.transforms |
    Where-Object { $_.type -eq 'FRAME_DISPATCH_CONSOLIDATION' }
)
foreach ($owner in @('Alternative Midair Movement','trainSystem','NightCityPizza')) {
    if (@($frameTransforms | Where-Object { $_.owner -eq $owner }).Count -lt 1) {
        throw "Generic frame consolidation did not compose before semantic injection for $owner."
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$EntryName) {
        $entry = $zip.GetEntry($EntryName)
        if ($null -eq $entry) { throw "ZIP entry not found: $EntryName" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $base = 'bin/x64/plugins/cyber_engine_tweaks/mods/'

    $midairOut = Read-ZipText ($base + 'Alternative Midair Movement/init.lua')
    $midairInputOut = Read-ZipText ($base + 'Alternative Midair Movement/input.lua')
    foreach ($text in @($midairOut,$midairInputOut)) {
        if ($text -notmatch [regex]::Escape('G-CET semantic:alternative-midair-movement')) {
            throw 'Alternative Midair Movement semantic marker did not cover both changed files.'
        }
    }
    if ($midairOut -notmatch 'MakeEventRegistrar' -or
        $midairOut -notmatch 'G-CET\.Semantic\.AlternativeMidairMovement' -or
        $midairOut -notmatch '__gcetMidairWakeTail' -or
        $midairOut -notmatch '__gcetMidairIdleElapsed' -or
        $midairOut -notmatch 'OnLocomotionStateChanged') {
        throw 'Alternative Midair Movement generic + semantic composition is incomplete.'
    }
    foreach ($action in @('MoveX','MoveY','Jump','Left','Right','Forward','Back')) {
        if ($midairOut -notmatch [regex]::Escape('"' + $action + '"')) {
            throw "Alternative Midair Movement routed action missing: $action"
        }
    }
    if ($midairInputOut -notmatch 'SetInputData\(action,\s*__gcetRoutedName\)' -or
        $midairInputOut -notmatch 'if actionName == "MoveX" or actionName == "MoveY" then' -or
        $midairInputOut -notmatch '__gcetRoutedName or Game\.NameToString') {
        throw 'Alternative Midair Movement input prefilter/rotation rewrite is incomplete.'
    }

    $metroOut = Read-ZipText ($base + 'trainSystem/init.lua')
    $metroEntryOut = Read-ZipText ($base + 'trainSystem/modules/entrySystem.lua')
    $metroHudOut = Read-ZipText ($base + 'trainSystem/modules/ui/hud.lua')
    foreach ($text in @($metroOut,$metroEntryOut,$metroHudOut)) {
        if ($text -notmatch [regex]::Escape('G-CET semantic:metro-system')) {
            throw 'Metro semantic marker did not cover all three changed files.'
        }
    }
    if ($metroOut -notmatch 'MakeEventRegistrar' -or
        $metroOut -notmatch '__gcetMetroIdleElapsed' -or
        $metroOut -notmatch '< 0\.20 then return' -or
        $metroOut -notmatch 'ts\.stationSys:update\(deltaTime\)') {
        throw 'Metro active/idle cadence did not compose with generic frame dispatch.'
    }
    if ($metroEntryOut -notmatch '__gcetPlayerPos' -or
        $metroEntryOut -match 'distanceVector\(GetPlayer\(\):GetWorldPosition\(\),\s*v\.center\)') {
        throw 'Metro entry sweep did not cache one player position.'
    }
    if ($metroHudOut -notmatch 'destinationWasVisible' -or
        $metroHudOut -notmatch 'observers\.nextStationPoint = nil' -or
        $metroHudOut -notmatch 'UnregisterMappin\(observers\.nextStationPoint\)') {
        throw 'Metro destination cleanup was not converted to transition-driven teardown.'
    }

    $pizzaOut = Read-ZipText ($base + 'NightCityPizza/init.lua')
    if ($pizzaOut -notmatch [regex]::Escape('G-CET semantic:nightcitypizza') -or
        $pizzaOut -notmatch 'MakeEventRegistrar' -or
        $pizzaOut -notmatch '__gcetPizzaMaintenanceElapsed' -or
        $pizzaOut -notmatch '< 0\.40 then return' -or
        $pizzaOut -notmatch 'tickBossTexts\(__gcetPizzaElapsed\)' -or
        $pizzaOut -notmatch 'if Pizza\.ctx then return end' -or
        $pizzaOut -notmatch 'ctx:onUpdate\(function\(dt\)') {
        throw 'NightCityPizza off-shift semantic cadence did not preserve the active ctx runtime.'
    }
}
finally {
    $zip.Dispose()
}

Write-Host 'Second-stack semantic composition contract passed: Midair + Metro + Pizza compose after generic AUTO and retain fail-closed structural proof.'
