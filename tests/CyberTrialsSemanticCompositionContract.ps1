param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) {
    throw "G-CET resolver executable not found: $resolverExe"
}

$root = Join-Path $env:RUNNER_TEMP 'gcet-cybertrials-semantic-composition'
if (Test-Path -LiteralPath $root) {
    Remove-Item -LiteralPath $root -Recurse -Force
}

$capture = Join-Path $root 'CET-20990101-020304_CYBERTRIALS'
$mods = Join-Path $root 'mods'
$cyber = Join-Path $mods 'CyberTrials'
$worldDir = Join-Path $cyber 'modules\external'
$utilsDir = Join-Path $cyber 'modules\utils'
$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$worldDir,$utilsDir,$zeroDir | Out-Null

# Use the exact fixed runtime so generated pass creation exercises the real runtime hash gate.
$encodedInitPath = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
$encodedInit = (Get-Content -LiteralPath $encodedInitPath -Raw).Trim()
$compressedInit = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressedInit)
$gzip = [System.IO.Compression.GZipStream]::new(
    $input,
    [System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes(
        (Join-Path $zeroDir 'init.lua'),
        $output.ToArray())
}
finally {
    $output.Dispose()
    $gzip.Dispose()
    $input.Dispose()
}

$initSource = @'
local Race = {}
local rootStateUpdateCount = 0
local readyToPlace = false
local timeTrials = {
    availableRaces = {},
    runtimeData = { inGame = true, inMenu = false }
}
local world = { interactions = {} }
local interactionUI = {}
local raceLogic = { raceInProgress = false }
local hubs = {}

-- Deliberately use single quotes / unusual spacing: semantic proof must not be formatting locked.
ObserveAfter ( 'BaseMappinBaseController' , 'UpdateRootState' , function( this ) -- author comment
    if rootStateUpdateCount == 0 then
        hubs.setupMappins(timeTrials.availableRaces)
        rootStateUpdateCount = rootStateUpdateCount + 1
    else
        rootStateUpdateCount = 0
    end
end )

registerForEvent ( "onUpdate" , function( dt )
    if #timeTrials.availableRaces > 0 and #world.interactions > 0 then -- compatible version comment
        -- presentation lane
        interactionUI.update()
        world.update()
    end

    local player = Game.GetPlayer()
    if not timeTrials.runtimeData.inMenu and timeTrials.runtimeData.inGame then
        Cron.Update(dt)
        -- frame-fed UI remains realtime
        interactionUI.update()
        world.update()
        if raceLogic.raceInProgress == true then -- compatible version comment
            UsePlayer(player)
        end
    end
end )
'@
$initPath = Join-Path $cyber 'init.lua'
$initSource | Set-Content -LiteralPath $initPath -Encoding utf8

$worldSource = @'
local world = { interactions = {} }

function world.init()
    ObserveAfter ( 'BaseMappinBaseController' , "UpdateRootState" , function( this ) -- changed formatting
        if not Game.GetPlayer() then return end
        if Game.GetPlayer().mountedVehicle then return end
        local mappin = this:GetMappin()
        if not mappin then return end
        local pos = mappin:GetWorldPosition()
        for _, interaction in pairs(world.interactions) do
            if Vector4.Distance(pos, interaction.pos) < 0.05 then
                local record = TweakDBInterface.GetUIIconRecord(interaction.icon)
                this.iconWidget:SetAtlasResource(record:AtlasResourcePath())
                this.iconWidget:SetTexturePart(record:AtlasPartName())
                this.iconWidget:SetTintColor(interaction.iconColor)
            end
        end
    end )
end

return world
'@
$worldPath = Join-Path $worldDir 'world.lua'
$worldSource | Set-Content -LiteralPath $worldPath -Encoding utf8

$hubsSource = @'
local interactionHubs = {}
local raceLogic = { raceInProgress = false }
local mappinIDs = {}

function interactionHubs.setupMappins ( tracksList ) -- renamed compatible parameter
    if not raceLogic.raceInProgress then
        local data = MappinData.new({
            mappinType = 'Mappins.QuestDynamicMappinDefinition',
            variant = gamedataMappinVariant.Zzz18_RacingVariant,
            visibleThroughWalls = false,
            active = true
        })
        for _, track in ipairs(tracksList) do
            local hubdata = track.startInteraction
            local mappinID = Game.GetMappinSystem():RegisterMappin(
                data,
                Vector4.new(hubdata.position.x, hubdata.position.y, hubdata.position.z + 1))
            table.insert(mappinIDs, mappinID)
        end
    else
        for index, mappin in ipairs(mappinIDs) do
            Game.GetMappinSystem():UnregisterMappin(mappin)
            table.remove(mappinIDs, index)
        end
    end
end

return interactionHubs
'@
$hubsPath = Join-Path $utilsDir 'interactionHubs.lua'
$hubsSource | Set-Content -LiteralPath $hubsPath -Encoding utf8

function Find-Range(
    [string]$Text,
    [string]$OpeningPattern,
    [string]$ClosingPattern
) {
    $lines = @($Text -split "\r?\n")
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $OpeningPattern) {
            $start = $i
            break
        }
    }
    if ($start -lt 0) { throw "Opening not found: $OpeningPattern" }

    for ($i = $start + 1; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $ClosingPattern) {
            return @{ Start = $start + 1; End = $i + 1 }
        }
    }
    throw "Closing not found after opening: $OpeningPattern"
}

