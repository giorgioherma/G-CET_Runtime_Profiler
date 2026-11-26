using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-first opt-in at the exact direct Game.GetPlayer():GetWorldPosition
/// expression. No semantic equivalence is asserted for PlayerPuppet:self or
/// aliases. The guarded provider invokes the on-demand STRICT-LIVE native
/// getter for gameplay; read-only formatted display has an independent opt-in.
/// Both paths fall back to the
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
        var header=LiveHeader(nl);
        output=header+builder;
        count=matches.Length;
        return true;
    }


    // The native provider already owns a single frame-keyed position sample.
    // Admit ONLY a bounded display expression: checkbox-controlled, formatted
    // XYZ, and one ImGui.Text sink with no other uses of the position or the
    // formatted values. Never infer gameplay freshness from a frame number.
    internal const string FrameDisplayMarker="-- G-CET native player position frame display v1";
    private const string DisplayRead="__gcetReadFrameDisplayPosition";
    private const string DisplaySample="__gcetFrameDisplaySample";
    private const string FrameDisplayHeader="""
-- G-CET native player position frame display v1
local __gcetFrameDisplayRead, __gcetFrameDisplayValid, __gcetFrameDisplayResolved
local function __gcetReadFrameDisplayPosition()
    if not __gcetFrameDisplayResolved then
        __gcetFrameDisplayResolved = true
        local ok, caps = pcall(function() return Game.GCETPlayerCapabilities() end)
        if ok and type(caps) == "number" and math.floor(caps / 128) % 2 == 1 then
            local rok, read = pcall(function() return Game.GCETPlayerVector4 end)
            local vok, valid = pcall(function() return Game.GCETPlayerVector4Valid end)
            if rok and vok and type(read) == "function" and type(valid) == "function" then
                __gcetFrameDisplayRead, __gcetFrameDisplayValid = read, valid
            end
        end
    end
    if not __gcetFrameDisplayRead then return nil end
    local ok, value = pcall(__gcetFrameDisplayRead)
    if not ok or value == nil then return nil end
    local vok, valid = pcall(__gcetFrameDisplayValid)
    if vok and valid == true then return value end
    return nil
end
""";
    private static readonly Regex DisplayScope=new(
        @"(?ms)^function[ \t]+[A-Za-z_]\w*:[A-Za-z_]\w*[ \t]*\([ \t]*\)[ \t]*\r?\n.*?^end[ \t]*(?=\r?$)",
        RegexOptions.CultureInvariant);
    private const string DisplayGetter=Helper+@"\s*\(\s*\)";
    private const string Quoted=@"(?:""[^""\r\n]*""|'[^'\r\n]*')";
    private const string Precision=@"(?:""%\.2f""|'%\.2f')";
    private const string DisplayGuard=@"\Afunction[ \t]+[A-Za-z_]\w*:[A-Za-z_]\w*[ \t]*\([ \t]*\)[ \t]*\r?\n"+
        @"[ \t]*self\.(?<flag>[A-Za-z_]\w*)\s*=\s*ImGui\.Checkbox\(\s*"+Quoted+
        @",\s*self\.\k<flag>\s*\)[ \t]*\r?\n[ \t]*if\s+self\.\k<flag>\s+then[ \t]*\r?\n";
    private static string FormatField(string field,string expression)=>@"(?<indent"+field+@">[ \t]*)local[ \t]+(?<"+field+
        @">[A-Za-z_]\w*)[ \t]*=[ \t]*string\.format\(\s*"+Precision+@",\s*"+expression+
        @"\s*\."+field+@"\s*\)[ \t]*\r?\n";
    private const string DisplaySink=@"[ \t]*ImGui\.Text\(\s*"+Quoted+@"\s*\.\.\s*\k<x>\s*\.\.\s*"+
        Quoted+@"\s*\.\.\s*\k<y>\s*\.\.\s*"+Quoted+@"\s*\.\.\s*\k<z>\s*\)[ \t]*(?=\r?$)";
    private static readonly Regex DisplayTriple=new(DisplayGuard+
        FormatField("x","(?<gx>"+DisplayGetter+")")+
        FormatField("y","(?<gy>"+DisplayGetter+")")+
        FormatField("z","(?<gz>"+DisplayGetter+")")+DisplaySink,
        RegexOptions.CultureInvariant|RegexOptions.Multiline);
    private static readonly Regex DisplayAlias=new(DisplayGuard+
        @"[ \t]*local[ \t]+(?<pos>[A-Za-z_]\w*)[ \t]*=[ \t]*(?<gpos>"+DisplayGetter+@")[ \t]*\r?\n"+
        FormatField("x",@"\k<pos>")+FormatField("y",@"\k<pos>")+FormatField("z",@"\k<pos>")+DisplaySink,
        RegexOptions.CultureInvariant|RegexOptions.Multiline);
    private static readonly Regex DisplayNames=new(@"\b__gcet(?:FrameDisplay(?:Sample|Read|Valid|Resolved)|ReadFrameDisplayPosition)\b",RegexOptions.CultureInvariant);

    internal static bool TryFrameDisplay(string original,out string output,out int sites)
    {
        output=original;sites=0;
        if(!original.Contains(Marker,StringComparison.Ordinal))return false;
        var nl=original.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var liveHeader=LiveHeader(nl);
        if(original.Split(liveHeader,StringSplitOptions.None).Length!=2)return false;
        var code=NativeReadReuseSource.MaskNonCode(original);
        if(ShadowGame.IsMatch(code)||Regex.IsMatch(code,
            @"\blocal\s+(?:ImGui|string)\b|\b(?:ImGui|string)(?:\s*\.\s*(?:Text|Checkbox|format))?\s*=(?!=)|\bfunction\s+[^(\r\n]*\([^)\r\n]*\b(?:ImGui|string)\b[^)\r\n]*\)"))
            return false;
        var header=FrameDisplayHeader.Replace("\r\n","\n",StringComparison.Ordinal).Replace("\n",nl,StringComparison.Ordinal)+nl+nl;
        var hasHeader=original.Contains(FrameDisplayMarker,StringComparison.Ordinal);
        var headerAt=original.IndexOf(header,StringComparison.Ordinal);
        if(hasHeader!=(headerAt>=0)||original.Split(header,StringSplitOptions.None).Length>2)return false;
        // Existing display adapters are recognized structurally, not by the
        // presence of a marker. Normalize only known generated fragments in
        // the candidate scope, and re-prove the entire original display sink.
        var outside=code.ToCharArray();
        void Mask(int start,int length){for(int j=start;j<start+length;j++)
            if(outside[j]!='\r'&&outside[j]!='\n')outside[j]=' ';}
        if(hasHeader)Mask(headerAt,header.Length);
        var edits=new List<(int start,int length,string value,int count)>();
        var admitted=0;
        foreach(Match fn in DisplayScope.Matches(code))
        {
            var actual=original.Substring(fn.Index,fn.Length);
            var plain=Regex.Replace(actual,
                @"(?m)^[ \t]*local "+DisplaySample+@" = "+DisplayRead+@"\(\)[ \t]*\r?\n","");
            plain=plain.Replace("("+DisplaySample+" or "+Helper+"())",Helper+"()",StringComparison.Ordinal)
                .Replace("("+DisplayRead+"() or "+Helper+"())",Helper+"()",StringComparison.Ordinal);
            if(DisplayNames.IsMatch(NativeReadReuseSource.MaskNonCode(plain)))continue;
            var m=DisplayTriple.Match(plain);
            var triple=m.Success;
            if(!triple)m=DisplayAlias.Match(plain);
            if(!m.Success||!actual.EndsWith(plain[m.Length..],StringComparison.Ordinal))continue;
            var plainCode=NativeReadReuseSource.MaskNonCode(plain);
            // A property token .x/.y/.z is not a reference to a local x/y/z.
            int Uses(string name)=>Regex.Matches(plainCode,@"(?<![\w.])"+Regex.Escape(name)+@"\b").Count;
            var names=new[]{"x","y","z"}.Select(k=>m.Groups[k].Value).ToArray();
            if(names.Distinct(StringComparer.Ordinal).Count()!=3||names.Any(n=>Uses(n)!=2))continue;
            if(!triple&&(names.Contains(m.Groups["pos"].Value)||Uses(m.Groups["pos"].Value)!=4))continue;
            var groups=(triple?new[]{"gx","gy","gz"}:new[]{"gpos"}).Select(k=>m.Groups[k]).ToArray();
            if(groups.Any(g=>!Regex.IsMatch(plainCode.Substring(g.Index,g.Length),@"\A"+DisplayGetter+@"\z")))continue;
            // All three component accesses are contiguous, pure formatting.
            // Read once per panel and preserve EACH original strict call on
            // unsupported/invalid native capability; no periodic subscription.
            var b=new StringBuilder(plain);
            foreach(var g in groups.Reverse())b.Remove(g.Index,g.Length).Insert(g.Index,
                "("+(triple?DisplaySample:DisplayRead+"()")+" or "+g.Value+")");
            if(triple)b.Insert(m.Groups["indentx"].Index,m.Groups["indentx"].Value+
                "local "+DisplaySample+" = "+DisplayRead+"()"+nl);
            var next=b.ToString();
            // Do not remove unknown prior display code even in a matching
            // function. Only the original or known exact generated forms
            // are allowed, including independently missing header/adapter.
            Mask(fn.Index,fn.Length);
            admitted+=groups.Length;
            if(next!=actual)edits.Add((fn.Index,fn.Length,next,groups.Length));
        }
        if(admitted==0||DisplayNames.IsMatch(new string(outside)))return false;
        if(edits.Count==0&&hasHeader)return false;
        var builder=new StringBuilder(original);
        foreach(var e in edits.AsEnumerable().Reverse())builder.Remove(e.start,e.length).Insert(e.start,e.value);
        if(!hasHeader)builder.Insert(0,header);
        output=builder.ToString();sites=edits.Sum(e=>e.count);
        if(sites==0)sites=admitted; // a missing exact adapter was restored
        return output!=original;
    }

    private static string LiveHeader(string nl)=>string.Join(nl,new []{
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

    internal static int TryStage(string modsRoot,
        Dictionary<string,byte[]> staged,
        Dictionary<string,int> appliedTransformsByFile,
        List<object> transformManifest,List<object> skipped,bool typedPositionEuler=false)
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
            if(ModOptimizationBlacklist.IsBlacklistedPath(relative) ||
               !CetModPresence.HasInstalledSource(root,relative))continue;
            var first=relative.Split('/')[0];
            if(first.Equals("0-Engine",StringComparison.OrdinalIgnoreCase) ||
               first.StartsWith("G-CET",StringComparison.OrdinalIgnoreCase))
                continue;
            var original=System.IO.File.ReadAllBytes(path);
            if(original.Length>2_000_000 || original.Any(b=>b==0))continue;
            var key=relative;
            var input=staged.TryGetValue(key,out var current)?current:original;
            var text=NativeReadReuseSource.Decode(input);
            var liveChanged=TryTransform(text,out var result,out var count);
            var displayChanged=TryFrameDisplay(result,out var display,out var displaySites);
            if(displayChanged)
            {
                if(LuaLocalBudgetAnalyzer.Analyze(display).MaxActiveLocals<=LuaLocalBudgetAnalyzer.Limit)
                    result=display;
                else
                {
                    displayChanged=false;
                    skipped.Add(new{type="NATIVE_PLAYER_POSITION_FRAME_DISPLAY",file=key,reason="LUA_LOCAL_LIMIT"});
                }
            }
            var pairChanged=ZeroEngineDemandSource.TryComposeExactPosePairs(result,typedPositionEuler,out var posePairs,out var pairSites);
            if(pairChanged)
            {
                if(LuaLocalBudgetAnalyzer.Analyze(posePairs).MaxActiveLocals<=LuaLocalBudgetAnalyzer.Limit)
                    result=posePairs;
                else
                {
                    pairChanged=false;
                    skipped.Add(new{type="NATIVE_EXACT_POSITION_EULER",file=key,reason="LUA_LOCAL_LIMIT"});
                }
            }
            if(!liveChanged&&!displayChanged&&!pairChanged)continue;
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
            // A display upgrade of an existing live helper is one additional
            // transform; stock direct replacements are already counted.
            var changes=count+(displayChanged&&count==0?1:0)+(pairChanged?Math.Max(1,pairSites):0);
            appliedTransformsByFile[key]=appliedTransformsByFile.GetValueOrDefault(key)+changes;
            if(count>0)transformManifest.Add(new {
                type="NATIVE_PLAYER_VECTOR_LIVE", contract=Contract,file=key,
                replacedOccurrences=count,receiverProof="DIRECT_GAME_GETPLAYER_EXPRESSION",
                source="ALL_INSTALLED_LUA_NOT_CAPTURE_GATED",
                backend="RED4EXT_TYPED_VECTOR4_FORCE_LIVE",
                originalFallback="Game.GetPlayer():GetWorldPosition()",
                cacheReuse=false,reason="CONSERVATIVE_CROSS_PHASE_FRESHNESS",
                runtimeCapability="GCETPlayerVector4Live+GCETPlayerVector4Valid",
                savings="NOT_MEASURED",doesNotRetireCallback=true
            });
            if(displayChanged)transformManifest.Add(new {
                type="NATIVE_PLAYER_POSITION_FRAME_DISPLAY",file=key,
                contract="GCET_NATIVE_PLAYER_FRAME_DISPLAY_V1",sourceProof=true,
                displayReadSites=displaySites,producerOwner="GCETStaticLocations.DemandFrameCache",
                backend="EXISTING_NATIVE_FRAME_SAMPLE",runtimeCapabilityBit=128,
                freshness="READ_ONLY_DISPLAY_FRAME_SAMPLE_NOT_EXACT_GAMEPLAY",
                originalFallback="EXISTING_STRICT_LIVE_HELPER",runtimePerformance="NOT_MEASURED",
                addsRecurringProducer=false
            });
            if(pairChanged)transformManifest.Add(new{
                type="NATIVE_EXACT_POSITION_EULER",file=key,sourceProof=true,
                exactPairs=pairSites,backend=typedPositionEuler?"EXISTING_ORDERED_NATIVE_POSE":"AUTHOR_EXACT_READS",
                engineReadsPerPairUnchanged=3,originalGetterOrderPreserved=true,
                nativeCrossingsPerPair=typedPositionEuler?1:3,
                coarseSpatialDonation=typedPositionEuler,
                runtimePerformance="NOT_MEASURED",addsRecurringProducer=false
            });
            applied+=changes;
        }
        return applied;
    }
}
