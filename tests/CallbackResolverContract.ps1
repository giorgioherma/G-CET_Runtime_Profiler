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

$results = Join-Path $root 'RESULTS'
$capture = Join-Path $results 'CET-20990101-010203_WORLD'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null

# The generated pass always ships the fixed 0-Engine runtime. Use the exact
# bundled fixed init as the live fixture so the contract exercises the same
# hash gate as a real generated pass.
$encodedInitPath = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
if (!(Test-Path -LiteralPath $encodedInitPath -PathType Leaf)) {
    throw "Bundled fixed 0-Engine init payload is missing: $encodedInitPath"
}

Add-Type -AssemblyName System.IO.Compression
$encodedInit = (Get-Content -LiteralPath $encodedInitPath -Raw).Trim()
$compressedInit = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressedInit)
$gzip = [System.IO.Compression.GZipStream]::new(
    $input,
    [System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes(
        (Join-Path $zeroDir 'init.lua'),
        $output.ToArray())
}
finally {
    $output.Dispose()
    $gzip.Dispose()
    $input.Dispose()
}


function Write-Mod([string]$Name, [string]$Source) {
    $dir = Join-Path $mods $Name
    New-Item -ItemType Directory -Force $dir | Out-Null
    $Source | Set-Content -LiteralPath (Join-Path $dir 'init.lua') -Encoding utf8
}

Write-Mod 'FixtureAction' @'
Observe("PlayerPuppet", "OnAction", function(self, action, consumer)
    local actionName = Game.NameToString(action:GetName())
    if actionName == "Jump" or actionName == "Dodge" then
        DoActionWork()
    end
end)
'@

Write-Mod 'FixtureSingleton' @'
Observe("PlayerPuppet", "OnAction", function(_, action)
    local ListenerAction = GetSingleton("gameinputScriptListenerAction")
    local actionName = Game.NameToString(ListenerAction:GetName(action))
    if actionName == "OpenInventoryMenu" then
        DoMenuWork()
    end
end)
'@

Write-Mod 'FixtureCName' @'
local turnX = CName.new("TurnX")
Observe("PlayerPuppet", "OnAction", function(this, action)
    if action == nil or this == nil then return end
    if action:GetName() ~= turnX then return end
    this:SetSteer(action:GetValue())
end)
'@

Write-Mod 'FixtureSelector' @'
local openAction = "OpenHubMenu"
if gameVer < 1.5 then openAction = "context_help" end
Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
    if action:IsAction(action, openAction) then
        DoOpen()
    elseif action:IsAction(action, "UI_Cancel") then
        consumer.Consume(consumer)
    end
end)
'@

Write-Mod 'FixtureConsumer' @'
Observe("PlayerPuppet", "OnAction", function(_, action, consumer)
    local name = Game.NameToString(action:GetName())
    if name == "MeleeAttack" then
        ListenerActionConsumer.Consume(consumer)
        DoMelee()
    end
end)
'@

Write-Mod 'FixturePattern' @'
Observe("PlayerPuppet", "OnAction", function(this, action, consumer)
    if not state.enabled then return end
    local name = action:GetName() and action:GetName().value or "Unknown"
    local value = action:GetValue()
    if name == "CameraMouseX" then
        consumer:ConsumeSingleAction()
        DoCamera(value)
    end
    if name == "VehicleTurnLeft" or string.find(name, "Turn") then
        DoTurn(value)
    end
end)
'@

Write-Mod 'FixtureNeighborOverride' @'
Observe("PlayerPuppet", "OnAction", function(_, action)
    local actionName = Game.NameToString(action:GetName())
    if actionName == "UI_Apply" then
        DoApply()
    end
end)

Override("ArcadeMachine", "SetupMinigame", function(this)
    DoOtherThing()
end)
'@

Write-Mod 'FixtureDynamic' @'
Observe("PlayerPuppet", "OnAction", function(_, action)
    local actionName = Game.NameToString(action:GetName())
    if actionName == "UI_Apply" then
        DoApply()
    end
    if state.active then
        game:handleInput(action)
    end
end)
'@

Write-Mod 'FixtureFrame' @'
registerForEvent("onUpdate", function(delta)
    DoFrameWork(delta)
end)
'@

