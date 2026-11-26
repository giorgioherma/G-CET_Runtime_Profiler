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
    internal const string EveryFrameFastPath = "        if lane.periods[1] and frame % 1 == 0 then return true end";

    internal static bool TryScheduler(string source, out string next)
    {
        next = source;
        if (source.Contains(Marker, StringComparison.Ordinal))
        {
            if (!source.Contains("Scheduler.Demand = demand", StringComparison.Ordinal) ||
                !source.Contains("function demand.TrackFrame(", StringComparison.Ordinal) ||
                !source.Contains("function demand.IsDue(", StringComparison.Ordinal))
                return false;
            // Upgrade only a complete known prior controller. Preserve
            // other extensions and customized/foreign implementations.
            if (Count(source, "function demand.IsDue(") == 1 &&
                Count(source, "function demand.TrackFrame(") == 1)
            {
                // C# multiline literals inherit the checkout's line endings.
                // Normalize before finding boundaries (Windows CRLF included).
                var canonical = NormalizeLf(Controller);
                var start = canonical.IndexOf("    function demand.IsDue(", StringComparison.Ordinal);
                var trackStart = canonical.IndexOf("    function demand.TrackFrame(", StringComparison.Ordinal);
                if (start < 0 || trackStart < 0 || trackStart >= start) return true;
                var end = canonical.IndexOf("\n\n    Scheduler.Demand = demand", start, StringComparison.Ordinal);
                if (end <= start) return true;
                var current = canonical[start..end];
                var previous = current.Replace("\n"+EveryFrameFastPath, "", StringComparison.Ordinal);
                var track = canonical[trackStart..start].TrimEnd();
                if (Count(NormalizeLf(source), track) == 1)
                    ReplaceUniqueLines(ref next, previous, current);
            }
            return true;
        }
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


    // The grouped pose read is a dependency of every downstream UpdateFrame
    // operation. Until those dependencies are proven dormant, keep the exact
    // original position/orientation/Euler reads and failure return. Reuse the
    // protected function itself: its only captured variable is the current
    // receiver, now an explicit argument. No retained entity or pose cache.
    private const string PoseReaderName = "__gcetReadPlayerPose";
    private const string PoseReaderMarker = "-- G-CET shared pose reader v1";
    private static readonly Regex PoseBody = new(
        @"(?m)^(?<indent>[ \t]*)local (?<ok>[A-Za-z_]\w*),[ \t]*(?<position>[A-Za-z_]\w*),[ \t]*(?<orientation>[A-Za-z_]\w*),[ \t]*(?<yaw>[A-Za-z_]\w*)[ \t]*=[ \t]*pcall\(function\(\)[ \t]*\r*\n[ \t]*local (?<p>[A-Za-z_]\w*)[ \t]*=[ \t]*(?<player>[A-Za-z_]\w*):GetWorldPosition\(\)[ \t]*\r*\n[ \t]*local (?<o>[A-Za-z_]\w*)[ \t]*=[ \t]*\k<player>:GetWorldOrientation\(\)[ \t]*\r*\n[ \t]*return \k<p>,[ \t]*\k<o>,[ \t]*\k<o>:ToEulerAngles\(\)\.yaw[ \t]*\r*\n\k<indent>end\)[ \t]*(?=\r*$)",
        RegexOptions.CultureInvariant);
    internal static bool TryPoseReader(string source, out string next)
    {
        next = source;
        var frame = Regex.Matches(source, @"(?m)^local function UpdateFrame\([A-Za-z_]\w*\)[ \t]*(?=\r*$)");
        if (frame.Count != 1) return false;
        var frameEnd = Regex.Match(source[frame[0].Index..], @"(?m)^end[ \t]*(?=\r*$)");
        if (!frameEnd.Success) return false;
        var body = source.Substring(frame[0].Index, frameEnd.Index+frameEnd.Length);
        // Recognize an existing equivalent local helper/explicit argument.
        // Neither a second helper nor a corrective pass is needed.
        if (source.Contains(PoseReaderName, StringComparison.Ordinal) ||
            source.Contains(PoseReaderMarker, StringComparison.Ordinal))
        {
            var helper = Regex.Matches(source, @"(?m)^local function "+PoseReaderName+
                @"\((?<r>[A-Za-z_]\w*)\)[ \t]*\r*\n[ \t]*local (?<p>[A-Za-z_]\w*) = \k<r>:GetWorldPosition\(\)\r*\n[ \t]*local (?<o>[A-Za-z_]\w*) = \k<r>:GetWorldOrientation\(\)\r*\n[ \t]*return \k<p>, \k<o>, \k<o>:ToEulerAngles\(\)\.yaw\r*\nend(?=\r*$)");
            return helper.Count==1 && helper[0].Index<frame[0].Index &&
                Regex.Matches(body,@"\bpcall\("+PoseReaderName+@",[ \t]*[A-Za-z_]\w*\)").Count==1 &&
                Count(source,PoseReaderName)==2 && PoseBody.Matches(body).Count==0;
        }
        var matches = PoseBody.Matches(body);
        if(matches.Count!=1)return false;
        var m=matches[0];
        string V(string name)=>m.Groups[name].Value;
        // Only the original receiver and the two local results occur in
        // the closure. Refuse name collisions (which would change binding).
        if(new[]{V("player"),V("p"),V("o")}.Distinct(StringComparer.Ordinal).Count()!=3)return false;
        var tail=body[(m.Index+m.Length)..];
        var expected=new[]{"if not "+V("ok")+" then return end",
            "State.pos = "+V("position"),"State.orientation = "+V("orientation"),
            "State.yaw = "+V("yaw")};
        var tailCode=WithoutLuaTrivia(tail);
        var ordered=string.Join(@"\s+",expected.Select(Regex.Escape));
        if(!Regex.IsMatch(tailCode,@"\A\s*"+ordered,RegexOptions.CultureInvariant))return false;
        var receiver=V("player");
        var replacement=V("indent")+"local "+V("ok")+", "+V("position")+", "+V("orientation")+", "+V("yaw")+
            " = pcall("+PoseReaderName+", "+receiver+")";
        var helperSource=PoseReaderMarker+"\nlocal function "+PoseReaderName+"("+receiver+")\n"+
            "    local "+V("p")+" = "+receiver+":GetWorldPosition()\n"+
            "    local "+V("o")+" = "+receiver+":GetWorldOrientation()\n"+
            "    return "+V("p")+", "+V("o")+", "+V("o")+":ToEulerAngles().yaw\nend\n\n";
        next=source.Remove(frame[0].Index+m.Index,m.Length).Insert(frame[0].Index+m.Index,replacement)
            .Insert(frame[0].Index,Newlines(source,helperSource));
        return true;
    }

    // Optional ordered native backend already implemented by the isolated
    // RED4ext producer service. The current production bundle lacks this
    // capability. No new hook, cache, observer, clock or retained player.
    // CET v1.37.1 RTTIHelper::ExecuteFunction returns the native return value
    // first, followed by out parameters in declaration order.
    private const string TypedPoseState =
        "local __gcetPoseNative, __gcetPoseNativeResolved -- G-CET typed pose backend v1";
    private static string TypedPoseBody(string receiver,bool sharedSpatial=false) => """
    if not __gcetPoseNativeResolved then
        __gcetPoseNativeResolved = true
        local ok, native = pcall(function() return Game.GCETReadPlayerPoseV1 end)
        if ok and type(native) == "function" then __gcetPoseNative = native end
    end
    if __gcetPoseNative then
        local status, position, orientation, yaw = __gcetPoseNative(RECEIVER)
        if status == 1 then
            if position == nil or orientation == nil or type(yaw) ~= "number" then
                error("G-CET typed pose result invalid", 0)
            end
            return position, orientation, yaw
        end
        if status ~= 0 then error("G-CET typed pose read failed", 0) end
        -- Unsupported is returned only before any getter executes.
        -- Failed, an exception, or an unknown result must NEVER replay.
    end
""".Replace("RECEIVER",receiver,StringComparison.Ordinal)
       .Replace("GCETReadPlayerPoseV1",sharedSpatial?"GCETReadSharedPlayerPoseV1":"GCETReadPlayerPoseV1",StringComparison.Ordinal);
    internal static bool TryComposePoseReader(string source,bool nativeAvailable,out string next,bool sharedSpatial=false)
    {
        next=source;
        var normalized=NormalizeLf(source);
        var declarations=Regex.Matches(normalized,@"(?m)^local function "+PoseReaderName+@"\((?<receiver>[A-Za-z_]\w*)\)[ \t]*$");
        var candidate=source;
        if(declarations.Count==1)
        {
            var receiver=declarations[0].Groups["receiver"].Value;
            var bodies=new[]{TypedPoseBody(receiver),TypedPoseBody(receiver,true)};
            var state=Newlines(source,TypedPoseState+"\n");
            // One existing owner: upgrade/downgrade either known ordered
            // backend, including missing declaration or missing body.
            if(Count(candidate,state)>1||bodies.Sum(body=>Count(candidate,Newlines(source,body)))>1)return false;
            if(Count(candidate,state)==1)candidate=candidate.Replace(state,"",StringComparison.Ordinal);
            foreach(var body in bodies)
            {
                if(Count(candidate,Newlines(source,body))==1&&
                   !ReplaceUnique(ref candidate,declarations[0].Value+"\n"+body+"\n",
                    declarations[0].Value+"\n"))return false;
            }
        }
        // A collision or substantively altered partial backend is unsafe.
        if(Regex.IsMatch(WithoutLuaTrivia(candidate),@"\b__gcetPoseNative(?:Resolved)?\b"))
            return false;
        if(!TryPoseReader(candidate,out var plain))return false;
        if(!nativeAvailable){next=plain;return true;}
        // Only the local explicit receiver and original protected phase are
        // supported. The grouped host publication/failure barrier remains.
        if(Regex.IsMatch(WithoutLuaTrivia(plain),@"\b(?:local\s+Game\b|Game\s*=)"))
            return false;
        var helper=Regex.Matches(plain,@"(?m)^local function "+PoseReaderName+@"\((?<receiver>[A-Za-z_]\w*)\)[ \t]*(?=\r?$)");
        if(helper.Count!=1)return false;
        var name=helper[0].Groups["receiver"].Value;
        var header=helper[0].Value;
        if(!ReplaceUnique(ref plain,header+"\n",TypedPoseState+"\n"+header+"\n"+TypedPoseBody(name,sharedSpatial)+"\n"))
            return false;
        next=plain;return true;
    }

    private const string ExactEulerHelperName="__gcetReadExactPositionEuler";
    private const string ExactEulerState="local __gcetExactEulerNative, __gcetExactEulerResolved";
    private const string ExactEulerHelper="""
-- G-CET exact position/Euler ordered producer v1
local function __gcetReadExactPositionEuler(receiver, yawOnly)
    if not __gcetExactEulerResolved then
        __gcetExactEulerResolved = true
        local ok, fn = pcall(function() return Game.GCETReadPlayerPositionEulerV1 end)
        if ok and type(fn) == "function" then __gcetExactEulerNative = fn end
    end
    local position, angles
    if __gcetExactEulerNative then
        local status, p, e = __gcetExactEulerNative(receiver)
        if status == 1 then
            if p == nil or e == nil then error("G-CET exact Euler result invalid", 0) end
            position, angles = p, e
        elseif status ~= 0 then
            error("G-CET exact Euler read failed", 0)
        end
        -- Unsupported is guaranteed to precede the first engine read.
    end
    if position == nil then
        position = receiver:GetWorldPosition()
        angles = receiver:GetWorldOrientation():ToEulerAngles()
    end
    if yawOnly then return position, angles.yaw end
    return position, angles
end
""";
    private static readonly Regex EulerStockPair=new(
        @"(?m)^(?<indent>[ \t]*)local[ \t]+(?<p>[A-Za-z_]\w*)[ \t]*=[ \t]*(?<r>[A-Za-z_]\w*)\s*:\s*GetWorldPosition\(\)[ \t]*\r?\n\k<indent>local[ \t]+(?<e>[A-Za-z_]\w*)[ \t]*=[ \t]*\k<r>\s*:\s*GetWorldOrientation\(\)\s*:\s*ToEulerAngles\(\)(?<yaw>\s*\.yaw)?[ \t]*(?=\r?$)",
        RegexOptions.CultureInvariant);
    private static readonly Regex EulerGeneratedPair=new(
        @"(?m)^(?<indent>[ \t]*)local[ \t]+(?<p>[A-Za-z_]\w*),[ \t]*(?<e>[A-Za-z_]\w*)[ \t]*=[ \t]*"+ExactEulerHelperName+@"\((?<r>[A-Za-z_]\w*),[ \t]*(?<yaw>true|false)\)[ \t]*(?=\r?$)",
        RegexOptions.CultureInvariant);
    // The local binding must still exist at the second read and cannot
    // have been captured by any intervening closure. Balanced intervening
    // blocks are allowed, but never movement of either getter past them.
    private static bool StableLocalPlayer(string code,int at,string name,bool sharedGetter,bool coordinatorGetter)
    {
        var id=Regex.Escape(name);
        var origin=Regex.Matches(code[..at],@"(?m)^[ \t]*local[ \t]+"+id+
            @"[ \t]*=[ \t]*(?:Game\s*\.\s*GetPlayer"+(sharedGetter?"|__gcetGetPlayer":"")+
            (coordinatorGetter?@"|__gcetNativeReads\s*\.\s*GetPlayer":"")+
            @")\s*\(\s*\)[ \t]*(?=\r?$)");
        if(origin.Count==0)return false;
        var last=origin[^1];
        var between=code[(last.Index+last.Length)..at];
        if(Regex.IsMatch(between,@"\b(?:function|goto)\b|::|(?<![\w.])"+id+
            @"\s*(?:,\s*[A-Za-z_]\w*)*\s*=(?!=)|\blocal\s+"+id+@"\b"))return false;
        var stack=new Stack<string>();
        bool awaitingDo=false;
        foreach(Match token in Regex.Matches(between,@"\b(?:if|for|while|repeat|do|until|end|else|elseif)\b"))
        {
            switch(token.Value)
            {
                case "if":stack.Push("if");break;
                case "for":case "while":
                    if(awaitingDo)return false;
                    stack.Push("loop");awaitingDo=true;break;
                case "do":
                    if(awaitingDo)awaitingDo=false;else stack.Push("do");
                    break;
                case "repeat":stack.Push("repeat");break;
                case "until":if(stack.Count==0||stack.Pop()!="repeat")return false;break;
                case "end":if(stack.Count==0||stack.Pop()=="repeat"||awaitingDo)return false;break;
                case "else":case "elseif":if(stack.Count==0||stack.Peek()!="if")return false;break;
            }
        }
        return stack.Count==0&&!awaitingDo;
    }
    // Preserve the position of an existing helper relative to other
    // independently generated provider headers. Known partial fragments
    // are composed in this same generation, never a corrective pass.
    private static bool RemoveKnownHeader(ref string source,string state,string helper,
        string slot,out bool present)
    {
        present=false;
        if(source.Contains(slot,StringComparison.Ordinal))return false;
        var parts=new[]{Newlines(source,state+"\n"),Newlines(source,helper+"\n\n")};
        var found=new List<(int At,string Value)>();
        foreach(var part in parts)
        {
            var count=Count(source,part);
            if(count>1)return false;
            if(count==1)found.Add((source.IndexOf(part,StringComparison.Ordinal),part));
        }
        if(found.Count==0)return true;
        present=true;
        var first=found.Min(x=>x.At);
        foreach(var part in found.OrderByDescending(x=>x.At))
            source=source.Remove(part.At,part.Value.Length);
        source=source.Insert(first,slot);
        return true;
    }
    private static bool IsCodeSpan(string code,Match m,Regex shape)=>
        shape.IsMatch(code.Substring(m.Index,m.Length));
    internal static bool TryComposeExactPosePairs(string source,bool enabled,out string next,out int sites,
        string? canonicalPlayerProvider=null,bool coordinatorGetter=false)
    {
        next=source;sites=0;
        const string slot="-- G-CET exact Euler insertion slot\n";
        if(!enabled&&!source.Contains(ExactEulerHelperName,StringComparison.Ordinal)&&
            !source.Contains(ExactEulerState,StringComparison.Ordinal))return false;
        var candidate=source;
        if(!RemoveKnownHeader(ref candidate,ExactEulerState,ExactEulerHelper,slot,out var hasHeader))
            return false;
        var code=MaskLuaTrivia(candidate);
        // Normalize only the exact known generated pair shape; retain any
        // unrecognized custom helper or collision untouched.
        var generated=EulerGeneratedPair.Matches(candidate).Cast<Match>().Where(m=>
            IsCodeSpan(code,m,EulerGeneratedPair)).ToArray();
        var b=new StringBuilder(candidate);
        foreach(var m in generated.Reverse())
        {
            string G(string k)=>m.Groups[k].Value;
            b.Remove(m.Index,m.Length).Insert(m.Index,G("indent")+"local "+G("p")+" = "+G("r")+
                ":GetWorldPosition()"+Newlines(source,"\n")+G("indent")+"local "+G("e")+" = "+G("r")+
                ":GetWorldOrientation():ToEulerAngles()"+(G("yaw")=="true"?".yaw":""));
        }
        candidate=b.ToString();
        code=MaskLuaTrivia(candidate);
        if(Regex.IsMatch(code,@"\b(?:__gcetReadExactPositionEuler|__gcetExactEulerNative|__gcetExactEulerResolved)\b"))
            return false;
        if(enabled)
        {
            // Local Game bindings or replacement of the facade are unknown.
            if(Regex.IsMatch(code,@"\blocal\s+Game\b|\bGame\s*(?:\.\s*GetPlayer\s*)?=(?!=)|\bfunction[^\(\r\n]*\([^)]*\bGame\b[^)]*\)"))
                return false;
            // The canonical provider may have been inserted by a later generic
            // stage with different blank separators or indentation.
            var knownGetter=canonicalPlayerProvider is null?null:NormalizeLf(canonicalPlayerProvider).TrimEnd();
            var inline=Regex.Matches(NormalizeLf(candidate),
                @"(?m)^-- G-CET shared provider: PLAYER[ \t]*\n[\s\S]*?^end[ \t]*(?=\n|$)");
            static string[] Lines(string value)=>NormalizeLf(value).Split('\n').Select(l=>l.Trim())
                .Where(l=>l.Length!=0).ToArray();
            var sharedGetter=knownGetter is not null&&knownGetter.Length>0&&inline.Count==1&&
                Lines(inline[0].Value).SequenceEqual(Lines(knownGetter),StringComparer.Ordinal)&&
                Regex.Matches(code,@"\blocal\s+function\s+__gcetGetPlayer\s*\(").Count==1&&
                !Regex.IsMatch(code,@"\b__gcetGetPlayer\s*=(?!=)|\bfunction[^\(\r\n]*\([^)]*\b__gcetGetPlayer\b[^)]*\)");
            // The source match itself must be code, not quoted or commented
            // text. Match original text too so no author comment is deleted.
            var pairs=EulerStockPair.Matches(candidate).Cast<Match>().Where(m=>
                IsCodeSpan(code,m,EulerStockPair)&&
                new[]{"p","e","r"}.Select(k=>m.Groups[k].Value).Distinct(StringComparer.Ordinal).Count()==3&&
                StableLocalPlayer(code,m.Index,m.Groups["r"].Value,sharedGetter,coordinatorGetter)).ToArray();
            b=new StringBuilder(candidate);
            foreach(var m in pairs.Reverse())
            {
                string G(string k)=>m.Groups[k].Value;
                b.Remove(m.Index,m.Length).Insert(m.Index,G("indent")+"local "+G("p")+", "+G("e")+" = "+
                    ExactEulerHelperName+"("+G("r")+", "+(m.Groups["yaw"].Success?"true":"false")+")");
            }
            candidate=b.ToString();sites=pairs.Length;
        }
        var header=sites>0?Newlines(source,ExactEulerState+"\n"+ExactEulerHelper+"\n\n"):"";
        if(hasHeader)candidate=candidate.Replace(slot,header,StringComparison.Ordinal);
        else if(header.Length>0&&coordinatorGetter)
        {
            var bridge=Regex.Match(candidate,@"(?m)^local __gcetNativeReads = require\([^\r\n]*\)[ \t]*\r?\n");
            if(!bridge.Success)return false;
            candidate=candidate.Insert(bridge.Index+bridge.Length,header);
        }
        else candidate=header+candidate;
        next=candidate;
        if(next==source){sites=0;return false;}
        return true;
    }

    private const string PsmBatchName="__gcetReadPsmBatch";
    private const string PsmBatchState="local __gcetPsmBatchInt, __gcetPsmBatchBool, __gcetPsmBatchResolved";
    private const string PsmBatchHelper="""
-- G-CET ordered PSM reads v1
local function __gcetReadPsmBatch(board, k1, k2, k3, k4, lastBool)
    if not __gcetPsmBatchResolved then
        __gcetPsmBatchResolved = true
        local ok, fn = pcall(function() return Game.GCETReadBlackboard4IntV1 end)
        if ok and type(fn) == "function" then __gcetPsmBatchInt = fn end
        ok, fn = pcall(function() return Game.GCETReadBlackboard3IntBoolV1 end)
        if ok and type(fn) == "function" then __gcetPsmBatchBool = fn end
    end
    local fn
    if lastBool then fn = __gcetPsmBatchBool else fn = __gcetPsmBatchInt end
    if fn then
        local status, a, b, c, d = fn(board, k1, k2, k3, k4)
        if status == 1 then
            if type(a) ~= "number" or type(b) ~= "number" or type(c) ~= "number" or
                (lastBool and type(d) ~= "boolean") or
                (not lastBool and type(d) ~= "number") then
                error("G-CET typed PSM result invalid", 0)
            end
            return a, b, c, d
        end
        if status ~= 0 then error("G-CET typed PSM read failed", 0) end
    end
    local a = board:GetInt(k1)
    local b = board:GetInt(k2)
    local c = board:GetInt(k3)
    if lastBool then return a, b, c, board:GetBool(k4) end
    return a, b, c, board:GetInt(k4)
end
""";
    private const string PsmFieldRead=@"(?<name>[A-Za-z_]\w*)[ \t]*=[ \t]*playerBB:(?<getter>GetInt|GetBool)\(\s*__gcetPsmFields\.(?<field>[A-Za-z_]\w*)\s*\)";
    private static Regex PsmStockGroup()=>new(@"(?m)^(?<indent>[ \t]*)local[ \t]+"+PsmFieldRead+
        @"[ \t]*\r?\n\k<indent>local[ \t]+"+PsmFieldRead+
        @"[ \t]*\r?\n\k<indent>local[ \t]+"+PsmFieldRead+
        @"[ \t]*\r?\n\k<indent>local[ \t]+"+PsmFieldRead+@"[ \t]*(?=\r?$)");
    private static readonly Regex PsmGeneratedGroup=new(
        @"(?m)^(?<indent>[ \t]*)local[ \t]+(?<name>[A-Za-z_]\w*),[ \t]*(?<name>[A-Za-z_]\w*),[ \t]*(?<name>[A-Za-z_]\w*),[ \t]*(?<name>[A-Za-z_]\w*)[ \t]*=[ \t]*"+
        PsmBatchName+@"\(playerBB, __gcetPsmFields\.(?<field>[A-Za-z_]\w*), __gcetPsmFields\.(?<field>[A-Za-z_]\w*), __gcetPsmFields\.(?<field>[A-Za-z_]\w*), __gcetPsmFields\.(?<field>[A-Za-z_]\w*), (?<bool>true|false)\)[ \t]*(?=\r?$)");
    // Called by the existing PSM owner before its complete immutable-key
    // ownership proof, then after that proof to compose the native variant.
    // No mutable value is cached and no read/event is moved across a phase.
    internal static bool TryNormalizePsmBatches(string source,out string plain)
    {
        plain=source;
        const string slot="-- G-CET PSM insertion slot\n";
        var candidate=source;
        if(!RemoveKnownHeader(ref candidate,PsmBatchState,PsmBatchHelper,slot,out var hadHeader))
            return false;
        var code=MaskLuaTrivia(candidate);
        var groups=PsmGeneratedGroup.Matches(candidate).Cast<Match>().Where(m=>
            IsCodeSpan(code,m,PsmGeneratedGroup)).ToArray();
        var b=new StringBuilder(candidate);
        foreach(var m in groups.Reverse())
        {
            var names=m.Groups["name"].Captures;var fields=m.Groups["field"].Captures;
            var indent=m.Groups["indent"].Value;
            var lastBool=m.Groups["bool"].Value=="true";
            var lines=Enumerable.Range(0,4).Select(i=>indent+"local "+names[i].Value+
                " = playerBB:"+(i==3&&lastBool?"GetBool":"GetInt")+"(__gcetPsmFields."+fields[i].Value+")");
            b.Remove(m.Index,m.Length).Insert(m.Index,string.Join(Newlines(source,"\n"),lines));
        }
        candidate=b.ToString();
        if(Regex.IsMatch(MaskLuaTrivia(candidate),@"\b(?:__gcetReadPsmBatch|__gcetPsmBatchInt|__gcetPsmBatchBool|__gcetPsmBatchResolved)\b"))
            return false;
        plain=hadHeader?candidate.Replace(slot,"",StringComparison.Ordinal):candidate;
        return true;
    }
    // Construct the exact historical experimental form for migration and
    // native-ABI contract fixtures only. Never called by the live generator:
    // the current owner has no stable board/key contract across getters.
    internal static bool TryComposeLegacyPsmBatchFixture(string plain,out string next,out int batches)
    {
        next=plain;batches=0;
        var code=MaskLuaTrivia(plain);
        if(Regex.IsMatch(code,@"\b(?:__gcetReadPsmBatch|__gcetPsmBatchInt|__gcetPsmBatchBool|__gcetPsmBatchResolved|local\s+Game)\b|\bGame\s*=(?!=)"))return false;
        var start=Regex.Matches(code,@"(?m)^function[ \t]+BlackboardCache\.Update\s*\(\s*player\s*\)[ \t]*(?=\r?$)");
        if(start.Count!=1)return false;
        var end=Regex.Match(code[start[0].Index..],@"(?m)^end[ \t]*(?=\r?$)");
        if(!end.Success)return false;
        var shape=PsmStockGroup();
        var matches=shape.Matches(plain).Cast<Match>().Where(m=>
            m.Index>start[0].Index&&m.Index+m.Length<start[0].Index+end.Index&&
            IsCodeSpan(code,m,shape)&&m.Groups["getter"].Captures.Take(3).All(g=>g.Value=="GetInt")&&
            m.Groups["name"].Captures.Select(g=>g.Value).Distinct().Count()==4&&
            m.Groups["field"].Captures.Select(g=>g.Value).Distinct().Count()==4).ToArray();
        if(matches.Length==0)return false;
        var b=new StringBuilder(plain);
        foreach(var m in matches.Reverse())
        {
            var names=m.Groups["name"].Captures.Select(g=>g.Value);
            var fields=m.Groups["field"].Captures.Select(g=>"__gcetPsmFields."+g.Value);
            var lastBool=m.Groups["getter"].Captures[3].Value=="GetBool";
            b.Remove(m.Index,m.Length).Insert(m.Index,m.Groups["indent"].Value+"local "+string.Join(", ",names)+
                " = "+PsmBatchName+"(playerBB, "+string.Join(", ",fields)+", "+(lastBool?"true":"false")+")");
        }
        b.Insert(start[0].Index,Newlines(plain,PsmBatchState+"\n"+PsmBatchHelper+"\n\n"));
        next=b.ToString();batches=matches.Length;return true;
    }

    internal const string SpatialHostPath="0-Engine/modules/GCETNativeLocationHost.lua";
    private const string SpatialArmHelper="""
-- G-CET explicit coarse spatial freshness v1
local function __gcetArmSharedSpatial(id)
  local ok,armed=pcall(native,"GCETStaticArmCoarseSpatialV1",id,true)
  if ok and armed==true then return true end
  return native("GCETStaticArm",id,true)
end
""";
    // One generic registration owner. Only arm calls inside the known
    // attach/attachCompiled lifecycle are admitted; all original disarms,
    // polling, transition callbacks, UNKNOWN and exact fallback survive.
    internal static bool TrySharedSpatialHost(string source,bool enabled,out string next)
    {
        next=source;
        var helper=Newlines(source,SpatialArmHelper+"\n\n");
        var candidate=source;
        var helperCount=Count(candidate,helper);
        if(helperCount>1)return false;
        if(helperCount==1)candidate=candidate.Replace(helper,"",StringComparison.Ordinal);
        const string oldCall="native(\"GCETStaticArm\",key,true)";
        const string newCall="__gcetArmSharedSpatial(key)";
        var armLine=new Regex(@"(?m)^(?<indent>[ \t]*)assert\((?<call>native\(\s*""GCETStaticArm""\s*,\s*key\s*,\s*true\s*\)|__gcetArmSharedSpatial\(\s*key\s*\))\s*==\s*true\)[ \t]*(?=\r?$)",RegexOptions.CultureInvariant);
        var code=MaskLuaTrivia(candidate);
        var matches=armLine.Matches(candidate).Cast<Match>().Where(m=>
            code.AsSpan(m.Index+m.Groups["indent"].Length,6).SequenceEqual("assert".AsSpan())).ToArray();
        // The two registrations must be real code in their respective
        // lifecycle functions, not a comment/string or assumed file order.
        // Admission also requires the actual existing 100 ms asynchronous
        // host delivery contract; faster/custom synchronous hosts remain exact.
        var attach=Regex.Matches(code,@"(?ms)^function Host:attach\([^\r\n]*\)[ \t]*\r?\n.*?^end[ \t]*(?=\r?$)");
        var compiled=Regex.Matches(code,@"(?ms)^function Host:attachCompiled\([^\r\n]*\)[ \t]*\r?\n.*?^end[ \t]*(?=\r?$)");
        bool Owns(Match scope,Match arm)=>arm.Index>scope.Index&&arm.Index+arm.Length<scope.Index+scope.Length;
        if(matches.Length!=2||attach.Count!=1||compiled.Count!=1||
           matches.Count(m=>Owns(attach[0],m))!=1||matches.Count(m=>Owns(compiled[0],m))!=1||
           Count(code,"local function native(name,...)")!=1||
           Count(code,"function Host.new(engine)")!=1||
           Count(code,"function Host:shutdown()")!=1||
           Count(candidate,"GCETStaticArmCoarseSpatialV1")!=0)return false;
        if(enabled&&
           (Regex.Matches(code,@"\bclient\s*\.\s*Schedule\s*\.\s*Every\s*\(\s*0?\.10*\s*,\s*\{").Count!=1||
            Regex.Matches(code,@"function\(\)\s*self:step\(\)\s*end").Count!=1||
            Count(code,"function Host:_tick()")!=1||
            Count(code,"function Host:step()")!=1))return false;
        var stripped=candidate;
        foreach(var m in matches.Reverse())
            stripped=stripped.Remove(m.Index,m.Length);
        if(stripped.Contains("__gcetArmSharedSpatial",StringComparison.Ordinal)||
           stripped.Contains("G-CET explicit coarse spatial freshness v1",StringComparison.Ordinal))return false;
        foreach(var m in matches.Reverse())
        {
            var g=m.Groups["call"];
            // Preserve harmless author whitespace in exact original arms.
            if(!enabled&&g.Value.StartsWith("native",StringComparison.Ordinal))continue;
            if(enabled&&g.Value==newCall)continue;
            candidate=candidate.Remove(g.Index,g.Length).Insert(g.Index,enabled?newCall:oldCall);
        }
        if(enabled)
        {
            var anchor="function Host.new(engine)";
            if(!ReplaceUnique(ref candidate,anchor,SpatialArmHelper+"\n\n"+anchor))return false;
        }
        next=candidate;return true;
    }

    internal static int TryStagePose(ref FixedZeroEngineBuild framework,List<object> manifest,
        bool nativeAvailable=false,bool sharedSpatialAvailable=false)
    {
        if(!framework.Files.TryGetValue(InitPath,out var bytes))return 0;
        var source=Encoding.UTF8.GetString(bytes);
        var host=framework.Files.TryGetValue(SpatialHostPath,out var hostBytes)?Encoding.UTF8.GetString(hostBytes):null;
        string? hostNext=null;
        var share=nativeAvailable&&sharedSpatialAvailable&&host is not null&&
            TrySharedSpatialHost(host,true,out hostNext);
        if(!TryComposePoseReader(source,nativeAvailable,out var next,share))return 0;
        if(!share&&host is not null&&TrySharedSpatialHost(host,false,out var exactHost))
            hostNext=exactHost;
        if(next==source&&(hostNext is null||hostNext==host))return 0;
        var files=framework.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        files[InitPath]=Encoding.UTF8.GetBytes(next);
        if(hostNext is not null&&hostNext!=host)files[SpatialHostPath]=Encoding.UTF8.GetBytes(hostNext);
        framework=framework with{Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[InitPath])).ToLowerInvariant()};
        manifest.Add(new{type="FRAMEWORK_POSE_READER",Owner="0-Engine",RuleId="0engine-shared-pose-reader",
            file=InitPath,contract="GCET_FRAMEWORK_POSE_READER_V1",
            Handler="ZeroEngineDemandSource",PolicyClass="SAME_PHASE_REUSE_PROTECTED_FUNCTION",
            sourceProof=true,nativeReadCountChanged=false,callbackOrCadenceChanged=false,
            backend=share?"EXPERIMENTAL_EXACT_POSE_COARSE_SPATIAL_SHARED_SAMPLE":
                nativeAvailable?"OPTIONAL_ORDERED_TYPED_POSE_V1":"ORIGINAL_CET_READS",
            sharedSpatialAdmitted=share,conditionalSpatialAcquisitionReuse=share,
            exactGameplayFreshnessUnchanged=true,
            runtimePerformance="NOT_MEASURED"});
        return 1;
    }

    private const string FrameDemandMarker = "-- G-CET frame emitter demand v1";
    private const string FrameDemandBind = "local __gcetDemand = require('modules/G-CET/Scheduler').Demand";
    private const string FrameDemandState = """
-- G-CET frame emitter demand v1
local __gcetFrameDemandHandles = {}
local __gcetFrameDemandReady = type(__gcetDemand) == "table" and
    type(__gcetDemand.TrackFrame) == "function" and type(__gcetDemand.IsDue) == "function"
""";
    private const string FrameEmitterLoop = """
    for interval, emitter in pairs(frameEmitters) do
        if currentFrame % interval == 0 then
            emitter:trigger(currentFrame)
        end
    end
""";
    private const string FrameEmitterCreate = """
    if not frameEmitters[interval] then
        frameEmitters[interval] = EventEmitter.new()
    end
""";
    private const string FrameEmitterRemove = """
            if frameEmitters[interval] and frameEmitters[interval]:getListenerCount() == 0 then
                frameEmitters[interval] = nil
            end
""";
    private const string FrameEmitterCreateTracked = """
    if not frameEmitters[interval] then
        frameEmitters[interval] = EventEmitter.new()
        if __gcetFrameDemandReady then
            __gcetFrameDemandHandles[interval] = __gcetDemand.TrackFrame("FrameEmitters", interval)
        end
    end
""";
    private const string FrameEmitterRemoveTracked = """
            if frameEmitters[interval] and frameEmitters[interval]:getListenerCount() == 0 then
                frameEmitters[interval] = nil
                local demandHandle = __gcetFrameDemandHandles[interval]
                if demandHandle then
                    demandHandle.Cancel()
                    __gcetFrameDemandHandles[interval] = nil
                end
            end
""";
    private const string FrameEmitterLoopTracked = """
    if not __gcetFrameDemandReady or __gcetDemand.IsDue("FrameEmitters", currentFrame) then
        for interval, emitter in pairs(frameEmitters) do
            if currentFrame % interval == 0 then
                emitter:trigger(currentFrame)
            end
        end
    end
""";

    // Frame consumers are a dependency of the host's successful pose phase.
    // Connect the actual interval buckets to the same exact-deadline controller.
    // Keep the author's pairs traversal, modulo, emitter mutation and listener
    // delivery semantics. Unknown/disabled legacy subscriptions remain tracked.
    internal static bool TryFrameEmitters(string source, out string next)
    {
        next = source;
        var candidate = source;
        var code = WithoutLuaTrivia(source);
        if (Count(code,"local frameEmitters = {}") != 1 ||
            Count(code,"function Engine.OnFrame(interval, fn, source)") != 1 ||
            Count(code,"local innerHandle = frameEmitters[interval]:subscribe(fn, source)") != 1)
            return false;
        // Prove every registry use, allowing the existing read-only overlay
        // traversal and unrelated custom code. Do not require its presence.
        var registry = Regex.Replace(code,
            @"\blocal\s+frameEmitters\s*=\s*\{\s*\}|\bpairs\s*\(\s*frameEmitters\s*\)|\bframeEmitters\s*\[\s*interval\s*\]", "");
        if (Regex.IsMatch(registry,@"\bframeEmitters\b")) return false;
        var refs = Regex.Matches(code,@"\bframeEmitters\s*\[\s*interval\s*\]").Count;
        if (refs != 6) return false;
        var traversals=Regex.Matches(code,@"\bpairs\s*\(\s*frameEmitters\s*\)").Count;
        if (traversals is <1 or >2 ||
            Regex.Matches(code,@"(?m)^[ \t]*for interval, emitter in pairs\(frameEmitters\) do[ \t]*(?=\r*$)").Count!=traversals)
            return false;
        // A second traversal is admitted only for the stock diagnostic reader,
        // with no callbacks, writes, or external escape of the emitter.
        if (Regex.Matches(code,@"\bpairs\s*\(\s*frameEmitters\s*\)").Count == 2 &&
            Count(code,"table.insert(intervals, { interval = interval, listeners = emitter:getListenerCount(), emitter = emitter })") != 1)
            return false;
        var normalized = Regex.Replace(candidate,@"\r+\n","\n");
        bool state = normalized.Contains(NormalizeLf(FrameDemandState),StringComparison.Ordinal);
        bool created = normalized.Contains(NormalizeLf(FrameEmitterCreateTracked),StringComparison.Ordinal);
        bool removed = normalized.Contains(NormalizeLf(FrameEmitterRemoveTracked),StringComparison.Ordinal);
        bool loop = normalized.Contains(NormalizeLf(FrameEmitterLoopTracked),StringComparison.Ordinal);
        // Each generated fragment can exist independently; complete only
        // missing fragments. Do not duplicate partially customized helpers.
        if (Count(source,FrameDemandMarker)!=(state?1:0) ||
            Count(source,"__gcetFrameDemandHandles")!=(state?1:0)+(created?1:0)+(removed?2:0) ||
            Count(source,"__gcetFrameDemandReady")!=(state?1:0)+(created?1:0)+(loop?1:0))
            return false;
        if (Count(candidate,FrameDemandBind)==0)
        {
            if (candidate.Contains("__gcetDemand",StringComparison.Ordinal) ||
                !BeforeLine(ref candidate,"local function UpdateFrame(delta)",FrameDemandBind))
                return false;
        }
        if(Count(candidate,FrameDemandBind)!=1)return false;
        if(!state&&!AfterLine(ref candidate,FrameDemandBind,FrameDemandState))return false;
        if(!created&&!ReplaceUniqueLines(ref candidate,FrameEmitterCreate,FrameEmitterCreateTracked))return false;
        if(!removed&&!ReplaceUniqueLines(ref candidate,FrameEmitterRemove,FrameEmitterRemoveTracked))return false;
        if(!loop&&!ReplaceUniqueLines(ref candidate,FrameEmitterLoop,FrameEmitterLoopTracked))return false;
        next=candidate;return true;
    }

    internal static int TryStageFrameEmitters(ref FixedZeroEngineBuild framework,List<object> manifest)
    {
        if(!framework.Files.TryGetValue(InitPath,out var hb)||
           !framework.Files.TryGetValue(SchedulerPath,out var sb))return 0;
        var host=Encoding.UTF8.GetString(hb);var scheduler=Encoding.UTF8.GetString(sb);
        if(!TryFrameEmitters(host,out var hn)||!TryScheduler(scheduler,out var sn)||
           (hn==host&&sn==scheduler))return 0;
        var files=framework.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        files[InitPath]=Encoding.UTF8.GetBytes(hn);files[SchedulerPath]=Encoding.UTF8.GetBytes(sn);
        framework=framework with{Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[InitPath])).ToLowerInvariant()};
        manifest.Add(new{type="FRAMEWORK_FRAME_DEMAND",Owner="0-Engine",file=InitPath,
            contract="GCET_FRAMEWORK_FRAME_DEMAND_V1",sourceProof=true,
            callbackOrCadenceChanged=false,PolicyClass="CENTRAL_EXACT_FRAME_DEMAND"});
        return 1;
    }


    // Reuse the *same* live player observation only on the two successful
    // lifecycle paths which cannot run author callbacks (warmup pending,
    // stable player). Ready/recreated/invalidation, failure and unknown
    // source always retain the host's original second GetPlayer call.
    private const string PlayerReuseMarker = "-- G-CET lifecycle same-phase player observation v1";
    private const string PlayerReuseHostMarker = "-- G-CET lifecycle Blackboard player reuse v1";
    private const string PlayerUpdateStock = """
    local ok, player = pcall(Game.GetPlayer)
    if not ok then player = nil end
    if player then
        local ok2, id = pcall(player.GetEntityID, player)
        if not ok2 or not id then
            player = nil
        end
    end
    local hash = GetPlayerHash(player)
""";
    private const string PlayerUpdateChecked = """
    local ok, player = pcall(Game.GetPlayer)
    if not ok then player = nil end
    local checkedId = nil
    if player then
        local ok2, id = pcall(player.GetEntityID, player)
        if not ok2 or not id then
            player = nil
        else
            checkedId = id
        end
    end
    local hash = GetPlayerHash(checkedId)
""";
    private const string PlayerUpdateTail = """
    if not hash then
        if State.inMenu then
            failFrames = 0
            return
        end
        if warmupFrames > 0 then
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
        end
        failFrames = failFrames + 1
        if playerValid and failFrames >= failThreshold then
            playerValid = false
            lastPlayerHash = nil
            failFrames = 0
            State.player:set(nil)
            Events.PlayerInvalidated:trigger()
        end
        return
    end
    failFrames = 0
    if not playerValid then
        if warmupFrames == 0 then
            warmupPlayer = player
            warmupHash = hash
        end
        if hash ~= warmupHash then
            warmupFrames = 0
            warmupPlayer = player
            warmupHash = hash
        end
        warmupFrames = warmupFrames + 1
        if warmupFrames >= warmupThreshold then
            playerValid = true
            lastPlayerHash = warmupHash
            State.player:set(warmupPlayer)
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
            Events.PlayerReady:trigger(player)
        end
        return
    end
    if hash ~= lastPlayerHash then
        lastPlayerHash = hash
        State.player:set(player)
        Events.PlayerRecreated:trigger(player)
    end
""";
    private const string PlayerUpdateOptimizedTail = """
    if not hash then
        if State.inMenu then
            failFrames = 0
            return
        end
        if warmupFrames > 0 then
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
        end
        failFrames = failFrames + 1
        if playerValid and failFrames >= failThreshold then
            playerValid = false
            lastPlayerHash = nil
            failFrames = 0
            State.player:set(nil)
            Events.PlayerInvalidated:trigger()
        end
        return
    end
    failFrames = 0
    if not playerValid then
        if warmupFrames == 0 then
            warmupPlayer = player
            warmupHash = hash
        end
        if hash ~= warmupHash then
            warmupFrames = 0
            warmupPlayer = player
            warmupHash = hash
        end
        warmupFrames = warmupFrames + 1
        if warmupFrames >= warmupThreshold then
            playerValid = true
            lastPlayerHash = warmupHash
            State.player:set(warmupPlayer)
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
            Events.PlayerReady:trigger(player)
            return
        end
        return player
    end
    if hash ~= lastPlayerHash then
        lastPlayerHash = hash
        State.player:set(player)
        Events.PlayerRecreated:trigger(player)
        return
    end
    return player
""";
    private const string PlayerWrapper = """
function Lifecycle.Update()
    __gcetLifecycleUpdateCore()
end
function Lifecycle.__gcetUpdateForBlackboard()
    return __gcetLifecycleUpdateCore()
end
""";
    private static string ProofTokens(string text) =>
        Regex.Replace(WithoutLuaTrivia(text),@"\s+"," ").Trim();

    internal static bool TryLifecyclePlayerReuse(string source,out string next)
    {
        next=source;
        var done=source.Contains(PlayerReuseMarker,StringComparison.Ordinal);
        var name=done?"local function __gcetLifecycleUpdateCore()":"function Lifecycle.Update()";
        var body=Regex.Matches(source,@"(?m)^"+Regex.Escape(name)+
            @"[ \t]*\r*\n(?<body>[\s\S]*?)^end[ \t]*(?=\r*$)");
        if(body.Count!=1)return false;
        var expected=done?PlayerUpdateOptimizedTail:PlayerUpdateTail;
        var actual=ProofTokens(body[0].Groups["body"].Value);
        var stock=actual==ProofTokens(PlayerUpdateStock+"\n"+expected);
        var checkedId=actual==ProofTokens(PlayerUpdateChecked+"\n"+expected);
        if(!stock&&!checkedId)return false;
        var helper=Regex.Matches(source,@"(?m)^local function GetPlayerHash\((?:player|id)\)[ \t]*\r*\n(?<body>[\s\S]*?)^end[ \t]*(?=\r*$)");
        if(helper.Count!=1)return false;
        var hash=ProofTokens(helper[0].Groups["body"].Value);
        if(stock && hash!="if not player then return nil end local ok, id = pcall(player.GetEntityID, player) if not ok or not id then return nil end return id.hash")return false;
        if(checkedId && hash!="if not id then return nil end return id.hash")return false;
        if(done)
            return Count(source,PlayerReuseMarker)==1 &&
                Count(source,"__gcetLifecycleUpdateCore")==3 &&
                Count(source,"__gcetUpdateForBlackboard")==1 &&
                Count(NormalizeLf(source),NormalizeLf(PlayerWrapper))==1;
        if(source.Contains("__gcetLifecycleUpdateCore",StringComparison.Ordinal)||
           source.Contains("__gcetUpdateForBlackboard",StringComparison.Ordinal))return false;
        // Preserve comments/custom formatting in the admitted function.
        var updated=body[0].Value;
        if(!ReplaceLine(ref updated,name,"local function __gcetLifecycleUpdateCore()")||
           !AfterLine(ref updated,"Events.PlayerReady:trigger(player)","return")||
           !AfterLine(ref updated,"Events.PlayerRecreated:trigger(player)","return"))return false;
        // The warmup return is the only bare return immediately before
        // the enclosing end and Player recreated block. Match its actual
        // structural position, not an occurrence elsewhere in the file.
        var m=Regex.Matches(updated,@"(?m)^        return[ \t]*(?=\r*\n    end[ \t]*\r*\n(?:[ \t]*\r*\n|[ \t]*--[^\r\n]*\r*\n)*    if hash ~= lastPlayerHash then)");
        if(m.Count!=1)return false;
        updated=updated.Remove(m[0].Index,m[0].Length).Insert(m[0].Index,"        return player");
        var last=Regex.Match(updated,@"(?m)^end[ \t]*\r*$");
        if(!last.Success)return false;
        updated=updated.Insert(last.Index,Newlines(source,"    return player\n"));
        var replacement=PlayerReuseMarker+"\n"+updated+"\n\n"+PlayerWrapper;
        next=source.Remove(body[0].Index,body[0].Length)
            .Insert(body[0].Index,Newlines(source,replacement));
        return true;
    }

    internal static bool TryLifecyclePlayerHost(string source,out string next)
    {
        next=source;
        const string first="Lifecycle.Update()";
        const string second="local pcOk, pc = pcall(Game.GetPlayer)";
        const string a="""
-- G-CET lifecycle Blackboard player reuse v1
local pc
if type(Lifecycle.__gcetUpdateForBlackboard) == "function" then
    pc = Lifecycle.__gcetUpdateForBlackboard()
else
    Lifecycle.Update()
end
""";
        const string b="""
local pcOk = pc ~= nil
if not pcOk then pcOk, pc = pcall(Game.GetPlayer) end
""";
        var normalized=NormalizeLf(source);
        if(source.Contains(PlayerReuseHostMarker,StringComparison.Ordinal))
            return Count(source,PlayerReuseHostMarker)==1 &&
                Count(source,"pc = Lifecycle.__gcetUpdateForBlackboard()")==1 &&
                Count(source,"if not pcOk then pcOk, pc = pcall(Game.GetPlayer) end")==1;
        // Between lifecycle and the original lookup there must be no
        // executable code, only comments. Prove the actual BB callsite.
        if(Count(source,first)!=1||Count(source,second)!=1||
           Count(source,"pcall(BlackboardCache.Update, pc)")!=1||
           source.Contains("__gcetUpdateForBlackboard",StringComparison.Ordinal))return false;
        if(!Regex.IsMatch(WithoutLuaTrivia(normalized),
            @"Lifecycle\.Update\(\)\s*local pcOk, pc = pcall\(Game\.GetPlayer\)\s*if pcOk and pc then\s*pcall\(BlackboardCache\.Update, pc\)"))return false;
        var candidate=source;
        if(!ReplaceLine(ref candidate,first,a)||!ReplaceLine(ref candidate,second,b))return false;
        next=candidate;return true;
    }

    internal static int TryStageLifecyclePlayer(string modsRoot,ref FixedZeroEngineBuild framework,
        Dictionary<string,byte[]> staged,Dictionary<string,int> counts,List<object> manifest)
    {
        const string relative="0-Engine/modules/Lifecycle.lua";
        var path=Path.Combine(modsRoot,"0-Engine","modules","Lifecycle.lua");
        if(!File.Exists(path)||!framework.Files.TryGetValue(InitPath,out var hb))return 0;
        var module=Encoding.UTF8.GetString(staged.TryGetValue(relative,out var pb)?pb:File.ReadAllBytes(path));
        var host=Encoding.UTF8.GetString(hb);
        if(!TryLifecyclePlayerReuse(module,out var mn)||!TryLifecyclePlayerHost(host,out var hn)||
           (module==mn&&host==hn))return 0;
        if(module!=mn){
            staged[relative]=Encoding.UTF8.GetBytes(mn);
            counts[relative]=counts.GetValueOrDefault(relative)+1;
        }
        if(host!=hn){
            var files=framework.Files.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
            files[InitPath]=Encoding.UTF8.GetBytes(hn);
            framework=framework with{Files=files,FixedInitSha256=Convert.ToHexString(
                SHA256.HashData(files[InitPath])).ToLowerInvariant()};
        }
        manifest.Add(new{type="FRAMEWORK_PLAYER_OBSERVATION_REUSE",Owner="0-Engine",file=relative,
            contract="GCET_LIFECYCLE_PLAYER_OBSERVATION_V1",sourceProof=true,
            callbackOrCadenceChanged=false,policy="SAME_PHASE_NO_CALLBACK_REUSE"});
        return 1;
    }

    private const string DerivedMarker = "-- G-CET derived sample demand v1";
    private const string SampleMarker = "-- G-CET demand sample slots v1";
    private const string DerivedHostMarker = "-- G-CET derived sample host v1";

    // Extend the same scheduler-owned demand table. A sample is offered
    // at its original producer phase. Legacy public cached fields carry
    // historical snapshot demand even before their first observable read.
    // Do not defer these samples without a separately proven consumer contract.
    internal static bool TrySampleController(string source, out string next)
    {
        next = source;
        if (source.Contains(SampleMarker, StringComparison.Ordinal))
        {
            // Other sample families use private locals with the same names.
            // Prove the actual DefineSample scope, not a module-wide method
            // count which rejects a valid composed runtime on pass two.
            var scope = Regex.Matches(source,
                @"(?m)^(?<indent>[ \t]*)function demand\.DefineSample\([^\r\n]+\)[ \t]*\r*\n(?<body>[\s\S]*?)^\k<indent>end[ \t]*(?=\r*$)");
            if (scope.Count != 1 || Count(source, "function demand.DefineSample(") != 1)
                return false;
            var sample = scope[0].Value;
            if (Count(sample, "function sample.Offer(") != 1 ||
                Count(sample, "function sample.Read(") != 1 ||
                Count(sample, "function sample.Expose(") != 1 ||
                Count(sample, "function sample.Reset(") != 1) return false;
            if (source.Contains("demand.SampleEpochVersion = 1", StringComparison.Ordinal) ||
                source.Contains("demand.SampleEpochVersion = 2", StringComparison.Ordinal))
            {
                if (Count(sample, "local offeredEpoch") != 1 ||
                    Count(sample, "offeredEpoch = epoch()") != 1 ||
                    Count(sample, "offeredEpoch ~= epoch()") != 1) return false;
                var v2 = Count(source, "demand.SampleEpochVersion = 2") == 1;
                if (v2 && Count(sample, "available, sampled = true, true") == 1 &&
                    Count(sample, "sample.Read()") == 2 &&
                    Regex.IsMatch(sample, @"offeredEpoch = epoch\(\)\s+produce\(\)")) return true;
                var oldOffer = v2 ? "sample.Read()" : "if exposed then sample.Read() end";
                if ((!v2 && Count(source, "demand.SampleEpochVersion = 1") != 1) ||
                    Count(sample, "available, sampled = true, false") != 1 ||
                    !Regex.IsMatch(sample, @"offeredEpoch = epoch\(\)\s+" + Regex.Escape(oldOffer))) return false;
                var upgradedSample = sample;
                // Scope the rewrite to Offer; Expose also calls Read.
                if (!ReplaceLine(ref upgradedSample, "available, sampled = true, false", "available, sampled = true, true") ||
                    !ReplaceUniqueLines(ref upgradedSample, "        offeredEpoch = epoch()\n        " + oldOffer,
                        "        offeredEpoch = epoch()\n        produce()"))
                {
                    // Existing generation indents the complete slot by the
                    // enclosing do block. Preserve that exact indentation.
                    var indent = scope[0].Groups["indent"].Value;
                    upgradedSample = sample;
                    if (!ReplaceLine(ref upgradedSample, "available, sampled = true, false", "available, sampled = true, true") ||
                        !ReplaceUniqueLines(ref upgradedSample, indent + "        offeredEpoch = epoch()\n" + indent + "        " + oldOffer,
                            indent + "        offeredEpoch = epoch()\n" + indent + "        produce()")) return false;
                }
                var upgraded = source.Remove(scope[0].Index, scope[0].Length).Insert(scope[0].Index, upgradedSample);
                if (!v2 && !ReplaceLine(ref upgraded, "demand.SampleEpochVersion = 1", "demand.SampleEpochVersion = 2")) return false;
                next = upgraded;
                return true;
            }
            // Complete the earlier development sample slot in place.
            // Epoch identity comes from the existing host frame clock.
            var candidate = source;
            if (!AfterLine(ref candidate, "local samples = {}", "demand.SampleEpochVersion = 2") ||
                !ReplaceLine(ref candidate, "function demand.DefineSample(key, produce)",
                    "function demand.DefineSample(key, produce, epoch)") ||
                !ReplaceLine(ref candidate, "if samples[key] or type(produce) ~= \"function\" then return nil end",
                    "if samples[key] or type(produce) ~= \"function\" or type(epoch) ~= \"function\" then return nil end") ||
                !AfterLine(ref candidate, "local available, sampled, exposed = false, false, false", "local offeredEpoch") ||
                !ReplaceLine(ref candidate, "if not available or sampled then return end",
                    "if not available or sampled or offeredEpoch ~= epoch() then return end") ||
                !ReplaceLine(ref candidate, "available, sampled = true, false", "available, sampled = true, true\nofferedEpoch = epoch()") ||
                !AfterLine(ref candidate, "available, sampled = false, false", "offeredEpoch = nil") ||
                !ReplaceLine(ref candidate, "if exposed then sample.Read() end", "produce()"))
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
            if (source.Contains("controller.SampleEpochVersion ~= 1", StringComparison.Ordinal) ||
                source.Contains("controller.SampleEpochVersion ~= 2", StringComparison.Ordinal))
            {
                if (Count(source, loadingReset) != 1 ||
                    Count(source, "__gcetPitchSample.Reset()") != 2 ||
                    Count(source, "function DerivedState.__gcetEnableDemand(controller, epoch)") != 1)
                    return false;
                if (Count(source, "controller.SampleEpochVersion ~= 2") == 1)
                    return Count(source, "controller.SampleEpochVersion ~= 1") == 0;
                return ReplaceUnique(ref next, "controller.SampleEpochVersion ~= 1", "controller.SampleEpochVersion ~= 2");
            }
            var candidate = source;
            if (Count(source, "__gcetPitchSample.Reset()") != 1 ||
                !ReplaceLine(ref candidate, "function DerivedState.__gcetEnableDemand(controller)",
                    "function DerivedState.__gcetEnableDemand(controller, epoch)") ||
                !ReplaceLine(ref candidate,
                    "if type(controller) ~= \"table\" or type(controller.DefineSample) ~= \"function\" then return false end",
                    "if type(controller) ~= \"table\" or controller.SampleEpochVersion ~= 2 or type(controller.DefineSample) ~= \"function\" or type(epoch) ~= \"function\" then return false end") ||
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
        var normalized = DerivedSharedProof(source);
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
    private const string LuaTriviaPattern =
        """--\[(?<ce>=*)\[.*?\]\k<ce>\]|--[^\r\n]*|\[(?<le>=*)\[.*?\]\k<le>\]|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'""";
    private static string WithoutLuaTrivia(string source) => Regex.Replace(source,
        LuaTriviaPattern," ",RegexOptions.Singleline|RegexOptions.CultureInvariant);
    private static string MaskLuaTrivia(string source) => Regex.Replace(source,
        LuaTriviaPattern,m=>new string(m.Value.Select(c=>c=='\r'||c=='\n'?c:' ').ToArray()),
        RegexOptions.Singleline|RegexOptions.CultureInvariant);

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
demand.SampleEpochVersion = 2
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
        available, sampled = true, true
        offeredEpoch = epoch()
        produce()
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
    if type(controller) ~= "table" or controller.SampleEpochVersion ~= 2 or type(controller.DefineSample) ~= "function" or type(epoch) ~= "function" then return false end
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


    // Legacy cached weapon/workspot state is observable after its original
    // six-successful-frame sample. Until exact historical tracking is proven,
    // the existing scheduler-owned slots must sample synchronously at Offer.
    // Version 2 deliberately refuses the former lazy mixed-runtime contract.
    private const string CadencedSampleMarker="-- G-CET cadenced sample slots v1";
    internal const string CadencedSampleController = """
-- G-CET cadenced sample slots v1
demand.CadencedSampleVersion = 2
function demand.DefineCadencedSample(key, produce, valid, getPlayer)
    if samples[key] or type(produce) ~= "function" or type(valid) ~= "function" or type(getPlayer) ~= "function" then return nil end
    local sample = {}
    local pending, exposed = false, false
    function sample.Read(player)
        if not pending or not valid() then return end
        player = player or getPlayer()
        if not player then return end
        pending = false
        produce(player)
    end
    function sample.Offer(player)
        -- The author's receiver and phase are already established here.
        -- A preceding getter may have re-entered the host and advanced its
        -- clock; that is not permission to skip this historical sample.
        pending = false
        produce(player)
    end
    function sample.Expose()
        exposed = true
        sample.Read()
    end
    function sample.Reset()
        pending = false
    end
    samples[key] = sample
    return sample
end
""";
    internal static bool TryCadencedController(string source,out string next)
    {
        next=source;
        if(!source.Contains(SampleMarker,StringComparison.Ordinal)||
           Count(source,"local samples = {}")!=1||
           Count(source,"demand.SampleEpochVersion = 2")!=1)return false;
        if(source.Contains(CadencedSampleMarker,StringComparison.Ordinal))
        {
            string Flat(string value) => Regex.Replace(NormalizeLf(value),@"(?m)^[ \t]+","");
            if(Count(Flat(source),Flat(CadencedSampleController))==1)return true;
            var directOffer = """
    function sample.Offer(player)
        -- The author's receiver and phase are already established here.
        -- A preceding getter may have re-entered the host and advanced its
        -- clock; that is not permission to skip this historical sample.
        pending = false
        produce(player)
    end
""";
            var initialV2Offer = """
    function sample.Offer(player)
        pending = true
        sample.Read(player)
    end
""";
            var initialV2=NormalizeLf(CadencedSampleController).Replace(NormalizeLf(directOffer),NormalizeLf(initialV2Offer),StringComparison.Ordinal);
            var legacy=initialV2.Replace("demand.CadencedSampleVersion = 2","demand.CadencedSampleVersion = 1",StringComparison.Ordinal)
                .Replace("        sample.Read(player)","        if exposed then sample.Read(player) end",StringComparison.Ordinal);
            var v2=Count(Flat(source),Flat(initialV2))==1;
            var v1=Count(Flat(source),Flat(legacy))==1;
            if(v2==v1)return false;
            var upgraded=source;
            if(v1&&!ReplaceLine(ref upgraded,"demand.CadencedSampleVersion = 1","demand.CadencedSampleVersion = 2"))return false;
            var previous=v2?NormalizeLf(initialV2Offer):NormalizeLf(initialV2Offer).Replace("sample.Read(player)\n","if exposed then sample.Read(player) end\n",StringComparison.Ordinal);
            // Replace the single proven old Offer block, preserving its
            // generated enclosing indentation and all unrelated source.
            var offer=Regex.Matches(upgraded,@"(?m)^(?<indent>[ \t]*)function sample\.Offer\(player\)[ \t]*(?=\r*$)");
            if(offer.Count!=1)return false;
            var indent=offer[0].Groups["indent"].Value;
            string IndentOffer(string block) => string.Join("\n",NormalizeLf(block).Split('\n').Select(l=>indent+l[4..]));
            if(!ReplaceUniqueLines(ref upgraded,IndentOffer(previous),IndentOffer(directOffer)))return false;
            next=upgraded;return true;
        }
        if(source.Contains("DefineCadencedSample",StringComparison.Ordinal))return false;
        return BeforeLine(ref next,"Scheduler.Demand = demand",CadencedSampleController);
    }

    private const string CadencedDerivedMarker="-- G-CET cadenced player scalar demand v1";
    private const string CadencedDerivedHostMarker="-- G-CET cadenced player scalar host v1";
    private const string CadencedLocals = """
-- G-CET cadenced player scalar demand v1
local __gcetPlayerSamples, __gcetCadencedClock, __gcetCadencedEpoch
local function __gcetExposePlayerSamples()
    if __gcetPlayerSamples then
        for _, sample in pairs(__gcetPlayerSamples) do sample.Expose() end
    end
end
local function __gcetResetPlayerSamples()
    __gcetCadencedEpoch = nil
    if __gcetPlayerSamples then
        for _, sample in pairs(__gcetPlayerSamples) do sample.Reset() end
    end
end
""";
    private const string CadencedAdapter = """
function DerivedState.__gcetEnableCadencedDemand(controller, epoch, getPlayer)
    if __gcetPlayerSamples then return true end
    if type(controller) ~= "table" or controller.CadencedSampleVersion ~= 2 or
        type(controller.DefineCadencedSample) ~= "function" or type(epoch) ~= "function" or type(getPlayer) ~= "function" then return false end
    local function valid() return __gcetCadencedEpoch ~= nil and __gcetCadencedEpoch == epoch() end
    local fields = {
        hasWeapon = controller.DefineCadencedSample("DerivedState.hasWeapon", function(player)
            cached.hasWeapon = resolveHasWeapon(player)
        end, valid, getPlayer),
        isInWorkspot = controller.DefineCadencedSample("DerivedState.isInWorkspot", function(player)
            cached.isInWorkspot = resolveIsInWorkspot(player)
        end, valid, getPlayer)
    }
    if next(fields) == nil then return false end
    __gcetPlayerSamples, __gcetCadencedClock = fields, epoch
    if __gcetRawExposed then __gcetExposePlayerSamples() end
    return true
end
""";
    private const string HasWeaponReader = """
local function resolveHasWeapon(player)
    local ok, result = pcall(function()
        if not weaponSlotID then
            weaponSlotID = TweakDBID.new("AttachmentSlots.WeaponRight")
        end
        local ts = Game.GetTransactionSystem()
        if not ts then return false end
        return ts:GetItemInSlot(player, weaponSlotID) ~= nil
    end)
    return ok and result or false
end
""";
    private const string WorkspotReader = """
local function resolveIsInWorkspot(player)
    local ok, result = pcall(function()
        local ws = Game.GetWorkspotSystem()
        if not ws then return false end
        return ws:IsActorInWorkspot(player)
    end)
    return ok and result or false
end
""";
    private const string CadencedWeaponOffer =
        "if __gcetPlayerSamples and __gcetPlayerSamples.hasWeapon then __gcetPlayerSamples.hasWeapon.Offer(player) else cached.hasWeapon = resolveHasWeapon(player) end";
    private const string CadencedWorkspotOffer =
        "if __gcetPlayerSamples and __gcetPlayerSamples.isInWorkspot then __gcetPlayerSamples.isInWorkspot.Offer(player) else cached.isInWorkspot = resolveIsInWorkspot(player) end";
    private const string CadencedMark =
        "if __gcetCadencedClock then __gcetCadencedEpoch = __gcetCadencedClock() end";
    private const string CadencedAdvance =
        "if __gcetCadencedClock and pollFrameCounter < pollThrottle then __gcetCadencedEpoch = __gcetCadencedClock() end";
    private const string CadencedRead = """
if tbl == cached and __gcetPlayerSamples then
    local sample = __gcetPlayerSamples[key]
    if sample then sample.Read() end
end
""";
    internal static bool TryCadencedDerivedProducer(string source,out string next)
    {
        next=source;
        if(!TryDerivedProducer(source,out var pitch)||pitch!=source)return false;
        // Only pure independent scalar readers are eligible: no additional
        // field uses, transition events, prior-value dependency or custom
        // native side effects. Weapon type, death, cover, time and district
        // remain at their original phase and cadence.
        var normalized=DerivedSharedProof(source);
        if(Count(normalized,NormalizeLf(HasWeaponReader))!=1||
           Count(normalized,NormalizeLf(WorkspotReader))!=1||
           Count(source,"local pollThrottle = 6")!=1||
           Count(source,"pollFrameCounter = pollFrameCounter + 1")!=1||
           Count(source,"if pollFrameCounter >= pollThrottle then")!=1||
           Count(source,"local weaponSlotID = nil")!=1)return false;
        if(source.Contains(CadencedDerivedMarker,StringComparison.Ordinal))
        {
            var legacyAdapter=CadencedAdapter.Replace("controller.CadencedSampleVersion ~= 2","controller.CadencedSampleVersion ~= 1",StringComparison.Ordinal);
            var current=Count(normalized,NormalizeLf(CadencedAdapter))==1;
            var legacy=Count(normalized,NormalizeLf(legacyAdapter))==1;
            if(current==legacy)return false;
            if(!(Count(normalized,NormalizeLf(CadencedLocals))==1&&
                Count(source,CadencedWeaponOffer)==1&&Count(source,CadencedWorkspotOffer)==1&&
                Count(source,CadencedMark)==1&&Count(source,CadencedAdvance)==1&&
                Count(source,"__gcetExposePlayerSamples()")==4&&
                Count(source,"__gcetResetPlayerSamples()")==3&&
                Count(source,"local sample = __gcetPlayerSamples[key]")==1&&
                Count(WithoutLuaTrivia(source),"cached.hasWeapon")==3&&
                Count(WithoutLuaTrivia(source),"cached.isInWorkspot")==3))return false;
            return current||ReplaceUnique(ref next,"controller.CadencedSampleVersion ~= 1","controller.CadencedSampleVersion ~= 2");
        }
        var code=WithoutLuaTrivia(source);
        if(Regex.Matches(code,@"\bhasWeapon\b").Count!=3||
           Regex.Matches(code,@"\bisInWorkspot\b").Count!=3||
           Regex.Matches(code,@"\bresolveHasWeapon\b").Count!=2||
           Regex.Matches(code,@"\bresolveIsInWorkspot\b").Count!=2||
           source.Contains("__gcetPlayerSamples",StringComparison.Ordinal))return false;
        // The original field order inside the six-frame phase matters,
        // especially when the subsequent CoverDirectionChanged callback
        // reads state. Do not move any of the remaining producers.
        if(!Regex.IsMatch(code,@"if pollFrameCounter >= pollThrottle then\s+pollFrameCounter = 0[\s\S]*?cached\.hasWeapon = resolveHasWeapon\(player\)\s+cached\.isInWorkspot = resolveIsInWorkspot\(player\)\s+local coverDir = resolveCoverDirection\(player\)"))
            return false;
        var candidate=source;
        if(!AfterLine(ref candidate,"local __gcetPitchSample, __gcetRawExposed = nil, false",CadencedLocals)||
           !AfterLine(ref candidate,"pollFrameCounter = pollFrameCounter + 1",CadencedAdvance)||
           !ReplaceLine(ref candidate,"cached.hasWeapon = resolveHasWeapon(player)",CadencedMark+"\n"+CadencedWeaponOffer)||
           !ReplaceLine(ref candidate,"cached.isInWorkspot = resolveIsInWorkspot(player)",CadencedWorkspotOffer)||
           !AfterLine(ref candidate,"cached.hasWeapon = false","__gcetResetPlayerSamples()")||
           !AfterLine(ref candidate,"cached.isLoading = loading == true","if loading == true then __gcetResetPlayerSamples() end")||
           !AfterLine(ref candidate,"function DerivedState.Get()","__gcetExposePlayerSamples()")||
           !AfterLine(ref candidate,"function DerivedState.__gcetReadField(tbl, key)",CadencedRead)||
           !AfterLine(ref candidate,"if tbl == nil or tbl == cached then","__gcetExposePlayerSamples()")||
           !BeforeLine(ref candidate,"return DerivedState",CadencedAdapter))return false;
        next=candidate;return true;
    }

    internal static bool TryCadencedDerivedHost(string source,out string next)
    {
        next=source;
        if(!TryDerivedHost(source,out var pitch)||pitch!=source||
           Count(source,"local function GetPlayer()")!=1||
           Count(source,"return State.player()")!=1)return false;
        const string bridge = """
-- G-CET cadenced player scalar host v1
if __gcetDerivedDemand and type(DerivedState.__gcetEnableCadencedDemand) == "function" then
    DerivedState.__gcetEnableCadencedDemand(__gcetDemand, function() return currentFrame end, GetPlayer)
end
""";
        if(source.Contains(CadencedDerivedHostMarker,StringComparison.Ordinal))
            return Count(NormalizeLf(source),NormalizeLf(bridge))==1;
        if(source.Contains("__gcetEnableCadencedDemand",StringComparison.Ordinal))return false;
        var handshake=Regex.Matches(source,@"(?m)^(?<line>__gcetDerivedDemand = [^\r\n]+DerivedState\.__gcetEnableDemand\(__gcetDemand, function\(\) return currentFrame end\))[ \t]*(?=\r*$)");
        if(handshake.Count!=1)return false;
        return AfterLine(ref next,handshake[0].Groups["line"].Value,bridge);
    }

    // The V2 historical fields always sample at the author phase. No
    // consumer can suppress those reads without losing a historical value.
    // Do not generate the old sample/proxy layer on stock source. Migrate
    // only recognizable owned fragments of previous overlays, independently
    // of the live Scheduler version; its unused compatibility API can stay.
    private static readonly Regex HistoricalTrivia = new(
        """--\[(?<ce>=*)\[.*?\]\k<ce>\]|--[^\r\n]*|\[(?<le>=*)\[.*?\]\k<le>\]|"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*'""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private static bool HistoricalResidual(string source,string owned) =>
        Regex.IsMatch(HistoricalTrivia.Replace(source,m=>m.Value.StartsWith("--",StringComparison.Ordinal)?" ":m.Value),owned);

    // Do not remove an exact-looking generated fragment inside an author
    // string or long comment. An actual generated marker starts its own
    // lexical line comment.
    private static Match[] HistoricalMatches(string source,Regex pattern,bool marker=false)
    {
        var trivia=HistoricalTrivia.Matches(source).Cast<Match>().ToArray();
        return pattern.Matches(source).Cast<Match>().Where(m=>{
            var at=m.Index+m.Groups["i"].Length;
            return !trivia.Any(v=>v.Index<=at&&at<v.Index+v.Length&&
                !(marker&&v.Index==at&&v.Value.StartsWith("--",StringComparison.Ordinal)));
        }).ToArray();
    }

    private static bool RemoveHistoricalFragment(ref string source, string fragment, int maxCount=1)
    {
        var lines=NormalizeLf(fragment).Split('\n');
        var pattern=@"(?m)^(?<i>[ \t]*)"+Regex.Escape(lines[0]);
        foreach(var line in lines.Skip(1))
            pattern+=@"\r*\n\k<i>"+Regex.Escape(line);
        pattern+=@"[ \t]*(?:\r*\n|$)";
        var matches=HistoricalMatches(source,new Regex(pattern,RegexOptions.CultureInvariant),fragment.StartsWith("--",StringComparison.Ordinal));
        if(matches.Length>maxCount)return false;
        for(var i=matches.Length-1;i>=0;--i)source=source.Remove(matches[i].Index,matches[i].Length);
        return true;
    }

    private static bool RestoreHistoricalLine(ref string source,string old,string original)
    {
        var pattern=new Regex(@"(?m)^(?<i>[ \t]*)"+Regex.Escape(old)+@"[ \t]*(?=\r?$)");
        var matches=HistoricalMatches(source,pattern);
        if(matches.Length==0)return true;
        if(matches.Length!=1)return false;
        var m=matches[0];
        source=source.Remove(m.Index,m.Length).Insert(m.Index,m.Groups["i"].Value+original);
        return true;
    }

    internal static bool TryRetireHistoricalModule(string source,out string next)
    {
        next=source;
        var candidate=source;
        // These identifiers are private to the previous generated layer.
        // If any unrecognized reference survives, do not publish a partial
        // reversal. Other author functions/fields remain byte-for-byte.
        const string owned=@"\b(?:__gcetPitchSample|__gcetRawExposed|__gcetPlayerSamples|__gcetCadencedClock|__gcetCadencedEpoch|__gcetExposePlayerSamples|__gcetResetPlayerSamples|__gcetEnableDemand|__gcetEnableCadencedDemand|__gcetPeek|__gcetReadField|__gcetExpose)\b";
        if(!HistoricalResidual(candidate,owned)&&
           !candidate.Contains(DerivedMarker,StringComparison.Ordinal)&&!candidate.Contains(CadencedDerivedMarker,StringComparison.Ordinal))return true;
        if(Count(WithoutLuaTrivia(candidate),"function DerivedState.Update(player)")!=1||
           Count(WithoutLuaTrivia(candidate),"function DerivedState.Get()")!=1||
           Count(WithoutLuaTrivia(candidate),"function DerivedState.Reset()")!=1)return false;
        bool Restore(string old,string original)
        {
            return RestoreHistoricalLine(ref candidate,old,original);
        }
        // Remove full generated adapters first (the cadence read/expose
        // hooks may be embedded within the pitch adapter).
        if(!RemoveHistoricalFragment(ref candidate,CadencedAdapter)||
           !RemoveHistoricalFragment(ref candidate,CadencedAdapter.Replace("controller.CadencedSampleVersion ~= 2","controller.CadencedSampleVersion ~= 1",StringComparison.Ordinal))||
           !RemoveHistoricalFragment(ref candidate,CadencedRead)||
           !RemoveHistoricalFragment(ref candidate,"__gcetExposePlayerSamples()",2)||
           !RemoveHistoricalFragment(ref candidate,"__gcetResetPlayerSamples()")||
           !RemoveHistoricalFragment(ref candidate,"if loading == true then __gcetResetPlayerSamples() end")||
           !RemoveHistoricalFragment(ref candidate,CadencedLocals)||
           !RemoveHistoricalFragment(ref candidate,CadencedAdvance)||
           !RemoveHistoricalFragment(ref candidate,CadencedMark)||
           !Restore(CadencedWeaponOffer,"cached.hasWeapon = resolveHasWeapon(player)")||
           !Restore(CadencedWorkspotOffer,"cached.isInWorkspot = resolveIsInWorkspot(player)"))return false;
        if(!RemoveHistoricalFragment(ref candidate,DerivedAdapter)||
           !RemoveHistoricalFragment(ref candidate,DerivedAdapter.Replace("controller.SampleEpochVersion ~= 2","controller.SampleEpochVersion ~= 1",StringComparison.Ordinal))||
           !Restore("if __gcetPitchSample then __gcetPitchSample.Offer() else cached.lookPitch = resolveLookPitch() end","cached.lookPitch = resolveLookPitch()")||
           !RemoveHistoricalFragment(ref candidate,"__gcetRawExposed = true\nif __gcetPitchSample then __gcetPitchSample.Expose() end")||
           !RemoveHistoricalFragment(ref candidate,"if loading == true and __gcetPitchSample then __gcetPitchSample.Reset() end")||
           !RemoveHistoricalFragment(ref candidate,"if __gcetPitchSample then __gcetPitchSample.Reset() end")||
           !RemoveHistoricalFragment(ref candidate,"local __gcetPitchSample, __gcetRawExposed = nil, false")||
           !RemoveHistoricalFragment(ref candidate,DerivedMarker)||
           !RemoveHistoricalFragment(ref candidate,CadencedDerivedMarker))return false;
        if(HistoricalResidual(candidate,owned)||
           Count(WithoutLuaTrivia(candidate),"cached.lookPitch = resolveLookPitch()")!=1)return false;
        next=candidate;return true;
    }

    internal static bool TryRetireHistoricalHost(string source,out string next)
    {
        next=source;
        var candidate=source;
        const string owned=@"\b(?:__gcetDerivedDemand|__gcetEnableDemand|__gcetEnableCadencedDemand|__gcetPeek|__gcetReadField|__gcetExpose)\b";
        if(!HistoricalResidual(candidate,owned)&&
           !candidate.Contains(DerivedHostMarker,StringComparison.Ordinal)&&!candidate.Contains(CadencedDerivedHostMarker,StringComparison.Ordinal))return true;
        const string legacy="__gcetDerivedDemand = type(DerivedState.__gcetEnableDemand) == 'function' and type(DerivedState.__gcetPeek) == 'function' and type(DerivedState.__gcetReadField) == 'function' and type(DerivedState.__gcetExpose) == 'function' and DerivedState.__gcetEnableDemand(__gcetDemand)";
        const string bridge = """
-- G-CET cadenced player scalar host v1
if __gcetDerivedDemand and type(DerivedState.__gcetEnableCadencedDemand) == "function" then
    DerivedState.__gcetEnableCadencedDemand(__gcetDemand, function() return currentFrame end, GetPlayer)
end
""";
        var current=legacy.Replace("(__gcetDemand)","(__gcetDemand, function() return currentFrame end)",StringComparison.Ordinal);
        foreach(var handshake in new[]{legacy,current,legacy.Replace("= type(DerivedState.","= Lifecycle.__gcetDerivedStateSafe == true and type(DerivedState.",StringComparison.Ordinal),current.Replace("= type(DerivedState.","= Lifecycle.__gcetDerivedStateSafe == true and type(DerivedState.",StringComparison.Ordinal)})
            if(!RemoveHistoricalFragment(ref candidate,handshake))return false;
        const string peek="State.derived = __gcetDerivedDemand and DerivedState.__gcetPeek() or DerivedState.Get()";
        if(!RestoreHistoricalLine(ref candidate,peek,"State.derived = DerivedState.Get()"))return false;
        if(!RemoveHistoricalFragment(ref candidate,bridge)||
           !RemoveHistoricalFragment(ref candidate,"if __gcetDerivedDemand and tbl == State.derived then DerivedState.__gcetReadField(tbl, k) end")||
           !RemoveHistoricalFragment(ref candidate,"if __gcetDerivedDemand then if tbl == State then DerivedState.__gcetExpose() elseif tbl == State.derived then DerivedState.__gcetExpose(tbl) end end")||
           !RemoveHistoricalFragment(ref candidate,"local __gcetDerivedDemand = false")||
           !RemoveHistoricalFragment(ref candidate,DerivedHostMarker)||
           !RemoveHistoricalFragment(ref candidate,CadencedDerivedHostMarker)||
           HistoricalResidual(candidate,owned)||
           Count(WithoutLuaTrivia(candidate),"State.derived = DerivedState.Get()")!=1)return false;
        next=candidate;return true;
    }

    // Shared stable system handles are independent of historical value
    // sampling. Reuse the EXISTING root provider at the original read site,
    // never cache a mutable camera vector, weapon, PSM state or time value.
    // Existing shared providers are session-scoped, not simply player-
    // identity-scoped. Reuse the host's actual existing session observations
    // and invalidation callback. No extra native read, event or clock.
    private const string ProviderSessionLocal =
        "local __gcetProviderSessionLoaded -- G-CET shared provider session lifetime v1";
    private const string ProviderCacheOld = """
local function __gcetGetSharedProvider(key)
    __gcetSyncSharedProviderEpoch()
    local value = __gcetSharedProviderValues[key]
    if value == nil then
        local getter = __gcetSharedProviderGetters[key]
        if getter ~= nil then
            value = getter()
            if value ~= nil then
                __gcetSharedProviderValues[key] = value
            end
        end
    end
    return value
end
""";
    private static string ProviderCacheCurrent =>
        NormalizeLf(ProviderCacheOld).Replace("value = getter()\n            if value ~= nil then\n                __gcetSharedProviderValues[key] = value",
            "local values = __gcetSharedProviderValues\n            value = getter()\n            if value ~= nil and values == __gcetSharedProviderValues then\n                values[key] = value",
            StringComparison.Ordinal);
    private static string ProviderNamedOld(string api)=>NormalizeLf("""
function API.GetScriptableSystem(name)
    __gcetSyncSharedProviderEpoch()
    local key = tostring(name or "")
    if key == "" then return nil end
    local value = __gcetScriptableSystemValues[key]
    if value ~= nil then return value end
    local container = API.GetScriptableSystemsContainer()
    if container == nil then return nil end
    local ok, resolved = pcall(function() return container:Get(CName.new(key)) end)
    if ok and resolved ~= nil then
        __gcetScriptableSystemValues[key] = resolved
        return resolved
    end
    return nil
end
""").Replace("API",api,StringComparison.Ordinal);
    private static string ProviderNamedCurrent(string api)=>ProviderNamedOld(api)
        .Replace("local container = "+api+".GetScriptableSystemsContainer()",
            "local values = __gcetScriptableSystemValues\n    local container = "+api+".GetScriptableSystemsContainer()",StringComparison.Ordinal)
        .Replace("__gcetScriptableSystemValues[key] = resolved",
            "if values == __gcetScriptableSystemValues then values[key] = resolved end",StringComparison.Ordinal);
    private static string ProviderReset(string api,bool both)=>NormalizeLf("""
function API.__gcetResetSharedProviderCaches()
    __gcetSharedProviderEpoch = nil
    __gcetSharedProviderValues = {}
    __gcetScriptableSystemValues = {}
ADAPTER
end
""").Replace("API",api,StringComparison.Ordinal).Replace("\nADAPTER",
        both?"\n    local api = Engine.GCET\n    if type(api) == \"table\" and type(api.__gcetResetSharedProviderCaches) == \"function\" then\n        api.__gcetResetSharedProviderCaches()\n    end":"",StringComparison.Ordinal);
    private const string ProviderSessionObserver = """
function Engine.__gcetObserveProviderSession(loaded)
    if type(loaded) ~= "boolean" or __gcetProviderSessionLoaded == loaded then return end
    __gcetProviderSessionLoaded = loaded
    Engine.__gcetResetSharedProviderCaches()
end
""";
    internal static bool TrySharedProviderLifecycle(string source,out string next)
    {
        next=source;
        var original=source;
        string Proof(string v)=>NormalizeLf(v).Replace("\r\n","\n",StringComparison.Ordinal);
        string Indent(string v,int n)=>string.Join("\n",NormalizeLf(v).Split('\n').Select(l=>new string(' ',n)+l));
        // Known root and adapter are generated by FixedZeroEngineRuntime.
        // Exact independent fragments; unknown custom cache bodies remain
        // unchanged. Scope is proved by the actual original host anchors.
        var hasAdapter=source.Contains("-- G-CET host compatibility bridge v1",StringComparison.Ordinal);
        var frame=Regex.Matches(source,@"(?m)^local function UpdateFrame\([A-Za-z_]\w*\)[ \t]*(?=\r*$)");
        var root=Regex.Matches(source,@"(?m)^local function __gcetGetSharedProvider\(key\)[ \t]*(?=\r*$)");
        var code=WithoutLuaTrivia(source);
        if(frame.Count!=1||root.Count!=1||frame[0].Index>=root[0].Index||
           Count(WithoutLuaTrivia(source),"local okLive, liveLoaded = pcall(GameSession.IsLoaded)")!=1||
           Count(WithoutLuaTrivia(source),"isSessionLoaded = liveLoaded")!=1||
           Count(WithoutLuaTrivia(source),"GameSession.On(function(state)")!=1||
           Count(WithoutLuaTrivia(source),"local loaded = state.isLoaded")!=1||
           Count(WithoutLuaTrivia(source),"local sessionState = GameSession.GetState()")!=1||
           Count(code,"DerivedState.Reset()")!=1||
           !Regex.IsMatch(code,@"local okLive, liveLoaded = pcall\(GameSession\.IsLoaded\)\s+if okLive then\s+isSessionLoaded = liveLoaded")||
           !Regex.IsMatch(code,@"GameSession\.On\(function\(state\)\s+local loaded = state\.isLoaded")||
           !Regex.IsMatch(code,@"Events\.PlayerInvalidated:subscribe\(function\(\)\s+DerivedState\.Reset\(\)"))
            return false;
        var candidate=source;
        bool Upgrade(string old,string current)
        {
            if(Count(Proof(candidate),NormalizeLf(current))==1)return true;
            return ReplaceUniqueLines(ref candidate,old,current);
        }
        bool EnsureAfter(string anchor,string fragment)
        {
            // Each owner fragment is independently recoverable. Verify its
            // exact adjacent placement instead of accepting a marker alone.
            var line=Line(anchor).Matches(candidate);
            if(line.Count!=1)return false;
            var expected=line[0].Groups["indent"].Value+NormalizeLf(fragment).Replace("\n","\n"+line[0].Groups["indent"].Value);
            var following=Proof(candidate[(line[0].Index+line[0].Length)..]);
            if(following.StartsWith("\n"+expected,StringComparison.Ordinal))return true;
            // The session observer itself owns one reset call. It is not
            // the independent invalidation callback's partial fragment.
            var other=Proof(candidate).Replace(NormalizeLf(ProviderSessionObserver),"",StringComparison.Ordinal);
            if(Count(other,expected)!=0)return false;
            return AfterLine(ref candidate,anchor,fragment);
        }
        if(!Upgrade(ProviderCacheOld,ProviderCacheCurrent)||
           !Upgrade(ProviderNamedOld("Engine"),ProviderNamedCurrent("Engine")))return false;
        if(hasAdapter&&(!Upgrade(Indent(ProviderCacheOld,8),Indent(ProviderCacheCurrent,8))||
                        !Upgrade(Indent(ProviderNamedOld("__gcetApi"),8),Indent(ProviderNamedCurrent("__gcetApi"),8))))return false;
        // Insert/reset beside its own generated cache function. The root
        // dispatch clears both known namespaces at the same transition.
        bool AfterBlock(string body,string addition)
        {
            var all=Proof(candidate);
            if(Count(all,NormalizeLf(body)+"\n\n"+NormalizeLf(addition))==1)
                return Count(all,NormalizeLf(addition))==1;
            if(Count(all,NormalizeLf(addition))!=0)return false;
            return ReplaceUniqueLines(ref candidate,body,body+"\n\n"+addition);
        }
        var rootReset=ProviderReset("Engine",hasAdapter);
        var alternateReset=ProviderReset("Engine",!hasAdapter);
        // An installed host can acquire or lose the known compatibility
        // adapter after an earlier generation. Upgrade the owned root
        // function in place, rather than let a duplicate shadow it.
        var rootDefinitions=Regex.Matches(WithoutLuaTrivia(candidate),@"\bfunction\s+Engine\.__gcetResetSharedProviderCaches\s*\(").Count;
        if(rootDefinitions>1)return false;
        if(rootDefinitions==1&&Count(Proof(candidate),rootReset)!=1)
        {
            if(Count(Proof(candidate),alternateReset)!=1||
                !ReplaceUniqueLines(ref candidate,alternateReset,rootReset))return false;
        }
        var observerDefinitions=Regex.Matches(WithoutLuaTrivia(candidate),@"\bfunction\s+Engine\.__gcetObserveProviderSession\s*\(").Count;
        if(observerDefinitions>1||(observerDefinitions==1&&Count(Proof(candidate),NormalizeLf(ProviderSessionObserver))!=1))return false;
        var adapterReset=Indent(ProviderReset("__gcetApi",false),8);
        var adapterDefinitions=Regex.Matches(WithoutLuaTrivia(candidate),@"\bfunction\s+__gcetApi\.__gcetResetSharedProviderCaches\s*\(").Count;
        if(adapterDefinitions>1||(adapterDefinitions==1&&(!hasAdapter||Count(Proof(candidate),adapterReset)!=1)))return false;
        if(!AfterBlock(ProviderCacheCurrent,rootReset)||
           !AfterBlock(rootReset,NormalizeLf(ProviderSessionObserver)))return false;
        if(hasAdapter&&!AfterBlock(Indent(ProviderCacheCurrent,8),adapterReset))return false;
        var frameAnchor=frame[0].Value;
        if(Count(candidate,ProviderSessionLocal)==0)
        {
            if(Regex.IsMatch(WithoutLuaTrivia(candidate),@"\blocal\s+__gcetProviderSessionLoaded\b")||
               !BeforeLine(ref candidate,frameAnchor,ProviderSessionLocal))return false;
        }
        else if(Count(candidate,ProviderSessionLocal)!=1||
            !Proof(candidate).Contains(ProviderSessionLocal+"\n"+frameAnchor,StringComparison.Ordinal))return false;
        if(!EnsureAfter("isSessionLoaded = liveLoaded",
                "if __gcetProviderSessionLoaded ~= liveLoaded then Engine.__gcetObserveProviderSession(liveLoaded) end")||
           !EnsureAfter("local loaded = state.isLoaded","Engine.__gcetObserveProviderSession(loaded)")||
           !EnsureAfter("local sessionState = GameSession.GetState()","Engine.__gcetObserveProviderSession(sessionState.isLoaded)")||
           !EnsureAfter("DerivedState.Reset()","Engine.__gcetResetSharedProviderCaches()"))return false;
        // Do not cache an old result into the NEW session on reentry. The
        // initiating caller still gets the original result (no replay).
        next=candidate;return true;
    }

    internal static int TryStageSharedProviderLifecycle(ref FixedZeroEngineBuild framework,List<object> manifest)
    {
        if(!framework.Files.TryGetValue(InitPath,out var bytes))return 0;
        var source=Encoding.UTF8.GetString(bytes);
        if(!TrySharedProviderLifecycle(source,out var next)||next==source)return 0;
        var files=framework.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        files[InitPath]=Encoding.UTF8.GetBytes(next);
        framework=framework with{Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[InitPath])).ToLowerInvariant()};
        manifest.Add(new{type="FRAMEWORK_SHARED_PROVIDER_SESSION_LIFETIME",Owner="0-Engine",file=InitPath,
            contract="GCET_SHARED_PROVIDER_SESSION_LIFETIME_V1",sourceProof=true,
            extraNativeReads=false,extraCallbacks=false,callbackOrCadenceChanged=false,
            invalidation="EXISTING_HOST_SESSION_AND_PLAYER_INVALIDATED",runtimePerformance="NOT_MEASURED"});
        return 1;
    }

    private const string SharedDerivedLocal =
        "local __gcetDerivedProviders = {} -- G-CET derived shared provider dependency v1";
    private const string SharedDerivedSetter = """
function DerivedState.__gcetSetSharedProviders(providers)
    __gcetDerivedProviders = providers or {}
end
""";
    private const string SharedDerivedHostBegin = "-- G-CET derived shared provider binding v1";
    private static IEnumerable<SharedProviderAuthorization> StableDerivedProviders =>
        SharedProviderCatalog.RuntimeProviders.Where(p=>p.Category=="SYSTEM_HANDLE");
    private static string SharedDerivedRead(string getter) =>
        "(__gcetDerivedProviders."+getter+" or Game."+getter+")()";
    // Normalize for EXISTING exact reader proofs only. Never strip authored
    // content from staged output; complete missing bridge pieces below.
    private static string DerivedSharedProof(string source)
    {
        if(!source.Contains(SharedDerivedLocal,StringComparison.Ordinal)&&
           !source.Contains(SharedDerivedSetter.Split('\n')[0].TrimEnd('\r'),StringComparison.Ordinal))
            return NormalizeLf(source);
        var proof=NormalizeLf(source);
        foreach(var p in StableDerivedProviders)
            proof=proof.Replace(SharedDerivedRead(p.Getter),"Game."+p.Getter+"()",StringComparison.Ordinal);
        return proof;
    }
    private static bool RootProviderProven(string host,SharedProviderAuthorization p)
    {
        var text=NormalizeLf(host);
        // Existing fixed-runtime source owns stable system lifetime. Do not
        // use an arbitrary similarly named function or compatibility cache.
        const string epoch = """
local function __gcetSyncSharedProviderEpoch()
    local player = GetPlayer()
    if __gcetSharedProviderEpoch ~= player then
        __gcetSharedProviderEpoch = player
        __gcetSharedProviderValues = {}
        __gcetScriptableSystemValues = {}
    end
end
""";
        const string cache = """
local function __gcetGetSharedProvider(key)
    __gcetSyncSharedProviderEpoch()
    local value = __gcetSharedProviderValues[key]
    if value == nil then
        local getter = __gcetSharedProviderGetters[key]
        if getter ~= nil then
            value = getter()
            if value ~= nil then
                __gcetSharedProviderValues[key] = value
            end
        end
    end
    return value
end
""";
        // Allow trivia/whitespace-equivalent producer methods; other actual
        // cache shapes remain untouched until independently proven.
        var body="function Engine."+p.Getter+"()\n    return __gcetGetSharedProvider(\""+p.Provider+"\")\nend";
        var getter=p.Provider+" = function() return Game."+p.Getter+"() end,";
        if(Count(text,NormalizeLf(epoch))!=1||(Count(text,NormalizeLf(cache))!=1&&Count(text,NormalizeLf(ProviderCacheCurrent))!=1)||
            Count(text,body)!=1||Count(text,"    "+getter)!=1||
            Count(text,"local function GetPlayer()\n    return State.player()\nend")!=1)
            return false;
        var code=WithoutLuaTrivia(host);
        return Regex.Matches(code,@"\bEngine\s*\.\s*"+Regex.Escape(p.Getter)+@"\s*(?:=|\(\s*\))").Count>=1&&
            Regex.Matches(code,@"\bfunction\s+Engine\s*\.\s*"+Regex.Escape(p.Getter)+@"\s*\(").Count==1&&
            !Regex.IsMatch(code,@"\bEngine\s*\.\s*"+Regex.Escape(p.Getter)+@"\s*=");
    }

    internal static bool TrySharedDerivedProviders(string module,string host,out string moduleNext,
        out string hostNext,out string[] getters)
    {
        moduleNext=module;hostNext=host;getters=[];
        var moduleCode=WithoutLuaTrivia(module);
        // Own the actual module export and existing host initialization, not
        // a version or a fixed mod list. Retain author functions and timing.
        if(Regex.Matches(moduleCode,@"\blocal\s+DerivedState\s*=\s*\{").Count!=1||
           Count(moduleCode,"function DerivedState.Init(events)")!=1||
           Count(moduleCode,"return DerivedState")!=1||
           Regex.IsMatch(moduleCode,@"\b(?:local\s+Game\b|Game\s*=|function\s+\w+\s*\([^)]*\bGame\b)")||
           Regex.IsMatch(moduleCode,@"\bGame\s*(?:\[|$)")||
           Regex.IsMatch(moduleCode,@"\bGame\s*\.\s*\w+\s*=")||
           Count(WithoutLuaTrivia(host),"DerivedState.Init(Events)")!=1)
            return false;
        var m=module;var h=host;
        var hasLocal=Count(m,SharedDerivedLocal)==1;
        var hasSetter=Count(NormalizeLf(m),NormalizeLf(SharedDerivedSetter))==1;
        if(Count(m,SharedDerivedLocal)>1||Count(m,"function DerivedState.__gcetSetSharedProviders(")!=(hasSetter?1:0))
            return false;
        // Recognize exactly our own prior expressions. Reject unknown alias
        // uses, name collisions or manually altered setter semantics.
        var residual=NormalizeLf(m);
        if(hasLocal)residual=residual.Replace(SharedDerivedLocal,"",StringComparison.Ordinal);
        if(hasSetter)residual=residual.Replace(NormalizeLf(SharedDerivedSetter),"",StringComparison.Ordinal);
        foreach(var p in StableDerivedProviders)
            residual=residual.Replace(SharedDerivedRead(p.Getter),"Game."+p.Getter+"()",StringComparison.Ordinal);
        if(Regex.IsMatch(WithoutLuaTrivia(residual),@"\b__gcetDerivedProviders\b|\b__gcetSetSharedProviders\b"))
            return false;
        var selected=new List<string>();
        foreach(var p in StableDerivedProviders)
        {
            if(!RootProviderProven(host,p))continue;
            // Rewrite only direct single local assignment statements. The
            // receiver-consuming method and its original pcall stay intact.
            var pat=@"(?m)^(?<prefix>[ \t]*local[ \t]+[A-Za-z_][A-Za-z0-9_]*[ \t]*=[ \t]*)Game\s*\.\s*"+
                Regex.Escape(p.Getter)+@"\s*\(\s*\)(?<end>[ \t]*\r?$)";
            var re=new Regex(pat,RegexOptions.CultureInvariant);
            // Raw/source and lexical counts must agree; never rewrite a
            // string literal, long comment or non-code lookalike.
            var raw=re.Matches(m).Count;
            if(raw!=re.Matches(WithoutLuaTrivia(m)).Count)continue;
            var present=Count(WithoutLuaTrivia(m),SharedDerivedRead(p.Getter));
            if(raw==0&&present==0)continue;
            m=re.Replace(m,x=>x.Groups["prefix"].Value+SharedDerivedRead(p.Getter)+x.Groups["end"].Value);
            selected.Add(p.Getter);
        }
        if(selected.Count==0)return false;
        if(!hasLocal)
        {
            var declaration=Regex.Matches(m,@"(?m)^local[ \t]+DerivedState[ \t]*=[ \t]*\{[^\r\n]*\}[ \t]*(?=\r?$)");
            if(declaration.Count!=1||!AfterLine(ref m,declaration[0].Value,SharedDerivedLocal))return false;
        }
        if(!hasSetter&&!BeforeLine(ref m,"return DerivedState",SharedDerivedSetter))return false;
        // A partially applied/older bridge is rebuilt only from the exact
        // owned syntactic shape, never from an author-defined setter.
        var begin=Regex.Escape(SharedDerivedHostBegin);
        var bridgePattern=@"(?m)^[ \t]*"+begin+@"\r?\n[ \t]*if type\(DerivedState\.__gcetSetSharedProviders\) == ""function"" then\r?\n[ \t]*DerivedState\.__gcetSetSharedProviders\(\{\r?\n(?:[ \t]*Get[A-Za-z0-9_]+ = Engine\.Get[A-Za-z0-9_]+,\r?\n)+[ \t]*\}\)\r?\n[ \t]*end[ \t]*(?:\r?\n|$)";
        var bridges=Regex.Matches(h,bridgePattern);
        if(bridges.Count>1||Count(h,SharedDerivedHostBegin)!=bridges.Count)return false;
        if(bridges.Count==1)
        {
            var owned=bridges[0];
            foreach(Match entry in Regex.Matches(owned.Value,@"\b(Get[A-Za-z0-9_]+) = Engine\.(Get[A-Za-z0-9_]+),"))
                if(entry.Groups[1].Value!=entry.Groups[2].Value||
                   !StableDerivedProviders.Any(p=>p.Getter==entry.Groups[1].Value))return false;
            h=h.Remove(owned.Index,owned.Length);
        }
        if(Regex.IsMatch(WithoutLuaTrivia(h),@"\b__gcetSetSharedProviders\b"))return false;
        var binding=SharedDerivedHostBegin+"\nif type(DerivedState.__gcetSetSharedProviders) == \"function\" then\n"+
            "    DerivedState.__gcetSetSharedProviders({\n"+
            string.Join("\n",selected.OrderBy(x=>x,StringComparer.Ordinal).Select(x=>"        "+x+" = Engine."+x+","))+
            "\n    })\nend";
        if(!AfterLine(ref h,"DerivedState.Init(Events)",binding))return false;
        moduleNext=m;hostNext=h;getters=selected.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        return true;
    }

    internal static int TryStageSharedDerivedProviders(string modsRoot,ref FixedZeroEngineBuild framework,
        Dictionary<string,byte[]> staged,Dictionary<string,int> counts,List<object> manifest)
    {
        const string relative="0-Engine/modules/DerivedState.lua";
        var path=Path.Combine(modsRoot,"0-Engine","modules","DerivedState.lua");
        if(!File.Exists(path)||!framework.Files.TryGetValue(InitPath,out var hb))return 0;
        var module=Encoding.UTF8.GetString(staged.TryGetValue(relative,out var pb)?pb:File.ReadAllBytes(path));
        var host=Encoding.UTF8.GetString(hb);
        if(!TrySharedDerivedProviders(module,host,out var mn,out var hn,out var getters)||
           (module==mn&&host==hn))return 0;
        if(module!=mn){
            staged[relative]=Encoding.UTF8.GetBytes(mn);
            counts[relative]=counts.GetValueOrDefault(relative)+1;
        }
        var files=framework.Files.ToDictionary(x=>x.Key,x=>x.Value,StringComparer.OrdinalIgnoreCase);
        files[InitPath]=Encoding.UTF8.GetBytes(hn);
        framework=framework with{Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[InitPath])).ToLowerInvariant()};
        manifest.Add(new{type="FRAMEWORK_SHARED_PROVIDER_DEPENDENCY",Owner="0-Engine",file=relative,
            contract="GCET_SHARED_DERIVED_SYSTEM_HANDLES_V1",getters,sourceProof=true,
            callbackOrCadenceChanged=false,mutableStateReadsChanged=false,
            providerOwner="0-Engine:existing-root-provider",nativeReadSavings="RUNTIME_MEASUREMENT_PENDING"});
        return 1;
    }

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

    // Author Cron remains the timing/ordering authority. A single canonical
    // Scheduler lane tracks whether its existing update has active work or
    // pending prune cleanup. No second clock, observer or timer dispatcher.
    internal const string CronRelative = "0-Engine/external/Cron.lua";
    private const string CronDeclareStock = """
local prune = false
""";
    private const string CronDeclareTracked = """
local prune = false
-- G-CET Cron active work demand v1
local __gcetCronActive, __gcetCronHandle = 0, nil
local __gcetCronOk, __gcetCronScheduler = pcall(require, 'modules/G-CET/Scheduler')
local __gcetCronDemand = __gcetCronOk and type(__gcetCronScheduler) == "table" and __gcetCronScheduler.Demand or nil
if type(__gcetCronDemand) ~= "table" or type(__gcetCronDemand.TrackFrame) ~= "function" or type(__gcetCronDemand.IsDue) ~= "function" then
    __gcetCronDemand = nil
end
local function __gcetCronSync()
    if not __gcetCronDemand then return end
    if __gcetCronActive > 0 or prune then
        if not __gcetCronHandle then __gcetCronHandle = __gcetCronDemand.TrackFrame("Cron", 1) end
    elseif __gcetCronHandle then
        __gcetCronHandle.Cancel()
        __gcetCronHandle = nil
    end
end
""";
    private const string CronInsertStock = """
    table.insert(timers, timer)
""";
    private const string CronInsertTracked = """
    table.insert(timers, timer)
    __gcetCronActive = __gcetCronActive + 1
    __gcetCronSync()
""";
    private const string CronHaltStock = """
            timer.active = false
            timer.halted = true
            prune = true
            break
""";
    private const string CronHaltTracked = """
            if timer.active then __gcetCronActive = __gcetCronActive - 1 end
            timer.active = false
            timer.halted = true
            prune = true
            __gcetCronSync()
            break
""";
    private const string CronPauseStock = """
            if not timer.halted then
                timer.active = false
            end
""";
    private const string CronPauseTracked = """
            if not timer.halted then
                if timer.active then __gcetCronActive = __gcetCronActive - 1 end
                timer.active = false
                __gcetCronSync()
            end
""";
    private const string CronResumeStock = """
            if not timer.halted then
                timer.active = true
            end
""";
    private const string CronResumeTracked = """
            if not timer.halted then
                if not timer.active then __gcetCronActive = __gcetCronActive + 1 end
                timer.active = true
                __gcetCronSync()
            end
""";
    private const string CronOneShotStock = """
                    timer.active = false
                    timer.halted = true
                    prune = true
""";
    private const string CronOneShotTracked = """
                    __gcetCronActive = __gcetCronActive - 1
                    timer.active = false
                    timer.halted = true
                    prune = true
""";
    private const string CronPruneStock = """
        for i = #timers, 1, -1 do
            if timers[i].halted then
                table.remove(timers, i)
            end
        end
""";
    private const string CronPruneTracked = """
        for i = #timers, 1, -1 do
            if timers[i].halted then
                table.remove(timers, i)
            end
        end
        __gcetCronSync()
""";
    private const string CronExportStock = """
return Cron
""";
    private const string CronExportTracked = """
Cron.__gcetDemand = __gcetCronDemand
Cron.__gcetUpdate = Cron.Update
return Cron
""";
    private const string CronAddBody = """
local function addTimer(timeout, recurring, callback, args)
    if type(timeout) ~= 'number' then
        return
    end
    if timeout < 0 then
        return
    end
    if type(recurring) ~= 'boolean' then
        return
    end
    if type(callback) ~= 'function' then
        if type(args) == 'function' then
            callback, args = args, callback
        else
            return
        end
    end
    if type(args) ~= 'table' then
        args = { arg = args }
    end
    counter = counter + 1
    local timer = {
        id = counter,
        callback = callback,
        recurring = recurring,
        timeout = timeout,
        active = true,
        halted = false,
        delay = timeout,
        args = args,
    }
    if args.id == nil then
        args.id = timer.id
    end
    if args.interval == nil then
        args.interval = timer.timeout
    end
    if args.Halt == nil then
        args.Halt = Cron.Halt
    end
    if args.Pause == nil then
        args.Pause = Cron.Pause
    end
    if args.Resume == nil then
        args.Resume = Cron.Resume
    end
    table.insert(timers, timer)
    return timer.id
end
""";
    private const string CronHaltBody = """
function Cron.Halt(timerId)
    if type(timerId) == 'table' then
        timerId = timerId.id
    end
    for _, timer in ipairs(timers) do
        if timer.id == timerId then
            timer.active = false
            timer.halted = true
            prune = true
            break
        end
    end
end
""";
    private const string CronPauseBody = """
function Cron.Pause(timerId)
    if type(timerId) == 'table' then
        timerId = timerId.id
    end
    for _, timer in ipairs(timers) do
        if timer.id == timerId then
            if not timer.halted then
                timer.active = false
            end
            break
        end
    end
end
""";
    private const string CronResumeBody = """
function Cron.Resume(timerId)
    if type(timerId) == 'table' then
        timerId = timerId.id
    end
    for _, timer in ipairs(timers) do
        if timer.id == timerId then
            if not timer.halted then
                timer.active = true
            end
            break
        end
    end
end
""";
    private const string CronUpdateBody = """
function Cron.Update(delta)
    if #timers == 0 then
        return
    end
    for _, timer in ipairs(timers) do
        if timer.active then
            timer.delay = timer.delay - delta
            if timer.delay <= 0 then
                if timer.recurring then
                    timer.delay = timer.delay + timer.timeout
                else
                    timer.active = false
                    timer.halted = true
                    prune = true
                end
                timer.callback(timer.args)
            end
        end
    end
    if prune then
        prune = false
        for i = #timers, 1, -1 do
            if timers[i].halted then
                table.remove(timers, i)
            end
        end
    end
end
""";

    internal static bool TryCronProducer(string source,out string next)
    {
        next=source;
        var candidate=source;
        var proof=source;
        // Each fragment is independently composable. Normalize only the
        // proven fragments for ownership checks; preserve author bytes.
        foreach(var pair in new[]{
            (CronDeclareStock,CronDeclareTracked),(CronInsertStock,CronInsertTracked),
            (CronHaltStock,CronHaltTracked),(CronPauseStock,CronPauseTracked),
            (CronResumeStock,CronResumeTracked),(CronOneShotStock,CronOneShotTracked),
            (CronPruneStock,CronPruneTracked),(CronExportStock,CronExportTracked)})
        {
            var normalized=Regex.Replace(candidate,@"\r+\n","\n");
            if(Count(normalized,NormalizeLf(pair.Item2))==1)
            {
                if(!ReplaceUniqueLines(ref proof,pair.Item2,pair.Item1))return false;
            }
            else if(!ReplaceUniqueLines(ref candidate,pair.Item1,pair.Item2))return false;
        }
        var code=WithoutLuaTrivia(proof);
        if(Regex.IsMatch(code,@"\b__gcetCron\w*|\bCron\.__gcet(?:Demand|Update)\b")||
            Regex.Matches(code,@"\btimers\b").Count!=10||
            Regex.Matches(code,@"\bprune\b").Count!=5||
            Count(code,"local timers = {}")!=1||
            Count(code,"local prune = false")!=1||
            Regex.Matches(code,@"\bCron\s*\.\s*Update\b").Count!=1)
            return false;
        foreach(var body in new[]{CronAddBody,CronHaltBody,CronPauseBody,CronResumeBody,CronUpdateBody})
        {
            var signature=NormalizeLf(body).Split('\n')[0];
            var actual=Regex.Matches(proof,@"(?m)^"+Regex.Escape(signature)+
                @"[ \t]*\r*\n[\s\S]*?^end[ \t]*(?=\r*$)");
            if(actual.Count!=1||ProofTokens(actual[0].Value)!=ProofTokens(body))return false;
        }
        next=candidate;return true;
    }

    private const string CronHostStock="pcall(Cron.Update, delta)";
    private const string CronHostTracked = """
if not __gcetDemand or Cron.__gcetDemand ~= __gcetDemand or Cron.__gcetUpdate ~= Cron.Update
    or __gcetDemand.IsDue("Cron", currentFrame) then
    pcall(Cron.Update, delta)
end
""";
    internal static bool TryCronHost(string source,out string next)
    {
        next=source;
        var code=WithoutLuaTrivia(source);
        if(Count(code,CronHostStock)!=1||
            Count(code,"local function UpdateFrame(delta)")!=1||
            Count(code,"local currentFrame = 0")!=1||
            Regex.Matches(source,@"(?m)^local Cron\s*=\s*require\(['""]external/Cron['""]\)\s*(?=\r*$)").Count!=1)
            return false;
        var candidate=source;
        if(Count(candidate,FrameDemandBind)==0)
        {
            if(candidate.Contains("__gcetDemand",StringComparison.Ordinal)||
                !BeforeLine(ref candidate,"local function UpdateFrame(delta)",FrameDemandBind))return false;
        }
        if(Count(candidate,FrameDemandBind)!=1)return false;
        var tracked="    "+NormalizeLf(CronHostTracked).Replace("\n","\n    ",StringComparison.Ordinal);
        if(Count(Regex.Replace(candidate,@"\r+\n","\n"),tracked)==1)
        {
            next=candidate;return true;
        }
        if(Count(code,"Cron.__gcetDemand")!=0||Count(code,"Cron.__gcetUpdate")!=0)return false;
        if(!ReplaceUniqueLines(ref candidate,"    "+CronHostStock,tracked))return false;
        next=candidate;return true;
    }

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
        // Retire the old historical sample layer at its original generator.
        // Stock historical fields already execute the cheapest correct path.
        const string derivedRelative = "0-Engine/modules/DerivedState.lua";
        var derivedLive = Path.Combine(modsRoot, "0-Engine", "modules", "DerivedState.lua");
        if (File.Exists(derivedLive))
        {
            var bytes = staged.TryGetValue(derivedRelative, out var pending)
                ? pending : File.ReadAllBytes(derivedLive);
            var text = Encoding.UTF8.GetString(bytes);
            if(TryRetireHistoricalModule(text,out var producer)&&TryRetireHistoricalHost(host,out var hostNext))
            {
                if(producer!=text||hostNext!=host)
                {
                    compatible++;applied++;
                    if(producer!=text)
                    {
                        staged[derivedRelative]=Encoding.UTF8.GetBytes(producer);
                        counts[derivedRelative]=counts.GetValueOrDefault(derivedRelative)+1;
                    }
                    host=hostNext;
                    manifest.Add(new {type="FRAMEWORK_HISTORICAL_SAMPLE_RETIREMENT",Owner="0-Engine",
                        file=derivedRelative,sourceProof=true,callbackOrCadenceChanged=false,
                        nativeReadCountChanged=false,policy="ORIGINAL_PHASE_DIRECT_ASSIGNMENT"});
                }
            }
            else
                skipped.Add(new {type="SEMANTIC_RULE",Owner="0-Engine",
                    RuleId="0engine-demand-derived-scalar-sample",
                    reason="Unknown historical sample ownership; retain existing module and host."});
        }
        var cronLive=Path.Combine(modsRoot,"0-Engine","external","Cron.lua");
        if(File.Exists(cronLive))
        {
            var bytes=staged.TryGetValue(CronRelative,out var cronPending)?cronPending:File.ReadAllBytes(cronLive);
            var cron=Encoding.UTF8.GetString(bytes);
            if(TryCronProducer(cron,out var cronNext)&&TryCronHost(host,out var cronHost))
            {
                if(cronNext!=cron||cronHost!=host||schedulerText!=Encoding.UTF8.GetString(scheduler))
                {
                    compatible++;applied++;
                    if(cronNext!=cron)
                    {
                        staged[CronRelative]=Encoding.UTF8.GetBytes(cronNext);
                        counts[CronRelative]=counts.GetValueOrDefault(CronRelative)+1;
                    }
                    host=cronHost;
                    manifest.Add(new{type="FRAMEWORK_CRON_DEMAND",Owner="0-Engine",file=CronRelative,
                        contract="GCET_CRON_ACTIVE_WORK_V1",sourceProof=true,
                        callbackOrCadenceChanged=false,policy="CANONICAL_SCHEDULER_ACTIVE_TIMER_AND_PRUNE"});
                }
            }
            else skipped.Add(new{type="SEMANTIC_RULE",Owner="0-Engine",
                RuleId="0engine-cron-active-demand",reason="Timer registry mutation or host update not proven; retain author update."});
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
        if lane.periods[1] and frame % 1 == 0 then return true end
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
