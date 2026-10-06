param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$ResolverRoot=(Resolve-Path $ResolverRoot).Path
$resolverExe=Join-Path $ResolverRoot 'G-CET-Resolver.exe'
$root=Join-Path $env:RUNNER_TEMP 'gcet-resolver-accuracy-semantic'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
$capture=Join-Path $root 'CET-20990101-235631_RESOLVER_ACCURACY'
$mods=Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods|Out-Null

$zeroDir=Join-Path $mods '0-Engine'; New-Item -ItemType Directory -Force $zeroDir|Out-Null
$encoded=(Get-Content (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$input=[IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output=[IO.MemoryStream]::new()
try{$gzip.CopyTo($output);[IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())}
finally{$output.Dispose();$gzip.Dispose();$input.Dispose()}

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source){
  $p=Join-Path (Join-Path $mods $Mod) $Relative
  New-Item -ItemType Directory -Force (Split-Path $p -Parent)|Out-Null
  $Source|Set-Content $p -Encoding utf8
}
function Find-CallbackRange([string]$Path,[string]$Pattern){
  $lines=@(Get-Content $Path);$start=-1
  for($i=0;$i-lt$lines.Count;$i++){if($lines[$i]-match$Pattern){$start=$i;break}}
  if($start-lt0){throw "Opening not found: $Pattern"}
  if($lines[$start]-match'end\s*\)\s*;?\s*$'){return @{Start=$start+1;End=$start+1}}
  for($i=$start+1;$i-lt$lines.Count;$i++){if($lines[$i]-match'^\s*end\s*\)\s*;?\s*$'){return @{Start=$start+1;End=$i+1}}}
  throw "Callback closing not found: $Pattern"
}
function Row($Id,$Owner,$Kind,$Target,$File,$Range,$Ms,$Calls=60){
  @{registrationId=$Id;owner=$Owner;infrastructure=$false;kind=$Kind;target=$Target;
    source=@{file=$File;lineStart=$Range.Start;lineEnd=$Range.End};
    callsPerSecond=$Calls;exclusiveMsPerSecond=$Ms;globalWorkSharePct=5;familyWorkSharePct=40;
    avgExclusiveUs=100;maxExclusiveMs=2;spikeCount=0;maxSpikeExclusiveMs=0}
}

Write-ModFile 'DriveBus' 'Modules/core.lua' @'
local Core={}
local exception_in_choice_list={NextWeapon=true,PreviousWeapon=true}
local exception_in_mount_list={QuickExit=true}
function Core:ChoiceAction(action_name,action,consumer)
 if action_name=="ChoiceApply" or action_name=="ChoiceScrollUp" or action_name=="ChoiceScrollDown" then consumer:Consume(action) end
end
function Core:SetObserve()
 Observe("PlayerPuppet","OnAction",function(self,action,consumer)
  local action_name=Game.NameToString(action:GetName(action))
  if exception_in_choice_list[action_name] or exception_in_mount_list[action_name] then
   self:ChoiceAction(action_name,action,consumer)
   return
  end
  self:ChoiceAction(action_name,action,consumer)
 end)
end
return Core
'@

Write-ModFile 'QuestTrackingToggle' 'init.lua' @'
local n_ToggleSprint=n"ToggleSprint"
local n_CameraAim=n"CameraAim"
local n_world_map_menu_rotate_mouse=n"world_map_menu_rotate_mouse"
local n_LeanFB=n"LeanFB"
local n_world_map_menu_zoom_to_mappin=n"world_map_menu_zoom_to_mappin"
local n_PhoneInteract=n"PhoneInteract"
local n_Jump=n"Jump"
local n_Handbrake=n"Handbrake"
local n_world_map_filter_navigation_down=n"world_map_filter_navigation_down"
local n_world_map_menu_track_waypoint=n"world_map_menu_track_waypoint"
local function toggleTrackedQuest() end
local function handlePadAction(this,action,consumer,isKBM)
 if action:IsAction(action,n_PhoneInteract) or action:IsAction(action,n_Jump) or action:IsAction(action,n_Handbrake)
 or action:IsAction(action,n_world_map_filter_navigation_down) or action:IsAction(action,n_world_map_menu_track_waypoint) then
  consumer.Consume(consumer);return true
 end
end
local function handlePadLongPressTrigger(this,action,isKBM)
 if action:IsAction(action,n_world_map_menu_zoom_to_mappin) then return true end
end
Observe('PlayerPuppet','OnAction',function(this,action,consumer)
 local isLeanFB=action:IsAction(action,n_LeanFB)
 if handlePadAction(this,action,consumer,true) then return end
 if handlePadLongPressTrigger(this,action,true) then return end
 if action:IsAction(action,n_ToggleSprint) and action:IsAction(action,n_CameraAim) then toggleTrackedQuest() end
 if action:IsAction(action,n_world_map_menu_rotate_mouse) then toggleTrackedQuest() end
 if isLeanFB and action:GetValue(action)>0 then toggleTrackedQuest() end
end)
'@

Write-ModFile 'sitAnywhere' 'init.lua' @'
local Cron={Update=function() end}
local interaction={hubShown=false,update=function() end}
local world={update=function() end}
local self={runtimeData={inMenu=false,inGame=true,forceScan=false},logic={
 isScanning=false,sittables={},onUpdate=function() end,
 inWorkspot=function() return false end,inTransition=function() return false end},yaw=0,pitch=0}
registerForEvent("onUpdate", function(dt)
        if not self.runtimeData.inMenu and self.runtimeData.inGame then
            Cron.Update(dt)
            interaction.update()
            world.update()
            self.logic:onUpdate()
            for _, spot in pairs(self.logic.sittables) do
                spot.workspot.yaw = self.yaw
                spot.workspot.pitch = self.pitch
                spot:update(dt)
            end
        end
end)
'@

Write-ModFile 'repeatable_increased_criminal_activity' 'init.lua' @'
local Mod={
    settings={diagnostics={enabled=true}},
    playerAttached=false,
    tickElapsed = 0.0,
    schedulerElapsed = 0.0,
}
local Sites={list={}}
local Mappins={sync=function() end}
local Diagnostics={setEnabled=function() end,configuration=function() end,event=function() end,snapshot=function() end}
local Bridge={system=function() return {Tick=function() end} end}
local function processBodyRewards(system) end
local function processCompletionRewards(system) end
function Mod.applyRuntimeSettings() end
local function runtimeTick()
    local system = Bridge.system()
    if not system or not Game.GetPlayer() then return end
    Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)
    Diagnostics.configuration(Mod.settings)
    Mod.applyRuntimeSettings()
    processBodyRewards(system)
    processCompletionRewards(system)
    Mappins.sync(system, Sites)
    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)
