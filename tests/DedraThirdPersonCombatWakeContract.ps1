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
mod.apiCore = { public = readOnly(public) }
mod.api = mod.apiCore and mod.apiCore.public or nil
return mod.api
'@
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
  catch {throw "Rule $rule rejected: $($_.Exception.InnerException.Message)"}
  $changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  return @{Applied=$result.Applied;Changes=$changes}
}
$b=Run 'immersive_third_person' 'immersive-third-person-dedra-toggle-signal' $itpFixture $base
if(!$b.Applied -or $b.Changes.Count -ne 1){throw 'Base should emit one source signal'}
$bridge=[Text.Encoding]::UTF8.GetString($b.Changes[0].Bytes)
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
$old=$bridge.Replace('pcall(addon.__gcetWakeFromBase, "__gcetItpToggleWakeV2")',
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

$a=Run 'DedraThirdPersonCombatAddon' 'dedra-third-person-combat-wake' $dedraFixture $addon
if(!$a.Applied -or $a.Changes.Count -ne 2){throw "Dedra expected init + separate worker, got $($a.Changes.Count)"}
$init=$a.Changes|Where-Object{$_.RelativeFile -like '*/init.lua'}|Select-Object -First 1
$worker=$a.Changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedra_camera_wake.lua'}|Select-Object -First 1
if(!$init -or !$worker){throw 'Missing source owner or detached wake worker'}
$lua=[Text.Encoding]::UTF8.GetString($init.Bytes)
$wake=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('Dedra.__gcetWakeController.Register()','Dedra.__gcetWakeController.Start()',
    'Dedra.__gcetWakeController.Signal()','Dedra.__gcetWakeController.Stop()',
    'Dedra.__gcetWakeFromBase','dofile("GCET_Semantic/dedra_camera_wake.lua")')){
  if(!$lua.Contains($token)){throw "Missing source bridge: $token"}
}
if($lua.Contains('pcall(dedraStep, dt)') -or
  ([regex]::Matches($lua,'Dedra.__gcetWakeController.Register\(\)')).Count -ne 1){
  throw 'Original frame callback still present or doubled'
}
foreach($token in @('FrameListeners.MakeRegistrar','handle.Pause()','handle.Resume()',
  'baseBridgeConfirmed','if proof == "__gcetItpToggleWakeV2"',
  'if p.userWantsThirdPerson == true','kind,owner,notify',
  'Original game-combat','linked','fallbackRegister("onUpdate",update)')){
  if(!$wake.Contains($token)){throw "Missing safe detached wake or fallback: $token"}
}
if($wake.Contains('api.__gcetDedraWakeBridgeV2') -or
   $wake.Contains('base.__gcetDedraWakeBridgeV2')){throw 'Read-only API probe persists'}
if($wake.Contains('0.10') -or $wake.Contains('0.25') -or $wake.Contains('SetInterval') -or
   $wake.Contains('OnFrame(')){throw 'Dormant polling/timer introduced'}
if([IO.File]::ReadAllText((Join-Path $dedra 'init.lua')) -ne $dedraFixture){throw 'Installed source edited'}
if([IO.File]::ReadAllText((Join-Path $itp 'init.lua')) -ne $itpFixture){throw 'Base live source edited'}
Write-Host 'Paired base CET toggle and Dedra detached worker: no dormant polling, one callback, author fallback PASS'
