param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) { throw "Resolver missing: $resolverExe" }

$root = Join-Path $env:RUNNER_TEMP 'gcet-generic-hard-dormant-contract'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-101010_GENERIC_HARD_DORMANT'
$mods = Join-Path $root 'mods'
New-Item -ItemType Directory -Force $capture,$mods | Out-Null

$zeroDir = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zeroDir | Out-Null
$encodedInit = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$compressed = [Convert]::FromBase64String($encodedInit)
$input = [System.IO.MemoryStream]::new($compressed)
$gzip = [System.IO.Compression.GZipStream]::new($input,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try { $gzip.CopyTo($output); [System.IO.File]::WriteAllBytes((Join-Path $zeroDir 'init.lua'),$output.ToArray()) }
finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }

function Write-ModFile([string]$Mod,[string]$Relative,[string]$Source) {
    $path = Join-Path (Join-Path $mods $Mod) $Relative
    New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
    $Source | Set-Content -LiteralPath $path -Encoding utf8
}
function Find-Line([string]$Path,[string]$Pattern) {
    $lines=@(Get-Content -LiteralPath $Path)
    for($i=0;$i -lt $lines.Count;$i++) { if($lines[$i] -match $Pattern) { return $i+1 } }
    throw "Pattern not found: $Pattern in $Path"
}
function CallbackRow([int]$Id,[string]$Owner,[string]$File,[int]$Line,[double]$Ms) {
    @{
        registrationId=$Id; owner=$Owner; infrastructure=$false
        kind='event'; target='onUpdate'
        source=@{file=$File;lineStart=$Line;lineEnd=$Line+14}
        callsPerSecond=60; exclusiveMsPerSecond=$Ms
        globalWorkSharePct=2.5; familyWorkSharePct=100
        avgExclusiveUs=100; maxExclusiveMs=1; spikeCount=0; maxSpikeExclusiveMs=0
    }
}

# POSITIVE: the author already wrote the exact dormant guard. The only work
# before it is callback-local, read-only setup. The state can be changed by an
# independent hotkey writer, so moving the guard earlier cannot hide discovery.
Write-ModFile 'HardDormantPositive' 'init.lua' @'
local enabled = false

registerHotkey("hard_dormant_positive_toggle", "Toggle", function()
    enabled = not enabled
end)

local function activeWork(player, timeSystem)
    if player and timeSystem then
        player:GetWorldPosition()
    end
end

registerForEvent("onUpdate", function(dt)
    local player = Game.GetPlayer()
    local timeSystem = Game.GetTimeSystem()
    if not enabled then return end

    activeWork(player, timeSystem)
end)
'@

# REJECT 1: same apparent activity state and wake path, but a call before the
# guard is not source-proven read-only. Hoisting would skip an observable side
# effect, so generic AUTO must refuse.
Write-ModFile 'HardDormantRejectSideEffect' 'init.lua' @'
local enabled = false
local counter = 0

registerHotkey("hard_dormant_reject_side_effect_toggle", "Toggle", function()
    enabled = not enabled
end)

local function mutateWorld()
    counter = counter + 1
    return counter
end

registerForEvent("onUpdate", function(dt)
    local token = mutateWorld()
    if not enabled then return end

    if token > 0 then
        Game.GetPlayer():GetWorldPosition()
    end
end)
'@

# REJECT 2: a Get*-looking member call is not proof of purity.
# This one mutates state, so generic AUTO must refuse to move the guard.
Write-ModFile 'HardDormantRejectDeceptiveGetter' 'init.lua' @'
local enabled = false
local counter = 0
local reader = {}

function reader:GetAndConsume()
    counter = counter + 1
    return counter
end

registerHotkey("hard_dormant_reject_deceptive_toggle", "Toggle", function()
    enabled = not enabled
end)

registerForEvent("onUpdate", function(dt)
    local token = reader:GetAndConsume()
    if not enabled then return end
    if token > 0 then Game.GetPlayer():GetWorldPosition() end
end)
'@

# REJECT 2: pre-guard work is read-only, but no independent registerHotkey /
# registerInput path writes the gate. The resolver must not infer that an
# ordinary function will necessarily be called while the callback is asleep.
Write-ModFile 'HardDormantRejectWake' 'init.lua' @'
local enabled = false

local function enableLater()
    enabled = true
end

registerForEvent("onUpdate", function(dt)
    local player = Game.GetPlayer()
    if not enabled then return end

    if player then player:GetWorldPosition() end
end)
'@

$positiveLine=Find-Line (Join-Path $mods 'HardDormantPositive\init.lua') 'registerForEvent\("onUpdate"'
$sideEffectLine=Find-Line (Join-Path $mods 'HardDormantRejectSideEffect\init.lua') 'registerForEvent\("onUpdate"'
$deceptiveLine=Find-Line (Join-Path $mods 'HardDormantRejectDeceptiveGetter\init.lua') 'registerForEvent\("onUpdate"'
$wakeLine=Find-Line (Join-Path $mods 'HardDormantRejectWake\init.lua') 'registerForEvent\("onUpdate"'

