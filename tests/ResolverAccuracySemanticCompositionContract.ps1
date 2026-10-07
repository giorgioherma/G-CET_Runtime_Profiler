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

Write-ModFile 'DriveBus' 'External/Cron.lua' @'
local timers = {}
function Cron.After(timeout, callback, data) end
function Cron.Every(timeout, callback, data) end
function Cron.Update(delta)
 for _, timer in ipairs(timers) do
  if timer.active then timer.delay = (timer.delay or 0) - delta end
 end
end
return Cron
'@

Write-ModFile 'DriveBus' 'init.lua' @'
Cron = require("External/Cron.lua")
registerForEvent("onUpdate", function(delta)
    Cron.Update(delta)
end)
registerForEvent("onDraw", function() end)
'@

Write-ModFile 'AutoLoot' 'init.lua' @'
local autoLoot={settings={useDefaultActionKey=true},isContinuousLooting=false}
local n_UI_DPadWeapons=n"UI_DPadWeapons"
local n_TogglePhotoMode=n"TogglePhotoMode"
local function handleButtonPressed(this,action)
 if action:IsAction(action,'UI_Apply') or action:IsAction(action,'TogglePhotoMode') then autoLoot.isContinuousLooting=true end
end
local function handleButtonReleased(this,action)
 if action:IsAction(action,'Choice1') or action:IsAction(action,'UI_DPadWeapons') then autoLoot.isContinuousLooting=false end
end
local actionType=nil
Observe('PlayerPuppet','OnAction',function(this,action)
 if not autoLoot.settings.useDefaultActionKey then return end
 actionType=action:GetType(action)
 if actionType==gameinputActionType.BUTTON_PRESSED then handleButtonPressed(this,action);return end
 if actionType==gameinputActionType.BUTTON_RELEASED then handleButtonReleased(this,action);return end
end)
registerForEvent("onUpdate",function(delta) end)
'@

Write-ModFile 'BetterLootMarkers' 'init.lua' @'
local BetterLootMarkers={
 Settings={immersiveMode=false},
 ImmersiveMode={Init=function() end,Tick=function(dt) end}
}
function BetterLootMarkers.HandleLootMarkersForController(ctrl) end
registerForEvent("onInit",function()
 BetterLootMarkers.ImmersiveMode.Init()
end)
registerForEvent("onUpdate",function(dt)
 BetterLootMarkers.ImmersiveMode.Tick(dt)
end)
local function blmFixtureSettingProbe()
 return BetterLootMarkers.Settings.immersiveMode
end)
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

Write-ModFile 'sitAnywhere' 'modules/external/Cron.lua' @'
local Cron={}
local timers={}
function Cron.Every(timeout,callback,data) end
function Cron.Update(delta)
 for _,timer in ipairs(timers) do
  if timer.active then timer.delay=(timer.delay or 0)-delta end
 end
end
function Cron.Halt(timerId) end
return Cron
'@

Write-ModFile 'sitAnywhere' 'modules/worldInteraction.lua' @'
local world={interactions={}}
function world.update() end
function world.togglePin(interaction,state) end
function world.onSessionStart()
 for _,interaction in pairs(world.interactions) do
  interaction.shown=false
  interaction.pinID=nil
 end
end
return world
'@

Write-ModFile 'sitAnywhere' 'modules/logic.lua' @'
local world=require("modules/worldInteraction")
local logic={}
function logic:new(mod)
 local o={}
 o.isScanning=false
 o.mod=mod
 o.sittables={}
 self.__index=self
 return setmetatable(o,self)
end
function logic:inWorkspot() return false end
function logic:inTransition() return false end
function logic:hideAllWorkspots()
 for key,_ in pairs(self.sittables) do
  world.interactions[key].pos=Vector4.new(0,0,0,0)
 end
end
function logic:onUpdate()
 local position=nil
 if position then
  world.interactions[0].pos=position
 else
  self:hideAllWorkspots()
 end
end
return logic
'@

