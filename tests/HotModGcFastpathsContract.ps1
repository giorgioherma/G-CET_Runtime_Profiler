param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$t=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxT=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$ctor=@($ctxT.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$root=Join-Path $env:RUNNER_TEMP 'gcet-hot-mod-gc-contract'
if(Test-Path $root){Remove-Item $root -Recurse -Force}
New-Item -ItemType Directory $root|Out-Null
function Check($owner,$rule,$method,$fixture,$required,$driftToken) {
  $folder=Join-Path $root $owner
  New-Item -ItemType Directory -Force $folder|Out-Null
  $path=Join-Path $folder 'init.lua'
  [IO.File]::WriteAllText($path,$fixture,[Text.UTF8Encoding]::new($false))
  $fn=$t.GetMethod($method,$flags)
  if(!$fn){throw "Missing $method"}
  $staged=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
  $ctx=$ctor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$staged))
  try{$result=$fn.Invoke($null,[object[]]@($ctx))}
  catch{throw "$rule matching fixture rejected: $($_.Exception.InnerException.Message)"}
  $changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
  if(!$result.Applied -or $changes.Count -ne 1){throw "$rule did not stage one source"}
  $out=[Text.Encoding]::UTF8.GetString($changes[0].Bytes)
  foreach($token in $required){if(!$out.Contains($token)){throw "$rule postcondition missing: $token"}}
  if([IO.File]::ReadAllText($path) -ne $fixture){throw "$rule modified original source"}
  [IO.File]::WriteAllText($path,$out)
  $empty=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
  $ctx2=$ctor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$empty))
  $again=$fn.Invoke($null,[object[]]@($ctx2))
  if($again.Applied -or @($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx2)).Count -ne 0){
    throw "$rule is not idempotent"
  }
  if($driftToken) {
    $changed=$fixture.Replace($driftToken,'UNKNOWN_AUTHOR_DRIFT')
    if($changed -eq $fixture){throw "Test drift anchor missing: $rule"}
    [IO.File]::WriteAllText($path,$changed)
    $ctx3=$ctor.Invoke([object[]]@([string]$root,[string]$owner,[string]$rule,$empty))
    $rejected=$false
    try{$fn.Invoke($null,[object[]]@($ctx3))|Out-Null}catch{$rejected=$true}
    if(!$rejected -or @($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx3)).Count -ne 0){
      throw "$rule accepted unknown author drift"
    }
  }
  Write-Host "$rule PASS"
}
$f80=@'
local SWEEP_INTERVAL   = 1.5
local PENDING_TTL      = 12.0
local AUDIO_CHECK_INTERVAL = 1.0
local audioCheckTimer = 0
local function setAnchorWoman(anchorName,womanNpc) end
---------------------------------------------------------
-- UPDATE LOOP
---------------------------------------------------------
registerForEvent("onUpdate",function(delta)
  -- Couple variance (time-based + random despawn)
  if CoupleVariance and CoupleVariance.Update then
    CoupleVariance.Update(
      delta,
      settings,
      ANCHORS,
      tracked,
      function(npc)
        if npc and npc.Dispose then
          pcall(function() npc:Dispose() end)
        end
      end,
      function(anchorName)
        setAnchorWoman(anchorName, nil)
      end
    )
  end
  local womenByAnchor = {}   -- anchorName -> NPCPuppet
  for _, entry in pairs(tracked) do
    local npc = entry and entry.entity or nil
  end
  for _, anchor in ipairs(ANCHORS) do
    setAnchorWoman(anchor.name, womenByAnchor[anchor.name])
  end
  sweepTimer = sweepTimer + delta
  if sweepTimer < SWEEP_INTERVAL then return end
  sweepTimer = 0
  for hash, data in pairs(pending) do
    local npc = data.npc
    if not npc then
      pending[hash] = nil
    else
      data.since = data.since + delta
    end
  end
  for hash, data in pairs(pendingAppearance) do
    local npc = data.npc
    if not npc then
      pendingAppearance[hash] = nil
    else
      data.since = data.since + delta
    end
  end
end)
'@
Check 'f80h_aroused_expressions' 'f80h-active-gc-sweep' 'ApplyF80hActiveGcSweep' $f80 @(
 '__gcetF80hActiveGcSweepV1','__gcetVarianceDispose',
 'audioCheckTimer + delta >= AUDIO_CHECK_INTERVAL',
 'data.since = data.since + __gcetSweepElapsed') 'local womenByAnchor = {}'
$npc=@'
local function GetNPCID(target) return "Npc.Base" end
local function ProcessRadarOutfits()
    for entIdStr, npc in pairs(activeNPCs) do
        local ok, isAttached = pcall(function() return npc:IsAttached() end)
        if not ok or not isAttached then
            activeNPCs[entIdStr] = nil
        else
            local recordIdStr = GetNPCID(npc)
        end
    end
end
registerForEvent("onInit",function()
  Observe("NPCPuppet","OnGameAttached",function(self)
    if self:IsNPC() then
                    activeNPCs[entId] = self
    end
  end)
  Observe("NPCPuppet","OnGameDetached",function(self)
    if self:IsNPC() then
                    activeNPCs[entId] = nil
    end
  end)
end)
registerForEvent("onUpdate",function(delta)
    local __gcetHasAutoOutfitRules = type(autoOutfitsData) == "table"
    if __gcetHasAutoOutfitRules or isCustomizeWindowOpen or isCameraModeActive
        or isAnimPlaying or autoRetryNakedTarget or isNCAWindowOpen
        or isShopSystemActive then
        currentTarget = GetLookAtNPC()
    end
end)
'@
Check 'NPC Outfit Manager' 'npc-outfit-manager-radar-gc' 'ApplyNpcOutfitRadarGc' $npc @(
 '__gcetNpcRadarGcV1','__gcetRadarRecordCache[entIdStr]',
 'radarTimer + delta >= 1.0') 'local function ProcessRadarOutfits()'
