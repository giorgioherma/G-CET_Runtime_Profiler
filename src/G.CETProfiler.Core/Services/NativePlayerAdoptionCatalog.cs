using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Whole-installed-Lua discovery, independent of callback execution/capture.
/// An identity tag does NOT supply a typed, frame-fresh native Vector4.
/// This catalog never authorizes an arbitrary GetWorldPosition substitution.
/// </summary>
internal static class NativePlayerAdoptionCatalog
{
    internal const string Contract = "GCET_NATIVE_PLAYER_ADOPTION_DISCOVERY_V1";
    internal sealed record Site(
        int Line,string Method,string Receiver,string IdentityTag,
        string SourceProof,string? ReceiverClass,string NativeFamily,
        bool NativeRewriteAuthorized,string MissingContract);
    internal sealed record File(string RelativeFile,Site[] Sites);
    internal sealed record Inventory(
        string Contract,string Source,string Status,
        int FilesScanned,int FilesSkipped,int TotalGetterSites,
        int SourcePlayerGetterSites,int OtherOrUnknownGetterSites,
        int NativeRewriteAuthorizedSites,File[] Files);

    internal static Inventory Scan(string modsRoot)
    {
        var files=new List<File>();
        var total=0;
        var player=0;
        var scanned=0;
        var skipped=0;
        var authorized=0;
        if(!Directory.Exists(modsRoot))
            return new(Contract,"INSTALLED_LUA_SOURCES","MODS_FOLDER_MISSING",
                0,0,0,0,0,0,[]);
        var root=Path.GetFullPath(modsRoot);
        try
        {
            foreach(var path in Directory.EnumerateFiles(root,"*.lua",
                        SearchOption.AllDirectories)
                     .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).Take(20000))
            {
                var rel=Path.GetRelativePath(root,path).Replace('\\','/');
                var folder=rel.Split('/')[0];
                if(folder.Equals("0-Engine",StringComparison.OrdinalIgnoreCase) ||
                   folder.StartsWith("G-CET",StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] bytes;
                try {bytes=System.IO.File.ReadAllBytes(path);}
                catch {skipped++;continue;}
                if(bytes.Length>2_000_000||Array.IndexOf(bytes,(byte)0)>=0)
                {skipped++;continue;}
                scanned++;
                var text=Encoding.UTF8.GetString(bytes);
                PlayerOwnershipEvidenceSource.Callsite[] points;
                try{points=PlayerOwnershipEvidenceSource.Analyze(text);}
                catch{skipped++;continue;}
                if(points.Length==0)continue;
                var sites=points.Select(x=>
                {
                    // The native plugin's exported XYZ scalars are not an
                    // equivalent substitute for a CET Vector4 with specific
                    // frame/lifecycle semantics. An installed native plugin
                    // does not establish this missing API contract.
                    var isPlayer=x.ReceiverTag is
                        "CURRENT_PLAYER_SOURCE_PROVEN" or
                        "PLAYER_ALIAS_SOURCE_EVIDENCE" or
                        "PLAYER_PUPPET_OBSERVER_RECEIVER" or
                        "PLAYER_PUPPET_OBSERVER_ALIAS";
                    if(isPlayer)player++;
                    if(x.Method=="GetWorldPosition" &&
                       x.ReceiverTag=="CURRENT_PLAYER_SOURCE_PROVEN")authorized++;
                    total++;
                    var directLive=x.Method=="GetWorldPosition" &&
                        x.ReceiverTag=="CURRENT_PLAYER_SOURCE_PROVEN";
                    return new Site(x.LineOffset+1,x.Method,x.ReceiverExpression,
                        x.ReceiverTag,x.Proof,x.ReceiverClass,
                        x.Method=="GetWorldPosition"?"NATIVE_PLAYER_TYPED_VECTOR4":
                        "NO_NATIVE_EQUIVALENT",directLive,
                        directLive
                          ?"AUTO_GUARDED_STRICT_LIVE_WITH_ORIGINAL_FALLBACK"
                          :x.Method=="GetWorldPosition"
                          ?"IDENTITY_OR_SAME_FRAME_SEMANTICS_NOT_PROVEN"
                          :"NO_COMPATIBLE_NATIVE_API");
                }).ToArray();
                files.Add(new File(rel,sites));
            }
        }
        catch(IOException){skipped++;}
        catch(UnauthorizedAccessException){skipped++;}
        return new(Contract,"INSTALLED_LUA_SOURCES",
            skipped==0?"INVENTORY_COMPLETE_WITHIN_SCAN_LIMITS":"PARTIAL_SCAN_FAILURE",
            scanned,skipped,total,player,total-player,authorized,files.ToArray());
    }

    internal static void Write(string modsRoot,string output)
    {
        var inventory=Scan(modsRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        var json=JsonSerializer.Serialize(inventory,
            new JsonSerializerOptions{WriteIndented=true});
        System.IO.File.WriteAllText(output,json);
    }
}
