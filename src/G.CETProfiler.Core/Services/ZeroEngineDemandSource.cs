using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// One scheduler-owned demand lane per producer. The existing host still owns
// callback order and the frame clock. No second ticker or per-mod timer.
internal static class ZeroEngineDemandSource
{
    internal const string InitPath = "0-Engine/init.lua";
    internal const string SchedulerPath = "0-Engine/modules/G-CET/Scheduler.lua";
    private const string Marker = "-- G-CET scheduler demand v1";

    internal static bool TryScheduler(string source, out string next)
    {
        next = source;
        if (source.Contains(Marker, StringComparison.Ordinal))
            return source.Contains("Scheduler.Demand = demand", StringComparison.Ordinal) &&
                source.Contains("function demand.TrackFrame(", StringComparison.Ordinal) &&
                source.Contains("function demand.IsDue(", StringComparison.Ordinal);
        if (source.Contains("Scheduler.Demand", StringComparison.Ordinal) ||
            !source.Contains("function Scheduler.Update(", StringComparison.Ordinal) ||
            !source.Contains("function Scheduler.EveryFrames(", StringComparison.Ordinal))
            return false;
        var export = Regex.Match(source, @"(?m)^[ \t]*return[ \t]+Scheduler[ \t]*;?[ \t]*\r?$");
        if (!export.Success || !string.IsNullOrWhiteSpace(source[(export.Index + export.Length)..]))
            return false;
        next = source.Insert(export.Index, Newlines(source, Controller) + Newlines(source, "\n\n"));
        return true;
    }

    // Source proof: only these established module ownership paths populate
    // and remove private registry entries. Preserve the actual original Update,
    // modulo, transitions, query APIs, reset, diagnostics and error handling.
    // A changed registration/storage contract refuses only this producer.
    internal static bool TryProducer(string source, string service, out string next)
    {
        next = source;
        var prox = service == "Proximity";
        if (!prox && service != "SpatialHash") return false;
        var registry = prox ? "zones" : "sets";
        var record = prox ? "zone" : "setData";
        var key = prox ? "zoneCounter" : "name";
        var removalKey = prox ? "capturedId" : "name";
        var marker = "-- G-CET demand registration v1: " + service;
        var attach = record + ".__gcetDemand = __gcetDemand.TrackFrame(\"" + service + "\", " +
            (prox ? "zone.throttle" : "setData.config.pollThrottle") + ")";
        var detach = "if " + registry + "[" + removalKey + "] and " +
            registry + "[" + removalKey + "].__gcetDemand then " +
            registry + "[" + removalKey + "].__gcetDemand.Cancel() end";
        var bind = "local __gcetDemand = require('modules/G-CET/Scheduler').Demand";
        var capability = service + ".__gcetDemand = __gcetDemand";
        var validate = "if type(__gcetDemand) ~= 'table' or type(__gcetDemand.TrackFrame) ~= 'function' or type(__gcetDemand.IsDue) ~= 'function' then __gcetDemand = nil end";
        var attachGuard = prox ? "if __gcetDemand then" : "if __gcetDemand and setData.config.pollRadiusSq then";
        var oldAdd = registry + "[" + key + "] = " + record;
        var oldRemove = registry + "[" + removalKey + "] = nil";
        // Check unique registry mutation and original cadence. Private
        // registry must not be reassigned or exposed by a different API.
        if (!source.Contains("function " + service + ".Update(frame)", StringComparison.Ordinal) ||
            !source.Contains(prox ? "frame % zone.throttle == 0" :
                "cfg.pollRadiusSq and frame % cfg.pollThrottle == 0", StringComparison.Ordinal) ||
            !source.Contains("function " + service + "." +
                (prox ? "RegisterZone(config)" : "RegisterSpatialSet(name, entries, config, source)"),
                StringComparison.Ordinal) ||
            Regex.Matches(source, @"(?m)^[ \t]*(?:local[ \t]+)?" + registry +
                @"[ \t]*=[ \t]*").Count != 1 ||
            Regex.Matches(source, @"(?m)^[ \t]*" + registry +
                @"\[[^\r\n]+\][ \t]*=[ \t]*").Count != 2)
            return false;

        var candidate = source;
        if (source.Contains(marker, StringComparison.Ordinal))
        {
            if (Count(source, bind) != 1 || Count(source, attach) != 1 ||
                Count(source, detach) != 1 || Count(source, oldAdd) != 1 ||
                Count(source, oldRemove) != 1) return false;
            if (Count(source, capability) == 1 && Count(source, validate) == 1 &&
                Count(source, attachGuard) == 1) return true;
            // Complete the earlier development form only when its exact
            // registration and removal contracts remain intact.
            if (source.Contains(capability, StringComparison.Ordinal) ||
                source.Contains(validate, StringComparison.Ordinal) ||
                source.Contains(attachGuard, StringComparison.Ordinal)) return false;
            if (!AfterLine(ref candidate, bind, validate + "\n" + capability))
                return false;
            if (prox)
            {
                if (!ReplaceLine(ref candidate, attach, attachGuard + "\n    " + attach + "\nend"))
                    return false;
            }
            else if (!ReplaceLine(ref candidate, "if setData.config.pollRadiusSq then", attachGuard))
                return false;
        }
        else
        {
            if (source.Contains("__gcetDemand", StringComparison.Ordinal)) return false;
            if (!AfterLine(ref candidate, "local " + registry + " = {}",
                    marker + "\n" + bind + "\n" + validate + "\n" + capability) ||
                !AfterLine(ref candidate, oldAdd, attachGuard + "\n    " + attach + "\nend") ||
                !BeforeLine(ref candidate, oldRemove, detach))
                return false;
        }
        next = candidate;
        return true;
    }

