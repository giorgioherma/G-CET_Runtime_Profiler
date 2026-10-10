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

$zeroDir=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed=[Convert]::FromBase64String($encodedInit)
$input=[IO.MemoryStream]::new($compressed)
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try{$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())}
finally{$output.Dispose();$gzip.Dispose();$input.Dispose()}

@'
local Joytoys = {
  _lastRequestSequence = 0,
  _lastStartRequest = 0,
  _lastCompleteRequest = 0,
  _readyWritten = false,
  _nifIdleTimer = 2.0,
  _sceneFactTimer = 0.0,
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

@'
local M={}
local STATE_CODE={idle=0,running=2,failed=-1}
local OUTCOME_CODE={none=0,stopped=1,failed=-1}
function M.new()
  local director={
    session=nil, lastSession=nil, phase="idle",
    dependencies={runtime={status=function() return {cameraIndex=0,phase="idle"} end}}
  }
  local function statusSnapshot()
    local session = director.session
    local runtime = director.dependencies and director.dependencies.runtime or nil
    local runtimeStatus = runtime and runtime.status() or { phase = "unavailable" }
    return {
      stateCode=STATE_CODE[director.phase] or -1,
      sessionSequence=session and session.sequence or 0,
      definitionId=session and session.definitionId or 0,
      profileId=session and session.profileId or 0,
      locationId=session and session.locationId or 0,
      cameraIndex=runtimeStatus.cameraIndex or 0,
      outcomeCode=OUTCOME_CODE[session and session.outcome or (director.lastSession and director.lastSession.outcome) or "none"] or 0,
    }
  end
  function director.status()
    return statusSnapshot()
  end
  return director
end
return M
'@ | Set-Content -LiteralPath (Join-Path $joy 'scene_director.lua') -Encoding utf8

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
if($null -eq $hit -or $hit.semantic.RuleId -ne 'joytoys-of-night-city-bridge-active-split-idle-facts-v2' -or
   -not $hit.semantic.SourceProofSatisfied -or -not $hit.semantic.generationReady){
 throw 'Joytoys semantic rule was not source-proven/generation-ready.'
}

$manifest=Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$transform=@($manifest.transforms | Where-Object {
 $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'joytoys-of-night-city-bridge-active-split-idle-facts-v2'
}) | Select-Object -First 1
if($null -eq $transform){throw 'Joytoys semantic transform missing from manifest.'}
$files=@($transform.files)
if($files.Count -ne 3){throw "Joytoys semantic v2 must change exactly 3 files; got $($files.Count): $($files -join ', ')"}
$initRel=@($files | Where-Object { $_.Replace('\','/') -match '(?i)(^|/)init\.lua$' }) | Select-Object -First 1
$scenesRel=@($files | Where-Object { $_.Replace('\','/') -match '(?i)(^|/)npc_scenes\.lua$' }) | Select-Object -First 1
$directorRel=@($files | Where-Object { $_.Replace('\','/') -match '(?i)(^|/)scene_director\.lua$' }) | Select-Object -First 1
if(!$initRel -or !$scenesRel -or !$directorRel){throw "Joytoys v2 manifest missing one of three files: $($files -join ', ')"}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try{
 function Read-ZipRelative([string]$relative){
  $normalized=$relative.Replace('\','/')
  $entry=@($zip.Entries | Where-Object {
    $_.FullName.Replace('\','/').EndsWith($normalized,[StringComparison]::OrdinalIgnoreCase)
  }) | Select-Object -First 1
  if($null -eq $entry){throw "Missing ZIP entry ending with: $normalized"}
  $reader=[IO.StreamReader]::new($entry.Open())
  try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
 }
 $init=Read-ZipRelative $initRel
 $scenes=Read-ZipRelative $scenesRel
 $director=Read-ZipRelative $directorRel
 $required=@(
  'G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2',
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
  'writeSceneDirectorFacts(Joytoys.SceneDirector.factStatus())',
  'if Joytoys._offerPollTimer >= 0.50 then'
 )
 foreach($token in $required){if(-not $init.Contains($token)){throw "Joytoys init missing: $token"}}
 if(-not $scenes.Contains('G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2') -or
    -not $scenes.Contains('if not controller.activeRequest then return end')){
  throw 'Joytoys NPCScenes inactive-request dormancy missing.'
 }
 if(-not $director.Contains('G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2') -or
    -not $director.Contains('function director.factStatus()') -or
    -not $director.Contains('if director.session then return statusSnapshot() end') -or
    -not $director.Contains('function director.status()') -or
    -not $director.Contains('return statusSnapshot()')) {
  throw 'Joytoys fact-only mirror must preserve public status and active scenes.'
 }
 foreach($field in @('stateCode','sessionSequence','definitionId','profileId','locationId','cameraIndex','outcomeCode')) {
   if(-not $director.Contains($field)) {throw "Joytoys fact-only mirror omitted $field"}
 }
 $bridge=$init.IndexOf('if Joytoys._bridgePollTimer >= 0.10 then')
 $immersive=$init.IndexOf('  updateImmersiveProof(dt)', [Math]::Max(0,$bridge))
 if($bridge -lt 0 -or $immersive -lt 0 -or $immersive -lt $bridge){throw 'Joytoys active runtime ordering changed unexpectedly.'}
}
finally{$zip.Dispose()}

# A previously G-CET-v1-transformed tree must upgrade safely without
# duplicating the bridge or the NPC scene active-request gate.
$legacyInit=$init.Replace(
 'G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2',
 'G-CET semantic:joytoys-of-night-city-bridge-active-split'
).Replace('Joytoys.SceneDirector.factStatus()', 'Joytoys.SceneDirector.status()')
$legacyScenes=$scenes.Replace(
 'G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2',
 'G-CET semantic:joytoys-of-night-city-bridge-active-split'
)
$legacyInit | Set-Content -LiteralPath (Join-Path $joy 'init.lua') -Encoding utf8
$legacyScenes | Set-Content -LiteralPath (Join-Path $joy 'npc_scenes.lua') -Encoding utf8
@'
local M={}
local STATE_CODE={idle=0,running=2,failed=-1}
local OUTCOME_CODE={none=0,stopped=1,failed=-1}
function M.new()
  local director={
    session=nil, lastSession=nil, phase="idle",
    dependencies={runtime={status=function() return {cameraIndex=0,phase="idle"} end}}
  }
  local function statusSnapshot()
    local session = director.session
    local runtime = director.dependencies and director.dependencies.runtime or nil
    local runtimeStatus = runtime and runtime.status() or { phase = "unavailable" }
    return {
      stateCode=STATE_CODE[director.phase] or -1,
      sessionSequence=session and session.sequence or 0,
      definitionId=session and session.definitionId or 0,
      profileId=session and session.profileId or 0,
      locationId=session and session.locationId or 0,
      cameraIndex=runtimeStatus.cameraIndex or 0,
      outcomeCode=OUTCOME_CODE[session and session.outcome or (director.lastSession and director.lastSession.outcome) or "none"] or 0,
    }
  end
  function director.status()
    return statusSnapshot()
  end
  return director
end
return M
'@ | Set-Content -LiteralPath (Join-Path $joy 'scene_director.lua') -Encoding utf8
$upgrade=(& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if(!$upgrade.ok -or !$upgrade.pass){throw 'Joytoys v1-to-v2 upgrade generation failed.'}
$upgradeManifest=Get-Content -LiteralPath $upgrade.pass.ManifestPath -Raw | ConvertFrom-Json
$up=@($upgradeManifest.transforms | Where-Object { $_.RuleId -eq 'joytoys-of-night-city-bridge-active-split-idle-facts-v2' })
if($up.Count -ne 1 -or @($up[0].files).Count -ne 3) {
 throw 'Joytoys v1 source must generate one complete three-file v2 upgrade.'
}
$upgradeZip=[IO.Compression.ZipFile]::OpenRead([string]$upgrade.pass.ZipPath)
try {
 foreach($relative in @('init.lua','npc_scenes.lua','scene_director.lua')) {
  $entry=@($upgradeZip.Entries | Where-Object {
    $_.FullName.EndsWith('JoytoysOfNightCity/'+$relative,[StringComparison]::OrdinalIgnoreCase)
  }) | Select-Object -First 1
  if(!$entry){throw "Joytoys upgrade ZIP missing $relative"}
  $reader=[IO.StreamReader]::new($entry.Open())
  try{$contents=$reader.ReadToEnd()}finally{$reader.Dispose()}
  if(-not $contents.Contains('G-CET semantic:joytoys-of-night-city-bridge-active-split-idle-facts-v2')) {
   throw "Joytoys v2 marker missing in $relative"
  }
  $contents | Set-Content -LiteralPath (Join-Path $joy $relative) -Encoding utf8
 }
}finally{$upgradeZip.Dispose()}
$repeat=(& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
$repeatReport=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$repeatMatches=@($repeatReport.callbackFamilies | ForEach-Object {$_.topConsumers} |
 Where-Object {$_.owner -eq 'JoytoysOfNightCity' -and
               $_.semantic.RuleId -eq 'joytoys-of-night-city-bridge-active-split-idle-facts-v2'})
if($repeatMatches.Count -lt 1 -or
   @($repeatMatches | Where-Object {$_.semantic.AlreadySatisfied}).Count -lt 1) {
 throw 'Joytoys v2 does not report already-satisfied source on repeat.'
}
if($repeat.ok -and $repeat.pass) {
 $repeatManifest=Get-Content -LiteralPath $repeat.pass.ManifestPath -Raw | ConvertFrom-Json
 if(@($repeatManifest.transforms | Where-Object { $_.RuleId -eq 'joytoys-of-night-city-bridge-active-split-idle-facts-v2' }).Count -gt 0) {
  throw 'Joytoys fully-upgraded v2 was applied twice.'
 }
} else {
 # The CLI returns a nonzero no-op pass when the live tree is fully satisfied.
 # The independently inspected resolver report above is the idempotence proof:
 # v2 is already satisfied and no additional pass can be emitted.
 if($repeat.pass) {throw 'Joytoys repeat returned an unexpected pass.'}
}

# A fully-satisfied --generate-pass CLI intentionally returns exit 1 with no ZIP.
# We verified its source proof and no-op result; do not propagate that expected
# native exit code as the PowerShell test step's final status.
$global:LASTEXITCODE = 0
Write-Host 'Joytoys v2: original, v1 upgrade, idempotence contracts passed.'
