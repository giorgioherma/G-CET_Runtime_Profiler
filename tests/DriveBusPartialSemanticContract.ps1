param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Missing published Resolver' }

$root=Join-Path $env:RUNNER_TEMP 'gcet-drivebus-second-generation'
if (Test-Path $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture=Join-Path $root 'CET-20990101-090900_DRIVEBUS_SECOND_PASS'
$mods=Join-Path $root 'mods'
$bus=Join-Path $mods 'DriveBus'
$zero=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$bus,
    (Join-Path $bus 'Modules'),(Join-Path $bus 'External'),$zero | Out-Null

# Generated pass must use the exact checked/pinned 0-Engine runtime.
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$in=[IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$gz=[IO.Compression.GZipStream]::new($in,[IO.Compression.CompressionMode]::Decompress)
$out=[IO.MemoryStream]::new()
try { $gz.CopyTo($out); [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$out.ToArray()) }
finally { $out.Dispose();$gz.Dispose();$in.Dispose() }

$corePath=Join-Path $bus 'Modules/core.lua'
$cronPath=Join-Path $bus 'External/Cron.lua'
$initPath=Join-Path $bus 'init.lua'
$eventPath=Join-Path $bus 'Modules/event.lua'

$core=@'
local Core = {}
function Core:SetObserve()
    local exception_in_choice_list = {}
    local exception_in_mount_list = {}
    Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
        local action_name = action:GetName(action).value
        local action_type = action:GetType(action).value
        local action_value = action:GetValue(action)
        local veh_status = self.available_event_obj:GetStatus()
        if veh_status == Def.VehicleStatus.PlayerIn then
            for _, exception in pairs(exception_in_choice_list) do
                if action_name == exception then
                    consumer:Consume()
                    break
                end
            end
            self:ChoiceAction(action_name, action_type, action_value)
        end
        if veh_status == Def.VehicleStatus.Mounted then
            for _, exception in pairs(exception_in_mount_list) do
                if action_name == exception then consumer:Consume() end
            end
        end
    end)
end
'@
$cron=@'
local Cron={}
local timers = {}
function Cron.Every(timeout, callback)
    timers[#timers+1] = { active=true, callback=callback, timeout=timeout }
end
function Cron.Update(delta)
    if #timers > 0 then
        for _, timer in ipairs(timers) do
            if timer.active then timer.callback(delta) end
        end
    end
end
return Cron
'@
$init=@'
Cron = require("External/Cron.lua")
registerForEvent("onUpdate", function(delta)
    Cron.Update(delta)
end)
'@
$event=@'
local Event = {}
function Event:Init()
    Cron.Every(0.1, {tick=1}, function(timer)
        self:CheckAllEvents()
    end)
end
'@
$event | Set-Content -LiteralPath $eventPath -Encoding utf8
$core | Set-Content -LiteralPath $corePath -Encoding utf8
$cron | Set-Content -LiteralPath $cronPath -Encoding utf8
$init | Set-Content -LiteralPath $initPath -Encoding utf8

$input=@{schemaVersion='2.0';callbacks=@(
    @{registrationId=317;owner='DriveBus';infrastructure=$false
      kind='event';target='onUpdate'
      source=@{file='init.lua';lineStart=2;lineEnd=4}
      callsPerSecond=60;exclusiveMsPerSecond=0.616396
      globalWorkSharePct=0.5;familyWorkSharePct=100
      avgExclusiveUs=10.27;maxExclusiveMs=0.4;spikeCount=0}
);optimizerEvidence=@()} | ConvertTo-Json -Depth 35
$input | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

function Generate {
    $result=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
    if (!$result.ok -or !$result.pass) { throw 'DriveBus semantic pass not generated' }
    return $result
}
function ZipText([string]$Zip,[string]$File) {
    $archive=[IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        $entry=$archive.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/'+$File)
        if (!$entry) { throw "ZIP missing $File" }
        $rd=[IO.StreamReader]::new($entry.Open())
        try {return $rd.ReadToEnd()} finally {$rd.Dispose()}
    } finally {$archive.Dispose()}
}
function RuleFiles($result) {
    $m=Get-Content -LiteralPath $result.pass.ManifestPath -Raw | ConvertFrom-Json
    $t=@($m.transforms | Where-Object {$_.RuleId -eq 'drivebus'})
    if ($t.Count -ne 1) {
        $skip=@($m.skipped | Where-Object {$_.RuleId -eq 'drivebus'})
        throw "DriveBus transform count $($t.Count) not 1; skipped=$($skip | ConvertTo-Json -Compress)"
    }
    return @($t[0].files)
}

# An authored, production-enabled semantic rule must survive profiler
# silence when this callback is registered but no invocation happened during
# the capture. Source proof, not measured popularity, controls generation.
$measuredInput = Get-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Raw
$zeroRow = $measuredInput | ConvertFrom-Json
$zeroRow.callbacks[0].callsPerSecond = 0
$zeroRow.callbacks[0].exclusiveMsPerSecond = 0
$zeroRow.callbacks[0].avgExclusiveUs = 0
$zeroRow.callbacks[0].globalWorkSharePct = 0
$zeroRow.callbacks[0].familyWorkSharePct = 0
$zeroRow | ConvertTo-Json -Depth 35 |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $zeroWork = Generate
    $zeroFiles = @(RuleFiles $zeroWork)
    if ($zeroFiles.Count -ne 4) {
        throw "Zero-observed-work DriveBus must still generate its complete safe semantic source stage; got $($zeroFiles.Count)"
    }
    $zeroReport = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw |
        ConvertFrom-Json
    $zeroCandidates = @($zeroReport.callbackFamilies | ForEach-Object { $_.topConsumers } |
        Where-Object { $_.semantic.RuleId -eq 'drivebus' })
    if ($zeroCandidates.Count -ne 1 -or
        !$zeroCandidates[0].semantic.generationReady -or
        $zeroCandidates[0].semantic.admissionLane -ne 'SEMANTIC_AUTHORED_UNMEASURED') {
        throw ('Zero-observed-work, source-proven semantic was incorrectly rejected: ' +
            ($zeroCandidates | ConvertTo-Json -Depth 8 -Compress))
    }
}
finally {
    $measuredInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}

# Explicit source-only admission: the profiler capture can omit both
# DriveBus callbacks while its current unmodified mod source still proves
# the production rule. Neither callback ownership nor measured runtime
# values may be synthesized.
$absentCapture=@{schemaVersion='2.0';callbacks=@();optimizerEvidence=@()} | ConvertTo-Json -Depth 35
$absentCapture | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $sourceOnly=Generate
    $sourceOnlyFiles=@(RuleFiles $sourceOnly)
    if ($sourceOnlyFiles.Count -ne 4) {
        throw "DriveBus missing-callback source-only admission wrote $($sourceOnlyFiles.Count) files instead of 4"
    }
    $sourceReport=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
    $sourceCandidates=@($sourceReport.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq 'drivebus'})
    # Current-source generic discovery is not a fabricated profiler capture.
    $invalidSourceFamilies=@($sourceReport.callbackFamilies|Where-Object {
        $_.sourceFirst -ne $true -or
        @($_.topConsumers|Where-Object {
            $_.sourceFirst -ne $true -or $null -ne $_.runtime
        }).Count -gt 0
    })
    if ($sourceCandidates.Count -ne 1 -or
        !$sourceCandidates[0].stageComposed -or
        $invalidSourceFamilies.Count -ne 0) {
        throw 'DriveBus source-only candidate missing or synthetic callback family generated'
    }
    $sourceManifest=Get-Content $sourceOnly.pass.ManifestPath -Raw | ConvertFrom-Json
    $sourceTransforms=@($sourceManifest.transforms | Where-Object {$_.RuleId -eq 'drivebus'})
    if ($sourceTransforms.Count -ne 1 -or
        $sourceTransforms[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
        $null -ne $sourceTransforms[0].runtimeMsPerSecond) {
        throw 'DriveBus source-only manifest invented measured callback cost or failed evidence provenance'
    }
}
finally {
    $measuredInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}

