param(
    [Parameter(Mandatory=$true)]
    [string]$SourceRoot
)

$ErrorActionPreference = "Stop"

function Read-Utf8([string]$Path) {
    return [IO.File]::ReadAllText($Path)
}

function Write-Utf8([string]$Path, [string]$Text) {
    [IO.File]::WriteAllText(
        $Path,
        $Text,
        (New-Object Text.UTF8Encoding($false))
    )
}

function Replace-LiteralOnce {
    param(
        [string]$Path,
        [string]$Old,
        [string]$New,
        [string]$Label
    )

    $text = Read-Utf8 $Path
    $first = $text.IndexOf($Old, [StringComparison]::Ordinal)
    if ($first -lt 0) {
        throw "Could not find patch target '$Label' in $Path"
    }

    $second = $text.IndexOf(
        $Old,
        $first + $Old.Length,
        [StringComparison]::Ordinal
    )

    if ($second -ge 0) {
        throw "Patch target '$Label' occurs more than once in $Path."
    }

    Write-Utf8 $Path (
        $text.Substring(0, $first) +
        $New +
        $text.Substring($first + $Old.Length)
    )
}

function Replace-RegexOnce {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement,
        [string]$Label
    )

    $text = Read-Utf8 $Path
    $matches = [regex]::Matches(
        $text,
        $Pattern,
        [Text.RegularExpressions.RegexOptions]::Multiline
    )

    if ($matches.Count -ne 1) {
        throw "Patch target '$Label' matched $($matches.Count) times in $Path; expected 1."
    }

    $patched = [regex]::Replace(
        $text,
        $Pattern,
        $Replacement,
        [Text.RegularExpressions.RegexOptions]::Multiline
    )

    Write-Utf8 $Path $patched
}

$src = (Resolve-Path $SourceRoot).Path

Push-Location $src
try {
    $head = (git rev-parse HEAD).Trim()
}
finally {
    Pop-Location
}

$expectedHead = "61bd6214f0f5f8748589c9e476538614a13908c0"
if ($head -ne $expectedHead) {
    throw "Unexpected CET revision. Expected $expectedHead, got $head."
}

$scH = Join-Path $src "src\scripting\ScriptContext.h"
$scC = Join-Path $src "src\scripting\ScriptContext.cpp"
$ssC = Join-Path $src "src\scripting\ScriptStore.cpp"
$scriptingC = Join-Path $src "src\scripting\Scripting.cpp"
$foH = Join-Path $src "src\scripting\FunctionOverride.h"
$foC = Join-Path $src "src\scripting\FunctionOverride.cpp"
$profH = Join-Path $src "src\scripting\CETRuntimeProfiler.h"

foreach ($p in @($scH, $scC, $ssC, $scriptingC, $foH, $foC)) {
    if (!(Test-Path -LiteralPath $p)) {
        throw "Missing CET source file: $p"
    }
}

Copy-Item `
    -LiteralPath (Join-Path $PSScriptRoot "CETRuntimeProfiler.h") `
    -Destination $profH `
    -Force

# v2.10.0 safety invariant: FunctionOverride::Context must remain exactly
# upstream. The v2.8 crash was caused by invasive registration-time handling.
$foHHashBefore = (Get-FileHash -Algorithm SHA256 $foH).Hash

# --------------------------------------------------------------------------
# ScriptContext.h
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $scH `
    -Old '#include "LuaSandbox.h"' `
    -New "#include `"LuaSandbox.h`"`n#include `"CETRuntimeProfiler.h`"" `
    -Label "ScriptContext profiler include"

