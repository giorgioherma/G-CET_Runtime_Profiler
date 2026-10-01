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
    game:handleInput(action)
end)
'@

Write-Mod 'FixtureGatedDynamic' @'
Observe("PlayerPuppet", "OnAction", function(_, action)
    local actionName = Game.NameToString(action:GetName(action))
    local actionType = action:GetType(action).value
    if actionName == "UI_Apply" then
        if actionType == "BUTTON_PRESSED" then
            DoApply()
        end
    end
    if state.session then
        if state.session.active then
            game:handleInput(action)
        end
    end
end)
'@

Write-Mod 'FixtureOverrideStructural' @'
Override("PlayerPuppet", "OnAction", function(self, action, consumer, wrappedMethod)
    local playerA = Game.GetPlayer()
    local playerB = Game.GetPlayer()
    local marker = CName.new("FixtureOverrideStructural")
    game:handleInput(action)
    return wrappedMethod(self, action, consumer)
end)
'@

Write-Mod 'FixtureOverridePrefilter' @'
local function needModule(path)
    return require(path)
end

local Input = needModule("runtime/input")
local Runtime = { ready = true, session = {}, input = {} }
local HANDLED_ACTIONS = Input.HANDLED_ACTIONS
FixtureOverrideRuntime = Runtime

function Runtime:onAction(action, consumer, wrappedMethod)
    if self.ready and self.session and self.input then
        local named, name = pcall(function() return Game.NameToString(action:GetName()) end)
        if named and self.traceActions then self:traceAction(name) end
        if named and HANDLED_ACTIONS[name] then
            self.input:onAction(name, action:GetType().value, action:GetValue())
        end
    end
    return wrappedMethod(action, consumer)
end

Override("PlayerPuppet", "OnAction", function(_, action, consumer, wrappedMethod)
    local current = FixtureOverrideRuntime
    if current then return current:onAction(action, consumer, wrappedMethod) end
    return wrappedMethod(action, consumer)
end)
'@
$overridePrefilterDir = Join-Path $mods 'FixtureOverridePrefilter'
$overridePrefilterRuntime = Join-Path $overridePrefilterDir 'runtime'
New-Item -ItemType Directory -Force -Path $overridePrefilterRuntime | Out-Null
@'
local Input = {}

Input.HANDLED_ACTIONS = {
    ChoiceScrollUp = true,
    ChoiceScrollDown = true,
    ChoiceApply = true,
}

return Input
'@ | Set-Content -LiteralPath (Join-Path $overridePrefilterRuntime 'input.lua') -Encoding utf8

Write-Mod 'FixtureDownstream' @'
Observe("PlayerPuppet", "OnAction", function(_, action)
    local actionName = Game.NameToString(action:GetName())
    local actionType = action:GetType(action).value
    if state.camera then
        controller:HandleInput(actionName, actionType, action)
    end
    if actionName == "Jump" then
        DoJump()
    end
end)
'@

$downstreamDir = Join-Path $mods 'FixtureDownstream'
@'
function Controller:HandleInput(actionName, actionType, action)
    local relevantInputs = {
        UI_MoveUp = true,
        UI_MoveDown = true,
    }

    if not self.lock then
        if actionName == "MoveX" or actionName == "PhotoMode_CameraMovementX" then
            self.x = action:GetValue(action)
        elseif actionName == "MoveY" then
            self.y = action:GetValue(action)
        end
    end

    if relevantInputs[actionName] then
        self.lastRelevant = true
    end
    self.isMoving = self.x ~= 0 or self.y ~= 0
end
'@ | Set-Content -LiteralPath (Join-Path $downstreamDir 'controller.lua') -Encoding utf8

Write-Mod 'FixtureStructural' @'
registerForEvent("onUpdate", function(delta)
    local playerA = Game.GetPlayer()
    local playerB = Game.GetPlayer()
    local nameA = CName.new("StructuralFixture")
    local nameB = CName.new("StructuralFixture")
    DoStructuralWork(playerA, playerB, nameA, nameB, delta)
end)
'@

Write-Mod 'FixtureDiscoveryAuthorRate' @'
local active = false
local discoveryTimer = 0
registerHotkey("fixture_discovery_author", "Fixture discovery author", function()
    active = true
end)
registerForEvent("onUpdate", function(delta)
    if not active then
        discoveryTimer = discoveryTimer + delta
        if discoveryTimer >= 1.0 then
            discoveryTimer = 0
            local player = Game.GetPlayer()
            local distance = Vector4.Distance(player:GetWorldPosition(), targetPosition)
            CheckNearbyActivity(distance)
        end
    end
    if active then
        RunActiveActivity(delta)
    end
end)
'@

Write-Mod 'FixtureDormantOnUpdate' @'
local active = false
registerHotkey("fixture_update_dormant", "Fixture update dormant", function()
    active = not active
end)
registerForEvent("onUpdate", function(delta)
    local player = Game.GetPlayer()
    if not active then return end
    DoActiveUpdate(player, delta)
end)
'@

Write-Mod 'FixtureDormantHard' @'
local active = false
registerHotkey("fixture_dormant", "Fixture dormant", function()
    active = not active
end)
Observe("PlayerPuppet", "FixtureTick", function(self)
    if not active then return end
    DoDormantWork(self)
end)
'@

