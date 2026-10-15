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
        "CallsiteSourceFile",
        "FrameInvocationOrdinal"
    )) {
        if (-not $profilerHeader.Contains($marker)) {
            throw "Profiler callback-identity marker missing from CETRuntimeProfiler.h: $marker"
        }
    }

    if (-not $scriptContext.Contains("ReadProfilerSourceInfo") -or
        -not $scriptContext.Contains("AttachSource")) {
        throw "ScriptContext event source attribution was not patched."
    }

    foreach ($marker in @(
        "TryProfiledLuaFunction",
        "TryProfiledLuaFunction(m_logger, m_profOnHook, m_onHook)",
        "TryProfiledLuaFunction(m_logger, m_profOnTweak, m_onTweak)",
        "TryProfiledLuaFunction(m_logger, m_profOnInit, m_onInit)",
        "TryProfiledLuaFunction(m_logger, m_profOnUpdate, m_onUpdate, aDeltaTime)",
        "TryProfiledLuaFunction(m_logger, m_profOnDraw, m_onDraw)",
        "TryProfiledLuaFunction(m_logger, m_profOnOverlayOpen, m_onOverlayOpen)",
        "TryProfiledLuaFunction(m_logger, m_profOnOverlayClose, m_onOverlayClose)",
        "TryProfiledLuaFunction(m_logger, m_profOnShutdown, m_onShutdown)",
        "original Lua hook restored"
    )) {
        if (-not $scriptContext.Contains($marker)) {
            throw "ScriptContext event failure-isolation marker missing: $marker"
        }
    }

    if ($scriptContext -match 'DeepTraceScope\s+deepTrace\(m_profOn') {
        throw "ScriptContext event failure isolation regressed: deep hook spans CET error handling."
    }

    if (-not $scripting.Contains("CETRuntimeProfiler::Get().BeginGameFrame()")) {
        throw "Scripting exact rendered-frame boundary was not patched."
    }

    if (-not $functionOverride.Contains("AttachSource") -or
        -not $functionOverride.Contains("context@") -or
        -not $functionOverride.Contains("DeepTraceScope")) {
        throw "FunctionOverride instance/source/deep attribution was not patched."
    }

    # Native marshalling retains method and boundary scope, but parent-class
    # RTTI is unknown because a non-null parent can be invalid in gameplay.
    # Do not reintroduce a parent dereference here or in the allocator.
    $nativeProxy=Get-Content ".\src\reverse\NativeProxy.cpp" -Raw
    $nativeGcPatch=Get-Content (Join-Path $PSScriptRoot 'Patch-LuaJIT-GC-Source.ps1') -Raw
    foreach($token in @('NativeClassHash','ggc_trace_class_set',
                        'uint64_t oldSize, newSize, methodHash, classHash',
                        'MethodScope(uint32_t boundary, uint64_t methodHash, uint64_t classHash')) {
        if(!$profilerHeader.Contains($token) -and !$nativeGcPatch.Contains($token)) {
            throw "Native class-qualified marshalling probe missing: $token"
        }
    }
    if($functionOverride.Contains('apFunction->parent->GetName()')) {
        throw 'Unsafe FunctionOverride parent RTTI dereference reintroduced'
    }
    if(([regex]::Matches($functionOverride,
            'CETRuntimeProfiler::MethodScope ggcMethodScope\(\s*[67],\s*apFunction->fullName\.hash,\s*0ULL,').Count) -ne 2) {
        throw 'Both FunctionOverride marshalling scopes must preserve method identity and unknown-class fallback'
    }
    if(!$nativeProxy.Contains('0ULL, // NativeProxy func is not proven to be a CClassFunction')) {
        throw 'NativeProxy must preserve unknown class rather than casting unproven function type'
    }
    if(!$profilerHeader.Contains('s.classHash') -or !$profilerHeader.Contains('NativeMethodHash,NativeClassHash')) {
        throw 'Class hash not exported in matching bounded sample CSV'
    }
    "Native method/boundary marshalling and safe unknown-class ABI verification: PASS"

    if(!$profilerHeader.Contains('CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv') -or
       !$profilerHeader.Contains('ggc_registered_pressure_read') -or
       !$nativeGcPatch.Contains('GGCRegisteredPressure *p=&ggc_registered_pressure[owner]') -or
       !$nativeGcPatch.Contains('memset(ggc_registered_pressure, 0,')) {
        throw 'Callback-owned allocation partitions or reset/export path missing'
    }
    "Callback-owned allocator partition and shared owner-zero isolation: PASS"
    "Callback identity/source/deep/frame-multiplicity verification: PASS"
}
finally {
    Pop-Location
}
