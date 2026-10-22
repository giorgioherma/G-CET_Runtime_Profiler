param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-extracted-native-completion'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force $root|Out-Null

$assembly=[Reflection.Assembly]::LoadFrom(
    (Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$structural=$assembly.GetType('GCETRuntimeProfiler.Core.Services.NativeLocationStructuralSource',$true)
$contextType=$assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$complete=$structural.GetMethod('CompleteStage',$flags)
$completedContext=$structural.GetMethod('CompleteStageWithCoordinator',$flags)
$tryPoker=$structural.GetMethod('TryPoker',$flags)
$tryDedka=$structural.GetMethod('TryDedka',$flags)
$constructor=@($contextType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|
    Where-Object{$_.GetParameters().Count -eq 4})[0]
if(!$complete -or !$completedContext -or !$tryPoker -or !$tryDedka -or !$constructor){
    throw 'Native extracted stage reflection targets missing'
}

$pokerSource=@'
-- poker.world_far poker.world_near poker.seat_near StreetTables.update
-- G-CET native location:poker-structural-v1
local __gcetPokerNativeOwned=false
local __gcetPokerNativeNear=false
farWorld.Resume()
-- G-CET semantic:poker-native-worker-v1
require("GCET_Semantic/poker_native_worker").attach(lookup,#StreetTables.locations,
  function() __gcetPokerNativeOwned=true end,
  function() __gcetPokerNativeOwned=true end,
  function() __gcetPokerNativeOwned=false;farWorld.Resume();return true end)
'@
$pokerWorker=@'
-- G-CET semantic coordinator:poker-native-worker
local M={}
function M.attach(host,count,onNear,onFar,onUnknown)
  local ok, attached=pcall(function()
    return host:attachCompiled(10031,count,onNear,onFar,onUnknown)
  end)
  return ok and attached == true
end
return M
'@
$dedkaSource=@'
-- G-CET native location:dedka-structural-v1
-- G-CET semantic:dedka-native-worker-v1
local function startShopDetail() end
local function stopShopDetail() end
local function registerWakeZone(id,pos)
   return engine.RegisterZone({radius=200,onEnter=function() startShopDetail() end,
      onExit=function() stopShopDetail() end})
end
registerWakeZone("dedka_entrance1",ENTRANCE_POS)
registerWakeZone("dedka_entrance2_cluster",ENTRANCE_POS_2)
local function __gcetDedkaRetire() end
local function __gcetDedkaRestore() end
require("GCET_Semantic/dedka_native_worker").attach(lookup,
   function() __gcetDedkaRetire(); startShopDetail() end,
   function() __gcetDedkaRetire(); stopShopDetail() end,
   function() __gcetDedkaRestore(); return true end)
'@
$dedkaWorker=@'
-- G-CET semantic coordinator:dedka-native-worker
local M={}
function M.attach(host,onNear,onFar,onUnknown)
  local ok, attached=pcall(function()
    return host:attachCompiled(10034,2,onNear,onFar,onUnknown)
  end)
  return ok and attached == true
end
return M
'@
function IsComplete($rule,$source,$worker){
    $parts=[string[]]@($source)
    if($null -ne $worker){$parts=[string[]]@($source,$worker)}
    return [bool]$complete.Invoke($null,[object[]]@([string]$rule,$parts))
}
function TestPair([string]$owner,[string]$rule,[string]$workerFile,
                  [string]$source,[string]$worker,$testMethod){
    if(IsComplete $rule $source $null){throw "$owner matched without coordinator"}
    if(!(IsComplete $rule $source $worker)){throw "$owner full source+worker pair not recognized"}
    if(IsComplete $rule $source ($worker.Replace('attachCompiled','attachDynamic'))) {
        throw "$owner accepted worker without compiled native ownership"
    }
    if(IsComplete $rule $source ($worker.Replace('semantic coordinator:','semantic coordinator:foreign-'))) {
        throw "$owner accepted foreign coordinator owner"
    }
    if(IsComplete $rule ($source.Replace('GCET_Semantic/','wrong/')) $worker){
        throw "$owner accepted a different source bridge"
    }
    $folder=Join-Path $root $owner
    New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
    [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $folder "GCET_Semantic/$workerFile"),$worker,[Text.UTF8Encoding]::new($false))
    $ctx=$constructor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,
        [Collections.Generic.Dictionary[string,byte[]]]::new()))
    if(![bool]$completedContext.Invoke($null,[object[]]@($ctx,$rule,$source))){
        throw "$owner live source-owner coordinator not recognized"
    }
    $result=$testMethod.Invoke($null,[object[]]@($ctx))
    if($result.Applied -or $result.Reason -notlike '*already satisfied*'){
        throw "$owner completed native stage was not safely skipped: $($result.Reason)"
    }
    # Staged worker must be accepted even before a physical worker exists.
    Remove-Item (Join-Path $folder "GCET_Semantic/$workerFile") -Force
    $staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
    $staged.Add("$owner/GCET_Semantic/$workerFile",[Text.Encoding]::UTF8.GetBytes($worker))
    $stagedCtx=$constructor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$staged))
    if(![bool]$completedContext.Invoke($null,[object[]]@($stagedCtx,$rule,$source))){
        throw "$owner freshly staged coordinator was not recognized"
    }
    # A missing worker MUST still refuse, not claim complete.
    $noWorkerCtx=$constructor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,
        [Collections.Generic.Dictionary[string,byte[]]]::new()))
    if([bool]$completedContext.Invoke($null,[object[]]@($noWorkerCtx,$rule,$source))){
        throw "$owner accepted missing coordinator"
    }
}
TestPair 'StreetGamesPoker' 'native-location-poker' 'poker_native_worker.lua' $pokerSource $pokerWorker $tryPoker
TestPair 'Dedka Auto Shop' 'native-location-dedka' 'dedka_native_worker.lua' $dedkaSource $dedkaWorker $tryDedka
Write-Host 'Poker/Dedka extracted native source-worker pair, staged/live ownership, incomplete/foreign refusal: PASS'
