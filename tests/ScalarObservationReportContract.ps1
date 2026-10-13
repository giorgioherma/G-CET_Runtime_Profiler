param([Parameter(Mandatory=$true)][string]$PublishedRoot)
$ErrorActionPreference='Stop'
$root=(Resolve-Path $PublishedRoot).Path
$exe=Join-Path $root 'app/G-CET-Runtime-Profiler.App.exe'
if (!(Test-Path $exe)) {throw "Missing Profiler application: $exe"}
$test=Join-Path $env:RUNNER_TEMP 'gcet-experimental-scalar-report'
if (Test-Path $test) {Remove-Item $test -Recurse -Force}
New-Item -ItemType Directory -Force $test | Out-Null
$csv=Join-Path $test 'CET_Runtime_Profile_Scalar_Observations.csv'
@'
Label,Observations,Unchanged,Changed,DroppedLabels,Interpretation
NPC.Outfit/SelectedId,9,7,1,0,OPT_IN_EXACT_SCALAR_EQUALITY_NOT_CACHE_PROOF
Marker/Count,4,0,3,0,OPT_IN_EXACT_SCALAR_EQUALITY_NOT_CACHE_PROOF
'@ | Set-Content -LiteralPath $csv -Encoding utf8
$run=Start-Process -FilePath $exe -ArgumentList @('--report','--capture',$test,'--json') -Wait -PassThru
if ($run.ExitCode -ne 0) {throw "Scalar fixture report exited $($run.ExitCode)"}
$summary=Get-Content (Join-Path $test 'CET_Summary.json') -Raw | ConvertFrom-Json
$handoff=Get-Content (Join-Path $test 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
$html=Get-Content (Join-Path $test 'CET_Report.html') -Raw
foreach ($obj in @($summary,$handoff)) {
    $p=$obj.scalarChangeObservations
    if ($p.status -ne 'OPT_IN_SCALARS_OBSERVED' -or $p.observedLabels -ne 2 -or
        $p.authorizesAutomaticRewrite -ne $false) {throw 'Scalar probe serialization contract failed'}
    $npc=@($p.observations | Where-Object {$_.label -eq 'NPC.Outfit/SelectedId'})[0]
    if (!$npc -or $npc.unchanged -ne 7 -or $npc.changed -ne 1 -or
        $npc.comparisonPairs -ne 8) {throw 'Scalar comparison accounting incorrect'}
}
if (!$html.Contains('Opt-in scalar value change observations') -or
    !$html.Contains('NPC.Outfit/SelectedId')) {throw 'Scalar HTML table missing'}
Remove-Item $csv -Force
$run=Start-Process -FilePath $exe -ArgumentList @('--report','--capture',$test,'--json') -Wait -PassThru
if ($run.ExitCode -ne 0) {throw "Legacy scalar-absent report exited $($run.ExitCode)"}
$legacy=Get-Content (Join-Path $test 'CET_Summary.json') -Raw | ConvertFrom-Json
if ($legacy.scalarChangeObservations.status -ne 'NOT_CAPTURED') {
    throw 'Missing experimental native CSV must not invent scalar observations'
}
Write-Host 'Optional native scalar probe HTML / Summary / Resolver JSON / legacy contract passed.'