Write-ModFile 'sitAnywhere' 'init.lua' @'
local Cron=require("modules/external/Cron")
local interaction={hubShown=false,update=function() end}
local world=require("modules/worldInteraction")
local Logic=require("modules/logic")
local self={runtimeData={inMenu=false,inGame=true,forceScan=false},logic=Logic:new(nil),yaw=0,pitch=0}
self.logic.mod=self
self.logic.sittables={
 [0]={workspot={enableCamera=false,camTransition=false,slide=false},update=function() end}
}
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
local ui={update=function() end}
local state={showing=false,atEntrance=false,atEntrance2=false,atShopExit=false,phase="root"}
local ENTRANCE_POS={x=0,y=0,z=0}
local ENTRANCE_POS_2={x=10,y=0,z=0}
local SELLER_POS={x=10,y=2,z=0}
local SHOP_EXIT_POS={x=10,y=4,z=0}
local OPEN_RADIUS_M,CLOSE_RADIUS_M=4.0,5.5
local CATALOG_DIR="cars"
local activeSpawns={}
local toggleSystem={active=false,currentIteration=0}
local playerVehicleState={wasInVehicle=false,currentVehicle=nil,lastCheckedVehicle=nil,monitoringActive=false}
local function isNear(pos,tgt,r) return false end
local function hideHub() state.showing=false end
local function showEntranceHub() state.showing=true;state.atEntrance=true end
local function showEntrance2Hub() state.showing=true;state.atEntrance2=true end
local function showShopExitHub() state.showing=true;state.atShopExit=true end
local function showRootHub() state.showing=true;state.phase="root" end
local function showBuyHub() state.showing=true;state.phase="buy" end
local function despawnAll() end
local function executeToggleCommand() end
local function checkPlayerVehicleEntry()
 if toggleSystem.active then executeToggleCommand() end
 local player=Game.GetPlayer();if not player then return end
 local currentVehicle=Game['GetMountedVehicle;GameObject'](player)
 playerVehicleState.wasInVehicle=currentVehicle~=nil
 playerVehicleState.currentVehicle=currentVehicle
 playerVehicleState.lastCheckedVehicle=currentVehicle
