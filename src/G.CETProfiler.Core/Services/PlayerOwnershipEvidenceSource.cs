using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Zero native-runtime overhead: derives context and receiver-origin tags from
/// existing profiler callback registration metadata + installed Lua source.
/// The tag for PlayerPuppet::X identifies an observing class, NOT the identity
/// of every entity accessed inside that callback. Never authorizes a pass.
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
    private static readonly Regex ShadowGame = new(
        @"\blocal\s+Game\b|\bGame\s*=(?!=)|\bfunction\s*\w+\s*\([^)]*\bGame\b[^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ScopeBoundary = new(
        @"\b(?:end|function|else|elseif|until)\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    internal sealed record Callsite(
        string Method,
        string ReceiverExpression,
        int LineOffset,
        string ReceiverTag,
        string Proof,
        bool RuntimeObserved,
        bool AuthorizesRewrite);
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
    internal static Callsite[] Analyze(string source)
    {
        if(string.IsNullOrEmpty(source))return [];
        var code=NativeReadReuseSource.MaskNonCode(source);
        var shadowed=ShadowGame.IsMatch(code);
        var sites=new List<(int At,Callsite Row)>();
        foreach(Match m in Direct.Matches(code))
        {
            var method=m.Groups["method"].Value;
            sites.Add((m.Index,new Callsite(method,"Game.GetPlayer()",
                LineOffset(source,m.Index),
                shadowed?"UNKNOWN":"CURRENT_PLAYER_SOURCE_PROVEN",
                shadowed?"GLOBAL_GAME_FACADE_SHADOWED":"DIRECT_GAME_GETPLAYER_EXPRESSION",
                false,false)));
        }
        foreach(Match m in Named.Matches(code))
        {
            var name=m.Groups["receiver"].Value;
            var method=m.Groups["method"].Value;
            var origin=!shadowed?TryTraceAlias(code,name,m.Index):null;
            sites.Add((m.Index,new Callsite(method,name,
                LineOffset(source,m.Index),
                origin is null?"UNKNOWN":"PLAYER_ALIAS_SOURCE_EVIDENCE",
                origin is null?"RECEIVER_ORIGIN_UNRESOLVED":
                    "SINGLE_LOCAL_ALIAS_NO_INTERVENING_LEXICAL_SCOPE_BOUNDARY",
                false,false)));
        }
        return sites.OrderBy(x=>x.At).Select(x=>x.Row).ToArray();
    }
    private static int LineOffset(string text,int pos)
    {
        var count=0;
        for(int i=0;i<pos;i++)if(text[i]=='\n')count++;
        return count;
    }
    // Trace a local alias only in a single straight-line lexical region.
    // Mutations, scope exits and multiple bindings fail closed. It is useful
    // investigation evidence, not a conclusive runtime receiver proof.
    private static string? TryTraceAlias(string code,string name,int position,int depth=0)
    {
        if(depth>4)return null;
        var prefix=code[..position];
        var declarations=Regex.Matches(prefix,
            @"\blocal\s+"+Regex.Escape(name)+@"\s*=\s*(?<rhs>Game\s*\.\s*GetPlayer\s*\(\s*\)|[A-Za-z_]\w*)",
            RegexOptions.CultureInvariant).Cast<Match>().ToArray();
        if(declarations.Length!=1)return null;
        var binding=declarations[0];
        // Never claim aliases with uncertain lifetime or branch flow.
        var between=code.Substring(binding.Index+binding.Length,
            position-binding.Index-binding.Length);
        if(ScopeBoundary.IsMatch(between)||
           Regex.IsMatch(between,@"(?<![A-Za-z0-9_\.])"+Regex.Escape(name)+@"\s*=(?!=)",
               RegexOptions.CultureInvariant))return null;
        var rhs=binding.Groups["rhs"].Value;
        if(rhs.StartsWith("Game",StringComparison.Ordinal))
            return "Game.GetPlayer";
        if(rhs==name)return null;
        return TryTraceAlias(code,rhs,binding.Index,depth+1);
    }
}
