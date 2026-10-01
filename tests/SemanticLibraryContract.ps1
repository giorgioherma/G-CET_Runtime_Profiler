param(
    [string]$ResolverRoot = ''
)

$ErrorActionPreference = 'Stop'

$libraryPath = Join-Path (Get-Location) 'resolver-knowledge\semantic-library.json'
$injectorPath = Join-Path (Get-Location) 'src\G.CETProfiler.Core\Services\SemanticInjectors.cs'

if (!(Test-Path -LiteralPath $libraryPath -PathType Leaf)) {
    throw "Semantic library not found: $libraryPath"
}
if (!(Test-Path -LiteralPath $injectorPath -PathType Leaf)) {
    throw "Semantic injector source not found: $injectorPath"
}

$library = Get-Content -LiteralPath $libraryPath -Raw | ConvertFrom-Json
$entries = @($library.entries)
if ($entries.Count -eq 0) {
    throw 'Production semantic library is empty.'
}

$ids = @($entries | ForEach-Object { [string]$_.id })
if (@($ids | Sort-Object -Unique).Count -ne $ids.Count) {
    throw 'Production semantic library contains duplicate rule IDs.'
}

foreach ($entry in $entries) {
    if ([string]::IsNullOrWhiteSpace([string]$entry.id)) {
        throw 'Production semantic library contains an entry without an ID.'
    }
    if (!$entry.generation.enabled) {
        throw "Production semantic rule is not generation-enabled: $($entry.id)"
    }
    if ([string]$entry.generation.implementationStatus -ne 'ACTIVE_SOURCE_INJECTOR') {
        throw "Production semantic rule is not an active source injector: $($entry.id)"
    }
    if ($entry.generation.shipReferenceOverride -or $entry.referenceOverrideAllowed) {
        throw "Production semantic rule can ship a reference override: $($entry.id)"
    }
    if ([double]$entry.runtimeAdmission.thresholdMsPerSecond -lt 3.0) {
        throw "Production semantic rule bypasses the 3 ms/s runtime admission floor: $($entry.id)"
    }
    if ([int]$entry.sourceProof.expectedMarkerFileCount -lt 1) {
        throw "Production semantic rule does not define complete-state marker coverage: $($entry.id)"
    }
}

if (!$library.policy.productionEntriesMustGenerate) {
    throw 'Semantic library policy no longer requires production entries to generate.'
}
if ([int]$library.policy.profileOnlyRuleCount -ne 0) {
    throw 'Profile-only/research rules leaked back into the production semantic library.'
}
if ([int]$library.policy.enabledInjectorCount -ne $entries.Count) {
    throw "Semantic library enabledInjectorCount does not match entry count: $($library.policy.enabledInjectorCount) vs $($entries.Count)"
}

$source = Get-Content -LiteralPath $injectorPath -Raw
$matches = [regex]::Matches(
    $source,
    '"(?<id>[^"]+)"\s*=>\s*Apply[A-Za-z0-9_]+\s*\(context\)')
$injectorIds = @($matches | ForEach-Object { $_.Groups['id'].Value })
if (@($injectorIds | Sort-Object -Unique).Count -ne $injectorIds.Count) {
    throw 'Semantic injector switch contains duplicate rule IDs.'
}

$missing = @($ids | Where-Object { $_ -notin $injectorIds })
$extra = @($injectorIds | Where-Object { $_ -notin $ids })
if ($missing.Count -gt 0) {
    throw "Production semantic rule(s) have no injector: $($missing -join ', ')"
}
if ($extra.Count -gt 0) {
    throw "Semantic injector(s) have no production library rule: $($extra -join ', ')"
}

$rejectedAuto = @(
    'dualsense-support',
    'overclockedlynxpaws',
    'immersive-third-person',
    'immersivefirstperson',
    'nativeinteractions',
    'minimap-widgets',
    'sitanywhere',
    'repeatable-increased-criminal-activity'
)
$leaked = @($rejectedAuto | Where-Object { $_ -in $ids -or $_ -in $injectorIds })
if ($leaked.Count -gt 0) {
    throw "Reviewed/rejected rules leaked back into production AUTO: $($leaked -join ', ')"
}

if (![string]::IsNullOrWhiteSpace($ResolverRoot)) {
    $resolvedRoot = (Resolve-Path $ResolverRoot).Path
    $publishedLibrary = Join-Path $resolvedRoot 'knowledge\semantic-library.json'
    if (!(Test-Path -LiteralPath $publishedLibrary -PathType Leaf)) {
        throw "Published standalone Resolver is missing its semantic library: $publishedLibrary"
    }

    $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $libraryPath).Hash
    $publishedHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $publishedLibrary).Hash
    if ($sourceHash -ne $publishedHash) {
        throw 'Published standalone Resolver semantic library does not match the repository production catalog.'
    }

    $retiredRegistry = Join-Path $resolvedRoot 'knowledge\high-impact-exceptions.json'
    if (Test-Path -LiteralPath $retiredRegistry) {
        throw 'Retired high-impact exception registry leaked into the standalone Resolver package.'
    }

    $runtimeRoot = Join-Path $resolvedRoot 'runtime\0-Engine'
    $requiredRuntime = @(
        'fixed-init.lua.gz.b64',
        'modules\ActionRouter.lua.gz.b64',
        'modules\Health.lua.gz.b64',
        'modules\Scheduler.lua.gz.b64'
    )
    foreach ($relative in $requiredRuntime) {
        $runtimeFile = Join-Path $runtimeRoot $relative
        if (!(Test-Path -LiteralPath $runtimeFile -PathType Leaf)) {
            throw "Published standalone Resolver is missing fixed runtime payload: $relative"
        }
    }

    $unexpectedRuntime = @(
        Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse |
        Where-Object {
            $_.Name -in @('manifest.json', 'init.patch') -or
            ($_.Name -like '*.b64' -and $_.Name -notlike '*.gz.b64')
        }
    )
    if ($unexpectedRuntime.Count -gt 0) {
        throw "Dead/development runtime payload leaked into standalone Resolver: $($unexpectedRuntime.FullName -join ', ')"
    }
}

Write-Host "Semantic production contract passed: $($entries.Count) active rules, exact injector parity, no profile-only/rejected rules."
