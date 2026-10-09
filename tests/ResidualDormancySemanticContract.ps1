param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "Resolver missing: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-residual-dormancy-semantic'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-090909_RESIDUAL_DORMANCY'
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
function Find-Line([string]$Path,[string]$Pattern) {
    $lines=@(Get-Content -LiteralPath $Path)
    for($i=0;$i -lt $lines.Count;$i++) { if($lines[$i] -match $Pattern) { return $i+1 } }
    throw "Pattern not found: $Pattern in $Path"
}
function CallbackRow([int]$Id,[string]$Owner,[string]$Target,[string]$File,[int]$Line,[double]$Ms) {
    @{
        registrationId=$Id; owner=$Owner; infrastructure=$false; kind='event'; target=$Target
        source=@{file=$File;lineStart=$Line;lineEnd=$Line+120}
        callsPerSecond=60; exclusiveMsPerSecond=$Ms; globalWorkSharePct=2.5; familyWorkSharePct=100
        avgExclusiveUs=100; maxExclusiveMs=1; spikeCount=0; maxSpikeExclusiveMs=0
    }
}

Write-ModFile 'Dedrajudygoonadate' 'init.lua' @'
local ui = require("modules/interactionUI")
local JudyDateSMS = {}
function JudyDateSMS:onInit() end
local function registerJudyDebugHotkeys(self) end
registerJudyDebugHotkeys(JudyDateSMS)
registerForEvent("onInit", function() JudyDateSMS:onInit() end)
registerForEvent("onDraw", function()
  if ui and ui.update then ui.update() end
end)
'@
Write-ModFile 'Dedrajudygoonadate' 'modules/interactionUI.lua' @'
local ui = { hubShown=false, customHubSelected=false, input=false, hub={id=1}, selectedIndex=0 }
local function getDialogChoiceHubs() return {} end
local function getActiveChoiceHubID() return 0 end
local function updateSelectedHub(_) end
local function updateSelectedIndex(_) end
function ui.showHub() ui.hubShown = true end
function ui.hideHub() ui.hubShown = false end
function ui.update()
  local hubs = getDialogChoiceHubs()
  if ui.hubShown and ui.customHubSelected and #hubs == 0 then
    updateSelectedHub(ui.hub.id)
    updateSelectedIndex(ui.selectedIndex)
  elseif ui.hubShown then
    ui.customHubSelected = getActiveChoiceHubID() == ui.hub.id
  end
  ui.input = false
end
return ui
'@

Write-ModFile 'FPVDrone' 'fpv/bindings.lua' @'
registerForEvent("onUpdate", function(deltaTime)
    updateFPVController(deltaTime)
    if updateFPVDroneDeathTransitionGuard() then
        updateIncrementalGarbageCollection(deltaTime)
        return
    end
    updatePendingDroneDespawn(deltaTime)
    updatePlayerStreamingProtectionRelease(deltaTime)
    if not droneEnabled and not droneViewActive and playerProtection.active and playerProtectionReleaseTimer <= 0.0 then
        schedulePlayerStreamingProtectionRelease(true)
    end
    maintainDroneCameraAcrossMenus()
    if droneEnabled then updateDrone(deltaTime) end
    FPVDroneAutoSaveGuard.Update(deltaTime)
    updatePlayerCameraRestore(deltaTime)
    FPVDroneFlightTimer.Update(deltaTime)
    updateDroneHUD(deltaTime, false)
    updateDroneAudio(deltaTime)
    updateIncrementalGarbageCollection(deltaTime)
end)
'@
Write-ModFile 'FPVDrone' 'fpv/state.lua' @'
droneEnabled=false
droneViewActive=false
pendingDroneViewEntry=false
pendingDroneDespawnID=nil
playerProtection={active=false}
playerProtectionReleaseTimer=0.0
playerCameraRestoreTimer=0.0
droneViewSaveLockActive=false
droneAudioStarted=false
FPVDroneAutoSaveGuard={desiredLocked=false,previousValueCaptured=false,Update=function() end}
FPVDroneSignalNoiseState={visualResetPending=false}
FPVDroneFlightTimer={Update=function() end}
'@
Write-ModFile 'FPVDrone' 'fpv/lifecycle.lua' @'
function toggleDrone()
  droneEnabled = not droneEnabled
end
'@

Write-ModFile 'AerialRace' 'init.lua' @'
local Cron={Update=function() end}
local settings={usePlatforms=false}
local checkpointsSpawned=false
local startTime=0
local nc1=require("nc1")
local function updatePlatforms() end
registerForEvent("onUpdate", function(dt)
    Cron.Update(dt)

    if settings.usePlatforms then
        updatePlatforms()
    end

    if checkpointsSpawned == false and Game.GetSimTime():ToFloat()-startTime > 90 then
        local player = Game.GetPlayer()
        if player then
            checkpointsSpawned = true
            nc1.SpawnCheckpoints()
        end
    end

    if checkpointsSpawned == true then
        nc1.Update()
    end
end)
'@
Write-ModFile 'AerialRace' 'nc1.lua' @'
local nc1={raceActive=false,resetTimeTarget=nil}
function nc1.Update() end
function nc1.SpawnCheckpoints() end
function nc1.ResetRace()
  nc1.raceActive=false
  nc1.resetTimeTarget=nil
