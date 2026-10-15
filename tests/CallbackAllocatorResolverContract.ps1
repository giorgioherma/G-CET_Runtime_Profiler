param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if(!(Test-Path $exe)){throw "Missing Resolver $exe"}
$root=Join-Path $env:RUNNER_TEMP 'gcet-allocator-resolver-identity-contract'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$mods=Join-Path $root 'mods'
$capture=Join-Path $root 'capture'
$owner=Join-Path $mods 'Alpha'
New-Item -ItemType Directory -Force $owner,$capture | Out-Null
'registerForEvent("onUpdate", function(dt) return dt end)' |
    Set-Content (Join-Path $owner 'init.lua') -Encoding utf8
$fixture=@{
 schemaVersion='2.0'
 callbacks=@(@{
   registrationId=42;owner='Alpha';infrastructure=$false
   kind='event';target='onUpdate'
   source=@{file='init.lua';lineStart=1;lineEnd=1}
   calls=60;callsPerSecond=60;exclusiveMsPerSecond=0.5
   avgExclusiveUs=8;maxExclusiveMs=0.02;globalWorkSharePct=1
   familyWorkSharePct=100;spikeCount=0;maxSpikeExclusiveMs=0
 })
 optimizerEvidence=@()
 callbackAllocatorPressure=@{
   metric='REGISTRATION_SCOPED_POSITIVE_LUAJIT_ALLOCATOR_PRESSURE'
   pressureAvailable=$true
   additiveToGcTracerRegistrationBytes=$false
   sharedNativeMarshallingAttributedToSubscribers=$false
   automaticRewriteAuthorized=$false
   callbacks=@(@{
      RegistrationId=42;Mod='Alpha';Kind='event';Target='onUpdate'
      TotalPositiveBytes=720;TotalOperations=18
      NativeBoundaryPositiveBytes=120;NativeBoundaryOperations=3
      Reconciled=$true;IdentityVerified=$true
   })
 }
}
function Analyze {
    $fixture | ConvertTo-Json -Depth 40 |
      Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
    $result=& $exe --capture $capture --mods $mods --json | ConvertFrom-Json
    if(!$result.ok){throw "Resolver failed: $($result.error)"}
    Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
}
$report=Analyze
$row=@($report.callbackFamilies | ForEach-Object {$_.topConsumers} |
       Where-Object {$_.owner -eq 'Alpha'})[0]
if(!$report.callbackAllocatorPressure -or
   $report.callbackAllocatorPressure.callbacks[0].Mod -ne 'Alpha' -or
   $row.runtime.callbackAllocator.status -ne 'VERIFIED_CALLBACK_EXECUTION_WINDOW' -or
   $row.runtime.callbackAllocator.ownedPositiveGrowthBytes -ne 720 -or
   $row.runtime.callbackAllocator.insideNativeExecutionBoundaryBytes -ne 120 -or
   $row.runtime.callbackAllocator.sharedPreDispatchMarshallingIncluded -or
   !$row.runtime.callbackAllocator.duplicateOfGcRegistrationTotalsNotAdditive -or
   $row.runtime.callbackAllocator.measuredSavings) {
   throw 'Verified owner-scoped callback allocator evidence was lost or inflated'
}
# A different owner must not receive the same registration-scoped bytes.
$fixture.callbackAllocatorPressure.callbacks[0].Mod='WrongOwner'
$drift=Analyze
$driftRow=@($drift.callbackFamilies | ForEach-Object {$_.topConsumers} |
       Where-Object {$_.owner -eq 'Alpha'})[0]
if($driftRow.runtime.callbackAllocator.status -ne 'NOT_MEASURED_FOR_CALLBACK' -or
   $null -ne $driftRow.runtime.callbackAllocator.ownedPositiveGrowthBytes) {
   throw 'Resolver assigned mismatched callback owner'
}
$fixture.Remove('callbackAllocatorPressure')
$legacy=Analyze
$legacyRow=@($legacy.callbackFamilies | ForEach-Object {$_.topConsumers} |
       Where-Object {$_.owner -eq 'Alpha'})[0]
if($null -ne $legacy.callbackAllocatorPressure -or
   $legacyRow.runtime.callbackAllocator.status -ne 'NOT_CAPTURED') {
   throw 'Legacy Resolver handoff fabricated allocation pressure'
}
Write-Host 'Resolver callback allocator registration identity, drift, legacy-null PASS'