Replace-RegexOnce `
    -Path $scH `
    -Pattern '([ \t]*std::shared_ptr<spdlog::logger>\s+m_logger\{nullptr\};\s*\r?\n[ \t]*bool\s+m_initialized\{false\};)' `
    -Replacement @'
$1

    CETRuntimeProfiler::Counter* m_profOnHook{};
    CETRuntimeProfiler::Counter* m_profOnTweak{};
    CETRuntimeProfiler::Counter* m_profOnInit{};
    CETRuntimeProfiler::Counter* m_profOnShutdown{};
    CETRuntimeProfiler::Counter* m_profOnUpdate{};
    CETRuntimeProfiler::Counter* m_profOnDraw{};
    CETRuntimeProfiler::Counter* m_profOnOverlayOpen{};
    CETRuntimeProfiler::Counter* m_profOnOverlayClose{};
'@ `
    -Label "ScriptContext profiler counter pointers"

# --------------------------------------------------------------------------
# ScriptContext.cpp
# --------------------------------------------------------------------------

Replace-RegexOnce `
    -Path $scC `
    -Pattern '(m_logger\s*=\s*env\["__logger"\]\.get<std::shared_ptr<spdlog::logger>>\(\);\s*\r?\n)' `
    -Replacement @'
$1
    auto& profiler = CETRuntimeProfiler::Get();
    m_profOnHook = profiler.Register(m_name, "event", "onHook");
    m_profOnTweak = profiler.Register(m_name, "event", "onTweak");
    m_profOnInit = profiler.Register(m_name, "event", "onInit");
    m_profOnShutdown = profiler.Register(m_name, "event", "onShutdown");
    m_profOnUpdate = profiler.Register(m_name, "event", "onUpdate");
    m_profOnDraw = profiler.Register(m_name, "event", "onDraw");
    m_profOnOverlayOpen = profiler.Register(m_name, "event", "onOverlayOpen");
    m_profOnOverlayClose = profiler.Register(m_name, "event", "onOverlayClose");

'@ `
    -Label "ScriptContext counter registration"

$eventPatches = @(
    @{
        Label = "TriggerOnHook timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onHook\);'
        Replacement = @'
if (m_onHook)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnHook);
        TryLuaFunction(m_logger, m_onHook);
    }
'@
    },
    @{
        Label = "TriggerOnTweak timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onTweak\);'
        Replacement = @'
if (m_onTweak)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnTweak);
        TryLuaFunction(m_logger, m_onTweak);
    }
'@
    },
    @{
        Label = "TriggerOnInit timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onInit\);'
        Replacement = @'
if (m_onInit)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnInit);
        TryLuaFunction(m_logger, m_onInit);
    }
'@
    },
    @{
        Label = "TriggerOnUpdate timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onUpdate,\s*aDeltaTime\);'
        Replacement = @'
if (m_onUpdate)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnUpdate);
        TryLuaFunction(m_logger, m_onUpdate, aDeltaTime);
    }
'@
    },
    @{
        Label = "TriggerOnDraw timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onDraw\);'
        Replacement = @'
if (m_onDraw)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnDraw);
        TryLuaFunction(m_logger, m_onDraw);
    }
'@
    },
    @{
        Label = "TriggerOnOverlayOpen timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onOverlayOpen\);'
        Replacement = @'
if (m_onOverlayOpen)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnOverlayOpen);
        TryLuaFunction(m_logger, m_onOverlayOpen);
    }
'@
    },
    @{
        Label = "TriggerOnOverlayClose timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onOverlayClose\);'
        Replacement = @'
if (m_onOverlayClose)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnOverlayClose);
        TryLuaFunction(m_logger, m_onOverlayClose);
    }
'@
    },
    @{
        Label = "TriggerOnShutdown timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onShutdown\);'
        Replacement = @'
if (m_onShutdown)
    {
        CETRuntimeProfiler::Scope profileScope(m_profOnShutdown);
        TryLuaFunction(m_logger, m_onShutdown);
    }
'@
    }
)

foreach ($p in $eventPatches) {
    Replace-RegexOnce `
        -Path $scC `
        -Pattern $p.Pattern `
        -Replacement $p.Replacement `
        -Label $p.Label
}

# --------------------------------------------------------------------------
# Scripting.cpp - global profiler control API available from CET console/mods
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $scriptingC `
    -Old '#include "FunctionOverride.h"' `
    -New "#include `"FunctionOverride.h`"`n#include `"CETRuntimeProfiler.h`"" `
    -Label "Scripting profiler include"

