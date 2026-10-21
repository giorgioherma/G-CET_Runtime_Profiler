using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-first opt-in at the exact direct Game.GetPlayer():GetWorldPosition
/// expression. No semantic equivalence is asserted for PlayerPuppet:self or
/// aliases. The guarded provider invokes the on-demand STRICT-LIVE native
/// getter, never the unverified phase-shared cache, and falls back to the
/// original expression if native ABI is missing or invalid.
/// </summary>
internal static class NativePlayerVectorStage
{
    internal const string Contract="GCET_NATIVE_PLAYER_VECTOR_LIVE_V1";
    internal const string Marker="-- G-CET native player Vector4 on-demand live v1";
    private const string Helper="__gcetNativePlayerWorldPosition";
    private static readonly Regex Expression=new(
        @"(?<![A-Za-z0-9_\.:])Game\s*\.\s*GetPlayer\s*\(\s*\)\s*:\s*GetWorldPosition\s*\(\s*\)",
        RegexOptions.CultureInvariant|RegexOptions.Compiled);
    private static readonly Regex ShadowGame=new(
        @"\blocal\s+Game\b|\bGame\s*=(?!=)|\bfunction\s*\w+\s*\([^)]*\bGame\b[^)]*\)",
        RegexOptions.CultureInvariant|RegexOptions.Compiled);

    // Independent, per-callsite adoption, including partially optimized
    // files. A generated helper is excluded from its OWN source scan so the
    // required live fallback is never recursively rewritten.
    internal static bool TryTransform(string original,out string output,out int count)
    {
        output=original;count=0;
        if(string.IsNullOrEmpty(original))return false;
        var masked=NativeReadReuseSource.MaskNonCode(original);
        if(ShadowGame.IsMatch(masked))return false;
        var already=original.Contains(Marker,StringComparison.Ordinal);
        if(already)
        {
            var helper=Regex.Matches(masked,
                @"local\s+function\s+"+Helper+
                @"\s*\(\s*\)[\s\S]*?return\s+Game\s*\.\s*GetPlayer\s*\(\s*\)\s*:\s*GetWorldPosition\s*\(\s*\)\s*end",
                RegexOptions.CultureInvariant);
            if(helper.Count!=1 ||
               !original.Contains("GCETPlayerVector4Live",StringComparison.Ordinal) ||
               !original.Contains("GCETPlayerVector4Valid",StringComparison.Ordinal))
                return false; // partial/foreign helper: never duplicate it
            var chars=masked.ToCharArray();
            for(int i=helper[0].Index;i<helper[0].Index+helper[0].Length;i++)
                if(chars[i]!='\n'&&chars[i]!='\r') chars[i]=' ';
            masked=new string(chars);
        }
        else if(Regex.IsMatch(masked,@"\b"+Helper+@"\b",
            RegexOptions.CultureInvariant))
            return false;
        var matches=Expression.Matches(masked).Cast<Match>().ToArray();
        if(matches.Length==0)return false;
        var builder=new StringBuilder(original);
        foreach(var m in matches.Reverse())
            builder.Remove(m.Index,m.Length).Insert(m.Index,Helper+"()");
        if(already)
        {
            output=builder.ToString();
            count=matches.Length;
            return true;
        }
        var nl=original.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var header=string.Join(nl,new []{
            Marker,
            "local __gcetNativeLive, __gcetNativeValid",
            "do",
            "  local ok,fn=pcall(function() return Game.GCETPlayerVector4Live end)",
            "  if ok and type(fn)=='function' then __gcetNativeLive=fn end",
            "  local validOk,validFn=pcall(function() return Game.GCETPlayerVector4Valid end)",
            "  if validOk and type(validFn)=='function' then __gcetNativeValid=validFn end",
            "end",
            "local function "+Helper+"()",
            "  if __gcetNativeLive and __gcetNativeValid then",
            "    local ok,value=pcall(__gcetNativeLive)",
            "    if ok and value~=nil then",
            "      local statusOk,valid=pcall(__gcetNativeValid)",
            "      if statusOk and valid==true then return value end",
            "    end",
            "  end",
            "  return Game.GetPlayer():GetWorldPosition()",
            "end",
            ""
        });
        output=header+builder;
        count=matches.Length;
        return true;
    }

    internal static int TryStage(string modsRoot,
        Dictionary<string,byte[]> staged,
        Dictionary<string,int> appliedTransformsByFile,
        List<object> transformManifest,List<object> skipped)
    {
        var root=Path.GetFullPath(modsRoot);
        var prefix=root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        // Scan the CURRENT installed files and all previously staged source;
        // earlier semantic rewrites may introduce a new eligible callsite.
        // Captured player-state inventory is evidence, not an admission lock.
        var applied=0;
        var liveFiles=Directory.EnumerateFiles(root,"*.lua",SearchOption.AllDirectories)
            .OrderBy(path=>path,StringComparer.OrdinalIgnoreCase).Take(20000);
        foreach(var path in liveFiles)
        {
            var relative=Path.GetRelativePath(root,path).Replace('\\','/');
            var first=relative.Split('/')[0];
            if(first.Equals("0-Engine",StringComparison.OrdinalIgnoreCase) ||
               first.StartsWith("G-CET",StringComparison.OrdinalIgnoreCase))
                continue;
            var original=System.IO.File.ReadAllBytes(path);
            if(original.Length>2_000_000 || original.Any(b=>b==0))continue;
            var key=relative;
            var input=staged.TryGetValue(key,out var current)?current:original;
            var text=NativeReadReuseSource.Decode(input);
            if(!TryTransform(text,out var result,out var count))continue;
            if(LuaLocalBudgetAnalyzer.Analyze(result).MaxActiveLocals>
               LuaLocalBudgetAnalyzer.Limit)
            {
                skipped.Add(new{type="NATIVE_PLAYER_VECTOR_LIVE",file=key,
                    reason="LUA_LOCAL_LIMIT"});
                continue;
            }
            var bom=input.Length>=3&&input[0]==0xef&&input[1]==0xbb&&input[2]==0xbf;
            var bytes=Encoding.UTF8.GetBytes(result);
            if(bom)bytes=[0xef,0xbb,0xbf,..bytes];
            staged[key]=bytes;
            appliedTransformsByFile[key]=appliedTransformsByFile.GetValueOrDefault(key)+count;
            transformManifest.Add(new {
                type="NATIVE_PLAYER_VECTOR_LIVE", contract=Contract,file=key,
                replacedOccurrences=count,receiverProof="DIRECT_GAME_GETPLAYER_EXPRESSION",
                source="ALL_INSTALLED_LUA_NOT_CAPTURE_GATED",
                backend="RED4EXT_TYPED_VECTOR4_FORCE_LIVE",
                originalFallback="Game.GetPlayer():GetWorldPosition()",
                cacheReuse=false,reason="CONSERVATIVE_CROSS_PHASE_FRESHNESS",
                runtimeCapability="GCETPlayerVector4Live+GCETPlayerVector4Valid",
                savings="NOT_MEASURED",doesNotRetireCallback=true
            });
            applied+=count;
        }
        return applied;
    }
}
