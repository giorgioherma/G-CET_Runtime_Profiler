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
  $targetType=if($method -eq 'ApplyIllegalV3'){$four}else{$type}
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
Write-Host 'Illegal Mechanic per-site compiled owner binding, individual FAR/NEAR wake, existing exact NPC streaming, and idempotence PASS'

Write-Host 'Native static location additional owners: Card Collection 100-site coarse dormancy, banker SpatialHash FAR, Yaiba bathroom author gate; fallback, CRLF and idempotence PASS'