Replace-RegexOnce `
    -Path $scriptingC `
    -Pattern '(globals\["GetVersion"\]\s*=\s*\[\]\(\)\s*->\s*std::string\s*\{\s*return CET_GIT_TAG;\s*\};\s*\r?\n)' `
    -Replacement @'
$1
    globals["CETProfilerStart"] = []() -> std::string {
        CETRuntimeProfiler::Get().StartCapture();
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerPause"] = []() -> std::string {
        CETRuntimeProfiler::Get().PauseCapture();
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerResume"] = []() -> std::string {
        CETRuntimeProfiler::Get().ResumeCapture();
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerStop"] = []() -> std::string {
        CETRuntimeProfiler::Get().StopCapture(true);
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerReset"] = []() -> std::string {
        CETRuntimeProfiler::Get().Reset();
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerDump"] = []() -> std::string {
        CETRuntimeProfiler::Get().Dump();
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerStatus"] = []() -> std::string {
        return CETRuntimeProfiler::Get().Status();
    };
    globals["CETProfilerIsRunning"] = []() -> bool {
        return CETRuntimeProfiler::Get().IsCapturing();
    };
    globals["CETProfilerSetSpikeThresholdMs"] = [](double aThresholdMs) -> double {
        return CETRuntimeProfiler::Get().SetSpikeThresholdMs(aThresholdMs);
    };
    globals["CETProfilerGetSpikeThresholdMs"] = []() -> double {
        return CETRuntimeProfiler::Get().GetSpikeThresholdMs();
    };
    globals["CETProfilerSetTimelineBucketMs"] = [](double aBucketMs) -> double {
        return CETRuntimeProfiler::Get().SetTimelineBucketMs(aBucketMs);
    };
    globals["CETProfilerGetTimelineBucketMs"] = []() -> double {
        return CETRuntimeProfiler::Get().GetTimelineBucketMs();
    };
    globals["CETProfilerMark"] = [](const std::string& aLabel) -> std::string {
        return CETRuntimeProfiler::Get().Mark(aLabel);
    };

    globals["CETProfilerSchedulerRegisterJob"] =
        [](const std::string& aOwner,
           const std::string& aJobType,
           const std::string& aJob,
           double aIntervalValue,
           const std::string& aIntervalUnit) -> uint64_t {
            return CETRuntimeProfiler::Get().RegisterSchedulerJob(
                aOwner, aJobType, aJob, aIntervalValue, aIntervalUnit);
        };
    globals["CETProfilerSchedulerFrameBegin"] = [](uint64_t aFrame) {
        CETRuntimeProfiler::Get().SchedulerFrameBegin(aFrame);
    };
    globals["CETProfilerSchedulerFrameEnd"] = [](uint64_t aFrame) {
        CETRuntimeProfiler::Get().SchedulerFrameEnd(aFrame);
    };
    globals["CETProfilerSchedulerJobBegin"] = [](uint64_t aHandle) -> uint64_t {
        return CETRuntimeProfiler::Get().SchedulerJobBegin(aHandle);
    };
    globals["CETProfilerSchedulerJobEnd"] =
        [](uint64_t aHandle, uint64_t aStartTicksNs, uint64_t aFrame) {
            CETRuntimeProfiler::Get().SchedulerJobEnd(
                aHandle, aStartTicksNs, aFrame);
        };
    globals["CETProfilerSetSchedulerJobSpikeThresholdMs"] =
        [](double aThresholdMs) -> double {
            return CETRuntimeProfiler::Get().SetSchedulerJobSpikeThresholdMs(
                aThresholdMs);
        };
    globals["CETProfilerGetSchedulerJobSpikeThresholdMs"] = []() -> double {
        return CETRuntimeProfiler::Get().GetSchedulerJobSpikeThresholdMs();
    };
    globals["CETProfilerSetSchedulerFrameBurstThresholdMs"] =
        [](double aThresholdMs) -> double {
            return CETRuntimeProfiler::Get().SetSchedulerFrameBurstThresholdMs(
                aThresholdMs);
        };
    globals["CETProfilerGetSchedulerFrameBurstThresholdMs"] = []() -> double {
        return CETRuntimeProfiler::Get().GetSchedulerFrameBurstThresholdMs();
    };

'@ `
    -Label "Profiler global control API"

