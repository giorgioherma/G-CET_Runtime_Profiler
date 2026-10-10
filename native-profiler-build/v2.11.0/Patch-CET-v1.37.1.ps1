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

Replace-LiteralOnce `
    -Path $scC `
    -Old @'
namespace
{
'@ `
    -New @'
namespace
{
struct ProfilerSourceInfo
{
    std::string File;
    int StartLine{};
    int EndLine{};
    uint64_t FunctionIdentity{};
};

ProfilerSourceInfo ReadProfilerSourceInfo(const sol::function& aFunction)
{
    ProfilerSourceInfo info;
    auto* state = aFunction.lua_state();
    if (!state || aFunction == sol::nil)
        return info;

    aFunction.push();
    info.FunctionIdentity = static_cast<uint64_t>(
        reinterpret_cast<uintptr_t>(lua_topointer(state, -1)));
    lua_Debug debugInfo{};
    if (lua_getinfo(state, ">S", &debugInfo) != 0)
    {
        const char* source = debugInfo.source ? debugInfo.source : "";
        if (source[0] == '@')
            ++source;

        info.File = source;
        info.StartLine = debugInfo.linedefined;
        info.EndLine = debugInfo.lastlinedefined;
    }

    return info;
}

template <typename... Args>
sol::protected_function_result TryProfiledLuaFunction(
    const std::shared_ptr<spdlog::logger>& acpLogger,
    CETRuntimeProfiler::Counter* aCounter,
    const sol::function& aFunc,
    Args... aArgs)
{
    sol::protected_function_result result{};
    if (aFunc)
    {
        try
        {
            {
                CETRuntimeProfiler::DeepTraceScope deepTrace(
                    aCounter, aFunc.lua_state());
                CETRuntimeProfiler::Scope profileScope(aCounter);
                result = aFunc(aArgs...);
            }
        }
        catch (std::exception& e)
        {
            acpLogger->error(e.what());
        }

        // The profiler scopes are already gone here. Error conversion/logging
        // therefore runs with CET's original Lua hook restored.
        if (!result.valid())
        {
            const sol::error cError = result;
            acpLogger->error(cError.what());
        }
    }
    return result;
}

'@ `
    -Label "ScriptContext profiler source helper"

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
TryProfiledLuaFunction(m_logger, m_profOnHook, m_onHook);
'@
    },
    @{
        Label = "TriggerOnTweak timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onTweak\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnTweak, m_onTweak);
'@
    },
    @{
        Label = "TriggerOnInit timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onInit\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnInit, m_onInit);
'@
    },
    @{
        Label = "TriggerOnUpdate timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onUpdate,\s*aDeltaTime\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnUpdate, m_onUpdate, aDeltaTime);
'@
    },
    @{
        Label = "TriggerOnDraw timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onDraw\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnDraw, m_onDraw);
'@
    },
    @{
        Label = "TriggerOnOverlayOpen timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onOverlayOpen\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnOverlayOpen, m_onOverlayOpen);
'@
    },
    @{
        Label = "TriggerOnOverlayClose timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onOverlayClose\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnOverlayClose, m_onOverlayClose);
'@
    },
    @{
        Label = "TriggerOnShutdown timing"
        Pattern = 'TryLuaFunction\(m_logger,\s*m_onShutdown\);'
        Replacement = @'
TryProfiledLuaFunction(m_logger, m_profOnShutdown, m_onShutdown);
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


# Capture the exact Lua source range registered for each CET event. This is
# metadata only; callback timing semantics remain unchanged.
Replace-RegexOnce `
    -Path $scC `
    -Pattern '(else\s*\r?\n\s*m_logger->error\("Tried to register an unknown event ''\{\}''!", acName\);)' `
    -Replacement @'
$1

        CETRuntimeProfiler::Counter* profilerCounter = nullptr;
        if (acName == "onHook") profilerCounter = m_profOnHook;
        else if (acName == "onTweak") profilerCounter = m_profOnTweak;
        else if (acName == "onInit") profilerCounter = m_profOnInit;
        else if (acName == "onShutdown") profilerCounter = m_profOnShutdown;
        else if (acName == "onUpdate") profilerCounter = m_profOnUpdate;
        else if (acName == "onDraw") profilerCounter = m_profOnDraw;
        else if (acName == "onOverlayOpen") profilerCounter = m_profOnOverlayOpen;
        else if (acName == "onOverlayClose") profilerCounter = m_profOnOverlayClose;

        if (profilerCounter)
        {
            const auto source = ReadProfilerSourceInfo(aCallback);
            CETRuntimeProfiler::Get().AttachSource(
                profilerCounter,
                source.File,
                source.StartLine,
                source.EndLine,
                source.FunctionIdentity);
        }
'@ `
    -Label "ScriptContext event source metadata"

# Event failure-isolation contract: the deep hook may exist only while the
# user's Lua function is executing. CET/sol error conversion and logging must
# happen after DeepTraceScope has restored the prior hook.
$scCText = Read-Utf8 $scC
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
    if (-not $scCText.Contains($marker)) {
        throw "ScriptContext profiler failure-isolation marker missing after patch: $marker"
    }
}
if ($scCText -match 'DeepTraceScope\s+deepTrace\(m_profOn') {
    throw "Event failure-isolation regression: a ScriptContext event still owns DeepTraceScope across CET error handling."
}

# --------------------------------------------------------------------------
# Scripting.cpp - global profiler control API available from CET console/mods
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $scriptingC `
    -Old '#include "FunctionOverride.h"' `
    -New "#include `"FunctionOverride.h`"`n#include `"CETRuntimeProfiler.h`"" `
    -Label "Scripting profiler include"

Replace-LiteralOnce `
    -Path $scriptingC `
    -Old 'static constexpr bool s_cThrowLuaErrors = true;' `
    -New @'
#include <cstring>

namespace
{
int ProfiledCollectgarbage(lua_State* state)
{
    const int args = lua_gettop(state);
    const char* operation = args > 0 && lua_type(state, 1) == LUA_TSTRING
        ? lua_tostring(state, 1) : nullptr;
    const char* action =
        (!operation || std::strcmp(operation, "collect") == 0) ? "collect" :
        std::strcmp(operation, "step") == 0 ? "step" : nullptr;
    auto& profiler = CETRuntimeProfiler::Get();
    const bool measure = action && profiler.IsCapturing();
    char caller[384]{};
    int line = 0;
    uint64_t before = 0;
    std::chrono::steady_clock::time_point start{};
    if (measure)
    {
        lua_Debug debug{};
        if (lua_getstack(state, 1, &debug) &&
            lua_getinfo(state, "Sl", &debug))
        {
            const char* source = debug.source ? debug.source : "";
            if (source[0] == '@') ++source;
            std::strncpy(caller, source, sizeof(caller) - 1);
            line = debug.currentline;
        }
        before = CETRuntimeProfiler::ReadLuaHeapBytes(state);
        start = std::chrono::steady_clock::now();
    }

    // Preserve exact original Lua C-function results and pcall error behavior.
    lua_pushvalue(state, lua_upvalueindex(1));
    lua_insert(state, 1);
    lua_call(state, args, LUA_MULTRET);
    const int results = lua_gettop(state);
    if (measure)
    {
        const auto end = std::chrono::steady_clock::now();
        const uint64_t after = CETRuntimeProfiler::ReadLuaHeapBytes(state);
        profiler.RecordGcExplicit(
            action, start, end, before, after, caller, line);
    }
    return results;
}
}

static constexpr bool s_cThrowLuaErrors = true;
'@ `
    -Label "Profiler timed collectgarbage original-function trampoline"

Replace-LiteralOnce `
    -Path $scriptingC `
    -Old @'
    // initialize sandbox
    m_sandbox.Initialize();

