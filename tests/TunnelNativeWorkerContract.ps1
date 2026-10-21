param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-tunnel-worker'
$folder=Join-Path $root 'Tunnel'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $folder | Out-Null
$source=@'
local __gcetTunnelNativeOutside=nil
local function __gcetTunnelFarOutside()
 if fact('reunion_pending')==1 then return false end
 if __gcetTunnelNativeOutside ~= nil then return __gcetTunnelNativeOutside end
 local site=L.rescue and L.rescue.surface
 return false
end
local function __gcetTunnelAttachNative()
    local ok,engine=pcall(GetMod,"0-Engine")
    local lookup=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
    if not lookup or type(lookup.attachCompiled)~='function' then return end
    local site=L.rescue and L.rescue.surface
    local giver=L.rescue and L.rescue.brother
    if type(site)~="table" or type(giver)~="table" then return end
    lookup:attachCompiled(10032,2,function() __gcetTunnelNativeOutside=false end,
              function() __gcetTunnelNativeOutside=true end,
              function() __gcetTunnelNativeOutside=nil;return true end)
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyTunnelNativeWorker',$flags)
function Apply([string]$content) {
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$content,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Tunnel',[string]'tunnel-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Tunnel worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $source
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Tunnel expected source bridge and coordinator'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/tunnel_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Tunnel outputs absent'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('GCET_Semantic/tunnel_native_worker','if fact(''reunion_pending'')==1 then return false end','__gcetTunnelNativeOutside=nil;return true')){if(!$b.Contains($token)){throw "Tunnel bridge omitted $token"}}
foreach($token in @('G-CET semantic coordinator:tunnel-native-worker','host:attachCompiled(10032, 2','onKnown(true)','onKnown(false)')){if(!$w.Contains($token)){throw "Tunnel worker omitted $token"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic') | Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/tunnel_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Tunnel worker reapplied'}
Write-Host 'Tunnel native tri-state worker, bridge and idempotence: PASS'
