param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$addon=$t.GetMethod('ApplyDedraThirdPersonCombatWake',$flags)
$base=$t.GetMethod('ApplyImmersiveThirdPersonDedraToggleSignal',$flags)
if(!$addon -or !$base){throw 'Both paired source injectors are required'}
$ctor=$ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4}|Select-Object -First 1
$root=Join-Path $env:RUNNER_TEMP 'gcet-dedra-v2'
$dedra=Join-Path $root 'DedraThirdPersonCombatAddon'
$itp=Join-Path $root 'immersive_third_person'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $dedra,$itp|Out-Null
$dedraFixture=@'
local Dedra = {cfg={combatTpp=true,anims={}},takeover=false,armSent=nil}
local function dedraReadItp() return nil end
local function dedraFirstPersonOnly(player) return false end
local function dedraLeaveTakeover(player, backToFirst) end
local function dedraStep(dt)
  local wantsTpp = true
  local combatFirst = false
  local blocked = false
  local wantTakeover = Dedra.cfg.combatTpp and wantsTpp and combatFirst and not blocked
  if Dedra.takeover then
    pcall(dedraLeaveTakeover, player, true)
  end
  Dedra.itp.perspective.release("dedra.aim")
end
registerForEvent("onInit", function()
  Observe("PlayerPuppet", "OnGameAttached", function()
    Dedra.takeover = false
    Dedra.armSent = nil
  end)
end)
__gcetRegisterEvent_293("onUpdate", function(dt)
  pcall(dedraStep, dt)
end)
registerForEvent("onShutdown", function()
  pcall(dedraLeaveTakeover, player, true)
end)
return Dedra
'@
$itpFixture=@'
local mod={}
local function autoTppHandleToggle()
  state.autoPerspective.userWantsTpp = not state.autoPerspective.userWantsTpp
end
registerInput("tpp_camera_toggle", TEXT.bindToggle[1], function(keypress)
  if not keypress then
    return
  end

  pcall(autoTppHandleToggle)
end)
local public = { events = { on = function() end }, perspective = { get = function() return {} end } }
local function readOnly(t)
  return setmetatable({}, {__index=t, __newindex=function() error("the Immersive Third Person API table is read-only") end})
end
registerForEvent("onInit", function()
  mod.captureConsumableTiming()
end)
mod.apiBuild = function()
  local Api = mod.loadModule("api")
  return Api.new({
    config = config,
    state = state,
    system = function() return nil end,
  })
end
mod.apiCore = { public = readOnly(public) }
mod.api = mod.apiCore and mod.apiCore.public or nil
return mod.api
'@
$itpApiFixture=@'
local Api={}
function Api.new(host)
  local core={}
  local function readOnly(t) return t end
  local function touching(t) return t end
  local events={}
  core.public = readOnly(touching({
    events = readOnly(touching(events)),
  }))
  return core
end
return Api
'@
$itpApiPath=Join-Path $itp 'api.lua'
[IO.File]::WriteAllText($itpApiPath,$itpApiFixture,[Text.UTF8Encoding]::new($false))
function Run([string]$owner,[string]$rule,[string]$source,$handler,[switch]$StageInput) {
  $folder=Join-Path $root $owner
  $staged=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
  if($StageInput) {
    $staged[($owner+'/init.lua')]=[Text.Encoding]::UTF8.GetBytes($source)
  } else {
    [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
  }
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$staged))
  try {$result=$handler.Invoke($null,[object[]]@($ctx))}
  catch {throw "Rule $rule [$script:dedraUpgradeCase] rejected: $($_.Exception.InnerException.Message)"}
  $changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  return @{Applied=$result.Applied;Changes=$changes}
}
$b=Run 'immersive_third_person' 'immersive-third-person-dedra-toggle-signal' $itpFixture $base
if(!$b.Applied -or $b.Changes.Count -ne 2){throw 'Base should emit init.lua signal and read-only api.lua capability'}
$baseInitChange=$b.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$baseApiChange=$b.Changes|Where-Object{$_.RelativeFile -like '*/api.lua'}|Select-Object -First 1
if(!$baseInitChange -or !$baseApiChange){throw 'Paired base source changes not generated'}
$bridge=[Text.Encoding]::UTF8.GetString($baseInitChange.Bytes)
$apiExport=[Text.Encoding]::UTF8.GetString($baseApiChange.Bytes)
if(!$apiExport.Contains('__gcetDedraWakeCapabilityV3') -or
   !$apiExport.Contains('gcetDedraWakeReady = function()') -or
   !$bridge.Contains('gcetDedraWakeReady = function() return mod.__gcetDedraWakeBridgeV2 == true end')) {
  throw 'Read-only startup wake capability missing or incorrectly sourced'
}
[IO.File]::WriteAllText($itpApiPath,$apiExport,[Text.UTF8Encoding]::new($false))
if(!$bridge.Contains('mod.__gcetDedraWakeBridgeV2 = true') -or
   $bridge.Contains('mod.api.__gcetDedraWakeBridgeV2') -or
   !$bridge.Contains('pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")') -or
   !$bridge.Contains('mod.captureConsumableTiming()') -or
   !$bridge.Contains('return mod.api') -or
   !$bridge.Contains('pcall(mod.__gcetDedraToggleSignalV2)') -or
   $bridge.Contains('registerHotkey(') -or
   ([regex]::Matches($bridge,'registerInput\("tpp_camera_toggle"')).Count -ne 1){
  throw 'Original CET keybind not preserved or user-request wake missing'
}
# Full public API export must already be complete on a second original-owned
# generation; the source injector may not rely on a separate corrective rule.
$twice=Run 'immersive_third_person' 'immersive-third-person-dedra-toggle-signal' $bridge $base -StageInput
if($twice.Applied -or $twice.Changes.Count -ne 0) {
  throw 'Public Dedra wake export was not idempotent'
}

