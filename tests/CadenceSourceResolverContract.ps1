param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot,

    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'

$PublishedRoot = (Resolve-Path $PublishedRoot).Path
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$exe = Join-Path $PublishedRoot 'G-CET-Runtime-Profiler.exe'
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
$profilerPayload = Join-Path $PublishedRoot 'payload\cyber_engine_tweaks.PROFILER.dll'

if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Profiler executable not found: $exe" }
if (!(Test-Path -LiteralPath $profilerPayload -PathType Leaf)) { throw "Profiler payload not found: $profilerPayload" }
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "G-CET resolver executable not found: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-cadence-source-contract'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }

$plugins = Join-Path $root 'bin\x64\plugins'
$cet = Join-Path $plugins 'cyber_engine_tweaks'
$mods = Join-Path $cet 'mods'
New-Item -ItemType Directory -Force $mods | Out-Null
Copy-Item -LiteralPath $profilerPayload -Destination (Join-Path $plugins 'cyber_engine_tweaks.asi')

function Write-Mod([string]$Name, [string]$Source) {
    $dir = Join-Path $mods $Name
    New-Item -ItemType Directory -Force $dir | Out-Null
    $Source | Set-Content -LiteralPath (Join-Path $dir 'init.lua') -Encoding utf8
}

Write-Mod 'FixtureExact' @'
local timer = 0.0
registerForEvent("onUpdate", function(delta)
    timer = timer + delta
    if timer < 0.5 then return end
    timer = 0.0
    DoExactWork()
end)
'@

Write-Mod 'FixtureMixed' @'
local timer = 0.0
registerForEvent("onUpdate", function(delta)
    SmoothEveryFrame(delta)
    timer = timer + delta
    if timer >= 1.0 then
        timer = 0.0
        DoPeriodicWork()
    end
end)
'@

Write-Mod 'FixtureLowPayback' @'
local lowTimer = 0.0
registerForEvent("onUpdate", function(delta)
    lowTimer = lowTimer + delta
    if lowTimer < 1.0 then return end
    lowTimer = 0.0
    DoLowPaybackWork()
end)
'@

Write-Mod 'FixtureState' @'
local active = false
registerForEvent("onUpdate", function(delta)
    if not active then
        return
    end
    DoActiveWork(delta)
end)
'@

Write-Mod 'FixtureUncertain' @'
registerForEvent("onUpdate", function(delta)
    DoFrameSensitiveWork(delta)
end)
'@

$elapsed = 30
$owners = @(
    @{ Name='FixtureExact'; Ms=0.68 },
    @{ Name='FixtureMixed'; Ms=0.68 },
    @{ Name='FixtureLowPayback'; Ms=0.519 },
    @{ Name='FixtureState'; Ms=0.60 },
    @{ Name='FixtureUncertain'; Ms=0.60 }
)

$byMod = [System.Collections.Generic.List[string]]::new()
$byMod.Add('Mod,Calls,CallsPerSecond,InclusiveTotalMs,ExclusiveTotalMs,InclusiveMsPerSecond,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxInclusiveMs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage')
foreach ($owner in $owners) {
    $calls = 1800
    $totalMs = [double]$owner.Ms * $elapsed
    $avgUs = $totalMs * 1000.0 / $calls
    $byMod.Add(('{0},{1},60,{2:F6},{2:F6},{3:F6},{3:F6},{4:F6},{5:F6},0.5,0.5,25,{6},FULL' -f
        $owner.Name, $calls, $totalMs, [double]$owner.Ms, ([double]$owner.Ms / 10.0), $avgUs, $elapsed))
}
$byMod | Set-Content -LiteralPath (Join-Path $cet 'CET_Runtime_Profile_ByMod.csv') -Encoding utf8

