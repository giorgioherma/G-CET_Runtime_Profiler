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

$root = Join-Path $env:RUNNER_TEMP 'gcet-generic-ui-sequence-contract'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-080808_GENERIC_UI_SEQUENCE'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray())
}
finally {
    $output.Dispose(); $gzip.Dispose(); $input.Dispose()
}

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source) {
    $path = Join-Path (Join-Path $mods $Mod) $Relative
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    $Source | Set-Content -LiteralPath $path -Encoding utf8
}

Write-ModFile 'GenericUiDormancyFixture' 'init.lua' @'
local visible = false

registerForEvent("onOverlayOpen", function()
    visible = true
end)

registerForEvent("onOverlayClose", function()
    visible = false
end)

registerForEvent("onDraw", function()
    if visible then
        ImGui.Text("Visible fixture")
        ImGui.Text("Second line")
    end
end)
'@

Write-ModFile 'GenericCrossFileUiFixture' 'init.lua' @'
local ui = require("modules/interactionUI")

registerForEvent("onDraw", function()
    if ui and ui.update then ui.update() end
end)
'@

Write-ModFile 'GenericCrossFileUiFixture' 'modules/interactionUI.lua' @'
local ui = {
    hubShown = false,
    customHubSelected = false,
    input = false,
    selectedIndex = 0,
    hub = { id = 1 }
}

local function getDialogChoiceHubs()
    return {}
end

local function updateSelectedHub(id) end
local function updateSelectedIndex(index) end
local function getActiveChoiceHubID() return 0 end

function ui.showHub()
    ui.hubShown = true
end

function ui.update()
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
    ui.input = false
end

return ui
'@

Write-ModFile 'GenericCrossFileUiRejectFixture' 'init.lua' @'
local ui = require("modules/interactionUI")

registerForEvent("onDraw", function()
    if ui and ui.update then ui.update() end
end)
'@

Write-ModFile 'GenericCrossFileUiRejectFixture' 'modules/interactionUI.lua' @'
local ui = {
    hubShown = false,
    customHubSelected = false,
    input = false,
    hiddenMaintenance = 0,
    hub = { id = 1 }
}

local function getDialogChoiceHubs()
    return {}
end

function ui.showHub()
    ui.hubShown = true
end

function ui.update()
    local hubs = getDialogChoiceHubs()

    if ui.hubShown and #hubs == 0 then
        ui.customHubSelected = true
    elseif ui.hubShown then
        ui.customHubSelected = false
    end

    -- This hidden-path side effect makes call-site dormancy unsafe.
    ui.hiddenMaintenance = ui.hiddenMaintenance + 1
    ui.input = false
end

return ui
'@

Write-ModFile 'GenericSequenceFixture' 'init.lua' @'
local lastSeq = -1

local function expensiveWork()
    local player = Game.GetPlayer()
    if player then
        player:GetWorldPosition()
    end
end

registerForEvent("onUpdate", function(dt)
    local ok, bridge = pcall(function()
        local container = Game.GetScriptableSystemsContainer()
        if not container then return nil end
        return container:Get(CName.new("GenericSequenceFixture.State"))
    end)
    if not ok or not bridge then return end
    local seq = bridge.seq
    if seq == lastSeq then return end
    lastSeq = seq

    expensiveWork()
end)
'@

function Find-Line([string]$Path,[string]$Pattern) {
    $lines=@(Get-Content -LiteralPath $Path)
    for($i=0;$i -lt $lines.Count;$i++) {
        if($lines[$i] -match $Pattern) { return $i+1 }
    }
    throw "Pattern not found in $Path : $Pattern"
}

function CallbackRow(
    [int]$Id,[string]$Owner,[string]$Kind,[string]$Target,
    [string]$File,[int]$Line,[double]$Ms,[double]$Calls
) {
    @{
        registrationId=$Id; owner=$Owner; infrastructure=$false
        kind=$Kind; target=$Target
        source=@{file=$File;lineStart=$Line;lineEnd=$Line+16}
        callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms
        globalWorkSharePct=2.5; familyWorkSharePct=100
        avgExclusiveUs=100; maxExclusiveMs=1; spikeCount=0; maxSpikeExclusiveMs=0
    }
}