end
return nc1
'@

$dedraLine=Find-Line (Join-Path $mods 'Dedrajudygoonadate\init.lua') 'registerForEvent\("onDraw"'
$fpvLine=Find-Line (Join-Path $mods 'FPVDrone\fpv\bindings.lua') 'registerForEvent\("onUpdate"'
$aerialLine=Find-Line (Join-Path $mods 'AerialRace\init.lua') 'registerForEvent\("onUpdate"'

$handoff=@{
  schemaVersion='1.8'
  callbacks=@(
    (CallbackRow 9101 'Dedrajudygoonadate' 'onDraw' 'init.lua' $dedraLine 5.0),
    (CallbackRow 9102 'FPVDrone' 'onUpdate' 'fpv/bindings.lua' $fpvLine 4.0),
    (CallbackRow 9103 'AerialRace' 'onUpdate' 'init.lua' $aerialLine 4.0)
  )
  optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Residual dormancy semantic pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$expected=@('fpv-drone-idle-dormancy','aerial-race-active-idle-split')
foreach($rule in $expected) {
  $hit=@($resolver.callbackFamilies | ForEach-Object {$_.topConsumers} | Where-Object {$_.semantic.RuleId -eq $rule})[0]
  if($null -eq $hit -or -not $hit.semantic.generationReady) { throw "Semantic rule not generation-ready: $rule" }
}

$dedra=@($resolver.callbackFamilies |
  ForEach-Object {$_.topConsumers} |
  Where-Object {$_.owner -eq 'Dedrajudygoonadate' -and $_.target -eq 'onDraw'})[0]
if($null -eq $dedra -or
   @($dedra.generic.RecipeFamilies | Where-Object {$_ -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD'}).Count -ne 1) {
  throw 'Dedra UI helper was not promoted to cross-file generic AUTO.'
}
if($dedra.semantic.Matched) {
  throw 'Dedra still matched a semantic rule after generic promotion.'
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
foreach($rule in $expected) {
  if(@($manifest.transforms | Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule}).Count -ne 1) {
    throw "Generated pass missing semantic rule: $rule"
  }
}
if(@($manifest.transforms | Where-Object {
  $_.type -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD' -and $_.owner -eq 'Dedrajudygoonadate'
}).Count -ne 1) {
  throw 'Generated pass missing promoted Dedra cross-file generic guard.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
  function Read-ZipText([string]$Name) {
    $e=$zip.GetEntry($Name); if($null -eq $e){throw "ZIP entry missing: $Name"}
    $r=[System.IO.StreamReader]::new($e.Open()); try{return $r.ReadToEnd()} finally{$r.Dispose()}
  }
  $base='bin/x64/plugins/cyber_engine_tweaks/mods/'
  $dedra=Read-ZipText ($base+'Dedrajudygoonadate/init.lua')
  if($dedra -notmatch 'ui\.hubShown or ui\.input') { throw 'Dedra hidden UI gate missing.' }

  $fpv=Read-ZipText ($base+'FPVDrone/fpv/bindings.lua')
  if($fpv -notmatch '__gcetFpvIdleElapsed' -or
     $fpv -notmatch 'pendingDroneDespawnID ~= nil' -or
     $fpv -notmatch 'previousValueCaptured == true' -or
     $fpv -notmatch 'visualResetPending == true' -or
     $fpv -notmatch '< 0\.50') {
    throw 'FPV active/idle split or pending-cleanup wake set is incomplete.'
  }

  $aerial=Read-ZipText ($base+'AerialRace/init.lua')
  if($aerial -notmatch '__gcetAerialIdleElapsed' -or $aerial -notmatch '< 0\.20') { throw 'Aerial idle discovery split incomplete.' }
  $aerialCron=$aerial.IndexOf('Cron.Update(dt)')
  $aerialPlatforms=$aerial.IndexOf('updatePlatforms()',$aerialCron)
  $aerialGate=$aerial.IndexOf('__gcetAerialBusy',$aerialCron)
  $aerialDiscovery=$aerial.IndexOf('if checkpointsSpawned == false',$aerialCron)
  if($aerialCron -lt 0 -or $aerialPlatforms -lt 0 -or $aerialGate -lt 0 -or $aerialDiscovery -lt 0 -or
     $aerialCron -gt $aerialPlatforms -or $aerialPlatforms -gt $aerialGate -or $aerialGate -gt $aerialDiscovery) {
    throw 'Aerial lane ordering is wrong: Cron/platforms must stay realtime before the idle race-discovery gate.'
  }
}
finally { $zip.Dispose() }

Write-Host 'Residual dormancy contract passed: Dedra generic AUTO + FPV/Aerial semantic lanes.'