$detail = [System.Collections.Generic.List[string]]::new()
$detail.Add('Mod,Kind,Target,Calls,CallsPerSecond,InclusiveTotalMs,ExclusiveTotalMs,InclusiveMsPerSecond,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxInclusiveMs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage')
foreach ($owner in $owners) {
    $calls = 1800
    $totalMs = [double]$owner.Ms * $elapsed
    $avgUs = $totalMs * 1000.0 / $calls
    $detail.Add(('{0},event,onUpdate,{1},60,{2:F6},{2:F6},{3:F6},{3:F6},{4:F6},{5:F6},0.5,0.5,25,{6},FULL' -f
        $owner.Name, $calls, $totalMs, [double]$owner.Ms, ([double]$owner.Ms / 10.0), $avgUs, $elapsed))
}
$detail | Set-Content -LiteralPath (Join-Path $cet 'CET_Runtime_Profile_Detail.csv') -Encoding utf8

$timeline = [System.Collections.Generic.List[string]]::new()
$timeline.Add('BucketIndex,BucketStartMs,BucketEndMs,Mod,Kind,Target,Calls,ExclusiveMs,MaxExclusiveMs,BucketWidthMs,DroppedTimelineRowsAtDump,Interpretation')
for ($bucket = 0; $bucket -lt 600; $bucket++) {
    $start = $bucket * 50
    $end = $start + 50

    $exactMs = if (($bucket % 10) -eq 0) { 0.25 } else { 0.01 }
    $mixedMs = if (($bucket % 20) -eq 0) { 0.30 } else { 0.02 }
    $lowPaybackMs = if (($bucket % 20) -eq 0) { 0.50 } else { 0.001 }

    if ($start -lt 10000) { $stateMs = 0.01 }
    elseif ($start -lt 20000) { $stateMs = 0.04 }
    else { $stateMs = 0.04 }

    $timeline.Add(('{0},{1},{2},FixtureExact,event,onUpdate,3,{3:F6},{3:F6},50,0,continuous-onupdate-callback-correlation' -f $bucket,$start,$end,$exactMs))
    $timeline.Add(('{0},{1},{2},FixtureMixed,event,onUpdate,3,{3:F6},{3:F6},50,0,continuous-onupdate-callback-correlation' -f $bucket,$start,$end,$mixedMs))
    $timeline.Add(('{0},{1},{2},FixtureLowPayback,event,onUpdate,3,{3:F6},{3:F6},50,0,continuous-onupdate-callback-correlation' -f $bucket,$start,$end,$lowPaybackMs))
    $timeline.Add(('{0},{1},{2},FixtureState,event,onUpdate,3,{3:F6},{3:F6},50,0,continuous-onupdate-callback-correlation' -f $bucket,$start,$end,$stateMs))
    $timeline.Add(('{0},{1},{2},FixtureUncertain,event,onUpdate,3,0.030000,0.030000,50,0,continuous-onupdate-callback-correlation' -f $bucket,$start,$end))
}
$timeline | Set-Content -LiteralPath (Join-Path $cet 'CET_Runtime_Profile_OnUpdateTimeline.csv') -Encoding utf8

$epoch = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
@(
    'Sequence,CaptureMs,UnixEpochMs,Label,DroppedMarkersAtDump'
    "1,0,$epoch,CAPTURE_START,0"
    "2,0,$epoch,SCENARIO_WORLD_START,0"
    "3,10000,$($epoch + 10000),SCENARIO_WORLD_END,0"
    "4,10000,$($epoch + 10000),SCENARIO_DRIVING_START,0"
    "5,20000,$($epoch + 20000),SCENARIO_DRIVING_END,0"
    "6,20000,$($epoch + 20000),SCENARIO_COMBAT_START,0"
    "7,30000,$($epoch + 30000),SCENARIO_COMBAT_END,0"
    "8,30000,$($epoch + 30000),PAUSE,0"
) | Set-Content -LiteralPath (Join-Path $cet 'CET_Runtime_Profile_Markers.csv') -Encoding utf8

$result = (& $exe --collect --game $root --json | ConvertFrom-Json)
if ([string]::IsNullOrWhiteSpace([string]$result.destination)) {
    throw 'Cadence source contract collection did not produce a destination.'
}
if (Test-Path -LiteralPath (Join-Path $result.destination 'CET_Cadence_Final.json')) {
    throw 'Profiler collection incorrectly ran the resolver; measurement and interpretation must remain separate.'
}

