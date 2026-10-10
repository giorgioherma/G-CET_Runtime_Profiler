param(
    [Parameter(Mandatory = $true)][string]$ResolverRoot,
    [Parameter(Mandatory = $true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$ResolverRoot = (Resolve-Path -LiteralPath $ResolverRoot).Path
$Destination = [IO.Path]::GetFullPath($Destination)

$exe = Join-Path $ResolverRoot 'G-CET-Resolver.exe'
$encoded = Join-Path $ResolverRoot 'runtime\0-Engine\fixed-init.lua.gz.b64'
if (!(Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Resolver executable missing: $exe" }
if (!(Test-Path -LiteralPath $encoded -PathType Leaf)) { throw "Pinned 0-Engine baseline missing: $encoded" }

$working = Join-Path $env:RUNNER_TEMP 'gcet-0engine-ready'
if (Test-Path -LiteralPath $working) { Remove-Item -LiteralPath $working -Recurse -Force }
$mods = Join-Path $working 'fixture\bin\x64\plugins\cyber_engine_tweaks\mods'
$zero = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $zero | Out-Null

# The resolver's own hash-checked factory generates a complete real runtime,
# not a folder of encoded source assets or unpatched init.lua.
$compressed = [Convert]::FromBase64String((Get-Content -LiteralPath $encoded -Raw).Trim())
$inputStream = [IO.MemoryStream]::new($compressed)
$gzip = [IO.Compression.GZipStream]::new($inputStream, [IO.Compression.CompressionMode]::Decompress)
$outputStream = [IO.MemoryStream]::new()
try {
    $gzip.CopyTo($outputStream)
    [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'), $outputStream.ToArray())
}
finally {
    $outputStream.Dispose(); $gzip.Dispose(); $inputStream.Dispose()
}

$overlay = Join-Path $working 'framework.zip'
$raw = & $exe --mods $mods --update-framework --pass-output $overlay --json 2>&1
if ($LASTEXITCODE -ne 0) { throw "0-Engine generation failed: $($raw -join ' ')" }
$summary = $raw | Select-Object -Last 1 | ConvertFrom-Json
if (!$summary.ok -or $summary.mode -ne 'ZERO_ENGINE_ONLY' -or
    [int]$summary.pass.FileCount -ne 9) {
    throw "Unexpected framework generation: $($raw -join ' ')"
}

$staging = Join-Path $working 'staged'
[IO.Compression.ZipFile]::ExtractToDirectory($overlay, $staging)
$actual = Join-Path $staging 'bin\x64\plugins\cyber_engine_tweaks\mods\0-Engine'
$required = @('init.lua', 'modules\ActionRouter.lua', 'modules\Health.lua',
    'modules\Scheduler.lua', 'modules\GCETWorkQueue.lua',
    'modules\GCETPhasePlanner.lua', 'modules\GCETFrameListeners.lua',
    'modules\GCETStateSignals.lua', 'modules\GCETWorkloadProbe.lua')
foreach ($f in $required) {
    if (!(Test-Path -LiteralPath (Join-Path $actual $f) -PathType Leaf)) {
        throw "Missing ready-to-copy 0-Engine file: $f"
    }
}
$actualFiles = @(Get-ChildItem -LiteralPath $actual -Recurse -File)
if ($actualFiles.Count -ne $required.Count -or
    @($actualFiles | Where-Object { $_.Extension -ne '.lua' }).Count -ne 0) {
    throw 'Ready-to-copy 0-Engine package has extra, encoded or non-Lua files.'
}

if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
New-Item -ItemType Directory -Force $Destination | Out-Null
Copy-Item -LiteralPath $actual -Destination (Join-Path $Destination '0-Engine') -Recurse
if (!(Test-Path -LiteralPath (Join-Path $Destination '0-Engine\init.lua'))) {
    throw 'Ready-to-copy ZIP root must be a folder named 0-Engine.'
}
Write-Host "Ready-to-copy 0-Engine folder staged: $Destination (9 actual Lua files)."
