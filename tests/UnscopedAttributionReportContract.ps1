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
0,CET_BOUNDARY,CET_FRAME_UPDATE,100000,10,ONE_SECOND
0,CET_BOUNDARY,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,348576,35,ONE_SECOND
1,CET_BOUNDARY,CET_FRAME_UPDATE,300000,30,ONE_SECOND
1,CET_BOUNDARY,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,300000,25,ONE_SECOND
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_UnscopedPathTimeline.csv" -Encoding utf8

@'
CaptureMs,OperationSequence,PositiveBytes,AllocatorPath,VmExecutionState,NativeExecutionBoundary,StackDepth,Frame1,Frame2,Frame3,Frame4,Frame5,Frame6,DroppedSamples
250,1024,120,RESIZE_GROWTH,VM_C,CET_FRAME_UPDATE,2,cyber_engine_tweaks.asi+0xABC,LuaJIT.dll+0x1,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0
1450,2048,180,NEW_GCO,VM_C,OUTSIDE_CET_INSTRUMENTED_BOUNDARY,1,cyber_engine_tweaks.asi+0xABC,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,NO_FRAME,0
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
        !$report.unknownAllocationCoverage.temporalDistributionMeasured){
        throw 'Unscoped multi-axis evidence accounting, provenance or compatibility failed.'
    }
}
$html=Get-Content "$root/CET_Report.html" -Raw
if(!$html.Contains('Unscoped G-GC: VM, allocation type') -or
   !$html.Contains('RESIZE_GROWTH') -or !$html.Contains('CET_FRAME_UPDATE')) {
    throw 'Unscoped diagnostics HTML is missing its distinct measured categories.'
}
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
