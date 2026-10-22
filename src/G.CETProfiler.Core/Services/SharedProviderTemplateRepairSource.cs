using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Repairs one malformed *G-CET-generated* Lua expression produced by an
/// earlier non-interpolated C# source template. The repair is source-proven,
/// version-neutral and performed only in staged pass output. No author
/// declaration, callback, source hash, or G-CET worker is bypassed.
/// </summary>
internal static class SharedProviderTemplateRepairSource
{
    internal const string Contract="GCET_PROVIDER_INTERPOLATION_REPAIR_V1";
    internal const string BrokenExpression="__gcetEngine.GCET.{recipe.EngineGetter}";
    private static readonly Regex OwnerMarker=new(
        @"(?m)^-- G-CET shared provider: (?<provider>[A-Z_]+)[ \t]*\r?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex GeneratedEnd=new(
        @"(?m)^end[ \t]*\r?$",RegexOptions.CultureInvariant);

    internal static int TryStage(string modsRoot,Dictionary<string,byte[]> staged,
        Dictionary<string,int> counts,List<object> transformed,List<object> skipped,
        Func<string,IEnumerable<string>> headerForProvider)
    {
        var root=Path.GetFullPath(modsRoot);
        var applied=0;
        foreach(var file in Directory.EnumerateFiles(root,"*.lua",SearchOption.AllDirectories)
                     .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase))
        {
            var relative=Path.GetRelativePath(root,file).Replace('\\','/');
            if(!relative.Contains('/') ||
               relative.StartsWith("0-Engine/",StringComparison.OrdinalIgnoreCase))
                continue;
            var bytes=staged.TryGetValue(relative,out var stagedBytes)?
                stagedBytes:File.ReadAllBytes(file);
            if(bytes.Length>2_000_000 || bytes.Contains((byte)0))continue;
            var original=NativeReadReuseSource.Decode(bytes);
            if(!original.Contains(BrokenExpression,StringComparison.Ordinal))continue;
            if(!TryRepair(original,headerForProvider,
                    out var patched,out var count,out var families,out var reason))
            {
                skipped.Add(new {type="GENERIC_PROVIDER_TEMPLATE_REPAIR",
                    category="GENERATION_FAILURE",file=relative,reason});
                continue;
            }
            var outputBytes=Encoding.UTF8.GetBytes(patched);
            if(bytes.Length>=3&&bytes[0]==0xEF&&bytes[1]==0xBB&&bytes[2]==0xBF)
                outputBytes=[0xEF,0xBB,0xBF,..outputBytes];
            staged[relative]=outputBytes;
            counts[relative]=counts.GetValueOrDefault(relative)+count;
            transformed.Add(new {type="GENERIC_PROVIDER_TEMPLATE_REPAIR",
                contract=Contract,file=relative,
                previouslyMalformedExpressionsRepaired=count,
                providerFamilies=families,
                verifiedAgainstCanonicalSharedProviderRecipe=true,
                authorCallbacksChanged=false,
                originalGameGetterFallbackPreserved=true});
            applied++;
        }
        return applied;
    }

    internal static bool TryRepair(string source,
        Func<string,IEnumerable<string>> headerForProvider,
        out string result,out int changes,out string[] families,out string? reason)
    {
        result=source;changes=0;families=[];reason=null;
        if(!source.Contains(BrokenExpression,StringComparison.Ordinal))return false;

        var fixedFamilies=new List<string>();
        // Work from the right so original source indices remain valid.
        var markers=OwnerMarker.Matches(source).Cast<Match>()
            .OrderByDescending(m=>m.Index).ToArray();
        foreach(var marker in markers)
        {
            var provider=marker.Groups["provider"].Value;
            if(!SharedProviderCatalog.ByProvider.TryGetValue(provider,out var recipe))
                continue;
            var afterMarker=source.IndexOf('\n',marker.Index);
            if(afterMarker<0)continue;
            var terminal=GeneratedEnd.Match(source,afterMarker+1);
            if(!terminal.Success)continue;
            var end=terminal.Index+terminal.Length;
            var block=source[marker.Index..end];
            var broken=block.Split(BrokenExpression,StringSplitOptions.None).Length-1;
            if(broken==0)continue;
            // A valid generated helper has this expression exactly once.
            // Refuse partial/ambiguous code rather than replace source text
            // belonging to another structure.
            if(broken!=1)
            {
                reason="Multiple malformed expressions in one G-CET provider: "+provider;
                return false;
            }
            var corrected=block.Replace(BrokenExpression,
                "__gcetEngine.GCET."+recipe.Getter,StringComparison.Ordinal);
            var candidate=result[..marker.Index]+corrected+result[end..];
            var reviewed=string.Join("\n",headerForProvider(provider)).TrimEnd('\r','\n');
            var mismatch="Missing reviewed fallback recipe";
            if(reviewed.Length==0 ||
               !GenericReadCoordinatorSource.TryFindProviderBlock(candidate,
                   provider,recipe.Getter,reviewed,out _,out mismatch))
            {
                reason="Malformed G-CET provider cannot be source-verified: "+
                    provider+"; "+mismatch;
                return false;
            }
            result=candidate;changes++;fixedFamilies.Add(provider);
        }

        if(changes==0 || result.Contains(BrokenExpression,StringComparison.Ordinal))
        {
            reason="Unowned or incomplete G-CET interpolation placeholder; refusing broad replacement.";
            result=source;changes=0;families=[];
            return false;
        }
        if(LuaLocalBudgetAnalyzer.Analyze(result).MaxActiveLocals >
           LuaLocalBudgetAnalyzer.Limit)
        {
            reason="Canonical provider repair would exceed Lua local budget.";
            result=source;changes=0;families=[];
            return false;
        }
        families=fixedFamilies.OrderBy(x=>x,StringComparer.Ordinal).ToArray();
        return true;
    }
}
