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
    $runtimeThreshold = [double]$entry.runtimeAdmission.thresholdMsPerSecond
    if ($runtimeThreshold -le 0.0 -or $runtimeThreshold -gt 3.0) {
        throw "Production semantic rule has an invalid runtime admission threshold: $($entry.id) = $runtimeThreshold"
    }
    if ($runtimeThreshold -lt 3.0) {
        if ([string]::IsNullOrWhiteSpace([string]$entry.runtimeAdmission.rationale)) {
            throw "Rule-local semantic threshold has no explicit rationale: $($entry.id)"
        }
        if (!$entry.runtimeAdmission.admitted -or
            [double]$entry.runtimeAdmission.measuredPeakExclusiveMsPerSecond -lt $runtimeThreshold) {
            throw "Rule-local semantic threshold lacks measured admission evidence: $($entry.id)"
        }
    }
    if ([int]$entry.sourceProof.expectedMarkerFileCount -lt 1) {
        throw "Production semantic rule does not define complete-state marker coverage: $($entry.id)"
    }
    if ($entry.sourceProof.allowIdentityFallback) {
        $ownerAll = @($entry.sourceProof.ownerAll)
        $ownerAnyGroups = @($entry.sourceProof.ownerAnyGroups)
        $identityPoints = $ownerAll.Count + $ownerAnyGroups.Count
        if ($identityPoints -lt 4) {
            throw "Name-independent semantic identity has fewer than four independent proof points: $($entry.id)"
        }

        $identityAnchors = @($ownerAll)
        foreach ($group in $ownerAnyGroups) {
            $identityAnchors += @($group)
        }
        $hasSourceShapedAnchor = @(
            $identityAnchors | Where-Object {
                [regex]::IsMatch([string]$_, '[.:()=\[\]]')
            }
        ).Count -gt 0
        if (!$hasSourceShapedAnchor) {
            throw "Name-independent semantic identity lacks a source-shaped anchor: $($entry.id)"
        }
    }
    foreach ($selector in @($entry.callbacks)) {
        if ([string]$selector.kind -eq '*' -or [string]$selector.target -eq '*') {
            throw "Production semantic rule contains a wildcard callback selector: $($entry.id)"
        }
    }
}

if ([double]$library.policy.admissionPolicy.runtimeThresholdMsPerSecond -ne 3.0) {
    throw 'Default semantic admission floor is no longer 3 ms/s.'
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

$sourceOnlyUnproven = @(
    'dualsense-support',
    'overclockedlynxpaws'
)
$leaked = @($sourceOnlyUnproven | Where-Object { $_ -in $ids -or $_ -in $injectorIds })
if ($leaked.Count -gt 0) {
    throw "Source-only semantic seed(s) leaked into production AUTO without a validated optimized reference: $($leaked -join ', ')"
}

$retiredUnsafeRules = @(
    'fpv-drone-idle-dormancy'
)
$retiredLeak = @($retiredUnsafeRules | Where-Object { $_ -in $ids -or $_ -in $injectorIds })
if ($retiredLeak.Count -gt 0) {
    throw "Retired crash-reproducing semantic rule leaked back into production AUTO: $($retiredLeak -join ', ')"
}

$restoredReferenceRules = @(
    'immersivefirstperson',
    'nativeinteractions',
    'minimap-widgets',
    'autoloot',
    'better-loot-markers'
)
$missingRestored = @($restoredReferenceRules | Where-Object { $_ -notin $ids -or $_ -notin $injectorIds })
if ($missingRestored.Count -gt 0) {
    throw "Validated optimized-reference semantic rule(s) are missing from production AUTO: $($missingRestored -join ', ')"
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
        'modules\ActionRouter.lua.b64',
        'modules\Health.lua.b64',
        'modules\Scheduler.lua.b64'
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
            ($_.Name -like '*.gz.b64' -and $_.Name -ne 'fixed-init.lua.gz.b64')
        }
    )
    if ($unexpectedRuntime.Count -gt 0) {
        throw "Dead/development runtime payload leaked into standalone Resolver: $($unexpectedRuntime.FullName -join ', ')"
    }
}

Write-Host "Semantic production contract passed: $($entries.Count) active rules, exact injector parity, no profile-only/rejected rules."
