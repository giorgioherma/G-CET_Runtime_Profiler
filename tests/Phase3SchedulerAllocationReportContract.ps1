param([Parameter(Mandatory=$true)][string]$PublishedRoot)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Resolve-Path $PublishedRoot).Path 'app/G-CET-Runtime-Profiler.App.exe'
if (!(Test-Path $exe)) { throw "Profiler app missing $exe" }
$root = Join-Path $env:RUNNER_TEMP 'gcet-phase3-scheduler-allocation'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
New-Item -ItemType Directory -Force $root | Out-Null

$jobs = Join-Path $root 'CET_Runtime_Profile_Scheduler_ByJob.csv'
$framework = Join-Path $root 'CET_Runtime_Profile_Scheduler_FrameworkAllocation.csv'
@'
Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,CallsPerSecond,TotalMs,MsPerSecond,MeasuredOneCorePct,AvgUs,MaxMs,ElapsedSeconds,Interpretation,InclusivePositiveBytes,InclusiveAllocationOperations,ExclusivePositiveBytes,ExclusiveAllocationOperations,ExclusiveOpsPerSecond,ExclusiveOpsPerCall,AllocationMeasurement
0-Engine,frame,outer,1,frame,10,10,2,2,0.2,200,0.3,1,nonadditive,600,60,200,20,20,2,job-envelope
sitAnywhere,gcet-listener,nested,1,frame,10,10,1,1,0.1,100,0.2,1,nonadditive,400,40,400,40,40,4,job-envelope
'@ | Set-Content $jobs -Encoding utf8
@'
Frames,FramePositiveBytes,FrameAllocationOperations,RootJobPositiveBytes,RootJobAllocationOperations,OutsideRootJobsPositiveBytes,OutsideRootJobsAllocationOperations,DroppedOrUnpairedScopes,Interpretation
10,1000,100,600,60,400,40,0,unassigned
'@ | Set-Content $framework -Encoding utf8

function Run-Report {
    $p=Start-Process -FilePath $exe -ArgumentList @('--report','--capture',$root,'--json') -Wait -PassThru
    if ($p.ExitCode -ne 0) {throw "Phase3 report failed $($p.ExitCode)"}
    return @(
        (Get-Content (Join-Path $root 'CET_Summary.json') -Raw | ConvertFrom-Json),
        (Get-Content (Join-Path $root 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json)
    )
}
foreach ($report in (Run-Report)) {
    $a = $report.phase3SchedulerAllocation
    if (!$a -or $a.measuredJobCount -ne 2 -or !$a.framework.Reconciled -or
        $a.framework.FrameOps -ne 100 -or $a.framework.RootOps -ne 60 -or
        $a.framework.ResidualOps -ne 40 -or $a.automaticDormancyDecisionAuthorized) {
        throw 'Phase3 framework accounting or no-auto-decision contract failed'
    }
    $outer=@($a.jobs | Where-Object {$_.Job -eq 'outer'})[0]
    if ($outer.InclusiveOps -ne 60 -or $outer.ExclusiveOps -ne 20 -or
        $outer.exclusiveOpsPerCall -ne 2) {
        throw 'Nested inclusive versus exclusive job accounting lost'
    }
}
if (!(Get-Content (Join-Path $root 'CET_Report.html') -Raw).Contains(
        'Phase 3 Scheduler allocation evidence')) {throw 'Phase3 HTML missing'}

# Inconsistent frame/root accounting must be flagged, never presented as proof.
(Get-Content $framework -Raw).Replace('10,1000,100,600,60,400,40,0,',
    '10,1000,100,600,60,400,39,0,') | Set-Content $framework -Encoding utf8
foreach ($report in (Run-Report)) {
    if ($report.phase3SchedulerAllocation.framework.Reconciled) {
        throw 'Inconsistent Phase3 frame envelope incorrectly reconciled'
    }
}
# A real capture reported scheduler frames and registered jobs, but all
# job invocations were zero. Report coverage as UNVERIFIED, not successful.
@'
Frames,FramePositiveBytes,FrameAllocationOperations,RootJobPositiveBytes,RootJobAllocationOperations,OutsideRootJobsPositiveBytes,OutsideRootJobsAllocationOperations,DroppedOrUnpairedScopes,Interpretation
1928,0,0,0,0,0,0,0,unassigned
'@ | Set-Content $framework -Encoding utf8
@'
Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,CallsPerSecond,TotalMs,MsPerSecond,MeasuredOneCorePct,AvgUs,MaxMs,ElapsedSeconds,Interpretation,InclusivePositiveBytes,InclusiveAllocationOperations,ExclusivePositiveBytes,ExclusiveAllocationOperations,ExclusiveOpsPerSecond,ExclusiveOpsPerCall,AllocationMeasurement
0-Engine,frame,outer,1,frame,0,0,0,0,0,0,0,60,nonadditive,0,0,0,0,0,0,job-envelope
'@ | Set-Content $jobs -Encoding utf8
foreach ($report in (Run-Report)) {
    $a=$report.phase3SchedulerAllocation
    if (!$a -or $a.schedulerExecutionCoverage -ne 'UNVERIFIED_ZERO_JOB_INVOCATIONS_WITH_FRAMES' -or
        $a.jobsWithRecordedExecutions -ne 0 -or $a.allZeroJobMeasurementsAreValidSavings) {
        throw 'Empty scheduler activity was incorrectly promoted to valid evidence'
    }
}

Remove-Item $framework -Force
@'
Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,CallsPerSecond,TotalMs,MsPerSecond,MeasuredOneCorePct,AvgUs,MaxMs,ElapsedSeconds,Interpretation
0-Engine,frame,outer,1,frame,10,10,2,2,0.2,200,0.3,1,legacy
'@ | Set-Content $jobs -Encoding utf8
foreach ($report in (Run-Report)) {
    if ($null -ne $report.phase3SchedulerAllocation) {
        throw 'Legacy scheduler telemetry must remain missing rather than zero'
    }
}
$native=Get-Content (Join-Path $PSScriptRoot '../native-profiler-build/v2.11.0/CETRuntimeProfiler.h') -Raw
foreach($needle in @('SchedulerAllocStateForThread','ExclusivePositiveBytes',
    'CET_Runtime_Profile_Scheduler_FrameworkAllocation.csv','m_schedulerResidualOps')) {
    if (!$native.Contains($needle)) { throw "Missing native Phase3 source anchor $needle" }
}
Write-Host 'Phase3 nested allocation, framework residual, reconciliation, HTML, legacy PASS'
