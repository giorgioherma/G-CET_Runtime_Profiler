param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) {
    throw "G-CET resolver executable not found: $resolverExe"
}

$root = Join-Path $env:RUNNER_TEMP 'gcet-callback-resolver-contract'
if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}

$capture = Join-Path $root 'capture'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$actionDir = Join-Path $mods 'FixtureAction'
$frameDir = Join-Path $mods 'FixtureFrame'
$unknownDir = Join-Path $mods 'FixtureUnknown'
New-Item -ItemType Directory -Force $actionDir,$frameDir,$unknownDir | Out-Null

@'
Observe("PlayerPuppet", "OnAction", function(self, action, consumer)
    local actionName = Game.NameToString(action:GetName())
    if actionName == "Jump" or actionName == "Dodge" then
        DoActionWork()
    end
end)
'@ | Set-Content -LiteralPath (Join-Path $actionDir 'init.lua') -Encoding utf8

@'
registerForEvent("onUpdate", function(delta)
    DoFrameWork(delta)
end)
'@ | Set-Content -LiteralPath (Join-Path $frameDir 'init.lua') -Encoding utf8

@'
Observe("PlayerPuppet", "SomeOtherMethod", function(self)
    DoUnknownWork()
end)
'@ | Set-Content -LiteralPath (Join-Path $unknownDir 'init.lua') -Encoding utf8

$handoff = @{
    schemaVersion = '1.5'
    callbacks = @(
        @{
            registrationId = 101
            owner = 'FixtureAction'
            infrastructure = $false
            kind = 'observe'
            target = 'PlayerPuppet::OnAction'
            source = @{ file = 'FixtureAction/init.lua'; lineStart = 1; lineEnd = 6 }
            callsPerSecond = 900
            exclusiveMsPerSecond = 12.0
            globalWorkSharePct = 40.0
            familyWorkSharePct = 100.0
            avgExclusiveUs = 13.333
            maxExclusiveMs = 0.4
            spikeCount = 0
            maxSpikeExclusiveMs = 0
        },
        @{
            registrationId = 102
            owner = 'FixtureFrame'
            infrastructure = $false
            kind = 'event'
            target = 'onUpdate'
            source = @{ file = 'FixtureFrame/init.lua'; lineStart = 1; lineEnd = 3 }
            callsPerSecond = 60
            exclusiveMsPerSecond = 10.0
            globalWorkSharePct = 33.333
            familyWorkSharePct = 100.0
            avgExclusiveUs = 166.667
            maxExclusiveMs = 0.5
            spikeCount = 0
            maxSpikeExclusiveMs = 0
        },
        @{
            registrationId = 103
            owner = 'FixtureUnknown'
            infrastructure = $false
            kind = 'observe'
            target = 'PlayerPuppet::SomeOtherMethod'
            source = @{ file = 'FixtureUnknown/init.lua'; lineStart = 1; lineEnd = 3 }
            callsPerSecond = 60
            exclusiveMsPerSecond = 8.0
            globalWorkSharePct = 26.667
            familyWorkSharePct = 100.0
            avgExclusiveUs = 133.333
            maxExclusiveMs = 0.5
            spikeCount = 0
            maxSpikeExclusiveMs = 0
        }
    )
} | ConvertTo-Json -Depth 20

$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --json | ConvertFrom-Json)
if (!$resolved.ok) { throw 'G-CET callback resolver pass failed.' }

$outPath = Join-Path $capture 'G-CET_Resolver.json'
if (!(Test-Path -LiteralPath $outPath -PathType Leaf)) {
    throw "G-CET callback resolver output is missing: $outPath"
}

$result = Get-Content -LiteralPath $outPath -Raw | ConvertFrom-Json
if (!$result.policy.callbackOriented) { throw 'Resolver is not marked callback-oriented.' }
if (!$result.policy.familyFirst) { throw 'Resolver is not marked callback-family-first.' }
if (!$result.policy.genericPatternsBeforeRegistry) { throw 'Registry is not gated behind generic pattern resolution.' }
if ($result.policy.registryContainsPatchCode) { throw 'Registry unexpectedly allows patch code.' }

$onAction = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONACTION' }) | Select-Object -First 1
if ($null -eq $onAction) { throw 'OnAction callback family was not resolved.' }
$action = @($onAction.topConsumers | Where-Object { $_.owner -eq 'FixtureAction' }) | Select-Object -First 1
if ($null -eq $action) { throw 'FixtureAction was not ranked inside OnAction.' }
if (!$action.generic.Automatable) { throw 'Finite exact OnAction filter was not marked automatable.' }
if ($action.generic.Pattern -ne 'ACTION_ROUTING_EXACT_SET') {
    throw "Unexpected OnAction pattern: $($action.generic.Pattern)"
}
if ($action.registry.checkedAfterGenericExhausted) {
    throw 'Registry was consulted even though generic OnAction resolution succeeded.'
}

$onUpdate = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONUPDATE' }) | Select-Object -First 1
if ($null -eq $onUpdate) { throw 'onUpdate callback family was not resolved.' }
$frame = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureFrame' }) | Select-Object -First 1
if ($null -eq $frame) { throw 'FixtureFrame was not ranked inside onUpdate.' }
if (!$frame.generic.Automatable) { throw 'Direct onUpdate registration was not marked automatable.' }
if (@($frame.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Frame dispatch consolidation recipe was not exposed.'
}

$other = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'OTHER' }) | Select-Object -First 1
$unknown = @($other.topConsumers | Where-Object { $_.owner -eq 'FixtureUnknown' }) | Select-Object -First 1
if ($null -eq $unknown) { throw 'FixtureUnknown was not ranked.' }
if ($unknown.generic.Automatable) { throw 'Unsupported callback family was incorrectly marked automatable.' }
if (!$unknown.registry.checkedAfterGenericExhausted) {
    throw 'Unresolved high-impact callback did not reach the registry fallback stage.'
}
if ($unknown.registry.matched) {
    throw 'Empty high-impact exception registry unexpectedly matched a callback.'
}

Write-Host 'Callback-first G-CET resolver contract passed.'
