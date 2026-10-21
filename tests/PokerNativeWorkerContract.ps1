param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-poker-native-worker'
$folder=Join-Path $root 'Street Games Poker'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $folder | Out-Null
$source=@'
-- poker.world_far poker.world_near poker.seat_near
StreetTables.locations={}
local function attachPoker()
    local lookup=engine.GCET.NativeLocationHost
    if lookup then
      lookup:attachCompiled(10031,#StreetTables.locations,
        function()
          __gcetPokerNativeOwned=true
          __gcetPokerNativeNear=true
          if pokerRuntimeReady() then setNearMode(true) end
        end,
        function()
          __gcetPokerNativeOwned=true
          __gcetPokerNativeNear=false
          if pokerRuntimeReady() then setNearMode(false) end
        end,
        function()
          __gcetPokerNativeOwned=false
          __gcetPokerNativeNear=false
          if farWorldJob then farWorldJob.Resume() end
          return true
        end)
    end
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyPokerNativeWorker',$flags)
if(!$m){throw 'Poker native worker injector missing'}
function Apply([string]$sourceText){
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$sourceText,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Street Games Poker',[string]'poker-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try {$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Poker native worker: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $source
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Poker expected bridge and worker'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/poker_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Poker worker/bridge absent'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('GCET_Semantic/poker_native_worker','__gcetPokerNativeOwned=false','return true','poker.world_far')){if(!$b.Contains($x)){throw "Poker bridge missing $x"}}
foreach($x in @('G-CET semantic coordinator:poker-native-worker','host:attachCompiled(10031, count','onNear, onFar, onUnknown')){if(!$w.Contains($x)){throw "Poker worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/poker_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Poker worker regenerated completed stage'}
Write-Host 'Poker worker, original callbacks and idempotence: PASS'
