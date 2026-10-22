param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-quest-worker'
$folder=Join-Path $root 'QuestRunner'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder | Out-Null
$fixture=@'
local pass5ManagerIdleAccumulator = 0.0
local pass5SpawnerAccumulator = 0.0
__gcetRegisterEvent_717("onUpdate", function(dt)
  if Runner.gameState.inMenu or not Runner.gameState.inGame or not Runner.isReady then return end
  Cron.Update(dt)
  -- PASS5 R10: active quests preserve rendered-frame updates. With no current
  -- quest, Manager only refreshes triggers every ~10 legacy frames, so drive the
  -- same idle work at 5 Hz with accumulated time instead of entering every frame.
  if Manager.current then
    pass5ManagerIdleAccumulator = 0.0
    Manager:update(dt)
  else
    pass5ManagerIdleAccumulator = pass5ManagerIdleAccumulator + (tonumber(dt) or 0)
    if pass5ManagerIdleAccumulator >= 0.20 then
      local elapsed = pass5ManagerIdleAccumulator
      pass5ManagerIdleAccumulator = 0.0
      Manager:update(elapsed)
    end
  end
  -- Spawner's own expensive body is hard-gated to one second. Feed it at 4 Hz
  -- with accumulated dt so its authored one-second cadence is unchanged.
  pass5SpawnerAccumulator = pass5SpawnerAccumulator + (tonumber(dt) or 0)
  if pass5SpawnerAccumulator >= 0.25 then
    local elapsed = pass5SpawnerAccumulator
    pass5SpawnerAccumulator = 0.0
    Spawner.Update(elapsed)
  end

  if Runner.debounce then return end
  Runner.debounce = true
end)
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplyQuestRunnerEngineWorker',$flags)
if(!$m){throw 'QuestRunner coordinator injector absent'}
function Apply([string]$text) {
  [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$text,[Text.UTF8Encoding]::new($false))
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'QuestRunner',[string]'questrunner-engine-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
  try {$r=$m.Invoke($null,[object[]]@($ctx))} catch {throw "QuestRunner injection failed: $($_.Exception.InnerException.Message)"}
  return @{result=$r;changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$a=Apply $fixture
if(!$a.result.Applied -or $a.changes.Count -ne 2){throw 'Expected two QuestRunner generated files'}
$bridge=@($a.changes|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($a.changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/questrunner_engine_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'QuestRunner worker or bridge absent'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('GCET_Semantic/questrunner_engine_worker','__gcetQuestCadence.update(dt, Manager, Spawner)','Cron.Update(dt)','if Runner.debounce then return end')){
 if(!$b.Contains($token)){throw "Bridge missing $token"}
}
if($b.Contains('pass5ManagerIdleAccumulator + ')){throw 'Legacy inline accumulator remains'}
foreach($token in @('managerIdleElapsed >= 0.20','spawnerElapsed >= 0.25','manager:update(dt)','manager:update(elapsed)','spawner.Update(elapsed)')){
 if(!$w.Contains($token)){throw "Worker missing $token"}
}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/questrunner_engine_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'QuestRunner stage injected twice'}
# The original semantic stage must also recognize this extracted worker as a
# completed stage. Without the worker it must refuse a partial installation.
$legacy=$t.GetMethod('ApplyQuestRunner',$flags)
$ctx=$ctor.Invoke([object[]]@([string]$root,[string]'QuestRunner',
    [string]'questrunner',[Collections.Generic.Dictionary[string,byte[]]]::new()))
$complete=$legacy.Invoke($null,[object[]]@($ctx))
if($complete.Applied -or $complete.Reason -notlike '*already installed*'){
 throw 'Original QuestRunner semantic did not recognize completed engine worker'
}
Remove-Item (Join-Path $folder 'GCET_Semantic/questrunner_engine_worker.lua') -Force
$missing=$ctor.Invoke([object[]]@([string]$root,[string]'QuestRunner',
    [string]'questrunner',[Collections.Generic.Dictionary[string,byte[]]]::new()))
$refused=$false
try {[void]$legacy.Invoke($null,[object[]]@($missing))}catch {
 $refused=$_.Exception.InnerException.Message -like '*missing or incompatible*'
}
if(!$refused){throw 'QuestRunner semantic accepted missing extracted worker'}
Write-Host 'QuestRunner PASS5 worker and idempotent bridge: PASS'
