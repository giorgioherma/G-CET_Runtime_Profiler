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
            if(Count(candidate,NL(candidate,SchedulerHelper))!=1)return false;
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

    // One event-owned lane, not a recurring job. Prepare only on the first
    // transition to idle after an actual subscription. Bind once inside the
    // original observer. All subsequent updates are boolean transitions.
    private const string SchedulerHelper = """
-- G-CET native observer demand v1
function demand.OwnObserver(klass, method, setEntry)
    local active, attempted, prepared, handle = true, false, false, 0
    local pending, native
    local function fallback()
        setEntry(nil)
        if native and handle ~= 0 then pcall(native.release, handle) end
        handle = 0
        if prepared then
            prepared = false
            Scheduler.NextTick({owner="0-Engine", id="observer-retire", pause="never", clock="real"},
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
            pending = Scheduler.NextTick({owner="0-Engine", id="observer-prepare", pause="never", clock="real"}, prepare)
        end
    end}
end
""";
}