# The synthetic CSV fixture has no native registration handles. Real profiler
# captures do. Add deterministic synthetic handles so the pass generator path
# is exercised without weakening its real-capture registration-id requirement.
$handoffPath = Join-Path $result.destination 'CET_Resolver_Input.json'
$handoffJson = Get-Content -LiteralPath $handoffPath -Raw | ConvertFrom-Json
$nextRegistrationId = 700
foreach ($callback in @($handoffJson.callbacks)) {
    $callback | Add-Member -NotePropertyName registrationId -NotePropertyValue $nextRegistrationId -Force
    $nextRegistrationId++
}
$handoffJson | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $handoffPath -Encoding utf8

$resolve = (& $resolverExe --capture $result.destination --mods $mods --json | ConvertFrom-Json)
if (!$resolve.ok) { throw 'Explicit G-CET resolver pass failed.' }

$finalPath = Join-Path $result.destination 'CET_Cadence_Final.json'
if (!(Test-Path -LiteralPath $finalPath -PathType Leaf)) {
    throw "Source-confirmed cadence output is missing: $finalPath"
}

$final = Get-Content -LiteralPath $finalPath -Raw | ConvertFrom-Json
if (!$final.policy.stackAgnostic) { throw 'Source resolver is not marked stack-agnostic.' }
if ($final.policy.modNameRules) { throw 'Source resolver unexpectedly allows mod-name rules.' }
if (!$final.policy.uncertainMeansLeaveAlone) { throw 'Source resolver does not enforce uncertain => LEAVE_ALONE.' }
if ([int]$final.groups.Count -ne 4) { throw "Expected exactly four final groups, got $($final.groups.Count)." }

function Group-For([string]$Owner) {
    $row = @($final.callbacks | Where-Object { $_.Owner -eq $Owner })
    if ($row.Count -ne 1) { throw "Expected exactly one final callback row for $Owner, got $($row.Count)." }
    return [string]$row[0].Group
}

if ((Group-For 'FixtureExact') -ne 'EXACT_CADENCE') {
    throw 'Generic exact timer callback was not classified EXACT_CADENCE.'
}
if ((Group-For 'FixtureMixed') -ne 'MIXED_SPLIT') {
    throw 'Generic mixed frame/timer callback was not classified MIXED_SPLIT.'
}
if ((Group-For 'FixtureLowPayback') -ne 'EXACT_CADENCE') {
    throw 'Low-payback author timer should still be recognized structurally as EXACT_CADENCE.'
}
if ((Group-For 'FixtureState') -ne 'ACTIVE_DORMANT') {
    throw 'Generic state-gated callback was not classified ACTIVE_DORMANT.'
}
if ((Group-For 'FixtureUncertain') -ne 'LEAVE_ALONE') {
    throw 'Uncertain frame callback was not conservatively classified LEAVE_ALONE.'
}

$uncertain = @($final.callbacks | Where-Object { $_.Owner -eq 'FixtureUncertain' })[0]
if ($uncertain.TransformCandidate) {
    throw 'LEAVE_ALONE callback was incorrectly authorized as a transform candidate.'
}

