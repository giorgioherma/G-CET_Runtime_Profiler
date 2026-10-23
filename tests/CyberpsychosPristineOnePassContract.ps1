param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$root = Join-Path $env:RUNNER_TEMP 'gcet-cyberpsychos-one-pass'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$folder = Join-Path $root 'repeatable_cyberpsychos'
New-Item -ItemType Directory -Force (Join-Path $folder 'modules') | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t = $assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT = $assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function Raw([string]$name) {
  $m = $t.GetMethod('Cyberpsycho' + $name,$flags)
  if (!$m) { throw "Missing source contract fixture method Cyberpsycho$name" }
  return [string]$m.Invoke($null,@())
}
$init = @'
local Config = require("./modules/config.lua")
local Sites = require("./modules/sites.lua")
local Bridge = require("./modules/bridge.lua")
local Mappins = require("./modules/mappins.lua")
local Rewards = require("./modules/rewards.lua")
local Diagnostics = require("./modules/diagnostics.lua")
local BossWatchdog = require("./modules/boss_watchdog.lua")
local Mod = {
    tickElapsed = 0.0,
    schedulerElapsed = 0.0,
    playerAttached = false,
}
Mod.settings = Config.load(Mod.configPath)
function Mod.saveSettings()
    Config.save(Mod.configPath, Mod.settings)
end

'@ + (Raw 'PreludeOld') + "`n`n" + (Raw 'OnInitOld') + "`n`n" + (Raw 'TailOld') + "`n`nreturn Mod`n"
$mappins = @'
local Mappins = {
    lastSummary = nil,
}
'@ + (Raw 'MappinsOld') + "`n`n" + @'
function Mappins.clear()
    Mappins.lastSummary = nil
end
return Mappins
'@
$diagnostics = @'
local Diagnostics = {
    lastSnapshotAt = 0,
}
'@ + (Raw 'DiagnosticsOld') + "`n`n" + @'
function Diagnostics.shutdown()
    Diagnostics.event("session_end", {}, true)
end
return Diagnostics
'@
$watchdog = @'
local Watchdog = {}
'@ + (Raw 'DistanceOld') + "`n`n" + @'
local function entityIDKey(id) return tostring(id) end
'@ + (Raw 'WatchdogOld') + "`n`n" + @'
function Watchdog.clear()
end
return Watchdog
'@
$utf8 = [Text.UTF8Encoding]::new($false)
$files = @{
  'init.lua' = $init
  'modules/mappins.lua' = $mappins
  'modules/diagnostics.lua' = $diagnostics
  'modules/boss_watchdog.lua' = $watchdog
}
foreach($item in $files.GetEnumerator()) {
  $path = Join-Path $folder $item.Key
  [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
  [IO.File]::WriteAllText($path, $item.Value, $utf8)
}
$ctxCtor = @($ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object {$_.GetParameters().Count -eq 4})[0]
function NewCtx([string]$rule,$staged) {
  $ctxCtor.Invoke([object[]]@([string]$root,[string]'repeatable_cyberpsychos',$rule,$staged))
}
function Changes($ctx) {
  @($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
}
$initial = [Collections.Generic.Dictionary[string,byte[]]]::new()
$fn = $t.GetMethod('ApplyRepeatableCyberpsychos',$flags)
$first = NewCtx 'repeatable-cyberpsychos' $initial
$before = [IO.File]::ReadAllBytes((Join-Path $folder 'init.lua'))
$result = $fn.Invoke($null,[object[]]@($first))
$edits = @(Changes $first)
if (!$result.Applied -or $edits.Count -ne 5) { throw "Primary pristine pass must create 4 files plus one owned worker; got $($edits.Count)." }
if ([Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $folder 'init.lua'))) -ne [Convert]::ToBase64String($before)) { throw 'Primary injector altered original mod files.' }
$staged = [Collections.Generic.Dictionary[string,byte[]]]::new()
foreach($edit in $edits) { $staged.Add($edit.RelativeFile,$edit.Bytes) }
function Staged([string]$path) { [Text.Encoding]::UTF8.GetString($staged['repeatable_cyberpsychos/' + $path]) }
$initResult = Staged 'init.lua'
$workResult = Staged 'GCET_Semantic/cyberpsychos_engine_worker.lua'

