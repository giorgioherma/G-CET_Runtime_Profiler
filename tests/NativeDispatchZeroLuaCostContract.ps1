param(
    [Parameter(Mandatory = $true)]
    [string]$ResolverRoot
)

$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path $ResolverRoot).Path
$resolverExe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $resolverExe -PathType Leaf)) {
    throw "Resolver executable missing: $resolverExe"
}

$root = Join-Path $env:RUNNER_TEMP 'gcet-native-dispatch-zero-lua'
if (Test-Path -LiteralPath $root) { Remove-Item $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-111111_NATIVE_DISPATCH'
$mods = Join-Path $root 'mods'
$zero = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$zero | Out-Null

# A real pass requires the pinned, hash-verified shared runtime.
$packed = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$input = [IO.MemoryStream]::new([Convert]::FromBase64String($packed))
$gzip = [IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
$output = [IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())
} finally {
    $output.Dispose()
    $gzip.Dispose()
    $input.Dispose()
}

function Write-Mod([string]$Owner,[string]$Text) {
    $dir = Join-Path $mods $Owner
    New-Item -ItemType Directory -Force $dir | Out-Null
    $p = Join-Path $dir 'init.lua'
    $Text | Set-Content -LiteralPath $p -Encoding utf8
    return $p
}
function Range([string]$Path,[string]$First) {
    $lines = @(Get-Content -LiteralPath $Path)
    $start = -1
    for ($i=0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $First) { $start=$i+1; break }
    }
    if ($start -lt 0) { throw "Callback not found in $Path" }
    for ($j=$start; $j -lt $lines.Count; $j++) {
        if ($lines[$j] -match '^\s*end\s*\)\s*$') {
            return @($start,$j+1)
        }
    }
    throw "Callback end missing in $Path"
}
function Row([int]$Id,[string]$Owner,[string]$Kind,[string]$Target,
             [double]$Calls,[double]$Ms,[string]$Path,[string]$First) {
    $range = Range $Path $First
    return @{
        registrationId=$Id; owner=$Owner; infrastructure=$false
        kind=$Kind; target=$Target
        source=@{file='init.lua';lineStart=$range[0];lineEnd=$range[1]}
        callsPerSecond=$Calls; exclusiveMsPerSecond=$Ms
        globalWorkSharePct=0; familyWorkSharePct=0
        avgExclusiveUs=0; maxExclusiveMs=0
        spikeCount=0; maxSpikeExclusiveMs=0
    }
}

$frame = Write-Mod 'NativeZeroFrame' @'
local active = false
registerHotkey('native_zero_frame_wake', 'Wake', function() active = true end)
registerForEvent('onUpdate', function(dt)
    if not active then return end
    ActiveFrame(dt)
end)
'@

$draw = Write-Mod 'NativeZeroDraw' @'
local showing = false
registerInput("native_zero_draw_open", "Show", function(pressed)
    if pressed then showing = not showing end
end)
registerForEvent("onDraw", function()
    if showing then DrawSomething() end
end)
'@

$action = Write-Mod 'NativeZeroAction' @'
Observe("PlayerPuppet", "OnAction", function(self, action, consumer)
    local actionName = Game.NameToString(action:GetName())
    if actionName == "Jump" or actionName == "Dodge" then
        DoActionWork()
    end
end)
'@

$guard = Write-Mod 'NativeZeroGuard' @'
local active = false
registerHotkey('native_zero_guard_wake', 'Wake', function() active = true end)
registerForEvent('onUpdate', function(dt)
    local player = Game.GetPlayer()
    if not active then return end
    DoActiveUpdate(player, dt)
end)
'@

$silent = Write-Mod 'NativeSilent' @'
registerForEvent("onUpdate", function(dt)
    NeverCalled(dt)
end)
'@

$other = Write-Mod 'NativeOther' @'
ObserveAfter("PlayerPuppet", "TickUnrelated", function(self)
    ReadOnlyNativeWork(self)
end)
'@

$wrapped = Write-Mod 'NativeWrapped' @'
local Event = {}
Event.Observe("PlayerPuppet", "OnAction", function(_, action)
    if action:GetName() == CName.new("Jump") then
        WrappedWork()
    end
end)
'@

$existing = Write-Mod 'NativeAlreadyRouted' @'
local __gcetRegisterEvent_9906 = registerForEvent
do
    local ok, engine = pcall(GetMod, "0-Engine")
    if ok and type(engine) == "table" and type(engine.MakeEventRegistrar) == "function" then
        __gcetRegisterEvent_9906 = engine.MakeEventRegistrar("NativeAlreadyRouted", registerForEvent)
    end
end
__gcetRegisterEvent_9906("onUpdate", function(dt)
    AlreadyRouted(dt)
end)
'@

$rows = @(
    (Row 9901 'NativeZeroFrame' 'event' 'onUpdate' 60 0 $frame 'registerForEvent'),
    (Row 9902 'NativeZeroDraw' 'event' 'onDraw' 60 0 $draw 'registerForEvent'),
    (Row 9903 'NativeZeroAction' 'Observe' 'PlayerPuppet::OnAction' 600 0 $action '^\s*Observe\('),
    (Row 9904 'NativeSilent' 'event' 'onUpdate' 0 0 $silent 'registerForEvent'),
    (Row 9905 'NativeOther' 'ObserveAfter' 'PlayerPuppet::TickUnrelated' 900 0 $other 'ObserveAfter'),
    (Row 9906 'NativeAlreadyRouted' 'event' 'onUpdate' 60 0 $existing '__gcetRegisterEvent_9906\("onUpdate"'),
    (Row 9907 'NativeWrapped' 'Observe' 'PlayerPuppet::OnAction' 600 0 $wrapped 'Event\.Observe'),
    (Row 9908 'NativeZeroGuard' 'event' 'onUpdate' 60 0 $guard 'registerForEvent')
)
$inputFile = Join-Path $capture 'CET_Resolver_Input.json'
@{schemaVersion='1.8';callbacks=$rows;optimizerEvidence=@()} |
    ConvertTo-Json -Depth 24 |
    Set-Content -LiteralPath $inputFile -Encoding utf8