$resolverPath = Join-Path $result.destination 'G-CET_Resolver.json'
if (!(Test-Path -LiteralPath $resolverPath -PathType Leaf)) {
    throw "Callback resolver output is missing: $resolverPath"
}
$callbackResolver = Get-Content -LiteralPath $resolverPath -Raw | ConvertFrom-Json
$onUpdateFamily = @($callbackResolver.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONUPDATE' }) | Select-Object -First 1
if ($null -eq $onUpdateFamily) { throw 'Callback resolver did not emit ONUPDATE family.' }

function Consumer-For([string]$Owner) {
    $row = @($onUpdateFamily.topConsumers | Where-Object { $_.owner -eq $Owner }) | Select-Object -First 1
    if ($null -eq $row) { throw "Callback resolver did not rank $Owner." }
    return $row
}

$exactConsumer = Consumer-For 'FixtureExact'
if (@($exactConsumer.generic.RecipeFamilies) -notcontains 'AUTHOR_CADENCE_WHOLE_CALLBACK') {
    $exactBlockers = [string]::Join(' | ', @($exactConsumer.generic.Blockers))
    $exactEvidence = [string]::Join(' | ', @($exactConsumer.generic.Evidence))
    throw "High-payback exact author cadence was not authorized. Blockers: $exactBlockers Evidence: $exactEvidence"
}
if (!$exactConsumer.generic.Facts.authorCadenceWholeCallback) {
    throw 'Author cadence generator facts were not emitted.'
}
if ([double]$exactConsumer.generic.Facts.estimatedCallbackPaybackPct -lt 10.0) {
    throw 'High-payback fixture did not clear the callback payback floor.'
}

$lowConsumer = Consumer-For 'FixtureLowPayback'
if (@($lowConsumer.generic.RecipeFamilies) -contains 'AUTHOR_CADENCE_WHOLE_CALLBACK') {
    throw 'Low-payback author cadence was incorrectly authorized for rewrite.'
}
$lowBlockers = [string]::Join(' | ', @($lowConsumer.generic.Blockers))
if ($lowBlockers -notmatch 'low-payback') {
    throw "Low-payback fixture was rejected for the wrong reason: $lowBlockers"
}

# Pass generation requires the supported fixed 0-Engine dependency. The
# standalone resolver publish already carries the exact hash-locked fixed init;
# materialize it into this synthetic live mods tree.
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedFixedInit = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
$encodedText = (Get-Content -LiteralPath $encodedFixedInit -Raw).Trim()
$compressedBytes = [Convert]::FromBase64String($encodedText)
$compressedStream = New-Object System.IO.MemoryStream(,$compressedBytes)
$gzipStream = New-Object System.IO.Compression.GZipStream(
    $compressedStream,
    [System.IO.Compression.CompressionMode]::Decompress
)
$outputStream = New-Object System.IO.MemoryStream
try {
    $gzipStream.CopyTo($outputStream)
    [System.IO.File]::WriteAllBytes(
        (Join-Path $zeroDir 'init.lua'),
        $outputStream.ToArray()
    )
}
finally {
    $gzipStream.Dispose()
    $compressedStream.Dispose()
    $outputStream.Dispose()
}

$generated = (& $resolverExe --capture $result.destination --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$generated.ok) { throw 'Author cadence pass generation failed.' }
$zipPath = [string]$generated.pass.ZipPath
if (!(Test-Path -LiteralPath $zipPath -PathType Leaf)) {
    throw "Generated author cadence ZIP is missing: $zipPath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    function Read-ZipText([string]$EntryName) {
        $entry = @($zip.Entries | Where-Object { $_.FullName -eq $EntryName }) | Select-Object -First 1
        if ($null -eq $entry) { throw "ZIP entry missing: $EntryName" }
        $reader = New-Object System.IO.StreamReader($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $exactText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureExact/init.lua'
    foreach ($needle in @(
        'AUTHOR',
        'Schedule.Every(0.5',
        'spread = false',
        'catchUp = false',
        'pause = "never"',
        'ctx.elapsed',
        'registerForEvent("onUpdate", __gcetAuthorCadence_'
    )) {
        if ($exactText -notmatch [regex]::Escape($needle)) {
            throw "Generated exact author cadence is missing: $needle"
        }
    }

    $lowEntry = @($zip.Entries | Where-Object {
        $_.FullName -eq 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureLowPayback/init.lua'
    }) | Select-Object -First 1
    if ($null -ne $lowEntry) {
        $reader = New-Object System.IO.StreamReader($lowEntry.Open())
        try { $lowText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($lowText -match 'Schedule\.Every') {
            throw 'Low-payback fixture was cadence-rewritten despite the payback gate.'
        }
    }
}
finally {
    $zip.Dispose()
}

# This contract collects through the published profiler, whose package root is
# publish\RESULTS. Never leave the synthetic WORLD fixture in the distributable
# package tree.
if (Test-Path -LiteralPath $result.destination -PathType Container) {
    Remove-Item -LiteralPath $result.destination -Recurse -Force
}

Write-Host 'Cadence source resolver generic four-group contract passed.'