# --------------------------------------------------------------------------
# ScriptStore.cpp
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $ssC `
    -Old '#include "Utils.h"' `
    -New "#include `"Utils.h`"`n#include `"CETRuntimeProfiler.h`"" `
    -Label "ScriptStore profiler include"

Replace-RegexOnce `
    -Path $ssC `
    -Pattern '(ScriptStore::ScriptStore\(LuaSandbox& aLuaSandbox, const Paths& aPaths, VKBindings& aBindings\)\s*\r?\n\s*: m_sandbox\(aLuaSandbox\)\s*\r?\n\s*, m_paths\(aPaths\)\s*\r?\n\s*, m_bindings\(aBindings\)\s*\r?\n\{\s*\r?\n)(\})' `
    -Replacement @'
$1    CETRuntimeProfiler::Get().Configure(m_paths.CETRoot());
$2
'@ `
    -Label "Profiler output path configuration"

Replace-RegexOnce `
    -Path $ssC `
    -Pattern '([ \t]*m_bindings\.InitializeMods\(m_vkBinds\);\s*\r?\n)' `
    -Replacement @'
$1
    CETRuntimeProfiler::Get().Reset();
'@ `
    -Label "Profiler runtime reset"

Replace-RegexOnce `
    -Path $ssC `
    -Pattern '(void ScriptStore::TriggerOnOverlayOpen\(\) const\s*\r?\n\{\s*\r?\n\s*for \(const auto& mod : m_contexts \| std::views::values\)\s*\r?\n\s*mod\.TriggerOnOverlayOpen\(\);\s*\r?\n)(\})' `
    -Replacement @'
$1
    CETRuntimeProfiler::Get().Dump();
$2
'@ `
    -Label "Profiler overlay dump trigger"

# --------------------------------------------------------------------------
# FunctionOverride.cpp - v2.10.0 non-invasive callback profiler
# --------------------------------------------------------------------------
#
# CRASH FIX:
# v2.8 dereferenced pContext->Environment["__logger"] during
# FunctionOverride::Override registration. WinDbg proved that this reached
# sol::basic_reference::push -> lua_checkstack with lua_State* == nullptr.
#
# v2.10.0 rules:
#   1. FunctionOverride.h / Context layout is untouched.
#   2. Registration performs ZERO Lua/Sol environment operations.
#   3. Metadata is bound externally by Context*.
#   4. Mod ownership is resolved lazily only while CET is already executing
#      the callback under its normal GetLockedState() path.
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $foC `
    -Old '#include "FunctionOverride.h"' `
    -New "#include `"FunctionOverride.h`"`n#include `"CETRuntimeProfiler.h`"" `
    -Label "FunctionOverride profiler include"

# Add a tiny native-only resolver in the existing anonymous namespace.
# Environment access is guarded by lua_state() and happens only at execution.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '(FunctionOverride\* s_pOverride = nullptr;\s*\r?\n)' `
    -Replacement @'
$1
CETRuntimeProfiler::Counter* ResolveProfilerCounter(
    const FunctionOverride::Context* apContext)
{
    if (!apContext)
        return nullptr;

    // Never push an environment whose reference has no Lua state.
    if (!apContext->Environment.lua_state())
        return nullptr;

    std::string modName = "<unknown>";

    // This executes only from CET callback execution paths after
    // GetLockedState() has established a live Lua state.
    const auto logger =
        apContext->Environment["__logger"].get<std::shared_ptr<spdlog::logger>>();
    if (logger)
        modName = logger->name();

    return CETRuntimeProfiler::Get().ResolveCallback(apContext, modName);
}