# Pristine source must receive all four author files in ONE pass.
$first=Generate
$f1=@(RuleFiles $first)
if ($f1.Count -ne 4) { throw "Pristine DriveBus wrote $($f1.Count), expected 4" }
$routed=ZipText $first.pass.ZipPath 'DriveBus/Modules/core.lua'
$guarded=ZipText $first.pass.ZipPath 'DriveBus/init.lua'
$timer=ZipText $first.pass.ZipPath 'DriveBus/External/Cron.lua'
$eventOut=ZipText $first.pass.ZipPath 'DriveBus/Modules/event.lua'
if (!$eventOut.Contains('__gcetDriveBusDiscoveryWakeV2') -or
    !$routed.Contains('__gcetDriveBusActionDormancyV2') -or
    !$routed.Contains('pcall(handle.SetActive, needed)')) {
    throw 'Pristine DriveBus native subscriber/dormancy wake stage missing'
}
foreach ($token in @('SubscribeAction','gcetDriveBusOnAction',
    'Observe("PlayerPuppet", "OnAction", gcetDriveBusOnAction)')) {
    if (!$routed.Contains($token)) { throw "Pristine DriveBus missing: $token" }
}
if (!$guarded.Contains('if Cron.HasActiveTimers() then Cron.Update(delta) end') -or
    !$timer.Contains('function Cron.HasActiveTimers()')) {
    throw 'Pristine DriveBus timer guard incomplete'
}