# An earlier G-CET generation wrote a flag onto ITP's read-only public API.
# Verify the SAME producer repairs that exact optimized/partial source.
$nl=[string][char]10
$crlf=([string][char]13)+$nl
$old=$bridge.Replace($crlf,$nl).Replace('pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")',
                     'pcall(addon.__gcetWakeFromBase)')
$old=$old.Replace('  pcall(mod.__gcetDedraToggleSignalV2)' + $nl + '  mod.captureConsumableTiming()',
                  '  mod.captureConsumableTiming()')
$apiAnchor='mod.api = mod.apiCore and mod.apiCore.public or nil'
$legacyApi=$apiAnchor + $nl +
    'if type(mod.api) == "table" then' + $nl +
    '  mod.api.__gcetDedraWakeBridgeV2 = mod.__gcetDedraWakeBridgeV2 == true' + $nl +
    'end'
$old=$old.Replace($apiAnchor,$legacyApi)
if(!$old.Contains('mod.api.__gcetDedraWakeBridgeV2')){throw 'Broken legacy fixture not constructed'}
$upgrade=Run 'immersive_third_person' 'immersive-third-person-dedra-toggle-signal' $old $base -StageInput
if(!$upgrade.Applied -or $upgrade.Changes.Count -ne 1){throw 'Prior broken ITP bridge not upgraded'}
$upgraded=[Text.Encoding]::UTF8.GetString($upgrade.Changes[0].Bytes)
if($upgraded.Contains('mod.api.__gcetDedraWakeBridgeV2') -or
   !$upgraded.Contains('pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")') -or
   !$upgraded.Contains('  pcall(mod.__gcetDedraToggleSignalV2)' + $nl + '  mod.captureConsumableTiming()')) {
    throw 'Legacy ITP crash and capability upgrade incomplete'
}
$upgradedAgain=Run 'immersive_third_person' 'immersive-third-person-dedra-toggle-signal' $upgraded $base -StageInput
if($upgradedAgain.Applied -or $upgradedAgain.Changes.Count -ne 0) {
    throw 'Read-only-safe ITP upgrade was not idempotent'
}

