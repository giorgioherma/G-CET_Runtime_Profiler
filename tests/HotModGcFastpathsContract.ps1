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
    radarTimer = radarTimer + delta
    if radarTimer >= 1.0 then
        radarTimer = 0.0
        ProcessRadarOutfits()
    end
end)
'@
Check 'NPC Outfit Manager' 'npc-outfit-manager-radar-gc' 'ApplyNpcOutfitRadarGc' $npc @(
 '__gcetNpcRadarGcV1','__gcetRadarRecordCache[entIdStr]',
 'radarTimer + delta >= 1.0','__gcetNpcRadarQueueV2',
 '__gcetNpcRadarSweepV2 >= 10.0','__gcetNpcRadarRulesChangedV2()') 'local function ProcessRadarOutfits()'
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
# Native Interactions is installed via its top-level CET entrypoint;
# worldInteraction.lua alone would be a removed-mod orphan.
'-- Installed nativeInteractions CET entrypoint' |
    Set-Content -LiteralPath (Join-Path $nifOwner 'init.lua') -Encoding utf8
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


# JNC Joytoy original onDraw is the top measured current draw allocator.
# Its author ui.update() still needs to clear ui.input every frame, but
# hidden custom hubs cannot affect choice hubs. No duplicate observers.
$joy=Join-Path $root 'DedraJNCJoytoy'
$joyModule=Join-Path $joy 'modules'
New-Item -ItemType Directory -Force $joyModule | Out-Null
$joyInit=@'
local ui = require("modules/interactionUI")
registerForEvent("onShutdown", function() end)
registerForEvent("onDraw", function()
  if shuttingDown then return end
  if not sessionActive then return end

  ui.update()
end)
'@
$joyUi=@'
local ui={hubShown=false,input=false}
local function getDialogChoiceHubs() return {} end
function ui.showHub() ui.hubShown=true end
function ui.hideHub() ui.hubShown=false end
function ui.update() -- Run ever frame to avoid unwanted changes
    local hubs = getDialogChoiceHubs()

    if ui.hubShown and ui.customHubSelected and #hubs == 0 then
        updateSelectedHub(ui.hub.id)
        updateSelectedIndex(ui.selectedIndex)
    elseif ui.hubShown then
        ui.customHubSelected = getActiveChoiceHubID() == ui.hub.id
        local hubs = getDialogChoiceHubs()
        if #hubs == 0 then
            updateSelectedHub(ui.hub.id)
            updateSelectedIndex(0)
            ui.customHubSelected = true
            ui.selectedIndex = 0
        end
    end
    ui.input = false -- Avoid double input
end

return ui
'@
[IO.File]::WriteAllText((Join-Path $joy 'init.lua'),$joyInit,[Text.UTF8Encoding]::new($false))
$joyFile=Join-Path $joyModule 'interactionUI.lua'
[IO.File]::WriteAllText($joyFile,$joyUi,[Text.UTF8Encoding]::new($false))
$joyMethod=$t.GetMethod('ApplyDedraJncJoytoyInactiveUiDraw',$flags)
if(!$joyMethod){throw 'Missing JNC Joytoy source-owned UI generator'}
$joyStaged=[Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)
$joyContext=$ctor.Invoke([object[]]@([string]$root,[string]'DedraJNCJoytoy',
  [string]'dedra-jnc-joytoy-inactive-ui-draw',$joyStaged))
try{$joyApplied=$joyMethod.Invoke($null,[object[]]@($joyContext))}
catch{throw "JNC Joytoy pristine original refused: $($_.Exception.InnerException.Message)"}
$joyChanges=@($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($joyContext))
if(!$joyApplied.Applied -or $joyChanges.Count -ne 1){throw 'JNC Joytoy hidden UI not generated in one source pass'}
$joyOut=[Text.Encoding]::UTF8.GetString($joyChanges[0].Bytes)
if(!$joyOut.Contains('__gcetDedraJncInactiveDrawV1') -or
   !$joyOut.Contains('if not ui.hubShown then') -or
   !$joyOut.Contains('ui.input = false -- preserve per-frame double-input latch reset') -or
   !$joyOut.Contains('local hubs = getDialogChoiceHubs()') -or
   !$joyOut.Contains('ui.customHubSelected = getActiveChoiceHubID() == ui.hub.id') -or
   !$joyOut.Contains('ui.input = false -- Avoid double input')){
  throw 'JNC Joytoy hidden/visible path or input reset lost'
}
if([IO.File]::ReadAllText($joyFile) -cne $joyUi){throw 'JNC Joytoy original file was modified'}
[IO.File]::WriteAllText($joyFile,$joyOut,[Text.UTF8Encoding]::new($false))
$joyAgain=$ctor.Invoke([object[]]@([string]$root,[string]'DedraJNCJoytoy',
  [string]'dedra-jnc-joytoy-inactive-ui-draw',
  [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)))
$joyAgainResult=$joyMethod.Invoke($null,[object[]]@($joyAgain))
if($joyAgainResult.Applied -or
   @($ctxT.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($joyAgain)).Count -ne 0){
  throw 'JNC Joytoy completed source was applied twice'
}
[IO.File]::WriteAllText($joyFile,$joyUi.Replace('local hubs = getDialogChoiceHubs()','local hubs = getUnexpectedHubs()'),[Text.UTF8Encoding]::new($false))
$joyReject=$ctor.Invoke([object[]]@([string]$root,[string]'DedraJNCJoytoy',
  [string]'dedra-jnc-joytoy-inactive-ui-draw',
  [Collections.Generic.Dictionary[string,byte[]]]::new([StringComparer]::OrdinalIgnoreCase)))
$joyRejected=$false
try{[void]$joyMethod.Invoke($null,[object[]]@($joyReject))}
catch{$joyRejected=$true}
if(!$joyRejected){throw 'JNC Joytoy unknown author draw source drift was accepted'}
Write-Host 'JNC Joytoy original hidden onDraw fastpath, visible path, input reset, idempotence, source drift PASS'
