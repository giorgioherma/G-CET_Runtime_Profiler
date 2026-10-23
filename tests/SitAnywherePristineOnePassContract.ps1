param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-sitanywhere-pristine-one-pass'
$mod=Join-Path $root 'sitAnywhere'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force (Join-Path $mod 'modules/external') | Out-Null
function Put($name,$src) {
  $full=Join-Path $mod $name
  $dir=Split-Path $full -Parent
  New-Item -ItemType Directory -Force $dir | Out-Null
  [IO.File]::WriteAllText($full,$src,[Text.UTF8Encoding]::new($false))
}
# This is original-shaped author source, with NO pre-existing PASS4
# coordinator, scheduler, G-CET marker, or injected worker.
$original=@'
local Cron = require("modules/external/Cron")
local interaction = require("modules/interactionUI")
local world = require("modules/worldInteraction")
function sit:new()
    registerForEvent("onInit", function()
        self.logic:init()
    end)
    registerForEvent("onUpdate", function(dt)
        if not self.runtimeData.inMenu and self.runtimeData.inGame then
            Cron.Update(dt)
            interaction.update()
            world.update()
            self.logic:onUpdate()
            for _, spot in pairs(self.logic.sittables) do
                spot.workspot.yaw = self.yaw
                spot.workspot.pitch = self.pitch
                spot:update(dt)
            end
        end
    end)
end
'@
Put 'init.lua' $original
Put 'modules/external/Cron.lua' @'
local timers = {}
function Cron.Update(dt) end
function Cron.Halt() end
'@
Put 'modules/logic.lua' @'
local logic = {}
function logic:new()
    local o = {}
    o.sittables = {}
    return o
end
function logic:hideAllWorkspots()
    self.sittables = {}
end
function logic:onUpdate()
    if position then
        update()
    end
end
'@
Put 'modules/worldInteraction.lua' @'
local world = {}
world.interactions = {}
function world.update() end
function world.onSessionStart() end
'@
$files=@('init.lua','modules/external/Cron.lua','modules/logic.lua','modules/worldInteraction.lua')
$sha=@{}
foreach($f in $files){$sha[$f]=(Get-FileHash (Join-Path $mod $f) -Algorithm SHA256).Hash}
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inj=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$method=$inj.GetMethod('ApplySitAnywhere',$flags)
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|
 Where-Object {$_.GetParameters().Count -eq 4})[0]
if(!$method -or !$ctor){throw 'SitAnywhere authoritative generator inaccessible'}
function MakeCtx($stage) {
 return $ctor.Invoke([object[]]@([string]$root,[string]'sitAnywhere',[string]'sitanywhere',$stage))
}
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
$ctx=MakeCtx $staged
$result=$method.Invoke($null,[object[]]@($ctx))
$changes=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
if(!$result.Applied -or $changes.Count -lt 5){throw 'Untouched SitAnywhere was not fully optimized in one generation'}
foreach($change in $changes){$staged[$change.RelativeFile]=$change.Bytes}
$init=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/init.lua'])
$worker=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua'])
foreach($token in @('local function runSitAnywhereFrame(dt)',
 'TryAttachSitAnywherePass4 = function()',
 'sitAnywhereBaseInitialized = true',
 'if pass4Attached then return end',
 'runSitAnywhereFrame(dt)',
 'require("GCET_Semantic/sitanywhere_engine_worker")',
 'G-CET semantic:sitanywhere-engine-worker-v1')) {
 if(!$init.Contains($token)){throw "Pristine output missing full scheduler stage: $token"}
}
foreach($token in @('Schedule.EveryFrame','sitanywhere.realtime',
 'created[#created + 1] = job')) {
 if(!$worker.Contains($token)){throw "Pristine worker incomplete: $token"}
}
$second=MakeCtx $staged
$secondResult=$method.Invoke($null,[object[]]@($second))
if($secondResult.Applied -or $secondResult.Reason -notlike '*already installed*') {
 throw 'Complete one-pass scheduler must be idempotent'
}
$staged.Remove('sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua')|Out-Null
$missing=MakeCtx $staged
$refused=$false
try{$null=$method.Invoke($null,[object[]]@($missing))}catch{
 if($_.Exception.ToString() -match 'partially installed'){$refused=$true}
}
if(!$refused){throw 'Missing worker was silently considered complete'}
foreach($f in $files) {
 if((Get-FileHash (Join-Path $mod $f) -Algorithm SHA256).Hash -ne $sha[$f]){
   throw "Generator touched original file: $f"
 }
}
Write-Host 'SitAnywhere pristine single-pass scheduler, worker, fallback, idempotence, missing-worker refusal, original preservation PASS'
