param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-seven-native-location-source'
if(Test-Path $root){Remove-Item $root -Force -Recurse}
New-Item -ItemType Directory -Force $root | Out-Null
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$type=$asm.GetType('GCETRuntimeProfiler.Core.Services.NativeLocationAdditionalOwnersSource',$true)
$four=$asm.GetType('GCETRuntimeProfiler.Core.Services.NativeLocationFourOwnersSource',$true)
$contextType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($contextType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic') | Where-Object {$_.GetParameters().Count -eq 4})[0]
if(!$ctor){throw 'SemanticPatchContext 4-argument source constructor missing'}
function Apply([string]$owner,[string]$relative,[string]$rule,[string]$method,[string]$fixture,[string[]]$need,[bool]$crlf=$false) {
  $mod=Join-Path $root $owner
  $path=Join-Path $mod ($relative.Replace('/',[IO.Path]::DirectorySeparatorChar))
  $parent=Split-Path $path -Parent
  New-Item -ItemType Directory -Force $parent | Out-Null
  if($crlf){$fixture=$fixture.Replace("`r`n","`n").Replace("`n","`r`n")}
  [IO.File]::WriteAllText($path,$fixture,[Text.UTF8Encoding]::new($false))
  $staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
  try {
    $args4=[object[]]@([string]$root,[string]$owner,[string]$rule,$staged)
    $ctx=$ctor.Invoke($args4)
  } catch {
    $types=(@($args4) | ForEach-Object {$_.GetType().FullName}) -join ', '
    throw "Constructing $owner native source context failed ($types): $($_.Exception.InnerException.Message)"
  }
  $targetType=if($method -in @('ApplyIllegalV3','ApplyPoker','ApplyDedka')){$four}else{$type}
  $m=$targetType.GetMethod($method,$flags)
  if(!$m){throw "Native location injector missing: $method"}
  try {$result=$m.Invoke($null,[object[]]@($ctx))}
  catch{throw "Native location $owner rejected current-author source: $($_.Exception.InnerException.Message)"}
  if(!$result.Applied){throw "Native $owner source refused its first pass"}
  $rows=@($contextType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  if($rows.Count -ne 1){throw "Native $owner did not stage exactly one source file"}
  $output=[Text.Encoding]::UTF8.GetString($rows[0].Bytes)
  foreach($part in $need){if(!$output.Contains($part)){throw "$owner native stage omitted: $part"}}
  if($crlf -and !$output.Contains("`r`n")){throw "$owner lost CRLF source format"}
  [IO.File]::WriteAllText($path,$output,[Text.UTF8Encoding]::new($false))
  $againArgs=[object[]]@([string]$root,[string]$owner,[string]$rule,[Collections.Generic.Dictionary[string,byte[]]]::new())
  try {$again=$ctor.Invoke($againArgs)}
  catch {throw "Repeated $owner native context constructor failed: $($_.Exception.InnerException.Message)"}
  try {$done=$m.Invoke($null,[object[]]@($again))}
  catch {throw "Repeated $owner native source injection failed: $($_.Exception.InnerException.Message)"}
  if($done.Applied){throw "Native $owner stage was not idempotent"}
  return $output
}
$card=@'
local spatialHandle
local engine=GetMod("0-Engine")
local Mod=engine.Register("CardCollection")
local function buildSpatialEntries()
 return {{x=1,y=2,z=3,id="card1"}}
end
local function collectCard(entry)
 Engine.RemoveSpatialEntry("card_collection",entry)
end
local function registerSpatialSet()
  if spatialHandle then return end
  local entries=buildSpatialEntries()
  spatialHandle = Mod.RegisterSpatialSet("card_collection", entries, {
    gridSize=100,pollRadius=2.0,pollThrottle=1.0,
    onEnter=function(entry,distSq) collectCard(entry) end,
  })
end
local function unregisterSpatialSet()
 if spatialHandle then spatialHandle:unregister();spatialHandle=nil end
end
registerForEvent("onInit", function()
  if engine.IsPlaying() then registerSpatialSet() end
  Mod.Subscribe("PlayerReady", registerSpatialSet)
  Mod.Subscribe("PlayerInvalidated", unregisterSpatialSet)
end)
'@
$c=Apply 'Card Collection' 'init.lua' 'native-location-card-collection' 'ApplyCards' $card @('G-CET native location:card-collection-v1','lookup:attachCompiled(10035,100','unregisterSpatialSet()','__gcetCardNativeFar')
$marmur=@'
local PosData={All={{x=1,y=2,z=3}}}
bank={}
function bank:registerSpatialSet()
	if not self.engine then return end
	if self.spatialHandle then
		self.spatialHandle:unregister()
		self.spatialHandle = nil
	end
	for _, loc in ipairs(PosData.All) do
		loc.radiusSq = self.bankerDistanceSq
	end
	self.spatialHandle = self.engine.RegisterSpatialSet("marmurbank", PosData.All, {
		gridSize = 100,
	})
	if self.spatialHandle then
		self.engine.Log("Marmurbank",
			string.format("Registered %d banker locations with 0-Engine SpatialHash", self.spatialHandle:count()), "info")
	end
end
function bank:distanceListener()
	if not self.engine then return 0 end
	local entry,distSq=self.engine.QueryNearest("marmurbank",25*25)
	self.nearestLocation=entry
	return entry and 1 or 0
end
'@
$m=Apply 'marmurbank' 'module/Bank.lua' 'native-location-marmurbank' 'ApplyMarmur' $marmur @('G-CET native location:marmurbank-v1','lookup:attachCompiled(10036,#PosData.All','self.spatialHandle:unregister()','if self.__gcetNativeFar then self.nearestLocation=nil;return 0 end') $true
$bath=@'
local ui=require("interactionUI")
local CONFIG={position={x=1,y=2,z=3},iconRange=6.0}
local bathtub={hubVisible=false,pinID=nil,waterApplied=false}
local function buildHub() end
local function togglePin(near) end
function bathtub.init()
    ui.init()
end
function bathtub.update(player, pPos)
    -- G-CET semantic:yaiba-penthouse-distant-bathtub
    -- Only skip when no bath UI, mappin or water reapply is pending.
    if player and pPos and not bathtub.hubVisible and not ui.hubShown and not bathtub.pinID and not bathtub.waterApplied then
        local __gcetBathPos = CONFIG.position
        local __gcetDx = pPos.x - __gcetBathPos.x
        local __gcetDy = pPos.y - __gcetBathPos.y
        local __gcetDz = pPos.z + 1.0 - __gcetBathPos.z
        if __gcetDx * __gcetDx + __gcetDy * __gcetDy + __gcetDz * __gcetDz > (CONFIG.iconRange + 1.0)^2 then return end
    end
    ui.update()
    togglePin(near)
end
'@
$y=Apply 'yaibaPenthouse' 'bathtub.lua' 'native-location-yaiba-bathtub' 'ApplyBathtub' $bath @('G-CET native location:yaiba-bathtub-v1','lookup:attachCompiled(10037,1','if __gcetBathNativeFar == true then return end','if __gcetBathNativeFar == nil then')

$ziggy=@'
local C={giver={position={1,2,3}}}
local H={s={phase='offered'},generation=1,playerKey='abc',hooks=nil}
local Dialogue={}
local function valid(x) return x~=nil end
registerForEvent('onInit',function()H.hooks=NewObject('ZG11Hooks');H.hooks:Register();print('ready')end)
local __gcetZiggyOfferedFarAcc=0.0
local function __gcetZiggyOfferedFar()
 if not H.s or H.s.phase~='offered' or not valid(H.hooks) then return false end
 if not H.hooks:SessionLoaded() or H.generation~=H.hooks.generation then return false end
 if Dialogue.busy(H.dialogue) then return false end
 local p=Game.GetPlayer()
 if not valid(p) or not p:IsAttached() then return false end
 if tostring(p:GetEntityID().hash)~=H.playerKey then return false end
 local pos=p:GetWorldPosition()
 local loc=C.giver and C.giver.position
 if not pos or not loc then return false end
 local dx=pos.x-loc[1];local dy=pos.y-loc[2];local dz=pos.z-loc[3]
 return dx*dx+dy*dy+dz*dz>130*130
end
__gcetRegisterEvent_1061('onUpdate',function(dt)
 local __gcetProbeOK,__gcetFar=false,false
 if H.s and H.s.phase=='offered' then
  __gcetProbeOK,__gcetFar=pcall(__gcetZiggyOfferedFar)
 end
 if __gcetProbeOK and __gcetFar then
  __gcetZiggyOfferedFarAcc=__gcetZiggyOfferedFarAcc+(dt or 0)
  if __gcetZiggyOfferedFarAcc < 1.0 then return end
  dt=__gcetZiggyOfferedFarAcc
  __gcetZiggyOfferedFarAcc=0
 end
end)
'@
$z=Apply 'ziggy_last_play' 'init.lua' 'native-location-ziggy-offered' 'ApplyZiggy' $ziggy @(
 'G-CET native location:ziggy-offered-v1',
 'lookup:attachCompiled(10038,1',
 'if __gcetZiggyNativeOfferedFar ~= nil then return __gcetZiggyNativeOfferedFar end',
 '__gcetZiggyNativeOfferedFar=nil;return true end',
 'if Dialogue.busy(H.dialogue) then return false end',
 'if tostring(p:GetEntityID().hash)~=H.playerKey then return false end',
 'local pos=p:GetWorldPosition()',
 'if __gcetZiggyOfferedFarAcc < 1.0 then return end'
) $true
Write-Host 'Ziggy offered source-native 130m: same author guards and 1Hz cadence, UNKNOWN fallback, CRLF and idempotence PASS'


$mechanic=@'
local cfg={spawnDistance=75.0,despawnDistance=95.0,updateInterval=0.5}
local state={mechanics={}}
local function loadMatrix()
 local system=getSystem()
 local count=system:GetMechanicCountForCET()
 local result={}
 for index=0,count-1 do
  result[#result+1]={index=index,mechanicID=system:GetMechanicIDAtForCET(index),x=1,y=2,z=3}
 end
  state.mechanics = result
 return true
end
local function getPlayer() return Game.GetPlayer() end
local function resolvePendingSpawn(entry) end
local function distanceFromPlayer(entry,player) return 0 end
function PhysicalNpc.Update(delta)
  if #state.mechanics == 0 and not loadMatrix() then return end
  local player = getPlayer()
 if player == nil then return end
 for _, entry in ipairs(state.mechanics) do
    if entry.spawnPending then
      resolvePendingSpawn(entry)
    else
      local distance = distanceFromPlayer(entry, player)
      if distance ~= nil then
        if entry.entityID == nil and distance <= cfg.spawnDistance then spawnMechanic(entry)
        elseif entry.entityID ~= nil and distance >= cfg.despawnDistance then deleteEntry(entry, "far") end
      end
    end
 end
end
'@
$il=Apply 'illegal_mechanic_phase1a' 'physical_npc.lua' 'native-location-illegal' 'ApplyIllegalV3' $mechanic @(
  'G-CET native location:illegal-v3',
  'host:attachCompiled(1003301+index,1,',
  'entry.__gcetNativeFar=true;state.nextUpdateAt=0',
  'if not __gcetAnySiteActive then return end',
  'entry.__gcetNativeFar == true and not entry.spawnPending',
  'distance <= cfg.spawnDistance',
  'distance >= cfg.despawnDistance'
)

# PASS11 partially optimized source has one owner gating every mechanic.
# Remove ONLY that proven aggregate, preserving the previous player-position
# reuse stage and exact 75/95 metre NPC lifecycle.
$legacyAttach=@'
  -- G-CET native location:illegal-v1
  local engineOk,engine=pcall(GetMod,"0-Engine")
  local lookup=engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
  if lookup and type(lookup.attach) == 'function' then
    local sites={}
    for _,entry in ipairs(result) do
      sites[#sites+1]={x=entry.x,y=entry.y,z=entry.z,radius=100,metric="xyz"}
    end
    lookup:attach(10033,sites,
      function() state.__gcetNativeFar=false;state.nextUpdateAt=0 end,
      function() state.__gcetNativeFar=true;state.nextUpdateAt=0 end,
      function() state.__gcetNativeFar=false;state.nextUpdateAt=0;return true end)
  end  log("matrix loaded")
'@
$legacyGate=@'
  if state.__gcetNativeFar then
    local hasLiveOrPending=false
    for _,entry in ipairs(state.mechanics) do
      if entry.spawnPending or entry.entityID ~= nil or entry.entity ~= nil then
        hasLiveOrPending=true;break
      end
    end
    if not hasLiveOrPending then return end
  end  local player = getPlayer()
'@
$nl=[Environment]::NewLine
$legacy=$mechanic.Replace('  state.mechanics = result',
  '  state.mechanics = result'+$nl+$legacyAttach)
$legacy=$legacy.Replace('  local player = getPlayer()',$legacyGate)
$legacy=$legacy.Replace('      local distance = distanceFromPlayer(entry, player)',
  '      local distance, __gcetPositionObserved = distanceFromPlayer(entry, player, __gcetMechanicPlayerPosition)')
$legacy=$legacy.Replace('  for _, entry in ipairs(state.mechanics) do',
  '  local __gcetMechanicPlayerPosition=nil'+$nl+'  for _, entry in ipairs(state.mechanics) do')
$legacyResult=Apply 'illegal_mechanic_phase1a' 'physical_npc.lua' 'native-location-illegal' 'ApplyIllegalV3' $legacy @(
 'G-CET native location:illegal-v3',
 'host:attachCompiled(1003301+index,1,',
 '__gcetMechanicPlayerPosition',
 'entry.__gcetNativeFar == true and not entry.spawnPending',
 'distance <= cfg.spawnDistance',
 'distance >= cfg.despawnDistance'
)
if($legacyResult.Contains('lookup:attach(10033,sites') -or
   $legacyResult.Contains('state.__gcetNativeFar')){
 throw 'PASS11 legacy aggregate owner was not fully retired'
}
Write-Host 'PASS11 aggregate -> independent native owner migration with prior position reuse PASS'

Write-Host 'Illegal Mechanic per-site compiled owner binding, individual FAR/NEAR wake, existing exact NPC streaming, and idempotence PASS'


# Prove source-owned compilation of two independent native owners from the
# actual author REDscript registry structure, not runtime coordinate guesses.
$game=Join-Path $env:RUNNER_TEMP 'gcet-mechanic-gcas-provenance'
$modsRoot=Join-Path $game 'bin/x64/plugins/cyber_engine_tweaks/mods'
$mechRoot=Join-Path $modsRoot 'illegal_mechanic_phase1a'
$redsRoot=Join-Path $game 'r6/scripts/zzz_illegal_mechanic_phase1a'
New-Item -ItemType Directory -Force $mechRoot,$redsRoot | Out-Null
[IO.File]::WriteAllText((Join-Path $mechRoot 'physical_npc.lua'),@'
-- G-CET native location:illegal-v3
local function attachMechanic(lookup,index,near,far,fallback)
 return lookup:attachCompiled(1003301+index,1,near,far,fallback)
end
'@)
$reds=@'
public final class IllegalMechanicRegistry extends IScriptable {
 public static func Build(out result: array<ref<IllegalMechanicDefinition>>) -> Void {
 let data: ref<IllegalMechanicDefinition>;
 ArrayClear(result);
 data = new IllegalMechanicDefinition();
 data.mechanicID = n"first_mechanic";
 data.npcPosition = new Vector4(10.000, 20.000, 30.000, 1.000);
 data.discoveryShape = EIllegalMechanicDiscoveryShape.RADIAL;
 ArrayPush(data.discoveryVertices, new Vector4(20.000, 25.000, 30.000, 1.000));
 data.discoveryLowestPointZ = 30.000;
 data.discoveryHighestPointZ = 30.000;
 data.discoveryZTolerance = 1.25;
 ArrayPush(result, data);
 data = new IllegalMechanicDefinition();
 data.mechanicID = n"second_mechanic";
 data.npcPosition = new Vector4(300.000, 400.000, 50.000, 1.000);
 data.discoveryShape = EIllegalMechanicDiscoveryShape.POLYGON;
 ArrayPush(data.discoveryVertices, new Vector4(390.000, 400.000, 50.000, 1.000));
 ArrayPush(data.discoveryVertices, new Vector4(300.000, 400.000, 50.000, 1.000));
 ArrayPush(data.discoveryVertices, new Vector4(320.000, 410.000, 50.000, 1.000));
 data.discoveryLowestPointZ = 50.000;
 data.discoveryHighestPointZ = 50.000;
 data.discoveryZTolerance = 1.25;
 ArrayPush(result, data);
 }
}
'@
[IO.File]::WriteAllText((Join-Path $redsRoot 'zzz_IllegalMechanicTypes.reds'),$reds)
$compilerType=$asm.GetType('GCETRuntimeProfiler.Core.Services.CompiledNativeLocations',$true)
$compile=$compilerType.GetMethod('Compile',[Reflection.BindingFlags]'Static,NonPublic')
if(!$compile){throw 'Compiled static location manifest compiler missing'}
$map=[Collections.Generic.Dictionary[string,byte[]]]::new()
# Native coordinates are reusable passive resources and must compile
# BEFORE any handpicked semantic attaches. A comment cannot *activate* native
# behavior; merely inventorying valid installed author coordinates is safe.
$unboundMap=[Collections.Generic.Dictionary[string,byte[]]]::new()
$unboundMap.Add('illegal_mechanic_phase1a/physical_npc.lua',
 [Text.Encoding]::UTF8.GetBytes('-- G-CET native location:illegal-v3'))
$unbound=$compile.Invoke($null,[object[]]@([string]$modsRoot,$unboundMap))
$unboundBytes=$compilerType.GetProperty('Bytes',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($unbound)
if([BitConverter]::ToUInt32($unboundBytes,8) -ne 2){
 throw 'Source-proven mechanic coordinates missing before Lua subscription'
}
$map.Add('illegal_mechanic_phase1a/physical_npc.lua',
 [IO.File]::ReadAllBytes((Join-Path $mechRoot 'physical_npc.lua')))
try {$built=$compile.Invoke($null,[object[]]@([string]$modsRoot,$map))}
catch {throw "Native author REDscript compilation failed: $($_.Exception.InnerException.Message)"}
$manifestBytes=$compilerType.GetProperty('Bytes',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($built)
if([Text.Encoding]::ASCII.GetString($manifestBytes,0,4) -ne 'GCAS'){throw 'Not a native GCAS file'}
if([BitConverter]::ToUInt32($manifestBytes,8) -ne 2){throw 'Expected exactly 2 compiled mechanic sites'}
if([BitConverter]::ToUInt32($manifestBytes,16+24) -ne 1003301 -or
   [BitConverter]::ToUInt32($manifestBytes,16+28+24) -ne 1003302){
    throw 'Per-mechanic stable native owner IDs were not preserved'
}
foreach($i in 0..1){
 $radius=[BitConverter]::ToSingle($manifestBytes,16+28*$i+16)
 if($radius -le 95.0 -or $radius -le 90.0){throw 'Native wake must exceed all authored discovery and 95m NPC boundaries'}
}
Write-Host 'Compiled REDscript 2-owner GCAS fixture: independent owner IDs, author positions and >95m discovery-safe wake PASS'


# The Resolver evaluates initial source candidates before any primary semantic
# stages have been staged. Once an existing source is optimized in this SAME
# generation, the reviewed native stage must compose without a second pass.
$followType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPassGeneratorService',$true)
$follow=$followType.GetMethod('ApplyPreparedNativeLocationStages',
    [Reflection.BindingFlags]'Static,NonPublic')
if(!$follow){throw 'Same-pass source-native composition stage absent'}
$followStaged=[Collections.Generic.Dictionary[string,byte[]]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$followStaged.Add('Card Collection/init.lua',[Text.Encoding]::UTF8.GetBytes($card))
$followCounts=[Collections.Generic.Dictionary[string,int]]::new(
    [StringComparer]::OrdinalIgnoreCase)
$followManifest=[Collections.Generic.List[object]]::new()
$followSkipped=[Collections.Generic.List[object]]::new()
try {
 $generated=$follow.Invoke($null,[object[]]@(
   [string]$root,$followStaged,
   $followCounts,$followManifest,$followSkipped))
} catch {throw "Pristine staged source -> native follow-up failed: $($_.Exception.InnerException.Message)"}
if($generated -ne 1){throw "Single-pass pristine staged Card Collection native composition failed: applied=$generated; skipped=$($followSkipped.Count)"}
$composed=[Text.Encoding]::UTF8.GetString($followStaged['Card Collection/init.lua'])
if(!$composed.Contains('lookup:attachCompiled(10035,100') -or
   !$composed.Contains('G-CET native location:card-collection-v1')){
  throw 'The composed source did not gain compiled card owner callbacks'
}
Write-Host 'Same-pass pristine author source -> compiled-native semantic composition PASS'


# Independent, source-structural hook contracts. These intentionally differ
# from older expanded source templates: scheduler variables, helper wrappers
# and logging text may change while the actual lifecycle remains provable.
$pokerSource=@'
local StreetTables={locations={{center={x=1,y=2},floorZ=3}}}
local pokerTableCount=#StreetTables.locations
local __gcetPokerRadius=25.0
local InteractionMenu={visible=false}
local function __gcetPokerReady() return true end
local function __gcetPokerActive() return false end
__gcetPokerAttachPass4=function()
 local engine=GetMod("0-Engine")
 local client=engine.Register("StreetGamesPoker")
 local nearMode=false
 local farWorld,nearWorld,nearSeat=nil,nil,nil
 local function setNear(state)
  state=state==true
  if state==nearMode then return end
  nearMode=state
  if state then
   if farWorld then farWorld.Pause() end
   if nearWorld then nearWorld.Resume() end
   if nearSeat then nearSeat.Resume() end
  else
   if nearWorld then nearWorld.Pause() end
   if nearSeat then nearSeat.Pause() end
   if farWorld then farWorld.Resume() end
  end
 end
 farWorld=client.Schedule.Every(1.0,{id="poker.world_far"},function(ctx)
  if not __gcetPokerReady() then return end
  if StreetTables.update(ctx.elapsed,25.0) then setNear(true) end
 end)
 nearWorld=client.Schedule.Every(0.2,{id="poker.world_near"},function(ctx)
  if StreetTables.update(ctx.elapsed,25.0) then setNear(true) end
 end)
 nearSeat=client.Schedule.Every(0.1,{id="poker.seat_near"},function(ctx)
  if nearMode then SeatController.update(ctx.elapsed) end
 end)
 local realtime=client.Schedule.EveryFrame({id="poker.realtime"},function(ctx)
  local active=__gcetPokerActive()
  if (active or InteractionMenu.visible) and not nearMode then setNear(true) end
 end)
 created[#created+1]=realtime
 __gcetPokerAttached=true
end
'@
$p=Apply 'StreetGamesPoker' 'init.lua' 'native-location-poker' 'ApplyPoker' $pokerSource @(
  'G-CET native location:poker-structural-v1',
  'lookup:attachCompiled(10031,#StreetTables.locations',
  'if __gcetPokerNativeOwned then farWorld.Pause() else farWorld.Resume() end',
  'if __gcetPokerNativeOwned and __gcetPokerNativeNear and not nearMode'
)
$dedkaSource=@'
local ENTRANCE_POS={x=-1677.94,y=160.47,z=7.82}
local ENTRANCE_POS_2={x=606.4076,y=-404.74185,z=6.784172}
local __gcetDedkaEngine=GetMod("0-Engine")
local shopNearCount=0
local function startShopDetail() end
local function stopShopDetail() end
local function registerWakeZone(id,pos)
 __gcetDedkaEngine.RegisterZone({
  id=id,x=pos.x,y=pos.y,z=pos.z,radius=200,throttle=10,
  onEnter=function() shopNearCount=shopNearCount+1;startShopDetail() end,
  onExit=function()
   shopNearCount=shopNearCount-1
   if shopNearCount<=0 then shopNearCount=0;stopShopDetail() end
  end
 })
end
registerWakeZone("dedka_entrance1",ENTRANCE_POS)
registerWakeZone("dedka_entrance2_cluster",ENTRANCE_POS_2)
'@
$d=Apply 'Dedka Auto Shop' 'init.lua' 'native-location-dedka' 'ApplyDedka' $dedkaSource @(
  'G-CET native location:dedka-structural-v1',
  'return __gcetDedkaEngine.RegisterZone({',
  'lookup:attachCompiled(10034,2',
  '__gcetDedkaNativeZone1=registerWakeZone'
)
$bankSource=@'
local bank={}
local PosData={All={{x=1,y=2,z=3}}}
function bank:registerSpatialSet()
 if not self.engine then return end
 if self.spatialHandle then
  self.spatialHandle:unregister()
  self.spatialHandle=nil
 end
 self.spatialHandle = self.engine.RegisterSpatialSet("marmurbank",PosData.All, {
  gridSize=100
 })
 if self.spatialHandle then
  self.engine.Log("MarmurBank changed log wording","works","info")
 end
end
function bank:distanceListener()
 if not self.engine then return 0 end
 local searchRadiusSq = 25 * 25
 local entry,distSq=self.engine.QueryNearest("marmurbank",searchRadiusSq)
 if entry and distSq then self.nearestLocation=entry end
 return 0
end
'@
$m=Apply 'marmurbank' 'module/Bank.lua' 'native-location-marmurbank' 'ApplyMarmur' $bankSource @(
  'G-CET native location:marmurbank-structural-v1',
  'lookup:attachCompiled(10036,#PosData.All',
  'if self.__gcetNativeFar then return end',
  'if self.__gcetNativeFar then self.nearestLocation=nil;return 0 end'
)
Write-Host 'Source-structural Poker, Dedka helper-zone and MarmurBank logger-independent, per-stage idempotent hooks PASS'

# Historical mixed-state acceptance: generated semantic work can contain
# source-optimized Poker scheduler and Dedka helper zones but no native
# location stage yet. Validate the FULL same-pass composer, not just the
# individual adapter's ability to patch a fixture.
$matrix=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$matrix.Add('StreetGamesPoker/init.lua',[Text.Encoding]::UTF8.GetBytes($pokerSource))
$matrix.Add('Dedka Auto Shop/init.lua',[Text.Encoding]::UTF8.GetBytes($dedkaSource))
$matrix.Add('marmurbank/module/Bank.lua',[Text.Encoding]::UTF8.GetBytes($m))
$matrixHash=[Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$matrixCounts=[Collections.Generic.Dictionary[string,int]]::new([StringComparer]::OrdinalIgnoreCase)
$matrixApplied=[Collections.Generic.List[object]]::new()
$matrixSkipped=[Collections.Generic.List[object]]::new()
try {$matrixResult=$follow.Invoke($null,[object[]]@(
  [string]$root,$matrix,$matrixHash,$matrixCounts,$matrixApplied,$matrixSkipped))}
catch {throw "Historical partly-optimized composition rejected: $($_.Exception.InnerException.Message)"}
# Banker was already completed in the fixture. Its completion is independent
# of the other stages and MUST NOT cause reapplication.
if($matrixResult -ne 2 -or $matrixSkipped.Count -ne 0) {
 throw "Mixed-state composer failed: expected Poker/Dedka only, got $matrixResult; skipped=$($matrixSkipped.Count)"
}
foreach($check in @(
 @{path='StreetGamesPoker/init.lua';marker='G-CET native location:poker-structural-v1'},
 @{path='Dedka Auto Shop/init.lua';marker='G-CET native location:dedka-structural-v1'})) {
 $found=[Text.Encoding]::UTF8.GetString($matrix[$check.path])
 if(!$found.Contains($check.marker) -or !$found.Contains('attachCompiled(')) {
  throw "Mixed state lost a source-proven native stage: $($check.path)"
 }
}
try {$matrixSecond=$follow.Invoke($null,[object[]]@(
  [string]$root,$matrix,$matrixHash,$matrixCounts,$matrixApplied,$matrixSkipped))}
catch {throw "Historical same-pass second generation failed: $($_.Exception.InnerException.Message)"}
if($matrixSecond -ne 0 -or $matrixSkipped.Count -ne 0) {
 throw "Historical source-composition is not idempotent: changed=$matrixSecond, skipped=$($matrixSkipped.Count)"
}
Write-Host 'Historical mixed partially-optimized stack: Poker + Dedka compose, banker already satisfied, rerun does not reapply PASS'




# Location extraction is independent from owner hook completion. Deliberately
# compile from PRE-native source with no "G-CET native location" markers.
# A source update may preserve the coordinates even if its listener changed.
$tables=@'
local StreetTables={}
StreetTables.locations = {
 {id="one",center={x=1,y=2},surfaceZ=4,floorZ=3},
 {id="two",center={x=101,y=102},surfaceZ=104,floorZ=103}
}
'@
$posdata=@'
local PosData={}
PosData.Bankers = {
 {x=10,y=11,z=12},
 {x=20,y=21,z=22},
 {x=30,y=31,z=32}
}
PosData.All={}
for _,b in ipairs(PosData.Bankers) do
 table.insert(PosData.All,{x=b.x,y=b.y,z=b.z,type="banker"})
end
return PosData
'@
$stPath=Join-Path $root 'StreetGamesPoker/StreetTables.lua'
$psPath=Join-Path $root 'marmurbank/data/PosData.lua'
New-Item -ItemType Directory -Force (Split-Path $stPath -Parent),(Split-Path $psPath -Parent) | Out-Null
[IO.File]::WriteAllText($stPath,$tables)
[IO.File]::WriteAllText($psPath,$posdata)
$geometry=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$geometry.Add('StreetGamesPoker/init.lua',[Text.Encoding]::UTF8.GetBytes($pokerSource))
$geometry.Add('Dedka Auto Shop/init.lua',[Text.Encoding]::UTF8.GetBytes($dedkaSource))
$geometry.Add('marmurbank/module/Bank.lua',[Text.Encoding]::UTF8.GetBytes($bankSource))
try {$compiled=$compile.Invoke($null,[object[]]@([string]$root,$geometry))}
catch {throw "Independent location geometry failed source proof: $($_.Exception.InnerException.Message)"}
$countMap=$compilerType.GetProperty('OwnerCounts',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($compiled)
if($countMap[10031] -ne 2 -or $countMap[10034] -ne 2 -or $countMap[10036] -ne 3) {
 throw "Independent source-owned location counts invalid: Poker=$($countMap[10031]) Dedka=$($countMap[10034]) Bank=$($countMap[10036])"
}
$bytes=$compilerType.GetProperty('Bytes',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($compiled)
$compiledSiteCount=[BitConverter]::ToUInt32($bytes,8)
if($compiledSiteCount -lt 7){throw 'Required seven source-proven locations missing from compiled database'}
# Native banker coarse wake MUST exceed the 25m source discovery scan.
$bankRadii=for($idx=0;$idx -lt $compiledSiteCount;$idx++){
 if([BitConverter]::ToUInt32($bytes,16+28*$idx+24) -eq 10036){
  [BitConverter]::ToSingle($bytes,16+28*$idx+16)
 }
}
if($bankRadii.Count -ne 3 -or @($bankRadii | Where-Object {$_ -lt 30}).Count -gt 0){
 throw 'Banker native wake does not encompass the source nearest query radius'
}
Write-Host 'Pre-semantic native inventory from original Poker, Dedka and banker sources without subscriptions PASS'

# Verify native coordinates are independent of whether the handpicked
# semantic subscription exists: original and optimized source states must
# produce identical inventory bytes whenever their positions are equivalent.
$semanticGeometry=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$semanticGeometry.Add('StreetGamesPoker/init.lua',[Text.Encoding]::UTF8.GetBytes($p))
$semanticGeometry.Add('Dedka Auto Shop/init.lua',[Text.Encoding]::UTF8.GetBytes($d))
$semanticGeometry.Add('marmurbank/module/Bank.lua',[Text.Encoding]::UTF8.GetBytes($m))
$semanticCompiled=$compile.Invoke($null,[object[]]@([string]$root,$semanticGeometry))
$semanticBytes=$compilerType.GetProperty('Bytes',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($semanticCompiled)
if(!([Convert]::ToBase64String($bytes) -ceq [Convert]::ToBase64String($semanticBytes))){
 throw 'Native geometry depended on the semantic subscription source'
}
Write-Host 'Pre-semantic and post-semantic coordinate inventory binary equivalence PASS'

# A newly generated pass may touch zero files belonging to these already
# completed owners. Do NOT silently drop their native geometry from GCAS.
$noNativeEdits=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
try {$completedSites=$compile.Invoke($null,[object[]]@([string]$root,$noNativeEdits))}
catch {throw "Existing completed native owners lost manifest coverage: $($_.Exception.InnerException.Message)"}
$completedMap=$compilerType.GetProperty('OwnerCounts',[Reflection.BindingFlags]'Instance,Public,NonPublic').GetValue($completedSites)
if($completedMap[10031] -ne 2 -or $completedMap[10034] -ne 2 -or $completedMap[10036] -ne 3) {
 throw 'Already satisfied Poker, Dedka, banker geometry omitted from new incremental GCAS'
}
Write-Host 'Existing compiled owners remain in GCAS with zero staged Lua owner changes PASS'



# Player's installed 0-Engine module may be the original dynamic owner host,
# with local custom code. It must gain only the missing compiled-owner API
# without overwriting the entire module or removing the immutable GCAS IDs.
$fixedType=$asm.GetType('GCETRuntimeProfiler.Core.Services.FixedZeroEngineRuntime',$true)
$upgrade=$fixedType.GetMethod('UpgradeNativeLocationHost',
    [Reflection.BindingFlags]'Static,NonPublic')
if(!$upgrade){throw 'Structural legacy 0-Engine native host capability migration absent'}
$oldHost=@'
local Host={}
Host.__index=Host
local function native(name,...) return Game[name](...) end
function Host:_stop() end
function Host:_schedule() return true end
function Host:_fallback(reason) return true end
function Host:attach(id,sites,onNear,onFar,onFallback)
 self.owners=self.owners or {}
 self.owners[id]={ids={1},fallback=onFallback}
 return true
end
-- USER CUSTOM CODE KEEP THIS UNTOUCHED
function Host:shutdown()
 self:_stop()
 for _,owner in pairs(self.owners) do
  for _,id in ipairs(owner.ids) do pcall(native,"GCETStaticRemoveAnchor",id) end
 end
end
return Host
'@
$nativeSource=Join-Path (Get-Location).Path 'resolver-runtime/0-Engine/modules/GCETNativeLocationHost.lua'
if(!(Test-Path $nativeSource)){throw "Bundled reviewed native module unavailable: $nativeSource"}
$officialBytes=[IO.File]::ReadAllBytes($nativeSource)
try {$mergedBytes=$upgrade.Invoke($null,[object[]]@(
  [Text.Encoding]::UTF8.GetBytes($oldHost),$officialBytes))}
catch {throw "Legacy customized native host capability migration failed: $($_.Exception.InnerException.Message)"}
$merged=[Text.Encoding]::UTF8.GetString($mergedBytes)
foreach($proof in @('USER CUSTOM CODE KEEP THIS UNTOUCHED',
 'function Host:attachCompiled(', 'GCETStaticOwnerAnchor',
 'local __gcetPriorAttach=Host.attach',
 'local __gcetPriorNativeShutdown=Host.shutdown',
 'owner.ids={}')){
 if(!$merged.Contains($proof)){throw "Legacy native host migration missing: $proof"}
}
try {$second=$upgrade.Invoke($null,[object[]]@($mergedBytes,$officialBytes))}
catch {throw "Reapplying native host capabilities failed: $($_.Exception.InnerException.Message)"}
if([Convert]::ToHexString($mergedBytes) -ne [Convert]::ToHexString([byte[]]$second)){
 throw 'Native host capability bridge was not idempotent'
}
Write-Host 'Existing customized 0-Engine native host retained + missing compiled API and safe shutdown reconstituted, no version/SHA gate PASS'

Write-Host 'Native static location additional owners: Card Collection 100-site coarse dormancy, banker SpatialHash FAR, Yaiba bathroom author gate; fallback, CRLF and idempotence PASS'
 
# Semantic catalog generic-source bridge, with original fallback preservation
# and explicit rejection of an unproven helper name.
$semanticInjectorType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$literalAdapter=$semanticInjectorType.GetMethod('ReplaceOnce',$flags)
$regexAdapter=$semanticInjectorType.GetMethod('RegexReplaceOnce',$flags)
if(!$literalAdapter -or !$regexAdapter){throw 'Shared semantic getter adapters missing'}
$srcGeneric=@'
-- G-CET shared provider: PLAYER
local function __gcetGetPlayer()
  return Game.GetPlayer()
end
local player = __gcetGetPlayer()
'@
$srcOriginal="local player = Game.GetPlayer()"
$replacement=@'
local player = Game.GetPlayer()
if not player then return end
'@
try {
 $rewritten=[string]$literalAdapter.Invoke($null,
  [object[]]@($srcGeneric,$srcOriginal,$replacement,"generic player source"))
} catch {throw "Literal generic-source bridge failed: $($_.Exception.InnerException.Message)"}
if(!$rewritten.Contains('local player = __gcetGetPlayer()') -or
   !$rewritten.Contains('return Game.GetPlayer()') -or
   !$rewritten.Contains('if not player then return end')){
 throw 'Semantic literal adapter discarded generic getter or fallback'
}
$regexPattern='(?m)^local\s+player\s*=\s*Game\.GetPlayer\(\)\s*$'
try {
 $rewrittenRegex=[string]$regexAdapter.Invoke($null,
  [object[]]@($srcGeneric,$regexPattern,$replacement,"generic regex player"))
} catch {throw "Regex generic-source bridge failed: $($_.Exception.InnerException.Message)"}
if(!$rewrittenRegex.Contains('local player = __gcetGetPlayer()') -or
   !$rewrittenRegex.Contains('if not player then return end') -or
   !$rewrittenRegex.Contains('return Game.GetPlayer()')){
 throw 'Semantic regex adapter discarded generic getter or original fallback'
}
$unproven='local player = __gcetGetPlayer()'
$refused=$false
try {
 $null=$literalAdapter.Invoke($null,
  [object[]]@($unproven,$srcOriginal,$replacement,"unproven helper"))
} catch {$refused=$true}
if(!$refused){throw 'Semantic adapter accepted unproven generic helper'}
Write-Host 'Catalog generic getter composition: literal, regex, fallback and unproven refusal PASS'

# Exercise EVERY authorized shared getter, not just PLAYER.
# The authorized provider list is read from the actual compiled catalog.
$providerCatalog=$asm.GetType('GCETRuntimeProfiler.Core.Services.SharedProviderCatalog',$true)
$providerField=$providerCatalog.GetField('Authorized',
 [Reflection.BindingFlags]'Static,NonPublic,Public')
if(!$providerField){throw 'Shared provider catalog is missing'}
$providerRows=@($providerField.GetValue($null))
if($providerRows.Count -ne 26){throw "Expected 26 authorized shared providers; got $($providerRows.Count)"}
foreach($row in $providerRows) {
 $getter=[string]$row.Getter
 $provider=[string]$row.Provider
 $srcLines=@(
  ('-- G-CET shared provider: '+$provider),
  ('local function __gcet'+$getter+'()'),
  ('  return Game.'+$getter+'()'),
  'end',
  ('local target = __gcet'+$getter+'()')
 )
 $source=$srcLines -join [Environment]::NewLine
 $old='local target = Game.'+$getter+'()'
 $new=$old+[Environment]::NewLine+'local checked = true'
 $regexSource='(?m)^local\s+target\s*=\s*Game\.'+$getter+'\(\)\s*$'
 $label='generic provider '+$getter
 $literalArgs=[object[]]::new(4)
 $literalArgs[0]=$source
 $literalArgs[1]=$old
 $literalArgs[2]=$new
 $literalArgs[3]=$label
 $regexArgs=[object[]]::new(4)
 $regexArgs[0]=$source
 $regexArgs[1]=$regexSource
 $regexArgs[2]=$new
 $regexArgs[3]=$label
 try {
  $literalCheck=[string]$literalAdapter.Invoke($null,$literalArgs)
  $regexCheck=[string]$regexAdapter.Invoke($null,$regexArgs)
 } catch {
  throw "Generic-first semantic compatibility failed for $provider/${getter}: $($_.Exception.InnerException.Message)"
 }
 foreach($output in @($literalCheck,$regexCheck)){
  if(!$output.Contains('local target = __gcet'+$getter+'()') -or
     !$output.Contains('return Game.'+$getter+'()') -or
     !$output.Contains('local checked = true')){
   throw "Generic-first semantic regression for $provider/$getter"
  }
 }
}
Write-Host 'All 26 authorized shared-provider getters preserved across literal and regex semantic adapters PASS'

# Real Ziggy 19:55 regression: generic has already rewritten
# GetQuestsSystem, while the authored save() and fact write contract survives.
$ziggyType=$asm.GetType('GCETRuntimeProfiler.Core.Services.ZiggyProximitySource',$true)
$ziggySnapshot=$ziggyType.GetMethod('ReuseZiggyNativeQuestManagerForSave',
 [Reflection.BindingFlags]'Static,NonPublic')
if(!$ziggySnapshot){throw 'Ziggy native snapshot source adapter missing'}
$ziggyGeneric=@'
-- G-CET shared provider: QUESTS_SYSTEM
local function __gcetGetQuestsSystem()
 return Game.GetQuestsSystem()
end
local function set(k,v)__gcetGetQuestsSystem():SetFactStr('zg11_'..k,v)end
local function save()
 if not H.s or not valid(H.hooks)or not H.hooks:SessionLoaded()then return end
 local s=H.s
 set('step',s.step)
 set('path',s.path)
 set('cycle',s.cycle)
 set('damage',s.damage)
 set('phase',s.phase)
 set('active',s.active)
 set('state',s.state)
end
local function trace(event,details)
 print(event)
end
'@
try{$ziggyOutput=[string]$ziggySnapshot.Invoke($null,[object[]]@($ziggyGeneric))}
catch{throw "Ziggy 19:55 generic-first save composition failed: $($_.Exception.InnerException.Message)"}
if(!$ziggyOutput.Contains('local __gcetZiggySnapshotQuestSystem=__gcetGetQuestsSystem()') -or
 !$ziggyOutput.Contains('G-CET semantic:ziggy-native-snapshot-manager-v6') -or
 !$ziggyOutput.Contains('local function __gcetZiggySnapshotSet(k,v)') -or
 !$ziggyOutput.Contains('local function set(k,v)__gcetGetQuestsSystem()')){
 throw 'Ziggy semantic failed to preserve generic QuestSystem getter and seven authored fact writes'
}
try{$ziggyAgain=[string]$ziggySnapshot.Invoke($null,[object[]]@($ziggyOutput))}
catch{throw "Ziggy native snapshot repeat failed: $($_.Exception.InnerException.Message)"}
if($ziggyAgain -cne $ziggyOutput){throw 'Ziggy source composition was not idempotent'}
$ziggyUnknown=$ziggyGeneric.Replace(
 'local function set(k,v)__gcetGetQuestsSystem():SetFactStr',
 'local function set(k,v)UnknownQuestsSystem():SetFactStr')
$refused=$false
try {$null=$ziggySnapshot.Invoke($null,[object[]]@($ziggyUnknown))}
catch {$refused=$true}
if(!$refused){throw 'Ziggy accepted an unknown quest-system writer without source proof'}
Write-Host 'Ziggy generic QuestSystem source: full seven-fact save, 1-pass, idempotence and unknown-source refusal PASS'

# Regression: two generic stages finish before semantic source composition.
# Native static locations are its LAST substage. No generic rewrite may
# run after semantic. The actual generated source is independently validated
# after fixed runtime assembly to enforce Lua's 200 active-local limit.
$passGeneratorSource=[IO.File]::ReadAllText(
  (Join-Path (Get-Location).Path 'src/G.CETProfiler.Core/Services/PassGeneratorService.cs'))
$orderedPipeline=@(
 'var transformed = TransformFile(',
 'BlackboardPsmDefinitionCacheSource.TryStage(',
 'ZeroEngineAllocationBatchSource.TryStage(',
 'NativePlayerVectorStage.TryStage(',
 'NativeReadReuseSource.TryStage(',
 'var availableLocations=CompiledNativeLocations.Compile(modsRoot,staged);',
 'SemanticPassGeneratorService.Apply(',
 'var fixedRuntime = FixedZeroEngineRuntime.Prepare(modsRoot);',
 'Whole-overlay Lua budget validation',
 'var fileManifest = staged'
)
$previous=-1
foreach($stage in $orderedPipeline){
 $index=$passGeneratorSource.IndexOf($stage,[StringComparison]::Ordinal)
 if($index -le $previous) {
  throw "GENERIC -> GENERIC -> NATIVE LOCATIONS -> SEMANTIC ordering violation: $stage ($index after $previous)"
 }
 $previous=$index
}
if(!$passGeneratorSource.Contains('LuaLocalBudgetAnalyzer.Analyze(pair.Value)') -or
   !$passGeneratorSource.Contains('budget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit')){
 throw 'Final generated Lua 200-local verification is missing'
}
$semanticSource=[IO.File]::ReadAllText(
  (Join-Path (Get-Location).Path 'src/G.CETProfiler.Core/Services/SemanticPassGeneratorService.cs'))
if(!$semanticSource.Contains('OrderBy(x => x.RuleId.StartsWith("native-location-"') -or
   !$semanticSource.Contains('ApplyPreparedNativeLocationStages(')){
 throw 'Native static location owner ordering or same-pass semantic composition missing'
}
Write-Host 'GENERIC 1 -> GENERIC 2 -> NATIVE static locations -> SEMANTIC subscribers -> final Lua budget PASS'
