param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-yaiba-native-worker'
$folder=Join-Path $root 'Yaiba'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $folder | Out-Null
$fixture=@'
local __gcetBathNativeFar=nil
function bathtub.init()
    ui.init()
    local ok,engine=pcall(GetMod,"0-Engine")
    local lookup=ok and engine and engine.GCET and engine.GCET.NativeLocationHost
    if lookup and type(lookup.attachCompiled)=="function" then
        -- The eye-height corrected anchor is already in GCAS.
        lookup:attachCompiled(10037,1,function() __gcetBathNativeFar=false end,
            function() __gcetBathNativeFar=true end,
            function() __gcetBathNativeFar=nil;return true end)
    end
end
function bathtub.update(player, pPos)
    if player and pPos and not bathtub.hubVisible and not ui.hubShown and not bathtub.pinID and not bathtub.waterApplied then
        if __gcetBathNativeFar == true then return end
        if __gcetBathNativeFar == nil then
            local __gcetBathPos = CONFIG.position
            if __gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz > (CONFIG.iconRange + 1.0)^2 then return end
        end
    end
    ui.update()
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyYaibaNativeWorker',$flags)
if(!$m){throw 'Yaiba native worker injector absent'}
function Apply([string]$source){
 [IO.File]::WriteAllText((Join-Path $folder 'bathtub.lua'),$source,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Yaiba',[string]'yaiba-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$r=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Yaiba worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$r;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply $fixture
if(!$a.result.Applied -or $a.rows.Count -ne 2){throw 'Yaiba expected source and worker'}
$bridge=@($a.rows|Where-Object{$_.RelativeFile -like '*/bathtub.lua'})|Select-Object -First 1
$worker=@($a.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/yaiba_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Yaiba files missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('GCET_Semantic/yaiba_native_worker','__gcetBathNativeFar=nil;return true','bathtub.waterApplied','ui.update()','__gcetDx * __gcetDx')){if(!$b.Contains($x)){throw "Yaiba bridge missing $x"}}
foreach($x in @('host:attachCompiled(10037, 1','onKnown(true)','onKnown(false)')){if(!$w.Contains($x)){throw "Yaiba worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/yaiba_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Yaiba native worker applied twice'}
Write-Host 'Yaiba native worker and idempotence: PASS'
