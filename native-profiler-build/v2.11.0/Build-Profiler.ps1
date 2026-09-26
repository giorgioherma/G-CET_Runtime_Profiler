param(
    [string]$WorkRoot = "$env:USERPROFILE\Desktop\CET-Native-Profiler-Build-v2.11.0"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$CetCommit = "61bd6214f0f5f8748589c9e476538614a13908c0"
$ReleaseTime = "2025-09-28T10:48:33Z"
$XmakeVersion = "3.0.3"
$RequiredSDK = "10.0.26100.0"

function Require-Command([string]$Name) {
    if (!(Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command '$Name' was not found in PATH."
    }
}

function Import-CmdEnvironment([string]$Command) {
    # Avoid cmd.exe /s quote-stripping edge cases with paths containing spaces.
    # The command is passed as a normal /d /c payload and VsDevCmd is invoked
    # through CALL with its path quoted.
    $payload = "$Command && set"
    $lines = & $env:ComSpec /d /c $payload
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to initialize MSVC environment."
    }

    foreach ($line in $lines) {
        $idx = $line.IndexOf("=")
        if ($idx -gt 0) {
            $name = $line.Substring(0, $idx)
            $value = $line.Substring($idx + 1)
            [Environment]::SetEnvironmentVariable($name, $value, "Process")
        }
    }
}

function Find-VsDevCmd144 {
    $roots = @(
        "${env:ProgramFiles}\Microsoft Visual Studio",
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
    ) | Where-Object { $_ -and (Test-Path $_) }

    foreach ($root in $roots) {
        $candidates = Get-ChildItem $root -Recurse -File -Filter "VsDevCmd.bat" -ErrorAction SilentlyContinue |
            Sort-Object FullName

        foreach ($candidate in $candidates) {
            $instance = Split-Path (Split-Path (Split-Path $candidate.FullName -Parent) -Parent) -Parent
            $msvcRoot = Join-Path $instance "VC\Tools\MSVC"
            if (Test-Path $msvcRoot) {
                $match = Get-ChildItem $msvcRoot -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -like "14.44.*" } |
                    Sort-Object Name -Descending |
                    Select-Object -First 1
                if ($match) {
                    return [pscustomobject]@{
                        VsDevCmd = $candidate.FullName
                        Toolset = $match.Name
                        Instance = $instance
                    }
                }
            }
        }
    }

    return $null
}