$before = @{}
foreach ($file in @($frame,$draw,$action,$guard,$silent,$other,$wrapped,$existing)) {
    $before[$file] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
}
$run = (& $resolverExe --capture $capture --mods $mods --generate-pass --json |
    ConvertFrom-Json)
if (!$run.ok -or $null -eq $run.pass) {
    throw 'Zero-Lua-cost native dispatch consolidation failed to generate a pass.'
}
$resolver = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw |
    ConvertFrom-Json
$consumers = @($resolver.callbackFamilies | ForEach-Object { $_.topConsumers })
function Consumer([string]$Owner) {
    return @($consumers | Where-Object { $_.owner -eq $Owner })[0]
}
foreach ($name in @('NativeZeroFrame','NativeZeroDraw','NativeZeroGuard','NativeZeroAction',
                     'NativeAlreadyRouted','NativeWrapped')) {
    if ($null -eq (Consumer $name)) { throw "Measured native callback was discarded: $name" }
}
foreach ($name in @('NativeSilent','NativeOther')) {
    if ($null -ne (Consumer $name)) { throw "Non-eligible zero-cost callback was admitted: $name" }
}
foreach ($name in @('NativeZeroFrame','NativeZeroDraw','NativeZeroGuard')) {
    $row = Consumer $name
    if (-not $row.generic.Automatable -or
        @($row.generic.RecipeFamilies | Where-Object { $_ -eq 'FRAME_DISPATCH_CONSOLIDATION' }).Count -ne 1) {
        throw "Low-Lua-work native frame dispatch was not source-proven: $name"
    }
}
if (-not (Consumer 'NativeZeroAction').generic.Automatable) {
    throw 'Low-Lua-work OnAction did not reach the existing exact-action router.'
}
if ((Consumer 'NativeWrapped').generic.Automatable) {
    throw 'Unsupported wrapped OnAction was unsafely admitted.'
}
if ((Consumer 'NativeAlreadyRouted').generic.Status -ne 'ALREADY_SATISFIED') {
    throw 'Previously adopted frame was not recognized as complete.'
}

$manifest = Get-Content -LiteralPath $run.pass.ManifestPath -Raw |
    ConvertFrom-Json
$transforms = @($manifest.transforms)
foreach ($name in @('NativeZeroFrame','NativeZeroDraw','NativeZeroGuard','NativeZeroAction')) {
    if (@($transforms | Where-Object { $_.owner -eq $name }).Count -eq 0) {
        $skipRows = @($manifest.skipped | Where-Object { $_.owner -eq $name })
        $skipDetails = ($skipRows | ConvertTo-Json -Depth 12 -Compress)
        throw "Expected native consolidation did not produce an overlay: $name; skips=$skipDetails"
    }
}
foreach ($name in @('NativeSilent','NativeOther','NativeWrapped','NativeAlreadyRouted')) {
    if (@($transforms | Where-Object { $_.owner -eq $name }).Count -ne 0) {
        throw "Unexpected native dispatch rewrite: $name"
    }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead([string]$run.pass.ZipPath)
try {
    $prefix='bin/x64/plugins/cyber_engine_tweaks/mods/'
    function Zip-Text([string]$Name) {
        $entry=$zip.GetEntry($prefix+$Name+'/init.lua')
        if ($null -eq $entry) { throw "Missing overlay for $Name" }
        $reader=[IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    if ((Zip-Text 'NativeZeroFrame') -notmatch 'MakeEventRegistrar') {
        throw 'Dormant onUpdate still uses an independent CET frame registration.'
    }
    if ((Zip-Text 'NativeZeroDraw') -notmatch 'MakeEventRegistrar') {
        throw 'Dormant onDraw still uses an independent CET draw registration.'
    }
    $guardText=Zip-Text 'NativeZeroGuard'
    if ($guardText.IndexOf('local player = Game.GetPlayer()') -lt 0 -or
        $guardText.IndexOf('if not active then return end') -lt
            $guardText.IndexOf('local player = Game.GetPlayer()')) {
        throw 'Zero-cost admission improperly hoisted a Lua-side guard.'
    }
    if (@($transforms | Where-Object { $_.owner -eq 'NativeZeroGuard' -and
         $_.type -eq 'HARD_DORMANT_GUARD_HOIST' }).Count -gt 0) {
        throw 'Unmeasured Lua work transformation was incorrectly admitted.'
    }
    $routed=Zip-Text 'NativeZeroAction'
    if ($routed -notmatch 'SubscribeAction' -or
        $routed -notmatch 'if not __gcetRouted_\d+ then Observe\("PlayerPuppet", "OnAction"') {
        throw 'OnAction shared native route or fail-safe original observer is missing.'
    }
    foreach ($name in @('NativeSilent','NativeOther','NativeWrapped','NativeAlreadyRouted')) {
        if ($zip.GetEntry($prefix+$name+'/init.lua')) {
            throw "Unexpected ZIP overlay for $name"
        }
    }
}
finally { $zip.Dispose() }

foreach ($file in $before.Keys) {
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $before[$file]) {
        throw "Resolver wrote to live source: $file"
    }
}
Write-Host 'Zero-Lua-cost native dispatch contract passed: frame/action routing, prior-stage recognition, unsafe exclusions and read-only originals.'
