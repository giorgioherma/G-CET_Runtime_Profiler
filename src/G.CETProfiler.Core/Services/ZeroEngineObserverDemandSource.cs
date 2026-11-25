using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Only a source-owned, pure Before ActionRouter bridge is eligible.
// Native membership is verified again at the actual callback invocation.
// Mixed host callbacks, unknown counter mutations and old DLLs stay stock.
internal static class ZeroEngineObserverDemandSource
{
    internal const string RouterPath = "0-Engine/modules/G-CET/ActionRouter.lua";
    internal const string Marker = "-- G-CET native observer demand v1";
    private const string Own = "__gcetOwnBeforeObserver";
    private const string Enter = "__gcetObserverEnter";
    private const string Change = "__gcetObserverChanged";
    private static readonly Regex Lexer = new(
        """(?<comment>--\[(?<ce>=*)\[.*?\]\k<ce>\]|--[^\r\n]*)|(?<long>\[(?<le>=*)\[.*?\]\k<le>\])|(?<quoted>"(?:\\[\s\S]|[^"\\])*"|'(?:\\[\s\S]|[^'\\])*')|[A-Za-z_][A-Za-z0-9_]*|\S""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static Match[] Tokens(string s) => Lexer.Matches(s).Cast<Match>()
        .Where(m => !m.Groups["comment"].Success).ToArray();
    private static string Value(Match m) => m.Groups["quoted"].Success &&
        !m.Value.Contains('\\') ? m.Value[1..^1] : m.Value;
    private static bool IsIdentifier(string s) =>
        Regex.IsMatch(s, @"\A[A-Za-z_][A-Za-z0-9_]*\z", RegexOptions.CultureInvariant);
    private static bool At(Match[] ts, int i, params string[] values) =>
        i >= 0 && i + values.Length <= ts.Length &&
        ts.Skip(i).Take(values.Length).Select(Value).SequenceEqual(values);
    private static int Count(string s, string value) =>
        (s.Length - s.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;
    private static string NL(string s, string text) => s.Contains("\r\n", StringComparison.Ordinal)
        ? text.Replace("\r\n","\n",StringComparison.Ordinal).Replace("\n","\r\n",StringComparison.Ordinal)
        : text.Replace("\r\n","\n",StringComparison.Ordinal);
    private static bool BeforeLine(ref string s, string line, string body)
    {
        var matches = Regex.Matches(s, @"(?m)^(?<indent>[ \t]*)"+Regex.Escape(line)+@"[ \t]*(?=\r?$)");
        if (matches.Count != 1) return false;
        var m = matches[0]; var indent = m.Groups["indent"].Value;
        s = s.Insert(m.Index, NL(s, indent + body.Replace("\n","\n"+indent,StringComparison.Ordinal)+"\n"));
        return true;
    }

    // Recognize a literal, sole host registration and the exact pure
    // dispatch body. Do not combine PlayerAction or reorder other observers.
    internal static bool TryHost(string source, out string next)
    {
        next = source;
        var t = Tokens(source);
        var registrations = Enumerable.Range(0,t.Length).Where(i =>
            (t[i].Value is "Observe" or "ObserveBefore" or "ObserveAfter" or "Override") &&
            At(t,i+1,"(","PlayerPuppet",",","OnAction",",")).ToArray();
        if (registrations.Length != 1) return false;
        int start = registrations[0], i = start;
        if (t[i].Value != "Observe" || !At(t,i+6,"function","(") ||
            i+15 >= t.Length || (i>0 && t[i-1].Value is "." or ":")) return false;
        string a=t[i+8].Value,b=t[i+10].Value,c=t[i+12].Value;
        if (!IsIdentifier(a)||!IsIdentifier(b)||!IsIdentifier(c)||
            !At(t,i+9,",")||!At(t,i+11,",")||!At(t,i+13,")")) return false;
        int body = i+14;
        string router;
        bool enter = At(t,body,"if","type","(");
        if (enter)
        {
            if (body+3>=t.Length) return false;
            router=t[body+3].Value;
            var shape=new[]{"if","type","(",router,".",Enter,")","=","=","function",
                "then",router,".",Enter,"(",")","end"};
            if (!At(t,body,shape)) return false;
            body += shape.Length;
        }
        else router=t[body].Value;
        if (!IsIdentifier(router)||!At(t,body,router,".","Dispatch","(",a,",",b,",",c,")","end",")"))
            return false;
        int close=body+11;
        // A compatibility adapter may delegate to a foreign host's own
        // SubscribeAction before using our router. A matching wrapper alone
        // is not a producer/consumer ownership proof. Leave that additive
        // bridge and its author-owned support modules unchanged.
        const string module="modules/G-CET/ActionRouter";
        bool direct = Enumerable.Range(0,t.Length).Any(j =>
            At(t,j,"local",router,"=","require","(",module,")"));
        if (!direct) return false;
        // Do not insert a statement into an expression or before trailing
        // executable code on the registration's closing line.
        var openLine=source.LastIndexOf('\n',t[start].Index)+1;
        if (!string.IsNullOrWhiteSpace(source[openLine..t[start].Index])) return false;
        int closeEnd=t[close].Index+t[close].Length;
        int lineEnd=source.IndexOf('\n',closeEnd); if(lineEnd<0)lineEnd=source.Length;
        if (!Regex.IsMatch(source[closeEnd..lineEnd],@"\A[ \t\r]*(?:--[^\r\n]*)?\z"))return false;
        var ownShape=new[]{"if","type","(",router,".",Own,")","=","=","function",
            "then",router,".",Own,"(",")","end"};
        bool own=At(t,close+1,ownShape);
        if(Count(source,Enter)!=(enter?2:0)||Count(source,Own)!=(own?2:0))return false;
        var result=source;
        if(!own)
        {
            string indent=source[openLine..t[start].Index];
            // Insert before newline, but after an existing trailing comment.
            int end=lineEnd; if(end>0&&source[end-1]=='\r')end--;
            result=result.Insert(end,NL(source,"\n"+indent+
                $"if type({router}.{Own}) == \"function\" then {router}.{Own}() end"));
        }
        if(!enter)
        {
            int dispatch=t[body].Index, line=source.LastIndexOf('\n',dispatch)+1;
            string indent=source[line..dispatch];
            if(!string.IsNullOrWhiteSpace(indent))return false;
            result=result.Insert(line,NL(source,indent+
                $"if type({router}.{Enter}) == \"function\" then {router}.{Enter}() end\n"));
        }
        next=result;return true;
    }

    internal static bool TryRouter(string source, out string next)
    {
        next=source;
        var tokens=Tokens(source);
        // Count every occurrence of an assignment to the private counter.
        // These are its initializer and the three original ownership
        // mutations. Unknown writers/exports cannot safely mirror demand.
        // GetInfo exports the count as a named field, not a write to the
        // private local. Exclude only that exact read-only field shape.
        var infoFields=Enumerable.Range(0,tokens.Length).Where(i =>
            At(tokens,i,"beforeActive","=","beforeActive",",","afterActive","=","afterActive",",")).ToArray();
        if(infoFields.Length!=1)return false;
        var assignments=Enumerable.Range(0,tokens.Length).Count(i =>
            At(tokens,i,"beforeActive","=") && !At(tokens,i,"beforeActive","=","=") && i!=infoFields[0]);
        var dispatch=Enumerable.Range(0,tokens.Length).Where(i=>At(tokens,i,"function","ActionRouter",".","Dispatch","(","this",",","action",",","consumer",")","if","beforeActive","=","=","0","then","return","end")).ToArray();
        if(dispatch.Length!=1||assignments!=4||
           Count(source,"local beforeActive = 0")!=1||
           Count(source,"else beforeActive = beforeActive + 1 end")!=1||
           Count(source,"else beforeActive = beforeActive - 1 end")!=1||
           Count(source,"else beforeActive = beforeActive + change end")!=1||
           Count(source,"if beforeActive == 0 then return end")!=1||
           Count(source,"function ActionRouter.Subscribe(config, fn, owner)")!=1||
           Count(source,"function ActionRouter.Dispatch(this, action, consumer)")!=1)
            return false;
        var candidate=source;
        bool helper=source.Contains(Marker,StringComparison.Ordinal);
        if(helper)
        {
            if(Count(source,RouterHelper.Replace("\r\n","\n",StringComparison.Ordinal))!=1 &&
               Count(source,NL(source,RouterHelper))!=1)return false;
        }
        else
        {
            if(Count(source,Own)>0||Count(source,Enter)>0||
               !BeforeLine(ref candidate,"function ActionRouter.Subscribe(config, fn, owner)",RouterHelper))
                return false;
        }
        // Every mutation/initial dormant registration goes through the
        // already maintained count. After-phase never changes this demand.
        const string hook="if phase == \"before\" then "+Change+"() end";
        var anchors=new[]{"local exactRoute = phase == \"after\" and afterExact or exact",
            "entry.cancelled = true","else beforeActive = beforeActive + change end"};
        foreach(var anchor in anchors)
        {
            var pattern=@"(?m)^(?<indent>[ \t]*)"+Regex.Escape(anchor)+@"[ \t]*(?=\r?$)";
            var matches=Regex.Matches(candidate,pattern);
            if(matches.Count!=1)return false;
            var m=matches[0];var indent=m.Groups["indent"].Value;
            int pos=anchor.StartsWith("else ",StringComparison.Ordinal)?m.Index+m.Length:m.Index;
            // SetActive's conditional closes on the anchor, so inject after.
            string addition=anchor.StartsWith("else ",StringComparison.Ordinal)
                ? "\n"+indent+hook : indent+hook+"\n";
            string already=NL(candidate,addition);
            bool exists=anchor.StartsWith("else ",StringComparison.Ordinal)
                ? candidate.AsSpan(pos).StartsWith(already,StringComparison.Ordinal)
                : pos>=already.Length&&candidate.AsSpan(pos-already.Length,already.Length).SequenceEqual(already.AsSpan());
            if(!exists)candidate=candidate.Insert(pos,already);
        }
        if(Count(candidate,hook)!=3)return false;
        next=candidate;return true;
    }

    private static bool InsertScheduler(ref string source)
    {
        var matches=Regex.Matches(source,@"(?m)^[ \t]*Scheduler\.Demand = demand[ \t]*(?=\r?$)");
        if(matches.Count!=1)return false;
        source=source.Insert(matches[0].Index,NL(source,SchedulerHelper+"\n"));
        return true;
    }

    internal static bool TryScheduler(string source,out string next)
    {
        next=source;
        if(!ZeroEngineDemandSource.TryScheduler(source,out var candidate))return false;
        if(candidate.Contains(Marker,StringComparison.Ordinal))
        {
            if(Count(candidate,NL(candidate,SchedulerHelper))!=1)
            {
                var old=NL(candidate,LegacySchedulerHelper);
                if(Count(candidate,old)!=1)return false;
                candidate=candidate.Replace(old,NL(candidate,SchedulerHelper),StringComparison.Ordinal);
            }
        }
        else if(candidate.Contains("OwnObserver",StringComparison.Ordinal)||
                !InsertScheduler(ref candidate))
            return false;
        next=candidate;return true;
    }

    internal static int TryStage(ref FixedZeroEngineBuild framework,bool nativeAvailable,out bool integrated)
    {
        integrated=false;
        if(!nativeAvailable||
           !framework.Files.TryGetValue(ZeroEngineDemandSource.InitPath,out var hb)||
           !framework.Files.TryGetValue(RouterPath,out var rb)||
           !framework.Files.TryGetValue(ZeroEngineDemandSource.SchedulerPath,out var sb))return 0;
        var h=Encoding.UTF8.GetString(hb);var r=Encoding.UTF8.GetString(rb);var s=Encoding.UTF8.GetString(sb);
        if(!TryHost(h,out var hn)||!TryRouter(r,out var rn)||!TryScheduler(s,out var sn))return 0;
        var files=framework.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        files[ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(hn);
        files[RouterPath]=Encoding.UTF8.GetBytes(rn);
        files[ZeroEngineDemandSource.SchedulerPath]=Encoding.UTF8.GetBytes(sn);
        framework=framework with {Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[ZeroEngineDemandSource.InitPath])).ToLowerInvariant()};
        integrated=true;return h==hn&&r==rn&&s==sn?0:1;
    }


    private const string LegacyMarker="-- G-CET legacy PlayerAction demand v1";
    private const string LegacySubscribeLine="if eventName == \"PlayerAction\" then return __gcetLegacySubscribe(proxy, fn, source) end";
    private const string LegacyEnterLine="if __gcetLegacyEnter then __gcetLegacyEnter() end";
    private const string LegacyOwnLine="__gcetLegacyOwn()";
    private const string LegacySubscribeShape="""
function Engine.Subscribe(eventName, fn, source)
    local proxy = Events[eventName]
    if proxy then
        return proxy:subscribe(fn, source)
    else
        Logger.Log("0-Engine", "Unknown event: " .. tostring(eventName), "warn")
        return { unsubscribe = function() end }
    end
end
""";
    private const string LegacyObserveShape="""
Observe("PlayerPuppet", "OnAction", function(_, action)
    if not isPlaying or State.inMenu then return end
    Events.PlayerAction:trigger(action)
end)
""";
    private const string LegacyHelper="""
-- G-CET legacy PlayerAction demand v1
local __gcetLegacyLane, __gcetLegacyEnter
local function __gcetLegacyRefresh()
    if __gcetLegacyLane then
        -- next, not #, is deliberately conservative for a sparse listener
        -- array during an original reentrant unsubscribe.
        __gcetLegacyLane.SetActive(next(Events.PlayerAction.listeners) ~= nil)
    end
end
local function __gcetLegacySubscribe(proxy, fn, source)
    -- Wake BEFORE mutation: source labelling or a reentrant callback may
    -- enter CET before the original subscribe function returns.
    if __gcetLegacyLane and type(fn) == "function" then
        __gcetLegacyLane.SetActive(true)
    end
    local handle = proxy:subscribe(fn, source)
    local original = handle.unsubscribe
    handle.unsubscribe = function()
        original()
        __gcetLegacyRefresh()
    end
    __gcetLegacyRefresh()
    return handle
end
local function __gcetLegacyOwn()
    if __gcetLegacyLane then return end
    local ok, scheduler = pcall(require, "modules/G-CET/Scheduler")
    local demand = ok and type(scheduler) == "table" and scheduler.Demand
    local schedule = Engine.Schedule and Engine.Schedule.NextTick
    if type(demand) ~= "table" or type(demand.OwnObserver) ~= "function" or
       type(schedule) ~= "function" then return end
    local accepted, lane = pcall(demand.OwnObserver, "PlayerPuppet", "OnAction", function(fn)
        __gcetLegacyEnter = fn
    end, schedule)
    if accepted and lane then
        __gcetLegacyLane = lane
        __gcetLegacyRefresh()
    end
end
""";

    private static string[] Shape(string source) => Tokens(source).Select(Value).ToArray();
    private static int ShapeCount(Match[] tokens,string shape)
    {
        var pattern=Shape(shape);
        return Enumerable.Range(0,tokens.Length).Count(i=>At(tokens,i,pattern));
    }
    // These are the actual mutable emitter operations, not a file fingerprint.
    // Reject alternate return handles, raw exports or altered trigger semantics.
    internal static bool LegacyEmitterProof(string source)
    {
        var t=Tokens(source);
        const string create="""
function EventEmitter.new()
    local self = setmetatable({}, EventEmitter)
    self.listeners = {}
    self.listenerSources = {}
    self._firing = false
    self._dirty = false
    return self
end
""";
        const string subscribe="""
function EventEmitter:subscribe(callback, source)
    if type(callback) ~= "function" then
        Logger.Log("0-Engine", "EventEmitter: Attempted to subscribe with non-function", "warn")
        return { unsubscribe = function() end }
    end
    table.insert(self.listeners, callback)
    table.insert(self.listenerSources, source or getSourceLabel(callback))
    return {
        unsubscribe = function()
            for i, listener in ipairs(self.listeners) do
                if listener == callback then
                    if self._firing then
                        self.listeners[i] = nil
                        self.listenerSources[i] = nil
                        self._dirty = true
                    else
                        table.remove(self.listeners, i)
                        table.remove(self.listenerSources, i)
                    end
                    break
                end
            end
        end
    }
end
""";
        const string trigger="""
function EventEmitter:trigger(...)
    local listeners = self.listeners
    local n = #listeners
    if n == 0 then return end
    self._firing = true
    for i = 1, n do
        local listener = listeners[i]
        if listener then
            local success, err = pcall(listener, ...)
            if not success then
                Logger.Log("0-Engine", "EventEmitter: Error in listener: " .. tostring(err), "error")
            end
        end
    end
    self._firing = false
    if self._dirty then
        self._dirty = false
        local sources = self.listenerSources
        local j = 0
        for i = 1, #listeners do
            if listeners[i] then
                j = j + 1
                listeners[j] = listeners[i]
                sources[j] = sources[i]
            end
        end
        for i = j + 1, #listeners do
            listeners[i] = nil
            sources[i] = nil
        end
    end
end
""";
        const string count="function EventEmitter:getListenerCount() return #self.listeners end";
        const string sources="""
function EventEmitter:getListenerSources()
    local result = {}
    for i = 1, #self.listenerSources do
        result[i] = self.listenerSources[i]
    end
    return result
end
""";
        var known=new[]{create,subscribe,trigger,count,sources};
        if(known.Any(k=>ShapeCount(t,k)!=1))return false;
        foreach(var word in new[]{"listeners","listenerSources","_firing","_dirty"})
            if(t.Count(x=>x.Value==word)!=known.Sum(k=>Shape(k).Count(x=>x==word)))return false;
        // No extra methods replacing or returning the mutable listener table.
        return ShapeCount(t,"EventEmitter.__index = EventEmitter")==1 &&
            t.Count(x=>x.Value=="EventEmitter")==known.Sum(k=>Shape(k).Count(x=>x=="EventEmitter"))+4;
    }

    // Validate the existing Event bus recipients, rather than assuming that
    // an arbitrary module receiving the whole bus cannot mutate the emitter.
    internal static bool LegacyBusRecipientProof(string source,string module)
    {
        var t=Tokens(source);
        bool board=module=="BlackboardCache";
        string init=module=="Lifecycle"?"function Lifecycle.Init(events, state, logger) Events = events":
            "function "+module+".Init(events) Events = events";
        if(ShapeCount(t,init)!=1||ShapeCount(t,"local Events = nil")!=1||
           t.Count(x=>x.Value=="events")!=2)return false;
        const string dispatch="local function Trigger(eventName, value) if Events and Events[eventName] then Events[eventName]:trigger(value) end end";
        if(board)
            return ShapeCount(t,dispatch)==1&&t.Count(x=>x.Value=="Events")==5;
        var allowed=module=="Lifecycle"?new[]{"PlayerInvalidated","PlayerReady","PlayerRecreated"}:
            new[]{"CoverDirectionChanged","DistrictChanged"};
        for(int i=0;i<t.Length;++i)
        {
            if(t[i].Value!="Events")continue;
            if(At(t,i-1,"local","Events","=","nil")||At(t,i,"Events","=","events"))continue;
            if(allowed.Any(n=>At(t,i,"Events","and","Events",".",n,"then")))continue;
            if(i+2>=t.Length||!allowed.Contains(t[i+2].Value)||!At(t,i+1,"."))return false;
            var eventName=t[i+2].Value;
            if(At(t,i,"Events",".",eventName,":","trigger","("))continue;
            if(At(t,i,"Events","and","Events",".",eventName,"then"))continue;
            if(At(t,i-2,"Events","and","Events",".",eventName,"then"))continue;
            return false;
        }
        return true;
    }
    private static bool LegacyHostProof(string source)
    {
        var t=Tokens(source);
        if(ShapeCount(t,LegacySubscribeShape)!=1||ShapeCount(t,LegacyObserveShape)!=1||
           ShapeCount(t,"local Events = { PlayerAction = EventEmitter.new() ,")!=1||
           ShapeCount(t,"local EventEmitter = require('modules/EventEmitter')")!=1)return false;
        // Engine.Subscribe is the only dynamic event lookup. The diagnostic
        // enumeration may read counts/source labels but cannot expose the bus.
        const string list="for name, _ in pairs(Events) do table.insert(names, name) end";
        const string overlay="for name, emitter in pairs(Events) do local count = emitter:getListenerCount() totalSubs = totalSubs + count table.insert(names, { name = name, count = count, emitter = emitter }) end";
        if(ShapeCount(t,list)!=1||ShapeCount(t,overlay)!=1)return false;
        if(ShapeCount(t,"Lifecycle.Init(Events, State, Logger)")!=1||
           ShapeCount(t,"DerivedState.Init(Events)")!=1||ShapeCount(t,"BlackboardCache.Init(Events)")!=1)return false;
        for(int i=0;i<t.Length;++i)
        {
            if(t[i].Value=="EventEmitter" &&
               !At(t,i-1,"local","EventEmitter","=")&&!At(t,i,"EventEmitter",".","new","(",")"))return false;
            if(t[i].Value!="Events")continue;
            if(At(t,i-1,"local","Events","=","{")||At(t,i-3,"local","proxy","=","Events","[","eventName","]"))continue;
            if(At(t,i-3,"Lifecycle",".","Init","(","Events",",","State",",","Logger",")"))continue;
            if(At(t,i-4,"Lifecycle",".","Init","(","Events",",","State",",","Logger",")"))continue;
            if(At(t,i-4,"DerivedState",".","Init","(","Events",")")||
               At(t,i-4,"BlackboardCache",".","Init","(","Events",")"))continue;
            if(At(t,i-2,"pairs","(","Events",")"))continue;
            if(i+4<t.Length&&At(t,i+1,".")&&IsIdentifier(t[i+2].Value)&&
               (At(t,i+3,":","trigger","(")||At(t,i+3,":","subscribe","("))&&
               (t[i+2].Value!="PlayerAction"||At(t,i+3,":","trigger","(")))continue;
            return false;
        }
        // No direct second legacy trigger or assignment. Other OnAction
        // observers keep their original registration and are checked by the
        // actual native chain gate, not a static count of registration sites.
        return ShapeCount(t,"Events.PlayerAction:trigger(")==1&&
            ShapeCount(t,"entry.emitter:getListenerSources()")>=1&&
            Enumerable.Range(0,t.Length).Where(i=>t[i].Value=="emitter").All(i=>
                At(t,i-2,"for","name","emitter")||At(t,i-3,"for","name",",","emitter")||
                At(t,i-3,"for","interval",",","emitter")||
                At(t,i,"emitter",":","getListenerCount","(",")")||
                At(t,i,"emitter","=","emitter","}")||At(t,i-2,"emitter","=","emitter")||
                At(t,i-2,"entry",".","emitter",":","getListenerSources","(",")"));
    }
    // Remove exactly the owned generated fragments only in a proof copy.
    // All source edits are additive at original statement anchors.
    private static bool RemoveShape(ref string source,string shape,out bool found)
    {
        var t=Tokens(source);var pattern=Shape(shape);
        var hits=Enumerable.Range(0,t.Length).Where(i=>At(t,i,pattern)).ToArray();
        found=hits.Length==1;
        if(hits.Length>1)return false;
        if(!found)return true;
        int i=hits[0],start=t[i].Index,end=t[i+pattern.Length-1].Index+t[i+pattern.Length-1].Length;
        source=source.Remove(start,end-start);
        return true;
    }
    internal static bool TryLegacyHost(string source,out string next)
    {
        next=source;
        var proof=source;
        // Remove the full owned helper, preserving author source outside it.
        var helper=source.Contains(LegacyMarker,StringComparison.Ordinal);
        if(helper)
        {
            if(!RemoveShape(ref proof,LegacyHelper,out var found)||!found)return false;
        }
        if(!RemoveShape(ref proof,LegacySubscribeLine,out var sub)||
           !RemoveShape(ref proof,LegacyEnterLine,out var enter)||
           !RemoveShape(ref proof,LegacyOwnLine,out var own))return false;
        if(!helper&&(sub||enter||own))return false;
        if(!LegacyHostProof(proof))return false;
        var candidate=source;
        if(!helper&&!BeforeLine(ref candidate,"function Engine.Subscribe(eventName, fn, source)",LegacyHelper.Replace("\r\n","\n",StringComparison.Ordinal)))return false;
        if(!sub&&!BeforeLine(ref candidate,"return proxy:subscribe(fn, source)",LegacySubscribeLine))return false;
        if(!enter)
        {
            var t=Tokens(candidate);
            var shape=Shape(LegacyObserveShape);
            var hits=Enumerable.Range(0,t.Length).Where(i=>At(t,i,shape)).ToArray();
            if(hits.Length!=1)return false;
            var guard=hits[0]+Array.IndexOf(shape,"if");
            int line=candidate.LastIndexOf('\n',t[guard].Index)+1;
            var indent=candidate[line..t[guard].Index];
            if(!string.IsNullOrWhiteSpace(indent))return false;
            candidate=candidate.Insert(line,NL(candidate,indent+LegacyEnterLine+"\n"));
        }
        if(!own)
        {
            var t=Tokens(candidate);
            var dispatch=Shape("Events.PlayerAction:trigger(action) end)");
            var hits=Enumerable.Range(0,t.Length).Where(i=>At(t,i,dispatch)).ToArray();
            if(hits.Length!=1)return false;
            int close=hits[0]+dispatch.Length-1;
            int end=t[close].Index+t[close].Length;
            int lineEnd=candidate.IndexOf('\n',end);if(lineEnd<0)lineEnd=candidate.Length;
            if(!Regex.IsMatch(candidate[end..lineEnd],@"\A[ \t\r]*(?:--[^\r\n]*)?\z"))return false;
            int line=candidate.LastIndexOf('\n',t[close-1].Index)+1;
            var indent=candidate[line..t[close-1].Index];
            if(!string.IsNullOrWhiteSpace(indent))return false;
            while(lineEnd>end&&candidate[lineEnd-1]=='\r')lineEnd--;
            candidate=candidate.Insert(lineEnd,NL(candidate,"\n"+indent+LegacyOwnLine));
        }
        next=candidate;return true;
    }
    internal static int TryLegacyStage(string modsRoot,ref FixedZeroEngineBuild framework,
        Dictionary<string,byte[]> staged,bool nativeAvailable,out bool integrated)
    {
        integrated=false;
        if(!nativeAvailable||!framework.Files.TryGetValue(ZeroEngineDemandSource.InitPath,out var hb)||
           !framework.Files.TryGetValue(ZeroEngineDemandSource.SchedulerPath,out var sb))return 0;
        // The host may already have other independent ActionRouter callbacks;
        // the sole native gate refuses the actual mixed chain without edits.
        var host=Encoding.UTF8.GetString(hb);var scheduler=Encoding.UTF8.GetString(sb);
        string? Load(string name)
        {
            var relative="0-Engine/modules/"+name+".lua";
            if(framework.Files.TryGetValue(relative,out var bytes)||staged.TryGetValue(relative,out bytes))
                return Encoding.UTF8.GetString(bytes);
            var path=Path.Combine(modsRoot,"0-Engine","modules",name+".lua");
            return File.Exists(path)?File.ReadAllText(path):null;
        }
        var emitter=Load("EventEmitter");var lifecycle=Load("Lifecycle");
        var derived=Load("DerivedState");var board=Load("BlackboardCache2");
        if(emitter==null||lifecycle==null||derived==null||board==null||
           !LegacyEmitterProof(emitter)||!LegacyBusRecipientProof(lifecycle,"Lifecycle")||
           !LegacyBusRecipientProof(derived,"DerivedState")||!LegacyBusRecipientProof(board,"BlackboardCache")||
           !TryLegacyHost(host,out var hn)||!TryScheduler(scheduler,out var sn))return 0;
        var files=framework.Files.ToDictionary(p=>p.Key,p=>p.Value,StringComparer.OrdinalIgnoreCase);
        files[ZeroEngineDemandSource.InitPath]=Encoding.UTF8.GetBytes(hn);
        files[ZeroEngineDemandSource.SchedulerPath]=Encoding.UTF8.GetBytes(sn);
        framework=framework with {Files=files,FixedInitSha256=Convert.ToHexString(
            SHA256.HashData(files[ZeroEngineDemandSource.InitPath])).ToLowerInvariant()};
        integrated=true;return host==hn&&scheduler==sn?0:1;
    }

    private const string RouterHelper = """
-- G-CET native observer demand v1
local __gcetObserverLane
function ActionRouter.__gcetOwnBeforeObserver()
    if __gcetObserverLane then return end
    local ok, scheduler = pcall(require, "modules/G-CET/Scheduler")
    local demand = ok and type(scheduler) == "table" and scheduler.Demand
    if type(demand) ~= "table" or type(demand.OwnObserver) ~= "function" then return end
    local accepted, lane = pcall(demand.OwnObserver, "PlayerPuppet", "OnAction", function(fn)
        ActionRouter.__gcetObserverEnter = fn
    end)
    if accepted then __gcetObserverLane = lane end
end
local function __gcetObserverChanged()
    if __gcetObserverLane then __gcetObserverLane.SetActive(beforeActive > 0) end
end
""";

    // One event-owned lane, not a recurring job. The original router still
    // prepares on its first idle transition. Legacy notifications can start
    // idle and use the host's canonical scheduler activation wrapper.
    private static string LegacySchedulerHelper => SchedulerHelper.Replace("\r\n","\n",StringComparison.Ordinal)
        .Replace("setEntry, schedule)", "setEntry)", StringComparison.Ordinal)
        .Replace("    schedule = schedule or Scheduler.NextTick\n", "", StringComparison.Ordinal)
        .Replace("schedule({owner=", "Scheduler.NextTick({owner=", StringComparison.Ordinal);
    private const string SchedulerHelper = """
-- G-CET native observer demand v1
function demand.OwnObserver(klass, method, setEntry, schedule)
    schedule = schedule or Scheduler.NextTick
    local active, attempted, prepared, handle = true, false, false, 0
    local pending, native
    local function fallback()
        setEntry(nil)
        if native and handle ~= 0 then pcall(native.release, handle) end
        handle = 0
        if prepared then
            prepared = false
            schedule({owner="0-Engine", id="observer-retire", pause="never", clock="real"},
                function() pcall(native.retire) end)
        end
    end
    local function mirror()
        local ok, value = pcall(native.set, handle, active)
        if not ok or value ~= true then fallback() end
    end
    local function bind()
        setEntry(nil)
        local ok, value = pcall(native.bind)
        if not ok or type(value) ~= "number" or value <= 0 then fallback(); return end
        handle = value
        if not active then mirror() end
    end
    local function prepare()
        pending = nil
        if active or attempted then return end
        attempted = true
        local ok, api = pcall(function()
            return {prepare=Game.GCETObserverPrepare, bind=Game.GCETObserverBind,
                set=Game.GCETObserverSetDemand, release=Game.GCETObserverRelease,
                retire=Game.GCETObserverRetire}
        end)
        if not ok then return end
        for _, name in ipairs({"prepare","bind","set","release","retire"}) do
            if type(api[name]) ~= "function" then return end
        end
        native = api
        local accepted, result = pcall(function()
            return native.prepare(CName.new(klass), CName.new(method))
        end)
        if accepted and result == true then
            prepared = true
            setEntry(bind)
        end
    end
    return {SetActive=function(value)
        value = value == true
        if active == value then return end
        active = value
        if handle ~= 0 then mirror()
        elseif active then
            if pending then pending.Cancel(); pending = nil end
        elseif not attempted and not pending then
            pending = schedule({owner="0-Engine", id="observer-prepare", pause="never", clock="real"}, prepare)
        end
    end}
end
""";
}
