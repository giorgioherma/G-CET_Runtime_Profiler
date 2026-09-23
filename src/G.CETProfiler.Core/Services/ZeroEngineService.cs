using System.Text;
using System.Text.RegularExpressions;
using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record ZeroFileState(string Kind, string Text, string? Hash = null);

internal sealed class ZeroEngineService
{
    private readonly PackageManifest manifest;

    public ZeroEngineService(PackageManifest manifest)
    {
        this.manifest = manifest;
    }

    public ZeroFileState GetInitState(ProfilerPaths paths)
    {
        if (!File.Exists(paths.ZeroInit))
            return new ZeroFileState("absent", "0-ENGINE NOT FOUND");

        var text = File.ReadAllText(paths.ZeroInit);
        if (text.Contains(manifest.ZeroEngine.ProfilerBridgeMarker, StringComparison.Ordinal))
            return new ZeroFileState("profiler-bridge", "ADAPTIVE PROFILER BRIDGE PRESENT");

        if (IsSchedulerIntegrated(paths.ZeroInit))
            return new ZeroFileState("integrated", "EXISTING SCHEDULER INTEGRATION");

        if (IsAdaptiveCompatible(paths.ZeroInit))
            return new ZeroFileState("adaptive", "ADAPTIVE BRIDGE AVAILABLE");

        return new ZeroFileState("unsafe", "STRUCTURE NOT RECOGNIZED");
    }

    public ZeroFileState GetSchedulerState(ProfilerPaths paths)
    {
        var aware = manifest.ZeroEngine.ProfilerSchedulerSha256.ToLowerInvariant();
        if (!File.Exists(paths.ZeroScheduler))
            return new ZeroFileState("absent", "ABSENT - WILL ADD");

        var hash = FileSystemService.Sha256(paths.ZeroScheduler);
        return string.Equals(hash, aware, StringComparison.OrdinalIgnoreCase)
            ? new ZeroFileState("aware", "PROFILER-AWARE", hash)
            : new ZeroFileState("other", "PRESENT - WILL BACKUP + REPLACE", hash);
    }

    public ZeroFileState GetAdaptiveSchedulerState(ProfilerPaths paths)
    {
        var aware = manifest.ZeroEngine.ProfilerAdaptiveSchedulerSha256.ToLowerInvariant();
        if (!File.Exists(paths.ZeroAdaptiveScheduler))
            return new ZeroFileState("absent", "WILL ADD CETProfilerScheduler");

        var hash = FileSystemService.Sha256(paths.ZeroAdaptiveScheduler);
        return string.Equals(hash, aware, StringComparison.OrdinalIgnoreCase)
            ? new ZeroFileState("aware", "CETProfilerScheduler READY", hash)
            : new ZeroFileState("other", "CUSTOM CETProfilerScheduler - WILL BACKUP", hash);
    }

    public bool IsSchedulerIntegrated(string initPath)
    {
        if (!File.Exists(initPath)) return false;

        var text = File.ReadAllText(initPath);
        if (!string.IsNullOrWhiteSpace(manifest.ZeroEngine.ProfilerBridgeMarker) &&
            text.Contains(manifest.ZeroEngine.ProfilerBridgeMarker, StringComparison.Ordinal))
            return false;

        var hasRequire = Regex.IsMatch(text, @"require\s*\(\s*['""]modules/Scheduler['""]\s*\)");
        var hasInit = Regex.IsMatch(text, @"Scheduler\.Init\s*\(");
        var hasUpdate = Regex.IsMatch(text, @"Scheduler\.Update\s*[,\(]");
        var hasEngineApi = Regex.IsMatch(text, @"Engine\.Schedule");
        var hasModApi = Regex.IsMatch(text, @"Mod\.Schedule");
        return hasRequire && hasInit && hasUpdate && hasEngineApi && hasModApi;
    }

    public bool IsAdaptiveCompatible(string initPath)
    {
        if (!File.Exists(initPath)) return false;

        var text = File.ReadAllText(initPath);
        if (text.Contains(manifest.ZeroEngine.ProfilerBridgeMarker, StringComparison.Ordinal))
            return true;

        var hasReturn = Regex.IsMatch(text, @"(?m)^[ \t]*return[ \t]+Engine[ \t]*$");
        var hasEngineTable = Regex.IsMatch(text, @"(?m)^\s*(local\s+)?Engine\s*=\s*\{");
        return hasReturn && hasEngineTable;
    }

    public void AddAdaptiveProfilerSchedulerBridge(string initPath)
    {
        var text = File.ReadAllText(initPath);
        if (text.Contains(manifest.ZeroEngine.ProfilerBridgeMarker, StringComparison.Ordinal))
            return;

        if (!IsAdaptiveCompatible(initPath))
            throw new InvalidOperationException(
                "0-Engine init.lua structure is not recognized as safe for adaptive Scheduler injection. " +
                "No 0-Engine files were changed. Use 'Core profiler only - leave 0-Engine untouched' instead.");

        var matches = Regex.Matches(text, @"(?m)^[ \t]*return[ \t]+Engine[ \t]*$");
        if (matches.Count == 0)
            throw new InvalidOperationException("0-Engine export anchor was not found.");

        var anchor = matches[^1];
        var patched = text[..anchor.Index] + AdaptiveBridge + text[anchor.Index..];

        File.WriteAllText(initPath, patched, new UTF8Encoding(false));

        if (!File.ReadAllText(initPath).Contains(manifest.ZeroEngine.ProfilerBridgeMarker, StringComparison.Ordinal))
            throw new InvalidOperationException("Adaptive 0-Engine profiler bridge verification failed.");
    }