$amm=@'
local AMM={}
local Util={}
local function run()
~~~~if AMM.Props.presetLoadInProgress then
~~~~~Util:FreezePlayer()

~~~~~Cron.After(5, function()
~~~~~~AMM.Props.presetLoadInProgress = false
~~~~~~Util:RemovePlayerEffects()
~~~~~end)
~~~~end
end
'@
$amm=[regex]::Replace($amm,'~+',{
  param($m)
  return ([string][char]9) * $m.Value.Length
})
Check 'AppearanceMenuMod' 'appearance-menu-mod-preset-once' 'ApplyAppearanceMenuPresetTimer' $amm @(
 '__gcetAmmPresetTimerV1','AMM.__gcetPresetTimerTicket == ticket',
 'AMM.__gcetPresetTimerArmed = false') 'Cron.After(5, function()'
$blm=@'
BetterLootMarkers={}
function BetterLootMarkers.ResolveHighestQualityByCategory(itemList)
    local categories = {}
    for _, item in ipairs(itemList) do
        local quality = RPGManager.GetItemDataQuality(item)
        local category = BetterLootMarkers.GetLootCategoryForItem(item)
        local newItem = {
            quality = quality,
            isIconic = RPGManager.IsItemIconic(item),
        }
        if categories[category] == nil then
            categories[category] = newItem
            categories[category].count = 1
        else
            categories[category].count = categories[category].count + 1
            local currentScore = BetterLootMarkers.GetItemScore(categories[category])
            local newScore = BetterLootMarkers.GetItemScore(newItem)
            if newScore > currentScore then
                local count = categories[category].count
                categories[category] = newItem
                categories[category].count = count
            end
        end
    end
end
function BetterLootMarkers.GetItemScore(itemData)
    return itemData.isIconic and 10 or 1
end
'@
Check 'BetterLootMarkers' 'better-loot-markers-category-no-temporary-table' 'ApplyBetterLootMarkersCategoryAllocation' $blm @(
 'better-loot-markers-category-no-temporary-table',
 'local iconic = RPGManager.IsItemIconic(item)',
 'local current = categories[category]') 'local newItem = {'

# NIF's native-to-Lua global observer must return before ANY engine reads
# when both author-owned pin tables are empty; no native object caching.
$nifOwner=Join-Path $root 'nativeInteractions'
$nifFolder=Join-Path $nifOwner 'modules/utils'
New-Item -ItemType Directory -Force $nifFolder|Out-Null
$nifPath=Join-Path $nifFolder 'worldInteraction.lua'
$nif=@'
local world={pinInteractions={},pinnedInteractions={}}
local function indexPin(interaction) end
local function unindexPin(interaction) end
local function findPinInteraction(mappin, mappinID) return nil end
world.pinInteractions = {}
world.pinnedInteractions = {}
    ObserveAfter("BaseMappinBaseController", "UpdateRootState", function(this) -- Custom pin texture
  local mappin=this:GetMappin()
  if not mappin then return end
  local profile=this:GetProfile()
  if not profile or profile:GetID().value ~= "WorldMappinUIProfile.nif" then return end
end)
'@
[IO.File]::WriteAllText($nifPath,$nif)
$fn=$t.GetMethod('ApplyNativeInteractionsEmptyPinIdle',$flags)
if(!$fn){throw 'Missing NIF empty-pin injector'}
$ctx=$ctor.Invoke([object[]]@([string]$root,[string]'nativeInteractions',
  [string]'nativeinteractions-no-pins-observer',
  [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)))
$res=$fn.Invoke($null,[object[]]@($ctx))
$changes=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))
if(!$res.Applied -or $changes.Count -ne 1){throw 'NIF empty pin stage failed'}
$raw=[Text.Encoding]::UTF8.GetString($changes[0].Bytes)
if(!$raw.Contains('if next(world.pinInteractions) == nil and next(world.pinnedInteractions) == nil then return end')){
  throw 'NIF empty marker inventory not checked'
}
if($raw.IndexOf('if next(world.pinInteractions)') -gt $raw.IndexOf('local mappin=this:GetMappin()')){
  throw 'NIF native reads occurred before empty-inventory gate'
}
if([IO.File]::ReadAllText($nifPath) -ne $nif){throw 'NIF original source overwritten'}
Write-Host 'NativeInteractions empty-pin observer gate PASS'

$tunnel=@'
local H={diag=0,tick=0,clock=0}
local function status() return true end
local function update(dt)
 H.clock=H.clock+dt;H.tick=H.tick+dt;H.diag=H.diag+dt
 if H.tick<.15 then return end;dt=math.min(H.tick,.5);H.tick=0;H.age=H.age+dt
 if not valid(H.hooks)then H.error='Native TRQHooks unavailable. Native scripts need to reload.';if H.diag>3 then H.diag=0;status()end;return end
end
'@
Check 'tunnel_rescue' 'tunnel-missing-hooks-diagnostics' 'ApplyTunnelMissingHooksDiagnostics' $tunnel @(
 '__gcetTunnelMissingHooksDiagV1',
 '__gcetTunnelMissingHooksReported and 15 or 3',
 '__gcetTunnelMissingHooksReported=false') 'Native TRQHooks unavailable. Native scripts need to reload.'
Write-Host 'Six GC/idle source-safe mod stages PASS'