    auto& globals = m_sandbox.GetGlobals();
'@ `
    -New @'
    // initialize sandbox
    m_sandbox.Initialize();

    auto& globals = m_sandbox.GetGlobals();
    // Every CET mod sandbox inherits this shared whitelisted table.
    // Wrapping once covers CMB without touching mod files.
    {
        lua_State* state = luaVm.lua_state();
        globals.push();
        lua_getfield(state, -1, "collectgarbage");
        if (lua_isfunction(state, -1))
        {
            lua_pushcclosure(state, &ProfiledCollectgarbage, 1);
            lua_setfield(state, -2, "collectgarbage");
        }
        else lua_pop(state, 1);
        lua_pop(state, 1);
    }
'@ `
    -Label "Shared CET sandbox collectgarbage timing"


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
# Exact rendered-frame boundary.
#
# Scripting::TriggerOnUpdate is entered once per game/render update before
# ScriptStore fans onUpdate out to individual mods. Keep one native frame id
# alive until the next update so Observe/Override/event callbacks can report
# exact per-frame invocation multiplicity without per-frame global scans.
# --------------------------------------------------------------------------

Replace-LiteralOnce `
    -Path $scriptingC `
    -Old @'
void Scripting::TriggerOnUpdate(float aDeltaTime) const
{
    m_store.TriggerOnUpdate(aDeltaTime);
}
'@ `
    -New @'
void Scripting::TriggerOnUpdate(float aDeltaTime) const
{
    CETRuntimeProfiler::Get().BeginGameFrame();
    m_store.TriggerOnUpdate(aDeltaTime);
}
'@ `
    -Label "Exact profiler rendered-frame boundary"

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

