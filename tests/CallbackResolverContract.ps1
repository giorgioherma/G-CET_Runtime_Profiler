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

Write-Mod 'FixtureWrappedAction' @'
local Event = {}
Event.Observe("PlayerPuppet", "OnAction", function(_, action)
    if modulesLoaded then
        game:handleInput(action)
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

Write-Mod 'FixtureNamedCName' @'
local n_ToggleSprint = n"ToggleSprint"
Observe("PlayerPuppet", "OnAction", function(this, action)
    if action:IsAction(action, n_ToggleSprint) then
        DoNamedCNameWork()
    end
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

Write-Mod 'FixtureWrappedOverride' @'
local Event = {}
Event.Override("PlayerPuppet", "OnAction", function(self, action, consumer, wrappedMethod)
    local name = Game.NameToString(action:GetName())
    if name == "Jump" then DoWrappedOverrideWork() end
    return wrappedMethod(action, consumer)
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
    local timeSystem = Game.GetTimeSystem()
    local navigationSystem = Game.GetNavigationSystem()
    local allBlackboardDefs = Game.GetAllBlackboardDefs()
    local futureProvider = Game.GetTotallyNewSharedThing()
    local nameA = CName.new("StructuralFixture")
    local nameB = CName.new("StructuralFixture")
    DoStructuralWork(playerA, playerB, timeSystem, navigationSystem, allBlackboardDefs, futureProvider, nameA, nameB, delta)
end)
'@

Write-Mod 'FixtureColdProvider' @'
registerForEvent("onUpdate", function(delta)
    local player = Game.GetPlayer()
    local pos = player:GetWorldPosition()
    DoColdWork(player, pos, delta)
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
local existingSchedulerJobId = "fixture.frame.scheduled"
'@

Write-Mod 'FixtureDraw' @'
registerForEvent("onDraw", function()
    DrawFixture()
end)
'@

Write-Mod 'FixtureInteractionUi' @'
local ui = { hubShown = false, input = false }

function ui.update()
    local hubs = getDialogChoiceHubs()

    if ui.hubShown and #hubs > 0 then
        DrawInteraction(hubs)
    elseif ui.hubShown then
        DrawEmptyInteraction()
    end

    ui.input = false
end

registerForEvent("onDraw", function()
    ui.update()
end)
'@

Write-Mod 'FixtureInteractionUiUnsafe' @'
local ui = { hubShown = false, input = false }

function ui.update()
    local hubs = getDialogChoiceHubs()
    RefreshUiState()

    if ui.hubShown and #hubs > 0 then
        DrawInteraction(hubs)
    end

    ui.input = false
end

registerForEvent("onDraw", function()
    ui.update()
end)
'@

Write-Mod 'FixtureAlreadyDraw' @'
local registerRuntimeEvent = registerForEvent
do
    local ok, engine = pcall(GetMod, "0-Engine")
    if ok and type(engine) == "table" and type(engine.MakeEventRegistrar) == "function" then
        registerRuntimeEvent = engine.MakeEventRegistrar("FixtureAlreadyDraw", registerForEvent)
    end
end
registerRuntimeEvent("onDraw", function()
    DrawAlreadyIntegratedFixture()
end)
'@

Write-Mod 'FixtureWrappedFrame' @'
local Event = {}
Event.registerForEvent("onUpdate", function(delta)
    DoWrappedFrameWork(delta)
end)
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
Write-Mod 'FixtureOwnerRegistrar' @'
local registerRuntimeEvent = registerForEvent
do
    local ok, engine = pcall(GetMod, "0-Engine")
    if ok and type(engine) == "table" and type(engine.MakeEventRegistrar) == "function" then
        registerRuntimeEvent = engine.MakeEventRegistrar("FixtureOwnerRegistrar", registerForEvent)
    end
end
registerRuntimeEvent("onUpdate", function(delta)
    DoOwnerRegistrarWork(delta)
end)
'@

Write-Mod 'FixtureSchedulerBootstrap' @'
local pass4Attached = false
local function TryAttachFixtureSchedulerBootstrap()
    local jobId = "fixture.bootstrap.scheduled"
    pass4Attached = true
    return jobId
end
registerForEvent("onUpdate", function(delta)
    if pass4Attached then return end
    if TryAttachFixtureSchedulerBootstrap then
        TryAttachFixtureSchedulerBootstrap()
    end
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

$schedulerDir = Join-Path $capture 'Data\Scheduler'
New-Item -ItemType Directory -Force $schedulerDir | Out-Null
@(
    'Owner,JobType,Job,IntervalValue,IntervalUnit,Calls,CallsPerSecond,TotalMs,MsPerSecond,MeasuredOneCorePct,AvgUs,MaxMs,ElapsedSeconds,Interpretation',
    'ManualAlias,timed,fixture.frame.scheduled,0.500000,seconds,20,2.000000,4.000000,0.400000,0.040000,200.000000,0.800000,10.000000,inclusive-inside-0-engine-do-not-add-to-mod-totals',
    'ManualBootstrap,timed,fixture.bootstrap.scheduled,0.500000,seconds,20,2.000000,4.000000,0.400000,0.040000,200.000000,0.800000,10.000000,inclusive-inside-0-engine-do-not-add-to-mod-totals'
) | Set-Content -LiteralPath (Join-Path $schedulerDir 'CET_Runtime_Profile_Scheduler_ByJob.csv') -Encoding utf8

$handoff = @{
    schemaVersion = '1.5'
    callbacks = @(
        # Deliberately use bare init.lua for most rows. The resolver must scope
        # this otherwise-ambiguous source filename to the measured owner first.
        (CallbackRow 101 'FixtureAction' 'observe' 'PlayerPuppet::OnAction' 900 12.0 20.0 'init.lua' 1 6),
        (CallbackRow 134 'FixtureWrappedAction' 'observe' 'PlayerPuppet::OnAction' 880 11.5 19.0 'init.lua' 2 6),
        (CallbackRow 104 'FixtureSingleton' 'observe' 'PlayerPuppet::OnAction' 800 11.0 18.0 'init.lua' 1 7),
        (CallbackRow 105 'FixtureCName' 'observe' 'PlayerPuppet::OnAction' 700 10.0 16.0 'init.lua' 2 6),
        (CallbackRow 139 'FixtureNamedCName' 'observe' 'PlayerPuppet::OnAction' 690 9.5 15.0 'init.lua' 2 6),
        (CallbackRow 106 'FixtureSelector' 'observe' 'PlayerPuppet::OnAction' 650 9.0 14.0 'init.lua' 3 9),
        (CallbackRow 107 'FixtureConsumer' 'observe' 'PlayerPuppet::OnAction' 600 8.0 12.0 'init.lua' 1 7),
        (CallbackRow 110 'FixturePattern' 'observe' 'PlayerPuppet::OnAction' 575 7.5 11.0 'init.lua' 1 12),
        (CallbackRow 108 'FixtureNeighborOverride' 'observe' 'PlayerPuppet::OnAction' 550 7.0 10.0 'init.lua' 1 6),
        (CallbackRow 109 'FixtureDynamic' 'observe' 'PlayerPuppet::OnAction' 500 6.0 8.0 'init.lua' 1 7),
        (CallbackRow 112 'FixtureGatedDynamic' 'observe' 'PlayerPuppet::OnAction' 495 5.9 7.8 'init.lua' 1 14),
        (CallbackRow 130 'FixtureOverrideStructural' 'Override' 'PlayerPuppet::OnAction' 500 16.0 4.0 'init.lua' 1 8),
        (CallbackRow 135 'FixtureWrappedOverride' 'Override' 'PlayerPuppet::OnAction' 500 14.0 3.8 'init.lua' 2 7),
        (CallbackRow 131 'FixtureOverridePrefilter' 'Override' 'PlayerPuppet::OnAction' 1400 15.0 4.4 'init.lua' 21 25),
        (CallbackRow 111 'FixtureDownstream' 'observe' 'PlayerPuppet::OnAction' 490 5.8 7.5 'init.lua' 1 10),
        (CallbackRow 120 'FixtureStructural' 'event' 'onUpdate' 60 9.0 2.0 'init.lua' 1 11),
        (CallbackRow 126 'FixtureDiscoveryAuthorRate' 'event' 'onUpdate' 60 13.0 3.2 'init.lua' 6 18),
        (CallbackRow 125 'FixtureDormantOnUpdate' 'event' 'onUpdate' 60 12.0 3.0 'init.lua' 5 9),
        (CallbackRow 121 'FixtureDormantHard' 'observe' 'PlayerPuppet::FixtureTick' 60 6.0 1.2 'init.lua' 5 8),
        (CallbackRow 122 'FixtureDormantDiscovery' 'observe' 'PlayerPuppet::FixtureDiscoveryTick' 60 6.0 1.2 'init.lua' 5 10),
        (CallbackRow 123 'FixtureDormantNever' 'observe' 'PlayerPuppet::FixtureCameraTick' 60 6.0 1.2 'init.lua' 1 4),
        (CallbackRow 128 'FixtureNeverGateUpdate' 'event' 'onUpdate' 60 18.0 4.0 'init.lua' 5 9),
        (CallbackRow 124 'FixtureDormantBackground' 'observe' 'PlayerPuppet::FixtureBackgroundTick' 60 6.0 1.2 'init.lua' 1 3),
        (CallbackRow 129 'FixtureOtherStructural' 'ObserveAfter' 'PlayerPuppet::FixtureStructuralObserve' 120 12.0 3.0 'init.lua' 1 19),
        (CallbackRow 102 'FixtureFrame' 'event' 'onUpdate' 60 5.0 1.0 'init.lua' 2 7),
        (CallbackRow 140 'FixtureDraw' 'event' 'onDraw' 60 4.8 0.9 'init.lua' 1 3),
        (CallbackRow 142 'FixtureInteractionUi' 'event' 'onDraw' 60 4.0 0.8 'init.lua' 15 17),
        (CallbackRow 143 'FixtureInteractionUiUnsafe' 'event' 'onDraw' 60 4.0 0.8 'init.lua' 14 16),
        (CallbackRow 141 'FixtureAlreadyDraw' 'event' 'onDraw' 60 4.7 0.8 'init.lua' 8 10),
        (CallbackRow 136 'FixtureWrappedFrame' 'event' 'onUpdate' 60 5.2 1.0 'init.lua' 2 4),
        (CallbackRow 132 'FixtureAlreadyFrame' 'event' 'onUpdate' 60 5.5 1.1 'init.lua' 8 10),
        (CallbackRow 137 'FixtureOwnerRegistrar' 'event' 'onUpdate' 60 6.2 1.2 'init.lua' 8 10),
        (CallbackRow 138 'FixtureSchedulerBootstrap' 'event' 'onUpdate' 60 0.05 0.01 'init.lua' 7 12),
        # Deliberately no FixtureAbsentSemantic folder exists under $mods.
        (CallbackRow 133 'FixtureAbsentSemantic' 'event' 'onUpdate' 60 7.7 1.4 'init.lua' 1 4),
        (CallbackRow 127 'FixtureUnknownHot' 'observe' 'PlayerPuppet::AnotherUnknownMethod' 60 20.0 5.0 'init.lua' 1 3),
        (CallbackRow 103 'FixtureUnknown' 'observe' 'PlayerPuppet::SomeOtherMethod' 60 4.0 1.0 'init.lua' 1 3)
    )
    optimizerEvidence = @(
        @{
            registrationId = 120
            owner = 'FixtureStructural'
            kind = 'event'
            target = 'onUpdate'
            deep = @{
                hotCallees = @(
                    @{
                        functionName = 'GetPlayer'
                        sampledCalls = 24
                        repeatedInSampleCount = 8
                        multiCallsiteSampleCount = 8
                    }
                )
                # GetTimeSystem is deliberately absent from hotCallees. It must
                # still reach shared-provider analysis through the untruncated
                # known-provider stream.
                sharedProviderCallees = @(
                    @{
                        functionName = 'GetPlayer'
                        childFunctionKey = 'fixture|GetPlayer|120'
                        sampledCalls = 24
                        repeatedInSampleCount = 8
                        multiCallsiteSampleCount = 8
                    }
                    @{
                        functionName = 'GetTimeSystem'
                        childFunctionKey = 'fixture|GetTimeSystem|120'
                        sampledCalls = 24
                        repeatedInSampleCount = 0
                        multiCallsiteSampleCount = 0
                    }
                    @{
                        functionName = 'GetNavigationSystem'
                        childFunctionKey = 'fixture|GetNavigationSystem|120'
                        sampledCalls = 19
                        repeatedInSampleCount = 3
                        multiCallsiteSampleCount = 2
                    }
                    @{
                        functionName = 'GetAllBlackboardDefs'
                        childFunctionKey = 'fixture|GetAllBlackboardDefs|120'
                        sampledCalls = 13
                        repeatedInSampleCount = 1
                        multiCallsiteSampleCount = 1
                    }
                    @{
                        # Deliberately no matching Game.GetWorkspotSystem() call in
                        # the callback block. This must survive as deep-only
                        # discovery evidence and remain non-generatable.
                        functionName = 'GetWorkspotSystem'
                        childFunctionKey = 'fixture-helper|GetWorkspotSystem|120'
                        sampledCalls = 31
                        repeatedInSampleCount = 7
                        multiCallsiteSampleCount = 4
                    }
                    @{
                        functionName = 'GetTotallyNewSharedThing'
                        childFunctionKey = 'fixture|GetTotallyNewSharedThing|120'
                        sampledCalls = 17
                        repeatedInSampleCount = 2
                        multiCallsiteSampleCount = 1
                    }
                )
            }
        }
        @{
            registrationId = 126
            owner = 'FixtureDiscoveryAuthorRate'
            kind = 'event'
            target = 'onUpdate'
            deep = @{
                hotCallees = @(
                    @{
                        functionName = 'GetPlayer'
                        sampledCalls = 12
                        repeatedInSampleCount = 0
                        multiCallsiteSampleCount = 0
                    }
                    @{
                        functionName = 'GetWorldPosition'
                        sampledCalls = 12
                        repeatedInSampleCount = 0
                        multiCallsiteSampleCount = 0
                    }
                )
            }
        }
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
                satisfiedAll = @('DoStructuralWork', 'StructuralFixture')
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
if (!$result.policy.genericPatternsBeforeSemanticLibrary) { throw 'Semantic library is not gated behind generic pattern resolution.' }
if (!$result.policy.exhaustiveMeasuredCallbacks) { throw 'Resolver is not marked exhaustive over measured callbacks.' }
$expectedMeasuredCallbacks = @(($handoff | ConvertFrom-Json).callbacks | Where-Object { !$_.infrastructure -and [double]$_.exclusiveMsPerSecond -gt 0 }).Count
if ([int]$result.summary.rankedCallbackCount -ne $expectedMeasuredCallbacks) {
    throw "Resolver did not inspect every measured callback: expected $expectedMeasuredCallbacks, got $($result.summary.rankedCallbackCount)."
}

if (!$result.policy.sharedProviderOpportunityAnalysis) {
    throw 'Shared-provider opportunity analysis is not enabled.'
}
if (!$result.policy.sharedProviderGenerationEnabled) {
    throw 'Source-proven shared-provider generation is not enabled.'
}
$expectedSharedProviderFamilies = @(
    'PLAYER',
    'QUESTS_SYSTEM',
    'STATS_SYSTEM',
    'TRANSACTION_SYSTEM',
    'BLACKBOARD_SYSTEM',
    'TARGETING_SYSTEM',
    'CAMERA_SYSTEM',
    'TIME_SYSTEM',
    'PREVENTION_SYSTEM',
    'SCRIPTABLE_SYSTEMS_CONTAINER',
    'ALL_BLACKBOARD_DEFS',
    'WORKSPOT_SYSTEM',
    'GAME_EFFECT_SYSTEM',
    'NAVIGATION_SYSTEM',
    'TELEPORTATION_FACILITY',
    'SYSTEM_REQUESTS_HANDLER',
    'MAPPIN_SYSTEM',
    'VEHICLE_SYSTEM',
    'AUDIO_SYSTEM',
    'UI_SYSTEM',
    'FADE_SYSTEM',
    'DYNAMIC_ENTITY_SYSTEM',
    'STAT_POOLS_SYSTEM',
    'STATUS_EFFECT_SYSTEM',
    'AI_NAVIGATION_SYSTEM',
    'JOURNAL_MANAGER'
)
foreach ($family in $expectedSharedProviderFamilies) {
    if (@($result.policy.sharedProviderGenerationFamilies) -notcontains $family) {
        throw "Stable shared-provider generation family is missing: $family"
    }
}
foreach ($dynamicFamily in @('PLAYER_POSITION','PLAYER_ORIENTATION','PLAYER_COMBAT_STATE')) {
    if (@($result.policy.sharedProviderGenerationFamilies) -contains $dynamicFamily) {
        throw "Dynamic player-derived state was incorrectly enabled for generic generation: $dynamicFamily"
    }
}
if ($result.policy.sharedProviderScope -ne 'MEASURED_CALLBACKS_ONLY') {
    throw "Shared-provider analysis escaped measured callbacks: $($result.policy.sharedProviderScope)"
}

$playerProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'PLAYER' }) |
    Select-Object -First 1
if ($null -eq $playerProvider) {
    throw 'PLAYER shared-provider opportunity was not detected.'
}
if ([int]$playerProvider.sourceOccurrences -lt 5) {
    throw "PLAYER provider did not aggregate repeated measured source reads: $($playerProvider.sourceOccurrences)"
}
if ([int]$playerProvider.callbacksWithRepeatedSourceReads -lt 1) {
    throw 'PLAYER provider did not retain same-callback duplicate-read evidence.'
}
if ([int]$playerProvider.deepObservedCallbackCount -lt 2 -or
    [int]$playerProvider.deepSampledCalls -lt 36 -or
    [int]$playerProvider.deepRepeatedSameInvocationCount -lt 8) {
    throw 'PLAYER provider did not join existing adaptive deep evidence.'
}
if ([double]$playerProvider.affectedCallbackWorkMsPerSecond -le 0) {
    throw 'PLAYER provider lost measured callback workload context.'
}
if (!$playerProvider.generationEnabled -or $playerProvider.generationRecipe -ne 'SHARED_PROVIDER_READ') {
    throw 'PLAYER shared-provider opportunity was not promoted to SHARED_PROVIDER_READ generation.'
}
if (@($playerProvider.callbacks | Where-Object { $_.substitutionEligible }).Count -lt 1) {
    throw 'PLAYER shared-provider opportunity emitted no source-proven substitution candidates.'
}

$eligibleSharedCandidates = @(
    foreach ($provider in @($result.sharedProviderOpportunities | Where-Object { $_.generationEnabled })) {
        foreach ($callback in @($provider.callbacks | Where-Object { $_.substitutionEligible })) {
            [pscustomobject]@{
                provider = [string]$provider.provider
                registrationId = [int64]$callback.registrationId
                reads = [int]$callback.sourceRecognizedOccurrences
                file = [string]$callback.sourceFile
                sha = [string]$callback.sourceSha256
                lineStart = [int]$callback.lineStart
                lineEnd = [int]$callback.lineEnd
            }
        }
    }
)
$expectedReadyCallbacks = @($eligibleSharedCandidates.registrationId | Sort-Object -Unique).Count
$expectedReadyReads = ($eligibleSharedCandidates | Measure-Object -Property reads -Sum).Sum
$expectedReadyFamilies = @($eligibleSharedCandidates.provider | Sort-Object -Unique)
if ([int]$result.summary.sharedProviderReadyCallbacks -ne $expectedReadyCallbacks) {
    throw "Resolver summary lost shared-provider-ready callback count: expected $expectedReadyCallbacks, got $($result.summary.sharedProviderReadyCallbacks)."
}
if ([int]$result.summary.sharedProviderReadyReads -ne [int]$expectedReadyReads) {
    throw "Resolver summary lost shared-provider-ready read count: expected $expectedReadyReads, got $($result.summary.sharedProviderReadyReads)."
}
if (@($result.summary.sharedProviderReadyFamilies).Count -ne $expectedReadyFamilies.Count) {
    throw 'Resolver summary lost shared-provider-ready family count.'
}
foreach ($family in $expectedReadyFamilies) {
    if (@($result.summary.sharedProviderReadyFamilies) -notcontains $family) {
        throw "Resolver summary lost ready shared-provider family: $family"
    }
}
if (@($playerProvider.owners) -contains 'FixtureColdProvider') {
    throw 'Shared-provider analysis scanned an unmeasured/cold mod into the opportunity set.'
}

$timeProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'TIME_SYSTEM' }) |
    Select-Object -First 1
if ($null -eq $timeProvider) {
    throw 'TIME_SYSTEM shared-provider opportunity was not detected.'
}
if ([int]$timeProvider.deepObservedCallbackCount -lt 1 -or
    [int]$timeProvider.deepSampledCalls -lt 24) {
    throw 'Shared-provider deep evidence is still gated by the per-callback hotCallees shortlist.'
}
if (!$timeProvider.generationEnabled -or $timeProvider.generationRecipe -ne 'SHARED_PROVIDER_READ') {
    throw 'TIME_SYSTEM was not promoted to source-proven shared-provider generation.'
}

$navigationProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'NAVIGATION_SYSTEM' }) |
    Select-Object -First 1
