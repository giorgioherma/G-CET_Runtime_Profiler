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
registerForEvent("onUpdate", function(delta)
    logic.tick(delta)
end)
'@ | Set-Content -LiteralPath (Join-Path $loot 'init.lua') -Encoding utf8

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
-- Independent quest-pin helper retains its own collection; never rewrite it.
local function questPinCandidates()
    local closeMappins = {}
    return closeMappins
end
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


function logic.lootInRange(player, range, lootInRange)
    local playerPos = player:GetWorldPosition()
    local rangeSquared = range * range
    if objectMappinsCount > 0 then
        for ownerHashStr, mappin in pairs(objectMappins) do
            if mappin and mappin.isNew then
                gameObj = findEntityByIdOrHashStr(mappin.entId or ownerHashStr)
                if isObjectOfInterest(gameObj) then
					if isNpcSafeToLoot(gameObj) then
						local hasItemsToLoot, objectHasLootItems, ownerHasLootItems = false, false, false
						result, items = transactionSystem:GetItemList(gameObj)
						if result and #items > 0 then hasItemsToLoot = true objectHasLootItems = true end
						local gameObjOwner = nil
						result = false
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
                            end
                        end
                    end
                end
            end
        end
    end
end

function logic.couldStartNewLootingCycle(player) return true end
function logic.tick(dt) end
return logic
'@ | Set-Content -LiteralPath (Join-Path $loot 'modules/logic.lua') -Encoding utf8