    internal static bool TryHost(string source, string service, out string next)
    {
        next = source;
        var marker = "-- G-CET demand dispatch v1: " + service;
        var original = "pcall(" + service + ".Update, currentFrame)";
        var oldGate = "if __gcetDemand.IsDue(\"" + service + "\", currentFrame) then";
        // A partial installation may have the host but not the matching
        // producer/scheduler. Never suppress original work in that state.
        var gate = "if not __gcetDemand or " + service + ".__gcetDemand ~= __gcetDemand or __gcetDemand.IsDue(\"" + service + "\", currentFrame) then";
        var bind = "local __gcetDemand = require('modules/G-CET/Scheduler').Demand";
        if (!source.Contains("local function UpdateFrame(delta)", StringComparison.Ordinal) ||
            !source.Contains(service + ".SetPlayerPos(pos)", StringComparison.Ordinal) ||
            Count(source, original) != 1)
            return false;
        var candidate = source;
        if (source.Contains(marker, StringComparison.Ordinal))
        {
            if (Count(source, bind) != 1) return false;
            if (Count(source, gate) == 1) return true;
            if (!ReplaceLine(ref candidate, oldGate, gate)) return false;
            next = candidate;
            return true;
        }
        if (Count(candidate, bind) == 0)
        {
            if (candidate.Contains("__gcetDemand", StringComparison.Ordinal) ||
                !BeforeLine(ref candidate, "local function UpdateFrame(delta)", bind))
                return false;
        }
        else if (Count(candidate, bind) != 1) return false;
        if (!ReplaceLine(ref candidate, original, marker + "\n" +
            gate + "\n    " + original + "\nend")) return false;
        next = candidate;
        return true;
    }

    private const string DerivedMarker = "-- G-CET derived sample demand v1";
    private const string SampleMarker = "-- G-CET demand sample slots v1";
    private const string DerivedHostMarker = "-- G-CET derived sample host v1";

    // Extend the same scheduler-owned demand table. A sample is offered
    // at its original producer phase. Scalar getters resolve only when
    // requested; an escaped raw table requires continuous original sampling.
    internal static bool TrySampleController(string source, out string next)
    {
        next = source;
        if (source.Contains(SampleMarker, StringComparison.Ordinal))
        {
            if (Count(source, "function demand.DefineSample(") != 1 ||
                Count(source, "function sample.Offer(") != 1 ||
                Count(source, "function sample.Read(") != 1 ||
                Count(source, "function sample.Expose(") != 1 ||
                Count(source, "function sample.Reset(") != 1) return false;
            if (source.Contains("demand.SampleEpochVersion = 1", StringComparison.Ordinal))
                return Count(source, "local offeredEpoch") == 1 &&
                    Count(source, "offeredEpoch = epoch()") == 1 &&
                    Count(source, "offeredEpoch ~= epoch()") == 1;
            // Complete the earlier development sample slot in place.
            // Epoch identity comes from the existing host frame clock.
            var candidate = source;
            if (!AfterLine(ref candidate, "local samples = {}", "demand.SampleEpochVersion = 1") ||
                !ReplaceLine(ref candidate, "function demand.DefineSample(key, produce)",
                    "function demand.DefineSample(key, produce, epoch)") ||
                !ReplaceLine(ref candidate, "if samples[key] or type(produce) ~= \"function\" then return nil end",
                    "if samples[key] or type(produce) ~= \"function\" or type(epoch) ~= \"function\" then return nil end") ||
                !AfterLine(ref candidate, "local available, sampled, exposed = false, false, false", "local offeredEpoch") ||
                !ReplaceLine(ref candidate, "if not available or sampled then return end",
                    "if not available or sampled or offeredEpoch ~= epoch() then return end") ||
                !AfterLine(ref candidate, "available, sampled = true, false", "offeredEpoch = epoch()") ||
                !AfterLine(ref candidate, "available, sampled = false, false", "offeredEpoch = nil"))
                return false;
            next = candidate;
            return true;
        }
        if (source.Contains("DefineSample", StringComparison.Ordinal)) return false;
        return BeforeLine(ref next, "Scheduler.Demand = demand", SampleController);
    }