end
local function load_catalog_from_json_dir(dir) end
-- --- tiny scheduler ---
local __tasks={}
local function later(delay,fn)
 __tasks[#__tasks+1]={t=os.clock()+(delay or 0),fn=fn}
end
local function runDueTasks()
 local now=os.clock()
 local i=1
 while i<=#__tasks do
  if __tasks[i].t<=now then local task=table.remove(__tasks,i);pcall(task.fn) else i=i+1 end
 end
end
registerForEvent("onInit", function()
 ui.init=function() end
 load_catalog_from_json_dir(CATALOG_DIR)
end)
registerForEvent("onDraw", function() pcall(function() ui.update() end) end)
registerForEvent("onUpdate", function(_)
 runDueTasks()
 local p=Game.GetPlayer();if not p then return end
 local pos=p:GetWorldPosition()
 checkPlayerVehicleEntry()
 for i=#activeSpawns,1,-1 do
  local spawn=activeSpawns[i]
  if type(spawn)=="table" and spawn.type=="timer" and spawn.check then
   if spawn.check() then table.remove(activeSpawns,i) end
  end
 end
 local nearEntrance=isNear(pos,ENTRANCE_POS,OPEN_RADIUS_M)
 local farFromEntrance=not isNear(pos,ENTRANCE_POS,CLOSE_RADIUS_M)
 local nearEntrance2=isNear(pos,ENTRANCE_POS_2,OPEN_RADIUS_M)
 local farFromEntrance2=not isNear(pos,ENTRANCE_POS_2,CLOSE_RADIUS_M)
 local nearSeller=isNear(pos,SELLER_POS,OPEN_RADIUS_M)
 local farFromSeller=not isNear(pos,SELLER_POS,CLOSE_RADIUS_M)
 local nearShopExit=isNear(pos,SHOP_EXIT_POS,OPEN_RADIUS_M)
 local farFromShopExit=not isNear(pos,SHOP_EXIT_POS,CLOSE_RADIUS_M)
 if nearEntrance and not state.showing and not state.atEntrance then showEntranceHub()
 elseif state.atEntrance and farFromEntrance then hideHub();state.atEntrance=false
 elseif nearEntrance2 and not state.showing and not state.atEntrance2 then showEntrance2Hub()
 elseif state.atEntrance2 and farFromEntrance2 then hideHub();state.atEntrance2=false
 elseif nearShopExit and not state.showing and not state.atShopExit then showShopExitHub()
 elseif state.atShopExit and farFromShopExit then hideHub();state.atShopExit=false
 elseif state.phase=="root" and not state.showing and nearSeller then showRootHub()
 elseif state.showing and farFromSeller then hideHub()
 elseif not state.showing and nearSeller and state.phase~="root" then showBuyHub()
 end
end)
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


Write-ModFile 'ImmersiveFirstPerson' 'init.lua' @'
local Helpers=require("Modules/Helpers")
local CameraCore={Update=function() end}
local cachedPlayerState={}
local function collectPlayerState()
    return Helpers.RefreshPlayerState(cachedPlayerState)
end
registerForEvent("onUpdate", function(delta)
        local isPaused=false
        local playerState = isPaused and nil or collectPlayerState()
        CameraCore.Update(delta, playerState)
end)
'@

Write-ModFile 'ImmersiveFirstPerson' 'Modules/Helpers.lua' @'
local Helpers={}
local session = {
    player = nil,
    blackboard = nil,
    definition = nil,
    workspotSystem = nil,
    transactionSystem = nil,
    weaponSlot = nil,
}
local function resetSession()
    session.player = nil
    session.weaponSlot = nil
end
local function refreshPlayerState(target)
    local player=Game.GetPlayer()
    local vehicleState=0
    local detailedLocomotion=0
    local landingState=0
    local inspectionComponent = player:GetInspectionComponent()
    local playerState = player:GetPS()
    local mountedVehicle = vehicleState == 0
        and Game['GetMountedVehicle;GameObject'](player)
        or nil
    local knockedDown = detailedLocomotion == 29
        or detailedLocomotion == 31
        or landingState > 1
        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, "VehicleKnockdown")
        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, "BikeKnockdown")
    target.inVehicle = vehicleState ~= 0 or mountedVehicle ~= nil
    target.knockedDown = knockedDown
    target.inspecting = inspectionComponent ~= nil
        and inspectionComponent:GetIsPlayerInspecting() == true
    target.inWorkspot = session.workspotSystem ~= nil
        and session.workspotSystem:IsActorInWorkspot(player) == true
    target.crouching = playerState ~= nil and playerState:IsCrouch() == true
    return target
end
function Helpers.RefreshPlayerState(target)
    target = target or {}
    local ok, refreshed = pcall(refreshPlayerState, target)
    return ok and refreshed or target
end
return Helpers
'@

Write-ModFile 'nativeInteractions' 'init.lua' @'
local Cron={Update=function() end}
local manager=require("modules/projectsManager")
local world=require("modules/utils/worldInteraction")
local resourceHelper={sceneQueue={},onUpdate=function() end}
local self={runtimeData={inGame=true,inMenu=false}}
registerForEvent("onUpdate", function (dt)
        if self.runtimeData.inGame and not self.runtimeData.inMenu then
            Cron.Update(dt)
            manager.update()
            world.update()
            resourceHelper.onUpdate()
        end
end)
'@

Write-ModFile 'nativeInteractions' 'modules/projectsManager.lua' @'
local manager={projects={},updateList={}}
local function rebuild()
 for _, project in pairs(manager.projects) do
  if project.enabled then
   for _, interaction in pairs(project.interactions) do
    if interaction.needsUpdate then table.insert(manager.updateList,interaction) end
   end
  end
 end
end
local playerPosition = { x = 0, y = 0, z = 0 }

function manager.update()
 for i=1,#manager.updateList do
  local interaction=manager.updateList[i]
 end
end
return manager
'@

