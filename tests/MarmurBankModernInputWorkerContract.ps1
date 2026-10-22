param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-bank-225-worker'
$folder=Join-Path $root 'marmurbank'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force (Join-Path $folder 'module')|Out-Null
New-Item -ItemType Directory -Force (Join-Path $folder 'external')|Out-Null
$ui=@'
local ui = { hubShown = false, input = false, suppressVanillaDialogs = false }
local WORLD_INTERACTION_ACTIONS = { ChoiceApply = true, Interact = true }
local function shouldBlockWorldAction(actionName,actionType) return false end
local function shouldSuppressVanillaNow() return ui.suppressVanillaDialogs end
local function getActionDetails(action) return "", "" end
local function consumeAction(action,wrappedConsumer) end
local function nowSeconds() return os.clock() end
function ui.suppressVanilla(active) end
function ui.maintainVanillaSuppression() end
function ui.isVanillaDialogActive(recentSeconds) end
function ui.init()
  local worldActionOverrideOk = pcall(function()
    Override("PlayerPuppet", "OnAction", function(_, action, consumer, wrappedMethod)
      local wrapped = wrappedMethod
      local wrappedConsumer = consumer
      if action then
        local actionName, actionType = getActionDetails(action)
        if shouldBlockWorldAction(actionName, actionType) then
          consumeAction(action, wrappedConsumer)
          ui.input = true
          return true
        end
      end
      if wrapped then
        if wrappedConsumer ~= nil then return wrapped(action, wrappedConsumer) end
        return wrapped(action)
      end
      return false
    end)
  end)
  Observe('PlayerPuppet', 'OnAction', function(_, action)
    if shouldBlockWorldAction(getActionDetails(action)) then
      ui.input = true
      return
    end
    if ui.input or not ui.hubShown then return end
    local actionName = Game.NameToString(action:GetName(action))
    if actionName == 'ChoiceScrollUp' then
      ui.selectedIndex = 0
    elseif actionName == 'ChoiceScrollDown' then
      ui.selectedIndex = 1
    elseif actionName == 'ChoiceApply' then
      if ui.callbacks[ui.selectedIndex + 1] then
        ui.callbacks[ui.selectedIndex + 1]()
      end
    end
  end)
  Override("InteractionUIBase", "OnDialogsSelectIndex", function(_, idx, wrapped) wrapped(idx) end)
end
return ui
'@
$init=@'
local packageEvent = registerForEvent
local packageRequire = require
local packageAllowedModules={ ["module/Bank"] = true }
require=function(name) if not packageAllowedModules[name] then return nil end return packageRequire(name) end
local function registerZeroEngineInterval(modName, interval, callback) end
registerForEvent("onShutdown", function() end)
if BANK then
    BANK:initialize(SPAWN)
end
GameUI.Listen(function(state)
    local event = state.event
    if event == "MenuClose" then isInMenu = false end
end)
marmurRuntimeTick = function(delta)
    if delta > MARMUR_UPDATE_DELTA_MAX then delta = MARMUR_UPDATE_DELTA_MAX end
    BANK:updateTimers(delta)
