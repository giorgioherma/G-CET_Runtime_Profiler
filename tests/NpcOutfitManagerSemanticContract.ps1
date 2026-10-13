param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$exe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path $exe)) { throw "Missing resolver $exe" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Join-Path $env:RUNNER_TEMP 'npc-outfit-manager-source-contract'
if (Test-Path $root) { Remove-Item $root -Recurse -Force }
$cap = Join-Path $root 'capture'
$mods = Join-Path $root 'mods'
$npc = Join-Path $mods 'NPC Outfit Manager'
New-Item -ItemType Directory -Force $cap,$npc | Out-Null

$original = @'
local autoOutfitsData = {settings = {}, outfits = {}}
local radarTimer = 0
local activeNPCs = {}
local currentSelections = {}
local activeRefits = {}
local activeBodyTypes = {}
local equippedSavedMeshes = {}
local nativeMeshHiddenStates = {}
local hiddenRefits = {}
local bodyPartStates = {} -- current editor state
local Cron = {Update = function(_) end}
local function GetLookAtNPC() return nil end
local function ProcessRadarOutfits() end
local function authorEdit(targetHash)
    currentSelections[targetHash] = {}
    currentSelections[targetHash]["Head"] = "Example"
    activeRefits[targetHash] = {}
    activeRefits[targetHash]["Hair"] = "Example"
    activeBodyTypes[targetHash] = "Example"
    equippedSavedMeshes[targetHash] = {}
    equippedSavedMeshes[targetHash]["Base"] = true
    nativeMeshHiddenStates[targetHash] = {}
    nativeMeshHiddenStates[targetHash]["A"] = false
    hiddenRefits[targetHash] = {}
    hiddenRefits[targetHash]["B"] = true
    bodyPartStates[targetHash] = {}
    bodyPartStates[targetHash]["Head"] = false
end
registerForEvent("onDraw", function()
    if not isCustomizeWindowOpen then return end
    local target = GetLookAtNPC()
end)
registerForEvent("onUpdate", function(delta)
    Cron.Update(delta)
    local currentTarget = GetLookAtNPC()
    if currentTarget then
        local entIdOk, entIdStr = pcall(function() return tostring(currentTarget:GetEntityID().hash) end)
        if entIdOk and entIdStr and not activeNPCs[entIdStr] then
            activeNPCs[entIdStr] = currentTarget
        end
    end

    radarTimer = radarTimer + delta
    if radarTimer >= 1.0 then
        radarTimer = 0.0
        ProcessRadarOutfits()
    end

    if isAnimPlaying then
        if currentTarget then end
    end
end)
'@
$path = Join-Path $npc 'init.lua'
$original | Set-Content -LiteralPath $path -Encoding utf8
$input = @{
  schemaVersion = '2.0'
  callbacks = @(@{
      registrationId = 71; owner='NPC Outfit Manager'; infrastructure=$false
      kind='event'; target='onUpdate'
      source=@{file='init.lua';lineStart=30;lineEnd=55}
      callsPerSecond=60;exclusiveMsPerSecond=0.40
      globalWorkSharePct=1;familyWorkSharePct=100
      avgExclusiveUs=6.6;maxExclusiveMs=17;spikeCount=1
      maxSpikeExclusiveMs=17
    })
  optimizerEvidence = @()
} | ConvertTo-Json -Depth 20
$input | Set-Content -LiteralPath (Join-Path $cap 'CET_Resolver_Input.json') -Encoding utf8

function Invoke-Variant([string]$label, [string]$source, [bool]$expected) {
    $source | Set-Content -LiteralPath $path -Encoding utf8
    $sourceHash = (Get-FileHash $path -Algorithm SHA256).Hash
    $run = (& $exe --capture $cap --mods $mods --generate-pass --json | ConvertFrom-Json)
    if (!$run.ok) { throw "NPC $label Resolver failed: $($run | ConvertTo-Json -Depth 5)" }
    $rules = @()
    if ($run.pass -and $run.pass.ManifestPath) {
        $manifest = Get-Content -LiteralPath $run.pass.ManifestPath -Raw | ConvertFrom-Json
        $rules = @($manifest.transforms | Where-Object {
            $_.type -eq 'SEMANTIC_RULE' -and $_.RuleId -eq 'npc-outfit-manager-idle-state'
        })
    }
    if ($rules.Count -ne [int]$expected) {
        throw "NPC $label expected semantic $expected, found $($rules.Count)"
    }
    if ((Get-FileHash $path -Algorithm SHA256).Hash -ne $sourceHash) {
        throw "NPC $label modified live author source"
    }
    if (!$expected) { return $null }
    if (@($rules[0].files).Count -ne 1) { throw "NPC $label changed more than one file" }
    $zip = [IO.Compression.ZipFile]::OpenRead([string]$run.pass.ZipPath)
    try {
        $entry = @($zip.Entries | Where-Object {
            $_.FullName.Replace('\','/').EndsWith('NPC Outfit Manager/init.lua',
                [StringComparison]::OrdinalIgnoreCase)
        }) | Select-Object -First 1
        if (!$entry) { throw "NPC $label source missing in generated pass" }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { $out = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
    foreach ($token in @(
        'G-CET semantic:npc-outfit-manager-idle-state',
        'local __gcetHasAutoOutfitRules',
        'next(autoOutfitsData.outfits) ~= nil',
        'if __gcetHasAutoOutfitRules then',
        'local __gcetLastEditedOutfits = {}',
        '__gcetOutfitEdited(targetHash)',
        'Cron.Update(delta)',
        'ProcessRadarOutfits()',
        'isAnimPlaying',
        'isCustomizeWindowOpen'
    )) {
        if (!$out.Contains($token)) { throw "NPC $label lost $token" }
    }
    if (([regex]::Matches($out,'__gcetOutfitEdited\(targetHash\)')).Count -lt 10) {
        throw "NPC $label missing mutation-driven revisions"
    }
    return $out
}
$optimized = Invoke-Variant 'original' $original $true
Invoke-Variant 'idempotent' $optimized $false | Out-Null
# Source formatting is changed without changing semantics: no fixed hash/version.
$spaceVariant = $original.Replace('local currentTarget = GetLookAtNPC()',
    'local   currentTarget= GetLookAtNPC ( )')
$spaceVariant = $spaceVariant.Replace('radarTimer = radarTimer + delta',
    'radarTimer  =  radarTimer + delta')
Invoke-Variant 'reformatted author update' $spaceVariant $true | Out-Null
$drift = $original.Replace('local currentTarget = GetLookAtNPC()',
    'local currentTarget = ChangedTargetingImplementation()')
Invoke-Variant 'unknown targeting semantics' $drift $false | Out-Null

Write-Host 'NPC Outfit Manager source pattern, idempotence, author-drift and unchanged-live-file contract passed.'