'@ `
    -Label "FunctionOverride lazy profiler resolver"

# Clear external bindings before CET destroys Context objects. This prevents
# stale Context* keys from surviving a reload / Clear cycle.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*m_functions\.clear\(\);\s*\r?\n)' `
    -Replacement @'
    CETRuntimeProfiler::Get().ClearCallbackBindings();
$1
'@ `
    -Label "FunctionOverride profiler binding clear"

# Registration: bind native metadata only. Critically, do NOT access
# pContext->Environment here.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*pContext->Forward = !aAbsolute;\s*\r?\n)' `
    -Replacement @'
$1
        const std::string profilerKind =
            aAbsolute ? "Override" : (aAfter ? "ObserveAfter" : "Observe");
        const std::string profilerTarget =
            acTypeName + "::" + acFullName;

        CETRuntimeProfiler::Get().BindCallback(
            pContext.get(),
            profilerKind,
            profilerTarget);

'@ `
    -Label "FunctionOverride native-only callback binding"

# ObserveBefore: resolve ownership outside the timed scope, then time only the
# original callback body.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*for \(const auto& call : aChain\.Before\)\s*\r?\n[ \t]*\{\s*\r?\n)([ \t]*const auto result = call->ScriptFunction\(as_args\(\*apOrigArgs\)\);)' `
    -Replacement @'
$1            auto* profilerCounter = ResolveProfilerCounter(call.get());
            CETRuntimeProfiler::Scope profileScope(profilerCounter);
$2
'@ `
    -Label "Observe callback timing"

# ObserveAfter.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*for \(const auto& call : aChain\.After\)\s*\r?\n[ \t]*\{\s*\r?\n)([ \t]*const auto result = call->ScriptFunction\(as_args\(\*apOrigArgs\)\);)' `
    -Replacement @'
$1            auto* profilerCounter = ResolveProfilerCounter(call.get());
            CETRuntimeProfiler::Scope profileScope(profilerCounter);
$2
'@ `
    -Label "ObserveAfter callback timing"

# v2.11.0 OVERRIDE ATTRIBUTION FIX.
#
# A mod Override receives a Lua `next` function. Time spent after the mod calls
# next() is downstream work: another Override or the original RED/native game
# function. v2.10.1 only subtracted nested *profiled callbacks*, so the final
# original/native function had no profiler scope and was incorrectly charged
# to the calling mod's Override.
#
# Scope(nullptr) is intentionally used as a transparent timing boundary. It
# records no row of its own, but its full elapsed duration is added as child
# time to the currently active mod callback. Nested profiler scopes remain
# children of this boundary, avoiding double subtraction.
#
# The root CET invocation also enters one of these lambdas, but with no active
# mod callback parent, so it contributes nothing to attribution.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '(\[&\]\(sol::variadic_args aWrapArgs, sol::this_state aState\) -> sol::variadic_results\s*\r?\n[ \t]*\{\s*\r?\n)([ \t]*std::string errorMessage;)' `
    -Replacement @'
$1            CETRuntimeProfiler::Scope profilerDownstreamBoundary(nullptr);
$2
'@ `
    -Label "Override terminal next()/real-function attribution boundary"

Replace-RegexOnce `
    -Path $foC `
    -Pattern '(\[&, aStep\]\(sol::variadic_args aWrapArgs, sol::this_state aState\) -> sol::variadic_results\s*\r?\n[ \t]*\{\s*\r?\n)([ \t]*const auto call = \(aChain\.Overrides\.rbegin\(\) \+ aStep\)->get\(\);)' `
    -Replacement @'
$1            CETRuntimeProfiler::Scope profilerDownstreamBoundary(nullptr);
$2
'@ `
    -Label "Override chained next() attribution boundary"

