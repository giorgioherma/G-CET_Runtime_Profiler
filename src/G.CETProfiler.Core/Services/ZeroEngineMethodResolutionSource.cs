using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Repair previously generated 0-Engine wrappers by the METHOD they need.
// Existing Engine.GCET may be a location-only extension: selecting it
// unconditionally silently restores the old CET callbacks and Game getters.
internal static class ZeroEngineMethodResolutionSource
{
    internal const string Contract="GCET_METHOD_LEVEL_API_LOOKUP_V1";
    private static readonly Regex Shared = new(
        @"local __gcetApi = type\(__gcetEngine\.GCET\) == ""table"" and __gcetEngine\.GCET or __gcetEngine",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex Route = new(
        @"if __gcetOk_(?<id>\d+) and type\(__gcetEngine_\k<id>\) == ""table"" and type\(__gcetEngine_\k<id>\.GCET\) == ""table"" then __gcetApi_\k<id> = __gcetEngine_\k<id>\.GCET end",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);
    private static readonly Regex Frame = new(
        @"if __gcetOk and type\(__gcetEngine\) == ""table"" and type\(__gcetEngine\.GCET\) == ""table"" then __gcetApi = __gcetEngine\.GCET end",
        RegexOptions.Compiled|RegexOptions.CultureInvariant);

    internal static string Repair(string text,out int count)
    {
        count=0;
        // Only own generated wrappers. Never infer the meaning of an author's
        // similarly named Lua table.
        if (!text.Contains("__gcet",StringComparison.Ordinal) ||
            !text.Contains("GetMod",StringComparison.Ordinal))
            return text;
        int edits=0;
        var result=Shared.Replace(text,m=>{
            var rest=text.AsSpan(m.Index+m.Length);
            var max=Math.Min(rest.Length,220);
            var call=Regex.Match(rest[..max].ToString(),
                @"type\(__gcetApi\.(?<method>Get[A-Za-z0-9_]+)\) == ""function""",
                RegexOptions.CultureInvariant);
            if(!call.Success ||
               !text.Contains("-- G-CET shared provider: ",StringComparison.Ordinal))
                return m.Value;
            edits++;
            var method=call.Groups["method"].Value;
            return @"local __gcetApi = type(__gcetEngine.GCET) == ""table"" and type(__gcetEngine.GCET."+
                method+@") == ""function"" and __gcetEngine.GCET or __gcetEngine";
        });
        if(result.Contains("__gcetHandles_",StringComparison.Ordinal))
        {
            result=Route.Replace(result,m=>{
                var id=m.Groups["id"].Value;
                if(!result.Contains("__gcetApi_"+id+".SubscribeAction",StringComparison.Ordinal))return m.Value;
                edits++;
                return m.Value.Replace(
                    "type(__gcetEngine_"+id+".GCET) == \"table\" then",
                    "type(__gcetEngine_"+id+".GCET) == \"table\" and type(__gcetEngine_"+id+
                    ".GCET.SubscribeAction) == \"function\" then",
                    StringComparison.Ordinal);
            });
        }
        if(result.Contains("MakeEventRegistrar",StringComparison.Ordinal) &&
           result.Contains("-- G-CET generated frame registrar:",StringComparison.Ordinal))
        {
            result=Frame.Replace(result,m=>{
                edits++;
                return m.Value.Replace(
                    "type(__gcetEngine.GCET) == \"table\" then",
                    "type(__gcetEngine.GCET) == \"table\" and type(__gcetEngine.GCET.MakeEventRegistrar) == \"function\" then",
                    StringComparison.Ordinal);
            });
        }
        count=edits;
        return result;
    }

    internal static int TryStage(
        string modsRoot,Dictionary<string,byte[]> staged,
        Dictionary<string,int> appliedByFile,List<object> manifest,List<object> skipped)
    {
        if(!Directory.Exists(modsRoot))return 0;
        var root=Path.GetFullPath(modsRoot);
        var total=0;
        foreach(var path in Directory.EnumerateFiles(root,"*.lua",SearchOption.AllDirectories)
                     .OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Take(20000))
        {
            var key=Path.GetRelativePath(root,path).Replace('\\','/');
            if(key.StartsWith("0-Engine/",StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("G-CET/",StringComparison.OrdinalIgnoreCase))
                continue;
            byte[] original;
            try{original=staged.TryGetValue(key,out var previous)?previous:File.ReadAllBytes(path);}
            catch{continue;}
            if(original.Length>2_000_000 || original.Any(b=>b==0))continue;
            var source=Encoding.UTF8.GetString(original);
            var revised=Repair(source,out var count);
            if(count==0)continue;
            var bytes=Encoding.UTF8.GetBytes(revised);
            var budget=LuaLocalBudgetAnalyzer.Analyze(bytes);
            if(budget.MaxActiveLocals>LuaLocalBudgetAnalyzer.Limit)
            {
                skipped.Add(new{type="GCET_API_METHOD_LOOKUP",file=key,reason="LUA_LOCAL_LIMIT"});
                continue;
            }
            staged[key]=bytes;
            appliedByFile[key]=appliedByFile.TryGetValue(key,out var earlier)?earlier+count:count;
            manifest.Add(new{type="GCET_API_METHOD_LOOKUP",file=key,replacedOccurrences=count,
                reason="method-level namespace selection; original fallback preserved"});
            total+=count;
        }
        return total;
    }
}
