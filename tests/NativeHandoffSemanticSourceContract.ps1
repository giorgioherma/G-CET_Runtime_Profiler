param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$dll = Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'
if (!(Test-Path $dll)) { throw 'Missing G.CETProfiler.Core.dll' }
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$type = $assembly.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
function Rewrite([string]$name, [string]$source) {
    $method = $type.GetMethod($name, $flags)
    if (!$method) {throw "Method missing: $name"}
    try { return [string]$method.Invoke($null, [object[]]@($source)) }
    catch { throw "$name source rejected: $($_.Exception.InnerException.Message)" }
}
$ifp = @'
local API = {}
local function setSessionLoaded(loaded, reason)
    isLoaded = loaded
    if loaded then
        isPaused = false
        bindPlayer(Game.GetPlayer())
        refreshInputSettings()
        RuntimeHeight.MarkDirty()
    else
        isPaused = false
        local player = Helpers.GetPlayer()
        if initialized then RuntimeHeight.Shutdown(player) end
        CameraCore.Suspend(reason)
        Helpers.DetachPlayer()
    end
end

local function setSessionPaused(paused, reason)
    isPaused = paused == true
    if isPaused then
        CameraCore.Pause(reason)
    else
        refreshInputSettings()
        RuntimeHeight.MarkDirty()
    end
end

function API.Enable() end
function API.Disable()
    isDisabledByApi = true
    isEnabled = false
    RuntimeHeight.Shutdown(Helpers.GetPlayer())
    CameraCore.Suspend("disabled by API")
end
local function registerPlayerInput(cetVersion)
    local function registerInputListeners(player)
        player:RegisterInputListener(player, "CameraMouseX")
        player:RegisterInputListener(player, "CameraMouseY")
        player:RegisterInputListener(player, "CameraX")
        player:RegisterInputListener(player, "CameraY")
        player:RegisterInputListener(player, "right_stick_x")
        player:RegisterInputListener(player, "right_stick_y")
        player:RegisterInputListener(player, "mouse_left")
    end
    Observe('PlayerPuppet', 'OnDetach', function(player)
        CameraCore.Suspend("player detached")
    end)
    Observe('PlayerPuppet', 'OnAction', function(first, second, third)
        if not isLoaded or not CameraCore.IsFreeLooking() then return end
        local action = cetVersion >= 1.14 and second or first
        local consumer = cetVersion >= 1.14 and third or second
        if not action then return end
        local listenerAction = GetSingleton('gameinputScriptListenerAction')
        local ok, actionName, actionValue = pcall(readListenerAction, listenerAction, action)
        if not ok then return end
        if actionName == "mouse_left" and actionValue > 0 then
            CameraCore.AbortFreeLook()
            return
        end
        CameraCore.OnAction(actionName, actionValue)
        if freeLookCameraActions[actionName] and consumer then
            local consumed = pcall(consumeSingleAction, consumer)
        end
    end)
    -- Reload All Mods can happen after OnGameAttached.
    registerInputListeners(Game.GetPlayer())
end
registerForEvent("onUpdate", function(delta)
        if context then
            CameraCore.Update(delta, context)
        end
end)
registerForEvent("onShutdown", function()
        CameraCore.Suspend("CET shutdown")
end)
registerForEvent("onDraw", function()
        if isEnabled then
            CameraCore.Suspend("disabled in overlay")
        end
        if not isEnabled then
                CameraCore.AbortFreeLook()
        end
end)
registerHotkey('ifp_toggle_enabled','Toggle',function()
        if not isEnabled then
            CameraCore.Suspend("disabled by hotkey")
        end
end)
registerInput("ifp_freelook", "FreeLook", function(keydown)
        if keydown then
            if context and context.freeEligible then
                CameraCore.BeginFreeLook(context)
            end
        else
            CameraCore.EndFreeLook(false)
        end
end)
'@
$out = Rewrite 'ExtendImmersiveFirstPersonActionDormancy' $ifp
foreach ($needle in @('-- __gcetIfpActionDormancyV2',
    '__gcetIfpActionHandle = zeroEngineMod.SubscribeAction',
    '__gcetIfpActionHandle = zeroEngine.SubscribeAction',
    'pcall(handle.SetActive, needed)',
    '__gcetIfpSyncActionDormancy()',
    "Observe('PlayerPuppet', 'OnAction', function(first, second, third)")) {
    if (!$out.Contains($needle)) {throw "IFP C-stage missing: $needle"}
}
if ((Rewrite 'ExtendImmersiveFirstPersonActionDormancy' $out) -cne $out) {
    throw 'IFP native subscriber stage not idempotent'
}
if ([regex]::Matches($out,"Observe\('PlayerPuppet', 'OnAction'").Count -ne 1) {
    throw 'IFP native fallback duplicated'
}
$loot = @'
local autoLoot = {settings={useDefaultActionKey=true},ui={onInit=function()end}}
local function handleTriggers() end
registerForEvent("onInit", function()
    local function zeroEngineOnAction(_, action)
        if not autoLoot.settings.useDefaultActionKey then return end
    end
    local zeroEngineMod, zeroEngine, zeroEngineOk = {}, {}, true
    local zeroEngineConfig = {actions={"UI_Apply"}}
    if type(zeroEngineMod) == "table" then
        zeroEngineMod.SubscribeAction(zeroEngineConfig, zeroEngineOnAction)
    elseif type(zeroEngine) == "table" then
        zeroEngine.SubscribeAction(zeroEngineConfig, zeroEngineOnAction, "AutoLoot")
    else
        Observe('PlayerPuppet', 'OnAction', zeroEngineOnAction)
    end
@@TAB@@autoLoot.ui.onInit()
end)
registerForEvent("onUpdate", function(delta)
    handleTriggers()
end)
registerInput('AutoLoot','Author key',function(down) end)
'@.Replace('@@TAB@@',[char]9)
$out2 = Rewrite 'ExtendAutoLootDefaultActionSubscriber' $loot
foreach ($needle in @('-- __gcetAutoLootDefaultActionDormancyV2',
    '__gcetAutoLootActionHandle = zeroEngineMod.SubscribeAction',
    '__gcetAutoLootActionHandle = zeroEngine.SubscribeAction',
    'pcall(handle.SetActive, needed)',
    'autoLoot.settings.useDefaultActionKey == true')) {
    if (!$out2.Contains($needle)) {throw "AutoLoot C-stage missing: $needle"}
}
if ((Rewrite 'ExtendAutoLootDefaultActionSubscriber' $out2) -cne $out2) {
    throw 'AutoLoot subscriber stage not idempotent'
}
if ([regex]::Matches($out2,"Observe\('PlayerPuppet', 'OnAction'").Count -ne 1) {
    throw 'AutoLoot native fallback duplicated'
}
$unsupported = @'
local function zeroEngineOnAction() end
Observe('PlayerPuppet', 'OnAction', zeroEngineOnAction)
'@
if ((Rewrite 'ExtendAutoLootDefaultActionSubscriber' $unsupported) -cne $unsupported) {
    throw 'AutoLoot route without independent setting wake was changed'
}
Write-Host 'IFP FreeLook C-stage / AutoLoot default-action C-stage: pristine, fallback, source wake and idempotence PASS'
