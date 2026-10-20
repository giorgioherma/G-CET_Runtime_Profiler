using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Offline evidence from existing callback registration metadata and installed
/// Lua sources. A PlayerPuppet method observer's first callback argument is
/// an instance of that class, NOT necessarily Game.GetPlayer() at every phase.
/// These tags never authorize a native-cache rewrite.
/// </summary>
internal static class PlayerOwnershipEvidenceSource
{
    internal const string Contract = "GCET_PLAYER_OWNERSHIP_EVIDENCE_V1";
    private const string Methods = "GetWorldPosition|GetWorldOrientation|IsInCombat";
    private static readonly Regex Direct = new(
        @"(?<![A-Za-z0-9_\.:])Game\s*\.\s*GetPlayer\s*\(\s*\)\s*:\s*(?<method>"+Methods+@")\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Named = new(
        @"(?<![A-Za-z0-9_\.:])(?<receiver>[A-Za-z_]\w*)\s*:\s*(?<method>"+Methods+@")\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // Only a direct CET registration with literal class/method names can bind
    // the callback's first argument. "Event.Observe" is a wrapper: unknown.
    private static readonly Regex Registration = new(
        @"(?<![A-Za-z0-9_\.:])(?<api>ObserveAfter|Observe|Override)\s*\(\s*(?<q1>['""])(?<class>[A-Za-z_]\w*)\k<q1>\s*,\s*(?<q2>['""])(?<method>[A-Za-z_]\w*)\k<q2>\s*,\s*function\s*\(\s*(?<first>[A-Za-z_]\w*)\s*(?:,|\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BlockToken = new(
        @"\b(function|if|for|while|do|repeat|until|end)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ShadowGame = new(
        @"\blocal\s+Game\b|\bGame\s*=(?!=)|\bfunction\s*\w+\s*\([^)]*\bGame\b[^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ScopeBoundary = new(
        @"\b(?:end|function|if|then|for|while|do|repeat|else|elseif|until)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal sealed record Callsite(
        string Method,
        string ReceiverExpression,
        int LineOffset,
        string ReceiverTag,
        string Proof,
        bool RuntimeObserved,
        bool AuthorizesRewrite,
        string? ReceiverClass = null);
    private sealed record BoundRegistration(
        int BodyStart,int BodyEnd,string FirstParameter,string Class,string Api,
        (int Start,int End)[] NestedFunctions);

    internal static string Context(string kind,string target)
    {
        if (target.Equals("PlayerPuppet",StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith("PlayerPuppet::",StringComparison.OrdinalIgnoreCase))
            return "PLAYER_PUPPET_CLASS_CONTEXT";
        if (kind.Equals("event",StringComparison.OrdinalIgnoreCase))
            return "GLOBAL_EVENT_CONTEXT";
        return string.IsNullOrWhiteSpace(target)
            ? "CALLBACK_CONTEXT_UNKNOWN"
            : "OTHER_CLASS_OR_TARGET_CONTEXT";
    }

    // start/end lines are optional: the Resolver supplies its measured source
    // span so an identical registration elsewhere in the same file cannot be
    // spuriously assigned to this callback.
    internal static Callsite[] Analyze(string source,string? callbackKind=null,
        string? callbackTarget=null,int? startLine=null,int? endLine=null)
    {
        if(string.IsNullOrEmpty(source))return [];
        source=source.Replace("\r\n","\n").Replace('\r','\n');
        var code=NativeReadReuseSource.MaskNonCode(source);
        var shadowed=ShadowGame.IsMatch(code);
        var registrations=FindRegistrations(source,code,callbackKind,callbackTarget);
        var sites=new List<(int At,Callsite Row)>();
        bool InWindow(int pos)
        {
            var line=LineNumber(source,pos);
            return (!startLine.HasValue||line>=startLine.Value) &&
                   (!endLine.HasValue||line<=endLine.Value);
        }
        int Offset(int pos)=>LineNumber(source,pos)-(startLine??1);

        foreach(Match m in Direct.Matches(code))
        {
            if(!InWindow(m.Index))continue;
            sites.Add((m.Index,new Callsite(m.Groups["method"].Value,"Game.GetPlayer()",
                Offset(m.Index),shadowed?"UNKNOWN":"CURRENT_PLAYER_SOURCE_PROVEN",
                shadowed?"GLOBAL_GAME_FACADE_SHADOWED":"DIRECT_GAME_GETPLAYER_EXPRESSION",
                false,false)));
        }
        foreach(Match m in Named.Matches(code))
        {
            if(!InWindow(m.Index))continue;
            var name=m.Groups["receiver"].Value;
            var method=m.Groups["method"].Value;
            var receiverClass=(string?)null;
            var tag="UNKNOWN";
            var proof="RECEIVER_ORIGIN_UNRESOLVED";

            // Find the actual enclosing observed/overridden Lua function.
            // Being in a PlayerPuppet callback does not make an unrelated
            // targetNpc:GetWorldPosition() a player operation.
            var owner=registrations
                .Where(r=>m.Index>=r.BodyStart && m.Index<r.BodyEnd &&
                      !r.NestedFunctions.Any(n=>m.Index>=n.Start&&m.Index<n.End))
                .OrderByDescending(r=>r.BodyStart).FirstOrDefault();
            if(owner is not null &&
                TryTraceCallbackArgument(code,owner,name,m.Index,out var alias))
            {
                receiverClass=owner.Class;
                tag=owner.Class.Equals("PlayerPuppet",StringComparison.Ordinal)
                    ? (alias?"PLAYER_PUPPET_OBSERVER_ALIAS":"PLAYER_PUPPET_OBSERVER_RECEIVER")
                    : "CLASS_BOUND_OBSERVER_RECEIVER";
                proof=alias?"LOCAL_ALIAS_OF_REGISTERED_CALLBACK_FIRST_PARAMETER":
                    "DIRECT_CET_REGISTRATION_FIRST_PARAMETER";
            }
            else
            {
                var origin=!shadowed?TryTracePlayerAlias(code,name,m.Index):null;
                if(origin is not null)
                {
                    tag="PLAYER_ALIAS_SOURCE_EVIDENCE";
                    proof="SINGLE_LOCAL_ALIAS_NO_INTERVENING_LEXICAL_SCOPE_BOUNDARY";
                }
            }
            sites.Add((m.Index,new Callsite(method,name,Offset(m.Index),
                tag,proof,false,false,receiverClass)));
        }
        return sites.OrderBy(x=>x.At).Select(x=>x.Row).ToArray();
    }

    private static BoundRegistration[] FindRegistrations(string original,
        string code,string? kind,string? target)
    {
        var found=new List<BoundRegistration>();
        foreach(Match match in Registration.Matches(original))
        {
            var apiGroup=match.Groups["api"];
            // Discard registrations found only inside comments/quoted text.
            if(!code.AsSpan(apiGroup.Index,apiGroup.Length)
                .SequenceEqual(apiGroup.Value.AsSpan()))continue;
            var api=apiGroup.Value;
            var cls=match.Groups["class"].Value;
            var method=match.Groups["method"].Value;
            var arg=match.Groups["first"].Value;
            if(arg=="_")continue; // intentionally discarded first argument
            if(kind is not null &&
               (kind.Equals("event",StringComparison.OrdinalIgnoreCase) ||
                (kind.Contains("override",StringComparison.OrdinalIgnoreCase)
                  !=api.Equals("Override",StringComparison.Ordinal))))continue;
            if(target is not null && target.Length>0 &&
                !target.Equals(cls+"::"+method,StringComparison.OrdinalIgnoreCase))
                continue;
            // Shadowed/replaced registration functions are not CET guarantees.
            var prefix=code[..apiGroup.Index];
            if(Regex.IsMatch(prefix,@"\b(?:local\s+|function\s+)?"+
                    Regex.Escape(api)+@"\s*=(?!=)|\blocal\s+function\s+"+
                    Regex.Escape(api)+@"\b",
                    RegexOptions.CultureInvariant))continue;
            var paramEnd=code.IndexOf(')',match.Groups["first"].Index);
            if(paramEnd<0)continue;
            if(!TryFindBody(code,paramEnd+1,out var bodyEnd,out var nested))
                continue;
            found.Add(new BoundRegistration(paramEnd+1,bodyEnd,arg,cls,api,nested));
        }
        return found.ToArray();
    }

    // Minimal lexical block matcher. If Lua source cannot be parsed with this
    // conservative model, it cannot establish a first-argument binding.
    private static bool TryFindBody(string code,int begin,out int end,
        out (int Start,int End)[] nested)
    {
        end=-1;
        nested=[];
        var stack=new List<(string Kind,int At)>{("function",begin)};
        var functions=new List<(int Start,int End)>();
        var awaitingDo=0;
        foreach(Match t in BlockToken.Matches(code,begin))
        {
            switch(t.Value)
            {
                case "function":stack.Add(("function",t.Index));break;
                case "if":stack.Add(("if",t.Index));break;
                case "for":
                case "while":stack.Add(("loop",t.Index));awaitingDo++;break;
                case "do":
                    if(awaitingDo>0)awaitingDo--;
                    else stack.Add(("do",t.Index));
                    break;
                case "repeat":stack.Add(("repeat",t.Index));break;
                case "until":
                    if(stack.Count==0||stack[^1].Kind!="repeat")return false;
                    stack.RemoveAt(stack.Count-1);
                    break;
                case "end":
                    if(stack.Count==0||stack[^1].Kind=="repeat")return false;
                    var last=stack[^1];
                    stack.RemoveAt(stack.Count-1);
                    if(last.Kind=="function"&&stack.Count>0)
                        functions.Add((last.At,t.Index+t.Length));
                    if(stack.Count==0)
                    {
                        end=t.Index;
                        nested=functions.ToArray();
                        return true;
                    }
                    break;
            }
        }
        return false;
    }

    private static bool TryTraceCallbackArgument(string code,BoundRegistration owner,
        string receiver,int position,out bool alias)
    {
        alias=false;
        var param=owner.FirstParameter;
        if(HasRebinding(code,param,owner.BodyStart,position))return false;
        if(receiver==param)return true;
        // A short straight-line alias is safe evidence even when not executed.
        // Nested block declarations, multi-assignments, and rebindings remain
        // unknown rather than being promoted to current-player identity.
        var pre=code.Substring(owner.BodyStart,position-owner.BodyStart);
        var pattern=@"\blocal\s+"+Regex.Escape(receiver)+
            @"\s*=\s*"+Regex.Escape(param)+@"\b";
        var assignments=Regex.Matches(pre,pattern,RegexOptions.CultureInvariant);
        if(assignments.Count!=1)return false;
        var declaration=assignments[0];
        if(ScopeBoundary.IsMatch(pre[..declaration.Index]) ||
           ScopeBoundary.IsMatch(pre[(declaration.Index+declaration.Length)..]))
            return false;
        if(HasRebinding(code,receiver,owner.BodyStart+declaration.Index+
                         declaration.Length,position))return false;
        alias=true;
        return true;
    }

    private static bool HasRebinding(string code,string name,int start,int end)
    {
        if(start>=end)return false;
        return Regex.IsMatch(code.Substring(start,end-start),
            @"(?<![A-Za-z0-9_\.])(?:local\s+)?"+Regex.Escape(name)+
            @"\s*=(?!=)",RegexOptions.CultureInvariant);
    }

    private static int LineNumber(string source,int pos)
    {
        var line=1;
        for(var i=0;i<pos;i++)if(source[i]=='\n')line++;
        return line;
    }

    private static string? TryTracePlayerAlias(string code,string name,int position,
        int depth=0)
    {
        if(depth>4)return null;
        var prefix=code[..position];
        var declarations=Regex.Matches(prefix,
            @"\blocal\s+"+Regex.Escape(name)+
            @"\s*=\s*(?<rhs>Game\s*\.\s*GetPlayer\s*\(\s*\)|[A-Za-z_]\w*)",
            RegexOptions.CultureInvariant).Cast<Match>().ToArray();
        if(declarations.Length!=1)return null;
        var binding=declarations[0];
        var between=code.Substring(binding.Index+binding.Length,
            position-binding.Index-binding.Length);
        if(ScopeBoundary.IsMatch(between)||HasRebinding(code,name,
            binding.Index+binding.Length,position))return null;
        var rhs=binding.Groups["rhs"].Value;
        if(rhs.StartsWith("Game",StringComparison.Ordinal))return "Game.GetPlayer";
        if(rhs==name)return null;
        return TryTracePlayerAlias(code,rhs,binding.Index,depth+1);
    }
}
