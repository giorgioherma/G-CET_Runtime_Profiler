param(
    [string]$RequiredToolsetPrefix = "14.44",
    [string]$RequiredSDK = "10.0.26100.0"
)

$ErrorActionPreference = "Stop"

$roots = @(
    "${env:ProgramFiles}\Microsoft Visual Studio",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio"
) | Where-Object { $_ -and (Test-Path $_) }

$toolsets = @()
foreach ($root in $roots) {
    $toolsets += Get-ChildItem $root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -match "\\VC\\Tools\\MSVC\\14\.44\.[^\\]+$"
        } |
        Select-Object -ExpandProperty FullName
}

$toolsets = $toolsets | Sort-Object -Unique
$sdk = Test-Path "${env:ProgramFiles(x86)}\Windows Kits\10\Include\$RequiredSDK"

Write-Host "Required by official CET reference:" -ForegroundColor Cyan
Write-Host "  MSVC toolset family : 14.44"
Write-Host "  Windows SDK target  : $RequiredSDK"
Write-Host ""
Write-Host "MSVC 14.44 installations found: $($toolsets.Count)"
$toolsets | ForEach-Object { Write-Host "  $_" }
Write-Host "Windows SDK $RequiredSDK present: $sdk"

if ($toolsets.Count -eq 0) {
    Write-Host ""
    Write-Host "MSVC 14.44 is NOT installed." -ForegroundColor Red
    Write-Host "Install the Visual Studio component:"
    Write-Host "  MSVC v143 - VS 2022 C++ x64/x86 build tools (v14.44-17.14)"
    Write-Host "Component ID:"
    Write-Host "  Microsoft.VisualStudio.Component.VC.14.44.17.14.x86.x64"
    exit 2
}

if (!$sdk) {
    Write-Host ""
    Write-Host "Windows SDK $RequiredSDK is NOT installed." -ForegroundColor Red
    exit 3
}

Write-Host ""
Write-Host "Historical compiler prerequisites are present." -ForegroundColor Green
