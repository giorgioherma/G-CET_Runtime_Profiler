param(
    [string]$SourceRoot = "$env:USERPROFILE\Desktop\CET-Native-Profiler-Build-v2.11.0\CyberEngineTweaks"
)

$ErrorActionPreference = "Stop"
$src = (Resolve-Path $SourceRoot).Path

Push-Location $src
try {
    "HEAD: $(git rev-parse HEAD)"
    "Status:"
    git status --short
    ""
    "Profiler markers:"
    Get-ChildItem ".\src\scripting" -File -Include *.h,*.cpp |
        Select-String -Pattern "CETRuntimeProfiler" |
        ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
}
finally {
    Pop-Location
}
