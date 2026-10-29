param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-sitanywhere-pristine-one-pass'
$mod=Join-Path $root 'sitAnywhere'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory -Force (Join-Path $mod 'modules/external') | Out-Null
function Put($name,$src) {
  $full=Join-Path $mod $name
  $dir=Split-Path $full -Parent
  New-Item -ItemType Directory -Force $dir | Out-Null
  [IO.File]::WriteAllText($full,$src,[Text.UTF8Encoding]::new($false))
}
# This is original-shaped author source, with NO pre-existing PASS4
# coordinator, scheduler, G-CET marker, or injected worker.
$original=@'
local Cron = require("modules/external/Cron")
local interaction = require("modules/interactionUI")
local world = require("modules/worldInteraction")
function sit:new()
    registerForEvent("onInit", function()
        self.logic:init()
    end)
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
end
'@
Put 'init.lua' $original
Put 'modules/external/Cron.lua' @'
local timers = {}
function Cron.Update(dt) end
function Cron.Halt() end
'@
Put 'modules/logic.lua' @'
local logic = {}
function logic:new()
    local o = {}
    o.sittables = {}
    return o
end
function logic:hideAllWorkspots()
    self.sittables = {}
end
function logic:onUpdate()
    if position then
        update()
    end
end
'@
Put 'modules/worldInteraction.lua' @'
local world = {}
world.interactions = {}
function world.update() end
function world.onSessionStart() end
'@
$files=@('init.lua','modules/external/Cron.lua','modules/logic.lua','modules/worldInteraction.lua')
$sha=@{}
foreach($f in $files){$sha[$f]=(Get-FileHash (Join-Path $mod $f) -Algorithm SHA256).Hash}
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inj=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$method=$inj.GetMethod('ApplySitAnywhere',$flags)
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|
 Where-Object {$_.GetParameters().Count -eq 4})[0]
if(!$method -or !$ctor){throw 'SitAnywhere authoritative generator inaccessible'}
function MakeCtx($stage) {
 return $ctor.Invoke([object[]]@([string]$root,[string]'sitAnywhere',[string]'sitanywhere',$stage))
}
$staged=[Collections.Generic.Dictionary[string,byte[]]]::new()
$ctx=MakeCtx $staged
$result=$method.Invoke($null,[object[]]@($ctx))
$changes=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
if(!$result.Applied -or $changes.Count -lt 5){throw 'Untouched SitAnywhere was not fully optimized in one generation'}
foreach($change in $changes){$staged[$change.RelativeFile]=$change.Bytes}
$init=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/init.lua'])
$worker=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua'])
if(!$worker.StartsWith("-- G-CET semantic coordinator:sitanywhere`n")) {
 throw 'Primary semantic rule does not own the generated worker'
}
foreach($owned in @('sitAnywhere/init.lua','sitAnywhere/modules/external/Cron.lua','sitAnywhere/modules/logic.lua','sitAnywhere/modules/worldInteraction.lua')) {
 if(!([Text.Encoding]::UTF8.GetString($staged[$owned])).Contains('-- G-CET semantic:sitanywhere')) {
   throw "Primary semantic ownership missing: $owned"
 }
}
foreach($token in @('local function runSitAnywhereFrame(dt)',
 'TryAttachSitAnywherePass4 = function()',
 'sitAnywhereBaseInitialized = true',
 'if pass4Attached then return end',
 'runSitAnywhereFrame(dt)',
 'require("GCET_Semantic/sitanywhere_engine_worker")',
 'G-CET semantic:sitanywhere-v1')) {
 if(!$init.Contains($token)){throw "Pristine output missing full scheduler stage: $token"}
}
foreach($token in @('Schedule.EveryFrame','sitanywhere.realtime',
 'created[#created + 1] = job')) {
 if(!$worker.Contains($token)){throw "Pristine worker incomplete: $token"}
}

