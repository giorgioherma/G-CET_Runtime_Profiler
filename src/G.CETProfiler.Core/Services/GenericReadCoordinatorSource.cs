using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Generic algorithm, per-source Lua worker: author gameplay stays in the mod.
// Only *our* exact proven shared-provider headers move into a separate chunk.
internal static class GenericReadCoordinatorSource
{
    internal const string Contract = "GCET_GENERIC_READ_COORDINATOR_V1";
    internal const string Marker = "-- G-CET generic native reads coordinator:v1 ";
    private const string WorkerMarker = "-- G-CET generic native reads worker:v1 ";
    private const string Alias = "__gcetNativeReads";
    private static readonly Regex ProviderHeader = new(
        @"(?m)^-- G-CET shared provider: (?<provider>[A-Z_]+)[ \t]*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex GameGetter = new(
        @"(?<![A-Za-z0-9_\.:])Game\s*\.\s*(?<getter>Get[A-Za-z0-9_]+)\s*\(\s*\)",
        RegexOptions.CultureInvariant);
    private static string Norm(string s) => s.Replace("\r\n","\n",StringComparison.Ordinal);
    private static string WorkerName(string relative) =>
        "gcet_native_reads_"+Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(relative.ToLowerInvariant())))
            [..10].ToLowerInvariant()+".lua";

    internal static int TryStage(string modsRoot,
        Dictionary<string,byte[]> staged,
        Dictionary<string,int> counts,List<object> transforms,List<object> skips,
        Func<string,IEnumerable<string>> headerForProvider)
    {
        var root=Path.GetFullPath(modsRoot);
        var result=0;
        foreach(var file in Directory.EnumerateFiles(root,"*.lua",SearchOption.AllDirectories)
                    .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            var rel=Path.GetRelativePath(root,file).Replace('\\','/');
            if(ModOptimizationBlacklist.IsBlacklistedPath(rel))continue;
            var owner=rel.Split('/')[0];
            if(!rel.Contains('/') ||
               owner is "0-Engine" or "G-CET" or "G-CET-Profiler" ||
               rel.Contains("/GCET_Semantic/",StringComparison.OrdinalIgnoreCase))
                continue;
            var original=staged.TryGetValue(rel,out var cached)?cached:File.ReadAllBytes(file);
            if(original.Length>2_000_000||original.Contains((byte)0))continue;
            var source=NativeReadReuseSource.Decode(original);
            var marked=source.Contains(Marker,StringComparison.Ordinal);
            var sourcePeak=LuaLocalBudgetAnalyzer.Analyze(original).MaxActiveLocals;
            var englishOnly=rel.Equals("hotscenes/init.lua",
                StringComparison.OrdinalIgnoreCase) &&
                source.Contains(HotscenesEnglishOnlySource.Marker,StringComparison.Ordinal);
            // A file may be under 197 locals now, yet a complete native-read
            // reuse pass would push it over 200. Project that pass, without
            // altering source, and extract our own provider helpers first.
            var hasProvider=source.Contains("-- G-CET shared provider: ",
                StringComparison.Ordinal);
            // A pristine author source has no G-CET provider helper yet.
            // Its *projected* complete native-read rewrite may exceed the
            // 200-local budget even when the original is below 197 locals.
            // Move the source-proven fallbacks into our existing per-file
            // coordinator before NativeReadReuseSource stages anything.
            var sourceGetterOperations = !marked
                ? NativeReadReuseSource.FindOperations(source)
                : Array.Empty<NativeReadReuseSource.Operation>();
            var hasAuthorGetters=!marked && sourceGetterOperations.Length>0;
            // Many independent lazy provider caches constitute substantial
            // persistent per-file optimization state, even when Lua's
            // 200-local limit is not yet pressured. Keep such G-CET-owned
            // state in one external source-owned worker, not a large preamble
            // of inline helpers. This decision uses observed current-source
            // getter families, never a mod name, filename inventory or an
            // old generated artifact.
            var currentProviderFamilies = new HashSet<string>(
                ProviderHeader.Matches(source).Cast<Match>()
                    .Select(m=>m.Groups["provider"].Value),
                StringComparer.Ordinal);
            foreach(var getter in sourceGetterOperations.Select(x=>x.Getter))
            {
                if(SharedProviderCatalog.ByGetter.TryGetValue(getter,out var family))
                    currentProviderFamilies.Add(family.Provider);
            }
            const int substantialProviderBundleMinimum = 9;
            var substantialProviderBundle = !marked &&
                currentProviderFamilies.Count>=substantialProviderBundleMinimum;
            var projectedOverflow=false;
            if(!marked && sourcePeak<197 && (hasProvider||hasAuthorGetters) &&
               NativeReadReuseSource.TryTransform(source,headerForProvider,
                   out var projected,out var operations) &&
               operations.Length>0)
                projectedOverflow=LuaLocalBudgetAnalyzer.Analyze(projected)
                    .MaxActiveLocals>LuaLocalBudgetAnalyzer.Limit;
            if(!marked && sourcePeak<197 && !projectedOverflow &&
               !substantialProviderBundle)
                continue;
            if(!marked && !hasProvider && !englishOnly && !hasAuthorGetters)
                continue;
            if(!TryRewrite(root,rel,source,staged,headerForProvider,
                out var rewritten,out var worker,out var workerRel,
                out var oldLocals,out var newLocals,out var occurrences,out var reason))
            {
                if(reason is not null)
                    skips.Add(new {type="GENERIC_READ_COORDINATOR",file=rel,
                        category="GENERATION_FAILURE",reason});
                continue;
            }
            if(source.Contains("\r\n",StringComparison.Ordinal))
                rewritten=rewritten.Replace("\n","\r\n",StringComparison.Ordinal);
            var encoded=Encoding.UTF8.GetBytes(rewritten);
            if(original.Length>=3&&original[0]==0xef&&original[1]==0xbb&&original[2]==0xbf)
                encoded=[0xef,0xbb,0xbf,..encoded];
            staged[rel]=encoded;
            staged[workerRel]=Encoding.UTF8.GetBytes(worker);
            counts[rel]=counts.GetValueOrDefault(rel)+occurrences;
            counts[workerRel]=counts.GetValueOrDefault(workerRel)+1;
            transforms.Add(new {type="GENERIC_READ_COORDINATOR",contract=Contract,
                file=rel,worker=workerRel,sourcePeak=oldLocals,generatedPeak=newLocals,
                adoptedOccurrences=occurrences,providerOwner="0-Engine",
                originalGameGetterFallbackPreserved=true,
                callbacksRetired=0});
            result++;
        }
        return result;
    }

    // Read the source's own generated provider block. Layout-only edits
    // (CRLF, indentation and empty lines) are acceptable; every executable
    // line must otherwise match the reviewed 0-Engine/Game fallback recipe.
    // Keep the actual original block when moving it to the new worker.
    internal static bool TryFindProviderBlock(string source,string provider,
        string getter,string reviewed,out string actual,out string detail)
    {
        actual="";
        detail="";
        // Inspect the source's *actual* generated helper, bounded by the
        // literal G-CET owner marker and unindented function terminator.
        // Do not pattern-match through other author functions or rely on
        // .NET regex multiline/Singleline interaction for source ownership.
        var marker="-- G-CET shared provider: "+provider;
        int first=-1, count=0;
        for(int at=source.IndexOf(marker,StringComparison.Ordinal);at>=0;
            at=source.IndexOf(marker,at+marker.Length,StringComparison.Ordinal))
        {
            if(at>0 && source[at-1]!='\n')continue;
            var after=at+marker.Length;
            if(after<source.Length && source[after]!='\n' &&
               source[after]!='\r' && source[after]!=' ' && source[after]!='\t')
                continue;
            count++;
            first=at;
        }
        if(count!=1)
        {
            detail="owner marker occurrences="+count;
            return false;
        }

        var cursor=source.IndexOf('\n',first);
        if(cursor<0)
        {
            detail="provider marker has no helper definition";
            return false;
        }
        var stop=-1;
        while(cursor<source.Length)
        {
            var lineStart=cursor+1;
            if(lineStart>=source.Length)break;
            var lineEnd=source.IndexOf('\n',lineStart);
            if(lineEnd<0)lineEnd=source.Length;
            // The generated outer helper ends at column zero. Nested
            // expressions use indented 'end', so scope remains bounded.
            if(source.AsSpan(lineStart,lineEnd-lineStart).TrimEnd().SequenceEqual(
                "end".AsSpan()))
            {
                stop=lineStart+3;
                break;
            }
            cursor=lineEnd;
        }
        if(stop<0)
        {
            detail="generated helper has no unindented closing end";
            return false;
        }
        actual=source[first..stop];
        static string[] NormalizedLines(string text) =>
            text.Replace("\r\n","\n",StringComparison.Ordinal).Split('\n')
                .Select(line=>line.Trim())
                .Where(line=>line.Length>0).ToArray();

        var actualLines=NormalizedLines(actual);
        var reviewedLines=NormalizedLines(reviewed);
        var mismatched=Enumerable.Range(0,Math.Min(actualLines.Length,reviewedLines.Length))
            .FirstOrDefault(i=>!string.Equals(actualLines[i],reviewedLines[i],
                StringComparison.Ordinal),-1);
        // Two source-owned G-CET releases generated the same fallback
        // helper with different GCET-table selection guards. Preserve the
        // installed recipe byte-for-byte when moving it into its worker;
        // never rewrite the author's original getter fallback. Only this
        // exact, getter-qualified historical guard is admitted.
        var historicalLines=reviewedLines.ToArray();
        var currentGuard=
            "local __gcetApi = type(__gcetEngine.GCET) == \"table\" and " +
            "type(__gcetEngine.GCET."+getter+") == \"function\" and " +
            "__gcetEngine.GCET or __gcetEngine";
        var historicalGuard=
            "local __gcetApi = type(__gcetEngine.GCET) == \"table\" and " +
            "__gcetEngine.GCET or __gcetEngine";
        var guardIndex=Array.IndexOf(historicalLines,currentGuard);
        var historicalMatch=false;
        if(guardIndex>=0)
        {
            historicalLines[guardIndex]=historicalGuard;
            historicalMatch=actualLines.SequenceEqual(historicalLines,
                StringComparer.Ordinal);
        }
        if((mismatched>=0 || actualLines.Length!=reviewedLines.Length) &&
           !historicalMatch)
        {
            detail=mismatched>=0
                ? "first differing generated-helper line "+(mismatched+1)+
                    " (source: "+actualLines[mismatched]+
                    "; expected: "+reviewedLines[mismatched]+")"
                : "helper length differs: source "+actualLines.Length+
                    " lines, expected "+reviewedLines.Length+" lines";
            actual="";
            return false;
        }
        if(!actual.Contains("return Game."+getter+"()",StringComparison.Ordinal))
        {
            detail="original Game getter fallback missing";
            actual="";
            return false;
        }
        return true;
    }

    internal static bool TryRewrite(string modsRoot,string relative,string input,
        Dictionary<string,byte[]> staged,Func<string,IEnumerable<string>> headers,
        out string output,out string worker,out string workerRel,
        out int oldPeak,out int newPeak,out int rewrittenCalls,out string? reason)
    {
        output=input;worker="";reason=null;rewrittenCalls=0;
        oldPeak=LuaLocalBudgetAnalyzer.Analyze(input).MaxActiveLocals;
        newPeak=oldPeak;
        var source=Norm(input);
        var owner=relative.Split('/')[0];
        var name=WorkerName(relative);
        workerRel=owner+"/GCET_Semantic/"+name;
        var module="GCET_Semantic/"+Path.GetFileNameWithoutExtension(name);
        var exists=source.Contains(Marker,StringComparison.Ordinal);
        // Never overwrite a user module or a different source's coordinator.
        if(!exists && (staged.ContainsKey(workerRel) ||
            File.Exists(Path.Combine(modsRoot,workerRel.Replace('/',
                Path.DirectorySeparatorChar)))))
        {
            reason="Read-worker target path is already owned by another file.";
            return false;
        }
        var functions=new Dictionary<string,string>(StringComparer.Ordinal);
        if(exists)
        {
            if(!source.StartsWith(Marker+module+"\n",StringComparison.Ordinal) ||
               !Regex.IsMatch(NativeReadReuseSource.MaskNonCode(source),
                   @"\blocal\s+"+Alias+@"\s*=\s*require\s*\("))
            {reason="Read coordinator source bridge changed.";return false;}
            var physical=Path.Combine(modsRoot,workerRel.Replace('/',Path.DirectorySeparatorChar));
            var loaded=staged.TryGetValue(workerRel,out var pending)
                ?NativeReadReuseSource.Decode(pending)
                :File.Exists(physical)?File.ReadAllText(physical):"";
            worker=Norm(loaded);
            if(!worker.StartsWith(WorkerMarker+relative+"\n",StringComparison.Ordinal))
            {reason="Existing worker missing or owner changed.";return false;}
            foreach(Match marker in ProviderHeader.Matches(worker))
            {
                var key=marker.Groups["provider"].Value;
                if(!SharedProviderCatalog.ByProvider.TryGetValue(key,out var p)||
                   functions.ContainsKey(p.Getter))
                {reason="Existing worker provider unknown or duplicated: "+key;return false;}
                var reviewed=string.Join("\n",headers(key)).TrimEnd('\n','\r');
                var detail="reviewed worker provider recipe is missing";
                if(reviewed.Length==0||!TryFindProviderBlock(worker,
                    key,p.Getter,reviewed,out var block,out detail))
                {reason="Existing worker provider differs from reviewed fallback: "+
                    key+"; "+detail;return false;}
                functions.Add(p.Getter,block);
            }
            if(functions.Count==0)
            {reason="Existing worker has no recognized G-CET fallbacks.";return false;}
        }
        else
        {
            var code=NativeReadReuseSource.MaskNonCode(source);
            // A package with its own require allowlist (for example, a
            // deferred native-handshake factory) needs a bespoke reviewed
            // bridge, not an automatic module import.
            if(Regex.IsMatch(code,
                @"\brequire\s*=\s*function\b|\bfunction\s+require\s*\("))
            {reason="Author package overrides require; generic worker import is not proven.";return false;}
            if(Regex.IsMatch(code,@"\b"+Alias+@"\b"))
            {reason="Coordinator alias conflicts with existing source.";return false;}
            var existing=ProviderHeader.Matches(source).Cast<Match>().ToArray();
            // No previously generated headers is the normal pristine Nexus
            // case. Allow only exact authorized author getter calls; the
            // worker below generates the SAME reviewed Game-getter fallbacks.
            if(existing.Length==0 && !(relative.Equals("hotscenes/init.lua",
                    StringComparison.OrdinalIgnoreCase) &&
                source.Contains(HotscenesEnglishOnlySource.Marker,
                    StringComparison.Ordinal)) &&
                NativeReadReuseSource.FindOperations(source).Length==0)
                return false;
            foreach(var match in existing)
            {
                var providerName=match.Groups["provider"].Value;
                if(!SharedProviderCatalog.ByProvider.TryGetValue(providerName,out var p)||
                   functions.ContainsKey(p.Getter))
                {reason="Existing G-CET provider unrecognized or duplicated: "+providerName;return false;}
                var reviewed=string.Join("\n",headers(providerName)).TrimEnd('\n','\r');
                var detail="reviewed source provider recipe is missing";
                if(reviewed.Length==0||!TryFindProviderBlock(source,
                    providerName,p.Getter,reviewed,out var block,out detail))
                {reason="Existing G-CET provider fallback has semantic source drift: "+
                    providerName+"; "+detail;return false;}
                functions.Add(p.Getter,block);
            }
            foreach(var block in functions.Values)source=source.Replace(block,"",StringComparison.Ordinal);
        }

        // No local shadow of CET's Game facade: preserve original caller values.
        var lex=NativeReadReuseSource.MaskNonCode(source);
        if(Regex.IsMatch(lex,
            @"\blocal\s+Game\b|\bGame\s*=(?!=)|\bfunction\s*\w+\s*\([^)]*\bGame\b[^)]*\)"))
        {reason="Game facade is shadowed; native getter ownership uncertain.";return false;}

        var calls=GameGetter.Matches(lex).Cast<Match>()
            .Where(m=>SharedProviderCatalog.ByGetter.ContainsKey(m.Groups["getter"].Value))
            .ToArray();
        foreach(var getter in calls.Select(m=>m.Groups["getter"].Value).Distinct())
        {
            if(functions.ContainsKey(getter))continue;
            var p=SharedProviderCatalog.ByGetter[getter];
            var block=string.Join("\n",headers(p.Provider)).TrimEnd('\n','\r');
            if(block.Length==0){reason="Missing 0-Engine fallback for "+getter;return false;}
            functions.Add(getter,block);
        }
        var substitutions=new List<(int Index,int Length,string Value)>();
        foreach(var m in calls)
            substitutions.Add((m.Index,m.Length,Alias+"."+m.Groups["getter"].Value+"()"));
        if(!exists)
        {
            var funcs=new Regex(@"\b__gcet(?:"
                +string.Join("|",functions.Keys.Select(Regex.Escape))+@")\b");
            foreach(Match m in funcs.Matches(lex))
                substitutions.Add((m.Index,m.Length,Alias+"."+m.Value[6..]));
        }
        if(exists)
        {
            // Reconstruct exactly what the previously generated coordinator
            // should contain. An author-edited worker is not disposable.
            var existingWorkerSnapshot=worker;
            var canonical=WorkerMarker+relative+"\n"+
                string.Join("\n\n",functions
                    .Where(x=>existingWorkerSnapshot.Contains(x.Value,StringComparison.Ordinal))
                    .OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>x.Value))+
                "\n\nreturn { "+
                string.Join(", ",functions
                    .Where(x=>existingWorkerSnapshot.Contains(x.Value,StringComparison.Ordinal))
                    .Select(x=>x.Key)
                    .OrderBy(x=>x,StringComparer.Ordinal)
                    .Select(x=>x+" = __gcet"+x))+" }\n";
            if(!string.Equals(worker,canonical,StringComparison.Ordinal))
            {
                reason="Existing source-owned read worker has unrelated edits; refusing overwrite.";
                return false;
            }
        }
        if(exists && calls.Length==0)return false;
        var builder=new StringBuilder(source);
        foreach(var patch in substitutions.OrderByDescending(x=>x.Index))
            builder.Remove(patch.Index,patch.Length).Insert(patch.Index,patch.Value);
        output=exists?builder.ToString():
            Marker+module+"\nlocal "+Alias+" = require(\""+module+"\")\n"+builder;
        newPeak=LuaLocalBudgetAnalyzer.Analyze(output).MaxActiveLocals;
        if(newPeak>LuaLocalBudgetAnalyzer.Limit){output=input;reason=null;return false;}
        worker=WorkerMarker+relative+"\n"+
            string.Join("\n\n",functions.OrderBy(x=>x.Key,StringComparer.Ordinal)
                .Select(x=>x.Value))+
            "\n\nreturn { "+
            string.Join(", ",functions.Keys.OrderBy(x=>x,StringComparer.Ordinal)
                .Select(x=>x+" = __gcet"+x))+" }\n";
        rewrittenCalls=calls.Length;
        return true;
    }
}
