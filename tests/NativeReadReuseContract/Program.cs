using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool yes,string why)
{
    if(!yes)throw new InvalidOperationException(why);
}
static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
var core=typeof(PassGeneratorService).Assembly;
var cls=core.GetType("GCETRuntimeProfiler.Core.Services.NativeReadReuseSource",true)!;
MethodInfo M(string name)=>cls.GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
var find=M("FindOperations");
var analyze=M("Analyze");
var transform=M("TryTransform");
var stage=M("TryStage");
var root=Path.Combine(Path.GetTempPath(),"gcet-native-read-reuse-"+Guid.NewGuid().ToString("N"));
var mods=Path.Combine(root,"mods");
Directory.CreateDirectory(mods);
try
{
    string Save(string rel,string contents)
    {
        var path=Path.Combine(mods,rel.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,contents,new UTF8Encoding(false));
        return path;
    }
    var main=Save("Example/init.lua", """
        -- Game.GetPlayer() should not change
        local quoted = "Game.GetQuestsSystem()"
        local raw = [=[Game.GetPlayer()]=]
        local actor = Game.GetPlayer()
        local quests = Game.GetQuestsSystem()
        local xyz = actor:GetWorldPosition()
        local data = Game.GetPlayer():GetWorldPosition()
        """.Replace("\n","\r\n"));
    Save("Other/helper.lua","return Game.GetTransactionSystem()\n");
    Save("0-Engine/init.lua","return Game.GetPlayer()\n");
    Save("Shadow/init.lua","local Game = require('Game')\nreturn Game.GetPlayer()\n");
    Save("OnlyComments/init.lua","-- Game.GetPlayer()\nreturn 'Game.GetPlayer()'\n");
    Save("PreviouslyApplied/init.lua","-- G-CET shared provider: PLAYER\nlocal function __gcetGetPlayer() return Game.GetPlayer() end\n");
    // Scanner must see exact code calls, ignore literals, disallow local Game
    // shadowing, and preserve direct derived Vector4 semantics.
    var plans=(Array)analyze.Invoke(null,[mods])!;
    Check(plans.Length==2,"Expected two eligible files, never framework/shadow/comments");
    var mainPlan=plans.Cast<object>().Single(x=>
       ((string)x.GetType().GetProperty("RelativeFile")!.GetValue(x)!) == "Example/init.lua");
    var operations=(Array)mainPlan.GetType().GetProperty("Operations")!.GetValue(mainPlan)!;
    Check(operations.Length==2,"Expected two independent authorized providers");
    Check(operations.Cast<object>().Sum(x=>(int)x.GetType().GetProperty("Occurrences")!.GetValue(x)!)==3,
        "Expected two GetPlayer + one Quests calls");
    var headers=new Func<string,IEnumerable<string>>(provider=>
    [
      "-- G-CET shared provider: "+provider,
      "local function __gcet"+(provider=="PLAYER"?"GetPlayer":provider=="QUESTS_SYSTEM"?"GetQuestsSystem":"GetTransactionSystem")+
      "() return nil end"
    ]);
    var mainText=File.ReadAllText(main);
    object?[] args=[mainText,headers,null,null];
    Check((bool)transform.Invoke(null,args)!,"Transform refused");
    string output=(string)args[2]!;
    Check(output.Contains("actor = __gcetGetPlayer()"),"Missing player getter rewrite");
    Check(output.Contains("actor:GetWorldPosition()"),"Derived vector read changed");
    Check(output.Contains("__gcetGetPlayer():GetWorldPosition()"),"Chained player getter was not reused");
    Check(output.Contains("-- Game.GetPlayer()"),"Comment removed");
    Check(output.Contains("\"Game.GetQuestsSystem()\""),"String modified");
    Check(output.Contains("[=[Game.GetPlayer()]=]"),"Long bracket modified");
    Check(output.Contains("\r\n"),"CRLF not preserved");
    object?[] again=[output,headers,null,null];
    Check(!(bool)transform.Invoke(null,again)!,"Transform not idempotent");
    // Exercise source-digest-locked generator attachment to an existing semantic
    // pass: no callback gate, no new hooks, original source remains unmodified.
    var rows=plans.Cast<object>().Select(x=>new {
        relativeFile=(string)x.GetType().GetProperty("RelativeFile")!.GetValue(x)!,
        sha256=(string)x.GetType().GetProperty("Sha256")!.GetValue(x)!
    }).ToArray();
    using var document=JsonDocument.Parse(JsonSerializer.Serialize(new {
        nativeReadReuse=new {contract="GCET_NATIVE_READ_REUSE_V1",files=rows}
    }));
    var staged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase)
    {
      ["Example/init.lua"]=Encoding.UTF8.GetBytes("-- semantic pass retained\n"+mainText)
    };
    var hashes=new Dictionary<string,string?>(StringComparer.OrdinalIgnoreCase);
    var applied=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var manifest=new List<object>();
    var skipped=new List<object>();
    object?[] stageArgs=[document.RootElement,mods,staged,hashes,applied,manifest,skipped,headers];
    var count=(int)stage.Invoke(null,stageArgs)!;
    Check(count==4,"Expected 3 direct reads + one helper getter replacement");
    var combined=Encoding.UTF8.GetString(staged["Example/init.lua"]);
    Check(combined.Contains("-- semantic pass retained"),"Existing semantic stage clobbered");
    Check(combined.Contains("__gcetGetPlayer()"),"Generated shared helper missing");
    Check(File.ReadAllText(main)==mainText,"Installed source was modified");
    Check(manifest.Count==3,"Expected per-provider transform manifest");
    // Drift must refuse without modifying staged file.
    File.AppendAllText(main,"-- changed\n");
    var before=staged["Example/init.lua"].ToArray();
    var newManifest=new List<object>();
    var drifts=new List<object>();
    object?[] driftArgs=[document.RootElement,mods,staged,hashes,applied,newManifest,drifts,headers];
    var afterDrift=(int)stage.Invoke(null,driftArgs)!;
    Check(afterDrift==0,"Drift accepted unexpectedly");
    Check(before.SequenceEqual(staged["Example/init.lua"]),"Drift mutated staged file");
    Check(drifts.Count>0,"Drift not diagnosed");
    Console.WriteLine("GCET NativeReadReuse source-first contracts PASS");
}
finally { Directory.Delete(root,true); }
