param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$injector=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$method=$injector.GetMethod('ApplyDedraThirdPersonCombatWake',[Reflection.BindingFlags]'NonPublic,Static')
if(!$method){throw 'Missing Dedra source injector'}
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$root=Join-Path $env:RUNNER_TEMP 'gcet-dedra-camera-combat'
$folder=Join-Path $root 'DedraThirdPersonCombatAddon'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder|Out-Null
$fixture=@'
local Dedra = {cfg={combatTpp=true,anims={}},itp=nil,takeover=false,standIn=nil,
  on=nil,armSent=nil,aimAsk=nil,headLookHeld=nil,planeHeld=nil,easeOut=nil}
local function dedraReadItp()
  local ok, api=pcall(GetMod, "immersive_third_person")
  if ok and type(api)=="table" then Dedra.itp=api end
end
local function dedraFirstPersonOnly(player) return false end
local function dedraLeaveTakeover(player, backToFirst) end
local function dedraStep(dt)
  local wantsTpp=true
  local combatFirst=false
  local blocked=false
  local wantTakeover = Dedra.cfg.combatTpp and wantsTpp and combatFirst and not blocked
  if Dedra.takeover then return wantTakeover end
  if Dedra.itp then Dedra.itp.perspective.release("dedra.aim") end
end
local function dedraSetCombatTpp(state)
  Dedra.cfg.combatTpp = state
end
local function dedraSetAnims(key, state)
  Dedra.cfg.anims[key] = state
end
registerForEvent("onInit", function()
  Observe("PlayerPuppet","OnGameAttached", function()
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
$live=Join-Path $folder 'init.lua'
function Run([string]$source) {
  [IO.File]::WriteAllText($live,$source,[Text.UTF8Encoding]::new($false))
  $staged=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'DedraThirdPersonCombatAddon',[string]'dedra-third-person-combat-wake',$staged))
  try {$result=$method.Invoke($null,[object[]]@($ctx))}
  catch {throw "Dedra wake rejected: $($_.Exception.InnerException.Message)"}
  $changes=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  return @{applied=$result.Applied;changes=$changes}
}
Write-Host 'Testing optimized registrar/LF...'
$first=Run $fixture
if(!$first.applied -or $first.changes.Count -ne 1){throw 'One source-owned Lua transform expected'}
$generated=[Text.Encoding]::UTF8.GetString($first.changes[0].Bytes)
foreach($needle in @(
  '-- G-CET semantic:dedra-third-person-combat-wake','__gcetDedraEventWakeV1',
  'local function __gcetDedraDue(dt)',
  'if __gcetDedraDue(dt) then pcall(dedraStep, __gcetDedraStepDelta) end',
  '"perspectiveChanged", "overrideChanged", "transitionStart"',
  'status ~= 2','__gcetDedraProbe >= 0.10',
  '__gcetDedraElapsed < 0.25','p.userWantsThirdPerson',
  'p.override == "combat"','return true  -- Original standalone',
  'Dedra.standIn ~= ""','local cleanup = not p or p.userWantsThirdPerson ~= true',
  '__gcetDedraPending(p)','Dedra.cfg.combatTpp = state',
  'pcall(dedraLeaveTakeover, player, true)')) {
  if(!$generated.Contains($needle)){throw "Combat wake postcondition missing: $needle"}
}
if($generated.Contains('registerInput(') -or $generated.Contains('registerHotkey(')){throw 'User CET binding duplicated'}
if(([regex]::Matches($generated,'pcall\(dedraStep,')).Count -ne 1){throw 'Step callback duplicated'}
if(([regex]::Matches($generated,'registerForEvent\("onInit", function\(\)')).Count -ne 1){throw 'Init registration corrupted'}
if(([regex]::Matches($generated,'(?:registerForEvent|__gcetRegisterEvent_293)\("onUpdate", function\(dt\)')).Count -ne 1){throw 'Update registration corrupted'}
if($generated.Contains('pcall(dedraStep, dt)')){throw 'Original onUpdate escaped wake gate'}
if([IO.File]::ReadAllText($live) -ne $fixture){throw 'Installed source was changed'}
Write-Host 'Testing idempotence...'
$again=Run $generated
if($again.applied -or $again.changes.Count -ne 0){throw 'Semantic stage not idempotent'}
Write-Host 'Testing CRLF...'
$crlf=Run ($fixture.Replace([string][char]10,([string][char]13 + [string][char]10)))
if(!$crlf.applied){throw 'CRLF source not supported'}
Write-Host 'Testing pristine registrar...'
$pristine=Run ($fixture.Replace('__gcetRegisterEvent_293("onUpdate"','registerForEvent("onUpdate"'))
if(!$pristine.applied){throw 'Pristine CET event registrar not supported'}
$drift=$fixture.Replace('pcall(dedraStep, dt)','pcall(newAuthorStep, dt)')
[IO.File]::WriteAllText($live,$drift)
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$ctx=$ctor.Invoke([object[]]@([string]$root,[string]'DedraThirdPersonCombatAddon',[string]'dedra-third-person-combat-wake',$staged))
$refused=$false
try {$null=$method.Invoke($null,[object[]]@($ctx))}
catch {$refused=$_.Exception.InnerException.Message -match 'changed|do not insert'}
if(!$refused -or @($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx)).Count -ne 0){
  throw 'Source drift was not refused without output'
}
Write-Host 'Dedra combat event wake: old/new registrar, FPP gate, combat cleanup, standalone fallback, CRLF, idempotence and drift PASS'
