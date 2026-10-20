using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-first, no-CPU-threshold adoption of existing 0-Engine shared getters.
/// Deliberately does NOT rewrite arbitrary player:GetWorldPosition() Vector4
/// expressions: the experimental RED4ext snapshot exports only XYZ scalars.
/// </summary>
internal static class NativeReadReuseSource
{
    internal const string Contract = "GCET_NATIVE_READ_REUSE_V1";
    private static readonly Regex DirectGetter = new(
        @"(?<![A-Za-z0-9_\.:])Game\s*\.\s*(?<getter>Get[A-Za-z0-9_]+)\s*\(\s*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    // A local Game binding shadows the global CET Game facade. Refuse the
    // entire file, rather than guessing the lifetime of that binding.
    private static readonly Regex ShadowGame = new(
        @"\blocal\s+Game\b|\bGame\s*=(?!=)|\bfunction\s*\w+\s*\([^)]*\bGame\b[^)]*\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly IReadOnlyDictionary<string,SharedProviderAuthorization> ByGetter =
        SharedProviderCatalog.Authorized.ToDictionary(x=>x.Getter, StringComparer.Ordinal);
    internal sealed record Operation(string Provider,string Getter,int Occurrences,string HelperName,bool ExistingHelper=false);
    internal sealed record FilePlan(string RelativeFile,string Sha256,Operation[] Operations,string Proof);
    internal static FilePlan[] Analyze(string modsRoot)
    {
        if(!Directory.Exists(modsRoot))return [];
        var root=Path.GetFullPath(modsRoot);
        var plans=new List<FilePlan>();
        foreach(var path in Directory.EnumerateFiles(root,"*.lua",SearchOption.AllDirectories)
                             .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Take(20000))
        {
            var relative=Path.GetRelativePath(root,path).Replace('\\','/');
            if(IsInfrastructure(relative))continue;
            byte[] bytes;
            try { bytes=File.ReadAllBytes(path); } catch { continue; }
            if(bytes.Length>2_000_000 || bytes.Any(x=>x==0))continue;
            var text=Decode(bytes);
            var operations=FindOperations(text);
            if(operations.Length==0)continue;
            plans.Add(new FilePlan(relative,Hash(bytes),operations,"LEXICAL_CODE_ONLY_EXACT_ZERO_ARG_GAME_GETTER"));
        }
        return plans.ToArray();
    }
    private static bool IsInfrastructure(string relative)
    {
        var first=relative.Split('/')[0];
        return first.Equals("0-Engine",StringComparison.OrdinalIgnoreCase) ||
               first.Equals("G-CET",StringComparison.OrdinalIgnoreCase) ||
               first.Equals("G-CET-Profiler",StringComparison.OrdinalIgnoreCase);
    }
    // Mask only complete, previously generated provider functions (and the
    // native Vector4 live helper). Their author fallback must remain exactly
    // intact. Source-matched callsites OUTSIDE those helpers remain eligible.
    private static string ExcludeGeneratedFallbacks(string source,string masked,
        out HashSet<string> existing)
    {
        var chars=masked.ToCharArray();
        existing=new HashSet<string>(StringComparer.Ordinal);
        foreach(var provider in ByGetter.Values)
        {
            var marker="-- G-CET shared provider: "+provider.Provider;
            if(!source.Contains(marker,StringComparison.Ordinal))continue;
            var helper="__gcet"+provider.Getter;
            var pattern=@"local\s+function\s+"+Regex.Escape(helper)+
                @"\s*\(\s*\)[\s\S]*?return\s+Game\s*\.\s*"+
                Regex.Escape(provider.Getter)+@"\s*\(\s*\)\s*end";
            var matches=Regex.Matches(masked,pattern,RegexOptions.CultureInvariant);
            if(matches.Count!=1)continue;
            existing.Add(provider.Provider);
            foreach(Match m in matches)
                for(int i=m.Index;i<m.Index+m.Length;i++)
                    if(chars[i]!='\r'&&chars[i]!='\n')chars[i]=' ';
        }
        if(source.Contains("-- G-CET native player Vector4 on-demand live v1",
            StringComparison.Ordinal))
        {
            var block=Regex.Matches(masked,
                @"local\s+function\s+__gcetNativePlayerWorldPosition\s*\(\s*\)[\s\S]*?return\s+Game\s*\.\s*GetPlayer\s*\(\s*\)\s*:\s*GetWorldPosition\s*\(\s*\)\s*end",
                RegexOptions.CultureInvariant);
            if(block.Count==1)
                foreach(Match m in block)
                    for(int i=m.Index;i<m.Index+m.Length;i++)
                        if(chars[i]!='\r'&&chars[i]!='\n')chars[i]=' ';
        }
        return new string(chars);
    }

    internal static Operation[] FindOperations(string text)
    {
        var code=MaskNonCode(text);
        if(ShadowGame.IsMatch(code))return [];
        code=ExcludeGeneratedFallbacks(text,code,out var existing);
        return DirectGetter.Matches(code).Cast<Match>()
            .Where(m=>ByGetter.ContainsKey(m.Groups["getter"].Value))
            .GroupBy(m=>m.Groups["getter"].Value,StringComparer.Ordinal)
            .Select(g=>{
                var provider=ByGetter[g.Key];
                var helper="__gcet"+g.Key;
                var marked=text.Contains("-- G-CET shared provider: "+
                    provider.Provider,StringComparison.Ordinal);
                if(marked && !existing.Contains(provider.Provider))return null;
                if(!marked && Regex.IsMatch(code,@"\b"+Regex.Escape(helper)+@"\b",
                     RegexOptions.CultureInvariant))return null;
                return new Operation(provider.Provider,g.Key,g.Count(),helper,marked);
            })
            .Where(x=>x is not null).Select(x=>x!)
            .OrderBy(x=>x.Provider,StringComparer.Ordinal).ToArray();
    }
    internal static bool TryTransform(string input,
        Func<string,IEnumerable<string>> headerForProvider,
        out string output,out Operation[] rewritten)
    {
        rewritten=FindOperations(input);
        output=input;
        if(rewritten.Length==0)return false;
        var permitted=rewritten.ToDictionary(x=>x.Getter,x=>x.HelperName,StringComparer.Ordinal);
        var masked=MaskNonCode(input);
        masked=ExcludeGeneratedFallbacks(input,masked,out _);
        var matches=DirectGetter.Matches(masked).Cast<Match>()
            .Where(m=>permitted.ContainsKey(m.Groups["getter"].Value)).ToArray();
        if(matches.Length==0)return false;
        var builder=new StringBuilder(input);
        foreach(var m in matches.Reverse())
            builder.Remove(m.Index,m.Length).Insert(m.Index,permitted[m.Groups["getter"].Value]+"()");
        var newline=input.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var newProviders=rewritten.Where(x=>!x.ExistingHelper).ToArray();
        var headers=newProviders.SelectMany(x=>headerForProvider(x.Provider)).ToArray();
        if(newProviders.Length>0&&headers.Length==0){output=input;return false;}
        output=headers.Length==0?builder.ToString():
            string.Join(newline,headers)+newline+builder;
        return true;
    }
    internal static int TryStage(JsonElement resolver,string modsRoot,
        Dictionary<string,byte[]> staged,
        Dictionary<string,string?> liveSourceHashes,
        Dictionary<string,int> appliedTransformsByFile,
        List<object> transformManifest,List<object> skipped,
        Func<string,IEnumerable<string>> headerForProvider)
    {
        // Resolver report SHA is provenance, not a compatibility gate.
        // Re-discover every installed mod source for each generation, including
        // dormant files and mod updates not present in an earlier capture.
        var plans=Analyze(modsRoot);
        var applied=0;
        var rootPath=Path.GetFullPath(modsRoot);
        var prefix=rootPath.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        foreach(var plan in plans)
        {
            var rel=plan.RelativeFile;
            var path=Path.GetFullPath(Path.Combine(rootPath,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)||
               !File.Exists(path))continue;
            var original=File.ReadAllBytes(path);
            var key=rel.Replace('\\','/');
            // Snapshot identity only: refuse mixing two different live files
            // into the SAME ongoing generation, not a different mod release.
            if(staged.ContainsKey(key) &&
               liveSourceHashes.TryGetValue(key,out var generationSource) &&
               generationSource is not null &&
               !Hash(original).Equals(generationSource,StringComparison.OrdinalIgnoreCase))
            {
                skipped.Add(new {type="NATIVE_READ_REUSE",file=key,
                    reason="LIVE_SOURCE_CHANGED_DURING_GENERATION"});
                continue;
            }
            var input=staged.TryGetValue(key,out var other)?other:original;
            var text=Decode(input);
            if(!TryTransform(text,headerForProvider,out var result,out var operations))continue;
            // Every generated helper adds a Lua local in the main chunk. The
            // existing 200-local safeguard must apply to source-first edits
            // too; preserve earlier semantic staging if the extra helper
            // would exceed the language's hard per-function limit.
            var budget=LuaLocalBudgetAnalyzer.Analyze(result);
            if(budget.MaxActiveLocals>LuaLocalBudgetAnalyzer.Limit)
            {
                skipped.Add(new {type="NATIVE_READ_REUSE",file=key,
                    reason="LUA_LOCAL_LIMIT",peak=budget.MaxActiveLocals});
                continue;
            }
            // BOM and original newline style are preserved; no per-mod hooks.
            var bom=input.Length>=3&&input[0]==0xef&&input[1]==0xbb&&input[2]==0xbf;
            var bytes=Encoding.UTF8.GetBytes(result);
            if(bom)bytes=[0xef,0xbb,0xbf,..bytes];
            staged[key]=bytes;
            if(!liveSourceHashes.ContainsKey(key))liveSourceHashes[key]=Hash(original);
            appliedTransformsByFile[key]=
                appliedTransformsByFile.GetValueOrDefault(key)+operations.Sum(x=>x.Occurrences);
            foreach(var operation in operations)
                transformManifest.Add(new {
                    type="NATIVE_READ_REUSE",
                    contract=Contract,
                    file=key,
                    sourceSha256=Hash(original),
                    operation=operation.Getter,
                    provider=operation.Provider,
                    replacedOccurrences=operation.Occurrences,
                    providerOwner="0-Engine",
                    backend="EXISTING_SHARED_PROVIDER",
                    originalGetter="Game."+operation.Getter+"()",
                    fallback="ORIGINAL_GETTER",
                    boundaryCrossingsSaved="NOT_MEASURED",
                    doesNotRetireCallback=true
                });
            applied+=operations.Sum(x=>x.Occurrences);
        }
        return applied;
    }
    internal static string Decode(byte[] bytes)
    {
        var start=bytes.Length>=3&&bytes[0]==0xef&&bytes[1]==0xbb&&bytes[2]==0xbf?3:0;
        return Encoding.UTF8.GetString(bytes,start,bytes.Length-start);
    }
    private static string Hash(byte[] bytes)=>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    /// <summary>Exact-length Lua code mask: remove comments and all string
    /// literals (including Lua long brackets) without changing source spans.</summary>
    internal static string MaskNonCode(string text)
    {
        var chars=text.ToCharArray();
        static void Blank(char[] buf,int a,int b)
        {for(int i=a;i<b;i++)if(buf[i]!='\r'&&buf[i]!='\n')buf[i]=' ';}
        static int LongOpen(string t,int start,out int eq)
        {
            eq=0;if(start>=t.Length||t[start]!='[')return 0;
            int i=start+1;while(i<t.Length&&t[i]=='='){i++;eq++;}
            return i<t.Length&&t[i]=='['?i-start+1:0;
        }
        int n=text.Length;
        for(int i=0;i<n;)
        {
            if(text[i]=='-'&&i+1<n&&text[i+1]=='-')
            {
                int start=i;i+=2;
                int width=LongOpen(text,i,out int eq);
                if(width>0)
                {
                    i+=width;
                    var close="]"+new string('=',eq)+"]";
                    int end=text.IndexOf(close,i,StringComparison.Ordinal);
                    i=end<0?n:end+close.Length;
                }
                else
                {
                    while(i<n&&text[i]!='\n'&&text[i]!='\r')i++;
                }
                Blank(chars,start,i);continue;
            }
            if(text[i]=='"'||text[i]=='\'')
            {
                int start=i;char quote=text[i++];
                while(i<n)
                {
                    if(text[i]=='\\'){i=Math.Min(n,i+2);continue;}
                    if(text[i++]==quote)break;
                }
                Blank(chars,start,i);continue;
            }
            int open=LongOpen(text,i,out int equality);
            if(open>0)
            {
                int start=i;i+=open;
                var close="]"+new string('=',equality)+"]";
                int end=text.IndexOf(close,i,StringComparison.Ordinal);
                i=end<0?n:end+close.Length;
                Blank(chars,start,i);continue;
            }
            i++;
        }
        return new string(chars);
    }
}