# A previous init-only output is not paired completion. Both source files
# must independently prove their wake roles; no marker-only acceptance.
$proof=$t.GetMethod('IsImmersiveThirdPersonDedraWakeComplete',$flags)
if(!$proof){throw 'Paired ITP wake source-completion verifier missing'}
$proofRoot=Join-Path $root 'itp-paired-proof'
New-Item -ItemType Directory -Force $proofRoot|Out-Null
$proofInit=Join-Path $proofRoot 'init.lua'
$proofApi=Join-Path $proofRoot 'api.lua'
[IO.File]::WriteAllText($proofInit,$bridge,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($proofApi,$itpApiFixture,[Text.UTF8Encoding]::new($false))
if($proof.Invoke($null,[object[]]@([string]$proofRoot))){
    throw 'Init-only wake incorrectly classified as complete'
}
[IO.File]::WriteAllText($proofApi,$apiExport,[Text.UTF8Encoding]::new($false))
if(!$proof.Invoke($null,[object[]]@([string]$proofRoot))){
    throw 'Paired init+api read-only wake not classified complete'
}
[IO.File]::WriteAllText($proofInit,$itpFixture,[Text.UTF8Encoding]::new($false))
if($proof.Invoke($null,[object[]]@([string]$proofRoot))){
    throw 'API-only wake incorrectly classified as complete'
}
[IO.File]::WriteAllText($proofInit,$upgraded,[Text.UTF8Encoding]::new($false))
if(!$proof.Invoke($null,[object[]]@([string]$proofRoot))){
    throw 'Legacy staged init upgrade lost paired completion proof'
}

# The source-only semantic may adopt a frame owner ONLY if 0-Engine's
# actual, currently COMPOSED author onUpdate has the namespaced driver.
# These minimal host bytes exercise that proof without prior G-CET output.
$hostRoot=Join-Path $root '0-Engine'
New-Item -ItemType Directory -Force $hostRoot|Out-Null
$hostPath=Join-Path $hostRoot 'init.lua'
$hostFixture=@'
registerForEvent("onUpdate", function(delta)
    pcall(UpdateFrame, delta)
    Engine.__gcetScheduleDriver({
        frame = currentFrame, dt = delta
    })
end)
__host.__gcetScheduleDriver = function(ctx)
    pcall(__scheduler.Update, ctx)
end
'@
[IO.File]::WriteAllText($hostPath,$hostFixture,[Text.UTF8Encoding]::new($false))
$nl=[string][char]10
$crlf=([string][char]13)+$nl
$a=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $dedraFixture $addon
if(!$a.Applied -or $a.Changes.Count -ne 2) {
  throw "Dedra pristine output must contain one init bridge and one worker: $($a.Changes.Count)"
}
$init=$a.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$worker=$a.Changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedra_camera_wake.lua'}|Select-Object -First 1
if(!$init -or !$worker){throw 'Dedra generated dependencies missing'}
$lua=[Text.Encoding]::UTF8.GetString($init.Bytes).Replace($crlf,$nl)
$wake=[Text.Encoding]::UTF8.GetString($worker.Bytes).Replace($crlf,$nl)
foreach($token in @(
  '-- G-CET semantic:dedra-third-person-combat-wake',
  '-- __gcetDedraSharedScheduleV3:',
  'Dedra.__gcetWakeController.Start()',
  'Dedra.__gcetWakeController.Signal()',
  'Dedra.__gcetWakeController.Stop()',
  'Dedra.__gcetWakeFromBase',
  'dofile("GCET_Semantic/dedra_camera_wake.lua")')) {
  if(!$lua.Contains($token)) {throw "Dedra bridge missing $token"}
}
foreach($token in @(
  '-- __gcetDedraSharedScheduleV3:',
  'schedule.EveryFrame(', 'schedule.Every(0.25,',
  'type(host.__gcetScheduleDriver) ~= "function"',
  'AimStateChanged','WeaponStateChanged','UpperBodyStateChanged',
  'CombatStateChanged', 'PlayerReady','PlayerInvalidated',
  'function M.Start()', 'function M.Stop()', 'function M.Signal()',
  'pcall(step, dt)', 'tryStep(ctx,false)', 'tryStep(ctx,true)')) {
  if(!$wake.Contains($token)){throw "Dedra scheduler or core maintenance missing: $token"}
}
if($lua.Contains('Dedra.__gcetWakeController.Register()') -or
   $wake.Contains('FrameListeners.MakeRegistrar') -or
   $wake.Contains('fallbackRegister("onUpdate"') -or
   $wake.Contains('registerForEvent(') -or
   ([regex]::Matches($lua,'(?:registerForEvent|__gcetRegisterEvent_[0-9]+)\(["'']onUpdate["'']')).Count -ne 0) {
  throw 'Dedra emitted a second/late native frame registrar'
}
if(!$wake.Contains('Dedra.on ~= false') -or
   !$wake.Contains('Dedra.armSent ~= false') -or
   !$wake.Contains('Dedra.planeHeld == true')) {
  throw 'Dedra ignored REDscript/cleanup debt'
}
$proof=$t.GetMethod('IsDedraThirdPersonCombatWakeComplete',$flags)
if(!$proof){throw 'Dedra two-file complete proof missing'}
$workerDir=Join-Path $dedra 'GCET_Semantic'
New-Item -ItemType Directory -Force $workerDir|Out-Null
$workerPath=Join-Path $workerDir 'dedra_camera_wake.lua'
[IO.File]::WriteAllText((Join-Path $dedra 'init.lua'),$lua,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($workerPath,$wake,[Text.UTF8Encoding]::new($false))
if(!$proof.Invoke($null,[object[]]@([string]$dedra))){throw 'Finished V3 source+worker not complete'}
$reapply=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput
if($reapply.Applied -or $reapply.Changes.Count -ne 0){throw 'Finished V3 not idempotent'}
[IO.File]::Delete($workerPath)
if($proof.Invoke($null,[object[]]@([string]$dedra))){throw 'Missing worker passed completion proof'}
$rebuild=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput
if(!$rebuild.Applied -or $rebuild.Changes.Count -ne 1 -or
   $rebuild.Changes[0].RelativeFile -notlike '*/GCET_Semantic/dedra_camera_wake.lua'){
  throw 'Missing V3 worker could not regenerate without changing completed Lua'}
[IO.File]::WriteAllText($workerPath,$wake,[Text.UTF8Encoding]::new($false))
$drift=$wake.Replace('pcall(step, dt)','pcall(step, nil)')
[IO.File]::WriteAllText($workerPath,$drift,[Text.UTF8Encoding]::new($false))
$rejected=$false
try{$null=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput}
catch{$rejected=$true}
if(!$rejected){throw 'Unknown optimized worker drift was silently rewritten'}
# Exact user-installed previous V2 coordinator and bridge are supported
# without recreating the obsolete CET frame registration path.
$legacyField=$t.GetField('WorkerBody',$flags)
if(!$legacyField){throw 'No verified V2 upgrade template in authoritative generator'}
$legacyWake='-- G-CET semantic coordinator:dedra-third-person-combat-wake' + $nl +
  ([string]$legacyField.GetRawConstantValue()).Replace($crlf,$nl)
$legacyInit=$lua.Replace(
   '-- G-CET semantic:dedra-third-person-combat-wake' + $nl +
   '-- __gcetDedraSharedScheduleV3: uses existing 0-Engine frame owner.',
   '-- __gcetDedraDetachedWakeV2: worker owns pausable frame subscription.')
$legacyInit=$legacyInit.Replace('  Dedra, dedraStep)',
 '  Dedra, dedraStep, registerForEvent)')
$legacyInit=$legacyInit.Replace('registerForEvent("onShutdown", function()',
 'Dedra.__gcetWakeController.Register()' + $nl +
 'registerForEvent("onShutdown", function()')
if($legacyInit -eq $lua -or !$legacyInit.Contains('__gcetDedraDetachedWakeV2')) {
  throw 'Prior V2 source fixture construction failed'
}
[IO.File]::WriteAllText($workerPath,$legacyWake,[Text.UTF8Encoding]::new($false))
$script:dedraUpgradeCase='V2 diagnostic worker and boot-safe init'
$upgrade=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $legacyInit $addon -StageInput
$upInit=$upgrade.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$upWorker=$upgrade.Changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedra_camera_wake.lua'}|Select-Object -First 1
if(!$upgrade.Applied -or $upgrade.Changes.Count -ne 2 -or !$upInit -or !$upWorker -or
  [Text.Encoding]::UTF8.GetString($upInit.Bytes).Replace($crlf,$nl) -ne $lua -or
  [Text.Encoding]::UTF8.GetString($upWorker.Bytes).Replace($crlf,$nl) -ne $wake){
  throw 'Source exact previous V2 install did not upgrade atomically to V3'
}
# A stack with no 0-Engine adopted driver must preserve stock CET callback.
Remove-Item $hostPath -Force
[IO.File]::WriteAllText($workerPath,$wake,[Text.UTF8Encoding]::new($false))
if($proof.Invoke($null,[object[]]@([string]$dedra))){
  throw 'Current Dedra worker without its 0-Engine driver falsely passed completion proof'
}
$noHost=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $dedraFixture $addon -StageInput
if($noHost.Applied -or $noHost.Changes.Count -ne 0){
  throw 'Unverified 0-Engine incorrectly adopted Dedra update'
}
[IO.File]::WriteAllText($hostPath,$hostFixture,[Text.UTF8Encoding]::new($false))
if([IO.File]::ReadAllText((Join-Path $itp 'init.lua')) -ne $itpFixture){
  throw 'ITP author source unexpectedly edited'
}
Write-Host 'Dedra V3: source-proven shared host, pristine+V2 upgrade, one execution owner, drift, missing host PASS'