Write-ModFile 'nativeInteractions' 'modules/utils/worldInteraction.lua' @'
local ref={Weak=function(x)return x end}
local world = {
    interactions = {},
    searchGrid = {},
    interactionCounter = 0,
    activeInteractions = {},
    pinnedInteractions = {},
    cellSize = 12
}
local function getGridKey(position)
    return tostring(position.x) .. "_" .. tostring(position.y)
end
function world.getGridInteractions(origin, singleCell, roundRobin) return {} end
function world.removeInteraction(key)
    if world.interactions[key] then
        local data = world.interactions[key]
        if world.interactions[key].pinID then
            Game.GetMappinSystem():UnregisterMappin(world.interactions[key].pinID)
        end
        world.pinnedInteractions[data] = nil
    end
end
function world.init()
    local _ = "WorldMappinUIProfile.nif"
    ObserveAfter("BaseMappinBaseController", "UpdateRootState", function(this)
        local mappin = this:GetMappin()
        if not mappin or this:GetProfile():GetID().value ~= "WorldMappinUIProfile.nif" then return end
        local pos = mappin:GetWorldPosition()
        for _, interaction in pairs(world.getGridInteractions(pos, true)) do
            if interaction.pinID and interaction.pinID.value == this:GetMappin():GetNewMappinID().value then
                local record = TweakDBInterface.GetUIIconRecord(interaction.icon)
                this.iconWidget:SetAtlasResource(record:AtlasResourcePath())
                this.iconWidget:SetTexturePart(record:AtlasPartName())
                if interaction.iconColor then
                    this.iconWidget:SetTintColor(HDRColor.new(interaction.iconColor))
                else
                    this.iconWidget.widget:BindProperty("tintColor", "MainColors.Blue")
                end
                interaction.pinController = ref.Weak(this)
                return
            end
        end
    end)
    Override("NativeInteractions", "IsCustomMappin", function (_, mappin)
        if mappin then
            local pos = mappin:GetWorldPosition()
            for _, interaction in pairs(world.getGridInteractions(pos, true)) do
                if interaction.pinID and interaction.pinID.value == mappin:GetNewMappinID().value then
                    return true
                end
            end
        end

        return false
    end)
end
function world.forceIcons()
    for interaction, _ in pairs(world.pinnedInteractions) do
        if interaction.pinID then
            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)
            local data = MappinData.new({})
            interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)
        end
    end
end

function world.togglePin(interaction, state)
    if not interaction.icon then return end
    if not state and interaction.pinID then
        Game.GetMappinSystem():UnregisterMappin(interaction.pinID)
        interaction.pinID = nil
        interaction.pinController = nil
        world.pinnedInteractions[interaction] = nil
    elseif not interaction.pinID and state then
        local data = MappinData.new({})
        interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)
        world.pinnedInteractions[interaction] = true
    end
end
function world.onSessionStart() -- Save loaded, all pins are gone
    world.activeInteractions = {}
    world.pinnedInteractions = {}
end
function world.shutdown()
    for _, interaction in pairs(world.interactions) do
        if interaction.pinID then
            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)
        end
    end
end

return world
'@

Write-ModFile 'Minimap Widgets' 'init.lua' @'
local MinimapWidgetsConfig={
 FPS=true,CoordInterv=1,ShowElevationArrow=true,
 ShowEnemies=true,ShowNCPD=true,ShowLoot=true,ShowDevices=true}
local elevationEnabled=MinimapWidgetsConfig.ShowElevationArrow
local shouldCountFPS=true
local shouldForceUpdate=true
local isGameLoading=false
local isPreGameState=false
local isInitialized=true
local timerFast,timerSlow,timerCoord=0,0,0
local function updateFpsCounter() end
local function updateFastWidgets() end
local function updateSlowWidgets() end
local function updateCoordinatesOnly() end
registerForEvent("onUpdate", function(deltaTime)
    -- EMERGENCY STOP:
    if isGameLoading or isPreGameState or not isInitialized then return end
    if not shouldForceUpdate then return end

    -- FIREWALL
    local player = Game.GetPlayer()
    if not player or not IsDefined(player) then return end
    if not Game.GetTimeSystem() or not Game.GetCameraSystem() or not Game.GetTransactionSystem() then return end
    if not Game.GetScriptableSystemsContainer() then return end

    -- 1. FPS
    if MinimapWidgetsConfig.FPS == true and shouldCountFPS == true then
        updateFpsCounter()
    end

    -- 2. Fast Widgets (0.2s)
    timerFast = timerFast + deltaTime
    if timerFast >= 0.2 then
        updateFastWidgets()
        timerFast = 0
    end
    timerSlow = timerSlow + deltaTime
    if timerSlow >= 1.0 then
        updateSlowWidgets()
        timerSlow = 0
    end
    timerCoord = timerCoord + deltaTime
    if timerCoord >= MinimapWidgetsConfig.CoordInterv then
        updateCoordinatesOnly()
        timerCoord = 0
    end
end)

