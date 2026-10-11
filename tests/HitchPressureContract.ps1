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

@(
'Sequence,Frame,CaptureStartMs,CaptureEndMs,DurationMs,Action,BeforeMiB,AfterMiB,ReleasedMiB,SourceFile,SourceLine,DroppedEventsAtDump,Interpretation',
'1,60,1000.5,1005.0,4.5,collect,390,360,30,mods/choom_memory_booster/init.lua,713,0,exact-explicit-call-wall-time-nested-within-callback',
'2,90,1500,1500.3,0.3,step,360,359,1,mods/other/init.lua,99,0,exact-explicit-call-wall-time-nested-within-callback'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GC_Explicit.csv') -Encoding utf8

@(
'Frame,CaptureStartMs,CaptureEndMs,IncrementalCalls,IncrementalMs,CompletedCycles,FullCalls,FullMs,CollectorTotalMs,MaxStepToDateMs,MaxFullToDateMs,DroppedFramesAtDump,Interpretation',
'60,999.0,1010.0,3,3.5,1,0,0,3.5,2.0,0,0,exact-internal-collector-time-frame-bucketed-nested',
'90,1498.0,1502.0,1,1.3,0,0,0,1.3,2.0,0,0,exact-internal-collector-time-frame-bucketed-nested'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GC_Collector.csv') -Encoding utf8

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
'6,2100,4102444802100,WORKLOAD_V1_Q_9_A_4_D_2_P_3_H_5_E_0_F_6_Z_2_S_7,0',
("5,{0},4102444800000,PAUSE,0" -f $cursor)
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Markers.csv') -Encoding utf8

@('BucketIndex,BucketStartMs,BucketEndMs,BucketWidthMs,Mod,Calls,InclusiveMs,ExclusiveMs,DroppedTimelineRowsAtDump,Interpretation') |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Timeline.csv') -Encoding utf8

@(
'Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,CallsPerSecond,TotalMs,MsPerSecond,AvgUs,MaxMs,ElapsedSeconds',
'0-Engine,frame,scheduler_worker,1,frames,10,3,4,1.2,400,1.0,3.5',
'ExampleMod,gcet-work,scan,0,cooperative,10,3,5,1.5,500,1.3,3.5'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Scheduler_ByJob.csv') -Encoding utf8

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


# New native phase/allocation schema with deliberately different GC trigger
# and producer. Collector capture must not assign pause ownership to producer.
@(
'Phase,Operations,WallMs,Interpretation',
'atomic,1,49.75,internal-state-operation-wall-time-nested-in-collector',
'propagate,18,2.3,internal-state-operation-wall-time-nested-in-collector',
'finalize,0,0,internal-state-operation-wall-time-nested-in-collector'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GC_Phases.csv') -Encoding utf8
@(
'CaptureStartMs,CaptureEndMs,DurationMs,ThreadId,RegistrationId,PhaseBefore,PhaseAfter,HeapBeforeMiB,HeapAfterMiB,ThresholdMiB,DebtKiB,Pause,StepMul,DroppedEventsAtDump,Interpretation',
'1000,1049.75,49.75,1,1,atomic,sweepstring,300,275,250,4096,150,200,0,exact-internal-phase-wall-time-trigger-site-not-allocator-owner'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GC_SlowSteps.csv') -Encoding utf8
@(
'RegistrationId,Mod,Kind,Target,SourceFile,Calls,GrossAllocatedMiB,GrossAllocatedMiBPerSecond,Interpretation',
'2,OwnerB,event,onUpdate,init.lua,100,125.5,5.0,capture-gated-positive-LuaJIT-allocation-growth-own-callback',
'1,OwnerA,event,onUpdate,init.lua,100,2.0,0.08,capture-gated-positive-LuaJIT-allocation-growth-own-callback'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Lua_Allocations.csv') -Encoding utf8

@(
'Mod,BytesRank,OperationsRank,SelectedTopTenAtDump,FocusedAtDump,AllocatedMiB,AllocatedMiBPerSecond,AllocationOperations,AllocationsPerSecond,NewGCObjects,NewStrings,SampledObjects,SampleCollisions,SampledReclaimedObjects,SampledReclaimedMiB,AverageReclaimedLifetimeMs,SampledFinalizers,FreedTablesSample,FreedStringsSample,FreedUserdataSample,FreedCdataSample,FreedOtherSample,ActualGlobalFinalizers,Interpretation',
'OwnerB,1,2,1,1,160,64,800,320,500,250,8,0,6,0.4,120,0,4,2,0,0,0,30,capture-only-creation-owner',
'OwnerA,2,1,1,1,80,32,2400,960,2000,400,16,0,12,0.3,16,2,8,4,0,0,0,30,capture-only-creation-owner',
'<unattributed>,3,3,0,0,5,2,50,20,20,10,2,0,1,0.01,4,0,1,0,0,0,0,30,unscoped'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GGC_Rankings.csv') -Encoding utf8
@(
'RegistrationId,ProfileEpoch,FunctionName,SourceFile,SourceLineStart,SourceLineEnd,SampledCalls,SampledExclusiveAllocatedBytes,SampledExclusiveAllocationOperations,Interpretation',
'2,1,refresh,mods/OwnerB/init.lua,40,55,5,8000,320,sampled-not-global',
'1,1,tick,mods/OwnerA/init.lua,20,26,4,2000,200,sampled-not-global'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GGC_Functions.csv') -Encoding utf8
@(
'RegistrationId,Mod,Kind,Target,AllocatedBytes,AllocationOperations,NewGCObjects,NewStrings,SampledObjects,SampleCollisions,SampledReclaimedObjects,SampledReclaimedBytes,SampledLifetimeTicks,SampledFinalizers,FocusedAtDump',
'1,OwnerA,event,onUpdate,83886080,2400,2000,400,16,0,12,314572,20000,2,1',
'2,OwnerB,event,onUpdate,167772160,800,500,250,8,0,6,419430,200000,0,1'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_GGC_Registrations.csv') -Encoding utf8

$result = (& $profilerExe --report --capture $capture --json | ConvertFrom-Json)
if (!$result.ok) { throw 'Profiler report generation failed.' }

$summary = Get-Content -LiteralPath (Join-Path $capture 'CET_Summary.json') -Raw | ConvertFrom-Json
$ggc = $summary.gcTracer
if ($null -eq $ggc -or
    !$ggc.dynamicFiveByBytesAndFiveDistinctByAllocationOperations -or
    $ggc.authorizesAutomaticRewrite -or
    @($ggc.rankings).Count -ne 3 -or
    $ggc.rankings[0].Mod -ne 'OwnerB' -or
    [int]$ggc.rankings[1].OperationsRank -ne 1 -or
    [int]$ggc.focusedFunctions[0].SampledExclusiveAllocationOperations -ne 320) {
    throw 'G-GC Tracer rankings, function sample or safety contract failed.'
}
$ggcHandoff = Get-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ($null -eq $ggcHandoff.gcTracer -or
    !$ggcHandoff.gcTracer.noAutomaticRewrites -or
    @($ggcHandoff.gcTracer.rankings).Count -ne 3) {
    throw 'G-GC Tracer measurement-only resolver contract failed.'
}
if ((Get-Content -LiteralPath (Join-Path $capture 'CET_Report.html') -Raw) -notmatch 'G-GC Tracer') {
    throw 'G-GC Tracer human-readable report section missing.'
}

$workload = $summary.workloadServices
if ($null -eq $workload -or
    $workload.additiveToSchedulerOrCallbackTime -or
    [int]$workload.peakObservedQueue -ne 9 -or
    [int]$workload.peakObservedPhasePending -ne 3 -or
    [int]$workload.latestHealth.FramePaused -ne 2 -or
    [int]$workload.measuredCalls -ne 10 -or
    [math]::Abs([double]$workload.measuredNestedMsPerSecond-1.5) -gt 0.01) {
    throw '0-Engine workload instrumented cost/status regression.'
}

$phaseEvidence = $summary.collectorGc
if (@($phaseEvidence.phaseRows).Count -ne 3 -or
    $phaseEvidence.phaseRows[0].Phase -ne 'atomic' -or
    [math]::Abs([double]$phaseEvidence.phaseRows[0].wallMs - 49.75) -gt 0.01 -or
    @($phaseEvidence.slowSteps).Count -ne 1 -or
    $phaseEvidence.slowSteps[0].PhaseBefore -ne 'atomic' -or
    $phaseEvidence.slowSteps[0].Callback -notmatch 'OwnerA' -or
    [int]$phaseEvidence.slowSteps[0].Pause -ne 150 -or
    [int]$phaseEvidence.slowSteps[0].StepMul -ne 200 -or
    @($phaseEvidence.allocationCandidates).Count -ne 2 -or
    $phaseEvidence.allocationCandidates[0].Callback -notmatch 'OwnerB' -or
    [math]::Abs([double]$phaseEvidence.allocationCandidates[0].allocatedMiB - 125.5) -gt 0.01) {
    throw 'Native phase/slow-step/gross-allocation report attribution contract failed.'
}
$diagnosticHtml = Get-Content -LiteralPath (Join-Path $capture 'CET_Report.html') -Raw
if ($diagnosticHtml -notmatch 'Collector phase execution' -or
    $diagnosticHtml -notmatch 'Slow internal GC operations' -or
    $diagnosticHtml -notmatch 'positive LuaJIT allocations by callback' -or
    $diagnosticHtml -notmatch 'this is not allocation ownership') {
    throw 'GC phase/allocation evidence missing or causality safety explanation lost.'
}

$collector = $summary.collectorGc
if ($null -eq $collector -or
    $collector.measurement -ne 'EXACT_INTERNAL_LUAJIT_GC_EXECUTION_FRAME_BUCKETED' -or
    $collector.automaticVsExplicitOriginSeparatelyIdentified -or
    [int]$collector.incrementalCalls -ne 4 -or
    [int]$collector.completedCycles -ne 1 -or
    [math]::Abs([double]$collector.totalMeasuredMs - 4.8) -gt 0.01 -or
    [math]::Abs([double]$collector.maximumFrameBucketMs - 3.5) -gt 0.01 -or
    [int]$collector.callbackSpikeOverlapFrames -ne 1 -or
    [int]$collector.potentiallyConfoundedSpikeCount -ne 0 -or
    @($collector.spikeCoincidences).Count -ne 1 -or
    [math]::Abs([double]$collector.spikeCoincidences[0].largestGcFrameBucketMs - 3.5) -gt 0.01) {
    throw 'LuaJIT collector instrumentation or correlation contract failed.'
}
$hp = $summary.frameTime.hitchPressure
$resolverInput=Get-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ($resolverInput.garbageCollection.phaseAndAllocatorPolicy -ne 'OBSERVATIONAL_REVIEW_ONLY_NO_AUTOMATIC_REWRITE' -or
    $resolverInput.garbageCollection.phaseTotals[0].Phase -ne 'atomic' -or
    $resolverInput.garbageCollection.slowOperations[0].Callback -notmatch 'OwnerA' -or
    [bool]$resolverInput.garbageCollection.slowOperations[0].authorizesRewrite -or
    $resolverInput.garbageCollection.allocationCandidates[0].Callback -notmatch 'OwnerB' -or
    [bool]$resolverInput.garbageCollection.allocationCandidates[0].authorizesRewrite) {
    throw 'Resolver diagnostic handoff must preserve evidence without granting rewrite authority.'
}
if ($resolverInput.workloadServices.policy -ne 'MEASURE_ONLY_OPT_IN_NO_AUTHOR_REWRITES' -or
    -not $resolverInput.workloadServices.nestedIntoParent) {
    throw 'Resolver misclassified nested 0-Engine workload measurements.'
}
if (-not $resolverInput.garbageCollection.automaticLuaJitTiming -or
    $resolverInput.garbageCollection.automaticVsExplicitOriginSeparated -or
    [math]::Abs([double]$resolverInput.garbageCollection.internalCollectorTotalMs - 4.8) -gt 0.01) {
    throw 'Resolver handoff failed collector cost evidence or origin classification.'
}
if($resolverInput.garbageCollection.collectorSpikeCoincidencePolicy -ne 'REVIEW_ONLY_NO_GC_SUBTRACTION_NO_AUTOMATIC_REWRITE' -or
   @($resolverInput.garbageCollection.spikeCoincidences).Count -ne 1) {
 throw 'Resolver handoff lost collector/callback coincidence review flags.'
}
if($resolverInput.garbageCollection.attributionPolicy -ne 'MEASUREMENT_ONLY_NO_AUTOMATIC_OPTIMIZATION' -or
   [math]::Abs([double]$resolverInput.garbageCollection.totalExplicitMs-4.8) -gt 0.01 -or
   @($resolverInput.garbageCollection.operations).Count -ne 2) {
 throw 'Resolver handoff lost measured GC evidence or confused timing with optimization.'
}
$explicit=$summary.explicitGc
if($null -eq $explicit -or
   $explicit.measurement -ne 'EXACT_EXPLICIT_COLLECTGARBAGE_CALLS_ONLY' -or
   [int]$explicit.count -ne 2 -or [int]$explicit.fullCollections -ne 1 -or
   [int]$explicit.steps -ne 1 -or
   [math]::Abs([double]$explicit.totalDurationMs - 4.8) -gt 0.01 -or
   [int]$explicit.callbacksNearRecordedSpikes -ne 1 -or
   [int]$explicit.alignedHitchEpisodeOverlaps -lt 1 -or
   $explicit.automaticLuaJitGcTimingAvailable) {
 throw 'Explicit GC duration / callback-spike correlation test failed.'
}
if((Get-Content -LiteralPath (Join-Path $capture 'CET_Report.html') -Raw) -notmatch
   'Explicit Lua garbage collection timing') {
 throw 'Explicit GC not included in human report.'
}
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


# Old 2.14 captures have no G-GC Tracer CSVs. They must still produce valid
# reports and resolver handoffs without inventing object provenance.
$legacyGcCapture = Join-Path $root 'legacy-gc-capture'
Copy-Item -LiteralPath $capture -Destination $legacyGcCapture -Recurse -Force
foreach ($file in @('CET_Runtime_Profile_GGC_Rankings.csv',
                   'CET_Runtime_Profile_GGC_Functions.csv',
                   'CET_Runtime_Profile_GGC_Registrations.csv')) {
    Remove-Item -LiteralPath (Join-Path $legacyGcCapture $file) -Force
}
$legacyResult = (& $profilerExe --report --capture $legacyGcCapture --json | ConvertFrom-Json)
if (!$legacyResult.ok) { throw 'Legacy pre-G-GC Tracer capture report failed.' }
$legacySummary = Get-Content (Join-Path $legacyGcCapture 'CET_Summary.json') -Raw | ConvertFrom-Json
$legacyResolver = Get-Content (Join-Path $legacyGcCapture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ($null -ne $legacySummary.gcTracer -or $null -ne $legacyResolver.gcTracer) {
    throw 'Legacy capture invented G-GC Tracer provenance without source CSVs.'
}

# Repeat with a collector-heavy bucket overlapping a long callback.
# This exercises the positive review path independently of the earlier zero
# confirmation, without treating bucket timing as caller-owned GC time.
$gcReviewCapture = Join-Path $root 'gc-review'
Copy-Item -LiteralPath $capture -Destination $gcReviewCapture -Recurse -Force
@(
'Frame,CaptureStartMs,CaptureEndMs,IncrementalCalls,IncrementalMs,CompletedCycles,FullCalls,FullMs,CollectorTotalMs,MaxStepToDateMs,MaxFullToDateMs,DroppedFramesAtDump,Interpretation',
'60,999.0,1039.0,3,18.0,1,0,0,18.0,17.5,0,0,exact-internal-collector-time-frame-bucketed-nested'
) | Set-Content -LiteralPath (Join-Path $gcReviewCapture 'CET_Runtime_Profile_GC_Collector.csv') -Encoding utf8
$reviewResult = (& $profilerExe --report --capture $gcReviewCapture --json | ConvertFrom-Json)
if (!$reviewResult.ok) { throw 'GC review scenario report generation failed.' }
$reviewSummary = Get-Content -LiteralPath (Join-Path $gcReviewCapture 'CET_Summary.json') -Raw | ConvertFrom-Json
$reviewHandoff = Get-Content -LiteralPath (Join-Path $gcReviewCapture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ([int]$reviewSummary.collectorGc.potentiallyConfoundedSpikeCount -ne 1 -or
    -not [bool]$reviewSummary.collectorGc.spikeCoincidences[0].PotentiallyConfounded -or
    [int]$reviewHandoff.garbageCollection.potentiallyConfoundedSpikeCount -ne 1 -or
    [math]::Abs([double]$reviewHandoff.garbageCollection.spikeCoincidences[0].largestGcFrameBucketMs - 18.0) -gt 0.01) {
    throw 'Collector-heavy bucket was not conservatively associated with overlapping callback.'
}
$reviewHtml = Get-Content -LiteralPath (Join-Path $gcReviewCapture 'CET_Report.html') -Raw
if ($reviewHtml -notmatch 'Callback spikes near collector-heavy frame buckets' -or
    $reviewHtml -notmatch 'No GC-free cost can be obtained by subtracting') {
    throw 'GC coincidence safety explanation missing from human report.'
}

Write-Host 'Hitch-pressure frametime tolerance + episode attribution contract passed.'