end
registerForEvent("onUpdate", function(delta)
    Mod.tickElapsed = Mod.tickElapsed + delta
    Mod.schedulerElapsed = Mod.schedulerElapsed + delta
    if Mod.tickElapsed >= 1.0 then
        Mod.tickElapsed = 0.0
        local ok, err = pcall(runtimeTick)
    end
    if Mod.schedulerElapsed >= 10.0 then
        Mod.schedulerElapsed = 0.0
        local system = Bridge.system()
        if system then local ok, err = pcall(function() system:Tick() end) end
    end
end)
'@

Write-ModFile 'Dedka Auto Shop' 'init.lua' @'
local state={showing=false}
local ui={update=function() end}
local function showRootHub() state.showing=true end
registerForEvent("onDraw", function() pcall(function() ui.update() end) end)
registerForEvent("onUpdate", function(_) end)
'@

Write-ModFile 'marmurbank' 'external/InteractionUI.lua' @'
local ui={input=false,hubShown=false}
local WORLD_INTERACTION_ACTIONS={ChoiceApply=true,Open=true,Use=true,Interact=true,UI_Apply=true}
local function isPressed(actionType) return tostring(actionType or "")=="BUTTON_PRESSED" end
local function getActionDetails(action)
 local actionName=Game.NameToString(action:GetName(action)) or ""
 local actionType=action:GetType(action).value
 return tostring(actionName or ""),tostring(actionType or "")