if ($null -eq $navigationProvider) {
    throw 'Arbitrary GetNavigationSystem discovery candidate was not surfaced.'
}
if ([int]$navigationProvider.sourceRecognizedOccurrences -ne 1 -or
    [int]$navigationProvider.sourceUnresolvedOccurrences -ne 0 -or
    [int]$navigationProvider.deepSampledCalls -ne 19) {
    throw 'NAVIGATION_SYSTEM did not preserve exact source/deep discovery evidence.'
}
if ($navigationProvider.analysisOnly -or !$navigationProvider.generationEnabled -or
    $navigationProvider.generationRecipe -ne 'SHARED_PROVIDER_READ') {
    throw 'NAVIGATION_SYSTEM was not promoted to source-proven shared-provider generation.'
}
if ($navigationProvider.category -ne 'SYSTEM_HANDLE') {
    throw "Unexpected NAVIGATION_SYSTEM category: $($navigationProvider.category)"
}

$blackboardDefsProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'ALL_BLACKBOARD_DEFS' }) |
    Select-Object -First 1
if ($null -eq $blackboardDefsProvider) {
    throw 'GetAllBlackboardDefs discovery candidate was not surfaced.'
}
if ([int]$blackboardDefsProvider.sourceRecognizedOccurrences -ne 1 -or
    [int]$blackboardDefsProvider.deepSampledCalls -ne 13) {
    throw 'ALL_BLACKBOARD_DEFS did not preserve exact source/deep discovery evidence.'
}
if ($blackboardDefsProvider.analysisOnly -or !$blackboardDefsProvider.generationEnabled -or
    $blackboardDefsProvider.generationRecipe -ne 'SHARED_PROVIDER_READ') {
    throw 'ALL_BLACKBOARD_DEFS was not promoted to source-proven shared-provider generation.'
}
if ($blackboardDefsProvider.category -ne 'SHARED_LOOKUP') {
    throw "Unexpected ALL_BLACKBOARD_DEFS category: $($blackboardDefsProvider.category)"
}

$futureProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'TOTALLY_NEW_SHARED_THING' }) |
    Select-Object -First 1
if ($null -eq $futureProvider) {
    throw 'Arbitrary zero-argument Game.Get...() provider was hidden by a discovery shortlist.'
}
if ([int]$futureProvider.sourceRecognizedOccurrences -ne 1 -or
    [int]$futureProvider.sourceUnresolvedOccurrences -ne 0 -or
    [int]$futureProvider.deepSampledCalls -ne 17) {
    throw 'Arbitrary getter did not preserve exact source/deep evidence.'
}
if (!$futureProvider.analysisOnly -or $futureProvider.generationEnabled) {
    throw 'Unknown arbitrary getter was incorrectly promoted to generation.'
}
if ($futureProvider.category -ne 'GAME_GETTER_CANDIDATE') {
    throw "Unexpected arbitrary getter category: $($futureProvider.category)"
}

$workspotProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'WORKSPOT_SYSTEM' }) |
    Select-Object -First 1
if ($null -eq $workspotProvider) {
    throw 'Deep-only GetWorkspotSystem discovery evidence was dropped.'
}
if ([int]$workspotProvider.sourceOccurrences -ne 0 -or
    [int]$workspotProvider.deepSampledCalls -ne 31 -or
    [int]$workspotProvider.deepObservedCallbackCount -ne 1) {
    throw 'WORKSPOT_SYSTEM deep-only evidence was not preserved correctly.'
}
if (!$workspotProvider.analysisOnly -or $workspotProvider.generationEnabled) {
    throw 'Deep-only WORKSPOT_SYSTEM was incorrectly marked generatable.'
}
$workspotCallback = @($workspotProvider.callbacks) | Select-Object -First 1
if ($null -eq $workspotCallback -or !$workspotCallback.deepOnly -or $workspotCallback.substitutionEligible) {
    throw 'WORKSPOT_SYSTEM callback did not expose deepOnly/non-eligible state.'
}