# The authoritative writer must emit an exact rule line, not only a longer stage marker.
$canonicalInit = @($initResult -split "`n" | Where-Object { $_.Trim() -eq '-- G-CET semantic:repeatable-cyberpsychos' })
if ($canonicalInit.Count -ne 1 -or -not $initResult.Contains('G-CET semantic:repeatable-cyberpsychos-six-job-v1')) {
  throw 'Cyberpsychos pristine init lost canonical rule ownership.'
}
foreach ($authorPath in @('modules/mappins.lua','modules/diagnostics.lua','modules/boss_watchdog.lua')) {
  $owned = @( (Staged $authorPath) -split "`n" | Where-Object { $_.Trim() -eq '-- G-CET semantic:repeatable-cyberpsychos' } )
  if ($owned.Count -ne 1) { throw "Cyberpsychos module lost exact rule ownership: $authorPath" }
}
foreach($id in @('runtime_combat_reward','runtime_watchdog','presentation_worker','runtime','presentation','scheduler')) {
  if (!$workResult.Contains('id = "cyberpsychos.' + $id + '"')) { throw "Missing source-owned job $id" }
}
foreach($required in @('TryAttachRepeatablePass4()','if pass4Attached then return end','runtimeCoreTick','runRuntimeWatchdogStep','runPresentationWorkStep')) {
  if (!$initResult.Contains($required)) { throw "Primary source missing $required" }
}
if (!(Staged 'modules/mappins.lua').Contains('function Mappins.syncStep(')) { throw 'Incremental mappins omitted' }
if (!(Staged 'modules/diagnostics.lua').Contains('function Diagnostics.snapshotStep(')) { throw 'Incremental diagnostics omitted' }
if (!(Staged 'modules/boss_watchdog.lua').Contains('function Watchdog.syncSite(')) { throw 'Per-site watchdog omitted' }
# A separate catalog identity now runs in the SAME generation, from the staged source.
$nativeMethod = $t.GetMethod('ApplyCyberpsychosNativeLocations',$flags)
$native = NewCtx 'native-location-cyberpsychos' $staged
$nativeResult = $nativeMethod.Invoke($null,[object[]]@($native))
if (!$nativeResult.Applied -or @(Changes $native).Count -ne 3) { throw 'Native worker/wake failed to compose in first pass.' }
foreach($edit in (Changes $native)) { $staged[$edit.RelativeFile] = $edit.Bytes }
$nativeWorker = Staged 'GCET_Semantic/cyberpsycho_native_wake.lua'
if (!$nativeWorker.Contains('host:attachCompiled(owner, 1,') -or !$nativeWorker.Contains('1004100 + index')) { throw 'Native compiled consumer missing' }
# Idempotent complete stage must not generate another coordinator.
$repeat = $fn.Invoke($null,[object[]]@((NewCtx 'repeatable-cyberpsychos' $staged)))
if ($repeat.Applied) { throw 'Pristine stage not idempotent.' }
$repeatNative = $nativeMethod.Invoke($null,[object[]]@((NewCtx 'native-location-cyberpsychos' $staged)))
if ($repeatNative.Applied) { throw 'Native worker stage not idempotent.' }
# Comments and harmless quote differences remain source-compatible.
$variation = [Collections.Generic.Dictionary[string,byte[]]]::new()
$variation['repeatable_cyberpsychos/init.lua'] = $utf8.GetBytes($init.Replace('Diagnostics.event("body_reward_failed"',"Diagnostics.event('body_reward_failed'").Replace('local function processBodyRewards(system)',"-- harmless comment`nlocal function processBodyRewards(system)"))
$variant = $fn.Invoke($null,[object[]]@((NewCtx 'repeatable-cyberpsychos' $variation)))
if (!$variant.Applied) { throw 'Equivalent formatting/quote variation incorrectly rejected.' }
# Executable control-flow drift must fail closed, not silently replace author code.
$drift = [Collections.Generic.Dictionary[string,byte[]]]::new()
$drift['repeatable_cyberpsychos/init.lua'] = $utf8.GetBytes($init.Replace('while pending > 0 and processed < 5 do','while pending >= 0 and processed < 5 do'))
$refused = $false
try { [void]$fn.Invoke($null,[object[]]@((NewCtx 'repeatable-cyberpsychos' $drift))) }
catch { $refused = $_.Exception.InnerException.Message -like '*source drifted*' }
if (!$refused) { throw 'Executable source drift incorrectly accepted.' }
Write-Host 'Cyberpsychos pristine six-job source, native consumer, exact author fallback, idempotence, source variation and drift refusal PASS'