end
local function shouldBlockWorldAction(actionName,actionType)
 return WORLD_INTERACTION_ACTIONS[tostring(actionName or "")]==true and isPressed(actionType)
end
function ui.init()
 local worldActionOverrideOk=pcall(function()
  Override("PlayerPuppet","OnAction",function(_,action,consumer,wrappedMethod)
   local wrapped=wrappedMethod
   local wrappedConsumer=consumer
   if wrapped==nil then wrapped=consumer;wrappedConsumer=nil end
   if action then
    local actionName, actionType = getActionDetails(action)
    if shouldBlockWorldAction(actionName,actionType) then ui.input=true;return true end
   end
   if wrapped then
    if wrappedConsumer~=nil then return wrapped(action,wrappedConsumer) end
    return wrapped(action)
   end
   return false
  end)
 end)
 Observe('PlayerPuppet','OnAction',function(_,action)
  if shouldBlockWorldAction(getActionDetails(action)) then ui.input=true;return end
  if ui.input or not ui.hubShown then return end
  local actionName=Game.NameToString(action:GetName(action))
  local actionType=action:GetType(action).value
  if actionName=='ChoiceScrollUp' or actionName=='ChoiceScrollDown' or actionName=='ChoiceApply' then
   ui.input=actionType=='BUTTON_PRESSED'
  end
 end)
end
return ui
'@

Write-ModFile 'immersive_third_person' 'init.lua' @'
local state={autoPerspective={},enabled=false,frameSeq=0,menuWasOpen=false}
local mod={}
local function isPlayerInAnyMenu() return false end
function mod.clearDigitalMoveLatches() end
function mod.nativeSettingsSaveTick() end
function mod.pollNativeToggle() end
function mod.nativeSettingsComboTick() end
function mod.headLookTick() end
function mod.fallCommitTick() end
function mod.notifyFaultTick() end
function mod.updatePhotoModeHeadRestore() end
function mod.updateHeadGuard() end
function mod.updateConsumableIdle() end
local function autoReadPhotoMode() return false end
local function updateSessionGuard(delta) end
local function updateAutoPerspective(delta) end
local function updateCameraTransition(delta) end
local function updateLootAssist(delta) end
local function updateThirdPersonCamera(delta) end
local function updatePendingFppCleanup(delta) end
local function updateFppRestoreWatchdog(delta) end
local function updateTppRepReassert(delta) end
local function updateMirrorHeadVerify(delta) end
local function updatePostSceneTppReapply(delta) end
local function updateFacialMute(delta) end
local function updatePendingTppAnimPoke(delta) end
local function updateItemPickupPulse(delta) end
local function updateDependencyGuard(delta) end
local function updateTppMaintenance(delta) end
local function safeCallQuiet(fn) pcall(fn) end
local function guardStep(name,fn,delta) if fn then fn(delta) end end
registerForEvent('onUpdate', function(delta)
  state.modClock = (state.modClock or 0) + math.max(delta or 0, 0)
  state.frameSeq = (state.frameSeq or 0) + 1
  local inMenuNow = isPlayerInAnyMenu()
  if state.menuWasOpen and not inMenuNow then
    mod.clearDigitalMoveLatches("menu close")
  end
  state.menuWasOpen = inMenuNow
  pcall(mod.nativeSettingsSaveTick, delta)
  pcall(mod.pollNativeToggle, delta)
  if (state.frameSeq % 6) == 0 then
    pcall(mod.nativeSettingsComboTick)
  end
  pcall(mod.headLookTick, delta)
  pcall(mod.fallCommitTick, delta)
  if state.pendingFaultNotice then
    state.faultNotifyTimer = (state.faultNotifyTimer or 0) + (delta or 0)
    if state.faultNotifyTimer >= 5.0 then
      state.faultNotifyTimer = 0
      pcall(mod.notifyFaultTick)
    end
  end
  if state.enabled or state.photoModeWasActive then
    state.photoModePollTimer = (state.photoModePollTimer or 0) + (delta or 0)
    if state.photoModePollTimer >= 0.15 then
      state.photoModePollTimer = 0
      local photoModeNow = autoReadPhotoMode()
      if state.photoModeWasActive and not photoModeNow and state.enabled then
        state.photoModeHeadRestore = { at = (state.modClock or 0) + 0.10, passes = 0 }
      end
      state.photoModeWasActive = photoModeNow
    end
  end
  mod.updatePhotoModeHeadRestore()

  safeCallQuiet(function() updateSessionGuard(delta) end)
  safeCallQuiet(function() updateAutoPerspective(delta) end)
  guardStep("updateCameraTransition", updateCameraTransition, delta)
  guardStep("updateLootAssist", updateLootAssist, delta)
  guardStep("updateThirdPersonCamera", updateThirdPersonCamera, delta)
  guardStep("updateHeadGuard", mod.updateHeadGuard)
  guardStep("updatePendingFppCleanup", updatePendingFppCleanup, delta)
  guardStep("updateFppRestoreWatchdog", updateFppRestoreWatchdog, delta)
  guardStep("updateTppRepReassert", updateTppRepReassert, delta)
  guardStep("updateMirrorHeadVerify", updateMirrorHeadVerify, delta)
  guardStep("updatePostSceneTppReapply", updatePostSceneTppReapply, delta)
  guardStep("updateFacialMute", updateFacialMute, delta)
  guardStep("updatePendingTppAnimPoke", updatePendingTppAnimPoke, delta)
  guardStep("updateItemPickupPulse", updateItemPickupPulse, delta)
  guardStep("updateConsumableIdle", mod.updateConsumableIdle, delta)
  guardStep("updateDependencyGuard", updateDependencyGuard, delta)

  if state.enabled then
    guardStep("tppMaintenance", updateTppMaintenance, delta)
  end
end)
'@