# Override. The resolver runs after CET has already entered the valid locked
# Lua execution path. v2.11.0 downstream boundaries make exclusive time mean
# the mod callback itself: chained Overrides and the original game function
# reached through next()/wrappedMethod() are excluded.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*auto next = WrapNextOverride\(aChain, aStep \+ 1, aLuaState, aLuaContext, aLuaArgs, apRealFunction, apRealContext, aLock\);\s*\r?\n)([ \t]*auto result = aLuaContext == sol::nil \? call->ScriptFunction\(as_args\(aLuaArgs\), next\) : call->ScriptFunction\(aLuaContext, as_args\(aLuaArgs\), next\);)' `
    -Replacement @'
$1            auto* profilerCounter = ResolveProfilerCounter(call);
            CETRuntimeProfiler::Scope profileScope(profilerCounter);
$2
'@ `
    -Label "Override callback timing"

# Hard safety checks.
$foHHashAfter = (Get-FileHash -Algorithm SHA256 $foH).Hash
if ($foHHashAfter -ne $foHHashBefore) {
    throw "v2.10.0 safety failure: FunctionOverride.h was modified."
}

$foCText = Read-Utf8 $foC

foreach ($marker in @(
    "ResolveProfilerCounter",
    "BindCallback(",
    "ResolveCallback(",
    "ClearCallbackBindings",
    "CETRuntimeProfiler::Scope profileScope",
    "profilerDownstreamBoundary"
)) {
    if (-not $foCText.Contains($marker)) {
        throw "FunctionOverride v2.10.0 profiler marker missing after patch: $marker"
    }
}

# Regression guard for the exact v2.8 crash source. There must be no profiler
# registration-time Environment lookup following pContext->Forward.
if ($foCText -match 'pContext->Forward = !aAbsolute;[\s\S]{0,1200}pContext->Environment\["__logger"\]') {
    throw "v2.10.0 safety failure: registration-time Environment logger lookup is still present."
}

$scriptingText = Read-Utf8 $scriptingC
foreach ($marker in @(
    "CETProfilerStart",
    "CETProfilerPause",
    "CETProfilerResume",
    "CETProfilerStop",
    "CETProfilerStatus",
    "CETProfilerSetSpikeThresholdMs",
    "CETProfilerGetSpikeThresholdMs",
    "CETProfilerSetTimelineBucketMs",
    "CETProfilerGetTimelineBucketMs",
    "CETProfilerMark",
    "CETProfilerSchedulerRegisterJob",
    "CETProfilerSchedulerFrameBegin",
    "CETProfilerSchedulerFrameEnd",
    "CETProfilerSchedulerJobBegin",
    "CETProfilerSchedulerJobEnd",
    "CETProfilerSetSchedulerJobSpikeThresholdMs",
    "CETProfilerGetSchedulerJobSpikeThresholdMs",
    "CETProfilerSetSchedulerFrameBurstThresholdMs",
    "CETProfilerGetSchedulerFrameBurstThresholdMs"
)) {
    if (-not $scriptingText.Contains($marker)) {
        throw "v2.10.0 control API marker missing after patch: $marker"
    }
}

Write-Host ""
Write-Host "CET v1.37.1 FULL CALLBACK profiler v2.11.0 patch applied successfully." -ForegroundColor Green
Write-Host "Validated source commit: $expectedHead"
Write-Host "Coverage: events + Observe + ObserveAfter + Override" -ForegroundColor Green
Write-Host "FunctionOverride::Context: VERIFIED UNCHANGED" -ForegroundColor Green
Write-Host "Registration-time Lua/Sol access: NONE" -ForegroundColor Green
Write-Host "Callback ownership: lazy resolution during valid locked execution" -ForegroundColor Green
Write-Host "Override downstream next()/native time: excluded from mod exclusive attribution" -ForegroundColor Green
Write-Host "Capture control: Start / Pause / Resume / Stop / Reset / Dump / Status" -ForegroundColor Green
Write-Host "CSV rates use captured time only (paused time excluded)" -ForegroundColor Green
Write-Host "Scheduler bridge API: per-job timing + individual spikes + combined frame bursts" -ForegroundColor Green
Write-Host "No files under cyber_engine_tweaks\mods were touched."
