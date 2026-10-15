param([Parameter(Mandatory=$true)][string]$PublishedRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $PublishedRoot).Path 'app/G-CET-Runtime-Profiler.App.exe'
if(!(Test-Path $exe)){throw "Missing report CLI $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-callback-allocator-pressure-contract'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force $root | Out-Null

@'
RegistrationId,Mod,Kind,Target,AllocatedBytes,AllocationOperations
101,Alpha,onUpdate,onUpdate,300,3
202,Beta,Observe,PlayerPuppet::OnAction,500,5
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_Registrations.csv" -Encoding utf8

@'
RegistrationId,Mod,Kind,Target,ReallocPositiveBytes,ReallocOperations,NewObjectPositiveBytes,NewObjectOperations,NativeBoundaryPositiveBytes,NativeBoundaryOperations,OutsideNativeBoundaryPositiveBytes,OutsideNativeBoundaryOperations,TotalPositiveBytes,TotalOperations,Reconciled,Scope
101,Alpha,onUpdate,onUpdate,200,2,100,1,50,1,250,2,300,3,1,EXACT_ACTIVE_CALLBACK_REGISTRATION_WINDOW
202,Beta,Observe,PlayerPuppet::OnAction,400,4,100,1,0,0,500,5,500,5,1,EXACT_ACTIVE_CALLBACK_REGISTRATION_WINDOW
32767,<registration-overflow>,UNKNOWN,UNKNOWN,0,0,10,1,0,0,10,1,10,1,1,EXACT_ACTIVE_CALLBACK_REGISTRATION_WINDOW
'@ | Set-Content "$root/CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv" -Encoding utf8

function RunReport {
    $p=Start-Process -FilePath $exe -ArgumentList @('--report','--capture',$root,'--json') -Wait -PassThru
    if($p.ExitCode -ne 0){throw "Report failed $($p.ExitCode)"}
    return @(
        (Get-Content "$root/CET_Summary.json" -Raw | ConvertFrom-Json),
        (Get-Content "$root/CET_Resolver_Input.json" -Raw | ConvertFrom-Json)
    )
}
$pair=RunReport
foreach($report in $pair) {
    $pressure=$report.callbackAllocatorPressure
    if(!$pressure -or !$pressure.pressureAvailable -or !$pressure.allRowsReconciled -or
       $pressure.callbackRows -ne 2 -or $pressure.positiveBytes -ne 800 -or
       $pressure.allocationOperations -ne 8 -or
       $pressure.nativeBoundaryPositiveBytes -ne 50 -or
       $pressure.nativeBoundaryAllocationOperations -ne 1 -or
       $pressure.sharedNativeMarshallingAttributedToSubscribers -or
       $pressure.additiveToGcTracerRegistrationBytes -or
       $pressure.nativeAllocationSavingsMeasured -or
       $pressure.automaticRewriteAuthorized) {
        throw 'Exact callback owner attribution or shared-marshalling exclusion failed'
    }
    $top=@($pressure.byMod | Where-Object {$_.owner -eq 'Alpha'})
    if($top.Count -ne 1 -or $top[0].positiveBytes -ne 300 -or
       $top[0].nativeBoundaryPositiveBytes -ne 50) {
        throw 'Owner identity or bridge partition lost'
    }
    $over=@($pressure.unattributableRows | Where-Object {$_.registrationId -eq 32767})
    if($over.Count -ne 1 -or $over[0].totalPositiveBytes -ne 10) {
        throw 'Overflow bucket was misassigned to a mod'
    }
}

# Identity drift must fail closed instead of populating another mod's rank.
$raw=(Get-Content "$root/CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv" -Raw)
$raw.Replace('202,Beta,Observe,','202,WrongOwner,Observe,') |
    Set-Content "$root/CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv" -Encoding utf8
$drift=RunReport
foreach($report in $drift) {
    $pressure=$report.callbackAllocatorPressure
    if($pressure.allRowsReconciled -or $pressure.callbackRows -ne 1 -or
       $pressure.positiveBytes -ne 300 -or
       !(@($pressure.unattributableRows | Where-Object {$_.mod -eq 'WrongOwner'}).Count)) {
        throw 'Mismatched owner was assigned pressure'
    }
}

Remove-Item "$root/CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv"
$legacy=RunReport
foreach($report in $legacy) {
    if($null -ne $report.callbackAllocatorPressure) {
        throw 'Legacy capture without native CSV must remain NOT_MEASURED'
    }
}
Write-Host 'Scoped callback allocation partitions, owner drift, native boundary, overflow, and legacy-null PASS'