function Find-BuiltASI([string]$Repo) {
    return Get-ChildItem -LiteralPath $Repo -Recurse -File -Filter "cyber_engine_tweaks.asi" |
        Where-Object { $_.FullName -match "\\build\\" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Copy-BuildArtifacts([string]$Repo, [string]$Out, [string]$Stem) {
    $asi = Find-BuiltASI $Repo
    if (!$asi) {
        throw "Build completed but cyber_engine_tweaks.asi was not found."
    }

    Copy-Item $asi.FullName (Join-Path $Out "$Stem.asi") -Force

    $pdb = [IO.Path]::ChangeExtension($asi.FullName, ".pdb")
    if (Test-Path $pdb) {
        Copy-Item $pdb (Join-Path $Out "$Stem.pdb") -Force
    }
}

Require-Command git

Write-Host "Checking historical CET release toolchain..." -ForegroundColor Cyan
$vs = Find-VsDevCmd144
if (!$vs) {
    throw @"
MSVC 14.44 was not found.

The known-good official CET binary was linked by linker 14.44.
Install:
  MSVC v143 - VS 2022 C++ x64/x86 build tools (v14.44-17.14)
Component ID:
  Microsoft.VisualStudio.Component.VC.14.44.17.14.x86.x64

Then rerun this build.
"@
}

if (!(Test-Path "${env:ProgramFiles(x86)}\Windows Kits\10\Include\$RequiredSDK")) {
    throw "Windows SDK $RequiredSDK was not found."
}

Write-Host "Using Visual Studio instance: $($vs.Instance)"
Write-Host "Using MSVC toolset       : $($vs.Toolset)"

$vcBatch = $vs.VsDevCmd

# cmd.exe quoting is intentionally handled inside Import-CmdEnvironment.
# Passing a string that already begins with a quote caused cmd /s /c to see:
#   ""C:\Program Files\...
# and fail before VsDevCmd could run.
$vcCommand = "call `"$vcBatch`" -arch=x64 -host_arch=x64 -vcvars_ver=14.44 >nul"
Import-CmdEnvironment $vcCommand

$cl = Get-Command cl.exe -ErrorAction Stop
$link = Get-Command link.exe -ErrorAction Stop
Write-Host "cl.exe   : $($cl.Source)"
Write-Host "link.exe : $($link.Source)"

# cl.exe writes its banner to STDERR and returns a non-zero exit code when
# invoked without a source file. With $ErrorActionPreference = "Stop",
# PowerShell turns that perfectly normal banner into NativeCommandError.
# Query the version through cmd.exe so we can inspect the text without
# treating cl.exe's diagnostic exit behavior as a build failure.
$clVersion = (& $env:ComSpec /d /c "cl.exe 2>&1" | Select-Object -First 2) -join " "
Write-Host "Compiler banner: $clVersion"
if ($clVersion -notmatch "19\.44\.") {
    throw "Historical environment activation failed: expected MSVC 19.44, got: $clVersion"
}

$repo = Join-Path $WorkRoot "CyberEngineTweaks"
$histRepo = Join-Path $WorkRoot "xmake-repo-20250928"
$tools = Join-Path $WorkRoot "tools"
$out = Join-Path $PSScriptRoot "BUILD_OUTPUT"
$xmakeGlobal = Join-Path $WorkRoot "xmake-global"
$xmakePkgCache = Join-Path $WorkRoot "xmake-pkg-cache"
$xmakePkgInstall = Join-Path $WorkRoot "xmake-pkg-install"

foreach ($dir in @($WorkRoot, $tools)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

foreach ($dir in @($repo, $histRepo, $out, $xmakeGlobal, $xmakePkgCache, $xmakePkgInstall)) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force
    }
}
New-Item -ItemType Directory -Path $out -Force | Out-Null
New-Item -ItemType Directory -Path $xmakeGlobal -Force | Out-Null
New-Item -ItemType Directory -Path $xmakePkgCache -Force | Out-Null
New-Item -ItemType Directory -Path $xmakePkgInstall -Force | Out-Null

# Completely isolate this build from packages cached by xmake 3.0.9 / MSVC 19.51.
$env:XMAKE_GLOBALDIR = $xmakeGlobal
$env:XMAKE_PKG_CACHEDIR = $xmakePkgCache
$env:XMAKE_PKG_INSTALLDIR = $xmakePkgInstall

$xmake = Join-Path $tools "xmake-bundle-v$XmakeVersion.win64.exe"
if (!(Test-Path $xmake)) {
    $url = "https://github.com/xmake-io/xmake/releases/download/v$XmakeVersion/xmake-bundle-v$XmakeVersion.win64.exe"
    Write-Host "Downloading historical Xmake $XmakeVersion..."
    Invoke-WebRequest -Uri $url -OutFile $xmake
}

$xmakeBanner = (& $xmake --version 2>&1 | Select-Object -First 1)
Write-Host "Xmake: $xmakeBanner"
if ($xmakeBanner -notmatch "3\.0\.3") {
    throw "Expected Xmake 3.0.3."
}

Write-Host ""
Write-Host "Cloning xmake-repo history..."
git clone --filter=blob:none https://github.com/xmake-io/xmake-repo.git $histRepo
if ($LASTEXITCODE -ne 0) { throw "xmake-repo clone failed" }

Push-Location $histRepo
try {
    $histCommit = (git rev-list -1 --before="$ReleaseTime" origin/master).Trim()
    if (!$histCommit) {
        throw "Could not resolve xmake-repo snapshot before $ReleaseTime"
    }
    git checkout --detach $histCommit
    if ($LASTEXITCODE -ne 0) { throw "Historical xmake-repo checkout failed" }
}
finally {
    Pop-Location
}
Write-Host "Historical xmake-repo commit: $histCommit"

Write-Host ""
Write-Host "Cloning official CET v1.37.1 source..."
git clone https://github.com/maximegmd/CyberEngineTweaks.git $repo
if ($LASTEXITCODE -ne 0) { throw "CET clone failed" }

Push-Location $repo
try {
    git checkout --detach $CetCommit
    if ($LASTEXITCODE -ne 0) { throw "CET commit checkout failed" }

    git submodule sync --recursive
    git submodule update --init --force --recursive
    if ($LASTEXITCODE -ne 0) { throw "CET submodule update failed" }

    & (Join-Path $PSScriptRoot "Patch-CET-HistoricalBuild.ps1") `
        -SourceRoot $repo `
        -HistoricalRepo $histRepo

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "PHASE A: HISTORICAL CONTROL CET" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    # IMPORTANT: activating VsDevCmd alone is not enough. Xmake performs
    # its own Visual Studio discovery and, on this machine, auto-selects
    # Visual Studio 2026 with its newest 14.51 toolset. Explicitly pin the
    # MSVC toolset so Xmake and all Xmake-built dependencies use 14.44.
    & $xmake f -y -m release -p windows -a x64 `
        --vs_toolset=14.44 `
        --vs_sdkver=$RequiredSDK
    if ($LASTEXITCODE -ne 0) { throw "historical CONTROL configure failed" }

    Write-Host ""
    Write-Host "Xmake configuration summary:" -ForegroundColor DarkCyan
    & $xmake show
    if ($LASTEXITCODE -ne 0) {
        Write-Host "xmake show returned a non-zero status; continuing to binary verification." -ForegroundColor Yellow
    }

    & $xmake -y
    if ($LASTEXITCODE -ne 0) { throw "historical CONTROL build failed" }

    Copy-BuildArtifacts $repo $out "cyber_engine_tweaks.CONTROL"

    $control = Join-Path $out "cyber_engine_tweaks.CONTROL.asi"
    $controlInfo = & (Join-Path $PSScriptRoot "Get-CETPEInfo.ps1") -Path $control
    $controlInfo | Format-List

    if ($controlInfo.Linker -ne "14.44") {
        throw "CONTROL binary linker is $($controlInfo.Linker), expected 14.44. Xmake did not honor the pinned --vs_toolset=14.44; refusing to continue."
    }

    Write-Host "Historical CONTROL linker verified as 14.44." -ForegroundColor Green

    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Cyan
    Write-Host "PHASE B: FULL CALLBACK PROFILER CET v2.11.0" -ForegroundColor Cyan
    Write-Host "============================================================" -ForegroundColor Cyan

    & (Join-Path $PSScriptRoot "Patch-CET-v1.37.1.ps1") -SourceRoot $repo
    if ($LASTEXITCODE -ne 0) { throw "profiler source patch failed" }

    & $xmake clean
    if ($LASTEXITCODE -ne 0) { throw "xmake clean failed" }

    & $xmake -y
    if ($LASTEXITCODE -ne 0) { throw "historical PROFILER build failed" }

    Copy-BuildArtifacts $repo $out "cyber_engine_tweaks.PROFILER"

    $profiler = Join-Path $out "cyber_engine_tweaks.PROFILER.asi"
    $profInfo = & (Join-Path $PSScriptRoot "Get-CETPEInfo.ps1") -Path $profiler
    if ($profInfo.Linker -ne "14.44") {
        throw "PROFILER binary linker is $($profInfo.Linker), expected 14.44."
    }

    Set-Content -LiteralPath (Join-Path $out "HISTORICAL_BUILD.txt") -Encoding UTF8 -Value @(
        "CET commit=$CetCommit",
        "Official release workflow run=18073224933",
        "Official release workflow date=$ReleaseTime",
        "Xmake=$XmakeVersion",
        "xmake-repo commit=$histCommit",
        "MSVC toolset=$($vs.Toolset)",
        "Windows SDK=$RequiredSDK",
        "Control linker=$($controlInfo.Linker)",
        "Profiler linker=$($profInfo.Linker)"
    )

    Write-Host ""
    Write-Host "SUCCESS - HISTORICAL CONTROL + PROFILER CREATED" -ForegroundColor Green
    Write-Host "Control : $control"
    Write-Host "Profiler: $profiler"
    Write-Host ""
    Write-Host "TEST CONTROL FIRST. Do NOT install profiler until CONTROL passes overlay test." -ForegroundColor Yellow
}
finally {
    Pop-Location
}