$uiLine=Find-Line (Join-Path $mods 'GenericUiDormancyFixture\init.lua') 'registerForEvent\("onDraw"'
$crossUiLine=Find-Line (Join-Path $mods 'GenericCrossFileUiFixture\init.lua') 'registerForEvent\("onDraw"'
$crossUiRejectLine=Find-Line (Join-Path $mods 'GenericCrossFileUiRejectFixture\init.lua') 'registerForEvent\("onDraw"'
$seqLine=Find-Line (Join-Path $mods 'GenericSequenceFixture\init.lua') 'registerForEvent\("onUpdate"'

$handoff=@{
    schemaVersion='1.8'
    callbacks=@(
        (CallbackRow 9001 'GenericUiDormancyFixture' 'event' 'onDraw' 'init.lua' $uiLine 6.0 60),
        (CallbackRow 9002 'GenericSequenceFixture' 'event' 'onUpdate' 'init.lua' $seqLine 6.0 60),
        (CallbackRow 9003 'GenericCrossFileUiFixture' 'event' 'onDraw' 'init.lua' $crossUiLine 6.0 60),
        (CallbackRow 9004 'GenericCrossFileUiRejectFixture' 'event' 'onDraw' 'init.lua' $crossUiRejectLine 6.0 60)
    )
    optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) {
    throw 'Generic UI/sequence pass generation failed.'
}

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json

$uiConsumer = @($resolver.callbackFamilies |
    Where-Object { $_.target -eq 'onDraw' } |
    ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'GenericUiDormancyFixture' })[0]
if($null -eq $uiConsumer) { throw 'Generic UI fixture was not resolved.' }
if(-not @($uiConsumer.generic.RecipeFamilies | Where-Object { $_ -eq 'UI_VISIBILITY_DORMANCY' })) {
    throw "Generic UI fixture did not receive UI_VISIBILITY_DORMANCY. Pattern=$($uiConsumer.generic.Pattern)"
}
if($uiConsumer.generic.Facts.uiVisibilityGate -ne 'visible') {
    throw "Generic UI gate proof mismatch: $($uiConsumer.generic.Facts.uiVisibilityGate)"
}

$crossUiConsumer = @($resolver.callbackFamilies |
    Where-Object { $_.target -eq 'onDraw' } |
    ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'GenericCrossFileUiFixture' })[0]
if($null -eq $crossUiConsumer) { throw 'Cross-file UI fixture was not resolved.' }
if(-not @($crossUiConsumer.generic.RecipeFamilies | Where-Object {
    $_ -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD'
})) {
    throw "Cross-file UI fixture did not receive the call-guard recipe. Pattern=$($crossUiConsumer.generic.Pattern)"
}
if($crossUiConsumer.generic.Facts.crossFileInteractionUiGate -ne 'ui.hubShown' -or
   $crossUiConsumer.generic.Facts.crossFileInteractionUiPending -ne 'ui.input' -or
   $crossUiConsumer.generic.Facts.crossFileInteractionUiModuleFile -ne 'GenericCrossFileUiFixture/modules/interactionUI.lua') {
    throw 'Cross-file UI proof facts are incomplete.'
}

$crossUiReject = @($resolver.callbackFamilies |
    Where-Object { $_.target -eq 'onDraw' } |
    ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'GenericCrossFileUiRejectFixture' })[0]
if($null -eq $crossUiReject) { throw 'Cross-file UI reject fixture was not resolved.' }
if(@($crossUiReject.generic.RecipeFamilies | Where-Object {
    $_ -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD'
}).Count -ne 0) {
    throw 'Cross-file UI AUTO accepted a helper with hidden-path side effects.'
}

