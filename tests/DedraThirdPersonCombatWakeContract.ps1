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

$a=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $dedraFixture $addon
if(!$a.Applied -or $a.Changes.Count -ne 2){throw "Dedra expected init + separate worker, got $($a.Changes.Count)"}
$init=$a.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$worker=$a.Changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedra_camera_wake.lua'}|Select-Object -First 1
if(!$init -or !$worker){throw 'Missing source owner or detached wake worker'}
$lua=[Text.Encoding]::UTF8.GetString($init.Bytes)
$wake=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('Dedra.__gcetWakeController.Register()','Dedra.__gcetWakeController.Start()',
    'Dedra.__gcetWakeController.Signal()','Dedra.__gcetWakeController.Stop()',
    'Dedra.__gcetWakeFromBase','Dedra.__gcetWakeDiagnostics = function()',
    'dofile("GCET_Semantic/dedra_camera_wake.lua")')){
  if(!$lua.Contains($token)){throw "Missing source bridge: $token"}
}
$normalizedInit=$lua.Replace($crlf,$nl)
if($normalizedInit.Contains('pcall(dedraStep, dt)') -or
  ([regex]::Matches($normalizedInit,'Dedra.__gcetWakeController.Register\(\)')).Count -ne 1 -or
  !$normalizedInit.Contains('registerForEvent("onInit", function()' + $nl +
      '  Dedra.__gcetWakeController.Register()' + $nl +
      '  Dedra.__gcetWakeController.Start()')){
  throw 'Original frame callback still present, doubled, or registered before onInit'
}
# Existing V2 optimized init called Register at module load time; the same
# authoritative producer moves that ownership into onInit without producing a
# second callback, while preserving source and the existing worker.
$oldInit=$normalizedInit.Replace('registerForEvent("onInit", function()' + $nl +
   '  Dedra.__gcetWakeController.Register()' + $nl +
   '  Dedra.__gcetWakeController.Start()',
   'registerForEvent("onInit", function()' + $nl +
   '  Dedra.__gcetWakeController.Start()')
$oldInit=$oldInit.Replace('registerForEvent("onShutdown", function()',
   'Dedra.__gcetWakeController.Register()' + $nl +
   'registerForEvent("onShutdown", function()')
$oldInit=$oldInit.Replace('Dedra.__gcetWakeDiagnostics = function()' + $nl +
  '  return Dedra.__gcetWakeController.Diagnostics()' + $nl +
  'end' + $nl,'')
if($oldInit -eq $lua){throw 'Legacy eager-registration fixture unchanged'}
foreach($token in @('function M.Diagnostics()','FrameListeners.MakeRegistrar','handle.Pause()','handle.Resume()',
  'baseBridgeConfirmed','base.gcetDedraWakeReady','if proof == "__gcetItpToggleWakeV2"',
  'if p.userWantsThirdPerson == true','kind,owner,notify',
  'Original game-combat','linked','fallbackRegister("onUpdate",update)')){
  if(!$wake.Contains($token)){throw "Missing safe detached wake or fallback: $token"}
}
if($wake.Contains('api.__gcetDedraWakeBridgeV2') -or
   $wake.Contains('base.__gcetDedraWakeBridgeV2')){throw 'Read-only API probe persists'}
if($wake.Contains('0.10') -or $wake.Contains('0.25') -or $wake.Contains('SetInterval') -or
   $wake.Contains('OnFrame(')){throw 'Dormant polling/timer introduced'}