foreach($token in @('local function shouldRunSitAnywhereFrame()',
  'created, runSitAnywhereFrame, shouldRunSitAnywhereFrame)',
  'self.__gcetWakeRealtime = function()',
  'self.runtimeData.forceScan = true')) {
  if(!$init.Contains($token)){throw "SitAnywhere pausable source missing $token"}
}
foreach($token in @('sitanywhere.wake','Schedule.Every(0.10',
  'job.Pause()','job.Resume()','created[#created + 1] = probe')) {
  if(!$worker.Contains($token)){throw "SitAnywhere wake worker missing $token"}
}
$logicWake=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/modules/logic.lua'])
if(([regex]::Matches($logicWake,'self.mod.__gcetWakeRealtime\(\)')).Count -ne 2){
  throw 'SitAnywhere original ScannerOpen/Close missing immediate wake'
}
$second=MakeCtx $staged
$secondResult=$method.Invoke($null,[object[]]@($second))
if($secondResult.Applied -or $secondResult.Reason -notlike '*already installed*') {
 throw 'Complete one-pass scheduler must be idempotent'
}
# V1 generated installs must regenerate only the missing worker and source
# wake stage, never duplicate the existing callback owner.
$priorField=$inj.GetField('SitAnywhereWorkerV1',$flags)
if(!$priorField){throw 'SitAnywhere legacy owned worker proof absent'}
$priorWorker=[string]$priorField.GetRawConstantValue()
$wakeStart=$init.IndexOf('local function shouldRunSitAnywhereFrame()')
$wakeEnd=$init.IndexOf('local function runSitAnywhereFrame(dt)',$wakeStart)
if($wakeStart -lt 0 -or $wakeEnd -le $wakeStart){
  throw 'SitAnywhere generated source wake boundary drift'
}
$oldInit=$init.Remove($wakeStart,$wakeEnd-$wakeStart)
$oldInit=$oldInit.Replace('created, runSitAnywhereFrame, shouldRunSitAnywhereFrame)',
  'created, runSitAnywhereFrame)')
$wakeBlock=@'
    self.__gcetWakeRealtime = function()
        if pass4FrameJob and type(pass4FrameJob.Resume) == "function" then
            pass4FrameJob.Resume()
        end
    end
'@
$wakeBlock=$wakeBlock.TrimEnd() + [char]10
if(!$oldInit.Contains($wakeBlock)){throw 'SitAnywhere scanner wake callback shape drift'}
$oldInit=$oldInit.Replace($wakeBlock,'')
$oldInit=[regex]::Replace($oldInit,
  '(?m)^            if self.__gcetWakeRealtime then self.__gcetWakeRealtime\(\) end\r?\n','')
$oldLogic=[regex]::Replace($logicWake,
  '(?m)^        if self.mod.__gcetWakeRealtime then self.mod.__gcetWakeRealtime\(\) end\r?\n','')
$staged['sitAnywhere/init.lua']=[Text.Encoding]::UTF8.GetBytes($oldInit)
$staged['sitAnywhere/modules/logic.lua']=[Text.Encoding]::UTF8.GetBytes($oldLogic)
$staged['sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua']=[Text.Encoding]::UTF8.GetBytes($priorWorker)
$legacyCtx=MakeCtx $staged
$legacyResult=$method.Invoke($null,[object[]]@($legacyCtx))
$legacyChanges=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($legacyCtx))
if(!$legacyResult.Applied -or $legacyChanges.Count -ne 3){
  throw 'SitAnywhere prior V1 worker/init/scanner migration failed'
}
foreach($change in $legacyChanges){$staged[$change.RelativeFile]=$change.Bytes}
$regenerated=[Text.Encoding]::UTF8.GetString($staged['sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua'])
if($regenerated.TrimEnd() -ne $worker.TrimEnd()){
  throw 'SitAnywhere prior coordinator failed to regenerate'
}
# Restore the originally proven complete stage for missing-worker test.
$staged['sitAnywhere/init.lua']=[Text.Encoding]::UTF8.GetBytes($init)
$staged['sitAnywhere/modules/logic.lua']=[Text.Encoding]::UTF8.GetBytes($logicWake)
$staged['sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua']=[Text.Encoding]::UTF8.GetBytes($worker)