end
'@
$bank=@'
function BANK:openAtmKeypadFromKeybind() return true end
function BANK:getNearbyAtmIndex(ignoreDismissed) return 1 end
function BANK:_cacheLocationPositions()
    for _, entry in ipairs(self._atmPosCache or {}) do
      if entry.isDropPoint == true then return end
    end
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyMarmurBankModernInput',[Reflection.BindingFlags]'Static,NonPublic')
function Apply([string]$u,[string]$i,[string]$b) {
    [IO.File]::WriteAllText((Join-Path $folder 'external/interactionUI.lua'),$u,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$i,[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $folder 'module/Bank.lua'),$b,[Text.UTF8Encoding]::new($false))
    $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'marmurbank',[string]'marmurbank-modern-input',[Collections.Generic.Dictionary[string,byte[]]]::new()))
    try {$result=$m.Invoke($null,[object[]]@($ctx))}
    catch { throw "Marmur 22.5 source worker: $($_.Exception.InnerException.Message)" }
    return @{result=$result;rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $ui $init $bank
if(!$first.result.Applied -or $first.rows.Count -ne 3){ throw "Expected 3 source/worker changes; got $($first.rows.Count)" }
$u=[Text.Encoding]::UTF8.GetString((@($first.rows|Where-Object{$_.RelativeFile -like '*/external/interactionUI.lua'})[0]).Bytes)
$i=[Text.Encoding]::UTF8.GetString((@($first.rows|Where-Object{$_.RelativeFile -like '*/init.lua'})[0]).Bytes)
$worker=[Text.Encoding]::UTF8.GetString((@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/marmurbank_ui_worker.lua'})[0]).Bytes)
foreach($needle in @('GCET_Semantic/marmurbank_ui_worker','__gcetWorker.subscribe','__gcetWorker.overrideActive','local function __gcetMarmurBankOnAction')) {
 if(!$u.Contains($needle)){throw "UI bridge lost $needle"}
}
foreach($needle in @('GameUI.Listen(function(state)','__gcetDropoffVendorClose(state)','BANK:updateTimers(delta)','BANK:initialize(SPAWN)','["GCET_Semantic/marmurbank_ui_worker"]=true')) {
 if(!$i.Contains($needle)){throw "Init bridge lost $needle"}
}
foreach($needle in @('G-CET semantic coordinator:marmurbank-modern-input','state.lastMenu ~= "Vendor"','"Trade"','0.35','bank._atmPosCache','entry.isDropPoint','SubscribeAction','originalObserve')) {
 if(!$worker.Contains($needle.Replace('originalObserve','observe('))){throw "Worker missing $needle"}
}
if($worker.Contains('Observe("InteractionUIBase"')){throw 'Worker added redundant vanilla dialog observer'}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/marmurbank_ui_worker.lua'),$worker,[Text.UTF8Encoding]::new($false))
$again=Apply $u $i $bank
if($again.result.Applied){throw 'Coordinator re-applied'}
$crlf=Apply ($ui.Replace("`n","`r`n")) ($init.Replace("`n","`r`n")) $bank
if(!$crlf.result.Applied){throw 'CRLF source refused'}
$drift= $ui.Replace('consumeAction(action, wrappedConsumer)','consumeAction(action, unknownConsumer)')
$refused=$false
try {$null=Apply $drift $init $bank}
catch {$refused=$_.Exception.Message -match 'Marmur suppression override changed'}
if(!$refused){throw 'Updated world suppression behavior was not refused'}

# Prove full pass generation ignores the obsolete 1-file count for a
# source-owned coordinator while still applying strict source checks.
[IO.File]::WriteAllText((Join-Path $folder 'external/interactionUI.lua'),$ui,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $folder 'init.lua'),$init,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $folder 'module/Bank.lua'),$bank,[Text.UTF8Encoding]::new($false))
$generatorType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPassGeneratorService',$true)
$generate=$generatorType.GetMethod('Apply',[Reflection.BindingFlags]'Static,NonPublic')
$oneFileCatalog=@'
{
  "callbackFamilies":[],
  "semanticSourceOnlyCandidates":[{
    "ruleId":"marmurbank-modern-input",
    "owner":"marmurbank",
    "handler":"SEMANTIC_MODERN_BANK_INPUT",
    "policyClass":"MODERN_BANK_SELECTIVE_ACTION_OWNER",
    "patchStyle":"source-injection",
    "sourceIndependent":true,
    "sourceProofSatisfied":true,
    "generationEnabled":true,
    "alreadySatisfied":false,
    "expectedMarkerFileCount":1
  }]
}
'@
$jsonDoc=[System.Text.Json.JsonDocument]::Parse($oneFileCatalog)
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
$byFile=[Collections.Generic.Dictionary[string,int]]::new()
$transforms=[Collections.Generic.List[object]]::new()
$skipped=[Collections.Generic.List[object]]::new()
try {
    $generated=$generate.Invoke($null,[object[]]@(
        $jsonDoc.RootElement,[string]$root,$staged,$byFile,$transforms,$skipped))
} finally { $jsonDoc.Dispose() }
if($generated -ne 1 -or $staged.Count -ne 3 -or $skipped.Count -ne 0) {
    $details=($skipped | ConvertTo-Json -Depth 8 -Compress)
    throw "Full Bank pass refused valid source-owned coordinator: applied=$generated, staged=$($staged.Count), skipped=$($skipped.Count), reasons=$details"
}
if(@($staged.Keys|Where-Object{$_ -like '*/GCET_Semantic/marmurbank_ui_worker.lua'}).Count -ne 1) {
    throw 'Full pass failed to package the separate Bank worker.'
}
Write-Host 'Marmur generated-pass structural ownership admission, independent of marker file count: PASS'

Write-Host 'Marmur 22.5 source worker, Vendor close, 0-Engine routing, CRLF, drift and idempotence: PASS'
