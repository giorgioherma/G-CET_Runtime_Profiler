param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-questrunner-pristine-worker'
$folder=Join-Path $root 'QuestRunner'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder | Out-Null
$original=@'
local Runner = {gameState={inMenu=false,inGame=true},isReady=true}
local Manager = {current=nil}
local Spawner = {}
local Cron = {}
local ui = {hubShown=false,input=false}
registerForEvent("onUpdate", function(dt)
    if Runner.gameState.inMenu or not Runner.gameState.inGame or not Runner.isReady then return end
    Cron.Update(dt)
    ui.update()
    Manager:update(dt)
    Spawner.Update(dt)
    if Runner.debounce then return end
    Runner.debounce = true
end)
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$method=$t.GetMethod('ApplyQuestRunner',$flags)
if(!$method){throw 'QuestRunner authoritative source generator missing'}
function Apply([string]$source) {
    [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
    $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'QuestRunner',[string]'questrunner',[Collections.Generic.Dictionary[string,byte[]]]::new()))
    try {$result=$method.Invoke($null,[object[]]@($ctx))}
    catch {throw "QuestRunner pristine generation failed: $($_.Exception.InnerException.Message)"}
    return @{ result=$result; rows=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx)) }
}
$first=Apply $original
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Pristine QuestRunner must generate exactly the bridge and one worker'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/questrunner_engine_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'QuestRunner one-pass worker missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($token in @('GCET_Semantic/questrunner_engine_worker','__gcetQuestCadence.update(dt, Manager, Spawner)','if ui.hubShown or ui.input then ui.update() end','Cron.Update(dt)','if Runner.debounce then return end')){
    if(!$b.Contains($token)){throw "QuestRunner generated bridge lost $token"}
}
foreach($token in @('managerIdleElapsed >= 0.20','spawnerElapsed >= 0.25','manager:update(dt)','manager:update(elapsed)','spawner.Update(elapsed)')){
    if(!$w.Contains($token)){throw "QuestRunner worker lost $token"}
}
if($b.Contains('__gcetQuestRunnerManagerElapsed') -or $b.Contains('__gcetQuestRunnerSpawnerElapsed')){throw 'QuestRunner duplicated old inline worker state'}
if([IO.File]::ReadAllText((Join-Path $folder 'init.lua')) -ne $original){throw 'QuestRunner untouched author source was modified'}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/questrunner_engine_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'QuestRunner worker was re-applied during idempotence check'}
Remove-Item (Join-Path $folder 'GCET_Semantic/questrunner_engine_worker.lua') -Force
$refused=$false
try {[void](Apply $b)} catch {$refused=$_.Exception.Message -like '*external worker missing or incompatible*'}
if(!$refused){throw 'QuestRunner accepted a missing external worker'}
$drift=$original.Replace('Spawner.Update(dt)','Spawner.Tick(dt)')
$refused=$false
try {[void](Apply $drift)} catch {$refused=$true}
if(!$refused){throw 'QuestRunner source drift did not fail closed'}
Write-Host 'QuestRunner pristine external worker, source preservation, fallback ownership, idempotence and drift: PASS'