ObserveAfter("MinimapStealthMappinController", "UpdateAboveBelowVerticalRelation", function(this)
			local vertRelation = this:GetVerticalRelationToPlayer()
			local shouldShow = this:GetRootWidget():IsVisible() and not this:IsClamped()
			local isAbove = vertRelation == gamemappinsVerticalPositioning.Above
			local isBelow = vertRelation == gamemappinsVerticalPositioning.Below
			if this:IsClamped() then 
				if this.aboveWidget then this.aboveWidget:SetVisible(false) end
			else 
				if this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end
				if this.belowWidget then this.belowWidget:SetVisible(isBelow) end
			end
end)

ObserveAfter("MinimapStealthMappinController", "Intro", function(this)
		if not IsDefined(this) then return end
		local attitude = this.stealthMappin:GetAttitudeTowardsPlayer()
end)
ObserveAfter("MinimapStealthMappinController", "Update", function(this)
		if not IsDefined(this) then return end
		local attitude = this.stealthMappin:GetAttitudeTowardsPlayer()
end)
'@

$autoloot=Join-Path $mods 'AutoLoot\init.lua'
$blm=Join-Path $mods 'BetterLootMarkers\init.lua'
$drive=Join-Path $mods 'DriveBus\Modules\core.lua'
$qtt=Join-Path $mods 'QuestTrackingToggle\init.lua'
$sit=Join-Path $mods 'sitAnywhere\init.lua'
$rica=Join-Path $mods 'repeatable_increased_criminal_activity\init.lua'
$dedka=Join-Path $mods 'Dedka Auto Shop\init.lua'
$bank=Join-Path $mods 'marmurbank\external\InteractionUI.lua'
$itp=Join-Path $mods 'immersive_third_person\init.lua'
$ifp=Join-Path $mods 'ImmersiveFirstPerson\init.lua'
$nif=Join-Path $mods 'nativeInteractions\init.lua'
$minimap=Join-Path $mods 'Minimap Widgets\init.lua'
$handoff=@{schemaVersion='1.9';callbacks=@(
 (Row 1 'DriveBus' 'Observe' 'PlayerPuppet::OnAction' 'Modules/core.lua' (Find-CallbackRange $drive 'Observe\("PlayerPuppet"') 17.82 1485),
 (Row 2 'QuestTrackingToggle' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' (Find-CallbackRange $qtt "Observe\('PlayerPuppet'") 11.65 1485),
 (Row 3 'sitAnywhere' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $sit 'registerForEvent\("onUpdate"') 7.81 60),
 (Row 4 'repeatable_increased_criminal_activity' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $rica 'registerForEvent\("onUpdate"') 4.72 60),
 (Row 5 'Dedka Auto Shop' 'event' 'onDraw' 'init.lua' (Find-CallbackRange $dedka 'registerForEvent\("onDraw"') 3.34 60),
 (Row 6 'marmurbank' 'Observe' 'PlayerPuppet::OnAction' 'external/InteractionUI.lua' (Find-CallbackRange $bank "Observe\('PlayerPuppet'") 23.30 1485),
 (Row 7 'immersive_third_person' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $itp "registerForEvent\('onUpdate'") 26.34 60),
 (Row 8 'ImmersiveFirstPerson' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $ifp 'registerForEvent\("onUpdate"') 11.24 60),
 (Row 9 'nativeInteractions' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $nif 'registerForEvent\("onUpdate"') 16.54 60),
 (Row 10 'Minimap Widgets' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $minimap 'registerForEvent\("onUpdate"') 8.23 60),
 (Row 11 'AutoLoot' 'Observe' 'PlayerPuppet::OnAction' 'init.lua' (Find-CallbackRange $autoloot "Observe\('PlayerPuppet'") 4.10 600),
 (Row 12 'BetterLootMarkers' 'event' 'onUpdate' 'init.lua' (Find-CallbackRange $blm 'registerForEvent\("onUpdate"') 3.20 60)
);optimizerEvidence=@()}|ConvertTo-Json -Depth 30
$handoff|Set-Content (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved=(& $resolverExe --capture $capture --mods $mods --generate-pass --json|ConvertFrom-Json)
if(!$resolved.ok -or $null-eq$resolved.pass){throw 'Resolver-accuracy semantic pass generation failed.'}
$resolver=Get-Content (Join-Path $capture 'G-CET_Resolver.json') -Raw|ConvertFrom-Json
$rules=@('drivebus','quest-tracking-toggle','sitanywhere','repeatable-increased-criminal-activity','dedka-auto-shop','marmurbank','immersive-third-person','immersivefirstperson','nativeinteractions','minimap-widgets','autoloot','better-loot-markers')
foreach($rule in $rules){
 $m=@();foreach($family in @($resolver.callbackFamilies)){$m+=@($family.topConsumers|Where-Object{$_.semantic.RuleId-eq$rule})}
 if($m.Count-lt1 -or @($m|Where-Object{$_.semantic.SourceProofSatisfied}).Count-lt1){
 $proof=@($m|ForEach-Object{@{owner=$_.owner;matched=$_.semantic.MatchedAnchors;missing=$_.semantic.MissingAnchors;graph=$_.semantic.Graph}})
 throw "Semantic source proof failed: $rule; $($proof|ConvertTo-Json -Depth 8 -Compress)"
}
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

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try{
 function Z($n){$e=$zip.GetEntry($n);if(!$e){throw "ZIP entry missing: $n"};$r=[IO.StreamReader]::new($e.Open());try{$r.ReadToEnd()}finally{$r.Dispose()}}
 $b='bin/x64/plugins/cyber_engine_tweaks/mods/'
 $x=Z($b+'DriveBus/Modules/core.lua');foreach($t in @('gcetDriveBusOnAction','SubscribeAction','QuickExit','ChoiceScrollDown')){if($x-notmatch[regex]::Escape($t)){throw "DriveBus missing $t"}}
 $x=Z($b+'DriveBus/External/Cron.lua');if($x-notmatch'function Cron\.HasActiveTimers\(\)'){throw 'DriveBus active-timer probe missing.'}
 $x=Z($b+'DriveBus/init.lua');if($x-notmatch'if Cron\.HasActiveTimers\(\) then Cron\.Update\(delta\) end'){throw 'DriveBus idle Cron gate missing.'}
 $x=Z($b+'AutoLoot/init.lua');foreach($t in @('gcetAutoLootOnAction','SubscribeAction','UI_DPadWeapons')){if($x-notmatch[regex]::Escape($t)){throw "AutoLoot missing $t"}}
 $x=Z($b+'BetterLootMarkers/init.lua');if($x-notmatch'if BetterLootMarkers\.Settings\.immersiveMode then' -or $x-notmatch'BetterLootMarkers\.ImmersiveMode\.Tick\(dt\)'){throw 'BetterLootMarkers off-state gate incomplete.'}
 $x=Z($b+'QuestTrackingToggle/init.lua');foreach($t in @('questTrackingOnAction','SubscribeAction','ToggleSprint','world_map_menu_track_waypoint')){if($x-notmatch[regex]::Escape($t)){throw "QTT missing $t"}}
 $x=Z($b+'sitAnywhere/init.lua');if($x-notmatch'Cron\.HasActiveTimers\(\)' -or $x-notmatch'world\.hasVisibleState\(\)' -or $x-notmatch'if interaction\.hubShown then interaction\.update\(\) end'){throw 'sitAnywhere hard dormancy incomplete.'}
 $x=Z($b+'sitAnywhere/modules/external/Cron.lua');if($x-notmatch'function Cron\.HasActiveTimers\(\)'){throw 'sitAnywhere timer wake probe missing.'}
 $x=Z($b+'sitAnywhere/modules/worldInteraction.lua');if($x-notmatch'function world\.hasVisibleState\(\)'){throw 'sitAnywhere visible-world wake probe missing.'}
 $x=Z($b+'sitAnywhere/modules/logic.lua');if($x-notmatch'workspotsHidden' -or $x-notmatch'if self\.workspotsHidden then return end'){throw 'sitAnywhere parked-workspot state missing.'}
 $x=Z($b+'repeatable_increased_criminal_activity/init.lua');if($x-notmatch'diagnosticsElapsed' -or $x-notmatch'runtimeTick\(includeDiagnostics\)' -or $x-notmatch'__gcetRunDiagnostics = Mod\.diagnosticsElapsed >= 5\.0'){throw 'RICA split incomplete.'}
 $x=Z($b+'Dedka Auto Shop/init.lua');foreach($t in @('__gcetDedkaSemanticReady','RegisterZone','SetInterval(0.2','VehicleMount','VehicleUnmount','PlayerInvalidated','if not __gcetDedkaNearby then return end','if state.showing then')){if($x-notmatch[regex]::Escape($t)){throw "Dedka semantic missing $t"}}
 $x=Z($b+'marmurbank/external/InteractionUI.lua');if($x-notmatch'__gcetMarmurActionRelevant' -or $x-notmatch'WORLD_INTERACTION_ACTIONS\[name\] == true' -or $x-notmatch'wrapped\(action, wrappedConsumer\)'){throw 'MarmurBank prefilter incomplete.'}
 $x=Z($b+'immersive_third_person/init.lua');if($x-notmatch'__gcetItppSupervisorElapsed' -or $x-notmatch'__gcetItppMaintenanceElapsed' -or $x-notmatch'if state\.enabled or state\.cameraTransition then' -or $x-notmatch'if state\.pendingFppCleanup then' -or $x-notmatch'pcall\(mod\.fallCommitTick, delta\)'){throw 'ITP split incomplete.'}
 $x=Z($b+'ImmersiveFirstPerson/Modules/Helpers.lua');if($x-notmatch'slowProbeElapsed' -or $x-notmatch'session\.slowProbe\.inWorkspot' -or $x-notmatch'Helpers\.RefreshPlayerState\(target, delta\)'){throw 'ImmersiveFirstPerson slow probe incomplete.'}
 $x=Z($b+'ImmersiveFirstPerson/init.lua');if($x-notmatch'collectPlayerState\(delta\)'){throw 'ImmersiveFirstPerson delta propagation incomplete.'}
 $x=Z($b+'nativeInteractions/init.lua');if($x-notmatch'__gcetNifIdleWorldInterval' -or $x-notmatch'manager\.hasRealtimeWork'){throw 'nativeInteractions cadence split incomplete.'}
 $x=Z($b+'nativeInteractions/modules/projectsManager.lua');if($x-notmatch'function manager\.hasRealtimeWork\(\)'){throw 'nativeInteractions realtime sentinel missing.'}
 $x=Z($b+'nativeInteractions/modules/utils/worldInteraction.lua');if($x-notmatch'pinInteractions' -or $x-notmatch'findPinInteraction' -or $x-notmatch'indexPin\(interaction\)'){throw 'nativeInteractions pin index incomplete.'}
 $x=Z($b+'Minimap Widgets/init.lua');if($x-notmatch'__gcetMinimapFastDue' -or $x-notmatch'local isClamped = this:IsClamped\(\)' -or $x-notmatch'local hideEnemies = not MinimapWidgetsConfig\.ShowEnemies'){throw 'Minimap Widgets semantic optimization incomplete.'}
}finally{$zip.Dispose()}
Write-Host 'Resolver-accuracy semantic composition contract passed.'