$sourcePath = Join-Path $loot 'modules/logic.lua'
$originalText = Get-Content -LiteralPath $sourcePath -Raw
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
if ($null -eq $hit -or $hit.semantic.RuleId -ne 'autoloot-world-pin-and-early-distance' -or
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
    $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'autoloot-world-pin-and-early-distance'
}) | Select-Object -First 1
if ($null -eq $transform -or @($transform.files).Count -ne 1) {
    throw 'Source-proven AutoLoot transform is missing or touched multiple files.'
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    $entry = @($zip.Entries | Where-Object {
        $_.FullName.Replace('\','/').EndsWith('AutoLoot/modules/logic.lua',
            [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
    if ($null -eq $entry) { throw 'AutoLoot composed logic source absent from generated pass.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $optimized = $reader.ReadToEnd() } finally { $reader.Dispose() }
} finally { $zip.Dispose() }

$world = $optimized.Substring($optimized.IndexOf('function isWorldMapPinOfTypeInRange('))
$world = $world.Substring(0, $world.IndexOf('function logic.couldStartNewLootingCycle('))
$expect = @('G-CET semantic:autoloot-world-pin-and-early-distance',
            'if matchingMappins == 1 then return firstMappin end',
            'nearestIndex = matchingMappins',
            'lowestDistanceIndex = nearestIndex',
            'lowestDistanceSquared = nearestDistanceSquared',
            'GetMappinEntries(gamemappinsMappinTargetTypeWorld)',
            'GetMappins(gamemappinsMappinTargetTypeWorld)')
foreach ($fragment in $expect) {
    if (!$optimized.Contains($fragment)) { throw "AutoLoot required fragment missing: $fragment" }
}
if ($world.Contains('tableInsert(closeMappins') -or $world.Contains('local closeMappins = {}')) {
    throw 'AutoLoot world-pin temporary candidate-table allocation remains.'
}
if (-not $world.Contains('local firstMappin, nearestMappin') -or
    $world.Contains('local closeMappins = {}')) {
    throw 'AutoLoot world-pin allocation elimination is missing.'
}
$gateIndex = $optimized.IndexOf('takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared')
$inventoryIndex = $optimized.IndexOf('result, items = transactionSystem:GetItemList(gameObj)')
if ($gateIndex -lt 0 -or $inventoryIndex -lt 0 -or $gateIndex -ge $inventoryIndex) {
    throw 'AutoLoot radius gate must be before both GetItemList calls.'
}
if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'Resolver modified the actual live mod instead of staging a pass.'
}
# Installed optimized state must be recognized, not rewritten again.
$optimized | Set-Content -LiteralPath $sourcePath -Encoding utf8
$repeat = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
# A fully optimized AutoLoot world-pin semantic must be idempotent.
# The Resolver can still generate other independent (e.g. generic 0-Engine)
# transforms from the same synthetic capture, so a ZIP is not itself a failure.
if ($repeat.pass -and $repeat.pass.ManifestPath) {
    $repeatManifest = Get-Content -LiteralPath $repeat.pass.ManifestPath -Raw | ConvertFrom-Json
    $repeatedWorldPin = @($repeatManifest.transforms | Where-Object {
        $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'autoloot-world-pin-and-early-distance'
    })
    if ($repeatedWorldPin.Count -gt 0) {
        throw 'Already optimized AutoLoot world-pin semantic was emitted twice.'
    }
}
$repeatResult = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$again = @($repeatResult.callbackFamilies | ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'AutoLoot' -and $_.target -eq 'onUpdate' })[0]
if ($null -eq $again -or $again.semantic.generationReady -or -not $again.semantic.AlreadySatisfied) {
    throw 'AutoLoot semantic pass is not idempotent.'
}
# Incremental source: prior world-pin optimization installed, early-radius gate absent.
# The same combined semantic must add ONLY radius first, retaining scalar pins.
$worldOriginalStart = $originalText.IndexOf('function isWorldMapPinOfTypeInRange(')
$worldOriginalEnd = $originalText.IndexOf('function logic.lootInRange(')
$worldOptimizedStart = $optimized.IndexOf('function isWorldMapPinOfTypeInRange(')
$worldOptimizedEnd = $optimized.IndexOf('function logic.lootInRange(')
if ($worldOriginalStart -lt 0 -or $worldOriginalEnd -lt 0 -or
    $worldOptimizedStart -lt 0 -or $worldOptimizedEnd -lt 0) {
    throw 'World-pin incremental source boundaries unavailable.'
}
$worldOriginal = $originalText.Substring($worldOriginalStart,$worldOriginalEnd-$worldOriginalStart)
$worldOptimized = $optimized.Substring($worldOptimizedStart,$worldOptimizedEnd-$worldOptimizedStart)
# The new *independent* GC owner helper is inserted immediately BEFORE the
# lootInRange function. This world-pin-only historical fixture must carry
# only the world-pin stage, not half of the newer protected-owner stage.
$ownerHelperStart = $worldOptimized.IndexOf('-- G-CET: same protected native owner lookup;', [StringComparison]::Ordinal)
if ($ownerHelperStart -ge 0) {
    $worldOptimized = $worldOptimized.Substring(0, $ownerHelperStart)
}
$legacy = "-- G-CET semantic:autoloot-world-pin-allocation-scan`n" +
    $originalText.Replace($worldOriginal,$worldOptimized)
$legacy | Set-Content -LiteralPath $sourcePath -Encoding utf8
$incremental = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$incremental.pass) { throw 'AutoLoot world-pin-only incremental upgrade produced no pass.' }
$incManifest = Get-Content -LiteralPath $incremental.pass.ManifestPath -Raw | ConvertFrom-Json
$incRule = @($incManifest.transforms | Where-Object {
    $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'autoloot-world-pin-and-early-distance'
})
if ($incRule.Count -ne 1) { throw 'AutoLoot incremental pass did not apply once.' }
$incZip = [IO.Compression.ZipFile]::OpenRead([string]$incremental.pass.ZipPath)
try {
    $incEntry = @($incZip.Entries | Where-Object {
        $_.FullName.Replace('\\','/').EndsWith('AutoLoot/modules/logic.lua',
            [StringComparison]::OrdinalIgnoreCase) }) | Select-Object -First 1
    if (!$incEntry) { throw 'AutoLoot incremental logic.lua missing.' }
    $incReader = [IO.StreamReader]::new($incEntry.Open())
    try { $incText = $incReader.ReadToEnd() } finally { $incReader.Dispose() }
} finally { $incZip.Dispose() }
$incWorld = [regex]::Match($incText,
    '(?ms)^function\s+isWorldMapPinOfTypeInRange\([^\r\n]*\).*?^end\s*;?\s*$').Value
if (!$incWorld -or $incWorld.Contains('local closeMappins = {}') -or
    !$incWorld.Contains('local firstMappin, nearestMappin') -or
    $incText.IndexOf('takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared') -ge
    $incText.IndexOf('result, items = transactionSystem:GetItemList(gameObj)')) {
    throw 'AutoLoot incremental pass changed the wrong stage or lost scalar pins.'
}
if (!$incText.Contains('local closeMappins = {}')) {
    throw 'AutoLoot incorrectly removed an unrelated quest-pin buffer.'
}

# Composition contract: source stages in either order, old/combined/no marker,
# harmless whitespace and renamed NEXT helper, plus unrelated quest-pin lists.
function Verify-Variant([string]$label,[string]$source,[bool]$emit) {
    $source | Set-Content -LiteralPath $sourcePath -Encoding utf8
    $hash=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $run=(& $resolverExe --capture $capture --mods $mods --generate-pass --json |
        ConvertFrom-Json)
    if ($emit -and (!$run.ok -or !$run.pass)) {
        throw "AutoLoot $label should generate the missing stage but did not."
    }
    $transforms=@()
    if ($run.pass -and $run.pass.ManifestPath) {
        $manifest=Get-Content -LiteralPath $run.pass.ManifestPath -Raw | ConvertFrom-Json
        $transforms=@($manifest.transforms | Where-Object {
            $_.type -eq 'SEMANTIC_RULE' -and
            $_.RuleId -eq 'autoloot-world-pin-and-early-distance'
        })
    }
    if ($transforms.Count -ne [int]$emit) {
        throw "AutoLoot $label expected emission=$emit; actual=$($transforms.Count)"
    }
    $report=Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw |
        ConvertFrom-Json
    $row=@($report.callbackFamilies | ForEach-Object {$_.topConsumers} |
        Where-Object {$_.owner -eq 'AutoLoot' -and $_.target -eq 'onUpdate'})[0]
    if (!$emit -and (!$row.semantic.AlreadySatisfied -or
        $row.semantic.AlreadySatisfiedMode -ne 'SCOPED_SOURCE_STAGES')) {
        throw "AutoLoot $label failed stage-aware idempotence."
    }
    if ($emit) {
        $zip=[IO.Compression.ZipFile]::OpenRead([string]$run.pass.ZipPath)
        try {
            $entry=@($zip.Entries | Where-Object {
                $_.FullName.Replace('\','/').EndsWith(
                    'AutoLoot/modules/logic.lua',[StringComparison]::OrdinalIgnoreCase)
            }) | Select-Object -First 1
            if (!$entry) {throw "AutoLoot $label generated source missing"}
            $reader=[IO.StreamReader]::new($entry.Open())
            try {$outputText=$reader.ReadToEnd()} finally {$reader.Dispose()}
        } finally {$zip.Dispose()}
        $worldStage=[regex]::Match($outputText,
            '(?ms)^function\s+isWorldMapPinOfTypeInRange\([^\r\n]*\).*?^end[ \t]*;?[ \t]*\r?$').Value
        if (!$worldStage -or $worldStage.Contains('local closeMappins = {}') -or
            !$worldStage.Contains('local firstMappin, nearestMappin') -or
            $outputText.IndexOf(
               'takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared') -ge
            $outputText.IndexOf(
               'result, items = transactionSystem:GetItemList(gameObj)') -or
            !$outputText.Contains('local closeMappins = {}')) {
            throw "AutoLoot $label lost a stage or unrelated quest helper."
        }
    }
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne $hash) {
        throw "AutoLoot $label modified live source."
    }
}
$origWorld=[regex]::Match($originalText,
    '(?ms)^function\s+isWorldMapPinOfTypeInRange\([^\r\n]*\).*?^end[ \t]*;?[ \t]*\r?$').Value
$doneWorld=[regex]::Match($optimized,
    '(?ms)^function\s+isWorldMapPinOfTypeInRange\([^\r\n]*\).*?^end[ \t]*;?[ \t]*\r?$').Value
if (!$origWorld -or !$doneWorld) {throw 'Variant world functions unavailable.'}
$radiusOnly=$optimized.Replace($doneWorld,$origWorld)
$worldOnly=$originalText.Replace($origWorld,$doneWorld)
$worldOnlyMarked='-- G-CET semantic:autoloot-world-pin-and-early-distance'+
    [Environment]::NewLine+$worldOnly
$whitespaceVariant=$worldOnlyMarked.Replace([string]([char]9),"    ").Replace(
    'function logic.couldStartNewLootingCycle(',
    'function logic.authorChangedNextHelper(')
$unmarkedComplete=[regex]::Replace($optimized,
    '(?m)^-- G-CET semantic:autoloot-world-pin-and-early-distance\r?\n','')
Verify-Variant 'original' $originalText $true
Verify-Variant 'world-only' $worldOnly $true
Verify-Variant 'world-only-marked' $worldOnlyMarked $true
Verify-Variant 'world-only-reformatted-next-function-renamed' $whitespaceVariant $true
Verify-Variant 'radius-only-marked' $radiusOnly $true
Verify-Variant 'complete-marked' $optimized $false
Verify-Variant 'complete-unmarked' $unmarkedComplete $false

Write-Host 'AutoLoot two-stage version-fluent partial/marker/composition contract passed.'
