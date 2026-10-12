param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)
$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe)) { throw "Resolver missing: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-autoloot-gc-semantic'
if (Test-Path -LiteralPath $root) { Remove-Item $root -Recurse -Force }
$capture = Join-Path $root 'capture'
$mods = Join-Path $root 'mods'
$loot = Join-Path $mods 'AutoLoot'
New-Item -ItemType Directory -Force $capture,(Join-Path $loot 'modules') | Out-Null

# Keep the existing 0-Engine detection/compatibility path intact.
$zero = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zero | Out-Null
$encoded = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$bytes = [Convert]::FromBase64String($encoded)
$input = [IO.MemoryStream]::new($bytes)
$gzip = [IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output = [IO.MemoryStream]::new()
try { $gzip.CopyTo($output); [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray()) }
finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

@'
local logic = require("modules/logic")
local autoLoot = { settings = { useDefaultActionKey = false } }
registerForEvent("onInit", function()
    local gameinputActionTypeBUTTON_PRESSED = gameinputActionType.BUTTON_PRESSED
    local gameinputActionTypeBUTTON_RELEASED = gameinputActionType.BUTTON_RELEASED
    local actionType
    local function handleButtonPressed(this, action) end
    local function handleButtonReleased(this, action) end
    Observe('PlayerPuppet', 'OnAction', function(this, action)
        if not autoLoot.settings.useDefaultActionKey then return end
        actionType = action:GetType(action)
        if actionType == gameinputActionTypeBUTTON_PRESSED then handleButtonPressed(this, action) return end
        if actionType == gameinputActionTypeBUTTON_RELEASED then handleButtonReleased(this, action) return end
    end)
end)
registerForEvent("onUpdate", function(delta)
    logic.tick(delta)
end)
registerForEvent("onDraw", function()
    local _ = autoLoot.settings.useDefaultActionKey
end)
'@ | Set-Content -LiteralPath (Join-Path $loot 'init.lua') -Encoding utf8
$initPath = Join-Path $loot 'init.lua'
$originalInit = Get-Content -LiteralPath $initPath -Raw

# No diagnostic-only journal observer in a clean author installation.
@'
local jmels = {}
function jmels.getAllJournalQuestEntries() return {} end
function jmels.getActiveQuestsMappins() return {} end
return jmels
'@ | Set-Content -LiteralPath (Join-Path $loot 'modules/jmels.lua') -Encoding utf8

# Synthetic source-shaped contract: the real mod is not redistributed.
@'
local logic = {}
local tableInsert = table.insert
local mathAbs = math.abs
local stringFind = string.find
local function vectorDistanceSquared(src, target)
    local dx = target.x - src.x;
    local dy = target.y - src.y;
    local dz = target.z - src.z;
    return dx * dx + dy * dy + dz * dz;
end;
local isGameV23 = true
local mappinSystem = {}
local gamemappinsMappinTargetTypeWorld = 1
local worldMappins
function isWorldMapPinOfTypeInRange(objectPos, range, typeFilterStr1, typeFilterStr2, skipPreCheck)
    if not skipPreCheck then
        if not objectPos then return false end
        if not range then range = 0.5 end
        if type(range) ~= 'number' then range = 0.5 end
        if type(typeFilterStr1) ~= 'string' then typeFilterStr1 = false end
        if type(typeFilterStr2) ~= 'string' then typeFilterStr2 = false end
    end
    local noTypeFilters = not typeFilterStr1 and not typeFilterStr2

    if not worldMappins then worldMappins = {} end
    if isGameV23 then worldMappins = mappinSystem:GetMappinEntries(gamemappinsMappinTargetTypeWorld) else worldMappins = mappinSystem:GetMappins(gamemappinsMappinTargetTypeWorld) end
    if not worldMappins then return false end
    if #worldMappins < 1 then return false end

	local closeMappins = {}
	local worldPosition
    for _, mappin in ipairs(worldMappins) do
        worldPosition = mappin.worldPosition
        if mathAbs(worldPosition.x - objectPos.x) <= range then
            if mathAbs(worldPosition.y - objectPos.y) <= range then
                if mathAbs(worldPosition.z - objectPos.z) <= range then
                    local takeIt = false
                    if noTypeFilters then
                        takeIt = true
                    else
                        if typeFilterStr1 and stringFind(mappin.type.value, typeFilterStr1) then takeIt = true end
                        if takeIt ~= true and typeFilterStr2 and stringFind(mappin.type.value, typeFilterStr2) then takeIt = true end
                    end
                    if takeIt then tableInsert(closeMappins, mappin) end
                end
            end
        end
    end

	local size = #closeMappins
	if size == 0 then return false end
	if size == 1 then return closeMappins[1] end

	lowestDistanceIndex = 0
	lowestDistanceSquared = 1000000000

	for i = 1, size do
		local distSquared = vectorDistanceSquared(objectPos, closeMappins[i].worldPosition)
		if distSquared < lowestDistanceSquared then
			lowestDistanceSquared = distSquared
			lowestDistanceIndex = i
		end
	end

	if lowestDistanceIndex > 0 and lowestDistanceSquared <= range * range then
		return closeMappins[lowestDistanceIndex]
	end
end

-- Author v3.11.x key-triggered loot scan: inventory precedes radius.
local version = "v3.11.1"
function logic.lootInRange(range, lootInView, lootVisible, audioFeedback)
  local lootInRange = range > 0
  local rangeSquared = range * range
  local playerPos = player:GetWorldPosition()
  for ownerHashStr, mappin in pairs(objectMappins) do
    if mappin and mappin.isNew then
      gameObj = findEntityByIdOrHashStr(mappin.entId or ownerHashStr)
      if isObjectOfInterest(gameObj) then
        if isNpcSafeToLoot(gameObj) then
          local hasItemsToLoot, objectHasLootItems, ownerHasLootItems = false, false, false
          result, items = transactionSystem:GetItemList(gameObj)
          if result and #items > 0 then hasItemsToLoot = true objectHasLootItems = true end
          local gameObjOwner = nil
          pcall(function() gameObjOwner = gameObj:GetOwner() end)
          if gameObjOwner then result, items = transactionSystem:GetItemList(gameObjOwner) end
          if result and #items > 0 then hasItemsToLoot = true ownerHasLootItems = true end
          if hasItemsToLoot then
            local takeIt = true
            local gameObjPos
            if lootInRange then
              gameObjPos = gameObj:GetWorldPosition()
              takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared
            end
            if takeIt then
              if lootInView then takeIt = cameraSystem:IsInCameraFrustum(gameObj, 0.5, 0.2) end
              if takeIt then
                if lootVisible then gameObjPos = gameObjPos or gameObj:GetWorldPosition() takeIt = isObjectVisibleToPlayer(gameObj, gameObjPos, player, playerPos, true) end
                if takeIt then
                  result = lootObjectItems(gameObj, forceShard, false, true, playerPos)
                end
              end
            end
          end
        end
      else
        mappin.isNew = false
      end
    end
  end
end

function lootNpc(gameObj, skipPreCheck)
end

function logic.couldStartNewLootingCycle(player) return true end
function logic.tick(dt) end
return logic
'@ | Set-Content -LiteralPath (Join-Path $loot 'modules/logic.lua') -Encoding utf8

$sourcePath = Join-Path $loot 'modules/logic.lua'
$originalHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
$handoff = @{
  schemaVersion = '2.0'
  callbacks = @(@{
    registrationId = 53
    owner = 'AutoLoot'
    infrastructure = $false
    kind = 'event'
    target = 'onUpdate'
    source = @{ file = 'init.lua'; lineStart = 2; lineEnd = 4 }
    callsPerSecond = 60
    exclusiveMsPerSecond = 1.0537
    globalWorkSharePct = 0.5
    familyWorkSharePct = 100
    avgExclusiveUs = 17.56
    maxExclusiveMs = 184.42
    spikeCount = 1
    maxSpikeExclusiveMs = 184.42
  })
  optimizerEvidence = @()
  gcTracer = @{
    contractVersion = '1.0'
    evidenceVocabulary = 'MEGAMIND_CET_MULTISIGNAL_V1'
    noAutomaticRewrites = $true
    authorizesAutomaticRewrite = $false
    metricSemantics = @{
      allocationOperations = 'POSITIVE_GROWTH_ALLOCATOR_REQUESTS_NOT_GC_OBJECT_BIRTHS'
      newGCObjects = 'OBSERVED_GC_OBJECT_CREATION_NOT_TOTAL_ALLOCATIONS'
    }
    registrations = @(@{
      RegistrationId = 53; Mod = 'AutoLoot'
      AllocatedBytes = 18297651; AllocationOperations = 142624
      NewGCObjects = 24513; SampledReclaimedObjects = 40; Focused = $false
    })
  }
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'AutoLoot allocation semantic pass was not generated.' }
$result = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$hit = @($result.callbackFamilies | ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'AutoLoot' -and $_.target -eq 'onUpdate' })[0]
if ($null -eq $hit -or $hit.semantic.RuleId -ne 'autoloot-input-frame-v3' -or
    -not $hit.semantic.SourceProofSatisfied -or -not $hit.semantic.generationReady) {
    throw 'AutoLoot rule failed exact source-proof/admission.'
}
if ($hit.runtime.megaMind.status -ne 'MATCHED_REGISTRATION' -or
    [long]$hit.runtime.megaMind.positiveAllocationOperations -ne 142624 -or
    $hit.runtime.megaMind.influencesGenericOrSemanticAdmission -or
    $hit.runtime.megaMind.authorizesRewrite) {
    throw 'GC evidence must join exactly and cannot independently authorize changes.'
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$transform = @($manifest.transforms | Where-Object {
    $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'autoloot-input-frame-v3'
}) | Select-Object -First 1
if ($null -eq $transform -or @($transform.files).Count -ne 2) {
    throw 'Source-proven AutoLoot transform is missing or touched multiple files.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    $entry = @($zip.Entries | Where-Object {
        $_.FullName.Replace('\','/').EndsWith('AutoLoot/modules/logic.lua',
            [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
    if ($null -eq $entry) { throw 'AutoLoot world-pin source absent from generated pass.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $optimized = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $initEntry = @($zip.Entries | Where-Object {
        $_.FullName.Replace('\','/').EndsWith('AutoLoot/init.lua',
            [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
    if ($null -eq $initEntry) { throw 'AutoLoot input source absent from generated pass.' }
    $initReader = [IO.StreamReader]::new($initEntry.Open())
    try { $routedInit = $initReader.ReadToEnd() } finally { $initReader.Dispose() }
} finally { $zip.Dispose() }

$inputExpect = @('G-CET semantic:autoloot-input-frame-v3',
 'local function zeroEngineOnAction(this, action)',
 'types = { "BUTTON_PRESSED", "BUTTON_RELEASED" }',
 'zeroEngineMod.SubscribeAction(zeroEngineConfig, zeroEngineOnAction)',
 "Observe('PlayerPuppet', 'OnAction', zeroEngineOnAction)",
 'registerRuntimeEvent("onUpdate", function(delta)',
 'registerRuntimeEvent("onDraw", function()',
 'MakeEventRegistrar("AutoLoot", registerForEvent)')
foreach ($item in $inputExpect) {
 if (!$routedInit.Contains($item)) { throw "Original AutoLoot 3.11.x input/frame optimization missing: $item" }
}
if ($routedInit.Contains("Observe('PlayerPuppet', 'OnAction', function(this, action)")) {
 throw 'Unrestricted direct OnAction observer survived 0-Engine routing.'
}
if ($routedInit.Contains('__gcetProbe') -or $routedInit.Contains('gcet_probe')) {
 throw 'Local diagnostic leaked into generated init.'
}
if ((Get-Content -LiteralPath $initPath -Raw) -ne $originalInit) {
 throw 'Resolver modified original AutoLoot init.lua rather than staging a pass.'
}

$world = $optimized.Substring($optimized.IndexOf('function isWorldMapPinOfTypeInRange('))
$world = $world.Substring(0, $world.IndexOf('function logic.couldStartNewLootingCycle('))
$expect = @('G-CET semantic:autoloot-input-frame-v3',
            'if matchingMappins == 1 then return firstMappin end',
            'nearestIndex = matchingMappins',
            'lowestDistanceIndex = nearestIndex',
            'lowestDistanceSquared = nearestDistanceSquared',
            'GetMappinEntries(gamemappinsMappinTargetTypeWorld)',
            'GetMappins(gamemappinsMappinTargetTypeWorld)',
            'if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then',
            'local version = "v3.11.1"')
foreach ($fragment in $expect) {
    if (!$optimized.Contains($fragment)) { throw "AutoLoot required fragment missing: $fragment" }
}
if ($optimized.Contains('__gcetProbe') -or $optimized.Contains('gcet_probe') -or
    $optimized.Contains('GCET_AutoLoot_ProfileDump')) {
    throw 'Production AutoLoot pass contains local action-phase diagnostic instrumentation.'
}
if ($world.Contains('tableInsert(closeMappins') -or $world.Contains('local closeMappins = {}')) {
    throw 'AutoLoot world-pin temporary candidate-table allocation remains.'
}
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'Resolver modified the actual live mod instead of staging a pass.'
}
# Installed optimized state must be recognized, not rewritten again.
$optimized | Set-Content -LiteralPath $sourcePath -Encoding utf8
$routedInit | Set-Content -LiteralPath $initPath -Encoding utf8
$repeat = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
# No-op may emit its JSON error to stderr and leave stdout empty. This is
# correct when the installed source already satisfies the rule, so validate
# the written Resolver report and absence of generated ZIP rather than stdout.
if ($repeat.pass -and $repeat.pass.ZipPath) {
    throw 'Fully optimized AutoLoot was rewritten on its idempotent second run.'
}
$repeatResult = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$again = @($repeatResult.callbackFamilies | ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'AutoLoot' -and $_.target -eq 'onUpdate' })[0]
if ($null -eq $again -or $again.semantic.generationReady -or -not $again.semantic.AlreadySatisfied) {
    throw 'AutoLoot semantic pass is not idempotent.'
}

# Reconstruct already-v1-optimized logic with a pristine init: the same
# onUpdate-admitted v3 rule must generate both input/frame and missing loot work.
# Reconstruct the already-v1-optimized installation: world-pin table elimination
# exists but the hot-key pre-inventory radius cull is still missing.
$v1 = $optimized.Replace(
 'if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then',
 'if isNpcSafeToLoot(gameObj) then').Replace(
 'G-CET semantic:autoloot-input-frame-v3',
 'G-CET semantic:autoloot-world-pin-allocation-scan')
$v1 | Set-Content -LiteralPath $sourcePath -Encoding utf8
$originalInit | Set-Content -LiteralPath $initPath -Encoding utf8

# The temporary journal-event observer used during GC research must not be
# silently carried into a production source-generated pass.
$journalPath = Join-Path $loot 'modules/jmels.lua'
$cleanJournal = Get-Content -LiteralPath $journalPath -Raw
($cleanJournal + "\nlocal __gcetProbe = require('modules/gcet_probe') -- diagnostic observer\n") |
    Set-Content -LiteralPath $journalPath -Encoding utf8
$blocked = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if ($blocked.pass) {
    $blockedManifest = Get-Content -LiteralPath $blocked.pass.ManifestPath -Raw | ConvertFrom-Json
    if (@($blockedManifest.transforms | Where-Object { $_.RuleId -eq 'autoloot-input-frame-v3' }).Count) {
        throw 'Resolver emitted production AutoLoot code while a diagnostic journal observer remained installed.'
    }
}
$cleanJournal | Set-Content -LiteralPath $journalPath -Encoding utf8

$upgrade = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if(!$upgrade.ok -or !$upgrade.pass){throw 'AutoLoot v1 world-pin source was not upgraded with the missing prefilter.'}
$upgradeManifest=Get-Content -LiteralPath $upgrade.pass.ManifestPath -Raw | ConvertFrom-Json
if(@($upgradeManifest.transforms | Where-Object {$_.RuleId -eq 'autoloot-input-frame-v3'}).Count -ne 1){
 throw 'The same combined rule must complete prior v1 semantic work.'
}
$upgradeZip=[IO.Compression.ZipFile]::OpenRead([string]$upgrade.pass.ZipPath)
try {
 $upgradeFile=@($upgradeZip.Entries | Where-Object {$_.FullName.EndsWith('AutoLoot/modules/logic.lua')})[0]
 $upgradeReader=[IO.StreamReader]::new($upgradeFile.Open())
 try {$upgradeSource=$upgradeReader.ReadToEnd()} finally {$upgradeReader.Dispose()}
} finally {$upgradeZip.Dispose()}
if(!$upgradeSource.Contains('if matchingMappins == 1 then return firstMappin end') -or
    !$upgradeSource.Contains('if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then') -or
    $upgradeSource.Contains('if takeIt then tableInsert(closeMappins, mappin) end')){
 throw 'Incremental upgrade lost prior world-pin optimization or failed to install radius guard.'
}


# Finally prove the real user upgrade state: v2 logic is already complete but
# 3.11.1 init.lua remains untouched. The new rule must add missing input/frame
# and retain the previously generated world-pin and early-radius behavior.
$v2 = $optimized.Replace(
 'G-CET semantic:autoloot-input-frame-v3',
 'G-CET semantic:autoloot-active-loot-cycle-v2')
$v2 | Set-Content -LiteralPath $sourcePath -Encoding utf8
$originalInit | Set-Content -LiteralPath $initPath -Encoding utf8
$v2Pass = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$v2Pass.ok -or !$v2Pass.pass) {
 throw 'Already-v2 AutoLoot with native OnAction was not upgraded to the full v3 pass.'
}
$zipV2 = [IO.Compression.ZipFile]::OpenRead([string]$v2Pass.pass.ZipPath)
try {
 $a = @($zipV2.Entries | Where-Object {$_.FullName.EndsWith('AutoLoot/init.lua')})[0]
 $b = @($zipV2.Entries | Where-Object {$_.FullName.EndsWith('AutoLoot/modules/logic.lua')})[0]
 if (!$a -or !$b) { throw 'v2->v3 pass must stage both original source files.' }
 $readerA = [IO.StreamReader]::new($a.Open())
 try { $newInit = $readerA.ReadToEnd() } finally { $readerA.Dispose() }
 $readerB = [IO.StreamReader]::new($b.Open())
 try { $newLogic = $readerB.ReadToEnd() } finally { $readerB.Dispose() }
} finally {$zipV2.Dispose()}
if (!$newInit.Contains('zeroEngineMod.SubscribeAction(zeroEngineConfig, zeroEngineOnAction)') -or
    !$newInit.Contains('registerRuntimeEvent("onUpdate", function(delta)') -or
    !$newInit.Contains('registerRuntimeEvent("onDraw", function()') -or
    !$newLogic.Contains('if matchingMappins == 1 then return firstMappin end') -or
    !$newLogic.Contains('if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then')) {
 throw 'v2->v3 upgrade lost existing loot optimization or failed to route frame/input.'
}

Write-Host 'AutoLoot original 3.11.x full v3, v1/v2 incremental upgrade, no probe, and idempotence contracts passed.'

