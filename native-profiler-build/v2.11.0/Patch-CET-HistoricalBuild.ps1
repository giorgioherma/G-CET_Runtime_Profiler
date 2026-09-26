param(
    [Parameter(Mandatory=$true)]
    [string]$SourceRoot,
    [Parameter(Mandatory=$true)]
    [string]$HistoricalRepo
)

$ErrorActionPreference = "Stop"

$src = (Resolve-Path $SourceRoot).Path
$repo = (Resolve-Path $HistoricalRepo).Path
$xmakeLua = Join-Path $src "xmake.lua"

if (!(Test-Path -LiteralPath $xmakeLua)) {
    throw "CET xmake.lua not found: $xmakeLua"
}
if (!(Test-Path -LiteralPath (Join-Path $repo "packages"))) {
    throw "Historical xmake-repo does not look valid: $repo"
}

$text = [IO.File]::ReadAllText($xmakeLua)

if ($text.Contains("CET_PROFILER_HISTORICAL_REPO")) {
    Write-Host "Historical repository already attached." -ForegroundColor DarkGray
    exit 0
}

$anchor = 'set_arch("x64")'
if (-not $text.Contains($anchor)) {
    throw "Could not find CET xmake architecture anchor."
}

$repoForward = $repo.Replace("\", "/")
$block = @"

-- CET_PROFILER_HISTORICAL_REPO
-- Reproduce the package definitions available to CET's successful
-- GitHub Actions release build on 2025-09-28.
add_repositories("cet-release-20250928 $repoForward")
-- /CET_PROFILER_HISTORICAL_REPO
"@

$text = $text.Replace($anchor, $anchor + $block)
[IO.File]::WriteAllText(
    $xmakeLua,
    $text,
    (New-Object Text.UTF8Encoding($false))
)

Write-Host "Attached historical xmake-repo snapshot to CET project." -ForegroundColor Green
