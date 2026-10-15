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
    if ($zeroFiles.Count -ne 3) {
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
    if ($sourceOnlyFiles.Count -ne 3) {
        throw "DriveBus missing-callback source-only admission wrote $($sourceOnlyFiles.Count) files instead of 3"
    }
    $sourceReport=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
    $sourceCandidates=@($sourceReport.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq 'drivebus'})
    if ($sourceCandidates.Count -ne 1 -or
        @($sourceReport.callbackFamilies).Count -ne 0) {
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

# Pristine source must receive all three author files in ONE pass.
$first=Generate
$f1=@(RuleFiles $first)
if ($f1.Count -ne 3) { throw "Pristine DriveBus wrote $($f1.Count), expected 3" }
$routed=ZipText $first.pass.ZipPath 'DriveBus/Modules/core.lua'
$guarded=ZipText $first.pass.ZipPath 'DriveBus/init.lua'
$timer=ZipText $first.pass.ZipPath 'DriveBus/External/Cron.lua'
foreach ($token in @('SubscribeAction','gcetDriveBusOnAction',
    'Observe("PlayerPuppet", "OnAction", gcetDriveBusOnAction)')) {
    if (!$routed.Contains($token)) { throw "Pristine DriveBus missing: $token" }
}
if (!$guarded.Contains('if Cron.HasActiveTimers() then Cron.Update(delta) end') -or
    !$timer.Contains('function Cron.HasActiveTimers()')) {
    throw 'Pristine DriveBus timer guard incomplete'
}

# Already routed, otherwise pristine: never reroute, only the TWO missing
# Cron/initialization source stages. Existing core file stays byte-identical.
$routed | Set-Content -LiteralPath $corePath -Encoding utf8
$cron | Set-Content -LiteralPath $cronPath -Encoding utf8
$init | Set-Content -LiteralPath $initPath -Encoding utf8
$hashBefore=(Get-FileHash $corePath -Algorithm SHA256).Hash
# A previously routed DriveBus with missing timer stages must also
# remain eligible when the capture has no DriveBus callback records.
$absentCapture | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
try {
    $partialNoCapture=Generate
    $partialNoCaptureFiles=@(RuleFiles $partialNoCapture)
    if ($partialNoCaptureFiles.Count -ne 2 -or
        'DriveBus/Modules/core.lua' -in $partialNoCaptureFiles -or
        'DriveBus/External/Cron.lua' -notin $partialNoCaptureFiles -or
        'DriveBus/init.lua' -notin $partialNoCaptureFiles) {
        throw "Capture-absent DriveBus partial upgrade must preserve existing routed core and emit only timer stages: $($partialNoCaptureFiles -join ',')"
    }
}
finally {
    $measuredInput | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
}
$partial=Generate
$f2=@(RuleFiles $partial)
if ($f2.Count -ne 2 -or
    'DriveBus/External/Cron.lua' -notin $f2 -or 'DriveBus/init.lua' -notin $f2 -or
    'DriveBus/Modules/core.lua' -in $f2) {
    throw "Partially routed DriveBus must update only Cron/init: $($f2 -join ',')"
}
if ((Get-FileHash $corePath -Algorithm SHA256).Hash -ne $hashBefore) {
    throw 'Generating partial pass overwrote current DriveBus routing'
}
(ZipText $partial.pass.ZipPath 'DriveBus/External/Cron.lua') |
    Set-Content -LiteralPath $cronPath -Encoding utf8
(ZipText $partial.pass.ZipPath 'DriveBus/init.lua') |
    Set-Content -LiteralPath $initPath -Encoding utf8

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

# Historical source uses a different callback local and action-table shape;
# it must also preserve the route without rerouting and update only Cron/init.
$legacyCore=$routed.Replace('gcetDriveBusOnAction','zeroEngineOnAction')
$legacyCore=$legacyCore -replace 'local __gcetActions_zeroEngineOnAction = \{ ([^\r\n]*) \}',
    'local zeroEngineConfig = { actions = { $1 } }'
$legacyCore=$legacyCore.Replace('__gcetActions_zeroEngineOnAction','zeroEngineConfig.actions')
$legacyCore=$legacyCore -replace '(?m)^-- G-CET semantic:drivebus\r?\n',''
if (!$legacyCore.Contains('local zeroEngineConfig') -or
    !$legacyCore.Contains('local function zeroEngineOnAction')) {
    throw 'Historical routed DriveBus fixture was not constructed'
}
$legacyCore | Set-Content -LiteralPath $corePath -Encoding utf8
$cron | Set-Content -LiteralPath $cronPath -Encoding utf8
$init | Set-Content -LiteralPath $initPath -Encoding utf8
$legacyHash=(Get-FileHash $corePath -Algorithm SHA256).Hash
$legacy=Generate
$f3=@(RuleFiles $legacy)
if ($f3.Count -ne 2 -or 'DriveBus/Modules/core.lua' -in $f3) {
    throw 'Historical DriveBus route should only receive the missing timer stages'
}
if ((Get-FileHash $corePath -Algorithm SHA256).Hash -ne $legacyHash) {
    throw 'Historical DriveBus action route was mutated in-place'
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
Write-Host 'DriveBus pristine, prior routed native observer, historical variant, partial upgrade, idempotent completion and drift checks: PASS'