$positionProvider = @($result.sharedProviderOpportunities | Where-Object { $_.provider -eq 'PLAYER_POSITION' }) |
    Select-Object -First 1
if ($null -eq $positionProvider) {
    throw 'PLAYER_POSITION shared-provider opportunity was not detected.'
}
if ([int]$positionProvider.sourceRecognizedOccurrences -lt 1) {
    throw 'PLAYER_POSITION did not prove a local receiver sourced from Game.GetPlayer().'
}
if ([int]$positionProvider.deepObservedCallbackCount -lt 1) {
    throw 'PLAYER_POSITION did not join its existing deep call evidence.'
}

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

$namedCName = Action-For 'FixtureNamedCName'
if (!$namedCName.generic.Automatable) {
    throw 'n"ActionName" CName literal selector was not resolved.'
}
if (@($namedCName.generic.Facts.actions) -notcontains 'ToggleSprint') {
    throw 'n"ActionName" literal was not emitted as a finite action.'
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

$wrappedAction = Action-For 'FixtureWrappedAction'
if ($wrappedAction.generic.Automatable) {
    throw 'Member-wrapped Event.Observe was incorrectly authorized for generic action routing.'
}
if ((@($wrappedAction.generic.Blockers) -join ' ') -notmatch 'wrapper/alias') {
    throw 'Wrapped Observe rejection did not explain the wrapper/alias safety boundary.'
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

$wrappedOverride = Action-For 'FixtureWrappedOverride'
if ($wrappedOverride.generic.Automatable) {
    throw 'Member-wrapped Event.Override was incorrectly authorized for generic Override rewriting.'
}
if ((@($wrappedOverride.generic.Blockers) -join ' ') -notmatch 'wrapper/alias') {
    throw 'Wrapped Override rejection did not explain the wrapper/alias safety boundary.'
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

$onDraw = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONDRAW' }) | Select-Object -First 1
if ($null -eq $onDraw) { throw 'onDraw callback family was not resolved.' }
$draw = @($onDraw.topConsumers | Where-Object { $_.owner -eq 'FixtureDraw' }) | Select-Object -First 1
if ($null -eq $draw) { throw 'FixtureDraw was not ranked inside onDraw.' }
if (!$draw.generic.Automatable -or
    @($draw.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Raw onDraw registration was not authorized for frame dispatch consolidation.'
}

$interactionDraw = @($onDraw.topConsumers | Where-Object { $_.owner -eq 'FixtureInteractionUi' }) | Select-Object -First 1
if ($null -eq $interactionDraw) { throw 'FixtureInteractionUi was not ranked inside onDraw.' }
if (!$interactionDraw.generic.Automatable -or
    @($interactionDraw.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION' -or
    @($interactionDraw.generic.RecipeFamilies) -notcontains 'INTERACTION_UI_IDLE_GUARD') {
    throw 'Safe interaction UI shape did not compose frame consolidation with the generic idle guard.'
}
if ($interactionDraw.generic.Facts.interactionUiFunction -ne 'ui.update' -or
    $interactionDraw.generic.Facts.interactionUiGate -ne 'ui.hubShown' -or
    $interactionDraw.generic.Facts.interactionUiIdleReset -ne 'ui.input = false' -or
    $interactionDraw.generic.Facts.interactionUiHubVariable -ne 'hubs') {
    throw 'Interaction UI proof facts were not emitted exactly.'
}

$interactionUnsafe = @($onDraw.topConsumers | Where-Object { $_.owner -eq 'FixtureInteractionUiUnsafe' }) | Select-Object -First 1
if ($null -eq $interactionUnsafe) { throw 'FixtureInteractionUiUnsafe was not ranked inside onDraw.' }
if (@($interactionUnsafe.generic.RecipeFamilies) -contains 'INTERACTION_UI_IDLE_GUARD') {
    throw 'Interaction UI idle guard ignored unconditional work outside the hubShown branch.'
}
$alreadyDraw = @($onDraw.topConsumers | Where-Object { $_.owner -eq 'FixtureAlreadyDraw' }) | Select-Object -First 1
if ($null -eq $alreadyDraw) { throw 'FixtureAlreadyDraw was not ranked inside onDraw.' }
if ($alreadyDraw.generic.Automatable -or $alreadyDraw.generic.Status -ne 'ALREADY_SATISFIED') {
    throw 'Existing owner-specific onDraw MakeEventRegistrar integration was not recognized.'
}

$onUpdate = @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'ONUPDATE' }) | Select-Object -First 1
if ($null -eq $onUpdate) { throw 'onUpdate callback family was not resolved.' }
$frame = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureFrame' }) | Select-Object -First 1
if ($null -eq $frame) { throw 'FixtureFrame was not ranked inside onUpdate.' }
if (!$frame.generic.Automatable) { throw 'Direct onUpdate registration was not marked automatable.' }
if (@($frame.generic.RecipeFamilies) -notcontains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Frame dispatch consolidation recipe was not exposed.'
}
if ($frame.generic.optimizationScope -ne 'REGISTRATION_DISPATCH_ONLY' -or
    !$frame.generic.materialBodyResidualAfterFrameDispatch) {
    throw 'Frame-only AUTO did not report its material callback-body residual.'
}
if ([int]$result.summary.frameDispatchOnlyMaterialResidual -lt 1 -or
    [double]$result.summary.frameDispatchOnlyMaterialResidualMsPerSecond -lt 5.0) {
    throw 'Resolver summary hid material work behind frame-dispatch-only AUTO.'
}
if ($frame.source.MatchMode -ne 'profiler-owner-relative') {
    throw 'onUpdate bare init.lua did not use owner-relative source mapping.'
}

if (!$frame.existingOptimization.zeroEngineSchedulerDetected -or
    !$frame.existingOptimization.sourceProven) {
    throw 'Captured 0-Engine Scheduler work present in live source was not recognized.'
}
if (!$frame.existingOptimization.residualNativeOnUpdate) {
    throw 'Scheduler-integrated raw onUpdate was not identified as a residual native callback.'
}
if (@($frame.existingOptimization.jobs | Where-Object { $_.job -eq 'fixture.frame.scheduled' }).Count -ne 1) {
    throw 'Resolver lost the source-proven Scheduler job identity.'
}
if (@($frame.existingOptimization.capturedSchedulerOwners) -notcontains 'ManualAlias') {
    throw 'Resolver incorrectly required Scheduler owner text to equal the CET mod folder.'
}

$wrappedFrame = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureWrappedFrame' }) | Select-Object -First 1
if ($null -eq $wrappedFrame) { throw 'FixtureWrappedFrame was not ranked inside onUpdate.' }
if ($wrappedFrame.generic.Automatable) {
    throw 'Member-wrapped Event.registerForEvent was incorrectly authorized for frame consolidation.'
}
if (@($wrappedFrame.generic.RecipeFamilies) -contains 'FRAME_DISPATCH_CONSOLIDATION') {
    throw 'Wrapped frame registrar leaked FRAME_DISPATCH_CONSOLIDATION.'
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

$ownerRegistrar = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureOwnerRegistrar' }) | Select-Object -First 1
if ($null -eq $ownerRegistrar) { throw 'FixtureOwnerRegistrar was not ranked inside onUpdate.' }
if ($ownerRegistrar.generic.Automatable -or $ownerRegistrar.generic.Status -ne 'ALREADY_SATISFIED') {
    throw 'Owner-specific MakeEventRegistrar integration was not recognized as already satisfied.'
}
if ((@($ownerRegistrar.generic.Evidence) -join ' ') -notmatch 'MakeEventRegistrar') {
    throw 'Owner-specific registrar recognition did not expose its source evidence.'
}

$schedulerBootstrap = @($onUpdate.topConsumers | Where-Object { $_.owner -eq 'FixtureSchedulerBootstrap' }) | Select-Object -First 1
if ($null -eq $schedulerBootstrap) { throw 'FixtureSchedulerBootstrap was not ranked inside onUpdate.' }
if (!$schedulerBootstrap.existingOptimization.zeroEngineSchedulerDetected -or
    !$schedulerBootstrap.existingOptimization.sourceProven) {
    throw 'Scheduler bootstrap fixture did not prove its existing Scheduler ownership.'
}
if ($schedulerBootstrap.generic.Automatable -or $schedulerBootstrap.generic.Status -ne 'ALREADY_SATISFIED') {
    throw 'Residual Scheduler bootstrap was incorrectly re-authorized for frame consolidation.'
}
if ((@($schedulerBootstrap.generic.Evidence) -join ' ') -notmatch 'residual bootstrap') {
    throw 'Residual Scheduler bootstrap recognition did not expose its source evidence.'
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

if (!$structural.semantic.AlreadySatisfied -or
    !$structural.semantic.SatisfiedBySourcePostcondition -or
    $structural.semantic.AlreadySatisfiedMode -ne 'SOURCE_POSTCONDITION') {
    $semanticDebug = $structural.semantic | ConvertTo-Json -Depth 10 -Compress
    throw "Equivalent manual semantic result was not recognized from source postconditions: $semanticDebug"
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

$unknownHot = $null
foreach ($otherFamily in @($result.callbackFamilies | Where-Object { $_.resolverFamily -eq 'OTHER' })) {
    $candidateHot = @($otherFamily.topConsumers | Where-Object { $_.owner -eq 'FixtureUnknownHot' }) | Select-Object -First 1
    if ($null -ne $candidateHot) { $unknownHot = $candidateHot; break }
}
if ($null -eq $unknownHot) { throw 'FixtureUnknownHot was not ranked.' }
if ($unknownHot.generic.Automatable) {
    throw 'High-cost unknown callback was incorrectly promoted to generic AUTO.'
}

if ($null -eq $resolved.pass) {
    throw 'CLI --generate-pass did not return a pass result.'
}
$expectedGenericTransforms = 0
foreach ($family in @($result.callbackFamilies)) {
    foreach ($consumer in @($family.topConsumers | Where-Object { $_.generic.Automatable })) {
        $expectedGenericTransforms += @($consumer.generic.RecipeFamilies).Count
    }
}
$expectedSharedTransforms = @(
    $eligibleSharedCandidates |
        ForEach-Object {
            "$($_.provider)|$($_.file)|$($_.sha)|$($_.lineStart)|$($_.lineEnd)"
        } |
        Sort-Object -Unique
).Count
$expectedPassTransforms = $expectedGenericTransforms + $expectedSharedTransforms
if ([int]$resolved.pass.TransformCount -ne $expectedPassTransforms) {
    if (Test-Path -LiteralPath $resolved.pass.ManifestPath -PathType Leaf) {
        $failedManifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
        Write-Host "PASS DEBUG transforms=$($failedManifest.summary.transforms) skipped=$($failedManifest.summary.skipped)"
        foreach ($skip in @($failedManifest.skipped)) {
            Write-Host ("PASS DEBUG SKIP owner={0} type={1} file={2} reason={3}" -f $skip.owner,$skip.type,$skip.file,$skip.reason)
        }
    }
    throw "Expected $expectedPassTransforms generated transforms ($expectedGenericTransforms generic callbacks + $expectedSharedTransforms shared-provider candidates), got $($resolved.pass.TransformCount)."
}
if ([int]$resolved.pass.FileCount -lt 5) {
    throw "Generated replacement file count is implausibly small: $($resolved.pass.FileCount)."
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
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDormantDiscovery/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOtherStructural/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOverrideStructural/init.lua',
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
    if ('bin/x64/plugins/cyber_engine_tweaks/mods/FixtureColdProvider/init.lua' -in $names) {
        throw 'Shared-provider generation escaped the measured callback set.'
    }
    foreach ($parkedEntry in @(
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureAlreadyFrame/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureWrappedAction/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureWrappedOverride/init.lua',
        'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureWrappedFrame/init.lua'
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

    function Count-SharedPlayerReads([string]$Text) {
        $allCalls = [regex]::Matches($Text, [regex]::Escape('__gcetGetPlayer()')).Count
        $helperDeclarations = [regex]::Matches(
            $Text,
            [regex]::Escape('local function __gcetGetPlayer()')).Count
        return $allCalls - $helperDeclarations
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

    $interactionText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureInteractionUi/init.lua'
    foreach ($requiredInteractionText in @(
        'G-CET interaction UI idle guard',
        'if not ui.hubShown then',
        'ui.input = false',
        'return',
        'local hubs = getDialogChoiceHubs()',
        '__gcetRegisterEvent_142("onDraw"'
    )) {
        if ($interactionText -notmatch [regex]::Escape($requiredInteractionText)) {
            throw "Generated interaction UI generic guard is missing: $requiredInteractionText"
        }
    }
    if ($interactionText.IndexOf('if not ui.hubShown then') -gt $interactionText.IndexOf('local hubs = getDialogChoiceHubs()')) {
        throw 'Interaction UI idle guard was inserted after the expensive hub read.'
    }
    if ([regex]::Matches($interactionText, [regex]::Escape('getDialogChoiceHubs()')).Count -ne 1) {
        throw 'Interaction UI idle guard duplicated or removed the original hub read.'
    }

    $interactionUnsafeText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureInteractionUiUnsafe/init.lua'
    if ($interactionUnsafeText -match 'G-CET interaction UI idle guard') {
        throw 'Unsafe interaction UI shape received the generic hidden-state rewrite.'
    }

    $structuralText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureStructural/init.lua'
    if ($structuralText -notmatch '__gcetRegisterEvent_120\s*\(\s*"onUpdate"') {
        throw 'Structural fixture lost safe frame-dispatch consolidation.'
    }
    if ($structuralText -match '__gcetReuse_120_' -or $structuralText -match '__gcetStatic_120_') {
        throw 'Structural hotpath rewrite leaked into the safe generic pass.'
    }
    $playerMarkerOk = $structuralText -match [regex]::Escape('-- G-CET shared provider: PLAYER')
    $playerApiOk = $structuralText -match [regex]::Escape('__gcetApi.GetPlayer')
    $playerFallbackOk = $structuralText -match [regex]::Escape('return Game.GetPlayer()')
    $playerReadCount = Count-SharedPlayerReads $structuralText
    $structuralCNameCount = [regex]::Matches($structuralText, [regex]::Escape('CName.new("StructuralFixture")')).Count
    if (!$playerMarkerOk -or !$playerApiOk -or !$playerFallbackOk -or
        $playerReadCount -ne 2 -or $structuralCNameCount -ne 2) {
        throw "PLAYER shared-provider substitution/fallback/isolation incomplete: marker=$playerMarkerOk api=$playerApiOk fallback=$playerFallbackOk reads=$playerReadCount cnames=$structuralCNameCount"
    }

    foreach ($sharedProviderCheck in @(
        @{ marker = '-- G-CET shared provider: NAVIGATION_SYSTEM'; helper = '__gcetGetNavigationSystem()'; fallback = 'return Game.GetNavigationSystem()' },
        @{ marker = '-- G-CET shared provider: ALL_BLACKBOARD_DEFS'; helper = '__gcetGetAllBlackboardDefs()'; fallback = 'return Game.GetAllBlackboardDefs()' }
    )) {
        if ($structuralText -notmatch [regex]::Escape($sharedProviderCheck.marker) -or
            $structuralText -notmatch [regex]::Escape($sharedProviderCheck.helper) -or
            $structuralText -notmatch [regex]::Escape($sharedProviderCheck.fallback)) {
            throw "Expanded shared-provider substitution is incomplete: $($sharedProviderCheck.marker)"
        }
    }
    if ($structuralText -notmatch [regex]::Escape('Game.GetTotallyNewSharedThing()')) {
        throw 'Unknown arbitrary getter was incorrectly rewritten.'
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
    $originalGetterIndex = $hardUpdateText.IndexOf('local player = __gcetGetPlayer()')
    $originalGuardIndex = $hardUpdateText.IndexOf('if not active then return end')
    if ($originalGetterIndex -lt 0 -or $originalGuardIndex -lt 0 -or $originalGetterIndex -gt $originalGuardIndex) {
        throw 'Shared-provider AUTO changed the dormant callback execution order.'
    }

    $dormantDiscoveryText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureDormantDiscovery/init.lua'
    if ((Count-SharedPlayerReads $dormantDiscoveryText) -ne 1 -or
        $dormantDiscoveryText -match 'G-CET dormant guard hoist' -or
        $dormantDiscoveryText -match 'Schedule\.Every') {
        throw 'PLAYER provider-only pass changed discovery/dormancy semantics.'
    }

    $otherStructuralText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOtherStructural/init.lua'
    if ((Count-SharedPlayerReads $otherStructuralText) -ne 2 -or
        $otherStructuralText -match '__gcetReuse_129_' -or
        $otherStructuralText -match '__gcetStatic_129_') {
        throw 'PLAYER provider-only pass leaked structural rewrites into FixtureOtherStructural.'
    }

    $overrideStructuralText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureOverrideStructural/init.lua'
    if ((Count-SharedPlayerReads $overrideStructuralText) -ne 2 -or
        $overrideStructuralText -match 'SubscribeAction' -or
        $overrideStructuralText -match '__gcetOverrideActions_130') {
        throw 'PLAYER provider-only pass changed Override semantics.'
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
    foreach ($family in $expectedSharedProviderFamilies) {
        if (@($manifest.policy.sharedProviderFamilies) -notcontains $family) {
            throw "Generated pass manifest does not advertise stable shared-provider generation: $family"
        }
    }
    if (@($manifest.policy.supportedPasses) -notcontains 'SHARED_PROVIDER_READ') {
        throw 'Generated pass manifest does not advertise SHARED_PROVIDER_READ generation.'
    }
    if (@($manifest.policy.supportedPasses) -notcontains 'INTERACTION_UI_IDLE_GUARD') {
        throw 'Generated pass manifest does not advertise INTERACTION_UI_IDLE_GUARD generation.'
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
    if ([int]$manifest.summary.transforms -ne $expectedPassTransforms) {
        throw 'Generated pass manifest transform count is wrong.'
    }
    if ([int]$manifest.summary.genericTransforms -ne $expectedPassTransforms -or [int]$manifest.summary.semanticTransforms -ne 0) {
        throw 'Shared-provider transforms were not composed into generic transform accounting.'
    }
    if ([int]$manifest.summary.callbackFiles -ne ([int]$resolved.pass.FileCount - [int]$manifest.summary.fixedRuntimeFiles)) {
        throw 'Generated pass callback-file accounting is inconsistent.'
    }
    if ([int]$manifest.summary.fixedRuntimeFiles -ne 4) {
        throw "Expected 4 fixed 0-Engine runtime files, got $($manifest.summary.fixedRuntimeFiles)."
    }
    if (!$manifest.fixedRuntime.included -or !$manifest.fixedRuntime.exception) {
        throw 'Generated pass did not mark 0-Engine as the fixed runtime exception.'
    }
    if ($manifest.fixedRuntime.FixedVersion -ne '0.18.13-EXPANDED-SHARED-PROVIDERS') {
        throw "Unexpected fixed 0-Engine version: $($manifest.fixedRuntime.FixedVersion)"
    }

    $zeroText = Read-ZipText 'bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua'
    foreach ($requiredRuntimeSymbol in @(
        'function Engine.MakeEventRegistrar',
        'function Engine.SubscribeAction',
        'function Engine.GetPlayer',
        'function Engine.GetBlackboardSystem',
        'function Engine.GetTimeSystem',
        'function Engine.GetStatsSystem',
        'function Engine.GetQuestsSystem',
        'function Engine.GetTargetingSystem',
        'function Engine.GetTransactionSystem',
        'function Engine.GetScriptableSystemsContainer',
        'function Engine.GetAllBlackboardDefs',
        'function Engine.GetWorkspotSystem',
        'function Engine.GetNavigationSystem',
        'function Engine.GetTeleportationFacility',
        'function Engine.GetSystemRequestsHandler',
        'function Engine.GetVehicleSystem',
        'function Engine.GetAudioSystem',
        'function Engine.GetUISystem',
        'function Engine.GetStatPoolsSystem',
        'function Engine.GetJournalManager',
        '-- G-CET shared providers v2',
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


# Structurally compatible foreign 0-Engine contract.
# Unknown versions keep their own runtime and receive only the additive
# Engine.GCET bridge + private G-CET ActionRouter support module.
$foreignRoot = Join-Path $root 'foreign-game'
$foreignPlugins = Join-Path $foreignRoot 'bin\x64\plugins'
$foreignMods = Join-Path $foreignPlugins 'cyber_engine_tweaks\mods'
New-Item -ItemType Directory -Force $foreignMods | Out-Null
Copy-Item -Path (Join-Path $mods '*') -Destination $foreignMods -Recurse -Force

$foreignInit = Join-Path $foreignMods '0-Engine\init.lua'
@'
local engineVersion = "99.4-custom"
local Engine = {}

function Engine.GetVersion()
    return engineVersion
end

function Engine.Register(name)
    return { name = name }
end

function Engine.RegisterZone(config)
    return "foreign-zone-api"
end

function Engine.RegisterSpatialSet(config)
    return "foreign-spatial-api"
end

function Engine.SetTimeout(seconds, fn)
    return "foreign-timeout-api"
end

return Engine
'@ | Set-Content -LiteralPath $foreignInit -Encoding utf8

$foreignResolved = (& $resolverExe --capture $capture --mods $foreignMods --generate-pass --json | ConvertFrom-Json)
if (!$foreignResolved.ok -or $null -eq $foreignResolved.pass) {
    throw 'Resolver rejected a structurally compatible foreign 0-Engine.'
}

$foreignManifest = Get-Content -LiteralPath $foreignResolved.pass.ManifestPath -Raw | ConvertFrom-Json
if ([string]$foreignManifest.fixedRuntime.LiveState -ne 'STRUCTURAL_COMPAT') {
    throw "Foreign 0-Engine was not admitted through STRUCTURAL_COMPAT: $($foreignManifest.fixedRuntime.LiveState)"
}
if ([string]$foreignManifest.fixedRuntime.FixedVersion -ne 'HOST-COMPAT-v1') {
    throw "Foreign 0-Engine did not use the host-preserving compatibility runtime: $($foreignManifest.fixedRuntime.FixedVersion)"
}
if ([string]$foreignManifest.fixedRuntime.mode -ne 'HOST_PRESERVING_ADAPTER' -or !$foreignManifest.fixedRuntime.hostPreserving) {
    throw 'Foreign 0-Engine manifest did not report host-preserving adapter mode.'
}
if ([int]$foreignManifest.summary.fixedRuntimeFiles -ne 2) {
    throw "Foreign 0-Engine compatibility should ship exactly init.lua + private ActionRouter, got $($foreignManifest.summary.fixedRuntimeFiles)."
}

$foreignZip = [System.IO.Compression.ZipFile]::OpenRead([string]$foreignResolved.pass.ZipPath)
try {
    function Read-ForeignZipText([string]$EntryName) {
        $entry = $foreignZip.GetEntry($EntryName)
        if ($null -eq $entry) { throw "Foreign ZIP entry not found: $EntryName" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $base = 'bin/x64/plugins/cyber_engine_tweaks/mods/'
    $foreignZeroText = Read-ForeignZipText ($base + '0-Engine/init.lua')
    foreach ($required in @(
        '99.4-custom',
        'function Engine.RegisterZone',
        'foreign-zone-api',
        'function Engine.RegisterSpatialSet',
        'foreign-spatial-api',
        'function Engine.SetTimeout',
        'foreign-timeout-api',
        '-- G-CET host compatibility bridge v1',
        '__gcetHost.GCET',
        'function __gcetApi.MakeEventRegistrar',
        'function __gcetApi.SubscribeAction',
        'modules/G-CET/ActionRouter'
    )) {
        if ($foreignZeroText -notmatch [regex]::Escape($required)) {
            throw "Host-preserving 0-Engine adapter lost required source/API: $required"
        }
    }

    if ($null -eq $foreignZip.GetEntry($base + '0-Engine/modules/G-CET/ActionRouter.lua')) {
        throw 'Host-preserving 0-Engine adapter did not ship its private ActionRouter.'
    }
    foreach ($forbidden in @(
        '0-Engine/modules/ActionRouter.lua',
        '0-Engine/modules/Health.lua',
        '0-Engine/modules/Scheduler.lua'
    )) {
        if ($null -ne $foreignZip.GetEntry($base + $forbidden)) {
            throw "Host-preserving compatibility incorrectly overwrote host module: $forbidden"
        }
    }

    $foreignActionText = Read-ForeignZipText ($base + 'FixtureAction/init.lua')
    if ($foreignActionText -notmatch '\.GCET' -or
        $foreignActionText -notmatch '__gcetApi_\d+\.SubscribeAction') {
        throw 'Generated action routing does not prefer the namespaced Engine.GCET API.'
    }

    $foreignFrameText = Read-ForeignZipText ($base + 'FixtureStructural/init.lua')
    if ($foreignFrameText -notmatch 'type\(__gcetEngine\.GCET\) == "table"' -or
        $foreignFrameText -notmatch '__gcetApi\.MakeEventRegistrar') {
        throw 'Generated frame routing does not prefer the namespaced Engine.GCET API.'
    }
}
finally {
    $foreignZip.Dispose()
}

Write-Host 'Foreign 0-Engine structural compatibility contract passed.'

# Profiler-managed foreign 0-Engine contract.
# Resolver must analyze the exact pre-profiler backup, not the temporary live
# bridge, and still use the structural compatibility path.
$profiledRoot = Join-Path $root 'profiled-game'
$profiledPlugins = Join-Path $profiledRoot 'bin\x64\plugins'
$profiledMods = Join-Path $profiledPlugins 'cyber_engine_tweaks\mods'
$profiledState = Join-Path $profiledPlugins '.cet_runtime_profiler'
New-Item -ItemType Directory -Force $profiledMods,$profiledState | Out-Null
Copy-Item -Path (Join-Path $foreignMods '*') -Destination $profiledMods -Recurse -Force

$copiedInit = Join-Path $profiledMods '0-Engine\init.lua'
$originalInit = [System.IO.File]::ReadAllBytes($copiedInit)
[System.IO.File]::WriteAllBytes(
    (Join-Path $profiledState '0-Engine.init.ORIGINAL.lua'),
    $originalInit)

$bridgeText = [System.Text.Encoding]::UTF8.GetString($originalInit)
$bridgeText += [Environment]::NewLine + '-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2' + [Environment]::NewLine
$bridgeText += '-- test-only profiler bridge shell' + [Environment]::NewLine
$bridgeText += '-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_END v2' + [Environment]::NewLine
[System.IO.File]::WriteAllText(
    $copiedInit,
    $bridgeText,
    [System.Text.UTF8Encoding]::new($false))

$profiledResolved = (& $resolverExe --capture $capture --mods $profiledMods --generate-pass --json | ConvertFrom-Json)
if (!$profiledResolved.ok -or $null -eq $profiledResolved.pass) {
    throw 'Resolver rejected a profiler-managed foreign 0-Engine even though its original backup is structurally compatible.'
}

$profiledManifest = Get-Content -LiteralPath $profiledResolved.pass.ManifestPath -Raw | ConvertFrom-Json
if ([string]$profiledManifest.fixedRuntime.LiveState -ne 'PROFILER_MANAGED_STRUCTURAL_COMPAT') {
    throw "Profiler-managed foreign 0-Engine did not use its backed-up structural source: $($profiledManifest.fixedRuntime.LiveState)"
}

# A profiler marker is not enough: an original that cannot prove a simple
# exported runtime table must still be rejected.
[System.IO.File]::WriteAllText(
    (Join-Path $profiledState '0-Engine.init.ORIGINAL.lua'),
    ('local internal = {}' + [Environment]::NewLine + 'return function() return internal end' + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

$unsupportedAccepted = $false
try {
    $unsupportedResult = (& $resolverExe --capture $capture --mods $profiledMods --generate-pass --json 2>$null | ConvertFrom-Json)
    if ($LASTEXITCODE -eq 0 -and $unsupportedResult.ok) { $unsupportedAccepted = $true }
} catch {
    $unsupportedAccepted = $false
}
if ($unsupportedAccepted) {
    throw 'Profiler bridge marker incorrectly authorized a non-structural 0-Engine backup.'
}
$global:LASTEXITCODE = 0

Write-Host 'Profiler-managed foreign 0-Engine contract passed.'

Write-Host 'Callback-first resolver + V1 pass generator contract passed.'
