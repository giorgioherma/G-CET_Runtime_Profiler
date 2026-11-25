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
    private static string Newlines(string source, string value) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? value.Replace("\n", "\r\n") : value;
    private static Regex Line(string value) => new(
        @"(?m)^(?<indent>[ \t]*)" + Regex.Escape(value) + @"[ \t]*(?=\r?$)");
    private static bool ReplaceLine(ref string source, string anchor, string replacement)
    {
        var matches = Line(anchor).Matches(source);
        if (matches.Count != 1) return false;
        var match = matches[0];
        var indent = match.Groups["indent"].Value;
        var body = Newlines(source, indent + replacement.Replace("\n", "\n" + indent));
        source = source.Remove(match.Index, match.Length).Insert(match.Index, body);
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
