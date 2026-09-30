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

    $profilerHeader = Get-Content ".\src\scripting\CETRuntimeProfiler.h" -Raw
    $scriptContext = Get-Content ".\src\scripting\ScriptContext.cpp" -Raw
    $scripting = Get-Content ".\src\scripting\Scripting.cpp" -Raw
    $functionOverride = Get-Content ".\src\scripting\FunctionOverride.cpp" -Raw

    foreach ($marker in @(
        "RegistrationId",
        "SourceFile",
        "SourceLineStart",
        "SourceLineEnd",
        "LuaFunctionIdentity",
        "AttachSource",
        "DeepTraceScope",
        "DeepLuaHook",
        "LUA_MASKLINE",
        "lua_getinfo(aLuaState, \"S\", aDebug)",
        "UnresolvedLineEvents",
        "EnterDeepRegistrationBoundary",
        "CET_Runtime_Profile_Deep_Samples.csv",
        "CET_Runtime_Profile_Deep_Lines.csv",
        "CET_Runtime_Profile_Deep_Functions.csv",
        "CET_Runtime_Profile_Deep_Callsites.csv",
        "CET_Runtime_Profile_FrameMultiplicity.csv",
        "RecordFrameMultiplicity",
        "CallsiteSourceFile"
    )) {
        if (-not $profilerHeader.Contains($marker)) {
            throw "Profiler callback-identity marker missing from CETRuntimeProfiler.h: $marker"
        }
    }

    if (-not $scriptContext.Contains("ReadProfilerSourceInfo") -or
        -not $scriptContext.Contains("AttachSource")) {
        throw "ScriptContext event source attribution was not patched."
    }

    if (-not $scripting.Contains("CETRuntimeProfiler::Get().BeginGameFrame()")) {
        throw "Scripting exact rendered-frame boundary was not patched."
    }

    if (-not $functionOverride.Contains("AttachSource") -or
        -not $functionOverride.Contains("context@") -or
        -not $functionOverride.Contains("DeepTraceScope")) {
        throw "FunctionOverride instance/source/deep attribution was not patched."
    }

    "Callback identity/source/deep/frame-multiplicity verification: PASS"
}
finally {
    Pop-Location
}
