using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Immutable positions are compiled from THE INSTALLED AUTHOR FILES, not an
// outdated reference ZIP or hardcoded source SHA. Unsupported source shape
// simply omits that owner: the existing source-owned Lua registration stays
// available (including mandatory dynamic REDscript-backed mechanic sites).
internal sealed record CompiledNativeLocations(byte[] Bytes,
    IReadOnlyDictionary<int,int> OwnerCounts)
{
    internal const string GameRootRelativePath =
        "red4ext/plugins/G-CET-StaticLocations/G-CET.locations";

    private readonly record struct Site(int Owner, float X,float Y,float Z,
                                        float Radius,bool XY=false);
    private const string Num=@"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";
    private static readonly Regex XYZ = new(
        @"\bx\s*=\s*(?<x>"+Num+@")\s*,\s*y\s*=\s*(?<y>"+Num+
        @")\s*,\s*z\s*=\s*(?<z>"+Num+@")",
        RegexOptions.CultureInvariant);
    private static readonly Regex Poker = new(
        @"\bcenter\s*=\s*\{\s*x\s*=\s*(?<x>"+Num+
        @")\s*,\s*y\s*=\s*(?<y>"+Num+
        @")\s*\}\s*,\s*surfaceZ\s*=\s*"+Num+
        @"\s*,\s*floorZ\s*=\s*(?<z>"+Num+")",
        RegexOptions.CultureInvariant);
    private static readonly Regex Card = new(
        @"\bpos\s*=\s*\{\s*x\s*=\s*(?<x>"+Num+
        @")\s*,\s*y\s*=\s*(?<y>"+Num+
        @")\s*,\s*z\s*=\s*(?<z>"+Num+@")\s*\}",
        RegexOptions.CultureInvariant);
    private static readonly Regex Scalar = new(
        @"(?<n>"+Num+")",RegexOptions.CultureInvariant);

    internal static CompiledNativeLocations Compile(
        string modsRoot, IReadOnlyDictionary<string,byte[]> staged)
    {
        var sites=new List<Site>();
        var counts=new SortedDictionary<int,int>();
        // Source is only admitted when this exact owner has an installed,
        // successfully generated semantic stage. Do not add anchor demand for
        // the other user's installed or absent mods.
        bool Has(string relative,string marker) =>
            staged.TryGetValue(relative,out var bytes) &&
            Encoding.UTF8.GetString(bytes).Contains(marker,StringComparison.Ordinal);

        string? Read(string relative) {
            var path=Path.Combine(modsRoot,
                relative.Replace('/',Path.DirectorySeparatorChar));
            if (staged.TryGetValue(relative,out var data))
                return Encoding.UTF8.GetString(data);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        void Include(int owner, IEnumerable<Site> rows) {
            var list=rows.ToArray();
            if(list.Length==0 || list.Length>100000)return;
            if(list.Any(v=>v.Owner!=owner || !float.IsFinite(v.X) ||
                   !float.IsFinite(v.Y) || !float.IsFinite(v.Z) ||
                   !float.IsFinite(v.Radius) || v.Radius<=0 ||
                   v.Radius>1024 || Math.Abs(v.X)>1000000 ||
                   Math.Abs(v.Y)>1000000 || Math.Abs(v.Z)>1000000))
                return;
            counts[owner]=list.Length;
            sites.AddRange(list);
        }
        static float Number(string s) =>
            float.Parse(s,NumberStyles.Float,CultureInfo.InvariantCulture);
        static List<Site> Parse(int owner,string? text,Regex regex,float radius,
                                int? exact=null) {
            var result=new List<Site>();
            if(text is null)return result;
            foreach(Match m in regex.Matches(text))
                result.Add(new Site(owner,Number(m.Groups["x"].Value),
                    Number(m.Groups["y"].Value),Number(m.Groups["z"].Value),radius));
            return exact.HasValue && result.Count!=exact.Value
                ? new List<Site>() : result;
        }
        static List<Site> ParseNamed(int owner,string? src,
                                    string name,float radius) {
            if(src is null)return new List<Site>();
            var m=Regex.Match(src,@"\blocal\s+"+Regex.Escape(name)+
                @"\s*=\s*\{(?<v>[^\}]+)\}",RegexOptions.CultureInvariant);
            if(!m.Success)return new List<Site>();
            return Parse(owner,m.Groups["v"].Value,XYZ,radius,1);
        }
        static Site? JsonPosition(int owner,JsonElement p,float radius,
                                  float zOffset=0) {
            if(p.ValueKind!=JsonValueKind.Array || p.GetArrayLength()!=3)
                return null;
            return new Site(owner,(float)p[0].GetDouble(),
                (float)p[1].GetDouble(),(float)p[2].GetDouble()+zOffset,radius);
        }
        static List<Site> JsonPair(string? src,int owner) {
            if(src is null)return new List<Site>();
            try {
                using var json=JsonDocument.Parse(src);
                var rescue=json.RootElement.GetProperty("rescue");
                var a=JsonPosition(owner,rescue.GetProperty("surface"),150);
                var b=JsonPosition(owner,rescue.GetProperty("brother"),150);
                return a.HasValue && b.HasValue ?
                    new List<Site>{a.Value,b.Value}:new List<Site>();
            } catch(JsonException) {return new List<Site>();}
              catch(KeyNotFoundException) {return new List<Site>();}
              catch(InvalidOperationException) {return new List<Site>();}
        }
        static List<Site> BankerSites(string? src) {
            if(src is null)return new List<Site>();
            // Current optimized PosData.Bankers uses numeric tables; older
            // source defines exactly the same three sites as quoted positions.
            var banker=Regex.Match(src,
                @"\bPosData\.Bankers\s*=\s*\{(?<v>[\s\S]*?)\n\}",
                RegexOptions.CultureInvariant);
            if(banker.Success) {
                var listed=Parse(10036,banker.Groups["v"].Value,XYZ,10,3);
                if(listed.Count==3)return listed;
            }
            var original=Regex.Match(src,
                @"\bPOSData_NCBankLocation\s*=\s*\{(?<v>[\s\S]*?)\n\}",
                RegexOptions.CultureInvariant);
            if(!original.Success)return new List<Site>();
            var rows=Regex.Matches(original.Groups["v"].Value,
                "\"(?<numbers>[-+0-9.eE\\s]+)\"",RegexOptions.CultureInvariant);
            if(rows.Count!=3)return new List<Site>();
            var list=new List<Site>();
            foreach(Match row in rows) {
                var numbers=Scalar.Matches(row.Groups["numbers"].Value);
                if(numbers.Count<3)return new List<Site>();
                list.Add(new Site(10036,Number(numbers[0].Value),
                    Number(numbers[1].Value),Number(numbers[2].Value),10));
            }
            return list;
        }

        if(Has("StreetGamesPoker/init.lua","G-CET native location:poker-"))
        {
            var init=Read("StreetGamesPoker/init.lua");
            var radius=init is null ? Match.Empty :
                Regex.Match(init,@"\bPOKER_WAKE_RADIUS\s*=\s*(?<r>"+Num+")");
            var src=Read("StreetGamesPoker/StreetTables.lua");
            if(radius.Success && src?.Contains("StreetTables.locations = {",
                                               StringComparison.Ordinal)==true)
                Include(10031,Parse(10031,src,Poker,Number(radius.Groups["r"].Value)));
        }

        if(Has("tunnel_rescue/init.lua","G-CET native location:tunnel-"))
            Include(10032,JsonPair(Read("tunnel_rescue/layout.json"),10032));

        // The REDscript registry is fixed author source, despite being
        // exposed to CET via a runtime system. Bind each mechanic independently.
        // Native wakes at least 15m OUTSIDE the largest of its discovery
        // polygon, 95m despawn and 75m spawn boundaries. No per-site
        // coordinate/geometry is inferred from the current player position.
        // An absent or drifted REDscript registry leaves the original
        // always-running Lua NPC supervision and REDscript discovery intact.
        if(Has("illegal_mechanic_phase1a/physical_npc.lua",
               "G-CET native location:illegal-v3"))
        {
            var game=new DirectoryInfo(Path.GetFullPath(modsRoot));
            // .../Cyberpunk 2077/bin/x64/plugins/cyber_engine_tweaks/mods
            for(var depth=0;depth<5 && game.Parent is not null;depth++)
                game=game.Parent;
            var reds=Path.Combine(game.FullName,"r6","scripts",
                "zzz_illegal_mechanic_phase1a","zzz_IllegalMechanicTypes.reds");
            if(File.Exists(reds)) {
                var definition=File.ReadAllText(reds);
                var blocks=Regex.Matches(definition,
                    @"data\s*=\s*new\s+IllegalMechanicDefinition\(\)\s*;(?<body>[\s\S]*?)ArrayPush\s*\(\s*result\s*,\s*data\s*\)\s*;",
                    RegexOptions.CultureInvariant);
                var candidates=new List<Site>();
                var complete=blocks.Count is >=1 and <=256;
                for(var index=0;index<blocks.Count && complete;index++) {
                    var body=blocks[index].Groups["body"].Value;
                    var idMatch=Regex.Match(body,
                        @"data\.mechanicID\s*=\s*n""[^""]+""\s*;");
                    var center=Regex.Match(body,
                        @"data\.npcPosition\s*=\s*new\s+Vector4\s*\(\s*(?<x>"+Num+
                        @")\s*,\s*(?<y>"+Num+@")\s*,\s*(?<z>"+Num+@")\s*,");
                    var vertices=Regex.Matches(body,
                        @"ArrayPush\s*\(\s*data\.discoveryVertices\s*,\s*new\s+Vector4\s*\(\s*(?<x>"+Num+
                        @")\s*,\s*(?<y>"+Num+@")\s*,\s*(?<z>"+Num+@")\s*,");
                    var zLow=Regex.Match(body,@"data\.discoveryLowestPointZ\s*=\s*"+Num);
                    var zHigh=Regex.Match(body,@"data\.discoveryHighestPointZ\s*=\s*"+Num);
                    var zTolerance=Regex.Match(body,@"data\.discoveryZTolerance\s*=\s*"+Num);
                    if(!idMatch.Success || !center.Success || vertices.Count==0 ||
                       !zLow.Success || !zHigh.Success || !zTolerance.Success){
                        complete=false;break;
                    }
                    var x=Number(center.Groups["x"].Value);
                    var y=Number(center.Groups["y"].Value);
                    var z=Number(center.Groups["z"].Value);
                    var maxXY=0d;
                    foreach(Match vertex in vertices) {
                        var dx=Number(vertex.Groups["x"].Value)-x;
                        var dy=Number(vertex.Groups["y"].Value)-y;
                        maxXY=Math.Max(maxXY,Math.Sqrt(dx*dx+dy*dy));
                    }
                    // Original REDscript discovery uses XY plus its own
                    // Z slab. Conservative 3D encompassing sphere also
                    // accounts for the author slab and outlying vertices.
                    var low=Number(zLow.Value.Split('=').Last().Trim());
                    var high=Number(zHigh.Value.Split('=').Last().Trim());
                    var tolerance=Number(zTolerance.Value.Split('=').Last().Trim());
                    var vertical=Math.Max(Math.Abs(z-(low-tolerance)),
                                          Math.Abs(high+tolerance-z));
                    var discovery=Math.Sqrt(maxXY*maxXY+vertical*vertical);
                    var radius=(float)Math.Max(110d,Math.Ceiling(discovery+15d));
                    if(!float.IsFinite(radius) || radius>1024) {
                        complete=false;break;
                    }
                    candidates.Add(new Site(1003301+index,x,y,z,radius));
                }
                if(complete && candidates.Count==blocks.Count) {
                    foreach(var candidate in candidates)
                        Include(candidate.Owner,new[]{candidate});
                }
            }
        }

        if(Has("Dedka Auto Shop/init.lua","G-CET native location:dedka-")) {
            var src=Read("Dedka Auto Shop/init.lua");
            var first=ParseNamed(10034,src,"ENTRANCE_POS",200);
            var second=ParseNamed(10034,src,"ENTRANCE_POS_2",200);
            if(first.Count==0 || second.Count==0) {
                first=ParseNamed(10034,src,"entrancePos",200);
                second=ParseNamed(10034,src,"entrancePos_2",200);
            }
            if(first.Count==1 && second.Count==1)
                Include(10034,first.Concat(second));
        }
        if(Has("Card Collection/init.lua","G-CET native location:card-collection-")){
            var src=Read("Card Collection/init.lua");
            // Require author card ID as well as its coordinate: no incidental
            // UI/vector literal is allowed into the native location table.
            if(src?.Contains("local cards = {",StringComparison.Ordinal)==true)
                Include(10035,Parse(10035,src,Card,20,100));
        }
        if(Has("marmurbank/module/Bank.lua","G-CET native location:marmurbank-"))
            Include(10036,BankerSites(Read("marmurbank/data/PosData.lua")));

        if(Has("yaibaPenthouse/bathtub.lua","G-CET native location:yaiba-bathtub-")){
            var src=Read("yaibaPenthouse/bathtub.lua");
            if(src is not null){
                var m=Regex.Match(src,@"\bposition\s*=\s*\{(?<v>[^\}]+)\}",
                    RegexOptions.CultureInvariant);
                var icon=Regex.Match(src,@"\biconRange\s*=\s*(?<r>"+Num+")",
                    RegexOptions.CultureInvariant);
                if(m.Success && icon.Success){
                    var original=Parse(10037,m.Groups["v"].Value,XYZ,
                        Number(icon.Groups["r"].Value)+1,1);
                    if(original.Count==1){
                        var v=original[0];
                        Include(10037,new[]{v with {Z=v.Z-1}});
                    }
                }
            }
        }

        if(Has("ziggy_last_play/init.lua","G-CET native location:ziggy-offered-")) {
            var src=Read("ziggy_last_play/quest.json");
            if(src is not null) {
                try {
                    using var json=JsonDocument.Parse(src);
                    var giver=JsonPosition(10038,
                        json.RootElement.GetProperty("giver").GetProperty("position"),130);
                    if(giver.HasValue)Include(10038,new[]{giver.Value});
                }catch(JsonException){}catch(KeyNotFoundException){}
                 catch(InvalidOperationException){}
            }
        }

        if(sites.Count>100000)
            throw new InvalidOperationException("Compiled static location limit exceeded.");
        using var stream=new MemoryStream();
        using var w=new BinaryWriter(stream,Encoding.UTF8,true);
        w.Write(Encoding.ASCII.GetBytes("GCAS"));
        w.Write((uint)1);w.Write((uint)sites.Count);w.Write((uint)0);
        var id=0;
        foreach(var site in sites) {
            w.Write(++id);w.Write(site.X);w.Write(site.Y);
            w.Write(site.Z);w.Write(site.Radius);
            w.Write(site.XY ? 1u:0u);w.Write((uint)site.Owner);
        }
        w.Flush();
        return new CompiledNativeLocations(stream.ToArray(),counts);
    }
}