Write-Mod 'FixtureDormantDiscovery' @'
local active = false
registerInput("fixture_discovery", "Fixture discovery", function(pressed)
    if pressed then active = true end
end)
Observe("PlayerPuppet", "FixtureDiscoveryTick", function(self)
    if not active then return end
    local player = Game.GetPlayer()
    local distance = Vector4.Distance(player:GetWorldPosition(), targetPosition)
    DoDiscoveryWork(distance)
end)
'@

Write-Mod 'FixtureDormantNever' @'
Observe("PlayerPuppet", "FixtureCameraTick", function(self)
    local camera = Game.GetCameraSystem():GetActiveCameraData()
    DoCameraWork(camera)
end)
'@

Write-Mod 'FixtureNeverGateUpdate' @'
local active = false
registerHotkey("fixture_never_gate", "Fixture never gate", function()
    active = not active
end)
registerForEvent("onUpdate", function(delta)
    local camera = Game.GetCameraSystem():GetActiveCameraData()
    if not active then return end
    DoCameraUpdate(camera, delta)
end)
'@

Write-Mod 'FixtureDormantBackground' @'
Observe("PlayerPuppet", "FixtureBackgroundTick", function(self)
    processTaskQueue()
end)
'@

Write-Mod 'FixtureOtherStructural' @'
ObserveAfter("PlayerPuppet", "FixtureStructuralObserve", function(self)
    local playerA = Game.GetPlayer()
    local playerB = Game.GetPlayer()
    local movingA = self:IsMovingHorizontally()
    local movingB = self:IsMovingHorizontally()
    local singletonA = GetSingleton("gameTargetingSystem")
    local singletonB = GetSingleton("gameTargetingSystem")
    local staticValues = {
        { 1, 2 },
        { 3, 4 },
    }
    local total = 0
    for _, pair in ipairs(staticValues) do
        total = total + pair[1] + pair[2]
    end
    local actionName = CName.new("FixtureOtherStructural")
    DoOtherStructuralWork(playerA, playerB, movingA, movingB, singletonA, singletonB, total, actionName)
end)
'@

Write-Mod 'FixtureFrame' @'
local registerRuntimeEvent = registerForEvent
registerRuntimeEvent(
    "onUpdate",
    function(delta)
        DoFrameWork(delta)
    end
)
'@

Write-Mod 'FixtureAlreadyFrame' @'
local __gcetRegisterEvent_132 = registerForEvent
do
    local __gcetOk, __gcetEngine = pcall(GetMod, "0-Engine")
    if __gcetOk and type(__gcetEngine) == "table" and type(__gcetEngine.MakeEventRegistrar) == "function" then
        __gcetRegisterEvent_132 = __gcetEngine.MakeEventRegistrar("FixtureAlreadyFrame", registerForEvent)
    end
end
__gcetRegisterEvent_132("onUpdate", function(delta)
    DoAlreadyFrameWork(delta)
end)
'@

Write-Mod 'FixtureUnknown' @'
Observe("PlayerPuppet", "SomeOtherMethod", function(self)
    DoUnknownWork()
end)
'@

Write-Mod 'FixtureUnknownHot' @'
Observe("PlayerPuppet", "AnotherUnknownMethod", function(self)
    DoExpensiveUnknownWork()
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
        (CallbackRow 109 'FixtureDynamic' 'observe' 'PlayerPuppet::OnAction' 500 6.0 8.0 'init.lua' 1 7),
        (CallbackRow 112 'FixtureGatedDynamic' 'observe' 'PlayerPuppet::OnAction' 495 5.9 7.8 'init.lua' 1 14),
        (CallbackRow 130 'FixtureOverrideStructural' 'Override' 'PlayerPuppet::OnAction' 500 16.0 4.0 'init.lua' 1 8),
        (CallbackRow 131 'FixtureOverridePrefilter' 'Override' 'PlayerPuppet::OnAction' 1400 15.0 4.4 'init.lua' 21 25),
        (CallbackRow 111 'FixtureDownstream' 'observe' 'PlayerPuppet::OnAction' 490 5.8 7.5 'init.lua' 1 10),
        (CallbackRow 120 'FixtureStructural' 'event' 'onUpdate' 60 9.0 2.0 'init.lua' 1 8),
        (CallbackRow 126 'FixtureDiscoveryAuthorRate' 'event' 'onUpdate' 60 13.0 3.2 'init.lua' 6 18),
        (CallbackRow 125 'FixtureDormantOnUpdate' 'event' 'onUpdate' 60 12.0 3.0 'init.lua' 5 9),
        (CallbackRow 121 'FixtureDormantHard' 'observe' 'PlayerPuppet::FixtureTick' 60 6.0 1.2 'init.lua' 5 8),
        (CallbackRow 122 'FixtureDormantDiscovery' 'observe' 'PlayerPuppet::FixtureDiscoveryTick' 60 6.0 1.2 'init.lua' 5 10),
        (CallbackRow 123 'FixtureDormantNever' 'observe' 'PlayerPuppet::FixtureCameraTick' 60 6.0 1.2 'init.lua' 1 4),
        (CallbackRow 128 'FixtureNeverGateUpdate' 'event' 'onUpdate' 60 18.0 4.0 'init.lua' 5 9),
        (CallbackRow 124 'FixtureDormantBackground' 'observe' 'PlayerPuppet::FixtureBackgroundTick' 60 6.0 1.2 'init.lua' 1 3),
        (CallbackRow 129 'FixtureOtherStructural' 'ObserveAfter' 'PlayerPuppet::FixtureStructuralObserve' 120 12.0 3.0 'init.lua' 1 19),
        (CallbackRow 102 'FixtureFrame' 'event' 'onUpdate' 60 5.0 1.0 'init.lua' 2 7),
        (CallbackRow 132 'FixtureAlreadyFrame' 'event' 'onUpdate' 60 5.5 1.1 'init.lua' 8 10),
        # Deliberately no FixtureAbsentSemantic folder exists under $mods.
        (CallbackRow 133 'FixtureAbsentSemantic' 'event' 'onUpdate' 60 7.7 1.4 'init.lua' 1 4),
        (CallbackRow 127 'FixtureUnknownHot' 'observe' 'PlayerPuppet::AnotherUnknownMethod' 60 20.0 5.0 'init.lua' 1 3),
        (CallbackRow 103 'FixtureUnknown' 'observe' 'PlayerPuppet::SomeOtherMethod' 60 4.0 1.0 'init.lua' 1 3)
    )
} | ConvertTo-Json -Depth 30

