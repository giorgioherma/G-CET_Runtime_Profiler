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
'@.Replace('@@TAB@@',"`t")
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

# Critical integration: a previously routed live AutoLoot has NO separate
# PlayerPuppet::OnAction profiler registration: 0-Engine owns dispatch.
# It must still qualify for its source-proven missing C-stage. Do not
# synthesize measured callback activity or treat earlier A/B completion
# as C-stage completion.
$root=Join-Path $env:RUNNER_TEMP 'gcet-autoloot-existing-route-sourceonly'
if (Test-Path $root) {Remove-Item -LiteralPath $root -Recurse -Force}
$capture=Join-Path $root 'capture'
$mods=Join-Path $root 'mods'
$lootDir=Join-Path $mods 'AutoLoot'
$zeroDir=Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$lootDir,$zeroDir | Out-Null
$exe=Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
$encoded=(Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
$input=[IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try {$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())}
finally {$output.Dispose();$gzip.Dispose();$input.Dispose()}
# Build from the actual *shape* of the user's live 0-Engine-routed source,
# not from an original native OnAction observer.
$live=@'
local __gcetRegisterEvent_45 = registerForEvent
local autoLoot = {
    settings = {useDefaultActionKey=false},
    isContinuousLooting = false,
    ui = {onInit=function()end}
}
-- This original owner-state write is source identity proof; it is not
-- inferred from a zero-callback profiler capture.
autoLoot.isContinuousLooting = false
local function handleButtonPressed(this,action) end
local function handleButtonReleased(this,action) end
registerForEvent("onInit", function()
    local function zeroEngineOnAction(this, action)
        if not autoLoot.settings.useDefaultActionKey then return end
        local actionType = action:GetType(action)
        if actionType == gameinputActionType.BUTTON_PRESSED then handleButtonPressed(this,action) end
        if actionType == gameinputActionType.BUTTON_RELEASED then handleButtonReleased(this,action) end
    end
    local zeroEngineOk, zeroEngine = pcall(GetMod, "0-Engine")
    local zeroEngineMod = zeroEngineOk and zeroEngine.Register("AutoLoot") or nil
    local zeroEngineConfig = { actions = { "UI_DPadWeapons", "TogglePhotoMode" }, types = {"BUTTON_PRESSED","BUTTON_RELEASED"} }
    if type(zeroEngineMod) == "table" and type(zeroEngineMod.SubscribeAction) == "function" then
        zeroEngineMod.SubscribeAction(zeroEngineConfig, zeroEngineOnAction)
    elseif zeroEngineOk and type(zeroEngine) == "table" and type(zeroEngine.SubscribeAction) == "function" then
        zeroEngine.SubscribeAction(zeroEngineConfig, zeroEngineOnAction, "AutoLoot")
    else
        Observe('PlayerPuppet', 'OnAction', zeroEngineOnAction)
    end
    autoLoot.ui.onInit()
end)
local function handleTriggers() end
__gcetRegisterEvent_45("onUpdate", function(delta)
    handleTriggers()
end)
'@
# Normalise only known author indentation; other current-source contents
# are unchanged. Source-only admission must not require callback records.
$live=$live.Replace('    autoLoot.ui.onInit()', ([string][char]9)+'autoLoot.ui.onInit()')
$live | Set-Content -LiteralPath (Join-Path $lootDir 'init.lua') -Encoding utf8
$empty=@{schemaVersion='2.0';callbacks=@();optimizerEvidence=@()} | ConvertTo-Json -Depth 35
$empty | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8
$first=(& $exe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$first.ok -or !$first.pass) {throw 'AutoLoot source-only generation failed'}
$manifest=Get-Content -LiteralPath $first.pass.ManifestPath -Raw | ConvertFrom-Json
$autoloot=@($manifest.transforms | Where-Object {$_.RuleId -eq 'autoloot'})
if ($autoloot.Count -ne 1 -or $autoloot[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
    $null -ne $autoloot[0].runtimeMsPerSecond -or
    'AutoLoot/init.lua' -notin @($autoloot[0].files)) {
    $skips=@($manifest.skipped | Where-Object {$_.RuleId -eq 'autoloot'})
    throw ("Existing-route zero-callback AutoLoot C stage was not source-admitted: " +
        ($skips | ConvertTo-Json -Compress))
}
$arc=[IO.Compression.ZipFile]::OpenRead([string]$first.pass.ZipPath)
try {
    $entry=$arc.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/AutoLoot/init.lua')
    if (!$entry) {throw 'AutoLoot source-only generated ZIP entry missing'}
    $reader=[IO.StreamReader]::new($entry.Open())
    try {$generated=$reader.ReadToEnd()} finally {$reader.Dispose()}
} finally {$arc.Dispose()}
foreach ($needle in @('__gcetAutoLootDefaultActionDormancyV2',
   '__gcetAutoLootActionHandle = zeroEngineMod.SubscribeAction',
   '__gcetAutoLootActionHandle = zeroEngine.SubscribeAction',
   'pcall(handle.SetActive, needed)',
   'Observe(''PlayerPuppet'', ''OnAction'', zeroEngineOnAction)',
   '__gcetRegisterEvent_45("onUpdate"')) {
    if (!$generated.Contains($needle)) {throw "AutoLoot source-only generated stage missing: $needle"}
}
$generated | Set-Content -LiteralPath (Join-Path $lootDir 'init.lua') -Encoding utf8
$again=(& $exe --capture $capture --mods $mods --json | ConvertFrom-Json)
if (!$again.ok) {throw 'AutoLoot source-only complete-source analysis failed'}
$report=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
if (@($report.semanticSourceOnlyCandidates | Where-Object {$_.ruleId -eq 'autoloot'}).Count -ne 0) {
    throw 'AutoLoot existing-route C stage not recognized as complete'
}
Write-Host 'AutoLoot 0-Engine delegated OnAction (zero profiler callback) => source-only native subscriber upgrade and completion: PASS'

Write-Host 'IFP FreeLook C-stage / AutoLoot default-action C-stage: pristine, fallback, source wake and idempotence PASS'
