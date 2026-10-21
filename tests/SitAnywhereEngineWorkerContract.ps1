param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-sit-engine-worker'
$folder=Join-Path $root 'sitAnywhere'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $folder | Out-Null
$fixture=@'
local function runSitAnywhereFrame(dt)
    if self.runtimeData.inMenu or not self.runtimeData.inGame then return end
    if not scanning and not inWorkspot and not inTransition and not hasTimers
        and not world.hasVisibleState() then return end
    self.logic:onUpdate()
end
TryAttachSitAnywherePass4 = function()
    if not sitAnywhereBaseInitialized then return false end
    if pass4Attached then return true end
    if pass4AttachInProgress then return false end
    pass4AttachInProgress = true

        local created = {}
        local okAttach = pcall(function()
            local okEngine, engine = pcall(GetMod, "0-Engine")
            if not okEngine or type(engine) ~= "table" or type(engine.Register) ~= "function" then
                error("0-Engine Register API not ready")
            end
            local client = engine.Register("sitAnywhere")
            if type(client) ~= "table" or type(client.Schedule) ~= "table"
                or type(client.Schedule.EveryFrame) ~= "function" then
                error("0-Engine Scheduler API not ready")
            end
            local frameJob = client.Schedule.EveryFrame({
                id = "sitanywhere.realtime",
                pause = "never"
            }, function(ctx)
                runSitAnywhereFrame(ctx.dt or 0)
            end)
            table.insert(created, frameJob)

            pass4FrameJob = frameJob
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
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ct=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ct.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object {$_.GetParameters().Count -eq 4})[0]
$m=$t.GetMethod('ApplySitAnywhereEngineWorker',$flags)
function Apply([string]$src) {
 [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$src,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'sitAnywhere',[string]'sitanywhere-engine-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "SitAnywhere worker failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ct.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $fixture
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'SitAnywhere expected two staged files'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/sitanywhere_engine_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'SitAnywhere bridge/worker absent'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('require("GCET_Semantic/sitanywhere_engine_worker")','pass4FrameJob','pass4Attached = true','pcall(handle.Cancel)','runSitAnywhereFrame(dt)')){if(!$b.Contains($x)){throw "Bridge missing $x"}}
foreach($x in @('G-CET semantic coordinator:sitanywhere-engine-worker','engine.Register("sitAnywhere")','id = "sitanywhere.realtime"','onFrame(ctx.dt or 0)','table.insert(created, job)')){if(!$w.Contains($x)){throw "Worker missing $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/sitanywhere_engine_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'SitAnywhere worker injected twice'}
Write-Host 'SitAnywhere 0-Engine worker, rollback and idempotence: PASS'
