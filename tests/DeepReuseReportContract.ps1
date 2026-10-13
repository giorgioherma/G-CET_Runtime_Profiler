param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot
)
$ErrorActionPreference = 'Stop'
$profilerExe = Join-Path (Resolve-Path $PublishedRoot).Path 'app\G-CET-Runtime-Profiler.App.exe'
$baseCapture = Join-Path $env:RUNNER_TEMP 'gcet-hitch-pressure-contract\capture'
if (!(Test-Path -LiteralPath $profilerExe)) { throw "Missing report CLI: $profilerExe" }
if (!(Test-Path -LiteralPath $baseCapture)) { throw "Missing base report fixture: $baseCapture" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-deep-reuse-contract'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$case = Join-Path $root 'capture'
New-Item -ItemType Directory -Path $root -Force | Out-Null
Copy-Item -Path $baseCapture -Destination $case -Recurse

@(
'RegistrationId,Mod,ProfileEpoch',
'1,OwnerA,99',
'2,OwnerB,99',
'3,OwnerC,9',
'3,DifferentOwner,9'
) | Set-Content -LiteralPath (Join-Path $case 'CET_Runtime_Profile_Deep_Registrations.csv') -Encoding utf8

@(
'RegistrationId,ProfileEpoch,FunctionKey,FunctionName,SourceFile,SourceLineStart,Calls,GGCTracerSampledOwnAllocatedBytes,GGCTracerSampledOwnAllocations',
'2,1,table-source,makeTable,mods/OwnerB/data.lua,44,5,1000,10',
'2,1,reader,readValue,mods/OwnerB/data.lua,60,3,2000,20',
'1,2,older-source,GetValue,mods/OwnerA/init.lua,24,1,200,2',
'3,1,duplicate,Make,mods/OwnerC/init.lua,2,5,900,9',
'3,1,duplicate,Make,mods/OwnerC/init.lua,2,5,900,9'
) | Set-Content -LiteralPath (Join-Path $case 'CET_Runtime_Profile_Deep_Functions.csv') -Encoding utf8

@(
'RegistrationId,ProfileEpoch,ChildFunctionKey,ParentFunctionKey,CallerSourceFile,CallerLine,Calls,ChildInclusiveMs,Frame,FrameInvocationOrdinal',
'2,1,table-source,parent-a,mods/OwnerB/a.lua,10,3,0.5,17,1',
'2,1,table-source,parent-b,mods/OwnerB/b.lua,30,2,0.5,20,2',
'2,1,reader,parent-b,mods/OwnerB/b.lua,31,3,0.8,21,1',
'1,2,older-source,parent-c,mods/OwnerA/a.lua,9,1,0.1,22,1',
'3,1,duplicate,parent-d,mods/OwnerC/a.lua,4,1,0.3,23,1',
'2,2,table-source,parent-b,mods/OwnerB/a.lua,10,1,0.3,24,1'
) | Set-Content -LiteralPath (Join-Path $case 'CET_Runtime_Profile_Deep_Callsites.csv') -Encoding utf8

$result = (& $profilerExe --report --capture $case --json | ConvertFrom-Json)
if (!$result.ok) { throw 'Native 2.15 deep sample fixture report failed.' }

$s = Get-Content (Join-Path $case 'CET_Summary.json') -Raw | ConvertFrom-Json
$r = Get-Content (Join-Path $case 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
$html = Get-Content (Join-Path $case 'CET_Report.html') -Raw

foreach ($report in @($s,$r)) {
    $d = $report.deepDataReuseCandidates
    if ($null -eq $d -or $d.status -ne 'BOUNDED_SAMPLE_ANALYZED' -or
        $d.authorizesAutomaticRewrite -or
        !$d.ownFunctionAllocationNotDividedAmongCallers -or
        [long]$d.coverage.functionRows -ne 5 -or
        [long]$d.coverage.callsiteRows -ne 6 -or
        [long]$d.coverage.matchedCallsiteRows -ne 4 -or
        [long]$d.coverage.unmatchedCallsiteRows -ne 2 -or
        [long]$d.coverage.ambiguousFunctionKeyRows -ne 1 -or
        [long]$d.coverage.conflictingRegistrationIds -ne 1 -or
        [long]$d.coverage.sampledFunctionOwnPositiveBytesCountedOnce -ne 3200 -or
        [long]$d.coverage.sampledFunctionOwnAllocationOpsCountedOnce -ne 32) {
        throw 'Deep sample join, non-double-counted own allocations or safety flags failed.'
    }
    $table = @($d.topSampledAllocationProducers | Where-Object { $_.functionKey -eq 'table-source' })[0]
    if ($null -eq $table -or $table.mod -ne 'OwnerB' -or
        [long]$table.sampledOwnPositiveBytesOnce -ne 1000 -or
        [long]$table.sampledEdgeCalls -ne 5 -or
        [int]$table.distinctObservedCallerSites -ne 2 -or
        $table.safeToShare -or
        $null -ne $table.estimatedAvoidableBytes -or
        $null -ne $table.callers[0].allocatedBytesAttributedToCaller -or
        $table.underlyingDataChangeFrequency -ne 'NOT_MEASURED') {
        throw 'Caller separation, unknown savings or reuse gating failed.'
    }
}
if (!$html.Contains('Data reuse investigation: sampled producers and callers') -or
    !$html.Contains('mods/OwnerB/data.lua') -or
    !$html.Contains('No tables have been approved for cross-mod sharing')) {
    throw 'Deep reuse HTML missing or claiming unproven sharing.'
}
if ($null -eq $s.gcTracer -or $null -eq $r.gcTracer -or
    $null -eq $s.dataProvenanceCandidates -or $null -eq $r.dataProvenanceCandidates) {
    throw 'Existing GC and Stage A data provenance fields were lost.'
}

$legacy = Join-Path $root 'legacy'
Copy-Item -Path $baseCapture -Destination $legacy -Recurse
$legacyResult = (& $profilerExe --report --capture $legacy --json | ConvertFrom-Json)
if (!$legacyResult.ok) { throw 'Legacy capture generation failed.' }
$legacySummary = Get-Content (Join-Path $legacy 'CET_Summary.json') -Raw | ConvertFrom-Json
if ($legacySummary.deepDataReuseCandidates.status -ne 'NOT_CAPTURED') {
    throw 'Legacy deep sampling data must report NOT_CAPTURED.'
}
Write-Host 'Deep reuse automatic HTML + both JSON reports, join keys, metadata-epoch drift, no double count, duplicate guards, legacy compatibility and fail-closed eligibility passed.'