Write-Mod 'FixtureUnknown' @'
Observe("PlayerPuppet", "SomeOtherMethod", function(self)
    DoUnknownWork()
end)
'@

function CallbackRow(
    [int]$Id,
    [string]$Owner,
    [string]$Kind,
    [string]$Target,
    [double]$Calls,
    [double]$Ms,
    [double]$Global,
    [string]$File,
    [int]$LineStart,
    [int]$LineEnd
) {
    @{
        registrationId = $Id
        owner = $Owner
        infrastructure = $false
        kind = $Kind
        target = $Target
        source = @{ file = $File; lineStart = $LineStart; lineEnd = $LineEnd }
        callsPerSecond = $Calls
        exclusiveMsPerSecond = $Ms
        globalWorkSharePct = $Global
        familyWorkSharePct = 10.0
        avgExclusiveUs = 10.0
        maxExclusiveMs = 0.5
        spikeCount = 0
        maxSpikeExclusiveMs = 0
    }
}

$handoff = @{
    schemaVersion = '1.5'
    callbacks = @(
        # Deliberately use bare init.lua for most rows. The resolver must scope
        # this otherwise-ambiguous source filename to the measured owner first.
        (CallbackRow 101 'FixtureAction' 'observe' 'PlayerPuppet::OnAction' 900 12.0 20.0 'init.lua' 1 6),
        (CallbackRow 104 'FixtureSingleton' 'observe' 'PlayerPuppet::OnAction' 800 11.0 18.0 'init.lua' 1 7),
        (CallbackRow 105 'FixtureCName' 'observe' 'PlayerPuppet::OnAction' 700 10.0 16.0 'init.lua' 2 6),
        (CallbackRow 106 'FixtureSelector' 'observe' 'PlayerPuppet::OnAction' 650 9.0 14.0 'init.lua' 3 9),
        (CallbackRow 107 'FixtureConsumer' 'observe' 'PlayerPuppet::OnAction' 600 8.0 12.0 'init.lua' 1 7),
        (CallbackRow 110 'FixturePattern' 'observe' 'PlayerPuppet::OnAction' 575 7.5 11.0 'init.lua' 1 12),
        (CallbackRow 108 'FixtureNeighborOverride' 'observe' 'PlayerPuppet::OnAction' 550 7.0 10.0 'init.lua' 1 6),
        (CallbackRow 109 'FixtureDynamic' 'observe' 'PlayerPuppet::OnAction' 500 6.0 8.0 'init.lua' 1 9),
        (CallbackRow 102 'FixtureFrame' 'event' 'onUpdate' 60 5.0 1.0 'init.lua' 1 3),
        (CallbackRow 103 'FixtureUnknown' 'observe' 'PlayerPuppet::SomeOtherMethod' 60 4.0 1.0 'init.lua' 1 3)
    )
} | ConvertTo-Json -Depth 30

$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

# Users naturally point the resolver at RESULTS. It must locate the newest
# collected CET-* capture itself.
$resolved = (& $resolverExe --capture $results --mods $mods --generate-pass --json | ConvertFrom-Json)
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

function Action-For([string]$Owner) {
    $row = @($onAction.topConsumers | Where-Object { $_.owner -eq $Owner }) | Select-Object -First 1
    if ($null -eq $row) { throw "OnAction fixture was not ranked: $Owner" }
    return $row
}

$action = Action-For 'FixtureAction'
if (!$action.generic.Automatable) { throw 'Finite exact OnAction filter was not marked automatable.' }
if ($action.generic.Pattern -ne 'ACTION_ROUTING_EXACT_SET') {
    throw "Unexpected basic OnAction pattern: $($action.generic.Pattern)"
}
if ($action.source.MatchMode -ne 'profiler-owner-relative') {
    throw "Bare init.lua was not resolved owner-relative: $($action.source.MatchMode)"
}
if (@($action.generic.Facts.actions).Count -ne 2) {
    throw 'Concrete action facts were not emitted for the generator handoff.'
}
if ($action.registry.checkedAfterGenericExhausted) {
    throw 'Registry was consulted even though generic OnAction resolution succeeded.'
}

$singleton = Action-For 'FixtureSingleton'
if (!$singleton.generic.Automatable) {
    throw 'Singleton/wrapper GetName action decoding was not resolved generically.'
}
if (@($singleton.generic.Facts.actions) -notcontains 'OpenInventoryMenu') {
    throw 'Singleton action name was not extracted.'
}

