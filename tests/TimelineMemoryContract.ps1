param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot
)

$ErrorActionPreference = 'Stop'
$profilerExe = Join-Path (Resolve-Path $PublishedRoot).Path 'app\G-CET-Runtime-Profiler.App.exe'
$fixture = Join-Path $env:RUNNER_TEMP 'gcet-hitch-pressure-contract\capture'
if (!(Test-Path -LiteralPath $profilerExe)) { throw "Missing compiled report CLI: $profilerExe" }
if (!(Test-Path -LiteralPath $fixture)) { throw "Missing HitchPressureContract fixture: $fixture" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-timeline-memory-contract'
if (Test-Path -LiteralPath $root) { Remove-Item $root -Recurse -Force }
New-Item $root -ItemType Directory | Out-Null
$small = Join-Path $root 'small'
Copy-Item $fixture $small -Recurse

$header = 'BucketIndex,BucketStartMs,BucketEndMs,Mod,Calls,ExclusiveMs,MaxExclusiveMs,BucketWidthMs,DroppedTimelineRowsAtDump,Interpretation'
@(
    $header,
    '0,0,50,OwnerA,2,2,1,50,7,continuous-per-mod-correlation',
    '0,0,50,OwnerB,1,1,1,50,7,continuous-per-mod-correlation',
    '0,0,50,OwnerA,3,3,3,50,7,continuous-per-mod-correlation',
    '1,50,100,OwnerA,5,5,5,50,7,continuous-per-mod-correlation',
    '1,50,100,OwnerB,2,4,4,50,7,continuous-per-mod-correlation',
    '2,100,150,OwnerB,1,2,2,50,7,continuous-per-mod-correlation'
) | Set-Content -LiteralPath (Join-Path $small 'CET_Runtime_Profile_Timeline.csv') -Encoding utf8

# Real scenario markers exercise segment overlap with the compact typed rows.
@(
'7,0,4102444800000,SCENARIO_WORLD_START,0',
'8,150,4102444800150,SCENARIO_WORLD_END,0'
) | Add-Content -LiteralPath (Join-Path $small 'CET_Runtime_Profile_Markers.csv') -Encoding utf8

$result = (& $profilerExe --report --capture $small --json | ConvertFrom-Json)
if (!$result.ok) { throw 'Compact timeline fixture report generation failed.' }
$s = Get-Content (Join-Path $small 'CET_Summary.json') -Raw | ConvertFrom-Json
$r = Get-Content (Join-Path $small 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
$html = Get-Content (Join-Path $small 'CET_Report.html') -Raw
$bucket0 = @($s.heavyCetWindows | Where-Object { $_.bucketIndex -eq '0' })[0]
$bucket1 = @($s.heavyCetWindows | Where-Object { $_.bucketIndex -eq '1' })[0]
if ($null -eq $bucket0 -or $null -eq $bucket1 -or
    [math]::Abs([double]$bucket0.exclusiveMs - 6) -gt 1e-6 -or
    [long]$bucket0.calls -ne 6 -or $bucket0.topOwner -ne 'OwnerA' -or
    [math]::Abs([double]$bucket1.exclusiveMs - 9) -gt 1e-6) {
    throw 'Timeline-heavy-window aggregation changed with compact rows.'
}
if (!$r.quality.timelineAvailable -or
    [math]::Abs([double]$r.quality.timelineBucketWidthMs - 50) -gt 1e-6 -or
    [long]$r.quality.droppedTimelineRows -ne 7) {
    throw 'Resolver timeline quality fields changed.'
}
$ownerA = @($r.owners | Where-Object { $_.owner -eq 'OwnerA' })[0]
$ownerB = @($r.owners | Where-Object { $_.owner -eq 'OwnerB' })[0]
if ([long]$ownerA.timeline.activeBuckets -ne 2 -or
    [long]$ownerA.timeline.observedBuckets -ne 3 -or
    [math]::Abs([double]$ownerA.timeline.meanExclusiveMsPerActiveBucket - 5) -gt 1e-6 -or
    [long]$ownerB.timeline.activeBuckets -ne 3) {
    throw 'Resolver timeline owner-bucket evidence changed.'
}
$world = @($r.scenarios | Where-Object { $_.name -eq 'WORLD' })[0]
$worldA = @($world.owners | Where-Object { $_.owner -eq 'OwnerA' })[0]
if ($null -eq $worldA -or
    [math]::Abs([double]$worldA.exclusiveMs - 10) -gt 1e-6 -or
    [math]::Abs([double]$worldA.calls - 10) -gt 1e-6 -or
    !$html.Contains('CET')) {
    throw 'Scenario overlap or HTML report output changed.'
}
Write-Host 'Compact timeline heavy-window, owner, scenario, HTML and Resolver checks passed.'

# A larger capture exercises allocation pressure while remaining affordable in
# the Windows managed CI job. Only the raw timeline is replaced.
$stress = Join-Path $root 'stress'
Copy-Item $fixture $stress -Recurse
$timelinePath = Join-Path $stress 'CET_Runtime_Profile_Timeline.csv'
$writer = [IO.StreamWriter]::new($timelinePath, $false, [Text.UTF8Encoding]::new($false))
try {
    $writer.WriteLine($header)
    for ($bucket=0; $bucket -lt 2500; $bucket++) {
        $start=$bucket*50; $end=$start+50
        for ($owner=0; $owner -lt 80; $owner++) {
            $writer.WriteLine(('{0},{1},{2},SyntheticOwner{3},3,0.125000,0.075000,50,9,continuous-per-mod-correlation' -f $bucket,$start,$end,$owner))
        }
    }
} finally { $writer.Dispose() }

$psi = [Diagnostics.ProcessStartInfo]::new()
$psi.FileName = $profilerExe
$psi.ArgumentList.Add('--report')
$psi.ArgumentList.Add('--capture')
$psi.ArgumentList.Add($stress)
$psi.ArgumentList.Add('--json')
$psi.UseShellExecute = $false
$psi.CreateNoWindow = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$proc = [Diagnostics.Process]::new()
$proc.StartInfo = $psi
try {
    if (!$proc.Start()) { throw 'Failed to launch isolated stress report command.' }
    $outTask = $proc.StandardOutput.ReadToEndAsync()
    $errTask = $proc.StandardError.ReadToEndAsync()
    # PeakWorkingSet64 becomes zero on Windows once the child has exited.
    # Sample the live process, not its terminated handle.
    $peakBytes = [long]0
    while (!$proc.HasExited) {
        try {
            $proc.Refresh()
            $peakBytes = [math]::Max($peakBytes,
                [math]::Max([long]$proc.WorkingSet64, [long]$proc.PeakWorkingSet64))
        } catch [InvalidOperationException] {
            # It may exit between HasExited and the property read.
        }
        Start-Sleep -Milliseconds 60
    }
    $proc.WaitForExit()
    $stdout = $outTask.GetAwaiter().GetResult()
    $stderr = $errTask.GetAwaiter().GetResult()
    if ($proc.ExitCode -ne 0) { throw "Stress report failed: $stderr $stdout" }
    if ($peakBytes -le 0) { throw 'No live-process working-set sample was collected.' }
    $peak = [math]::Round($peakBytes / 1MB, 1)
    Write-Host "Synthetic timeline: 200000 rows; sampled peak working set: $peak MiB"
} finally { $proc.Dispose() }

$stressResolver = Get-Content (Join-Path $stress 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if (!$stressResolver.quality.timelineAvailable -or
    [long]$stressResolver.quality.droppedTimelineRows -ne 9 -or
    [math]::Abs([double]$stressResolver.quality.timelineBucketWidthMs - 50) -gt 1e-6) {
    throw 'Stress timeline lost quality/provenance fields.'
}
Write-Host 'Managed report memory contract passed.'