    auto& profiler = CETRuntimeProfiler::Get();

    // Capture OFF must be inert: do not touch callback ownership, Lua
    // environment state, or source metadata until a capture is active.
    if (!profiler.IsCapturing())
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

    auto* counter = profiler.ResolveCallback(apContext, modName);

    // Source discovery is also lazy. Registration stays native-only and safe;
    // the Lua function is inspected only on the first real callback execution.
    if (counter && counter->SourceFile.empty())
    {
        auto* state = apContext->ScriptFunction.lua_state();
        if (state && apContext->ScriptFunction != sol::nil)
        {
            apContext->ScriptFunction.push();
            const uint64_t functionIdentity = static_cast<uint64_t>(
                reinterpret_cast<uintptr_t>(lua_topointer(state, -1)));
            lua_Debug debugInfo{};
            if (lua_getinfo(state, ">S", &debugInfo) != 0)
            {
                const char* source = debugInfo.source ? debugInfo.source : "";
                if (source[0] == '@')
                    ++source;

                profiler.AttachSource(
                    counter,
                    source,
                    debugInfo.linedefined,
                    debugInfo.lastlinedefined,
                    functionIdentity);
            }
        }
    }

    return counter;
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
$1            const auto result = [&]() {
                if (!CETRuntimeProfiler::Get().IsCapturing())
                    return call->ScriptFunction(as_args(*apOrigArgs));

                auto* profilerCounter = ResolveProfilerCounter(call.get());
                CETRuntimeProfiler::DeepTraceScope deepTrace(
                    profilerCounter, call->ScriptFunction.lua_state());
                CETRuntimeProfiler::Scope profileScope(profilerCounter);
                return call->ScriptFunction(as_args(*apOrigArgs));
            }();
'@ `
    -Label "Observe capture-gated full callback timing"

# ObserveAfter.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*for \(const auto& call : aChain\.After\)\s*\r?\n[ \t]*\{\s*\r?\n)([ \t]*const auto result = call->ScriptFunction\(as_args\(\*apOrigArgs\)\);)' `
    -Replacement @'
$1            const auto result = [&]() {
                if (!CETRuntimeProfiler::Get().IsCapturing())
                    return call->ScriptFunction(as_args(*apOrigArgs));

                auto* profilerCounter = ResolveProfilerCounter(call.get());
                CETRuntimeProfiler::DeepTraceScope deepTrace(
                    profilerCounter, call->ScriptFunction.lua_state());
                CETRuntimeProfiler::Scope profileScope(profilerCounter);
                return call->ScriptFunction(as_args(*apOrigArgs));
            }();
'@ `
    -Label "ObserveAfter capture-gated full callback timing"

# Override safety policy.
#
# Do NOT hold a profiler RAII scope across next()/wrappedMethod(). LuaJIT may
# forward luaL_error through Windows SEH; crossing an active C++ RAII timing
# object on that path is unsafe and can terminate the process without a normal
# Cyberpunk crash report. This exact boundary was previously removed for that
# reason and is intentionally kept out of the capture path.
#
# Override callback timing remains confined to the protected ScriptFunction
# call itself. Downstream next()/native time is conservatively included in the
# Override's exclusive attribution during this diagnostic pass.
Replace-RegexOnce `
    -Path $foC `
    -Pattern '([ \t]*auto next = WrapNextOverride\(aChain, aStep \+ 1, aLuaState, aLuaContext, aLuaArgs, apRealFunction, apRealContext, aLock\);\s*\r?\n)([ \t]*auto result = aLuaContext == sol::nil \? call->ScriptFunction\(as_args\(aLuaArgs\), next\) : call->ScriptFunction\(aLuaContext, as_args\(aLuaArgs\), next\);)' `
    -Replacement @'
$1            auto result = [&]() {
                if (!CETRuntimeProfiler::Get().IsCapturing())
                {
                    return aLuaContext == sol::nil
                        ? call->ScriptFunction(as_args(aLuaArgs), next)
                        : call->ScriptFunction(aLuaContext, as_args(aLuaArgs), next);
                }

                auto* profilerCounter = ResolveProfilerCounter(call);
                CETRuntimeProfiler::DeepTraceScope deepTrace(
                    profilerCounter, call->ScriptFunction.lua_state());
                CETRuntimeProfiler::Scope profileScope(profilerCounter);
                return aLuaContext == sol::nil
                    ? call->ScriptFunction(as_args(aLuaArgs), next)
                    : call->ScriptFunction(aLuaContext, as_args(aLuaArgs), next);
            }();
'@ `
    -Label "Override capture-gated full callback timing"

# Hard safety checks.
$foHHashAfter = (Get-FileHash -Algorithm SHA256 $foH).Hash
if ($foHHashAfter -ne $foHHashBefore) {
    throw "v2.10.0 safety failure: FunctionOverride.h was modified."
}

$foCText = Read-Utf8 $foC

foreach ($marker in @(
    "ResolveProfilerCounter",
    "if (!profiler.IsCapturing())",
    "if (!CETRuntimeProfiler::Get().IsCapturing())",
    "BindCallback(",
    "ResolveCallback(",
    "AttachSource(",
    "ClearCallbackBindings",
    "CETRuntimeProfiler::Scope profileScope",
    "CETRuntimeProfiler::DeepTraceScope"
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
    "CETProfilerGetSchedulerFrameBurstThresholdMs",
    "CETRuntimeProfiler::Get().BeginGameFrame()"
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
Write-Host "Capture OFF: FunctionOverride profiling fully bypassed" -ForegroundColor Green
Write-Host "Callback identity/source: registration ID + Lua source line range + closure identity" -ForegroundColor Green
Write-Host "Adaptive deep profiling: runtime hotset + sampled Lua call/return trees + callsites" -ForegroundColor Green
Write-Host "Exact frame telemetry: per-callback multiplicity from Scripting::TriggerOnUpdate" -ForegroundColor Green
Write-Host "Override downstream next()/native time: conservatively included; no RAII downstream boundary" -ForegroundColor Yellow
Write-Host "Capture control: Start / Pause / Resume / Stop / Reset / Dump / Status" -ForegroundColor Green
Write-Host "CSV rates use captured time only (paused time excluded)" -ForegroundColor Green
Write-Host "Scheduler bridge API: per-job timing + individual spikes + combined frame bursts" -ForegroundColor Green
Write-Host "No files under cyber_engine_tweaks\mods were touched."