$cname = Action-For 'FixtureCName'
if (!$cname.generic.Automatable) {
    throw 'CName alias + inverse early-return action filter was not resolved.'
}
if (@($cname.generic.Facts.actions) -notcontains 'TurnX') {
    throw 'CName alias action was not emitted.'
}

$selector = Action-For 'FixtureSelector'
if (!$selector.generic.Automatable) {
    throw 'Finite variable IsAction selector was not resolved.'
}
foreach ($expected in @('OpenHubMenu','context_help','UI_Cancel')) {
    if (@($selector.generic.Facts.actions) -notcontains $expected) {
        throw "Finite IsAction selector lost expected action: $expected"
    }
}

$consumer = Action-For 'FixtureConsumer'
if (!$consumer.generic.Automatable) {
    throw 'Consumer mutation behind a proven exact action filter was incorrectly rejected.'
}
if (!$consumer.generic.Facts.consumerMutation) {
    throw 'Consumer mutation fact was not preserved for the future generator.'
}

$neighbor = Action-For 'FixtureNeighborOverride'
if (!$neighbor.generic.Automatable) {
    throw 'Neighboring unrelated Override incorrectly poisoned an Observe callback.'
}

$pattern = Action-For 'FixturePattern'
if (!$pattern.generic.Automatable) {
    throw 'Exact + action-name pattern routing with an early state gate was not resolved.'
}
if ($pattern.generic.Pattern -ne 'ACTION_ROUTING_STATE_GATED_EXACT_SET_WITH_PATTERN') {
    throw "Unexpected exact+pattern recipe: $($pattern.generic.Pattern)"
}
if (@($pattern.generic.Facts.actions) -notcontains 'CameraMouseX' -or
    @($pattern.generic.Facts.actions) -notcontains 'VehicleTurnLeft') {
    throw 'Exact actions were lost from the mixed exact+pattern recipe.'
}
if (@($pattern.generic.Facts.actionPatterns) -notcontains 'Turn') {
    throw 'Action-name pattern was not emitted for the generator handoff.'
}

$dynamic = Action-For 'FixtureDynamic'
if ($dynamic.generic.Automatable) {
    throw 'Raw downstream action forwarding was incorrectly reduced to a finite exact set.'
}
if ($dynamic.generic.Pattern -ne 'ACTION_ROUTING_DYNAMIC_DOWNSTREAM') {
    throw "Unexpected dynamic downstream pattern: $($dynamic.generic.Pattern)"
}
if (!$dynamic.generic.Facts.dynamicActionForward) {
    throw 'Dynamic downstream action-forward fact was not emitted.'
}