$staged.Remove('sitAnywhere/GCET_Semantic/sitanywhere_engine_worker.lua')|Out-Null
$missing=MakeCtx $staged
$refused=$false
try{$null=$method.Invoke($null,[object[]]@($missing))}catch{
 if($_.Exception.ToString() -match 'partially installed'){$refused=$true}
}
if(!$refused){throw 'Missing worker was silently considered complete'}
foreach($f in $files) {
 if((Get-FileHash (Join-Path $mod $f) -Algorithm SHA256).Hash -ne $sha[$f]){
   throw "Generator touched original file: $f"
 }
}
Write-Host 'SitAnywhere pristine single-pass scheduler, worker, fallback, idempotence, missing-worker refusal, original preservation PASS'


# Source-shaped supplementary fixture: source-local optimizations are part
# of the same authoritative SitAnywhere generator as the external scheduler.
# It models only the relevant authored hotpaths, not a copied Nexus source.
Put 'modules/logger.lua' @'
local active = false
logger = { text = "" }
function logger.log(title, ...)
    if not active then return end
    logger.text = logger.text .. title
end
return logger
'@
$logicSource=@'
local logic = {}
function logic:getHit(castType)
    local forwardOffset = 1
    if castType == "preset" then forwardOffset = 1.6 end
    local playerHead = utils.addVector(GetPlayer():GetWorldPosition(), Vector4.new(0, 0, 1.6, 0))
    local from = utils.addVector(playerHead, utils.multVector(__gcetGetCameraSystem():GetActiveCameraForward(), forwardOffset))
    local to = utils.addVector(playerHead, utils.multVector(__gcetGetCameraSystem():GetActiveCameraForward(), 100))

    local result = self:raycast(from, to, castType)

    logger.log("Cast castType", castType)
    logger.log("Cast succes", result:IsValid())
    logger.log("Cast distance", Vector4.Vector3To4(result.position):Distance(GetPlayer():GetWorldPosition()))
    logger.log("Cast yDist", Vector4.Vector3To4(result.position).z - GetPlayer():GetWorldPosition().z)
    logger.log("Cast normal pitch", math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch))

    if not result:IsValid() then return end -- No hit

    if Vector4.Vector3To4(result.position):Distance(GetPlayer():GetWorldPosition()) > 2.5 then return end
    local yDist = Vector4.Vector3To4(result.position).z - GetPlayer():GetWorldPosition().z
    if yDist < 0.3 or yDist > 0.8 then return end
    if math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) < 80 or math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) > 95 then -- Try custom normals, given ones are sometimes faulty
        local normal = Vector4.Vector3To4(result.normal)
        local p1 = {position = result.position}
        local p2 = {position = result.position}
        local p3 = {position = result.position}
        logger.log("Custom normal pitch", math.abs(normal:ToRotation().pitch))
        logger.log("Custom normal z1", p1.position.z)
        logger.log("Custom normal z2", p2.position.z)
        logger.log("Custom normal z3", p3.position.z)
        result.normal = normal:Vector4To3()
        if math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) < 80 or math.abs(Vector4.Vector3To4(result.normal):ToRotation().pitch) > 95 then return end
    end
    local target = __gcetGetTargetingSystem():GetObjectClosestToCrosshair(GetPlayer(), TSQ_NPC())
    if target and target:GetWorldPosition():Distance(Vector4.Vector3To4(result.position)) < 0.75 then return end
    return result
end
function logic:onUpdate()
    logger.log("Status", self.isScanning, self:isValidEnviroment(), self.interface)
    self:protectSpot()
end
return logic
'@
$worldSource=@'
local world = {interactions={}}
function world.init()
    ObserveAfter("BaseMappinBaseController", "UpdateRootState", function(this)
        local mappin = this:GetMappin()
        if not mappin then return end
        local pos = mappin:GetWorldPosition()
        for _, interaction in pairs(world.interactions) do
            if Vector4.Distance(pos, interaction.pos) < 0.05 and interaction.shown then
                local record = TweakDBInterface.GetUIIconRecord(interaction.icon)
                this.iconWidget:SetAtlasResource(record:AtlasResourcePath())
                this.iconWidget:SetTexturePart(record:AtlasPartName())
                this.iconWidget:SetTintColor(interaction.iconColor or HDRColor.new({ Red = 1, Green = 1, Blue = 1 }))
            end
        end
    end)
