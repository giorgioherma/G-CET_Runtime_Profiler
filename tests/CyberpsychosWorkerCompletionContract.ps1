param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-cyberpsycho-worker-completion'
$folder=Join-Path $root 'repeatable_cyberpsychos'
if(Test-Path $root){Remove-Item $root -Force -Recurse}
New-Item -ItemType Directory -Force (Join-Path $folder 'modules')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'init.lua'),@'
local Sites = require("./modules/sites.lua")
local function TryAttachRepeatablePass4() return true end
local function init()
    BossWatchdog.init(Bridge, Diagnostics)
end
'@,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $folder 'modules/boss_watchdog.lua'),@'
local function syncSiteWithPosition(system, site, status, state, triggerDistance)
    local trackedID, trackedEntity = tracked(system, site.id)
    if status == 1 then
        -- PASS4-R4:
        if trackedEntity then return end
    end
end
function Watchdog.clear()
end
'@,[Text.UTF8Encoding]::new($false))
$asm=[Reflection.Assembly]::LoadFrom(
 (Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$m=$t.GetMethod('ApplyCyberpsychosNativeLocations',$flags)
$ctor=@($ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|
  Where-Object {$_.GetParameters().Count -eq 4})[0]
function NewCtx($stage) {
 return $ctor.Invoke([object[]]@([string]$root,[string]'repeatable_cyberpsychos',
  [string]'native-location-cyberpsychos',$stage))
}
$empty=[Collections.Generic.Dictionary[string,byte[]]]::new()
$ctx=NewCtx $empty
$first=$m.Invoke($null,[object[]]@($ctx))
$changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
if(!$first.Applied -or $changes.Count -ne 3){
 throw 'Cyberpsycho pristine native fallback must generate init, watchdog and owned worker'
}
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
foreach($change in $changes){$staged.Add($change.RelativeFile,$change.Bytes)}
$init=[Text.Encoding]::UTF8.GetString($staged['repeatable_cyberpsychos/init.lua'])
$watchdog=[Text.Encoding]::UTF8.GetString($staged['repeatable_cyberpsychos/modules/boss_watchdog.lua'])
if(!$init.Contains('-- G-CET native location:cyberpsychos-fallback-v1') -or
   !$watchdog.Contains('-- G-CET native location:cyberpsychos-fallback-v1')){
 throw 'Native fallback completion marker is missing from one of the two author files'
}
$next=NewCtx $staged
$again=$m.Invoke($null,[object[]]@($next))
if($again.Applied -or $again.Reason -notlike '*already installed*'){
 throw 'Cyberpsychos completed init+watchdog+worker not recognized'
}
$staged.Remove('repeatable_cyberpsychos/GCET_Semantic/cyberpsycho_native_wake.lua')|Out-Null
$missing=NewCtx $staged
$refused=$false
try {[void]$m.Invoke($null,[object[]]@($missing))}catch {
 $refused=$_.Exception.InnerException.Message -like '*missing or incompatible*'
}
if(!$refused){throw 'Cyberpsychos incorrectly accepted missing native worker'}
Write-Host 'Cyberpsychos native source/worker completion, fresh init marker and missing-worker refusal PASS'