$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$semanticLibrary = Join-Path $root 'semantic-library.json'
@{
    schemaVersion = '0.1-test'
    entries = @(
        @{
            id = 'fixture-structural'
            identityHints = @('FixtureStructural')
            policyClass = 'TEST_FAST'
            callbacks = @(
                @{ kind = 'event'; target = 'onUpdate'; role = 'hot-path' }
            )
            sourceProof = @{
                ownerAll = @('DoStructuralWork', 'StructuralFixture')
                alreadySatisfiedMarker = 'G-CET semantic:fixture-structural'
            }
            behavior = @{ handler = 'SEMANTIC_TEST_FAST' }
            generation = @{
                enabled = $false
                patchStyle = 'source-injection'
                shipReferenceOverride = $false
            }
        },
        @{
            id = 'fixture-absent'
            identityHints = @('FixtureAbsentSemantic')
            policyClass = 'TEST_ABSENT'
            callbacks = @(
                @{ kind = 'event'; target = 'onUpdate'; role = 'hot-path' }
            )
            sourceProof = @{
                ownerAll = @('NeverCreateThisFile')
                alreadySatisfiedMarker = 'G-CET semantic:fixture-absent'
            }
            behavior = @{ handler = 'SEMANTIC_TEST_ABSENT' }
            generation = @{
                enabled = $true
                patchStyle = 'source-injection'
                shipReferenceOverride = $false
            }
        }
    )
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $semanticLibrary -Encoding utf8

# Users naturally point the resolver at RESULTS. It must locate the newest
# collected CET-* capture itself.
$resolved = (& $resolverExe --capture $results --mods $mods --semantic-library $semanticLibrary --generate-pass --json | ConvertFrom-Json)
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

$onActionFamilies = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONACTION' })
if ($onActionFamilies.Count -eq 0) { throw 'OnAction callback family was not resolved.' }

function Action-For([string]$Owner) {
    foreach ($family in $onActionFamilies) {
        $row = @($family.topConsumers | Where-Object { $_.owner -eq $Owner }) | Select-Object -First 1
        if ($null -ne $row) { return $row }
    }
    throw "OnAction fixture was not ranked: $Owner"
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

$gated = Action-For 'FixtureGatedDynamic'
if (!$gated.generic.Automatable) {
    throw 'Simple state-gated full-stream callback was not resolved.'
}
if ($gated.generic.Pattern -ne 'ACTION_ROUTING_GATED_WILDCARD') {
    throw "Unexpected gated wildcard recipe: $($gated.generic.Pattern)"
}
if (!$gated.generic.Facts.gatedWildcardResolved) {
    throw 'Gated wildcard fact was not emitted.'
}
if ($gated.generic.Facts.dynamicGateExpression -ne '(state.session and state.session.active)') {
    throw "Unexpected gated wildcard expression: $($gated.generic.Facts.dynamicGateExpression)"
}
if (@($gated.generic.Facts.actions) -notcontains 'UI_Apply') {
    throw 'Gated wildcard lost independently routed exact action.'
}

$overrideStructural = Action-For 'FixtureOverrideStructural'
if ($overrideStructural.generic.Automatable) {
    throw 'Structural fallback must not authorize generic AUTO for an Override.'
}
if ($overrideStructural.generic.Pattern -ne 'STRUCTURAL_HOTPATH_EVIDENCE') {
    throw "Override structural evidence exposed the wrong pattern: $($overrideStructural.generic.Pattern)"
}
if (@($overrideStructural.generic.RecipeFamilies).Count -ne 0) {
    throw 'Analysis-only Override structural evidence leaked an automatic recipe.'
}
if (!$overrideStructural.generic.Facts.structuralHotpath) {
    throw 'Override structural opportunity was not retained as analysis evidence.'
}
$overrideBlockers = (@($overrideStructural.generic.Blockers) -join ' ')
if ($overrideBlockers -notmatch 'Override semantics' -or $overrideBlockers -notmatch 'analysis-only') {
    throw 'Override structural blockers no longer explain routing and semantic-rule requirements.'
}

$overridePrefilter = Action-For 'FixtureOverridePrefilter'
if (!$overridePrefilter.generic.Automatable) {
    $overrideDebug = $overridePrefilter.generic | ConvertTo-Json -Depth 10 -Compress
    throw "Transparent finite OnAction Override was not made auto-patchable. Resolver: $overrideDebug"
}
if ($overridePrefilter.generic.Pattern -ne 'ACTION_OVERRIDE_EXACT_PREFILTER') {
    throw "Unexpected Override prefilter recipe: $($overridePrefilter.generic.Pattern)"
}
if (!$overridePrefilter.generic.Facts.overridePrefilterProven) {
    throw 'Transparent Override proof fact was not emitted.'
}
if ($overridePrefilter.generic.Facts.overridePrefilterGateReceiver -ne 'current' -or
    $overridePrefilter.generic.Facts.overridePrefilterGateMember -ne 'traceActions') {
    throw "Override trace gate was not preserved: $($overridePrefilter.generic.Facts.overridePrefilterGateReceiver).$($overridePrefilter.generic.Facts.overridePrefilterGateMember)"
}
foreach ($expected in @('ChoiceScrollUp','ChoiceScrollDown','ChoiceApply')) {
    if (@($overridePrefilter.generic.Facts.actions) -notcontains $expected) {
        throw "Override downstream expansion lost expected action: $expected"
    }
}

$downstream = Action-For 'FixtureDownstream'
if (!$downstream.generic.Automatable) {
    throw 'Finite owner-local downstream action handler was not resolved generically.'
}
if ($downstream.generic.Pattern -ne 'ACTION_ROUTING_DOWNSTREAM_STATIC_SET') {
    throw "Unexpected downstream expansion recipe: $($downstream.generic.Pattern)"
}
if (!$downstream.generic.Facts.downstreamExpanded) {
    throw 'Downstream expansion fact was not emitted.'
}
foreach ($expected in @('Jump','MoveX','MoveY','PhotoMode_CameraMovementX','UI_MoveUp','UI_MoveDown')) {
    if (@($downstream.generic.Facts.actions) -notcontains $expected) {
        throw "Downstream action expansion lost expected action: $expected"
    }
}
if (@($downstream.generic.Facts.downstreamMethods) -notcontains 'HandleInput') {
    throw 'Downstream method provenance was not emitted.'
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

$alreadyFrame = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureAlreadyFrame' }) | Select-Object -First 1
if ($null -eq $alreadyFrame) { throw 'FixtureAlreadyFrame was not ranked inside onUpdate.' }
if ($alreadyFrame.generic.Automatable) { throw 'Already-consolidated frame callback was incorrectly re-authorized.' }
if ($alreadyFrame.generic.Status -ne 'ALREADY_SATISFIED') {
    throw "Already-consolidated frame state was not recognized: $($alreadyFrame.generic.Status)"
}
if ($alreadyFrame.disposition -ne 'ALREADY_SATISFIED') {
    throw "Already-consolidated callback received wrong disposition: $($alreadyFrame.disposition)"
}
if (@($alreadyFrame.generic.RecipeFamilies) -contains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Already-consolidated callback exposed another frame transform.'
}

$structural = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureStructural' }) | Select-Object -First 1
if ($null -eq $structural) { throw 'FixtureStructural was not ranked inside onUpdate.' }
if (!$structural.generic.Automatable) { throw 'Raw structural onUpdate should still receive safe frame consolidation.' }
if (@($structural.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Safe frame consolidation disappeared from the structural fixture.'
}
if (@($structural.generic.RecipeFamilies) -contains 'STRUCTURAL_HOTPATH_REWRITE') {
    throw 'Structural hotpath rewrite leaked back into generic AUTO.'
}
if (!$structural.generic.Facts.structuralHotpath -or
    @($structural.generic.Facts.identicalExpressions).Count -lt 1 -or
    @($structural.generic.Facts.literalConstructors).Count -lt 1) {
    throw 'Structural opportunity was not retained as analysis evidence.'
}

if (!$result.semanticLibrary.loaded -or [int]$result.semanticLibrary.entryCount -ne 2) {
    throw 'Semantic library fixtures were not loaded.'
}
if (!$structural.semantic.Matched) {
    throw 'Measured FixtureStructural callback did not match its semantic rule.'
}
if (!$structural.semantic.SourceProofSatisfied) {
    $semanticDebug = $structural.semantic | ConvertTo-Json -Depth 10 -Compress
    throw "Semantic source graph did not prove FixtureStructural: $semanticDebug"
}
if ($structural.semantic.RuleId -ne 'fixture-structural') {
    throw "Wrong semantic rule selected: $($structural.semantic.RuleId)"
}
if ($structural.semantic.GenerationEnabled) {
    throw 'Analysis-only semantic fixture unexpectedly authorized generation.'
}
if ($structural.semantic.PatchStyle -ne 'source-injection' -or $structural.semantic.ShipReferenceOverride) {
    throw 'Semantic fixture violated source-injection/no-override policy.'
}
if ([int]$structural.semantic.Graph.luaFileCount -lt 1) {
    throw 'Semantic mod graph did not enumerate the live owner folder.'
}

$absentSemantic = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureAbsentSemantic' }) | Select-Object -First 1
if ($null -eq $absentSemantic) {
    throw 'Absent semantic fixture was not ranked inside onUpdate.'
}
if (!$absentSemantic.semantic.Matched) {
    throw 'Absent semantic fixture did not match the catalog identity/callback selector.'
}
if ($absentSemantic.semantic.SourceProofSatisfied) {
    throw 'A semantic rule passed source proof even though its live mod folder does not exist.'
}
if ($absentSemantic.semantic.GenerationReady) {
    throw 'An absent live mod was incorrectly marked semantic-generation ready.'
}
if ($absentSemantic.disposition -ne 'SEMANTIC_RULE_NEEDS_SOURCE_PROOF') {
    throw "Absent semantic fixture received wrong disposition: $($absentSemantic.disposition)"
}

$discoveryAuthor = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureDiscoveryAuthorRate' }) | Select-Object -First 1
if ($null -eq $discoveryAuthor) { throw 'FixtureDiscoveryAuthorRate was not ranked inside onUpdate.' }
if ($discoveryAuthor.dormancy.Class -ne 'DISCOVERY_DORMANT') {
    throw "Expected DISCOVERY_DORMANT author-rate classification, got $($discoveryAuthor.dormancy.Class)"
}
if (!$discoveryAuthor.dormancy.AuthorDiscoveryCadenceProven) {
    throw "Author-paced discovery cadence was not proven. Blocker=$($discoveryAuthor.dormancy.AuthorDiscoveryBlocker)"
}
if ([math]::Abs([double]$discoveryAuthor.dormancy.AuthorDiscoveryIntervalSeconds - 1.0) -gt 0.0001) {
    throw "Unexpected discovery interval: $($discoveryAuthor.dormancy.AuthorDiscoveryIntervalSeconds)"
}
if ($discoveryAuthor.dormancy.AuthorDiscoveryAccumulator -ne 'discoveryTimer') {
    throw "Unexpected discovery accumulator: $($discoveryAuthor.dormancy.AuthorDiscoveryAccumulator)"
}
if ($discoveryAuthor.dormancy.AuthorDiscoveryGate -ne 'active') {
    throw "Unexpected discovery active gate: $($discoveryAuthor.dormancy.AuthorDiscoveryGate)"
}
if (!$discoveryAuthor.dormancy.DiscoveryRegionSelfContained) {
    throw 'Self-contained author discovery region was not recognized.'
}
if (!$discoveryAuthor.dormancy.EvidenceOnly) {
    throw 'Dormancy classification evidence should remain visible even when a finite generic recipe is available.'
}
if (!$discoveryAuthor.generic.Automatable -or
    @($discoveryAuthor.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Discovery fixture lost the safe frame-dispatch transform.'
}
if (@($discoveryAuthor.generic.RecipeFamilies) -contains 'AUTHOR_DISCOVERY_DORMANT_SCHEDULE') {
    throw 'Discovery dormancy leaked into generic AUTO.'
}
$discoveryBlockers = (@($discoveryAuthor.generic.Blockers) -join ' ')
if ($discoveryBlockers -notmatch 'analysis-only') {
    throw 'Discovery semantic opportunity is missing the analysis-only blocker.'
}

$neverUpdate = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureNeverGateUpdate' }) | Select-Object -First 1
if ($null -eq $neverUpdate) { throw 'FixtureNeverGateUpdate was not ranked inside onUpdate.' }
if ($neverUpdate.dormancy.Class -ne 'NEVER_GATE') {
    throw "Sensitive onUpdate was not classified NEVER_GATE: $($neverUpdate.dormancy.Class)"
}
if (@($neverUpdate.generic.RecipeFamilies) -contains 'HARD_DORMANT_GUARD_HOIST') {
    throw 'Sensitive camera onUpdate incorrectly received HARD_DORMANT_GUARD_HOIST.'
}
if ($neverUpdate.advanced.Eligible) {
    throw 'Sensitive camera onUpdate incorrectly became Advanced dormancy eligible.'
}

$hardUpdate = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureDormantOnUpdate' }) | Select-Object -First 1
if ($null -eq $hardUpdate) { throw 'FixtureDormantOnUpdate was not ranked inside onUpdate.' }
if (!$hardUpdate.generic.Automatable) { throw 'Dormant onUpdate should still receive safe frame consolidation.' }
if (@($hardUpdate.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Dormant fixture lost the safe frame-dispatch transform.'
}
if (@($hardUpdate.generic.RecipeFamilies) -contains 'HARD_DORMANT_GUARD_HOIST') {
    throw 'Hard dormant guard-hoist leaked into generic AUTO.'
}
if ($hardUpdate.generic.Facts.hardDormantGateExpression -ne 'active') {
    throw "Unexpected hard dormant gate: $($hardUpdate.generic.Facts.hardDormantGateExpression)"
}
if ([int]$hardUpdate.generic.Facts.hardDormantPreGuardReadCount -lt 1) {
    throw 'Hard dormant proof did not record pre-guard read/setup work.'
}

function Dormancy-For([string]$Owner) {
    foreach ($family in @($result.callbackFamilies)) {
        $row = @($family.topConsumers | Where-Object { $_.owner -eq $Owner }) | Select-Object -First 1
        if ($null -ne $row) { return $row.dormancy }
    }
    throw "Dormancy fixture was not ranked: $Owner"
}

$hardDormancy = Dormancy-For 'FixtureDormantHard'
if ($hardDormancy.Class -ne 'HARD_DORMANT' -or !$hardDormancy.EvidenceOnly) {
    throw "Expected evidence-only HARD_DORMANT, got $($hardDormancy.Class)"
}
if (!$hardDormancy.CompleteWakePathProven -or @($hardDormancy.StateWriterSignals).Count -lt 1) {
    throw 'HARD_DORMANT fixture did not preserve its external gate-writer/wake proof.'
}

$discoveryDormancy = Dormancy-For 'FixtureDormantDiscovery'
if ($discoveryDormancy.Class -ne 'DISCOVERY_DORMANT' -or !$discoveryDormancy.EvidenceOnly) {
    throw "Expected evidence-only DISCOVERY_DORMANT, got $($discoveryDormancy.Class)"
}
if ($discoveryDormancy.CompleteWakePathProven) {
    throw 'DISCOVERY_DORMANT must not be considered a complete hard-dormant wake path.'
}

$neverDormancy = Dormancy-For 'FixtureDormantNever'
if ($neverDormancy.Class -ne 'NEVER_GATE' -or !$neverDormancy.EvidenceOnly) {
    throw "Expected evidence-only NEVER_GATE, got $($neverDormancy.Class)"
}
$neverRow = $null
foreach ($family in @($result.callbackFamilies)) {
    $candidateNever = @($family.topConsumers | Where-Object { $_.owner -eq 'FixtureDormantNever' }) | Select-Object -First 1
    if ($null -ne $candidateNever) { $neverRow = $candidateNever; break }
}
if ($null -eq $neverRow -or $neverRow.advanced.Eligible) {
    throw 'NEVER_GATE callback was incorrectly made eligible for Advanced dormancy.'
}

$backgroundDormancy = Dormancy-For 'FixtureDormantBackground'
if ($backgroundDormancy.Class -ne 'BACKGROUND' -or !$backgroundDormancy.EvidenceOnly) {
    throw "Expected evidence-only BACKGROUND, got $($backgroundDormancy.Class)"
}

$otherStructural = $null
foreach ($family in @($result.callbackFamilies)) {
    $candidateOther = @($family.topConsumers | Where-Object { $_.owner -eq 'FixtureOtherStructural' }) | Select-Object -First 1
    if ($null -ne $candidateOther) { $otherStructural = $candidateOther; break }
}
if ($null -eq $otherStructural) { throw 'FixtureOtherStructural was not ranked.' }
if ($otherStructural.generic.Automatable) {
    throw 'Hot non-onUpdate structural callback must remain analysis-only.'
}
if ($otherStructural.generic.Pattern -ne 'STRUCTURAL_HOTPATH_EVIDENCE') {
    throw "Unexpected structural evidence pattern: $($otherStructural.generic.Pattern)"
}
if (@($otherStructural.generic.RecipeFamilies).Count -ne 0) {
    throw 'Non-onUpdate structural evidence leaked an automatic recipe.'
}
if (@($otherStructural.generic.Facts.identicalExpressions).Count -lt 1 -or
    @($otherStructural.generic.Facts.literalConstructors).Count -lt 1 -or
    @($otherStructural.generic.Facts.staticLiteralTables).Count -lt 1) {
    throw 'Non-onUpdate structural analysis facts were lost.'
}

$unknown = $null
foreach ($otherFamily in @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'OTHER' })) {
    $candidateUnknown = @($otherFamily.topConsumers | Where-Object { $_.owner -eq 'FixtureUnknown' }) | Select-Object -First 1
    if ($null -ne $candidateUnknown) {
        $unknown = $candidateUnknown
        break
    }
}
if ($null -eq $unknown) { throw 'FixtureUnknown was not ranked.' }
if ($unknown.generic.Automatable) { throw 'Unsupported callback family was incorrectly marked automatable.' }
if (!$unknown.registry.checkedAfterGenericExhausted) {
    throw 'Unresolved high-impact callback did not reach the registry fallback stage.'
}
if ($unknown.registry.matched) {
    throw 'Empty high-impact exception registry unexpectedly matched a callback.'
}
if ($unknown.advanced.Eligible) {
    throw 'Low-cost unknown callback should not bother the user in Advanced mode.'
}

$unknownHot = $null
foreach ($otherFamily in @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'OTHER' })) {
    $candidateHot = @($otherFamily.topConsumers | Where-Object { $_.owner -eq 'FixtureUnknownHot' }) | Select-Object -First 1
    if ($null -ne $candidateHot) { $unknownHot = $candidateHot; break }
}
if ($null -eq $unknownHot) { throw 'FixtureUnknownHot was not ranked.' }
if (!$unknownHot.advanced.Eligible -or !$unknownHot.advanced.UserClassificationUseful) {
    throw 'High-cost UNKNOWN callback should request user classification in Advanced mode.'
}
if ($unknownHot.advanced.NextEvidence -ne 'USER_CLASSIFICATION') {
    throw "Unexpected high-cost UNKNOWN next evidence: $($unknownHot.advanced.NextEvidence)"
}

if ($null -eq $resolved.pass) {
    throw 'CLI --generate-pass did not return a pass result.'
}
if ([int]$resolved.pass.TransformCount -ne 15) {
    if (Test-Path -LiteralPath $resolved.pass.ManifestPath -PathType Leaf) {
        $failedManifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
        Write-Host "PASS DEBUG transforms=$($failedManifest.summary.transforms) skipped=$($failedManifest.summary.skipped)"
        foreach ($skip in @($failedManifest.skipped)) {
            Write-Host ("PASS DEBUG SKIP owner={0} type={1} file={2} reason={3}" -f $skip.owner,$skip.type,$skip.file,$skip.reason)
        }
    }
    throw "Expected 15 generated transforms, got $($resolved.pass.TransformCount)."
}
if ([int]$resolved.pass.FileCount -ne 19) {
    throw "Expected 19 generated replacement files (15 callback + 4 fixed 0-Engine), got $($resolved.pass.FileCount)."
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
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureStructural/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDiscoveryAuthorRate/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDormantOnUpdate/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOverridePrefilter/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixturePattern/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDownstream/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureGatedDynamic/init.lua',
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
    if ('bin/x64/plugins/cyber_engine_tweaks/mods/FixtureAbsentSemantic/init.lua' -in $names) {
        throw 'Semantic catalog created a file for a mod that is not installed.'
    }
    foreach ($parkedEntry in @(
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOtherStructural/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOverrideStructural/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureAlreadyFrame/init.lua'
    )) {
        if ($parkedEntry -in $names) {
            throw "Analysis-only/already-satisfied callback leaked into generated pass: $parkedEntry"
        }
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
    if ($actionText -notmatch 'decodeType\s*=\s*false') {
        throw 'Generated exact action route is missing decodeType=false.'
    }
    if ($actionText -notmatch '__gcetRoutedName' -or
        $actionText -notmatch '__gcetRoutedName\s+or') {
        throw 'Generated exact action route did not reuse the router-decoded action name.'
    }

    $frameText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureFrame/init.lua'
    if ($frameText -notmatch 'MakeEventRegistrar' -or
        $frameText -notmatch '__gcetRegisterEvent_102\s*\(\s*"onUpdate"') {
        throw 'Generated frame-dispatch replacement is incomplete.'
    }
    if ($frameText -match '(?m)^\s*registerRuntimeEvent\s*\(\s*"onUpdate"') {
        throw 'Generated frame-dispatch replacement left the original runtime registrar call active.'
    }

    $structuralText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureStructural/init.lua'
    if ($structuralText -notmatch '__gcetRegisterEvent_120\s*\(\s*"onUpdate"') {
        throw 'Structural fixture lost safe frame-dispatch consolidation.'
    }
    if ($structuralText -match '__gcetReuse_120_' -or $structuralText -match '__gcetStatic_120_') {
        throw 'Structural hotpath rewrite leaked into the safe generic pass.'
    }
    if ([regex]::Matches($structuralText, [regex]::Escape('Game.GetPlayer()')).Count -ne 2 -or
        [regex]::Matches($structuralText, [regex]::Escape('CName.new("StructuralFixture")')).Count -ne 2) {
        throw 'Analysis-only structural expressions were modified by generic AUTO.'
    }

    $discoveryText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDiscoveryAuthorRate/init.lua'
    if ($discoveryText -notmatch '__gcetRegisterEvent_126\s*\(\s*"onUpdate"') {
        throw 'Discovery fixture lost safe frame-dispatch consolidation.'
    }
    if ($discoveryText -match 'Schedule\.Every' -or $discoveryText -match '__gcetDiscovery_126') {
        throw 'Discovery dormancy rewrite leaked into generic AUTO.'
    }
    if ([regex]::Matches($discoveryText, [regex]::Escape('CheckNearbyActivity(distance)')).Count -ne 1) {
        throw 'Generic AUTO changed the author discovery body.'
    }

    $hardUpdateText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDormantOnUpdate/init.lua'
    if ($hardUpdateText -notmatch '__gcetRegisterEvent_125\s*\(\s*"onUpdate"') {
        throw 'Dormant fixture lost safe frame-dispatch consolidation.'
    }
    if ($hardUpdateText -match 'G-CET dormant guard hoist') {
        throw 'Dormant guard-hoist leaked into generic AUTO.'
    }
    $originalGetterIndex = $hardUpdateText.IndexOf('local player = Game.GetPlayer()')
    $originalGuardIndex = $hardUpdateText.IndexOf('if not active then return end')
    if ($originalGetterIndex -lt 0 -or $originalGuardIndex -lt 0 -or $originalGetterIndex -gt $originalGuardIndex) {
        throw 'Generic AUTO changed the dormant callback execution order.'
    }

    $overridePrefilterText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOverridePrefilter/init.lua'
    foreach ($requiredOverrideText in @(
        'Override("PlayerPuppet", "OnAction"',
        '__gcetOverrideActions_131',
        'ChoiceScrollUp',
        'ChoiceScrollDown',
        'ChoiceApply',
        'pcall(function() return Game.NameToString(action:GetName()) end)',
        'not current.traceActions',
        'wrappedMethod(action, consumer)',
        'G-CET finite Override prefilter'
    )) {
        if ($overridePrefilterText -notmatch [regex]::Escape($requiredOverrideText)) {
            throw "Generated Override prefilter is missing: $requiredOverrideText"
        }
    }
    if ($overridePrefilterText -match 'SubscribeAction') {
        throw 'Override prefilter incorrectly converted the Override to ActionRouter routing.'
    }

    $patternText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixturePattern/init.lua'
    if ($patternText -notmatch 'actions = "\*"' -or
        $patternText -notmatch 'string\.find\(routedName, "Turn", 1, true\)') {
        throw 'Generated mixed exact+pattern router did not preserve the resolver pattern.'
    }
    if ($patternText -notmatch '__gcetOnAction_110\(this, action, consumer, routedName\)') {
        throw 'Pattern router did not pass its already-decoded action name into the original callback.'
    }

    $downstreamText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDownstream/init.lua'
    foreach ($expectedAction in @('Jump','MoveX','MoveY','PhotoMode_CameraMovementX','UI_MoveUp','UI_MoveDown')) {
        if ($downstreamText -notmatch [regex]::Escape($expectedAction)) {
            throw "Generated downstream router lost expected action: $expectedAction"
        }
    }

    $gatedText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureGatedDynamic/init.lua'
    foreach ($requiredGatedText in @('actions = "*"','UI_Apply','state.session and state.session.active','GatedWildcard','__gcetOnAction_112(this, action, consumer, routedName)')) {
        if ($gatedText -notmatch [regex]::Escape($requiredGatedText)) {
            throw "Generated gated wildcard router is missing: $requiredGatedText"
        }
    }

    $manifest = (Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw) | ConvertFrom-Json
    if ($manifest.policy.selection -ne 'MEASURED_GENERIC_PLUS_SOURCE_PROVEN_SEMANTIC_CANDIDATES_FROM_G-CET_Resolver.json') {
        throw 'Generated pass manifest is not using the combined generic + semantic resolver selection model.'
    }
    if ($manifest.policy.modNameRules) {
        throw 'Generated pass manifest unexpectedly allows mod-name rules.'
    }
    if (!$manifest.policy.cadenceTransforms) {
        throw 'Generated pass manifest did not advertise semantic cadence/source-injection support.'
    }
    if ([double]$manifest.policy.semanticRuntimeThresholdMsPerSecond -ne 3.0) {
        throw 'Semantic runtime admission threshold changed unexpectedly.'
    }
    if (!$manifest.policy.semanticCurrentSourceProofRequired) {
        throw 'Semantic generation no longer requires current-source proof.'
    }
    if (!$manifest.policy.semanticExistingLiveFilesOnly -or $manifest.policy.semanticCreatesNewModFiles) {
        throw 'Semantic pass policy no longer guarantees existing-live-file-only output.'
    }
    if ($manifest.policy.semanticReferenceOverridesShipped) {
        throw 'Semantic development reference overrides leaked into the deployable pass policy.'
    }
    foreach ($forbiddenType in @(
        'STRUCTURAL_HOTPATH_REWRITE',
        'HARD_DORMANT_GUARD_HOIST',
        'AUTHOR_DISCOVERY_DORMANT_SCHEDULE',
        'AUTHOR_CADENCE_WHOLE_CALLBACK'
    )) {
        if (@($manifest.transforms | Where-Object { $_.type -eq $forbiddenType }).Count -ne 0) {
            throw "Parked semantic transform leaked into the manifest: $forbiddenType"
        }
    }
    if ([int]$manifest.summary.transforms -ne 15) {
        throw 'Generated pass manifest transform count is wrong.'
    }
    if ([int]$manifest.summary.genericTransforms -ne 15 -or [int]$manifest.summary.semanticTransforms -ne 0) {
        throw 'Analysis-only semantic fixture unexpectedly changed transform accounting.'
    }
    if ([int]$manifest.summary.callbackFiles -ne 15) {
        throw "Expected 15 callback replacement files, got $($manifest.summary.callbackFiles)."
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

$hintPath = Join-Path $capture 'G-CET_Advanced_UserHints.json'
@{
    schemaVersion = '0.1'
    entries = @(
        @{
            owner = 'FixtureUnknownHot'
            kind = 'observe'
            target = 'PlayerPuppet::AnotherUnknownMethod'
            classification = 'HARD_DORMANT'
        }
    )
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $hintPath -Encoding utf8

$hinted = (& $resolverExe --capture $results --mods $mods --json | ConvertFrom-Json)
if (!$hinted.ok) { throw 'Resolver failed after evidence-only Advanced user hint was saved.' }
$hintDocument = Get-Content -LiteralPath $hinted.resolverPath -Raw | ConvertFrom-Json
$hintedHot = $null
foreach ($family in @($hintDocument.callbackFamilies)) {
    $candidate = @($family.topConsumers | Where-Object { $_.owner -eq 'FixtureUnknownHot' }) | Select-Object -First 1
    if ($null -ne $candidate) { $hintedHot = $candidate; break }
}
if ($null -eq $hintedHot) { throw 'Advanced user hint test lost FixtureUnknownHot.' }
if ($hintedHot.generic.Automatable) {
    throw 'User classification hint incorrectly authorized a source transform.'
}
if (!$hintedHot.advanced.UserHintAppliedAsEvidenceOnly -or $hintedHot.advanced.UserHint -ne 'HARD_DORMANT') {
    throw 'Resolver did not preserve the saved Advanced user classification as evidence.'
}
if ($hintedHot.advanced.NextEvidence -ne 'SOURCE_ACTIVE_STATE_WAKE_PROOF') {
    throw "User HARD_DORMANT hint did not steer the next proof request: $($hintedHot.advanced.NextEvidence)"
}

Write-Host 'Callback-first resolver + V1 pass generator contract passed.'