$handoff=@{
    schemaVersion='1.8'
    callbacks=@(
        (CallbackRow 9201 'HardDormantPositive' 'init.lua' $positiveLine 6.0),
        (CallbackRow 9202 'HardDormantRejectSideEffect' 'init.lua' $sideEffectLine 6.0),
        (CallbackRow 9203 'HardDormantRejectWake' 'init.lua' $wakeLine 6.0),
        (CallbackRow 9204 'HardDormantRejectDeceptiveGetter' 'init.lua' $deceptiveLine 6.0)
    )
    optimizerEvidence=@()
} | ConvertTo-Json -Depth 30
$handoff | Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

$resolved = (& $resolverExe --capture $capture --mods $mods --generate-pass --json | ConvertFrom-Json)
if (!$resolved.ok -or $null -eq $resolved.pass) { throw 'Generic hard-dormant pass generation failed.' }

$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
function Consumer([string]$Owner) {
    return @($resolver.callbackFamilies |
        Where-Object { $_.target -eq 'onUpdate' } |
        ForEach-Object { $_.topConsumers } |
        Where-Object { $_.owner -eq $Owner })[0]
}

$positive=Consumer 'HardDormantPositive'
$rejectSideEffect=Consumer 'HardDormantRejectSideEffect'
$rejectWake=Consumer 'HardDormantRejectWake'
$rejectDeceptive=Consumer 'HardDormantRejectDeceptiveGetter'

if($null -eq $positive) { throw 'Positive hard-dormant fixture missing from resolver.' }
if(-not @($positive.generic.RecipeFamilies | Where-Object { $_ -eq 'HARD_DORMANT_GUARD_HOIST' })) {
    throw "Positive fixture was not admitted to HARD_DORMANT_GUARD_HOIST. Pattern=$($positive.generic.Pattern)"
}
if($positive.generic.Facts.hardDormantGateExpression -ne 'enabled' -or
   [int]$positive.generic.Facts.hardDormantPreGuardReadCount -ne 2) {
    throw 'Positive hard-dormant source proof facts are incomplete.'
}

foreach($reject in @($rejectSideEffect,$rejectWake,$rejectDeceptive)) {
    if($null -eq $reject) { throw 'Hard-dormant reject fixture missing from resolver.' }
    if(@($reject.generic.RecipeFamilies | Where-Object { $_ -eq 'HARD_DORMANT_GUARD_HOIST' }).Count -ne 0) {
        throw "Unsafe fixture was admitted to HARD_DORMANT_GUARD_HOIST: $($reject.owner)"
    }
}

$manifest = Get-Content -LiteralPath $resolved.pass.ManifestPath -Raw | ConvertFrom-Json
$hardTransforms=@($manifest.transforms | Where-Object { $_.type -eq 'HARD_DORMANT_GUARD_HOIST' })
if($hardTransforms.Count -ne 1 -or $hardTransforms[0].owner -ne 'HardDormantPositive') {
    throw "Expected exactly one hard-dormant transform for the positive fixture; got $($hardTransforms.Count)."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[System.IO.Compression.ZipFile]::OpenRead([string]$resolved.pass.ZipPath)
try {
    function Read-ZipText([string]$Name) {
        $e=$zip.GetEntry($Name); if($null -eq $e){throw "ZIP entry missing: $Name"}
        $r=[System.IO.StreamReader]::new($e.Open())
        try{return $r.ReadToEnd()} finally{$r.Dispose()}
    }

    $base='bin/x64/plugins/cyber_engine_tweaks/mods/'
    $positiveText=Read-ZipText ($base+'HardDormantPositive/init.lua')
    $guard=$positiveText.IndexOf('if not enabled then return end')
    $player=$positiveText.IndexOf('local player =')
    $time=$positiveText.IndexOf('local timeSystem =')
    if($guard -lt 0 -or $player -lt 0 -or $time -lt 0 -or $guard -gt $player -or $guard -gt $time) {
        throw 'Generated hard-dormant guard was not hoisted ahead of the read-only setup.'
    }
    if($positiveText -notmatch 'registerHotkey\("hard_dormant_positive_toggle"') {
        throw 'Positive fixture wake path was altered.'
    }

    # Rejects may still receive mechanical frame-dispatch consolidation. If
    # present in the ZIP, their author guard must stay below the pre-guard work.
    foreach($name in @('HardDormantRejectSideEffect','HardDormantRejectWake','HardDormantRejectDeceptiveGetter')) {
        $entry=$zip.GetEntry($base+$name+'/init.lua')
        if($null -eq $entry){continue}
        $reader=[System.IO.StreamReader]::new($entry.Open())
        try{$txt=$reader.ReadToEnd()} finally{$reader.Dispose()}
        $g=$txt.IndexOf('if not enabled then return end')
        if($name -eq 'HardDormantRejectSideEffect') {
            $pre=$txt.IndexOf('local token = mutateWorld()')
        } elseif($name -eq 'HardDormantRejectDeceptiveGetter') {
            $pre=$txt.IndexOf('local token = reader:GetAndConsume()')
        } else {
            $pre=$txt.IndexOf('local player =')
        }
        if($g -ge 0 -and $pre -ge 0 -and $g -lt $pre) {
            throw "Reject fixture was unsafely guard-hoisted: $name"
        }
    }
}
finally { $zip.Dispose() }

Write-Host 'Generic hard-dormant guard hoist + reject contract passed.'
