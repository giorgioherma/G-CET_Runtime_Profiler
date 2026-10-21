param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-dedka-native-worker'
$folder=Join-Path $root 'Dedka'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $folder|Out-Null
$fixture=@'
local function startShopDetail() end
local function stopShopDetail() end
local function registerDedka()
  local __gcetDedkaNativeZone1=makeZone("dedka_entrance1",entrancePos)
  local __gcetDedkaNativeZone2=makeZone("dedka_entrance2_cluster",entrancePos_2)
  local function __gcetDedkaRetire()
    return true
  end
  local function __gcetDedkaRestore()
    return true
  end
  local lookup=engine.GCET and engine.GCET.NativeLocationHost
  if lookup and type(lookup.attachCompiled)=="function" then
    lookup:attachCompiled(10034,2,
      function()
        if not __gcetDedkaRetire() then return false end
        shopNearCount=1;startShopDetail()
      end,
      function()
        if not __gcetDedkaRetire() then return false end
        shopNearCount=0;stopShopDetail()
      end,
      function() return __gcetDedkaRestore() end)
  end
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyDedkaNativeWorker',$flags)
if(!$m){throw 'Dedka injector absent'}
function Apply([string]$source){
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Dedka',[string]'dedka-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Dedka native worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply $fixture
if(!$a.result.Applied -or $a.rows.Count -ne 2){throw 'Dedka expected two generated files'}
$bridge=@($a.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($a.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/dedka_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Dedka bridge/worker missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('GCET_Semantic/dedka_native_worker','__gcetDedkaRetire()','__gcetDedkaRestore()','startShopDetail()','stopShopDetail()')){if(!$b.Contains($token)){throw "Dedka bridge missing $token"}}
foreach($token in @('G-CET semantic coordinator:dedka-native-worker','host:attachCompiled(10034, 2, onNear, onFar, onUnknown)')){if(!$w.Contains($token)){throw "Dedka worker missing $token"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/dedka_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Dedka duplicated coordinator'}
Write-Host 'Dedka native zone coordinator and idempotence: PASS'
