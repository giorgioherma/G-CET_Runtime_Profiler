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
    Save("HelperCollision/init.lua","local function __gcetGetPlayer() return nil end\nreturn Game.GetPlayer()\n");
    Save("Overflow/init.lua","local "+string.Join(",",Enumerable.Range(1,200).Select(i=>"v"+i))+"\nreturn Game.GetPlayer()\n");
    Save("PreviouslyApplied/init.lua","-- G-CET shared provider: PLAYER\nlocal function __gcetGetPlayer() return Game.GetPlayer() end\n");
    // Scanner must see exact code calls, ignore literals, disallow local Game
    // shadowing, and preserve direct derived Vector4 semantics.
    var plans=(Array)analyze.Invoke(null,[mods])!;
    Check(plans.Length==3,"Expected three candidates; never framework/shadow/comments/helper-collision");
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
    object?[] transformArgs=[mainText,headers,null,null];
    Check((bool)transform.Invoke(null,transformArgs)!,"Transform refused");
    string output=(string)transformArgs[2]!;
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
    Check(count==4,"Expected four safe reads; 200-local overflow must not be staged");
    Check(skipped.Any(x=>x.ToString()!.Contains("LUA_LOCAL_LIMIT")), "Lua local limit skip missing");
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
    // Player ownership evidence is built from callback registration metadata
    // and source analysis; it must not equate PlayerPuppet class context with
    // the receiver of unrelated NPC getter calls.
    var ownership=core.GetType("GCETRuntimeProfiler.Core.Services.PlayerOwnershipEvidenceSource",true)!;
    var classify=ownership.GetMethod("Context",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    Check((string)classify.Invoke(null,["Observe","PlayerPuppet::OnAction"])! ==
        "PLAYER_PUPPET_CLASS_CONTEXT","PlayerPuppet context tag missing");
    Check((string)classify.Invoke(null,["event","onUpdate"])! ==
        "GLOBAL_EVENT_CONTEXT","Global event must not be tagged as player");
    var analyzeOwnership=ownership.GetMethod("Analyze",BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    var playerScript="""
        -- Game.GetPlayer():GetWorldPosition()
        local ignored = "this:GetWorldPosition()"
        local direct = Game.GetPlayer():GetWorldPosition()
        local player = Game.GetPlayer()
        local actor = player
        local pos = actor:GetWorldPosition()
        local npc = findNearestNPC()
        local hostile = npc:GetWorldOrientation()
        local changing = Game.GetPlayer()
        changing = npc
        local dynamic = changing:IsInCombat()
        local observer = this:GetWorldPosition()
        """;
    var siteRows=(Array)analyzeOwnership.Invoke(null,[playerScript,null,null,null,null])!;
    Check(siteRows.Length==5,"Unexpected getter receiver callsite count");
    string Tag(object x)=>(string)x.GetType().GetProperty("ReceiverTag")!.GetValue(x)!;
    var playerTags=siteRows.Cast<object>().Select(Tag).ToArray();
    Check(playerTags.Count(x=>x=="CURRENT_PLAYER_SOURCE_PROVEN")==1,
        "Direct source player getter proof not classified");
    Check(playerTags.Count(x=>x=="PLAYER_ALIAS_SOURCE_EVIDENCE")==1,
        "Straight-line local alias not recognized");
    Check(playerTags.Count(x=>x=="UNKNOWN")==3,
        "NPC, rebound alias or PlayerPuppet 'this' must remain UNKNOWN");
    var shadowedRows=(Array)analyzeOwnership.Invoke(null,[
        "local Game = something\nlocal pos=Game.GetPlayer():GetWorldPosition()",null,null,null,null])!;
    Check(shadowedRows.Length==1&&Tag(shadowedRows.GetValue(0)!)=="UNKNOWN",
        "Shadowed Game facade was falsely proven");
    var boundaryRows=(Array)analyzeOwnership.Invoke(null,[
        "local p = Game.GetPlayer()\nif ready then return end\nlocal x=p:GetWorldPosition()",null,null,null,null])!;
    Check(boundaryRows.Length==1&&Tag(boundaryRows.GetValue(0)!)=="UNKNOWN",
        "Cross-scope local alias was falsely proven");
    Check(siteRows.Cast<object>().All(x=>
        !(bool)x.GetType().GetProperty("RuntimeObserved")!.GetValue(x)! &&
        !(bool)x.GetType().GetProperty("AuthorizesRewrite")!.GetValue(x)!),
        "Source-only ownership tag must never impersonate runtime evidence");
    // Class-registered callback first parameter is a real instance receiver;
    // it may be called 'self', 'actor', etc. Matching the class/target and
    // lexical body is mandatory. The tag does not prove current local player.
    var observedScript="""
        -- Observe("PlayerPuppet", "OnAction", function(fake)
        --    fake:GetWorldPosition()
        -- end)
        Observe("PlayerPuppet", "OnAction", function(actor, action)
            local p = actor
            local a = actor:GetWorldPosition()
            local b = p:GetWorldOrientation()
            if action then
                local c = actor:IsInCombat()
            end
            local npcPos = nearbyNpc:GetWorldPosition()
            local inner = function() return actor:GetWorldPosition() end
        end)
        Observe("NPCPuppet", "OnAction", function(npc, action)
            local d = npc:GetWorldPosition()
        end)
        """;
    var observedRows=(Array)analyzeOwnership.Invoke(null,
        [observedScript,"observe","PlayerPuppet::OnAction",null,null])!;
    var observedTags=observedRows.Cast<object>().Select(Tag).ToArray();
    Check(observedTags.Length==6,"Wrong full-file registered-callsite inventory");
    Check(observedTags.Count(x=>x=="PLAYER_PUPPET_OBSERVER_RECEIVER")==2,
        "PlayerPuppet first argument not bound to registration, including if body");
    Check(observedTags.Count(x=>x=="PLAYER_PUPPET_OBSERVER_ALIAS")==1,
        "Safe straight-line first-parameter alias not recognized");
    Check(observedTags.Count(x=>x=="UNKNOWN")==3,
        "NPC, nested function or different registration falsely bound to player");
    Check(observedRows.Cast<object>().Any(x=>
        (string)x.GetType().GetProperty("ReceiverClass")!.GetValue(x)! == "PlayerPuppet"),
        "Registered callback receiver class provenance missing");
    var overridden="""
        Override("PlayerPuppet", "OnAction", function(entity, action, wrapped)
            return entity:GetWorldPosition()
        end)
        """;
    var overrideRows=(Array)analyzeOwnership.Invoke(null,
        [overridden,"Override","PlayerPuppet::OnAction",null,null])!;
    Check(overrideRows.Length==1&&Tag(overrideRows.GetValue(0)!)==
        "PLAYER_PUPPET_OBSERVER_RECEIVER","Override first receiver not recognized");
    var wrapped="""
        Event.Observe("PlayerPuppet", "OnAction", function(self)
            return self:GetWorldPosition()
        end)
        """;
    var wrappedRows=(Array)analyzeOwnership.Invoke(null,
        [wrapped,"observe","PlayerPuppet::OnAction",null,null])!;
    Check(wrappedRows.Length==1&&Tag(wrappedRows.GetValue(0)!)=="UNKNOWN",
        "Wrapped Observe must not inherit direct CET first-arg contract");
    var rebound="""
        Observe("PlayerPuppet", "OnAction", function(self)
            if condition then self = npc end
            return self:GetWorldPosition()
        end)
        """;
    var reboundRows=(Array)analyzeOwnership.Invoke(null,
        [rebound,"observe","PlayerPuppet::OnAction",null,null])!;
    Check(reboundRows.Length==1&&Tag(reboundRows.GetValue(0)!)=="UNKNOWN",
        "Rebound self incorrectly classified as player");
    var globalEvent="""
        registerForEvent("onUpdate", function(self)
            local pos = self:GetWorldPosition()
        end)
        """;
    var globalRows=(Array)analyzeOwnership.Invoke(null,
        [globalEvent,"event","onUpdate",null,null])!;
    Check(globalRows.Length==1&&Tag(globalRows.GetValue(0)!)=="UNKNOWN",
        "Event callback first arg incorrectly assumed to be player");
    var otherClass="""
        Observe("ScriptedPuppet", "OnAction", function(self)
            return self:GetWorldPosition()
        end)
        """;
    var otherRows=(Array)analyzeOwnership.Invoke(null,
        [otherClass,"observe","ScriptedPuppet::OnAction",null,null])!;
    Check(otherRows.Length==1&&Tag(otherRows.GetValue(0)!)==
        "CLASS_BOUND_OBSERVER_RECEIVER","Other class must not be PlayerPuppet");
    // Full-file metadata windows restrict a callback to its measured span:
    // an unrelated PlayerPuppet registration cannot leak across callbacks.
    var scopedRows=(Array)analyzeOwnership.Invoke(null,
        [observedScript,"observe","PlayerPuppet::OnAction",10,14])!;
    Check(scopedRows.Cast<object>().All(x=>Tag(x)!="CLASS_BOUND_OBSERVER_RECEIVER"),
        "Source span leaked a different registration");
    Check(observedRows.Cast<object>().All(x=>
        !(bool)x.GetType().GetProperty("RuntimeObserved")!.GetValue(x)! &&
        !(bool)x.GetType().GetProperty("AuthorizesRewrite")!.GetValue(x)!),
        "Registered self evidence falsely enabled an optimization");

    Console.WriteLine("GCET PlayerOwnership source-only receiver evidence PASS");

    // All installed mod sources participate, including dormant callbacks
    // absent from profiler registration traces. Native XYZ does not supply
    // a compatible full Vector4/freshness contract, so no unsafe rewrite.
    Save("DormantPlayerObserver/init.lua", """
        Observe("PlayerPuppet", "OnAction", function(self, action)
            local pos = self:GetWorldPosition()
            local npc = findNpc()
            local npcPos = npc:GetWorldPosition()
        end)
        """);
    var catalogType=core.GetType(
        "GCETRuntimeProfiler.Core.Services.NativePlayerAdoptionCatalog",true)!;
    var scan=catalogType.GetMethod("Scan",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    var catalog=scan.Invoke(null,[mods])!;
    var files=(Array)catalog.GetType().GetProperty("Files")!.GetValue(catalog)!;
    var dormant=files.Cast<object>().Single(x=>(string)x.GetType()
        .GetProperty("RelativeFile")!.GetValue(x)! == "DormantPlayerObserver/init.lua");
    var dormantSites=(Array)dormant.GetType().GetProperty("Sites")!
        .GetValue(dormant)!;
    Check(dormantSites.Length==2,"Dormant observer inventory omitted getter");
    string InventoryTag(object x)=>(string)x.GetType().GetProperty("IdentityTag")!
        .GetValue(x)!;
    Check(dormantSites.Cast<object>().Count(x=>
        InventoryTag(x)=="PLAYER_PUPPET_OBSERVER_RECEIVER")==1,
        "Dormant PlayerPuppet self not recognized");
    Check(dormantSites.Cast<object>().Count(x=>
        InventoryTag(x)=="UNKNOWN")==1,
        "NPC getter was incorrectly promoted to player");
    Check((int)catalog.GetType().GetProperty("NativeRewriteAuthorizedSites")!
        .GetValue(catalog)! >= 1,
        "Direct Game.GetPlayer native live candidate was not authorized");
    var saveCatalog=catalogType.GetMethod("Write",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    var savedCatalog=Path.Combine(root,"native-player-discovery.json");
    saveCatalog.Invoke(null,[mods,savedCatalog]);
    using(var saved=JsonDocument.Parse(File.ReadAllText(savedCatalog)))
    {
        Check(saved.RootElement.GetProperty("Files").GetArrayLength()>0,
            "Installer-native inventory was not emitted");
        Check(saved.RootElement.GetProperty("Contract").GetString()==
            "GCET_NATIVE_PLAYER_ADOPTION_DISCOVERY_V1",
            "Native inventory contract mismatch");
    }
    Console.WriteLine("GCET NativePlayerAdoption full-source catalog PASS");

    // Native adoption must be mechanical, fail-open, CRLF/idempotent,
    // and never substitute other NPC receivers or comments/strings.
    var vectorStage=core.GetType(
        "GCETRuntimeProfiler.Core.Services.NativePlayerVectorStage",true)!;
    var tryVector=vectorStage.GetMethod("TryTransform",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    var vecSource="""
        -- Game.GetPlayer():GetWorldPosition() is only a comment
        local str="Game.GetPlayer():GetWorldPosition()"
        local a=Game.GetPlayer():GetWorldPosition()
        local npc=findNpc()
        local b=npc:GetWorldPosition()
        """.Replace("\n","\r\n");
    object?[] vecArgs=[vecSource,null,0];
    Check((bool)tryVector.Invoke(null,vecArgs)! &&
        (int)vecArgs[2]! == 1,"Direct GetPlayer source should be rewritten once");
    var transformed=(string)vecArgs[1]!;
    Check(transformed.Contains("GCETPlayerVector4Live") &&
        transformed.Contains("GCETPlayerVector4Valid") &&
        transformed.Contains("return Game.GetPlayer():GetWorldPosition()"),
        "Typed native live provider or original fallback missing");
    Check(transformed.Contains("npc:GetWorldPosition()") &&
        transformed.Contains("-- Game.GetPlayer():GetWorldPosition()") &&
        transformed.Contains("\"Game.GetPlayer():GetWorldPosition()\""),
        "NPC, quoted or commented getter modified");
    Check(transformed.Contains("\r\n"),"Native vector helper lost CRLF");
    object?[] vecAgain=[transformed,null,0];
    Check(!(bool)tryVector.Invoke(null,vecAgain)!,"Native vector transform not idempotent");
    object?[] shadowVec=[
        "local Game={}\nreturn Game.GetPlayer():GetWorldPosition()",null,0];
    Check(!(bool)tryVector.Invoke(null,shadowVec)!,"Shadowed Game should be rejected");
    Console.WriteLine("GCET NativePlayerVector guarded live source PASS");

    Console.WriteLine("GCET NativeReadReuse source-first contracts PASS");
}
finally { Directory.Delete(root,true); }