# Simulate an already-routed historical v1 source without the new C stage.
# Preserve its old route and existing native fallback; add only missing
# stage postconditions with one generation pass.
$olderRouted=[regex]::Replace($routed, '(?ms)\r?\n    -- __gcetDriveBusActionDormancyV2:.*?^    end\r?\n', '')
$olderRouted=$olderRouted.Replace('__gcetDriveBusActionHandle = __gcetClient_gcetDriveBusOnAction.SubscribeAction',
    '__gcetClient_gcetDriveBusOnAction.SubscribeAction')
$olderRouted=$olderRouted.Replace('__gcetDriveBusActionHandle = __gcetEngine_gcetDriveBusOnAction.SubscribeAction',
    '__gcetEngine_gcetDriveBusOnAction.SubscribeAction')
if ($olderRouted.Contains('__gcetSyncActionDormancy') -or
    !$olderRouted.Contains('gcetDriveBusOnAction')) { throw 'Could not construct routed pre-C-stage source' }
$olderRouted | Set-Content -LiteralPath $corePath -Encoding utf8
$cron | Set-Content -LiteralPath $cronPath -Encoding utf8
$init | Set-Content -LiteralPath $initPath -Encoding utf8
$event | Set-Content -LiteralPath $eventPath -Encoding utf8
$hashBefore=(Get-FileHash $corePath -Algorithm SHA256).Hash
# A previously routed DriveBus with missing timer stages must also
# remain eligible when the capture has no DriveBus callback records.
$absentCapture | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $partialNoCapture=Generate
    $partialNoCaptureFiles=@(RuleFiles $partialNoCapture)
    if ($partialNoCaptureFiles.Count -ne 4 -or
        'DriveBus/Modules/core.lua' -notin $partialNoCaptureFiles -or
        'DriveBus/Modules/event.lua' -notin $partialNoCaptureFiles -or
        'DriveBus/External/Cron.lua' -notin $partialNoCaptureFiles -or
        'DriveBus/init.lua' -notin $partialNoCaptureFiles) {
        throw "Capture-absent DriveBus must extend existing routed core with subscriber dormancy and update timer/wake stages: $($partialNoCaptureFiles -join ',')"
    }
}
finally {
    $measuredInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}
$partial=Generate
$f2=@(RuleFiles $partial)
if ($f2.Count -ne 4 -or
    'DriveBus/Modules/event.lua' -notin $f2 -or
    'DriveBus/External/Cron.lua' -notin $f2 -or 'DriveBus/init.lua' -notin $f2 -or
    'DriveBus/Modules/core.lua' -notin $f2) {
    throw "Partially routed DriveBus must extend exactly existing subscriber and timer/wake stages: $($f2 -join ',')"
}
if ((Get-FileHash $corePath -Algorithm SHA256).Hash -ne $hashBefore) {
    throw 'Resolver mutated the currently installed source instead of emitting a separate pass'
}
$extendedCore=ZipText $partial.pass.ZipPath 'DriveBus/Modules/core.lua'
if (!$extendedCore.Contains('__gcetDriveBusActionDormancyV2') -or
    !$extendedCore.Contains('gcetDriveBusOnAction') -or
    !$extendedCore.Contains('Observe("PlayerPuppet", "OnAction", gcetDriveBusOnAction)')) {
    throw 'Existing DriveBus route and native fallback were not preserved during C-stage extension'
}
$extendedCore | Set-Content -LiteralPath $corePath -Encoding utf8
(ZipText $partial.pass.ZipPath 'DriveBus/External/Cron.lua') |
    Set-Content -LiteralPath $cronPath -Encoding utf8
(ZipText $partial.pass.ZipPath 'DriveBus/init.lua') |
    Set-Content -LiteralPath $initPath -Encoding utf8