$drive=Join-Path $mods 'DriveBus\Modules\core.lua'
$qtt=Join-Path $mods 'QuestTrackingToggle\init.lua'
$sit=Join-Path $mods 'sitAnywhere\init.lua'
$rica=Join-Path $mods 'repeatable_increased_criminal_activity\init.lua'
$dedka=Join-Path $mods 'Dedka Auto Shop\init.lua'
$bank=Join-Path $mods 'marmurbank\external\InteractionUI.lua'
$itp=Join-Path $mods 'immersive_third_person\init.lua'
$handoff=@{schemaVersion='1.9';callbacks=@(
 (Row 1 'DriveBus' 'Observe' 'PlayerPuppet::OnAction' 'Modules/core.lua' (Find-CallbackRange $drive 'Observe\("PlayerPuppet"') 17.82 1485),
 (Row 2 'QuestTrackingToggle' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' (Find-CallbackRange $qtt "Observe\('PlayerPuppet'") 11.65 1485),
 (Row 3 'sitAnywhere' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $sit 'registerForEvent\("onUpdate"') 7.81 60),
 (Row 4 'repeatable_increased_criminal_activity' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $rica 'registerForEvent\("onUpdate"') 4.72 60),
 (Row 5 'Dedka Auto Shop' 'event' 'onDraw' 'init.lua' (Find-CallbackRange $dedka 'registerForEvent\("onDraw"') 3.34 60),
 (Row 6 'marmurbank' 'Observe' 'PlayerPuppet::OnAction' 'external/InteractionUI.lua' (Find-CallbackRange $bank "Observe\('PlayerPuppet'") 23.30 1485),
 (Row 7 'immersive_third_person' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $itp "registerForEvent\('onUpdate'") 26.34 60)
);optimizerEvidence=@()}|ConvertTo-Json -Depth 30
$handoff|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved=(& $resolverExe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
if(!$resolved.ok -or $null-eq$resolved.pass){throw 'Resolver-accuracy semantic pass generation failed.'}
$resolver=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
$rules=@('drivebus','quest-tracking-toggle','sitanywhere','repeatable-increased-criminal-activity','dedka-auto-shop','marmurbank','immersive-third-person')
foreach($rule in $rules){
 $m=@();foreach($family in @($resolver.callbackFamilies)){$m+=@($family.topConsumers|Where-Object{$_.semantic.RuleId-eq$rule})}
 if($m.Count-lt1 -or @($m|Where-Object{$_.semantic.SourceProofSatisfied}).Count-lt1){throw "Semantic source proof failed: $rule"}
}
$manifest=Get-Content $resolved.pass.ManifestPath -Raw|ConvertFrom-Json
foreach($rule in $rules){
 $hits=@($manifest.transforms|Where-Object{$_.type-eq'SEMANTIC_RULE' -and $_.RuleId-eq$rule})
 if($hits.Count-ne1){
  $skip=@($manifest.skipped|Where-Object{$_.type-eq'SEMANTIC_RULE' -and $_.RuleId-eq$rule})
  throw "Semantic transform missing: $rule; $($skip|ConvertTo-Json -Depth 8 -Compress)"
 }
}
if(@($manifest.transforms|Where-Object{$_.type-eq'FRAME_DISPATCH_CONSOLIDATION' -and $_.eventTarget-eq'onDraw' -and $_.owner-eq'Dedka Auto Shop'}).Count-ne1){
 throw 'Generic onDraw consolidation did not compose before Dedka semantic.'
}

Add-Type -AssemblyName IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try{
 function Z($n){$e=$zip.GetEntry($n);if(!$e){throw "ZIP entry missing: $n"};$r=[IO.StreamReader]::new($e.Open());try{$r.ReadToEnd()}finally{$r.Dispose()}}
 $b='bin/x64/plugins/cyber_engine_tweaks/mods/'
 $x=Z($b+'DriveBus/Modules/core.lua');foreach($t in @('gcetDriveBusOnAction','SubscribeAction','QuickExit','ChoiceScrollDown')){if($x-notmatch[regex]::Escape($t)){throw "DriveBus missing $t"}}
 $x=Z($b+'QuestTrackingToggle/init.lua');foreach($t in @('questTrackingOnAction','SubscribeAction','ToggleSprint','world_map_menu_track_waypoint')){if($x-notmatch[regex]::Escape($t)){throw "QTT missing $t"}}
 $x=Z($b+'sitAnywhere/init.lua');if($x-notmatch'__gcetSitIdleElapsed' -or $x-notmatch'interaction\.hubShown' -or $x-notmatch'self\.logic\.isScanning'){throw 'sitAnywhere split incomplete.'}
 $x=Z($b+'repeatable_increased_criminal_activity/init.lua');if($x-notmatch'diagnosticsElapsed' -or $x-notmatch'runtimeTick\(includeDiagnostics\)' -or $x-notmatch'__gcetRunDiagnostics = Mod\.diagnosticsElapsed >= 5\.0'){throw 'RICA split incomplete.'}
 $x=Z($b+'Dedka Auto Shop/init.lua');if($x-notmatch'if state\.showing then' -or $x-notmatch'__gcetRegisterEvent_\d+\("onDraw"'){throw 'Dedka draw composition incomplete.'}
 $x=Z($b+'marmurbank/external/InteractionUI.lua');if($x-notmatch'__gcetMarmurActionRelevant' -or $x-notmatch'WORLD_INTERACTION_ACTIONS\[name\] == true' -or $x-notmatch'wrapped\(action, wrappedConsumer\)'){throw 'MarmurBank prefilter incomplete.'}
 $x=Z($b+'immersive_third_person/init.lua');if($x-notmatch'__gcetItppSupervisorElapsed' -or $x-notmatch'__gcetItppMaintenanceElapsed' -or $x-notmatch'if state\.pendingFppCleanup then' -or $x-notmatch'pcall\(mod\.fallCommitTick, delta\)'){throw 'ITP split incomplete.'}
}finally{$zip.Dispose()}
Write-Host 'Resolver-accuracy semantic composition contract passed.'
