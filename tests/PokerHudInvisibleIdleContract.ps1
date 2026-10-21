param([Parameter(Mandatory = $true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.exe'
if (!(Test-Path -LiteralPath $exe)) { throw "Resolver not found: $exe" }
$root = Join-Path $env:RUNNER_TEMP 'gcet-poker-hud-invisible-idle'
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$capture = Join-Path $root 'CET-20990101-091000_POKER_HUD'
$mods = Join-Path $root 'mods'
$poker = Join-Path $mods 'StreetGamesPoker'
$zero = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $capture,$poker,$zero | Out-Null

# Use the Resolver's genuine pinned 0-Engine runtime for supported source-only discovery.
$encoded = (Get-Content -LiteralPath (Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64') -Raw).Trim()
$inputStream = [System.IO.MemoryStream]::new([Convert]::FromBase64String($encoded))
$gzip = [System.IO.Compression.GZipStream]::new($inputStream,[System.IO.Compression.CompressionMode]::Decompress)
$output = [System.IO.MemoryStream]::new()
try {
    $gzip.CopyTo($output)
    [System.IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())
} finally { $output.Dispose();$gzip.Dispose();$inputStream.Dispose() }

$initPath = Join-Path $poker 'init.lua'
$hudPath = Join-Path $poker 'PokerHUD.lua'
@'
StreetGamesPoker={ready=true}
local PokerHUD=require("PokerHUD")
registerForEvent("onDraw", function()
    if StreetGamesPoker.ready then PokerHUD.draw() end
end)
'@ | Set-Content -LiteralPath $initPath -Encoding utf8
@'
PokerHUD = { visible = false }
local last = nil
function PokerHUD.show() PokerHUD.visible = true end
function PokerHUD.hide()
    PokerHUD.visible = false
    PokerHUD.push(true)
end
function PokerHUD.push(force)
    local player = GetPlayer()
    if not player then return end
    local vis = PokerHUD.visible and true or false
    local header, board, hand, seats, log = "", "", "", "", ""
    if vis then
        header = "LIVE"
    end
    local key = tostring(vis) .. '|' .. header .. '|' .. board .. '|' .. hand .. '|' .. seats .. '|' .. log
    if not force and key == last then return end
    last = key
    player:NCPoker_SetHUD(vis, header, board, hand, seats, log)
end
function PokerHUD.draw()
    PokerHUD.push(false)
end
return PokerHUD
'@ | Set-Content -LiteralPath $hudPath -Encoding utf8
$author = Get-Content -LiteralPath $hudPath -Raw
$originalHash = (Get-FileHash -LiteralPath $hudPath -Algorithm SHA256).Hash
@{ schemaVersion='1.8';callbacks=@();optimizerEvidence=@() } |
    ConvertTo-Json -Depth 20 |
    Set-Content -LiteralPath (Join-Path $capture 'CET_Resolver_Input.json') -Encoding utf8

function ResolvePokerHud {
    $processInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $processInfo.FileName = $exe
    $processInfo.UseShellExecute = $false
    $processInfo.RedirectStandardOutput = $true
    $processInfo.RedirectStandardError = $true
    foreach ($argument in @('--capture', $capture, '--mods', $mods, '--generate-pass', '--json')) {
        [void]$processInfo.ArgumentList.Add([string]$argument)
    }
    $process = [System.Diagnostics.Process]::Start($processInfo)
    if ($null -eq $process) { throw 'Could not launch standalone Resolver' }
    try {
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $stdoutText = ([string]$stdoutTask.GetAwaiter().GetResult()).Trim()
        $stderrText = ([string]$stderrTask.GetAwaiter().GetResult()).Trim()
    } finally { $process.Dispose() }
    $jsonText = if ($stdoutText.Length -gt 0) { $stdoutText } else { $stderrText }
    if ($jsonText.Length -eq 0) {
        throw "Resolver produced no diagnostic JSON; exit ${exitCode}"
    }
    try { $parsed = $jsonText | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Resolver did not return parsable JSON, exit ${exitCode}: $jsonText" }
    $noPass = $exitCode -eq 1 -and !$parsed.ok -and
        [string]$parsed.error -like '*no applicable generic or source-proven semantic transforms*'
    if ($exitCode -ne 0 -and !$noPass) {
        throw "Unexpected Resolver exit code ${exitCode}: $jsonText"
    }
    $result = $parsed
    $report = Get-Content -LiteralPath (Join-Path $capture 'G-CET_Resolver.json') -Raw | ConvertFrom-Json
    return @{ Result=$result; Report=$report; ExitCode=$exitCode; NoPass=$noPass }
}
function ReadGeneratedHud($zipPath) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead([string]$zipPath)
    try {
        $entry = $zip.GetEntry('bin/x64/plugins/cyber_engine_tweaks/mods/StreetGamesPoker/PokerHUD.lua')
        if (!$entry) { throw 'Poker HUD was not emitted by source-only generation' }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
$first = ResolvePokerHud
if (!$first.Result.ok -or !$first.Result.pass) { throw 'HUD pristine source-only generation failed' }
$candidates = @($first.Report.semanticSourceOnlyCandidates |
    Where-Object { $_.ruleId -eq 'streetgamespoker-hud-invisible-native-idle' })
# Source-first generic families are unmeasured current-source opportunities,
# not fabricated profiler captures. Reject actual measured callback families
# and any source-first row that incorrectly contains runtime evidence.
$invalidFamilies = @($first.Report.callbackFamilies | Where-Object {
    $_.sourceFirst -ne $true -or
    @($_.topConsumers | Where-Object {
        $_.sourceFirst -ne $true -or $null -ne $_.runtime
    }).Count -gt 0
})
if ($candidates.Count -ne 1 -or $invalidFamilies.Count -gt 0) {
    throw "HUD source-only admission invalid: candidates=$($candidates.Count), "+
          "measuredOrMislabelledFamilies=$($invalidFamilies.Count)"
}
$manifest = Get-Content $first.Result.pass.ManifestPath -Raw | ConvertFrom-Json
$rule = @($manifest.transforms | Where-Object { $_.RuleId -eq 'streetgamespoker-hud-invisible-native-idle' })
if ($rule.Count -ne 1 -or $rule[0].admissionEvidence -ne 'SOURCE_PROVEN_NO_CAPTURE' -or
    $null -ne $rule[0].runtimeMsPerSecond -or @($rule[0].files).Count -ne 1 -or
    @($rule[0].files)[0] -ne 'StreetGamesPoker/PokerHUD.lua') {
    throw 'HUD rule must emit exactly one independently source-proven file with null runtime'
}
$generated = ReadGeneratedHud $first.Result.pass.ZipPath
$guard = "if not force and not PokerHUD.visible and last == 'false|||||' then return end"
if (!$generated.Contains($guard) -or !$generated.Contains('PokerHUD.push(true)') -or
    !$generated.Contains('PokerHUD.push(false)') -or
    $generated.IndexOf($guard) -gt $generated.IndexOf('local player = GetPlayer()') -or
    !($generated.Contains('if not force and key == last then return end'))) {
    throw 'Poker HUD guard lost author force, first-hide, active HUD or order'
}
if ((Get-FileHash -LiteralPath $hudPath -Algorithm SHA256).Hash -ne $originalHash) {
    throw 'Resolver mutated original HUD source'
}

# Applying only the resulting source must mark the distinct new stage complete.
$generated | Set-Content -LiteralPath $hudPath -Encoding utf8
$second = ResolvePokerHud
$repeated = @($second.Report.semanticSourceOnlyCandidates |
    Where-Object { $_.ruleId -eq 'streetgamespoker-hud-invisible-native-idle' })
if ($repeated.Count -ne 0) { throw 'Already-complete Poker HUD was admitted again' }

# Cosmetic whitespace and double-quoted separators are source-equivalent.
$variant = $author.Replace(".. '|' ..", '.. "|" ..')
$variant = $variant.Replace('    local player = GetPlayer()', '      local  player  = GetPlayer( )')
$variant | Set-Content -LiteralPath $hudPath -Encoding utf8
$variation = ResolvePokerHud
if (!$variation.Result.ok -or !$variation.Result.pass) {
    throw 'Poker HUD source-equivalent quote/whitespace variation was rejected'
}
$variantOut = ReadGeneratedHud $variation.Result.pass.ZipPath
if (!$variantOut.Contains($guard)) { throw 'Source-equivalent Poker HUD variant lost idle guard' }

# A substantive native/player query change must refuse generation rather than guess.
$drift = $author.Replace('local player = GetPlayer()', 'local player = GetOtherPlayer()')
$drift | Set-Content -LiteralPath $hudPath -Encoding utf8
$unsafe = ResolvePokerHud
$unsafeRows = @($unsafe.Report.semanticSourceOnlyCandidates |
    Where-Object { $_.ruleId -eq 'streetgamespoker-hud-invisible-native-idle' })
if ($unsafeRows.Count -gt 0) {
    throw 'Unknown Poker HUD player provider was source-only admitted'
}
Write-Host 'Poker HUD source-only pristine, invisible-first-write, force/visible, repeat, variation and drift contracts passed.'