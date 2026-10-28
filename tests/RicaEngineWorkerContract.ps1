param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-rica-engine-worker'
$folder=Join-Path $root 'repeatable_increased_criminal_activity'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder | Out-Null
$fixture=@'
local pass4AttachInProgress = false
local pass4DiagnosticsWorkerJob
local function diagnosticsWorkerTick() end
local function rewardTick() end
local function runtimeStateTick() end
local function mappinTick() end
local function scheduledDiagnosticsTick() end
local function schedulerTick() end
TryAttachRicaPass4 = function()
    if pass4AttachInProgress then return false end
    pass4AttachInProgress = true

    local created = {}
    local okAttach = pcall(function()
        local okEngine, engine = pcall(GetMod, "0-Engine")
        if not okEngine or type(engine) ~= "table" or type(engine.Register) ~= "function" then
            error("0-Engine Register API not ready")
        end
        local client = engine.Register("RICA")
        if type(client) ~= "table" or type(client.Schedule) ~= "table"
            or type(client.Schedule.Every) ~= "function"
            or type(client.Schedule.EveryFrame) ~= "function" then
            error("0-Engine Scheduler API not ready")
        end
        local diagnosticsWorkerJob = client.Schedule.EveryFrame({
            id = "rica.diagnostics_worker", active = false, pause = "never"
        }, function() diagnosticsWorkerTick() end)
        created[#created + 1] = diagnosticsWorkerJob
        pass4DiagnosticsWorkerJob = diagnosticsWorkerJob
        local rewardJob = client.Schedule.Every(0.25, {
            id = "rica.rewards", pause = "never", clock = "game", catchUp = false
        }, function() rewardTick(4, 1) end)
        created[#created + 1] = rewardJob
        local stateJob = client.Schedule.Every(1.0, {
            id = "rica.state", pause = "never", clock = "game", catchUp = false
        }, function() runtimeStateTick() end)
        created[#created + 1] = stateJob
        local mappinJob = client.Schedule.Every(1.0, {
            id = "rica.mappins", pause = "never", clock = "game", catchUp = false
        }, function() mappinTick() end)
        created[#created + 1] = mappinJob
        local diagnosticsJob = client.Schedule.Every(5.0, {
            id = "rica.diagnostics", pause = "never", clock = "game", catchUp = false
        }, function() scheduledDiagnosticsTick() end)
        created[#created + 1] = diagnosticsJob
        local systemJob = client.Schedule.Every(10.0, {
            id = "rica.system", pause = "never", clock = "game", catchUp = false
        }, function() schedulerTick() end)
        created[#created + 1] = systemJob
        pass4Attached = true
    end)

    pass4AttachInProgress = false
    if not okAttach then
        for _, handle in ipairs(created) do
            if handle and type(handle.Cancel) == "function" then
                pcall(handle.Cancel)
            end
        end
        return false
    end
    return true
end
'@
[IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$fixture,[Text.UTF8Encoding]::new($false))
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyRicaEngineWorker',$flags)
if(!$m){throw 'RICA engine worker injector missing'}
function Apply([string]$source){
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$source,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'repeatable_increased_criminal_activity',[string]'rica-engine-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$r=$m.Invoke($null,[object[]]@($ctx))}catch{throw "RICA worker generation failed: $($_.Exception.InnerException.Message)"}
 return @{result=$r;changes=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $fixture
if(!$first.result.Applied -or $first.changes.Count -ne 2){throw 'RICA expected bridge and worker outputs'}
$bridge=@($first.changes|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.changes|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/rica_engine_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'RICA worker/bridge files missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('require("GCET_Semantic/rica_engine_worker")','pass4Attached = true','pcall(handle.Cancel)','pass4AttachInProgress = false')){if(!$b.Contains($x)){throw "RICA bridge missing $x"}}
foreach($x in @('G-CET semantic coordinator:rica-engine-worker','rica.diagnostics_worker','rica.rewards','rica.state','rica.mappins','rica.diagnostics','rica.system','Schedule.Every(10.0','Schedule.Every(0.25','created[#created + 1]')){if(!$w.Contains($x)){throw "RICA worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/rica_engine_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'RICA worker injected twice'}
# The pristine RICA source generator retains quarter-second reward handling
# when native pending hints are set. Empty ticks may skip costly entity
# resolution, but every fourth tick MUST run a full author-equivalent poll.
$primarySource = Get-Content -LiteralPath 'src/G.CETProfiler.Core/Services/ModPasses/RepeatableIncreasedCriminalActivity.cs' -Raw
foreach ($required in @(
    'G-CET original-1Hz-safe RICA reward idle prefilter v1',
    'GetPendingBodyRewardSiteID(), system:GetPendingRewardSerial()',
    'if ricaRewardIdleChecks < 4 then return end',
    'processBodyRewards(system, 4)',
    'processCompletionRewards(system, 1)'
)) {
    if (!$primarySource.Contains($required)) {
        throw "RICA pristine owner lost 1Hz reward fail-open guarantee: $required"
    }
}
if (!$primarySource.Contains('type(completionSerial) == \"number\"') -or
    !$primarySource.Contains('local ok, bodySite, completionSerial = pcall(function()')) {
    throw 'RICA native reward pending-hint guard must fail open on invalid native responses'
}
Write-Host 'RICA six-job worker and idempotent bridge: PASS'