    internal static bool TryDerivedProducer(string source, out string next)
    {
        next = source;
        const string getter = "function DerivedState.Get()";
        const string update = "cached.lookPitch = resolveLookPitch()";
        const string reset = "cached.lookPitch = 0";
        const string export = "return DerivedState";
        const string loading = "cached.isLoading = loading == true";
        const string loadingReset = "if loading == true and __gcetPitchSample then __gcetPitchSample.Reset() end";
        if (source.Contains(DerivedMarker, StringComparison.Ordinal))
        {
            if (Count(source, "function DerivedState.__gcetEnableDemand(") != 1 ||
                Count(source, "function DerivedState.__gcetPeek(") != 1 ||
                Count(source, "function DerivedState.__gcetReadField(") != 1 ||
                Count(source, "function DerivedState.__gcetExpose(") != 1 ||
                Count(source, "__gcetPitchSample.Offer()") != 1 ||
                Count(source, "__gcetPitchSample.Expose()") != 3) return false;
            if (source.Contains("controller.SampleEpochVersion ~= 1", StringComparison.Ordinal))
                return Count(source, loadingReset) == 1 &&
                    Count(source, "__gcetPitchSample.Reset()") == 2 &&
                    Count(source, "function DerivedState.__gcetEnableDemand(controller, epoch)") == 1;
            var candidate = source;
            if (Count(source, "__gcetPitchSample.Reset()") != 1 ||
                !ReplaceLine(ref candidate, "function DerivedState.__gcetEnableDemand(controller)",
                    "function DerivedState.__gcetEnableDemand(controller, epoch)") ||
                !ReplaceLine(ref candidate,
                    "if type(controller) ~= \"table\" or type(controller.DefineSample) ~= \"function\" then return false end",
                    "if type(controller) ~= \"table\" or controller.SampleEpochVersion ~= 1 or type(controller.DefineSample) ~= \"function\" or type(epoch) ~= \"function\" then return false end") ||
                !ReplaceUnique(ref candidate, "    end)\n    if not __gcetPitchSample then return false end",
                    "    end, epoch)\n    if not __gcetPitchSample then return false end") ||
                !AfterLine(ref candidate, loading, loadingReset)) return false;
            next = candidate;
            return true;
        }
        // The admitted producer is a pure scalar camera read with no
        // transition event, internal consumer or native reference retained.
        // Unproved changes to its body/other accesses retain original polling.
        const string pureReader = """
local function resolveLookPitchInner()
    local camSys = Game.GetCameraSystem()
    if not camSys then return 0 end
    local fwd = camSys:GetActiveCameraForward()
    if not fwd then return 0 end
    local clamped = math.max(-1.0, math.min(1.0, fwd.z))
    return math.deg(math.asin(clamped))
end
""";
        const string protectedReader = """
local function resolveLookPitch()
    local ok, result = pcall(resolveLookPitchInner)
    return ok and result or 0
end
""";
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (Count(normalized, NormalizeLf(pureReader)) != 1 || Count(normalized, NormalizeLf(protectedReader)) != 1 ||
            Regex.Matches(source, @"\blookPitch\b").Count != 3 ||
            Regex.Matches(source, @"\bresolveLookPitch\b").Count != 2 ||
            Count(source, "function DerivedState.Update(player)") != 1 ||
            Count(source, "function DerivedState.Reset()") != 1 ||
            Count(source, "function DerivedState.SetLoading(loading)") != 1 ||
            source.IndexOf("function DerivedState.SetLoading(loading)", StringComparison.Ordinal) < source.IndexOf("function DerivedState.Update(player)", StringComparison.Ordinal) ||
            Count(source, getter) != 1 ||
            Count(source, "return cached\n") + Count(source, "return cached\r\n") != 1 ||
            Count(normalized, getter + "\n    return cached\nend") != 1 ||
            source.Contains("__gcetPitch", StringComparison.Ordinal))
            return false;
        // Do not move the original read phase or introduce another timer.
        // The host handshake must succeed before this module may skip a read.
        if (!BeforeLine(ref next, "function DerivedState.Update(player)",
                DerivedMarker + "\nlocal __gcetPitchSample, __gcetRawExposed = nil, false") ||
            !ReplaceLine(ref next, update,
                "if __gcetPitchSample then __gcetPitchSample.Offer() else " + update + " end") ||
            !AfterLine(ref next, reset, "if __gcetPitchSample then __gcetPitchSample.Reset() end") ||
            !AfterLine(ref next, loading, loadingReset) ||
            !AfterLine(ref next, getter,
                "__gcetRawExposed = true\nif __gcetPitchSample then __gcetPitchSample.Expose() end") ||
            !BeforeLine(ref next, export, DerivedAdapter))
        {
            next = source;
            return false;
        }
        return true;
    }

    // The host gives Lifecycle a raw State table. Admit only a module
    // which proves that the raw reference is private and uses unrelated
    // fields. Its runtime capability protects mixed overlays.
    internal static bool TryDerivedLifecycle(string source, out string next)
    {
        next = source;
        const string capability = "Lifecycle.__gcetDerivedStateSafe = true";
        var code = WithoutLuaTrivia(source);
        if (Count(code, "function Lifecycle.Init(events, state, logger)") != 1 ||
            Count(code, "local State = nil") != 1 ||
            Count(code, "State = state") != 1 ||
            Regex.Matches(code, @"\bstate\b").Count != 2)
            return false;
        code = Regex.Replace(code, @"\blocal\s+State\s*=\s*nil|\bState\s*=\s*state|\bState\s*\.\s*(?:player|inMenu)\b", "");
        if (Regex.IsMatch(code, @"\bState\b")) return false;
        if (Count(source, capability) == 1) return true;
        if (source.Contains("__gcetDerivedStateSafe", StringComparison.Ordinal)) return false;
        return BeforeLine(ref next, "return Lifecycle", capability);
    }

