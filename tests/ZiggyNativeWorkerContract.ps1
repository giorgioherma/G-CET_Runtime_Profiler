param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-ziggy-native-worker'
$folder=Join-Path $root 'Ziggy'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $folder | Out-Null
$fixture=@'
local __gcetZiggyNativeOfferedFar=nil
local function __gcetZiggyOfferedFar()
 if H.s.phase~='offered' then return false end
 if tostring(p:GetEntityID().hash)~=H.playerKey then return false end
 if __gcetZiggyNativeOfferedFar ~= nil then return __gcetZiggyNativeOfferedFar end
 local pos=p:GetWorldPosition()
 return false
end
local function __gcetZiggyAttachNative()
 local ok,engine=pcall(GetMod,'0-Engine')
 local lookup=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
 local loc=C.giver and C.giver.position
 if not lookup or type(lookup.attachCompiled)~='function' or
    type(loc)~='table' then return end
 lookup:attachCompiled(10038,1,function() __gcetZiggyNativeOfferedFar=false end,
   function() __gcetZiggyNativeOfferedFar=true end,
   function() __gcetZiggyNativeOfferedFar=nil;return true end)
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyZiggyNativeWorker',$flags)
if(!$m){throw 'Ziggy native worker injector absent'}
function Apply([string]$source){
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Ziggy',[string]'ziggy-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Ziggy native worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply $fixture
if(!$a.result.Applied -or $a.rows.Count -ne 2){throw 'Expected Ziggy source and worker'}
$bridge=@($a.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($a.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/ziggy_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Ziggy generated files missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('GCET_Semantic/ziggy_native_worker','H.s.phase','H.playerKey','__gcetZiggyNativeOfferedFar=nil;return true')){if(!$b.Contains($x)){throw "Ziggy bridge missing $x"}}
foreach($x in @('host:attachCompiled(10038, 1','onKnown(true)','onKnown(false)')){if(!$w.Contains($x)){throw "Ziggy worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/ziggy_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Ziggy regenerated completed stage'}
Write-Host 'Ziggy native worker and idempotence: PASS'
