param(
    [Parameter(Mandatory=$true)]
    [string]$SourceRoot
)

$ErrorActionPreference = 'Stop'

function Must([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "CET 2.5 native instrumentation contract failed: $Message" }
}

function ExactCount([string]$Text, [string]$Token, [int]$Expected) {
    $matches = [regex]::Matches($Text, [regex]::Escape($Token))
    Must ($matches.Count -eq $Expected) "Expected $Expected occurrences of '$Token', got $($matches.Count)."
}

$foC = Get-Content -LiteralPath (Join-Path $SourceRoot 'src/scripting/FunctionOverride.cpp') -Raw
$foH = Get-Content -LiteralPath (Join-Path $SourceRoot 'src/scripting/FunctionOverride.h') -Raw
$scC = Get-Content -LiteralPath (Join-Path $SourceRoot 'src/scripting/ScriptContext.cpp') -Raw
$scripting = Get-Content -LiteralPath (Join-Path $SourceRoot 'src/scripting/Scripting.cpp') -Raw

# No extra fields or changes to the v1.38.1 Context memory/ABI.
Must ([regex]::IsMatch($foH, '(?s)struct Context\s*\{\s*sol::protected_function ScriptFunction;\s*sol::environment Environment;\s*bool Forward;\s*\};')) 'FunctionOverride::Context does not match the CET 1.38.1 ABI.'
Must ($foH.Contains('std::string RequestedTypeName;')) 'Deferred hook requests do not retain requested Lua class name.'
Must ($foC.Contains('m_pending.push_back({pClassType, pRealFunction, acFullName, acTypeName,')) 'Deferred hook registration dropped the source class.'
Must ($foC.Contains('pending.FullName, pending.RequestedTypeName,')) 'Deferred hook replay dropped the source class.'
Must ($foC.Contains('acTypeName + "::" + acFullName;')) 'Callback CSV targets must preserve the requested Lua class name.'
Must ($foC.Contains('std::unique_lock lock(m_lock, std::try_to_lock);')) 'CET deadlock-prevention try_lock was lost.'
Must ($foC.Contains('void FunctionOverride::ApplyPendingOverrides()')) 'Pending hook application was removed.'
Must ($scripting.Contains('m_override.ApplyPendingOverrides();')) 'Pending hook processing was removed from onUpdate.'
Must ($foC.Contains('CETRuntimeProfiler::Get().ClearCallbackBindings();')) 'Stale callback binding cleanup is missing.'
Must ([regex]::Matches($foC, 'ResolveProfilerCounter\(call').Count -eq 3) 'Exactly one profiler resolver is required for Observe, ObserveAfter and Override.'

# Ownership stays lazy and capture-gated. No Lua environment reads at hook registration.
Must ($foC.Contains('if (!profiler.IsCapturing())')) 'Capture-off ownership bypass missing.'
Must ($foC.Contains('sol::get_environment(apContext->ScriptFunction)')) 'Real callback function environment ownership lookup missing.'
Must ($foC.Contains('readOwner(apContext->Environment)')) 'Safe CET Context fallback for callbacks without function environment missing.'
Must ($foC.Contains('if (!apContext->ScriptFunction.lua_state())')) 'Callback environment must be checked for valid Lua state before read.'
$regMatch = [regex]::Match($foC, '(?s)pContext->Forward = !aAbsolute;(.{0,800}?)pEntry->Overrides')
Must ($regMatch.Success) 'Hook registration metadata sequence missing.'
Must (!$regMatch.Groups[1].Value.Contains('["__logger"]')) 'Hook registration dereferences a Lua environment before execution.'

# The scope boundaries must end *before* CET can call luaL_error (Lua longjmp).
ExactCount $foC 'CETRuntimeProfiler::Scope profilerDownstreamBoundary(nullptr);' 2
Must ([regex]::IsMatch($foC, '(?s)sol::variadic_results results = \[&\]\(\) \{\s*CETRuntimeProfiler::Scope profilerDownstreamBoundary\(nullptr\);\s*return RTTIHelper::Get\(\)\.ExecuteFunction\(.*?\);\s*\}\(\);')) 'Terminal downstream timing does not finish before error handling.'
Must ([regex]::IsMatch($foC, '(?s)auto result = \[&\]\(\) \{\s*// This scope exits before CET.*?CETRuntimeProfiler::Scope profilerDownstreamBoundary\(nullptr\);')) 'Chained downstream timing is not enclosed by a safe C++ call.'
Must (![regex]::IsMatch($foC, '(?s)\[&\]\(sol::variadic_args aWrapArgs, sol::this_state aState\) -> sol::variadic_results\s*\{\s*CETRuntimeProfiler::Scope')) 'Terminal next() wrapper spans Lua longjmp.'
Must (![regex]::IsMatch($foC, '(?s)\[&, aStep\]\(sol::variadic_args aWrapArgs, sol::this_state aState\) -> sol::variadic_results\s*\{\s*CETRuntimeProfiler::Scope')) 'Chained next() wrapper spans Lua longjmp.'

# All original public profiler callbacks and lifecycle hooks still exist.
foreach ($target in @('m_profOnHook', 'm_profOnTweak', 'm_profOnInit', 'm_profOnShutdown', 'm_profOnUpdate', 'm_profOnDraw', 'm_profOnOverlayOpen', 'm_profOnOverlayClose')) {
    Must ($scC.Contains($target)) "Lost public CET event counter: $target."
}
foreach ($api in @('CETProfilerStart', 'CETProfilerPause', 'CETProfilerResume', 'CETProfilerStop', 'CETProfilerDump', 'CETProfilerStatus', 'CETProfilerSchedulerRegisterJob', 'CETProfilerSchedulerJobBegin', 'CETProfilerSchedulerJobEnd')) {
    Must ($scripting.Contains($api)) "Lost public profiler control / Scheduler API: $api."
}

Write-Host 'PASS: CET 1.38.1 native hook, owner, deferred registration and safe attribution contract.' -ForegroundColor Green
