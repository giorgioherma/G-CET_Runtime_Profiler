param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if (!(Test-Path $exe)) {throw 'Missing Resolver'}
Add-Type -AssemblyName System.IO.Compression.FileSystem

function ZipText($zipPath,$suffix) {
  $z=[IO.Compression.ZipFile]::OpenRead([string]$zipPath)
  try {
    $e=@($z.Entries | Where-Object {$_.FullName.Replace('\','/').EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)}) | Select-Object -First 1
    if (!$e) {throw "Missing generated $suffix"}
    $r=[IO.StreamReader]::new($e.Open())
    try {return $r.ReadToEnd()} finally {$r.Dispose()}
  } finally {$z.Dispose()}
}
function Case($version) {
  $root=Join-Path $env:RUNNER_TEMP ("gcet-gig-specter-"+$version)
  if(Test-Path $root){Remove-Item $root -Recurse -Force}
  $capture=Join-Path $root 'capture'
  $mods=Join-Path $root 'mods'
  $gig=Join-Path $mods 'GigCore'
  $rec=Join-Path $mods 'SpecterM11RecoveryDev'
  New-Item -ItemType Directory -Force $capture,$gig,$rec|Out-Null
  # Provide the pinned live 0-Engine runtime required by Resolver pass generation.
  $zero = Join-Path $mods '0-Engine'
  New-Item -ItemType Directory -Force $zero | Out-Null
  $encoded = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
  $bytes = [Convert]::FromBase64String($encoded)
  $input = [IO.MemoryStream]::new($bytes)
  $gzip = [IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
  $output = [IO.MemoryStream]::new()
  try { $gzip.CopyTo($output); [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray()) }
  finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }


@'
local function _rawMenuOrPhotoPaused()
  local defs = Game.GetAllBlackboardDefs()
  local ui   = Game.GetBlackboardSystem():Get(defs.UI_System)
  if ui and ui:GetBool(defs.UI_System.IsInMenu) then return true end
  local pm = Game.GetBlackboardSystem():Get(defs.PhotoMode)
  if pm and pm:GetBool(defs.PhotoMode.IsActive) then return true end
  return false
end
local function computePauseState() return _rawMenuOrPhotoPaused() end
local author073MapPins = "GigCore-v073-mappins"
local pausedThisFrame = false
local Marker = { tickAll = function() end }
registerForEvent("onUpdate",function(delta)
  do local okPs, errPs = pcall(function() pausedThisFrame = computePauseState() end)
     if not okPs then print("onUpdate GUARD: computePauseState threw: " .. tostring(errPs)) end end
  Marker.tickAll(delta)
  GigCore.tick(delta,pausedThisFrame,author073MapPins)
end)
'@ | Set-Content (Join-Path $gig 'init.lua') -Encoding utf8

  $music=''
  $call=''
  if ($version -eq '1.5') {
    $music='local Music = require("specter_mission_music")'
    $call='  Music.update(delta, phoneBus(), active and {id="recovery_counter_isolation"} or nil, stage, active and "recovery" or "idle", Game.GetPlayer() ~= nil, active)'
@'
local M={}
function M.update(delta,bus,contract,stage,state,playerPresent,arrived)
  local id=contract and contract.id
  return id
end
return M
'@ | Set-Content (Join-Path $rec 'specter_mission_music.lua') -Encoding utf8
  }
  $before=@'
local function metricGet(ns,key,default) return default end
local function refresh() end
local STORY_NS="infinitegigs"
local autoStartClock=0
local owner=nil
local active=false
local stage=0
local function phoneBus() return nil end
local function start() end
local function applyRestore() end
local restorePending=false
local restoreClock=0
'@
  $after=@'
registerForEvent("onUpdate",function(delta)
  if restorePending then
    restoreClock=restoreClock+(delta or 0)
    if restoreClock>=0.90 and Game.GetPlayer() then applyRestore() end
  end
  if not active then
    local seq=metricGet(STORY_NS,"specter_story_sequence",0)
    local rainbowDone=metricGet(STORY_NS,"specter_story_done_specter_rainbow_market_dev",0)
    local recoveryDone=metricGet(STORY_NS,"specter_story_done_recovery_counter_isolation",0)
    if seq==9 and rainbowDone==1 and recoveryDone==0 then
      autoStartClock=autoStartClock+(delta or 0)
      refresh()
      if autoStartClock>=0.75 and owner and owner.state=="idle" and owner.contract==nil and Game.GetPlayer() then
        autoStartClock=0.0
        start()
      end
    else
      autoStartClock=0.0
    end
    return
  end
  local originalFeature="SPECTER-1.5-dialogue-restore"
end)
'@
  $nl=[Environment]::NewLine
  ($music+$nl+$before+$nl+$call+$nl+$after)|Set-Content (Join-Path $rec 'init.lua') -Encoding utf8
  $gPath=Join-Path $gig 'init.lua'
  $sPath=Join-Path $rec 'init.lua'
  $gHash=(Get-FileHash $gPath -Algorithm SHA256).Hash
  $sHash=(Get-FileHash $sPath -Algorithm SHA256).Hash
  $input=@{
    schemaVersion='2.0'
    callbacks=@(
      @{registrationId=510;owner='GigCore';infrastructure=$false;kind='event';target='onUpdate';source=@{file='init.lua';lineStart=12;lineEnd=15};callsPerSecond=60;exclusiveMsPerSecond=9.21;globalWorkSharePct=10;familyWorkSharePct=100;avgExclusiveUs=153.5;maxExclusiveMs=1.84;spikeCount=0},
      @{registrationId=511;owner='SpecterM11RecoveryDev';infrastructure=$false;kind='event';target='onUpdate';source=@{file='init.lua';lineStart=20;lineEnd=42};callsPerSecond=60;exclusiveMsPerSecond=8.18;globalWorkSharePct=9;familyWorkSharePct=100;avgExclusiveUs=136.33;maxExclusiveMs=161.49;spikeCount=1}
    )
    optimizerEvidence=@()
  } | ConvertTo-Json -Depth 30
  $input|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
  $r=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if(!$r.ok -or !$r.pass){throw "Missing source pass for $version"}
  $manifest=Get-Content $r.pass.ManifestPath -Raw|ConvertFrom-Json
  foreach($id in @('gigcore-blackboard-once','specter-recovery-prerequisite-reads')){
    $n=@($manifest.transforms|Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $id}).Count
    if($n -ne 1){$skip=@($manifest.skipped|Where-Object {$_.RuleId -eq $id})|ConvertTo-Json -Depth 8 -Compress;throw "$version $id transforms=$n skips=$skip"}
  }
  $g=ZipText $r.pass.ZipPath 'GigCore/init.lua'
  $s=ZipText $r.pass.ZipPath 'SpecterM11RecoveryDev/init.lua'
  if(!$g.Contains('local function __gcetGigCoreRefreshPausedFrame()') -or !$g.Contains('pcall(__gcetGigCoreRefreshPausedFrame)')){throw 'Missing one-pass GigCore closure reuse'}
  $boardReadOk=$g.Contains('local __gcetGigBoards = Game.GetBlackboardSystem()') -or
               $g.Contains('local __gcetGigBoards = __gcetGetBlackboardSystem()')
  if(!$boardReadOk -or !$g.Contains('__gcetGigBoards:Get(defs.PhotoMode)') -or !$g.Contains('GigCore-v073-mappins')){throw "GigCore change/feature wrong: $version"}
  if(!$s.Contains('local rainbowDone=seq==9 and metricGet(') -or !$s.Contains('local recoveryDone=(seq==9 and rainbowDone==1) and metricGet(') -or !$s.Contains('autoStartClock>=0.75') -or !$s.Contains('SPECTER-1.5-dialogue-restore')){throw "Recovery behavior wrong: $version"}
  if($version -eq '1.5'){
    if(!$s.Contains('local __gcetRecoveryMusicContract = {id="recovery_counter_isolation"}') -or !$s.Contains('active and __gcetRecoveryMusicContract or nil') -or $s.Contains('active and {id="recovery_counter_isolation"} or nil')){throw '1.5 music allocation not removed'}
  }elseif($s.Contains('__gcetRecoveryMusicContract')){throw 'Music added to 1.4 incorrectly'}
  if((Get-FileHash $gPath -Algorithm SHA256).Hash -ne $gHash -or (Get-FileHash $sPath -Algorithm SHA256).Hash -ne $sHash){throw 'Modified live source'}
  $g|Set-Content $gPath -Encoding utf8
  $s|Set-Content $sPath -Encoding utf8
  $r2=(& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
  if(!$r2.ok) {
    if([string]$r2.error -notmatch 'no applicable generic or source-proven semantic transforms'){
      throw "Unexpected Resolver rerun failure $version"
    }
    # An already-complete source correctly generates no second ZIP.
    $global:LASTEXITCODE=0
  }
  if($r2.pass -and $r2.pass.ManifestPath){
    $manifest2=Get-Content $r2.pass.ManifestPath -Raw|ConvertFrom-Json
    if(@($manifest2.transforms|Where-Object {$_.RuleId -in @('gigcore-blackboard-once','specter-recovery-prerequisite-reads')}).Count -gt 0){throw "Non-idempotent $version"}
  }
  Write-Host "GigCore + SPECTER $version source variant and idempotence passed."
}
Case '1.4.1'
Case '1.5'
