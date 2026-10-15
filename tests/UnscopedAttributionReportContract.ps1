param([Parameter(Mandatory=$true)][string]$PublishedRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $PublishedRoot).Path 'app/G-CET-Runtime-Profiler.App.exe'
if(!(Test-Path $exe)) {throw "Missing G-CET profiler report CLI: $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-unscoped-producer-diagnostics'
if(Test-Path $root) {Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $root | Out-Null

@'
Mod,BytesRank,OperationsRank,AllocatedMiB,AllocationOperations,NewGCObjects,NewStrings
<unattributed>,1,1,1.000000,100,3,0
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_Rankings.csv" -Encoding utf8

@'
VmExecutionState,PositiveBytes,AllocationOperations
VM_C,900000,90
VM_INTERP,148576,10
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedContexts.csv" -Encoding utf8

@'
CaptureSecond,VmExecutionState,PositiveBytes,AllocationOperations,Window
0,VM_C,400000,40,ONE_SECOND
0,VM_INTERP,48576,5,ONE_SECOND
1,VM_C,500000,50,ONE_SECOND
1,VM_INTERP,100000,5,ONE_SECOND
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedTimeline.csv" -Encoding utf8

@'
AllocatorPath,PositiveGrowthTier,PositiveBytes,AllocationOperations
RESIZE_GROWTH,64_255B,600000,60
NEW_GCO,64_255B,448576,40
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedShapes.csv" -Encoding utf8

@'
Dimension,ReallocOrigin,SizeTier,PositiveBytes,AllocationOperations
POSITIVE_DELTA,OLD_SIZE_ZERO,64_255B,400000,40
POSITIVE_DELTA,OLD_SIZE_NONZERO,64_255B,200000,20
ABSOLUTE_NEW_SIZE,OLD_SIZE_ZERO,64_255B,400000,40
ABSOLUTE_NEW_SIZE,OLD_SIZE_NONZERO,256_1023B,200000,20
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Encoding utf8

@'
NativeExecutionBoundary,PositiveBytes,AllocationOperations
CET_FRAME_UPDATE,400000,40
OUTSIDE_CET_INSTRUMENTED_BOUNDARY,648576,60
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedBoundaries.csv" -Encoding utf8

@'
CaptureSecond,Dimension,Category,PositiveBytes,AllocationOperations,Window
0,ALLOCATOR_PATH,RESIZE_GROWTH,200000,20,ONE_SECOND
0,ALLOCATOR_PATH,NEW_GCO,248576,25,ONE_SECOND
1,ALLOCATOR_PATH,RESIZE_GROWTH,400000,40,ONE_SECOND
1,ALLOCATOR_PATH,NEW_GCO,200000,15,ONE_SECOND
0,REALLOC_ORIGIN,OLD_SIZE_ZERO,100000,10,ONE_SECOND
0,REALLOC_ORIGIN,OLD_SIZE_NONZERO,100000,10,ONE_SECOND
1,REALLOC_ORIGIN,OLD_SIZE_ZERO,300000,30,ONE_SECOND
1,REALLOC_ORIGIN,OLD_SIZE_NONZERO,100000,10,ONE_SECOND
0,CET_BOUNDARY,CET_FRAME_UPDATE,100000,10,ONE_SECOND
0,CET_BOUNDARY,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,348576,35,ONE_SECOND
1,CET_BOUNDARY,CET_FRAME_UPDATE,300000,30,ONE_SECOND
1,CET_BOUNDARY,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,300000,25,ONE_SECOND
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedPathTimeline.csv" -Encoding utf8

@'
CaptureMs,OperationSequence,PositiveBytes,AllocatorPath,VmExecutionState,NativeExecutionBoundary,StackDepth,Frame1,Frame2,Frame3,Frame4,Frame5,Frame6,DroppedSamples,Interpretation,ReallocOrigin,OldSize,NewSize
250,1024,120,RESIZE_GROWTH,VM_C,CET_FRAME_UPDATE,2,cyber_engine_tweaks.asi+0xABC,LuaJIT.dll+0x1,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,OLD_SIZE_ZERO,0,120
1450,2048,180,NEW_GCO,VM_C,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,1,cyber_engine_tweaks.asi+0xABC,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,NOT_REALLOC,0,180
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv" -Encoding utf8

function Run-Report {
    $p=Start-Process -FilePath $exe -ArgumentList @('--report','--capture',$root,'--json') -Wait -PassThru
    if($p.ExitCode -ne 0){throw "Report failed code=$($p.ExitCode)"}
    $sum=Get-Content "$root/CET_Summary.json" -Raw | ConvertFrom-Json
    $res=Get-Content "$root/CET_Resolver_Input.json" -Raw | ConvertFrom-Json
    return @($sum,$res)
}
$pair=Run-Report
foreach($report in $pair) {
    $u=$report.unscopedProducerDiagnostics
    if(!$u -or !$u.vmReconciled -or !$u.extendedReconciled -or
        $u.sourceAttribution -ne 'NOT_PROVEN' -or $u.autoRewriteAuthorized -or
        $u.nativeCallpaths.sampledEvents -ne 2 -or
        !$u.hasReallocOrigins -or !$u.reallocOriginsReconciled -or
        $u.reallocOriginDiagnostics.positiveDeltaBytes -ne 600000 -or
        $u.reallocOriginDiagnostics.absoluteNewSizePositiveGrowthBytes -ne 600000 -or
        $u.reallocOriginDiagnostics.perSecondOperations -ne 60 -or
        !$report.unknownAllocationCoverage.temporalDistributionMeasured){
        throw 'Unscoped multi-axis evidence accounting, provenance or compatibility failed.'
    }
}
# Native method samples are the outgoing allocation-pressure metric; legacy
# captures remain method/class UNKNOWN rather than inheriting Lua callback CPU.
foreach ($report in $pair) {
    $p=$report.nativeMarshallingAllocationPressure
    if (!$p -or $p.nativeMethodHashRecorded -or $p.nativeClassHashRecorded -or
        $p.onAction.observedMethodSampleCount -ne 0 -or
        $p.modAllocationAttributionProven -or $p.automaticRewriteAuthorized) {
        throw 'Legacy native samples were falsely class-qualified or assigned to mods'
    }
}
$oldNative=(Get-Content "$root/CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv" -Raw)
@'
CaptureMs,OperationSequence,PositiveBytes,AllocatorPath,VmExecutionState,NativeExecutionBoundary,StackDepth,Frame1,Frame2,Frame3,Frame4,Frame5,Frame6,DroppedSamples,Interpretation,ReallocOrigin,OldSize,NewSize,NativeMethodHash
250,1024,120,RESIZE_GROWTH,VM_C,CET_FUNCTION_OVERRIDE_PURE,2,cyber_engine_tweaks.asi+0xABC,LuaJIT.dll+0x1,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,OLD_SIZE_ZERO,0,120,9658066991423262154
1450,2048,180,NEW_GCO,VM_C,CET_FUNCTION_OVERRIDE_PURE,1,cyber_engine_tweaks.asi+0xABC,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,NOT_REALLOC,0,180,9658066991423262154
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv" -Encoding utf8
$methodOnly=Run-Report
foreach ($report in $methodOnly) {
    $p=$report.nativeMarshallingAllocationPressure
    if (!$p.nativeMethodHashRecorded -or $p.nativeClassHashRecorded -or
        $p.onAction.observedMethodSampleCount -ne 2 -or
        $p.onAction.observedClassQualifiedSampleCount -ne 0 -or
        $p.onAction.observedPlayerPuppetSampleCount -ne 0 -or
        $p.modAllocationAttributionProven -or $p.nativeAllocationSavingsMeasured) {
        throw 'Method-only OnAction evidence was incorrectly class-qualified or treated as savings'
    }
}
@'
CaptureMs,OperationSequence,PositiveBytes,AllocatorPath,VmExecutionState,NativeExecutionBoundary,StackDepth,Frame1,Frame2,Frame3,Frame4,Frame5,Frame6,DroppedSamples,Interpretation,ReallocOrigin,OldSize,NewSize,NativeMethodHash,NativeClassHash
250,1024,120,RESIZE_GROWTH,VM_C,CET_FUNCTION_OVERRIDE_PURE,2,cyber_engine_tweaks.asi+0xABC,LuaJIT.dll+0x1,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,OLD_SIZE_ZERO,0,120,9658066991423262154,14897567717734570886
1450,2048,180,NEW_GCO,VM_C,CET_FUNCTION_OVERRIDE_PURE,1,cyber_engine_tweaks.asi+0xABC,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0,sample,NOT_REALLOC,0,180,9658066991423262154,0
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv" -Encoding utf8
$classQualified=Run-Report
foreach ($report in $classQualified) {
    $p=$report.nativeMarshallingAllocationPressure
    if (!$p.nativeClassHashRecorded -or
        $p.onAction.observedMethodSampleCount -ne 2 -or
        $p.onAction.observedSamplePositiveBytes -ne 300 -or
        $p.onAction.observedClassQualifiedSampleCount -ne 1 -or
        $p.onAction.observedPlayerPuppetSampleCount -ne 1 -or
        $p.onAction.observedPlayerPuppetSamplePositiveBytes -ne 120 -or
        $p.onAction.perSubscriberAllocationBytes -ne $null -or
        $p.modAllocationAttributionProven -or $p.nativeAllocationSavingsMeasured) {
        throw 'Class-qualified native OnAction sample pipeline or no-mod-attribution contract failed'
    }
    $samples=@($p.methodGroups | Where-Object {
        $_.className -eq 'PlayerPuppet' -and $_.methodName -eq 'OnAction' })
    if ($samples.Count -ne 1 -or $samples[0].observedSamplePositiveBytes -ne 120 -or
        $samples[0].subscriberRegistrationId -ne $null) {
        throw 'PlayerPuppet::OnAction numeric hash identity incorrectly grouped'
    }
}
$oldNative | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv" -Encoding utf8

$html=Get-Content "$root/CET_Report.html" -Raw
if(!$html.Contains('Unscoped G-GC: VM, allocation type') -or
   !$html.Contains('RESIZE_GROWTH') -or !$html.Contains('OLD_SIZE_NONZERO') -or !$html.Contains('CET_FRAME_UPDATE') -or
   !$html.Contains('Native-to-Lua marshalling allocation pressure')) {
    throw 'Unscoped diagnostics HTML is missing its distinct measured categories.'
}
# Realloc-origin mismatch must fail closed independently of old VM/path shapes.
$originalOrigins=Get-Content "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Raw
$originalOrigins.Replace('400000,40','400000,39') |
    Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Encoding utf8
$reallocBad=Run-Report
foreach($report in $reallocBad) {
    if($report.unscopedProducerDiagnostics.reallocOriginsReconciled -or
       $report.unscopedProducerDiagnostics.extendedReconciled -or
       $report.unscopedProducerDiagnostics.autoRewriteAuthorized) {
        throw 'Realloc-origin mismatch was considered reconciled or rewrite-safe.'
    }
}
$originalOrigins | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Encoding utf8
# Missing optional new origins must retain legacy capture compatibility.
Remove-Item "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Force
$oldExtension=Run-Report
foreach($report in $oldExtension) {
    if($report.unscopedProducerDiagnostics.hasReallocOrigins -or
       !$report.unscopedProducerDiagnostics.extendedReconciled) {
        throw 'Old native-capture layout was incorrectly rejected.'
    }
}
$originalOrigins | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedReallocOrigins.csv" -Encoding utf8
# Shape mismatch must fail closed and never assign a producer.
(Get-Content "$root/CET_Runtime_Profile_GGC_UnscopedShapes.csv" -Raw).Replace(
    '448576,40','448576,39') | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedShapes.csv" -Encoding utf8
$bad=Run-Report
foreach($report in $bad){
    if($report.unscopedProducerDiagnostics.extendedReconciled -or
       $report.unscopedProducerDiagnostics.autoRewriteAuthorized) {
        throw 'Reconciliation mismatch was treated as verified or rewrite-safe.'
    }
}
# Old captures must remain valid when optional context files do not exist.
Remove-Item "$root/CET_Runtime_Profile_GGC_Unscoped*.csv" -Force
$legacy=Run-Report
foreach($report in $legacy) {
    if($null -ne $report.unscopedProducerDiagnostics -or
        $report.unknownAllocationCoverage.temporalDistributionMeasured) {
        throw 'Legacy file absence was incorrectly treated as source evidence.'
    }
}
Write-Host 'Unscoped provenance: exact reconciliation, sampled-path safety, HTML/JSON, mismatch and legacy absence passed.'
