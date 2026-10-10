param(
    [Parameter(Mandatory = $true)]
    [string]$PublishedRoot,
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$PublishedRoot = (Resolve-Path $PublishedRoot).Path
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$profilerExe = Join-Path $PublishedRoot 'app\G-CET-Runtime-Profiler.App.exe'
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $profilerExe -PathType Leaf)) { throw "Profiler app not found: $profilerExe" }
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "Resolver not found: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-burst-materiality-contract'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$capture = Join-Path $root 'capture'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

function Write-Mod([string]$Name,[string]$Target) {
    $dir = Join-Path $mods $Name
    New-Item -ItemType Directory -Force $dir | Out-Null
    @"
Observe("PlayerPuppet", "$Target", function(self)
    DoWork(self)
end)
"@ | Set-Content -LiteralPath (Join-Path $dir 'init.lua') -Encoding utf8
}

Write-Mod 'FixturePeriodic' 'FixturePeriodicTick'
Write-Mod 'FixtureCatastrophic' 'FixtureCatastrophicTick'
Write-Mod 'FixtureSustained' 'FixtureSustainedTick'
Write-Mod 'FixtureNoise' 'FixtureNoiseTick'

@(
'Mod,Calls,CallsPerSecond,ExclusiveTotalMs,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage',
'FixturePeriodic,600,60,10,1.0,0.1,16.667,6.4,14.2857,10,native',
'FixtureCatastrophic,100,10,5,0.5,0.05,50,45.0,7.1429,10,native',
'FixtureSustained,600,60,40,4.0,0.4,66.667,0.3,57.1429,10,native',
'FixtureNoise,100,10,5,0.5,0.05,50,5.5,7.1429,10,native'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_ByMod.csv') -Encoding utf8

@(
'RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,SourceLineEnd,Calls,CallsPerSecond,InclusiveTotalMs,ExclusiveTotalMs,InclusiveMsPerSecond,ExclusiveMsPerSecond,MeasuredOneCorePct,AvgExclusiveUs,MaxInclusiveMs,MaxExclusiveMs,MeasuredExclusiveSharePct,ElapsedSeconds,Coverage',
'1,FixturePeriodic,observe,PlayerPuppet::FixturePeriodicTick,init.lua,1,3,600,60,10,10,1,1,0.1,16.667,6.4,6.4,14.2857,10,native',
'2,FixtureCatastrophic,observe,PlayerPuppet::FixtureCatastrophicTick,init.lua,1,3,100,10,5,5,0.5,0.5,0.05,50,45,45,7.1429,10,native',
'3,FixtureSustained,observe,PlayerPuppet::FixtureSustainedTick,init.lua,1,3,600,60,40,40,4,4,0.4,66.667,0.3,0.3,57.1429,10,native',
'4,FixtureNoise,observe,PlayerPuppet::FixtureNoiseTick,init.lua,1,3,100,10,5,5,0.5,0.5,0.05,50,5.5,5.5,7.1429,10,native'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Detail.csv') -Encoding utf8

@(
'Sequence,Frame,CaptureStartMs,CaptureEndMs,InclusiveMs,ExclusiveMs,ChildMs,RegistrationId,Mod,Kind,Target,SourceFile,SourceLineStart,SourceLineEnd,ThreadId,ThresholdMs,DroppedEventsAtDump,Interpretation',
'1,60,1000,1006,6,6,0,1,FixturePeriodic,observe,PlayerPuppet::FixturePeriodicTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'2,180,3000,3006.2,6.2,6.2,0,1,FixturePeriodic,observe,PlayerPuppet::FixturePeriodicTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'3,300,5000,5006.4,6.4,6.4,0,1,FixturePeriodic,observe,PlayerPuppet::FixturePeriodicTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'4,420,7000,7006.1,6.1,6.1,0,1,FixturePeriodic,observe,PlayerPuppet::FixturePeriodicTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'5,240,4000,4045,45,45,0,2,FixtureCatastrophic,observe,PlayerPuppet::FixtureCatastrophicTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'6,120,2000,2005.5,5.5,5.5,0,4,FixtureNoise,observe,PlayerPuppet::FixtureNoiseTick,init.lua,1,3,1,5,0,correlation-only-not-causation',
'7,510,8500,8505.4,5.4,5.4,0,4,FixtureNoise,observe,PlayerPuppet::FixtureNoiseTick,init.lua,1,3,1,5,0,correlation-only-not-causation'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Spikes.csv') -Encoding utf8

@(
'Sequence,CaptureMs,UnixEpochMs,Label,DroppedMarkersAtDump',
'1,0,4102444800000,CAPTURE_START,0',
'2,10000,4102444810000,PAUSE,0'
) | Set-Content -LiteralPath (Join-Path $capture 'CET_Runtime_Profile_Markers.csv') -Encoding utf8

$reportResult = (& $profilerExe --report --capture $capture --json | ConvertFrom-Json)
if (!$reportResult.ok) { throw 'Profiler report generation failed.' }

$summary = Get-Content -LiteralPath (Join-Path $capture 'CET_Summary.json') -Raw | ConvertFrom-Json
if ([string]$summary.interop.contractVersion -ne '1.1') { throw 'Summary did not move to burst-aware interop 1.1.' }
if ([string]$summary.burstAnalysis.vocabularyVersion -ne '1.0') { throw 'Summary burst vocabulary missing.' }

$periodicSummary = @($summary.burstAnalysis.candidates | Where-Object owner -eq 'FixturePeriodic') | Select-Object -First 1
if ($null -eq $periodicSummary -or $periodicSummary.primaryClass -ne 'PERIODIC_STUTTER') {
    throw 'Periodic 2-second 6 ms callback was not classified PERIODIC_STUTTER.'
}
if ([math]::Abs([double]$periodicSummary.medianIntervalMs - 2000.0) -gt 0.001) {
    throw "Periodic median interval is wrong: $($periodicSummary.medianIntervalMs)"
}
if (![bool]$periodicSummary.stutterMaterial) { throw 'Periodic callback was not marked stutter-material.' }

$catSummary = @($summary.burstAnalysis.candidates | Where-Object owner -eq 'FixtureCatastrophic') | Select-Object -First 1
if ($null -eq $catSummary -or @($catSummary.classes) -notcontains 'CATASTROPHIC_BURST') {
    throw '45 ms callback was not classified CATASTROPHIC_BURST.'
}

if (@($summary.burstAnalysis.candidates | Where-Object owner -eq 'FixtureNoise').Count -ne 0) {
    throw 'Two irregular ~5 ms samples were incorrectly promoted to stutter-material.'
}

$handoff = Get-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Raw | ConvertFrom-Json
if ([string]$handoff.schemaVersion -ne '2.0' -or [string]$handoff.interop.contractVersion -ne '1.1') {
    throw 'Resolver handoff did not move to burst-aware schema/interop.'
}
$periodicHandoff = @($handoff.callbacks | Where-Object owner -eq 'FixturePeriodic') | Select-Object -First 1
if (!$periodicHandoff.burst.periodicStutter -or !$periodicHandoff.burst.stutterMaterial) {
    throw 'Resolver handoff lost periodic burst evidence.'
}

$resolved = (& $resolverExe --capture $capture --mods $mods --json | ConvertFrom-Json)
if (!$resolved.ok) { throw 'Resolver rejected burst-aware profiler handoff.' }
$result = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json

if (!$result.policy.burstAwareMateriality) { throw 'Resolver did not advertise burst-aware materiality.' }
if ([int]$result.summary.materialRemaining -ne 3) {
    throw "Expected periodic + catastrophic + sustained unresolved callbacks to be material; got $($result.summary.materialRemaining)."
}
if ([int]$result.summary.sustainedMaterialRemaining -ne 1) {
    throw "Expected one sustained-material unresolved callback; got $($result.summary.sustainedMaterialRemaining)."
}
if ([int]$result.summary.burstMaterialRemaining -ne 2) {
    throw "Expected two burst-material unresolved callbacks; got $($result.summary.burstMaterialRemaining)."
}
if ([int]$result.summary.belowThreshold -ne 1) {
    throw "Expected only irregular noise below materiality; got $($result.summary.belowThreshold)."
}

$other = @($result.callbackFamilies | Where-Object resolverFamily -eq 'OTHER')
$periodicResolved = @($other.topConsumers | Where-Object owner -eq 'FixturePeriodic') | Select-Object -First 1
if ($null -eq $periodicResolved -or
    !$periodicResolved.runtime.materiality.burst -or
    !$periodicResolved.runtime.materiality.material -or
    $periodicResolved.runtime.materiality.burstClassificationAuthorizesAuto) {
    throw 'Resolver did not preserve burst materiality while keeping AUTO authorization false.'
}

Write-Host 'Burst-aware profiler + resolver materiality contract passed.'
