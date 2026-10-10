param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference = 'Stop'
$exe = Join-Path (Resolve-Path $ResolverRoot).Path 'G-CET-Resolver.App.exe'
if (!(Test-Path $exe)) { $exe = Join-Path (Resolve-Path $ResolverRoot).Path 'app\G-CET-Resolver.App.exe' }
if (!(Test-Path $exe)) { throw "Resolver executable not found in $ResolverRoot" }
$root = Join-Path $env:RUNNER_TEMP 'gcet-framework-only-contract'
if (Test-Path $root) { Remove-Item $root -Force -Recurse }
$mods = Join-Path $root 'game\bin\x64\plugins\cyber_engine_tweaks\mods'
$engine = Join-Path $mods '0-Engine'
New-Item -ItemType Directory -Force $engine | Out-Null
$source = Join-Path (Resolve-Path $ResolverRoot).Path 'runtime\0-Engine\fixed-init.lua.gz.b64'
if (!(Test-Path $source)) { throw "Bundled known fixed runtime source missing: $source" }
$bytes = [Convert]::FromBase64String((Get-Content $source -Raw).Trim())
$mem = [IO.MemoryStream]::new($bytes)
$gz = [IO.Compression.GZipStream]::new($mem, [IO.Compression.CompressionMode]::Decompress)
$dest = [IO.MemoryStream]::new()
$gz.CopyTo($dest)
$gz.Dispose(); $mem.Dispose()
[IO.File]::WriteAllBytes((Join-Path $engine 'init.lua'), $dest.ToArray())
$dest.Dispose()

$output = Join-Path $root 'framework-only.zip'
$raw = & $exe --update-framework --mods $mods --pass-output $output --json 2>&1
if ($LASTEXITCODE -ne 0) { throw "Framework-only CLI failed: $($raw -join ' ')" }
$got = ($raw | Select-Object -Last 1 | ConvertFrom-Json)
if (!$got.ok -or $got.mode -ne 'ZERO_ENGINE_ONLY' -or [int]$got.pass.TransformCount -ne 0 -or
    [int]$got.pass.FileCount -ne 9) {
    throw "Framework-only result invalid: $($got | ConvertTo-Json -Compress)"
}
if (!(Test-Path $output) -or !(Test-Path $got.pass.ManifestPath)) {
    throw 'Framework ZIP and JSON manifest must be created.'
}

$zip = [IO.Compression.ZipFile]::OpenRead($output)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
    if ($entries.Count -ne 9) { throw "Expected exactly nine 0-Engine-only entries, got $($entries.Count)." }
    foreach ($name in $entries) {
        if (!$name.StartsWith('bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/')) {
            throw "Unrelated mod included in framework update: $name"
        }
    }
    foreach ($name in @('GCETWorkQueue','GCETPhasePlanner','GCETFrameListeners','GCETStateSignals','GCETWorkloadProbe')) {
        if (!($entries -contains "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/$name.lua")) {
            throw "Framework-only ZIP missing $name"
        }
    }
} finally { $zip.Dispose() }
$manifest = Get-Content $got.pass.ManifestPath -Raw | ConvertFrom-Json
if ($manifest.kind -ne 'ZERO_ENGINE_ONLY' -or $manifest.changes.Count -ne 9) {
    throw 'Missing explicit framework-only manifest contract.'
}
# Verify idempotence: install ZIP into fixture (test fixture only), then
# a second update must decline to generate another ZIP.
[IO.Compression.ZipFile]::ExtractToDirectory($output, (Join-Path $root 'game'), $true)
$second = Join-Path $root 'framework-only-second.zip'
$raw2 = & $exe --update-framework --mods $mods --pass-output $second --json 2>&1
if ($LASTEXITCODE -eq 0 -or ($raw2 -join ' ') -notmatch 'already up to date' -or (Test-Path $second)) {
    throw "Already-current framework should not generate a redundant pass: $($raw2 -join ' ')"
}
Write-Host 'Framework-only upgrade, isolated ZIP, and idempotent no-op contracts PASSED.'
