param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-cyberstunts-worker'
$folder=Join-Path $root 'CyberStunts'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder|Out-Null
$fixture=@'
local __gcetStuntsFixedCount=#jumps
local __gcetStuntsNativeFar=nil
local function __gcetStuntsLocationSleeping()
    return __gcetStuntsNativeFar==true and not isJumping and
        not __gcetStuntsUserEdited and #jumps==__gcetStuntsFixedCount
end
local function __gcetStuntsAttachNative()
    if #jumps~=(__gcetStuntsFixedCount) then return end
    local ok,engine=pcall(GetMod,"0-Engine")
    local host=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
    if host and type(host.attachCompiled)=="function" then
        host:attachCompiled(10039,__gcetStuntsFixedCount,
            function() __gcetStuntsNativeFar=false;wasVehicleAirborne=nil end,
            function() __gcetStuntsNativeFar=true;wasVehicleAirborne=nil end,
            function() __gcetStuntsNativeFar=nil;wasVehicleAirborne=nil;return true end)
    end
end
function SaveUserJumps()
  __gcetStuntsUserEdited=true
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyCyberStuntsNativeWorker',$flags)
if(!$m){throw 'CyberStunts worker injector missing'}
function Apply([string]$source){
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'CyberStunts',[string]'cyberstunts-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "CyberStunts worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply $fixture
if(!$a.result.Applied -or $a.rows.Count -ne 2){throw 'CyberStunts expected bridge and worker'}
$bridge=@($a.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($a.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/cyberstunts_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'CyberStunts generated files missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('GCET_Semantic/cyberstunts_native_worker','__gcetStuntsUserEdited','wasVehicleAirborne=nil','SaveUserJumps()')){if(!$b.Contains($x)){throw "CyberStunts bridge missing $x"}}
foreach($x in @('host:attachCompiled(10039, count','onKnown(true)','onKnown(false)','onUnknown')){if(!$w.Contains($x)){throw "CyberStunts worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/cyberstunts_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'CyberStunts native worker applied twice'}
Write-Host 'CyberStunts native worker and idempotence: PASS'