$initRange = Find-Range $initSource '^\s*registerForEvent\s*\(' '^\s*end\s*\)\s*$'
$worldRange = Find-Range $worldSource '^\s*ObserveAfter\s*\(' '^\s*end\s*\)\s*$'

function CallbackRow(
    [int]$Id,
    [string]$Kind,
    [string]$Target,
    [string]$File,
    [int]$LineStart,
    [int]$LineEnd,
    [double]$Ms
) {
    @{
        registrationId = $Id
        owner = 'CyberTrials'
        infrastructure = $false
        kind = $Kind
        target = $Target
        source = @{ file = $File; lineStart = $LineStart; lineEnd = $LineEnd }
        callsPerSecond = 60.0
        exclusiveMsPerSecond = $Ms
        globalWorkSharePct = 25.0
        familyWorkSharePct = 50.0
        avgExclusiveUs = 100.0
        maxExclusiveMs = 1.0
        spikeCount = 0
        maxSpikeExclusiveMs = 0
    }
}

@{
    schemaVersion = '1.7'
    callbacks = @(
        (CallbackRow 900 'event' 'onUpdate' 'init.lua' $initRange.Start $initRange.End 8.0),
        (CallbackRow 901 'ObserveAfter' 'BaseMappinBaseController::UpdateRootState' 'modules/external/world.lua' $worldRange.Start $worldRange.End 7.0)
    )
    optimizerEvidence = @()
} | ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok) { throw 'CyberTrials Resolver/generator composition run failed.' }
if ($null -eq $resolved.pass) { throw 'CyberTrials composition run returned no generated pass.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
$cyberSemantic = @(
    $resolver.callbackFamilies.topConsumers |
    Where-Object { $_.owner -eq 'CyberTrials' -and $_.semantic.RuleId -eq 'cybertrials' }
)
if ($cyberSemantic.Count -lt 1) {
    throw 'Production CyberTrials semantic rule did not match the compatible-version fixture.'
}
if (@($cyberSemantic | Where-Object { $_.semantic.SourceProofSatisfied }).Count -lt 1) {
    throw 'CyberTrials semantic source proof did not accept the compatible-version fixture.'
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$cyberSkips = @($manifest.skipped | Where-Object { $_.Owner -eq 'CyberTrials' -or $_.owner -eq 'CyberTrials' })
if ($cyberSkips.Count -ne 0) {
    throw "CyberTrials composition unexpectedly skipped: $($cyberSkips.reason -join '; ')"
}

$semanticTransforms = @(
    $manifest.transforms |
    Where-Object { $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'cybertrials' }
)
if ($semanticTransforms.Count -ne 1) {
    throw "Expected exactly one CyberTrials semantic transform, got $($semanticTransforms.Count)."
}

$playerTransforms = @(
    $manifest.transforms |
    Where-Object { $_.type -eq 'SHARED_PROVIDER_READ' -and $_.provider -eq 'PLAYER' -and $_.owner -eq 'CyberTrials' }
)
if ($playerTransforms.Count -lt 2) {
    throw 'CyberTrials fixture did not compose PLAYER substitutions before semantic injection.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$EntryName) {
        $entry = $zip.GetEntry($EntryName)
        if ($null -eq $entry) { throw "ZIP entry not found: $EntryName" }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    }

    $base = 'bin/x64/plugins/cyber_engine_tweaks/mods/CyberTrials/'
    $initText = Read-ZipText ($base + 'init.lua')
    $worldText = Read-ZipText ($base + 'modules/external/world.lua')
    $hubsText = Read-ZipText ($base + 'modules/utils/interactionHubs.lua')

    foreach ($text in @($initText,$worldText,$hubsText)) {
        if ($text -notmatch [regex]::Escape('G-CET semantic:cybertrials')) {
            throw 'CyberTrials semantic marker did not cover all three changed files.'
        }
    }

    if ($initText -notmatch '__gcetGetPlayer\(\)' -or
        $initText -notmatch '__gcetCyberTrialsWorldElapsed' -or
        $initText -match 'rootStateUpdateCount\s*==\s*0') {
        throw 'CyberTrials init.lua did not compose PLAYER + semantic cadence transforms.'
    }

    if ($worldText -notmatch '__gcetGetPlayer\(\)' -or
        $worldText -notmatch 'Custom race pin texture' -or
        $worldText -notmatch 'Zzz18_RacingVariant' -or
        $worldText -match 'Vector4\.Distance\(pos,\s*interaction\.pos\)') {
        throw 'CyberTrials world observer was not structurally replaced after PLAYER staging.'
    }

    if ($hubsText -notmatch 'local function clearMappins\(\)' -or
        $hubsText -notmatch '#tracksList' -or
        $hubsText -notmatch 'function interactionHubs\.setupMappins\s*\(\s*tracksList\s*\)') {
        throw 'CyberTrials setupMappins injection did not tolerate the renamed compatible parameter.'
    }
}
finally {
    $zip.Dispose()
}

Write-Host 'CyberTrials semantic composition contract passed: version-tolerant structure + PLAYER staging + semantic generation.'
