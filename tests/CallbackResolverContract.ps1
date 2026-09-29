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
        (CallbackRow 105 'FixtureCName' 'observe' 'PlayerPuppet::OnAction' 700 10.0 16.0 'init.lua' 2 7),
        (CallbackRow 106 'FixtureSelector' 'observe' 'PlayerPuppet::OnAction' 650 9.0 14.0 'init.lua' 3 10),
        (CallbackRow 107 'FixtureConsumer' 'observe' 'PlayerPuppet::OnAction' 600 8.0 12.0 'init.lua' 1 7),
        (CallbackRow 108 'FixtureNeighborOverride' 'observe' 'PlayerPuppet::OnAction' 550 7.0 10.0 'init.lua' 1 6),
        (CallbackRow 109 'FixtureDynamic' 'observe' 'PlayerPuppet::OnAction' 500 6.0 8.0 'init.lua' 1 9),
        (CallbackRow 102 'FixtureFrame' 'event' 'onUpdate' 60 5.0 1.0 'init.lua' 1 3),
        (CallbackRow 103 'FixtureUnknown' 'observe' 'PlayerPuppet::SomeOtherMethod' 60 4.0 1.0 'init.lua' 1 3)
    )
} | ConvertTo-Json -Depth 30

$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

# Users naturally point the resolver at RESULTS. It must locate the newest
# collected CET-* capture itself.
$resolved = (& $resolverExe --capture $results --mods $mods --json | ConvertFrom-Json)
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

Write-Host 'Callback-first G-CET resolver contract passed.'