end
function world.update()
    local showKeys = {}
    local posPlayer = GetPlayer():GetWorldPosition()
    posPlayer.z = posPlayer.z + 1
    local playerForward = GetPlayer():GetWorldForward()
    for key, interaction in pairs(world.interactions) do
        local update = interaction.shown
        if Vector4.Distance(posPlayer, interaction.pos) < interaction.interactionRange then
            if 180 - Vector4.GetAngleBetween(playerForward, interaction.pos) < interaction.angle then
                update = true
            else
                update = false
            end
        else
            update = false
        end
        if update ~= interaction.shown then
            if update then table.insert(showKeys,key) else
                interaction.shown = update
                interaction.callback(update)
            end
        end
        if interaction.icon and Vector4.Distance(posPlayer, interaction.pos) < interaction.iconRange then
            world.togglePin(interaction,true)
        elseif interaction.icon then
            world.togglePin(interaction,false)
        end
    end
    for _, key in pairs(showKeys) do
        world.interactions[key].shown = true
        world.interactions[key].callback(true)
    end
end
return world
'@
$fastpathMethod=$inj.GetMethod('PreserveSitAnywhereSourceFastpaths',$flags)
if(!$fastpathMethod){throw 'SitAnywhere source-owned fastpath implementation is absent'}
$sourceStage=[Collections.Generic.Dictionary[string,byte[]]]::new()
$fastCtx=MakeCtx $sourceStage
try {
    $fastResult=$fastpathMethod.Invoke($null,[object[]]@($fastCtx,$logicSource,$worldSource))
}
catch { throw "SitAnywhere source-local pristine case failed: $($_.Exception.InnerException.Message)" }
$newLogic=[string]$fastResult.Item1
$newWorld=[string]$fastResult.Item2
if(!$fastResult.Item3 -or
   $newLogic -notmatch 'if not valid then return end' -or
   $newLogic -notmatch 'local resultPosition = Vector4.Vector3To4\(result.position\)' -or
   $newLogic.IndexOf('if not valid then return end') -gt
      $newLogic.IndexOf('local resultPosition = Vector4.Vector3To4(result.position)') -or
   $newLogic -notmatch 'if logger.isActive\(\) then' -or
   $newWorld -notmatch 'local function distanceSquared' -or
   $newWorld -notmatch 'if not player then return end' -or
   $newWorld -notmatch 'if not pos then pos = mappin:GetWorldPosition\(\) end' -or
   $newWorld -match 'Vector4.Distance\(posPlayer, interaction.pos\)') {
   throw 'SitAnywhere missing guarded raycast/logger/mappin/squared-distance output'
}
$added=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($fastCtx))
if(!($added | Where-Object {$_.RelativeFile -like '*/modules/logger.lua'})) {
    throw 'SitAnywhere source-owned logger API was not emitted'
}
foreach($change in $added){$sourceStage[$change.RelativeFile]=$change.Bytes}
$secondFast=MakeCtx $sourceStage
$secondResult=$fastpathMethod.Invoke($null,[object[]]@($secondFast,$newLogic,$newWorld))
if($secondResult.Item1 -cne $newLogic -or $secondResult.Item2 -cne $newWorld -or
   $secondResult.Item3) { throw 'SitAnywhere source fastpath repeated a complete transformation' }
$unsafe=$logicSource.Replace('if not result:IsValid() then return end -- No hit',
    '-- modified validity behavior')
$driftRefused=$false
try{$null=$fastpathMethod.Invoke($null,[object[]]@((MakeCtx $sourceStage),$unsafe,$worldSource))}
catch{$driftRefused=$true}
if(!$driftRefused){throw 'SitAnywhere changed raycast semantics were not refused'}
Write-Host 'SitAnywhere pristine source-local guarded raycasts, logger, native mappin, squared-distance, repeat and drift PASS'