(ZipText $partial.pass.ZipPath 'DriveBus/Modules/event.lua') |
    Set-Content -LiteralPath $eventPath -Encoding utf8

# Once all source stages are present, an absent callback row must
# not offer this already-complete mod again.
$absentCapture | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $completeAbsent=(& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
    if (!$completeAbsent.ok) { throw 'DriveBus complete empty-capture analysis failed' }
    $completeAbsentReport=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
    if (@($completeAbsentReport.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq 'drivebus'}).Count -ne 0) {
        throw 'Already complete DriveBus remains falsely source-only eligible'
    }
}
finally {
    $measuredInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}
$analyzed=(& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
if (!$analyzed.ok) { throw 'DriveBus completed source analysis failed' }
$report=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$completed=@($report.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.semantic.RuleId -eq 'drivebus' -and $_.semantic.AlreadySatisfied})
if ($completed.Count -ne 1) {
    throw 'DriveBus routed+timer source was not recognized as already satisfied'
}

# Historical v1 source uses a different callback local/action-table shape;
# extend its existing route, never require a pristine predecessor pass.
$legacyCore=$olderRouted.Replace('gcetDriveBusOnAction','zeroEngineOnAction')
$legacyCore=$legacyCore -replace 'local __gcetActions_zeroEngineOnAction = \{ ([^\r\n]*) \}',
    'local zeroEngineConfig = { actions = { $1 } }'
$legacyCore=$legacyCore.Replace('__gcetActions_zeroEngineOnAction','zeroEngineConfig.actions')
$legacyCore=$legacyCore.Replace('__gcetClient_zeroEngineOnAction','zeroEngineMod')
$legacyCore=$legacyCore.Replace('__gcetEngine_zeroEngineOnAction','zeroEngine')
$legacyCore=$legacyCore -replace '(?m)^-- G-CET semantic:drivebus\r?\n',''
if (!$legacyCore.Contains('local zeroEngineConfig') -or
    !$legacyCore.Contains('local function zeroEngineOnAction')) {
    throw 'Historical routed DriveBus fixture was not constructed'
}
$legacyCore | Set-Content -LiteralPath $corePath -Encoding utf8
$cron | Set-Content -LiteralPath $cronPath -Encoding utf8
$init | Set-Content -LiteralPath $initPath -Encoding utf8
$event | Set-Content -LiteralPath $eventPath -Encoding utf8
$legacyHash=(Get-FileHash $corePath -Algorithm SHA256).Hash
$legacy=Generate
$f3=@(RuleFiles $legacy)
if ($f3.Count -ne 4 -or 'DriveBus/Modules/core.lua' -notin $f3) {
    throw 'Historical DriveBus must receive new subscriber/wake stages without rerouting'
}
if ((Get-FileHash $corePath -Algorithm SHA256).Hash -ne $legacyHash) {
    throw 'Historical DriveBus source was mutated in place'
}
$legacyExtended=ZipText $legacy.pass.ZipPath 'DriveBus/Modules/core.lua'
if (!$legacyExtended.Contains('local function zeroEngineOnAction') -or
    !$legacyExtended.Contains('Observe("PlayerPuppet", "OnAction", zeroEngineOnAction)') -or
    !$legacyExtended.Contains('__gcetDriveBusActionDormancyV2')) {
    throw 'Historical DriveBus local route or fallback lost during subscriber stage'
}

# A broken fallback is NOT a successfully completed action stage.
$invalid=$legacyCore.Replace(
    'Observe("PlayerPuppet", "OnAction", zeroEngineOnAction)',
    'Observe("PlayerPuppet", "OnAction", OtherHandler)')
if ($invalid -eq $legacyCore) { throw 'Historical fallback drift fixture unchanged' }
$invalid | Set-Content -LiteralPath $corePath -Encoding utf8
$invalidRun=(& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
$invalidReport=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$invalidComplete=@($invalidReport.callbackFamilies | ForEach-Object {$_.topConsumers} |
    Where-Object {$_.semantic.RuleId -eq 'drivebus' -and $_.semantic.AlreadySatisfied})
if ($invalidComplete.Count -gt 0) {
    throw 'DriveBus invalid original fallback incorrectly recognized as complete'
}
Write-Host 'DriveBus pristine four-file A/B/C stage, previous routed source, historical route, subscriber/wake composition, idempotence and fallback drift: PASS'