$onUpdate = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONUPDATE' }) | Select-Object -First 1
if ($null -eq $onUpdate) { throw 'onUpdate callback family was not resolved.' }
$frame = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureFrame' }) | Select-Object -First 1
if ($null -eq $frame) { throw 'FixtureFrame was not ranked inside onUpdate.' }
if (!$frame.generic.Automatable) { throw 'Direct onUpdate registration was not marked automatable.' }
if (@($frame.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Frame dispatch consolidation recipe was not exposed.'
}
if ($frame.source.MatchMode -ne 'profiler-owner-relative') {
    throw 'onUpdate bare init.lua did not use owner-relative source mapping.'
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

if ($null -eq $resolved.pass) {
    throw 'CLI --generate-pass did not return a pass result.'
}
if ([int]$resolved.pass.TransformCount -ne 8) {
    throw "Expected 8 generated V1 transforms, got $($resolved.pass.TransformCount)."
}
if ([int]$resolved.pass.FileCount -ne 12) {
    throw "Expected 12 generated replacement files (8 callback + 4 fixed 0-Engine), got $($resolved.pass.FileCount)."
}
if (!(Test-Path -LiteralPath $resolved.pass.ZipPath -PathType Leaf)) {
    throw "Generated pass ZIP is missing: $($resolved.pass.ZipPath)"
}
if (!(Test-Path -LiteralPath $resolved.pass.ManifestPath -PathType Leaf)) {
    throw "Generated pass manifest is missing beside ZIP: $($resolved.pass.ManifestPath)"
}
if ([System.IO.Path]::GetDirectoryName([string]$resolved.pass.ManifestPath) -ne
    [System.IO.Path]::GetDirectoryName([string]$resolved.pass.ZipPath)) {
    throw 'Generated pass manifest is not beside the ZIP.'
}
if ([System.IO.Path]::GetFileNameWithoutExtension([string]$resolved.pass.ManifestPath) -ne
    [System.IO.Path]::GetFileNameWithoutExtension([string]$resolved.pass.ZipPath)) {
    throw 'Generated pass manifest does not share the ZIP basename.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    $names = @($zip.Entries | ForEach-Object FullName)

    foreach ($requiredEntry in @(
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureAction/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureFrame/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixturePattern/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/ActionRouter.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/Health.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/Scheduler.lua'
    )) {
        if ($requiredEntry -notin $names) {
            throw "Generated pass ZIP is missing expected entry: $requiredEntry"
        }
    }

    if ('G-CET_Pass_Manifest.json' -in $names) {
        throw 'Documentation manifest leaked into the deployable ZIP.'
    }
    if ('bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDynamic/init.lua' -in $names) {
        throw 'Blocked dynamic OnAction callback leaked into the generated pass.'
    }

    function Read-ZipText([string]$EntryName) {
        $entry = $zip.GetEntry($EntryName)
        if ($null -eq $entry) { throw "ZIP entry not found: $EntryName" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $actionText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureAction/init.lua'
    if ($actionText -notmatch 'SubscribeAction') {
        throw 'Generated OnAction replacement does not use the action router.'
    }
    if ($actionText -notmatch 'Jump' -or $actionText -notmatch 'Dodge') {
        throw 'Generated OnAction replacement lost resolver-emitted action facts.'
    }

    $frameText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureFrame/init.lua'
    if ($frameText -notmatch 'MakeEventRegistrar' -or
        $frameText -notmatch '__gcetRegisterEvent_102\("onUpdate"') {
        throw 'Generated frame-dispatch replacement is incomplete.'
    }

    $patternText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixturePattern/init.lua'
    if ($patternText -notmatch 'actions = "\*"' -or
        $patternText -notmatch 'string\.find\(routedName, "Turn", 1, true\)') {
        throw 'Generated mixed exact+pattern router did not preserve the resolver pattern.'
    }

    $manifest = (Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw) | ConvertFrom-Json
    if ($manifest.policy.selection -ne 'ONLY_AUTOMATABLE_CANDIDATES_FROM_G-CET_Resolver.json') {
        throw 'Generated pass manifest is not resolver-only.'
    }
    if ($manifest.policy.modNameRules) {
        throw 'Generated pass manifest unexpectedly allows mod-name rules.'
    }
    if ([int]$manifest.summary.transforms -ne 8) {
        throw 'Generated pass manifest transform count is wrong.'
    }
    if ([int]$manifest.summary.callbackFiles -ne 8) {
        throw "Expected 8 callback replacement files, got $($manifest.summary.callbackFiles)."
    }
    if ([int]$manifest.summary.fixedRuntimeFiles -ne 4) {
        throw "Expected 4 fixed 0-Engine runtime files, got $($manifest.summary.fixedRuntimeFiles)."
    }
    if (!$manifest.fixedRuntime.included -or !$manifest.fixedRuntime.exception) {
        throw 'Generated pass did not mark 0-Engine as the fixed runtime exception.'
    }
    if ($manifest.fixedRuntime.FixedVersion -ne '0.18.11-PASS4.2.1-PHASE-CADENCE-FIX') {
        throw "Unexpected fixed 0-Engine version: $($manifest.fixedRuntime.FixedVersion)"
    }

    $zeroText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua'
    foreach ($requiredRuntimeSymbol in @(
        'function Engine.MakeEventRegistrar',
        'function Engine.SubscribeAction',
        'ActionRouter.Dispatch'
    )) {
        if ($zeroText -notmatch [regex]::Escape($requiredRuntimeSymbol)) {
            throw "Generated fixed 0-Engine init is missing runtime symbol: $requiredRuntimeSymbol"
        }
    }
}
finally {
    $zip.Dispose()
}

Write-Host 'Callback-first resolver + V1 pass generator contract passed.'