$seqConsumer = @($resolver.callbackFamilies |
    Where-Object { $_.target -eq 'onUpdate' } |
    ForEach-Object { $_.topConsumers } |
    Where-Object { $_.owner -eq 'GenericSequenceFixture' })[0]
if($null -eq $seqConsumer) { throw 'Generic sequence fixture was not resolved.' }
if(-not @($seqConsumer.generic.RecipeFamilies | Where-Object { $_ -eq 'SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL' })) {
    throw "Generic sequence fixture did not receive SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL. Pattern=$($seqConsumer.generic.Pattern)"
}
if($seqConsumer.generic.Facts.sequenceSystemName -ne 'GenericSequenceFixture.State' -or
   $seqConsumer.generic.Facts.sequenceMember -ne 'seq') {
    throw 'Generic sequence source proof facts are incomplete.'
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
if(@($manifest.transforms | Where-Object {
    $_.type -eq 'UI_VISIBILITY_DORMANCY' -and $_.owner -eq 'GenericUiDormancyFixture'
}).Count -ne 1) {
    throw 'Generated pass is missing UI_VISIBILITY_DORMANCY.'
}
if(@($manifest.transforms | Where-Object {
    $_.type -eq 'SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL' -and $_.owner -eq 'GenericSequenceFixture'
}).Count -ne 1) {
    throw 'Generated pass is missing SEQUENCE_FIRST_SCRIPTABLE_SYSTEM_POLL.'
}
if(@($manifest.transforms | Where-Object {
    $_.type -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD' -and $_.owner -eq 'GenericCrossFileUiFixture'
}).Count -ne 1) {
    throw 'Generated pass is missing CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD.'
}
if(@($manifest.transforms | Where-Object {
    $_.type -eq 'CROSS_FILE_INTERACTION_UI_IDLE_CALL_GUARD' -and $_.owner -eq 'GenericCrossFileUiRejectFixture'
}).Count -ne 0) {
    throw 'Generated pass incorrectly emitted a cross-file UI guard for the reject fixture.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$Name) {
        $e=$zip.GetEntry($Name)
        if($null -eq $e) { throw "ZIP entry missing: $Name" }
        $r=[System.IO.StreamReader]::new($e.Open())
        try { return $r.ReadToEnd() } finally { $r.Dispose() }
    }

    $base='bin/x64/plugins/cyber_engine_tweaks/mods/'
    $ui=Read-ZipText ($base+'GenericUiDormancyFixture/init.lua')
    if($ui -notmatch 'if not visible then return end' -or
       $ui -match 'if visible then') {
        throw 'Generated UI dormancy rewrite did not hoist the visibility gate.'
    }

    $crossUi=Read-ZipText ($base+'GenericCrossFileUiFixture/init.lua')
    if($crossUi -notmatch 'ui\.hubShown or ui\.input' -or
       $crossUi -notmatch 'if ui and ui\.update and') {
        throw 'Generated cross-file UI call guard is incomplete.'
    }

    $crossUiReject=Read-ZipText ($base+'GenericCrossFileUiRejectFixture/init.lua')
    if($crossUiReject -match 'ui\.hubShown or ui\.input') {
        throw 'Reject fixture received the cross-file UI call guard.'
    }

    $seq=Read-ZipText ($base+'GenericSequenceFixture/init.lua')
    if($seq -notmatch '__gcetSequenceSystem_' -or
       $seq -notmatch 'GetScriptableSystem\("GenericSequenceFixture\.State"\)' -or
       $seq -notmatch 'if seq == lastSeq then return end') {
        throw 'Generated sequence-first rewrite is incomplete.'
    }

    $engine=Read-ZipText ($base+'0-Engine/init.lua')
    if($engine -notmatch 'function Engine\.GetScriptableSystem\(name\)' -or
       $engine -notmatch '__gcetScriptableSystemValues') {
        throw 'Fixed 0-Engine runtime is missing keyed ScriptableSystem caching.'
    }
}
finally {
    $zip.Dispose()
}

Write-Host 'Generic UI visibility + cross-file interaction UI + sequence-first ScriptableSystem polling contract passed.'