    // Ignore comments and literal contents when checking for an additional
    // raw State alias. Quoted/dynamic State indexing is not admitted.
    private static string WithoutLuaTrivia(string source) => Regex.Replace(source,
        """--\[(?<ce>=*)\[.*?\]\k<ce>\]|--[^\r\n]*|\[(?<le>=*)\[.*?\]\k<le>\]|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'""",
        " ", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static bool HasProvenRawStatePaths(string source)
    {
        var code = WithoutLuaTrivia(source);
        code = Regex.Replace(code,
            @"\blocal\s+State\s*=|\bDeepReadOnly\s*\(\s*State\s*,|\bLifecycle\.Init\s*\(\s*Events\s*,\s*State\s*,|\bState\s*\.\s*[A-Za-z_][A-Za-z0-9_]*", "");
        return !Regex.IsMatch(code, @"\bState\b");
    }

    internal static bool TryDerivedHost(string source, out string next)
    {
        next = source;
        const string bind = "local __gcetDemand = require('modules/G-CET/Scheduler').Demand";
        const string read = "local v = tbl[k]";
        const string raw = "return next, tbl, nil";
        const string update = "State.derived = DerivedState.Get()";
        const string legacyHandshake = "__gcetDerivedDemand = type(DerivedState.__gcetEnableDemand) == 'function' and type(DerivedState.__gcetPeek) == 'function' and type(DerivedState.__gcetReadField) == 'function' and type(DerivedState.__gcetExpose) == 'function' and DerivedState.__gcetEnableDemand(__gcetDemand)";
        var baseHandshake = legacyHandshake.Replace("(__gcetDemand)", "(__gcetDemand, function() return currentFrame end)", StringComparison.Ordinal);
        var lifecycleRequired = source.Contains("Lifecycle.Init(Events, State, Logger)", StringComparison.Ordinal);
        var handshake = lifecycleRequired
            ? baseHandshake.Replace("= type(DerivedState.", "= Lifecycle.__gcetDerivedStateSafe == true and type(DerivedState.", StringComparison.Ordinal)
            : baseHandshake;
        const string readHook = "if __gcetDerivedDemand and tbl == State.derived then DerivedState.__gcetReadField(tbl, k) end";
        const string rawHook = "if __gcetDerivedDemand then if tbl == State then DerivedState.__gcetExpose() elseif tbl == State.derived then DerivedState.__gcetExpose(tbl) end end";
        const string peek = "State.derived = __gcetDerivedDemand and DerivedState.__gcetPeek() or DerivedState.Get()";
        if (source.Contains(DerivedHostMarker, StringComparison.Ordinal))
        {
            if (Count(source, readHook) != 1 || Count(source, rawHook) != 1 ||
                Count(source, peek) != 1 || Count(source, "local __gcetDerivedDemand = false") != 1 ||
                !HasProvenRawStatePaths(source.Replace(readHook, "", StringComparison.Ordinal)
                    .Replace(rawHook, "", StringComparison.Ordinal))) return false;
            if (Count(source, handshake) == 1) return true;
            // Complete earlier host forms without creating another binding.
            var gatedLegacy = legacyHandshake.Replace("= type(DerivedState.", "= Lifecycle.__gcetDerivedStateSafe == true and type(DerivedState.", StringComparison.Ordinal);
            return ReplaceLine(ref next, legacyHandshake, handshake) ||
                (lifecycleRequired && ReplaceLine(ref next, gatedLegacy, handshake));
        }
        // Admit the original proxy/raw-iteration contract. Unknown direct
        // reads and escapes cannot safely be made lazy by this transform.
        if (!HasProvenRawStatePaths(source) ||
            Count(source, "local currentFrame = 0") != 1 ||
            Count(source, "currentFrame = currentFrame + 1") != 1 ||
            Count(source, "local function DeepReadOnly(tbl, path)") != 1 ||
            Count(source, "local function UpdateFrame(delta)") != 1 ||
            Count(source, "local stateProxy = nil") != 1 ||
            Count(source, "stateProxy = DeepReadOnly(State, \"state\")") != 1 ||
            Count(source, "function Engine.GetState()") != 1 ||
            Count(source, "pcall(DerivedState.Update, player)") != 1 ||
            Count(source, "DerivedState.Get()") != 1 ||
            Regex.Matches(source, @"\bState\.derived\b").Count !=
                1 + Count(source, "State.derived.district") ||
            source.Contains("__gcetDerivedDemand", StringComparison.Ordinal) ||
            source.Contains("lookPitch", StringComparison.Ordinal))
            return false;
        if (Count(next, bind) == 0 &&
            !BeforeLine(ref next, "local function UpdateFrame(delta)", bind))
            return false;
        if (Count(next, bind) != 1 ||
            !BeforeLine(ref next, "local function DeepReadOnly(tbl, path)",
                DerivedHostMarker + "\nlocal __gcetDerivedDemand = false") ||
            !AfterLine(ref next, bind, handshake) ||
            !BeforeLine(ref next, read, readHook) ||
            !BeforeLine(ref next, raw, rawHook) ||
            !ReplaceLine(ref next, update, peek))
        {
            next = source;
            return false;
        }
        return true;
    }

    internal const string SampleController = """
-- G-CET demand sample slots v1
local samples = {}
demand.SampleEpochVersion = 1
function demand.DefineSample(key, produce, epoch)
    if samples[key] or type(produce) ~= "function" or type(epoch) ~= "function" then return nil end
    local sample = {}
    local available, sampled, exposed = false, false, false
    local offeredEpoch
    function sample.Read()
        if not available or sampled or offeredEpoch ~= epoch() then return end
        sampled = true
        produce()
    end
    function sample.Offer()
        available, sampled = true, false
        offeredEpoch = epoch()
        if exposed then sample.Read() end
    end
    function sample.Expose()
        -- A raw reference has no unsubscribe handle. Preserve future
        -- author-phase updates for the lifetime of this escaped table.
        exposed = true
        sample.Read()
    end
    function sample.Reset()
        available, sampled = false, false
        offeredEpoch = nil
        -- Keep exposure: the author resets the same retained table.
    end
    samples[key] = sample
    return sample
end
""";

    internal const string DerivedAdapter = """
function DerivedState.__gcetEnableDemand(controller, epoch)
    if __gcetPitchSample then return true end
    if type(controller) ~= "table" or controller.SampleEpochVersion ~= 1 or type(controller.DefineSample) ~= "function" or type(epoch) ~= "function" then return false end
    __gcetPitchSample = controller.DefineSample("DerivedState.lookPitch", function()
        cached.lookPitch = resolveLookPitch()
    end, epoch)
    if not __gcetPitchSample then return false end
    if __gcetRawExposed then __gcetPitchSample.Expose() end
    return true
end

-- Only the matching host uses this non-escaping internal accessor.
function DerivedState.__gcetPeek()
    return cached
end

function DerivedState.__gcetReadField(tbl, key)
    if tbl == cached and key == "lookPitch" and __gcetPitchSample then
        __gcetPitchSample.Read()
    end
end

function DerivedState.__gcetExpose(tbl)
    if tbl == nil or tbl == cached then
        __gcetRawExposed = true
        if __gcetPitchSample then __gcetPitchSample.Expose() end
    end
end
""";

    private const string IdleMarker = "-- G-CET scheduler active-work demand v1";
    private const string IdleHostMarker = "-- G-CET scheduler idle host v1";

    // The canonical scheduler owns active work and necessary cleanup. Keep
    // phase clocks and profiler frame scopes even with no runnable work.
    // No new host timer, no context creation for a completely idle frame.
    internal static bool TryIdleScheduler(string source, out string next)
    {
        next = source;
        if (source.Contains(IdleMarker, StringComparison.Ordinal))
            return Count(source, "function Scheduler.HasPendingWork()") == 1 &&
                Count(source, "function Scheduler.TryIdleFrame(") == 1 &&
                Count(source, "local function beginFrame(") == 1 &&
                Count(source, "local function cancelJob(") == 1 &&
                Count(source, "local function setJobActive(") == 1 &&
                Count(source, "if job.active then activeJobs = activeJobs + 1 end") == 2 &&
                Count(source, "cancelJob(job)") == 4;
        if (source.Contains("Scheduler.TryIdleFrame", StringComparison.Ordinal) ||
            source.Contains("Scheduler.HasPendingWork", StringComparison.Ordinal) ||
            source.Contains("local activeJobs", StringComparison.Ordinal) ||
            source.Contains("local function cancelJob(", StringComparison.Ordinal) ||
            source.Contains("local function beginFrame(", StringComparison.Ordinal))
            return false;

        // Prove every stock job state mutation before using an O(1) count.
        // The internal job records must stay owned by these private queues.
        var code = WithoutLuaTrivia(source);
        if (Regex.Matches(code, @"\.\s*active\s*=(?!=)").Count != 5 ||
            Regex.Matches(code, @"\.\s*cancelled\s*=(?!=)").Count != 3 ||
            Regex.Matches(code, @"\blocal\s+(?:frameBuckets|timedJobs)\b\s*=(?!=)").Count != 2 ||
            Regex.Matches(code, @"\b(?:frameBuckets|timedJobs)\b\s*=(?!=)").Count != 3 ||
            Count(code, "bucket[#bucket + 1] = job") != 1 ||
            Count(code, "timedJobs[#timedJobs + 1] = job") != 1 ||
            Count(code, "return makeHandle(job)") != 2 ||
            Count(code, "active = options.active ~= false") != 2 ||
            Count(code, "cancelled = false") != 2 ||
            Count(code, "function Scheduler.Update(ctx)") != 1)
            return false;

        const string handles = """
local function makeHandle(job)
    local handle = {}
    function handle.Cancel() job.cancelled = true; job.active = false end
    handle.unsubscribe = handle.Cancel
    function handle.Pause() if not job.cancelled then job.active = false end end
    function handle.Resume() if not job.cancelled then job.active = true end end
    function handle.SetActive(value) if not job.cancelled then job.active = value == true end end
    function handle.IsActive() return job.active and not job.cancelled end
    return handle
end
""";
        const string countedHandles = """
local function makeHandle(job)
    local handle = {}
    function handle.Cancel() cancelJob(job) end
    handle.unsubscribe = handle.Cancel
    function handle.Pause() if not job.cancelled then setJobActive(job, false) end end
    function handle.Resume() if not job.cancelled then setJobActive(job, true) end end
    function handle.SetActive(value) if not job.cancelled then setJobActive(job, value == true) end end
    function handle.IsActive() return job.active and not job.cancelled end
    return handle
end
""";
        const string updateStart = """
function Scheduler.Update(ctx)
    lastFrame = ctx.frame

    local profileActive = Profiler.enabled and Profiler.isRunning()
    if profileActive then
        Profiler.frameBegin(ctx.frame)
    end

    local gameNow = tonumber(ctx.now)
    if gameNow ~= nil then
        phaseGameClock = gameNow
    else
        phaseGameClock = phaseGameClock + (ctx.dt or 0)
    end
    phaseRealClock = phaseRealClock + (ctx.realDt or ctx.dt or 0)
""";
        const string updateShared = """
local function beginFrame(frame, dt, realDt, now)
    lastFrame = frame

    local profileActive = Profiler.enabled and Profiler.isRunning()
    if profileActive then
        Profiler.frameBegin(frame)
    end

    local gameNow = tonumber(now)
    if gameNow ~= nil then
        phaseGameClock = gameNow
    else
        phaseGameClock = phaseGameClock + (dt or 0)
    end
    phaseRealClock = phaseRealClock + (realDt or dt or 0)
    return profileActive
end

function Scheduler.HasPendingWork()
    return activeJobs ~= 0 or pendingCleanup
end

function Scheduler.TryIdleFrame(frame, dt, realDt, now)
    if Scheduler.HasPendingWork() then return false end
    local profileActive = beginFrame(frame, dt, realDt, now)
    if profileActive then Profiler.frameEnd(frame) end
    return true
end

function Scheduler.Update(ctx)
    local profileActive = beginFrame(ctx.frame, ctx.dt, ctx.realDt, ctx.now)
    if not Scheduler.HasPendingWork() then
        if profileActive then Profiler.frameEnd(ctx.frame) end
        return
    end
    -- Cancellations during this scan may need the next scan to compact
    -- an already-visited bucket. Keep that cleanup demand until handled.
    pendingCleanup = false
""";
        const string counts = """
-- G-CET scheduler active-work demand v1
local activeJobs = 0
local pendingCleanup = false
local function setJobActive(job, value)
    if job.active == value then return end
    job.active = value
    activeJobs = activeJobs + (value and 1 or -1)
end
local function cancelJob(job)
    if job.cancelled then return end
    setJobActive(job, false)
    job.cancelled = true
    pendingCleanup = true
end
""";
        var candidate = source;
        if (!BeforeLine(ref candidate, "local function makeHandle(job)", counts) ||
            !ReplaceUnique(ref candidate, handles, countedHandles) ||
            !AfterLine(ref candidate, "bucket[#bucket + 1] = job", "if job.active then activeJobs = activeJobs + 1 end") ||
            !AfterLine(ref candidate, "timedJobs[#timedJobs + 1] = job", "if job.active then activeJobs = activeJobs + 1 end") ||
            !ReplaceLine(ref candidate,
                "if job.generationScoped and job.generation ~= generation then job.cancelled = true; return false end",
                "if job.generationScoped and job.generation ~= generation then cancelJob(job); return false end") ||
            !ReplaceUnique(ref candidate,
                "                    job.cancelled = true\n                    job.active = false",
                "                    cancelJob(job)") ||
            !ReplaceUnique(ref candidate, updateStart, updateShared))
            return false;
        next = candidate;
        return true;
    }

    internal static bool TryIdleHost(string source, out string next)
    {
        next = source;
        const string originalContext = """
        local __gcetPlayer = State.player()
        Engine.__gcetScheduleDriver({
            frame = currentFrame, dt = delta, realDt = delta,
            now = State.time, generation = 0, playing = isPlaying,
            inMenu = State.inMenu, player = __gcetPlayer,
            state = Engine.GetState()
        })
""";
        const string idleContext = """
        if not Engine.__gcetScheduleIdle or not Engine.__gcetScheduleIdle(currentFrame, delta, State.time) then
            local __gcetPlayer = State.player()
            Engine.__gcetScheduleDriver({
                frame = currentFrame, dt = delta, realDt = delta,
                now = State.time, generation = 0, playing = isPlaying,
                inMenu = State.inMenu, player = __gcetPlayer,
                state = Engine.GetState()
            })
        end
""";
        const string driver = """
            local __lastRealClock = os.clock()
            __host.__gcetScheduleDriver = function(ctx)
""";
        const string idleDriver = """
            local __lastRealClock = os.clock()
            -- G-CET scheduler idle host v1
            __host.__gcetScheduleIdle = function(frame, dt, now)
                if type(__scheduler.HasPendingWork) ~= "function" or
                    type(__scheduler.TryIdleFrame) ~= "function" or
                    __scheduler.HasPendingWork() then return false end
                local __realClock = os.clock()
                local __realDt = __realClock - __lastRealClock
                __lastRealClock = __realClock
                __frame = __frame + 1
                -- Same error boundary and phase-clock semantics as Update.
                pcall(__scheduler.TryIdleFrame, frame, dt, __realDt, now)
                return true
            end
            __host.__gcetScheduleDriver = function(ctx)
""";
        if (source.Contains(IdleHostMarker, StringComparison.Ordinal))
            return Count(source, "__host.__gcetScheduleIdle = function(") == 1 &&
                Count(source, "Engine.__gcetScheduleIdle(currentFrame, delta, State.time)") == 1 &&
                Count(source, "pcall(__scheduler.TryIdleFrame, frame, dt, __realDt, now)") == 1 &&
                Count(source, "pcall(__scheduler.Update, ctx)") == 1;
        if (Count(source, "Engine.__gcetScheduleDriver({") != 1 ||
            Count(source, "__host.__gcetScheduleDriver = function(ctx)") != 1 ||
            Count(source, "pcall(__scheduler.Update, ctx)") != 1 ||
            Count(source, "local __lastRealClock = os.clock()") != 1 ||
            source.Contains("__gcetScheduleIdle", StringComparison.Ordinal))
            return false;
        var candidate = source;
        if (!ReplaceUniqueLines(ref candidate, originalContext, idleContext) ||
            !ReplaceUniqueLines(ref candidate, driver, idleDriver))
            return false;
        next = candidate;
        return true;
    }

    // Compose on top of the actual staged generic module, never replace a
    // live module with a historical copy. Commit a service only when all three
    // pieces (scheduler, registry and host) are proved. No demand API emitted
    // solely for an incompatible host. Existing scheduler jobs unchanged.
    internal static int TryStage(string modsRoot, ref FixedZeroEngineBuild framework,
        Dictionary<string, byte[]> staged, Dictionary<string, int> counts,
        List<object> manifest, List<object> skipped)
    {
        if (!framework.Files.TryGetValue(InitPath, out var init) ||
            !framework.Files.TryGetValue(SchedulerPath, out var scheduler) ||
            !TryScheduler(Encoding.UTF8.GetString(scheduler), out var schedulerText))
            return 0;
        var host = Encoding.UTF8.GetString(init);
        var applied = 0;
        var compatible = 0;
        foreach (var service in new[] { "Proximity", "SpatialHash" })
        {
            var relative = "0-Engine/modules/" + service + ".lua";
            var live = Path.Combine(modsRoot, "0-Engine", "modules", service + ".lua");
            if (!File.Exists(live)) continue;
            var bytes = staged.TryGetValue(relative, out var pending) ? pending : File.ReadAllBytes(live);
            var text = Encoding.UTF8.GetString(bytes);
            if (!TryProducer(text, service, out var producer) ||
                !TryHost(host, service, out var hostNext))
            {
                skipped.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                    RuleId = "0engine-demand-" + service, reason = "Current producer registry or host dispatch is not structurally proven; preserve original cadence." });
                continue;
            }
            compatible++;
            if (producer != text)
            {
                staged[relative] = Encoding.UTF8.GetBytes(producer);
                counts[relative] = counts.GetValueOrDefault(relative) + 1;
            }
            if (producer != text || hostNext != host || schedulerText != Encoding.UTF8.GetString(scheduler))
            {
                applied++;
                manifest.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                    RuleId = "0engine-demand-" + service, sourceProof = true,
                    Handler = "ZeroEngineDemandSource", PolicyClass = "CENTRAL_EXACT_FRAME_DEMAND",
                    gameplayVerification = "PENDING_COMBINED_BATCH" });
            }
            host = hostNext;
        }
        // Independent scalar producer slice. Reuse the exact same
        // scheduler instance and preserve author sampling if any side drifts.
        const string derivedRelative = "0-Engine/modules/DerivedState.lua";
        var derivedLive = Path.Combine(modsRoot, "0-Engine", "modules", "DerivedState.lua");
        if (File.Exists(derivedLive))
        {
            var bytes = staged.TryGetValue(derivedRelative, out var pending)
                ? pending : File.ReadAllBytes(derivedLive);
            var text = Encoding.UTF8.GetString(bytes);
            const string lifecycleRelative = "0-Engine/modules/Lifecycle.lua";
            var lifecyclePath = Path.Combine(modsRoot, "0-Engine", "modules", "Lifecycle.lua");
            var needsLifecycle = host.Contains("Lifecycle.Init(Events, State, Logger)", StringComparison.Ordinal);
            string? lifecycleSource = null;
            string? lifecycleNext = null;
            var lifecycleSafe = !needsLifecycle;
            if (needsLifecycle && File.Exists(lifecyclePath))
            {
                lifecycleSource = Encoding.UTF8.GetString(staged.TryGetValue(lifecycleRelative, out var pendingLifecycle)
                    ? pendingLifecycle : File.ReadAllBytes(lifecyclePath));
                lifecycleSafe = TryDerivedLifecycle(lifecycleSource, out lifecycleNext);
            }
            if (lifecycleSafe && TryDerivedProducer(text, out var producer) &&
                TryDerivedHost(host, out var hostNext) &&
                TrySampleController(schedulerText, out var sampleScheduler))
            {
                compatible++;
                var lifecycleChanged = lifecycleNext != lifecycleSource;
                if (lifecycleChanged)
                {
                    staged[lifecycleRelative] = Encoding.UTF8.GetBytes(lifecycleNext!);
                    counts[lifecycleRelative] = counts.GetValueOrDefault(lifecycleRelative) + 1;
                }
                if (producer != text)
                {
                    staged[derivedRelative] = Encoding.UTF8.GetBytes(producer);
                    counts[derivedRelative] = counts.GetValueOrDefault(derivedRelative) + 1;
                }
                if (producer != text || hostNext != host || lifecycleChanged || sampleScheduler != Encoding.UTF8.GetString(scheduler))
                {
                    applied++;
                    manifest.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                        RuleId = "0engine-demand-derived-scalar-sample", sourceProof = true,
                        Handler = "ZeroEngineDemandSource", PolicyClass = "CENTRAL_ON_DEMAND_SCALAR",
                        gameplayVerification = "PENDING_COMBINED_BATCH" });
                }
                host = hostNext;
                schedulerText = sampleScheduler;
            }
            else
            {
                skipped.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                    RuleId = "0engine-demand-derived-scalar-sample",
                    reason = "Camera scalar/proxy access, raw Lifecycle state ownership or sample controller not proven; preserve original sampling." });
            }
        }
        // Context demand belongs to the same existing scheduler. No new
        // native callback or recurring producer; old/mixed runtime fallback.
        if (host.Contains("__gcetScheduleDriver", StringComparison.Ordinal))
        {
            if (TryIdleScheduler(schedulerText, out var idleScheduler) &&
                TryIdleHost(host, out var idleHost))
            {
                compatible++;
                if (idleHost != host || idleScheduler != schedulerText)
                {
                    applied++;
                    manifest.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                        RuleId = "0engine-demand-scheduler-idle-context", sourceProof = true,
                        Handler = "ZeroEngineDemandSource", PolicyClass = "CENTRAL_ACTIVE_WORK_CONTEXT",
                        gameplayVerification = "PENDING_COMBINED_BATCH" });
                }
                host = idleHost;
                schedulerText = idleScheduler;
            }
            else
                skipped.Add(new { type = "SEMANTIC_RULE", Owner = "0-Engine",
                    RuleId = "0engine-demand-scheduler-idle-context",
                    reason = "Canonical job lifecycle or host context driver not structurally proven; preserve existing dispatch." });
        }
        if (compatible > 0)
        {
            var files = framework.Files.ToDictionary(x => x.Key, x => x.Value,
                StringComparer.OrdinalIgnoreCase);
            files[InitPath] = Encoding.UTF8.GetBytes(host);
            files[SchedulerPath] = Encoding.UTF8.GetBytes(schedulerText);
            framework = framework with { Files = files,
                FixedInitSha256 = Convert.ToHexString(SHA256.HashData(files[InitPath])).ToLowerInvariant() };
        }
        return applied;
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    private static string NormalizeLf(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);
    private static string Newlines(string source, string value) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? NormalizeLf(value).Replace("\n", "\r\n") : NormalizeLf(value);
    private static Regex Line(string value) => new(
        @"(?m)^(?<indent>[ \t]*)" + Regex.Escape(value) + @"[ \t]*(?=\r?$)");
    private static bool ReplaceLine(ref string source, string anchor, string replacement)
    {
        var matches = Line(anchor).Matches(source);
        if (matches.Count != 1) return false;
        var match = matches[0];
        var indent = match.Groups["indent"].Value;
        var body = Newlines(source, indent + NormalizeLf(replacement).Replace("\n", "\n" + indent));
        source = source.Remove(match.Index, match.Length).Insert(match.Index, body);
        return true;
    }
    private static bool ReplaceUnique(ref string source, string anchor, string replacement)
    {
        anchor = Newlines(source, anchor);
        if (Count(source, anchor) != 1) return false;
        source = source.Replace(anchor, Newlines(source, replacement), StringComparison.Ordinal);
        return true;
    }
    // Existing generated hosts can contain CRCRLF inside the author
    // adapter (historical Windows raw-string conversion). Match equivalent
    // line boundaries only in the proven block; do not normalize unrelated
    // author/custom source. The replacement uses ordinary source newlines.
    private static bool ReplaceUniqueLines(ref string source, string anchor, string replacement)
    {
        var pattern = string.Join(@"\r*\n", NormalizeLf(anchor).Split('\n').Select(Regex.Escape));
        var matches = Regex.Matches(source, pattern, RegexOptions.CultureInvariant);
        if (matches.Count != 1) return false;
        var match = matches[0];
        source = source.Remove(match.Index, match.Length)
            .Insert(match.Index, Newlines(source, replacement));
        return true;
    }
    private static bool BeforeLine(ref string source, string anchor, string before) =>
        ReplaceLine(ref source, anchor, before + "\n" + anchor);
    private static bool AfterLine(ref string source, string anchor, string after) =>
        ReplaceLine(ref source, anchor, anchor + "\n" + after);

    internal const string Controller = """
-- G-CET scheduler demand v1
do
    local demand, lanes = {}, {}

    -- A lane stores unique active intervals, not consumers. A registration
    -- change invalidates its next deadline. Between deadlines only one
    -- scalar comparison is needed; an empty lane performs no scans.
    function demand.TrackFrame(service, interval)
        local lane = lanes[service]
        if not lane then
            lane = { periods = {}, fallback = 0 }
            lanes[service] = lane
        end
        -- Unknown cadence keeps the original producer eligible every frame;
        -- original modulo/error behavior remains authoritative.
        local valid = type(interval) == "number" and interval >= 1
            and interval < math.huge and interval == math.floor(interval)
        if valid then
            lane.periods[interval] = (lane.periods[interval] or 0) + 1
        else
            lane.fallback = lane.fallback + 1
        end
        lane.nextDue, lane.lastFrame = nil, nil
        local active = true
        return { Cancel = function()
            if not active then return end
            active = false
            if valid then
                local n = lane.periods[interval] - 1
                lane.periods[interval] = n > 0 and n or nil
            else
                lane.fallback = lane.fallback - 1
            end
            lane.nextDue, lane.lastFrame = nil, nil
            if lane.fallback == 0 and next(lane.periods) == nil then
                lanes[service] = nil
            end
        end }
    end

    function demand.IsDue(service, frame)
        local lane = lanes[service]
        if not lane then return false end
        if lane.fallback > 0 then return true end
        if lane.lastFrame == frame then return lane.lastDue end
        if lane.nextDue and frame < lane.nextDue and
            (not lane.lastFrame or frame >= lane.lastFrame) then
            return false
        end
        local due, earliest = false, math.huge
        for interval in pairs(lane.periods) do
            local remainder = frame % interval
            if remainder == 0 then due = true end
            local deadline = frame + interval - remainder
            if deadline < earliest then earliest = deadline end
        end
        lane.nextDue, lane.lastFrame, lane.lastDue = earliest, frame, due
        return due
    end

    Scheduler.Demand = demand
end
""";
}
