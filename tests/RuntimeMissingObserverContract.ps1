param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.PassGeneratorService',$true)
$m=$t.GetMethod('TryTransformUnsupportedGameHooks',
  [Reflection.BindingFlags]'NonPublic,Static')
if(!$m){throw 'Authoritative generic source hook generator absent'}

function Apply([string]$path,[string]$source) {
  $args=[object[]]@($path,$source,$null,$null)
  $changed=[bool]$m.Invoke($null,$args)
  return @{changed=$changed;output=[string]$args[2];stages=@($args[3])}
}
$lf=[string][char]10
$crlf=([string][char]13)+$lf

$gameUi=@'
local gameUI = {}
gameUI.menuMethods = {
  ['MenuScenario_Settings'] = {
    ['OnSwitchToCredits'] = 'Credits',
  },
  ['MenuScenario_PauseMenu'] = {
    ['OnSwitchToBrightnessSettings'] = 'Brightness',
    ['OnSwitchToCredits'] = 'Credits',
    ['OnSwitchToLoadGame'] = 'LoadGame',
  },
}
return gameUI
'@
$a=Apply 'SomeOtherInstalledMod/Modules/GameUI.lua' $gameUi
if(!$a.changed -or $a.stages.Count -ne 1 -or
   !$a.output.Contains("    -- ['OnSwitchToCredits'] = 'Credits',") -or
   !$a.output.Contains("    ['OnSwitchToLoadGame'] = 'LoadGame',") -or
   !$a.output.Contains("    ['OnSwitchToCredits'] = 'Credits',")) {
  throw 'Generic GameUI removal must only disable the missing pause-menu event'
}
if((Apply 'SomeOtherInstalledMod/Modules/GameUI.lua' $a.output).changed) {
  throw 'GameUI observer removal not idempotent'
}
if((Apply 'AnotherInstalledMod/metadata.lua' $gameUi).changed) {
  throw 'Observer removal incorrectly targets non-GameUI source'
}
$search="    ['OnSwitchToCredits'] = 'Credits',"+$lf+"    ['OnSwitchToLoadGame']"
$replace="    -- ['OnSwitchToCredits'] = 'Credits', -- PASS8: removed game method"+$lf+"    ['OnSwitchToLoadGame']"
$historical=$gameUi.Replace($crlf,$lf).Replace($search,$replace)
if(!$historical.Contains("    -- ['OnSwitchToCredits'] = 'Credits', -- PASS8")) {
  throw 'Historical completed stage fixture not constructed'
}
if((Apply 'HistoricalMod/Modules/GameUI.lua' $historical).changed) {
  throw 'Completed Phase1+2 observer removal was reapplied'
}
$double=$gameUi.Replace($crlf,$lf).Replace("    ['OnSwitchToLoadGame']",
  "    ['OnSwitchToCredits'] = 'Credits',"+$lf+"    ['OnSwitchToLoadGame']")
if($double -eq $gameUi -or
   $double.Split("['OnSwitchToCredits'] = 'Credits',").Count -lt 4) {
  throw 'Duplicate invalid observer fixture not constructed'
}
$refused=$false
try { $null=Apply 'AmbiguousMod/GameUI.lua' $double } catch { $refused=$true }
if(!$refused){throw 'Ambiguous duplicate invalid GameUI entries did not fail close'}

$boss=@'
registerForEvent("onInit", function()
  Observe("ScriptedPuppet", "OnAttach", function(self)
    if combatPhase >= 1 and activeBossRecord then
      local selfIDStr = tostring(self:GetRecordID())
      local isValidEnemy = false
      local e = self:GetEntityID()
      handleBoss(e)
    end
  end)
  Observe("ScriptedPuppet", "OnDeath", function(self)
    cleanup(self)
  end)
end)
'@
$b=Apply 'OtherOwner/init.lua' $boss
if(!$b.changed -or $b.stages.Count -ne 1 -or
   !$b.output.Contains('Observe("ScriptedPuppet", "OnGameAttached", function(self)') -or
   !$b.output.Contains('local e = self:GetEntityID()') -or
   !$b.output.Contains('Observe("ScriptedPuppet", "OnDeath", function(self)') -or
   $b.output.Contains('Observe("ScriptedPuppet", "OnAttach"')) {
  throw 'Proven ScriptedPuppet boss hook did not preserve author callback body'
}
if((Apply 'OtherOwner/init.lua' $b.output).changed) {
  throw 'Boss hook replacement was not idempotent'
}
$unknown=$boss.Replace('if combatPhase >= 1 and activeBossRecord then',
  'if someOtherCondition then')
if((Apply 'OtherOwner/init.lua' $unknown).changed) {
  throw 'Unproven ScriptedPuppet hook should not be changed'
}
$check=Apply 'Another/GameUI.lua' ($gameUi.Replace($crlf,$lf).Replace($lf,$crlf))
if(!$check.changed -or !$check.output.Contains($crlf)) {
  throw 'GameUI CRLF preservation failed'
}
Write-Host 'Pristine, partial, source-hook compatibility, idempotence, drift and newline preservation PASS'