    private const string AdaptiveBridge = """
-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2
-- Temporary profiler integration. Profiler Manager restores this exact init.lua from backup.
-- The profiler module has a unique filename so it cannot collide with 0-Engine's own Scheduler.lua.
local __CETRP_Scheduler = require('modules/CETProfilerScheduler')
__CETRP_Scheduler.Init(nil, nil)

local function __CETRP_NormalizeOptions(owner, options, fn)
    if type(options) == "function" then
        fn = options
        options = {}
    end
    local copy = {}
    for k, v in pairs(options or {}) do copy[k] = v end
    copy.owner = copy.owner or owner or "unscoped"
    return copy, fn
end

if type(Engine.Schedule) ~= "table" then
    Engine.Schedule = {}
    function Engine.Schedule.EveryFrame(options, fn)
        options, fn = __CETRP_NormalizeOptions("unscoped", options, fn)
        return __CETRP_Scheduler.EveryFrame(options, fn)
    end
    function Engine.Schedule.EveryFrames(interval, options, fn)
        options, fn = __CETRP_NormalizeOptions("unscoped", options, fn)
        return __CETRP_Scheduler.EveryFrames(interval, options, fn)
    end
    function Engine.Schedule.Every(seconds, options, fn)
        options, fn = __CETRP_NormalizeOptions("unscoped", options, fn)
        return __CETRP_Scheduler.Every(seconds, options, fn)
    end
    function Engine.Schedule.After(seconds, options, fn)
        options, fn = __CETRP_NormalizeOptions("unscoped", options, fn)
        return __CETRP_Scheduler.After(seconds, options, fn)
    end
    function Engine.Schedule.NextTick(options, fn)
        options, fn = __CETRP_NormalizeOptions("unscoped", options, fn)
        return __CETRP_Scheduler.NextTick(options, fn)
    end
    Engine.Schedule.GetInfo = __CETRP_Scheduler.GetInfo
end

if type(Engine.Register) == "function" then
    local __CETRP_OriginalRegister = Engine.Register
    Engine.Register = function(name)
        local Mod = __CETRP_OriginalRegister(name)
        if type(Mod) == "table" and type(Mod.Schedule) ~= "table" then
            Mod.Schedule = {}

            local function scopedArgs(options, fn)
                return __CETRP_NormalizeOptions(name, options, fn)
            end

            local function scopedCallback(fn)
                return function(...)
                    if type(Engine.IsModEnabled) == "function" then
                        local okEnabled, enabled = pcall(Engine.IsModEnabled, name)
                        if okEnabled and enabled == false then return end
                    end
                    return fn(...)
                end
            end

            function Mod.Schedule.EveryFrame(options, fn)
                options, fn = scopedArgs(options, fn)
                return __CETRP_Scheduler.EveryFrame(options, scopedCallback(fn))
            end
            function Mod.Schedule.EveryFrames(interval, options, fn)
                options, fn = scopedArgs(options, fn)
                return __CETRP_Scheduler.EveryFrames(interval, options, scopedCallback(fn))
            end
            function Mod.Schedule.Every(seconds, options, fn)
                options, fn = scopedArgs(options, fn)
                return __CETRP_Scheduler.Every(seconds, options, scopedCallback(fn))
            end
            function Mod.Schedule.After(seconds, options, fn)
                options, fn = scopedArgs(options, fn)
                return __CETRP_Scheduler.After(seconds, options, scopedCallback(fn))
            end
            function Mod.Schedule.NextTick(options, fn)
                options, fn = scopedArgs(options, fn)
                return __CETRP_Scheduler.NextTick(options, scopedCallback(fn))
            end
        end
        return Mod
    end
end

local __CETRP_Frame = 0
local __CETRP_GameClock = 0
local __CETRP_LastRealClock = os.clock()
registerForEvent("onUpdate", function(delta)
    delta = tonumber(delta) or 0
    __CETRP_Frame = __CETRP_Frame + 1
    __CETRP_GameClock = __CETRP_GameClock + delta

    local realClock = os.clock()
    local realDelta = realClock - __CETRP_LastRealClock
    __CETRP_LastRealClock = realClock

    local playing = true
    if type(Engine.IsPlaying) == "function" then
        local okPlaying, value = pcall(Engine.IsPlaying)
        if okPlaying then playing = value == true end
    end

    local state = nil
    if type(Engine.GetState) == "function" then
        local okState, value = pcall(Engine.GetState)
        if okState then state = value end
    end

    local player = nil
    if type(Engine.GetPlayer) == "function" then
        local okPlayer, value = pcall(Engine.GetPlayer)
        if okPlayer then player = value end
    end

    pcall(__CETRP_Scheduler.Update, {
        frame = __CETRP_Frame,
        dt = delta,
        realDt = realDelta,
        elapsed = delta,
        now = __CETRP_GameClock,
        generation = 0,
        playing = playing,
        inMenu = state and state.inMenu or false,
        player = player,
        state = state
    })
end)
-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_END v2

""";
}
