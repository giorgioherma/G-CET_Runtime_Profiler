param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)
$ErrorActionPreference='Stop'
$ResolverRoot=(Resolve-Path $ResolverRoot).Path
$resolverExe=Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if(!(Test-Path $resolverExe)){throw "Resolver missing: $resolverExe"}

$root=Join-Path $env:RUNNER_TEMP 'gcet-joytoys-semantic'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$capture=Join-Path $root 'capture'
$mods=Join-Path $root 'mods'
$joy=Join-Path $mods 'JoytoysOfNightCity'
New-Item -ItemType Directory -Force $capture,$joy | Out-Null

@'
local Joytoys={
 _lastRequestSequence=0,_lastStartRequest=0,_lastCompleteRequest=0,_readyWritten=false,
 _nifIdleTimer=2.0,_sceneFactTimer=0.0
}
local Shift={stage=0}
local FACT={}
local function questSystem() return {} end
local function writeReadyState(quests) end
local function processLocationRequest(quests) end
local function processJobRequests(quests)
 local startSequence=0
 if startSequence > Joytoys._lastStartRequest then
  Joytoys._lastStartRequest=startSequence
  ensureOffer()
 end
end
local function updateImmersiveProof(dt) end
local function writeSceneDirectorFacts(status) end
local function ensureOffer() end
local function setNifProject(state) end
Joytoys.NPCScenes={update=function() end}
Joytoys.SceneDirector={update=function() end,status=function() return {} end}
registerForEvent("onUpdate", function(dt)
  local quests = questSystem()
  if not quests then return end
  writeReadyState(quests)
  processLocationRequest(quests)
  processJobRequests(quests)
  updateImmersiveProof(dt)
  if Joytoys.NPCScenes then Joytoys.NPCScenes.update(dt) end
  if Joytoys.SceneDirector then Joytoys.SceneDirector.update(dt) end
  Joytoys._sceneFactTimer = Joytoys._sceneFactTimer + (tonumber(dt) or 0.0)
  if Joytoys.SceneDirector and Joytoys._sceneFactTimer >= 0.5 then
    Joytoys._sceneFactTimer = 0.0
    writeSceneDirectorFacts(Joytoys.SceneDirector.status())
  end

  if Shift.stage == 0 then
    ensureOffer()
    Joytoys._nifIdleTimer = Joytoys._nifIdleTimer + (tonumber(dt) or 0.0)
    if Joytoys._nifIdleTimer >= 2.0 then
      Joytoys._nifIdleTimer = 0.0
      setNifProject(false)
    end
  end
end)
return Joytoys
'@ | Set-Content -LiteralPath (Join-Path $joy 'init.lua') -Encoding utf8

@'
local M={}
local function create()
  local controller={provider={},activeRequest=nil}
  local function callProvider(method,...) return false end
  local function isRunning()
    if not controller.provider then return false end
    local running=callProvider("isRunning")
    if not running then controller.activeRequest=nil end
    return running
  end
  function controller.update(deltaTime)
    local api = controller.provider and controller.provider.api or nil
    if api and type(api.update) == "function" then pcall(api.update, tonumber(deltaTime) or 0.0) end
    isRunning()
  end
  return controller
end
return M
'@ | Set-Content -LiteralPath (Join-Path $joy 'npc_scenes.lua') -Encoding utf8

$line=1
$lines=@(Get-Content (Join-Path $joy 'init.lua'))
for($i=0;$i -lt $lines.Count;$i++){if($lines[$i] -match 'registerForEvent\("onUpdate"'){ $line=$i+1;break }}

$handoff=@{
 schemaVersion='2.0'
 callbacks=@(@{
  registrationId=565;owner='JoytoysOfNightCity';infrastructure=$false;kind='event';target='onUpdate'
  source=@{file='init.lua';lineStart=$line;lineEnd=$line+24}
  callsPerSecond=60.09;exclusiveMsPerSecond=8.735067;globalWorkSharePct=5;familyWorkSharePct=100
  avgExclusiveUs=145.37;maxExclusiveMs=133.0526;spikeCount=3;maxSpikeExclusiveMs=133.0526
  burst=@{stutterMaterial=$true;periodicStutter=$false;burstHot=$true;catastrophicBurst=$true}
 })
 optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved=(& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if(!$resolved.ok -or $null -eq $resolved.pass){throw 'Joytoys semantic pass generation failed.'}
$result=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$hit=@($result.callbackFamilies|ForEach-Object {$_.topConsumers}|Where-Object {$_.owner -eq 'JoytoysOfNightCity'})[0]
if($null -eq $hit -or $hit.semantic.RuleId -ne 'joytoys-of-night-city-bridge-active-split' -or
   -not $hit.semantic.SourceProofSatisfied -or -not $hit.semantic.generationReady){
 throw 'Joytoys semantic rule was not source-proven/generation-ready.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try{
 function Read-Zip([string]$name){
  $e=$zip.GetEntry($name);if($null -eq $e){throw "Missing ZIP entry: $name"}
  $r=[IO.StreamReader]::new($e.Open());try{return $r.ReadToEnd()}finally{$r.Dispose()}
 }
 $base='bin/x64/plugins/cyber_engine_tweaks/mods/JoytoysOfNightCity/'
 $init=Read-Zip ($base+'init.lua')
 $scenes=Read-Zip ($base+'npc_scenes.lua')
 $required=@(
  'G-CET semantic:joytoys-of-night-city-bridge-active-split',
  '_bridgePollTimer = 0.10',
  '_offerPollTimer = 0.50',
  'if Joytoys._bridgePollTimer >= 0.10 then',
  'processLocationRequest(quests)',
  'processJobRequests(quests)',
  'updateImmersiveProof(dt)',
  'if Joytoys.NPCScenes then Joytoys.NPCScenes.update(dt) end',
  'if Joytoys.SceneDirector then Joytoys.SceneDirector.update(dt) end',
  'Joytoys._sceneFactTimer >= 0.5',
  'Joytoys._nifIdleTimer >= 2.0',
  'if Joytoys._offerPollTimer >= 0.50 then'
 )
 foreach($token in $required){if(-not $init.Contains($token)){throw "Joytoys init missing: $token"}}
 if(-not $scenes.Contains('if not controller.activeRequest then return end')){
  throw 'Joytoys NPCScenes inactive-request dormancy missing.'
 }
 $bridge=$init.IndexOf('if Joytoys._bridgePollTimer >= 0.10 then')
 $immersive=$init.IndexOf('updateImmersiveProof(dt)')
 if($bridge -lt 0 -or $immersive -lt 0 -or $immersive -lt $bridge){throw 'Joytoys active runtime ordering changed unexpectedly.'}
}
finally{$zip.Dispose()}

Write-Host 'Joytoys bridge sentinel + active-scene split contract passed.'
