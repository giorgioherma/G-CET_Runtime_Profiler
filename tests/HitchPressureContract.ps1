param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot
)

$ErrorActionPreference = 'Stop'
$PublishedRoot = (Resolve-Path $PublishedRoot).Path
$profilerExe = Join-Path $PublishedRoot 'app\G-CET-Runtime-Profiler.App.exe'
if (!(Test-Path -LiteralPath $profilerExe -PathType Leaf)) { throw "Profiler app not found: $profilerExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-hitch-pressure-contract'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$capture = Join-Path $root 'capture'
$frameRoot = Join-Path $capture 'FrameTime'
New-Item -ItemType Directory -Force $capture,$frameRoot | Out-Null

$frameMs = New-Object System.Collections.Generic.List[double]
for ($i=0; $i -lt 220; $i++) { $frameMs.Add(16.6667) }
$frameMs[60] = 40.0
$frameMs[65] = 35.0
$frameMs[150] = 60.0
$frameMs[205] = 30.0

$timeSeconds = New-Object System.Collections.Generic.List[double]
$cursor = 0.0
for ($i=0; $i -lt $frameMs.Count; $i++) {
    $timeSeconds.Add($cursor / 1000.0)
    $cursor += $frameMs[$i]
}
$elapsedSeconds = $cursor / 1000.0

@(
'Mod,Calls,CallsPerSecond,ExclusiveTotalMs,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage',
("OwnerA,100,30,20,1.0,0.1,200,20,50,{0},native" -f $elapsedSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)),
("OwnerB,100,30,30,1.0,0.1,300,30,50,{0},native" -f $elapsedSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_ByMod.csv') -Encoding utf8

@(
'RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,SourceLineEnd,Calls,CallsPerSecond,InclusiveTotalMs,ExclusiveTotalMs,InclusiveMsPerSecond,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxInclusiveMs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage',
("1,OwnerA,event,onUpdate,init.lua,1,3,100,30,20,20,1,1,0.1,200,20,20,50,{0},native" -f $elapsedSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)),
("2,OwnerB,event,onUpdate,init.lua,1,3,100,30,30,30,1,1,0.1,300,30,30,50,{0},native" -f $elapsedSeconds.ToString([Globalization.CultureInfo]::InvariantCulture))
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Detail.csv') -Encoding utf8

$starts = @($timeSeconds | ForEach-Object { $_ * 1000.0 })
$spikeA = $starts[60] + 1.0
$spikeB = $starts[150] + 1.0
@(
'Sequence,Frame,CaptureStartMs,CaptureEndMs,InclusiveMs,ExclusiveMs,ChildMs,RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,SourceLineEnd,ThreadId,ThresholdMs,DroppedEventsAtDump,Interpretation',
("1,60,{0},{1},20,20,0,1,OwnerA,event,onUpdate,init.lua,1,3,1,5,0,correlation-only-not-causation" -f $spikeA,$($spikeA+20.0)),
("2,150,{0},{1},30,30,0,2,OwnerB,event,onUpdate,init.lua,1,3,1,5,0,correlation-only-not-causation" -f $spikeB,$($spikeB+30.0))
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Spikes.csv') -Encoding utf8

@(
'Sequence,CaptureMs,UnixEpochMs,Label,DroppedMarkersAtDump',
'1,0,4102444800000,CAPTURE_START,0',
'2,100,4102444800100,GC_HEAP_V1_KIB_400000_BASE,0',
'3,1010,4102444801010,GC_HEAP_V1_KIB_370000_DROP_30000,0',
'4,2000,4102444802000,GC_HEAP_V1_KIB_375000_BASE,0',
("5,{0},4102444800000,PAUSE,0" -f $cursor)
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Markers.csv') -Encoding utf8

@('BucketIndex,BucketStartMs,BucketEndMs,BucketWidthMs,Mod,Calls,InclusiveMs,ExclusiveMs,DroppedTimelineRowsAtDump,Interpretation') |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Timeline.csv') -Encoding utf8

$zeros = @(0..($frameMs.Count-1) | ForEach-Object { 0.0 })
$falses = @(0..($frameMs.Count-1) | ForEach-Object { $false })
$types = @(0..($frameMs.Count-1) | ForEach-Object { 'Application' })
$cpu = @($frameMs | ForEach-Object { [Math]::Max(1.0, $_ - 0.2) })
$gpu = @(0..($frameMs.Count-1) | ForEach-Object { 6.0 })
$cap = @{
  Info = @{
    CreationDate = '2099-01-01T00:00:10Z'
    AppVersion = 'test'
    GameName = 'Cyberpunk 2077'
    GPU = 'test'
    Processor = 'test'
  }
  Runs = @(@{
    CaptureData = @{
      TimeInSeconds = @($timeSeconds)
      MsBetweenPresents = @($frameMs)
      CpuActive = $cpu
      GpuActive = $gpu
      PcLatency = $zeros
      Dropped = $falses
      FrameType = $types
    }
  })
}
$cap | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath (Join-Path $frameRoot 'CapFrameX-test.json') -Encoding utf8
@{ startKeyKnown = $true } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $frameRoot 'CompanionManifest.json') -Encoding utf8

$result = (& $profilerExe --report --capture $capture --json | ConvertFrom-Json)
if (!$result.ok) { throw 'Profiler report generation failed.' }

$summary = Get-Content -LiteralPath (Join-Path $capture 'CET_Summary.json') -Raw | ConvertFrom-Json
$hp = $summary.frameTime.hitchPressure
$heap=$summary.luaHeap
if($null -eq $heap -or $heap.mode -ne 'PASSIVE_HEAP_COUNT_NO_COLLECTION_CONTROL') {
    throw 'Passive Lua heap summary missing from report.'
}
if([int]$heap.recordedCheckpoints -ne 3 -or [int]$heap.observedShrinkEvents -ne 1) {
    throw "Incorrect Lua heap sample/drop counts: $($heap | ConvertTo-Json -Compress)"
}
if([math]::Abs([double]$heap.maxSingleSampleShrinkMiB - (30000.0/1024.0)) -gt 0.005) {
    throw 'Lua heap substantial drop magnitude was not preserved in MiB.'
}
if([int]$heap.shrinkEventsNearRecordedSpikes -ne 1) {
    throw 'Lua heap drop / measured runtime spike coincidence missing.'
}
if($heap.gcPauseAttribution -ne 'NOT_MEASURED') {
    throw 'Passive heap evidence was misreported as measured GC pauses.'
}
$html=Get-Content -LiteralPath (Join-Path $capture 'CET_Report.html') -Raw
if($html -notmatch 'Lua heap &amp; GC indicators' -or
   $html -notmatch 'not proof of GC') {
    throw 'Human report did not explain passive heap telemetry limitations.'
}
$controls = Get-Content -LiteralPath 'payload\CETProfilerControls\init.lua' -Raw
if($controls -notmatch 'pcall\(collectgarbage, "count"\)' -or
   $controls -notmatch 'GC_MAX_MARKERS = 480' -or
   $controls -match 'collectgarbage\("collect"\)' -or
   $controls -match 'collectgarbage\("step"\)') {
    throw 'Runtime controls GC observer was changed into active GC maintenance.'
}

if ($null -eq $hp) { throw 'Hitch pressure block missing.' }
if ([string]$hp.vocabularyVersion -ne '1.0') { throw 'Hitch pressure vocabulary version missing.' }
if ([math]::Abs([double]$hp.tolerance.thresholdFrameMs - 25.0) -gt 0.01) {
    throw "Unexpected adaptive tolerance: $($hp.tolerance.thresholdFrameMs)"
}
if ([int]$hp.frequency.episodes -ne 3) {
    throw "Expected 3 hitch episodes; got $($hp.frequency.episodes)"
}
if ([int]$hp.attribution.runtimeSignalEpisodes -ne 2 -or
    [int]$hp.attribution.noRecordedRuntimeSignalEpisodes -ne 1) {
    throw 'Hitch episode runtime-signal accounting is wrong.'
}
if ([bool]$hp.attribution.authorizesTransform) {
    throw 'Hitch pressure must never authorize a transform.'
}
$a = @($hp.attribution.owners | Where-Object owner -eq 'OwnerA') | Select-Object -First 1
$b = @($hp.attribution.owners | Where-Object owner -eq 'OwnerB') | Select-Object -First 1
if ($null -eq $a -or $null -eq $b -or [int]$a.episodes -ne 1 -or [int]$b.episodes -ne 1) {
    throw 'Per-owner hitch episode overlap is wrong.'
}

$handoff = Get-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ($null -eq $handoff.hitchPressure -or [bool]$handoff.hitchPressure.authorizesTransform) {
    throw 'Resolver handoff lost safe hitch-pressure semantics.'
}

Write-Host 'Hitch-pressure frametime tolerance + episode attribution contract passed.'