# The catalog may suppress a fully installed semantic by source proof alone.
# Prove that old eager init, missing coordinator, and prior worker version all
# remain INCOMPLETE even though their historical marker is present.
$dedraCompletion=$t.GetMethod('IsDedraThirdPersonCombatWakeComplete',$flags)
if(!$dedraCompletion){throw 'Dedra onInit two-file completion verifier absent'}
$completionRoot=Join-Path $root 'dedra-completion'
$completionWorkerDir=Join-Path $completionRoot 'GCET_Semantic'
New-Item -ItemType Directory -Force $completionWorkerDir|Out-Null
$completionInitPath=Join-Path $completionRoot 'init.lua'
$completionWorkerPath=Join-Path $completionWorkerDir 'dedra_camera_wake.lua'
[IO.File]::WriteAllText($completionInitPath,$normalizedInit,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($completionWorkerPath,$wake,[Text.UTF8Encoding]::new($false))
if(!$dedraCompletion.Invoke($null,[object[]]@([string]$completionRoot))){
  throw 'Current Dedra onInit source and pausable worker failed completion proof'
}
[IO.File]::WriteAllText($completionInitPath,$oldInit,[Text.UTF8Encoding]::new($false))
if($dedraCompletion.Invoke($null,[object[]]@([string]$completionRoot))){
  throw 'Old eager Dedra source falsely completed new dormancy'
}
[IO.File]::WriteAllText($completionInitPath,$normalizedInit,[Text.UTF8Encoding]::new($false))
[IO.File]::Delete($completionWorkerPath)
if($dedraCompletion.Invoke($null,[object[]]@([string]$completionRoot))){
  throw 'Missing external Dedra worker falsely completed new dormancy'
}
[IO.File]::WriteAllText($completionWorkerPath,$wake,[Text.UTF8Encoding]::new($false))

# Older optimized stacks have an external worker with a read-only public API
# flag probe. It must be upgraded through this same generating rule, without
# re-injecting author callbacks or moving the worker inline.
$previousWake=$wake.Replace($crlf,$nl)
$diagnosticsStart=$previousWake.IndexOf('  function M.Diagnostics()' + $nl)
$diagnosticsEnd=$previousWake.IndexOf('  function M.Stop()', $diagnosticsStart)
if($diagnosticsStart -lt 0 -or $diagnosticsEnd -le $diagnosticsStart){
  throw 'Read-only source worker diagnostics are missing or misowned'
}
$previousWake=$previousWake.Substring(0,$diagnosticsStart) +
  $previousWake.Substring($diagnosticsEnd)
$readinessProbe=(
  '    -- Source-proven capability exported by the ITP generator. This is' + $nl +
  '    -- read-only and can be checked during Dedra''s own startup, even when' + $nl +
  '    -- the base onInit proof signal was emitted earlier in load order.' + $nl +
  '    -- Unmodified/partial ITP stays fail-open until a real toggle proof.' + $nl +
  '    if not baseBridgeConfirmed and type(base.gcetDedraWakeReady) == "function" then' + $nl +
  '      local okReady, ready = pcall(base.gcetDedraWakeReady)' + $nl +
  '      if okReady and ready == true then baseBridgeConfirmed = true end' + $nl +
  '    end' + $nl)
$previousWake=$previousWake.Replace($readinessProbe,'')
[IO.File]::WriteAllText($completionWorkerPath,$previousWake,[Text.UTF8Encoding]::new($false))
if($dedraCompletion.Invoke($null,[object[]]@([string]$completionRoot))){
  throw 'Earlier detached Dedra worker falsely completed new onInit dormancy'
}
if($previousWake -eq $wake -or $previousWake.Contains('gcetDedraWakeReady')){
  throw 'Previous shipped worker fixture is not exact'
}
$legacyWake=$previousWake
$legacyWake=$legacyWake.Replace('  local baseBridgeConfirmed = false' + $nl,'')
$legacyWake=$legacyWake.Replace(
  '    if not baseBridgeConfirmed or not linked or not api then',
  '    if not linked or not api or api.__gcetDedraWakeBridgeV2 ~= true then')
$legacyWake=$legacyWake.Replace(
  '      type(base.perspective.get) ~= "function" then',
  '      type(base.perspective.get) ~= "function" or' + $nl +
  '      base.__gcetDedraWakeBridgeV2 ~= true then')
$legacyWake=$legacyWake.Replace(
  '  function M.Signal(proof)' + $nl +
  '    if proof == "__gcetItpToggleWakeV2" then baseBridgeConfirmed = true end',
  '  function M.Signal()')
if(!$legacyWake.Contains('api.__gcetDedraWakeBridgeV2')) {
  throw 'Prior generated Dedra worker fixture was not constructed'
}
$workerDir=Join-Path $dedra 'GCET_Semantic'
New-Item -ItemType Directory -Force $workerDir|Out-Null
$workerPath=Join-Path $workerDir 'dedra_camera_wake.lua'
[IO.File]::WriteAllText($workerPath,$legacyWake,[Text.UTF8Encoding]::new($false))
$script:dedraUpgradeCase='legacy worker with canonical init'
$workerUpgrade=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput
if(!$workerUpgrade.Applied -or $workerUpgrade.Changes.Count -ne 1) {
  throw 'Previously optimized Dedra worker did not upgrade in place'
}
$updatedWorker=[Text.Encoding]::UTF8.GetString($workerUpgrade.Changes[0].Bytes)
if($updatedWorker -ne $wake -or
  $workerUpgrade.Changes[0].RelativeFile -notlike '*/GCET_Semantic/dedra_camera_wake.lua') {
  throw 'Prior worker update changed execution ownership or generated unexpected code'
}
[IO.File]::WriteAllText($workerPath,$previousWake,[Text.UTF8Encoding]::new($false))
$script:dedraUpgradeCase='previous worker with canonical init'
$previousUpgrade=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput
if(!$previousUpgrade.Applied -or $previousUpgrade.Changes.Count -ne 1 -or
   [Text.Encoding]::UTF8.GetString($previousUpgrade.Changes[0].Bytes) -ne $wake) {
  throw 'Previously shipped V2 worker did not upgrade to source-proven startup wake'
}
[IO.File]::WriteAllText($workerPath,$previousWake,[Text.UTF8Encoding]::new($false))
$script:dedraUpgradeCase='previous worker with eager old init'
$oldInitUpgrade=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $oldInit $addon -StageInput
$oldInitFile=$oldInitUpgrade.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$oldWorkerFile=$oldInitUpgrade.Changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedra_camera_wake.lua'}|Select-Object -First 1
if(!$oldInitUpgrade.Applied -or $oldInitUpgrade.Changes.Count -ne 2 -or
   !$oldInitFile -or !$oldWorkerFile -or
   [Text.Encoding]::UTF8.GetString($oldInitFile.Bytes).Replace($crlf,$nl) -ne $normalizedInit -or
   [Text.Encoding]::UTF8.GetString($oldWorkerFile.Bytes) -ne $wake){
  throw 'Previously optimized eager-registration owner did not upgrade atomically'
}
[IO.File]::WriteAllText($workerPath,$updatedWorker,[Text.UTF8Encoding]::new($false))
$script:dedraUpgradeCase='current canonical worker reapply'
$workerTwice=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput
if($workerTwice.Applied -or $workerTwice.Changes.Count -ne 0) {
  throw 'Updated external worker was not idempotent'
}
$driftedWorker=$legacyWake.Replace('fallbackRegister("onUpdate",update)',
  'fallbackRegister("onUpdate",nil)')
[IO.File]::WriteAllText($workerPath,$driftedWorker,[Text.UTF8Encoding]::new($false))
$refused=$false
try { $null=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $lua $addon -StageInput }
catch { $refused=$true }
if(!$refused){throw 'Unknown external worker drift was silently overwritten'}

if([IO.File]::ReadAllText((Join-Path $dedra 'init.lua')) -ne $dedraFixture){throw 'Installed source edited'}
if([IO.File]::ReadAllText((Join-Path $itp 'init.lua')) -ne $itpFixture){throw 'Base live source edited'}
Write-Host 'Paired base CET toggle and Dedra detached worker: no dormant polling, one callback, author fallback PASS'
