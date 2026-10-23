param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path $exe)){throw 'Resolver executable missing'}
Add-Type -AssemblyName System.IO.Compression.FileSystem

function ZipSource([string]$zipPath,[string]$suffix){
  $zip=[IO.Compression.ZipFile]::OpenRead($zipPath)
  try{
    $entry=@($zip.Entries|Where-Object {$_.FullName.Replace('\','/').EndsWith($suffix,[StringComparison]::OrdinalIgnoreCase)})|Select-Object -First 1
    if(!$entry){throw "Output missing $suffix"}
    $reader=[IO.StreamReader]::new($entry.Open())
    try{return $reader.ReadToEnd()}finally{$reader.Dispose()}
  }finally{$zip.Dispose()}
}

$root=Join-Path $env:RUNNER_TEMP 'gcet-itpp-20261016-regression'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$mods=Join-Path $root 'mods'
$capture=Join-Path $root 'capture'
$owner=Join-Path $mods 'immersive_third_person'
$engine=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$owner,$engine|Out-Null
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$raw=[Convert]::FromBase64String($encoded)
$input=[IO.MemoryStream]::new($raw)
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try{$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $engine 'init.lua'),$output.ToArray())}
finally{$output.Dispose();$gzip.Dispose();$input.Dispose()}

# Narrow source-shaped fixture constructed from independently audited author
# function signatures and early returns. NEVER distribute the full mod.
$source=@'
local mod={}
local state={enabled=false,observedActions=false,gameCameraHold=nil,pendingFppCleanup=nil,
 pendingTppRepReassert=nil,pendingMirrorHeadVerify=nil,pendingFacialMute=nil,
 pendingTppAnimPoke=nil,pendingFppRestore=nil}
local config={camera={move360=true}}
local function isPlayerInAnyMenu() return false end
local function getActionName(action) return action:GetName(action).value end
local function getActionValue(action) return action:GetValue(action) end
local function guardStep(name,fn,...) return pcall(fn,...) end
mod.handledActions = {
  mouse_x = true, mouse_y = true, CameraMouseX = true, CameraMouseY = true,
  right_stick_x = true, right_stick_y = true, CameraX = true, CameraY = true,
  left_stick_x = true, left_stick_y = true, MoveX = true, MoveY = true,
  world_map_menu_move_horizontal = true, world_map_menu_move_vertical = true,
  Forward = true, Back = true, Left = true, Right = true,
}
mod.move360CaptureNamed = function(action,actionName,actionValue) end
mod.move360Capture = function(action)
  local actionName = getActionName(action)
  if actionName == "MoveX" or actionName == "MoveY" or actionName == "Forward" or actionName == "Back"
     or actionName == "Left" or actionName == "Right" then
    mod.move360CaptureNamed(action, actionName, getActionValue(action))
  end
end
mod.gameCameraMoveSeen = function(action)
  local name = getActionName(action)
  if name == "Forward" or name == "Back" or name == "Left" or name == "Right" or name == "MoveX" or name == "MoveY"
     or name == "left_stick_x" or name == "left_stick_y" then
    state.gameCameraMoveAt = 1
  end
end
local function setDigitalMove(actionName, actionValue)
  if actionName == "Forward" then return true end
  if actionName == "Back" then return true end
  if actionName == "Left" then return true end
  if actionName == "Right" then return true end
  return false
end
local function handlePlayerAction(action)
  if not state.enabled or isPlayerInAnyMenu() then
    if state.gameCameraHold ~= nil then
      mod.gameCameraMoveSeen(action)
    end
    if config.camera.move360 then
      mod.move360Capture(action)
    end
    return
  end
  local actionName = getActionName(action)
  if not actionName or not mod.handledActions[actionName] then
    return
  end
  local actionValue = getActionValue(action)
  mod.move360CaptureNamed(action, actionName, actionValue)
  if setDigitalMove(actionName, actionValue) then return end
end
local function observePlayerActions()
  if state.observedActions or not Observe then
    return
  end

  local okObs = pcall(function()
    Observe('PlayerPuppet', 'OnAction', function(_, action)
      pcall(handlePlayerAction, action)
    end)
  end)
  if okObs then
    state.observedActions = true
  end
end
local function updatePendingFppCleanup(delta)
  local pending = state.pendingFppCleanup
  if not pending then
    return
  end
end
local function updateTppRepReassert(delta)
  local pending = state.pendingTppRepReassert
  if not pending then
    return
  end
end
local function updateMirrorHeadVerify(delta)
  local pending = state.pendingMirrorHeadVerify
  if not pending then
    return
  end
end
local function updateFacialMute(delta)
  local pending = state.pendingFacialMute
  if not pending then
    return
  end
end
local function updatePendingTppAnimPoke(delta)
  local pending = state.pendingTppAnimPoke
  if not pending then
    return
  end
end
local function updateFppRestoreWatchdog(delta)
  if not state.pendingFppRestore or state.enabled then
    return
  end
end
local function updateAutoPerspective(delta) end
local function updateThirdPersonCamera(delta) end
mod.updateTppMaintenance = function(delta) end
mod.headLookTick = function(delta) end
mod.updateMove360Step = function(delta) end
mod.apiTick = function(delta) end
mod.pluginHeartbeat = function(delta) end
registerForEvent('onUpdate', function(delta)
  mod.pluginHeartbeat(delta)
  guardStep("updateAutoPerspective", updateAutoPerspective, delta)
  guardStep("updateThirdPersonCamera", updateThirdPersonCamera, delta)
  guardStep("updateApi", mod.apiTick, delta)
  guardStep("updateMove360", mod.updateMove360Step, delta)
  guardStep("headLookTick", mod.headLookTick, delta)
  guardStep("updatePendingFppCleanup", updatePendingFppCleanup, delta)
  guardStep("updateTppRepReassert", updateTppRepReassert, delta)
  guardStep("updateMirrorHeadVerify", updateMirrorHeadVerify, delta)
  guardStep("updateFacialMute", updateFacialMute, delta)
  guardStep("updatePendingTppAnimPoke", updatePendingTppAnimPoke, delta)
  guardStep("updateFppRestoreWatchdog", updateFppRestoreWatchdog, delta)
  if state.enabled then
    guardStep("tppMaintenance", mod.updateTppMaintenance, delta)
  end
end)
'@
$sourcePath=Join-Path $owner 'init.lua'
[IO.File]::WriteAllText($sourcePath,$source,[Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $owner 'api.lua'),'return { run = function() end }',[Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $owner 'camera_motion.lua'),'return { preserve = true }',[Text.Encoding]::UTF8)
$hashes=@{}
foreach($p in @('init.lua','api.lua','camera_motion.lua')) {
  $hashes[$p]=(Get-FileHash (Join-Path $owner $p) -Algorithm SHA256).Hash
}
@{
 schemaVersion='2.0'
 callbacks=@(
  @{registrationId=941;owner='immersive_third_person';infrastructure=$false;kind='event';target='onUpdate';source=@{file='init.lua';lineStart=125;lineEnd=151};callsPerSecond=60;exclusiveMsPerSecond=4.5;globalWorkSharePct=1.0;familyWorkSharePct=100;avgExclusiveUs=75;maxExclusiveMs=1.0;spikeCount=0},
  @{registrationId=942;owner='immersive_third_person';infrastructure=$false;kind='Observe';target='PlayerPuppet::OnAction';source=@{file='init.lua';lineStart=62;lineEnd=80};callsPerSecond=1380;exclusiveMsPerSecond=0.4;globalWorkSharePct=0.1;familyWorkSharePct=100;avgExclusiveUs=0.3;maxExclusiveMs=0.2;spikeCount=0}
 )
 optimizerEvidence=@()
}|ConvertTo-Json -Depth 20|Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$result=& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json
if(!$result.ok -or !$result.pass){throw ('Updated ITP generation refused: '+($result|ConvertTo-Json -Compress -Depth 8))}
$manifest=Get-Content $result.pass.ManifestPath -Raw|ConvertFrom-Json
foreach($rule in @('immersive-third-person','immersive-third-person-action-routing')){
 $transforms=@($manifest.transforms|Where-Object {$_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq $rule})
 if($transforms.Count -ne 1){
  $skips=@($manifest.skipped|Where-Object {$_.RuleId -eq $rule})|ConvertTo-Json -Compress -Depth 8
  throw "Expected one source-proven updated pass for $rule, got $($transforms.Count): $skips"
 }
}
$patched=ZipSource $result.pass.ZipPath 'immersive_third_person/init.lua'
if(!$patched.Contains('__gcetItppUpdatedPendingGatesV3') -or
   !$patched.Contains('__gcetItppRoutedOnActionV3') -or
   !$patched.Contains('__gcetItppAlwaysListeningV3') -or
   !$patched.Contains('mod.gameCameraMoveSeen(action)') -or
   !$patched.Contains('mod.move360Capture(action)') -or
   !$patched.Contains('pcall(handlePlayerAction, action)') -or
   !$patched.Contains('if not routed and Observe then') -or
   !$patched.Contains('guardStep("updateMove360", mod.updateMove360Step, delta)') -or
   !$patched.Contains('guardStep("updateApi", mod.apiTick, delta)') -or
   !$patched.Contains('guardStep("headLookTick", mod.headLookTick, delta)') -or
   !$patched.Contains('guardStep("tppMaintenance", mod.updateTppMaintenance, delta)') -or
   $patched.Contains('handle.SetActive') -or
   !$patched.Contains('if state.pendingFppCleanup then') -or
   !$patched.Contains('if state.pendingFppRestore then')) {
 throw 'Updated ITP pass missing required active API/movement/fallback or six empty-state guards'
}
if(([regex]::Matches($patched,"Observe\('PlayerPuppet', 'OnAction'")).Count -ne 1) {
 throw 'Updated ITP created an unexpected extra native observer'
}
if(@($manifest.files|Where-Object {$_.path -like 'immersive_third_person/*' -and $_.path -ne 'immersive_third_person/init.lua'}).Count -gt 0){
 throw 'Updated ITP API or camera_motion modules were rewritten'
}
foreach($p in $hashes.Keys) {
 if((Get-FileHash (Join-Path $owner $p) -Algorithm SHA256).Hash -ne $hashes[$p]) { throw "Original ITP source mutated: $p" }
}
[IO.File]::WriteAllText($sourcePath,$patched,[Text.Encoding]::UTF8)
$again=& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json
if(!$again.ok){throw 'Updated ITP second pass rejected complete source'}
if($again.pass -and $again.pass.ManifestPath){
 $next=Get-Content $again.pass.ManifestPath -Raw|ConvertFrom-Json
 if(@($next.transforms|Where-Object {$_.RuleId -in @('immersive-third-person','immersive-third-person-action-routing')}).Count -gt 0) {
  throw 'Updated ITP source was transformed again after complete pass'
 }
}
[IO.File]::WriteAllText($sourcePath,$source.Replace('MoveY = true,','MoveY = true, UnknownAction = true,'),[Text.Encoding]::UTF8)
$drift=& $exe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json
if($drift.ok -and $drift.pass -and $drift.pass.ManifestPath){
 $m=Get-Content $drift.pass.ManifestPath -Raw|ConvertFrom-Json
 if(@($m.transforms|Where-Object {$_.RuleId -eq 'immersive-third-person-action-routing'}).Count -ne 0) {
  throw 'Unknown updated ITP action was routed without source proof'
 }
}
# Same callback/body, but generically moved behind the 0-Engine registrar
# BEFORE the semantic pass. Source lines remain stable for capture coordinates.
$wrapped=$source.Replace('local mod={}',
  'local mod={}; local __gcetRegisterEvent_525 = registerForEvent')
$wrapped=$wrapped.Replace("registerForEvent('onUpdate', function(delta)",
  "__gcetRegisterEvent_525('onUpdate', function(delta)")
if($wrapped -eq $source){throw 'Generic-owner fixture did not change'}
[IO.File]::WriteAllText($sourcePath,$wrapped,[Text.Encoding]::UTF8)
$wrappedPass=& $exe --capture $capture --mods $mods --generate-pass --json |
  ConvertFrom-Json
if(!$wrappedPass.ok -or !$wrappedPass.pass){throw 'Wrapped ITP owner generation refused'}
$wrappedManifest=Get-Content $wrappedPass.pass.ManifestPath -Raw|ConvertFrom-Json
if(@($wrappedManifest.skipped|Where-Object{
  $_.RuleId -eq 'immersive-third-person' -and $_.category -eq 'GENERATION_FAILURE'
}).Count -gt 0){
  throw 'Semantic stage refused otherwise identical generic-owned ITP callback'
}
$wrappedOutput=ZipSource $wrappedPass.pass.ZipPath 'immersive_third_person/init.lua'
if(!$wrappedOutput.Contains('__gcetItppUpdatedPendingGatesV3') -or
   !$wrappedOutput.Contains('__gcetRegisterEvent_525') -or
   !$wrappedOutput.Contains('mod.pluginHeartbeat(delta)')){
   throw 'ITP upgraded generic registrar lost heartbeat/pending gates'
}
$global:LASTEXITCODE=0
Write-Host 'Updated ITP source: author and generic registrars, API/move360 retained, idempotence and action-map drift fail-closed passed.'
