using System.Reflection;
using System.Text;
using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool yes,string why)
{
    if(!yes)throw new InvalidOperationException(why);
}
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
    // Exercise version-independent source regeneration on a staged semantic pass.
    // pass: no callback gate, no new hooks, original source remains unmodified.
    var rows=plans.Cast<object>().Select(x=>new {
        relativeFile=(string)x.GetType().GetProperty("RelativeFile")!.GetValue(x)!
    }).ToArray();
    using var document=JsonDocument.Parse(JsonSerializer.Serialize(new {
        nativeReadReuse=new {contract="GCET_NATIVE_READ_REUSE_V1",files=rows}
    }));
    var staged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase)
    {
      ["Example/init.lua"]=Encoding.UTF8.GetBytes("-- semantic pass retained\n"+mainText)
    };
    var applied=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var manifest=new List<object>();
    var skipped=new List<object>();
    object?[] stageArgs=[document.RootElement,mods,staged,applied,manifest,skipped,headers];
    var count=(int)stage.Invoke(null,stageArgs)!;
    Check(count==4,"Expected four safe reads; 200-local overflow must not be staged");
    Check(skipped.Any(x=>x.ToString()!.Contains("LUA_LOCAL_LIMIT")), "Lua local limit skip missing");
    var combined=Encoding.UTF8.GetString(staged["Example/init.lua"]);
    Check(combined.Contains("-- semantic pass retained"),"Existing semantic stage clobbered");
    Check(combined.Contains("__gcetGetPlayer()"),"Generated shared helper missing");
    Check(File.ReadAllText(main)==mainText,"Installed source was modified");
    Check(manifest.Count==3,"Expected per-provider transform manifest");
    // A large Lua file with 199 top-level locals cannot accept two newly
    // generated shared providers, but CAN safely benefit from one. The
    // resolver must keep the higher-impact provider, rather than rejecting
    // all independent getter reuse or deleting author declarations.
    var tightRoot=Path.Combine(root,"tightmodset");
    var tightMods=Path.Combine(tightRoot,"Budget");
    Directory.CreateDirectory(tightMods);
    var authorLine="local "+string.Join(",",
        Enumerable.Range(1,199).Select(i=>"author"+i));
    var tightSource=authorLine+"\n"+
        "return Game.GetPlayer(), Game.GetPlayer(), Game.GetPlayer(), "+
        "Game.GetQuestsSystem()\n";
    File.WriteAllText(Path.Combine(tightMods,"init.lua"),
        tightSource,new UTF8Encoding(false));
    var withinBudget=M("TryTransformWithinBudget");
    object?[] budgetArgs=[tightSource,headers,null,null,0,null];
    Check((bool)withinBudget.Invoke(null,budgetArgs)!,
        "Near-limit source rejected every independently affordable getter");
    var partialOutput=(string)budgetArgs[2]!;
    var picked=(Array)budgetArgs[3]!;
    var unpicked=(string[])budgetArgs[5]!;
    Check((int)budgetArgs[4] == 201 && picked.Length==1 &&
          unpicked.SequenceEqual(["GetQuestsSystem"]) &&
          partialOutput.Contains("return __gcetGetPlayer(), __gcetGetPlayer(), __gcetGetPlayer(), Game.GetQuestsSystem()") &&
          partialOutput.Contains(authorLine, StringComparison.Ordinal),
        "Lua-budget source selector must preserve all 199 author locals "+
        "while applying the highest-value proven getter");
    var tightStages=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    var tightApplied=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var tightManifest=new List<object>();
    var tightSkipped=new List<object>();
    object?[] tightArgs=[document.RootElement,tightRoot,tightStages,
        tightApplied,tightManifest,tightSkipped,headers];
    Check((int)stage.Invoke(null,tightArgs)! == 3 &&
          tightStages.ContainsKey("Budget/init.lua") &&
          tightManifest.Count==1 &&
          tightSkipped.Any(x=>x.ToString()!.Contains("LUA_LOCAL_LIMIT_PARTIAL")),
        "Native stage did not preserve the highest-benefit safe partial rewrite");
    var tightAgain=new List<object>();
    object?[] tightRepeat=[document.RootElement,tightRoot,tightStages,
        tightApplied,tightAgain,new List<object>(),headers];
    Check((int)stage.Invoke(null,tightRepeat)! == 0 && tightAgain.Count==0,
        "Lua-budget partial provider was duplicated on a repeat generation");
    Check(File.ReadAllText(Path.Combine(tightMods,"init.lua"))==tightSource,
        "Local-budget fallback mutated installed Lua source");
    Console.WriteLine("GCET 199-local partial reuse: three reads saved, "+
        "all author locals retained, unsafe fourth deferred PASS");

    // Near-limit source can move ONLY exact G-CET generated helpers to a
    // separate, file-owned chunk. Do not delete author code or add another
    // callback. The original Game fallback is inside the new Lua worker.
    var workerType=core.GetType("GCETRuntimeProfiler.Core.Services.GenericReadCoordinatorSource",true)!;
    var workerStage=workerType.GetMethod("TryStage",
        BindingFlags.NonPublic|BindingFlags.Static)!;
    var workerRoot=Path.Combine(root,"worker-budget");
    var workerMod=Path.Combine(workerRoot,"Ready");
    Directory.CreateDirectory(workerMod);
    Func<string,IEnumerable<string>> workerHeaders=provider=>
    {
        var getter=provider switch {
            "PLAYER"=>"GetPlayer",
            "QUESTS_SYSTEM"=>"GetQuestsSystem",
            "STATS_SYSTEM"=>"GetStatsSystem",
            _=>throw new InvalidOperationException("unexpected fixture provider: "+provider)
        };
        return new [] {
            "-- G-CET shared provider: "+provider,
            "local __gcetProvider_"+getter+" = nil",
            "local function __gcet"+getter+"()",
            "    if __gcetProvider_"+getter+" == nil then",
            "        __gcetProvider_"+getter+" = Game."+getter+"",
            "    end",
            "    local answer = __gcetProvider_"+getter+"()",
            "    if answer ~= nil then return answer end",
            "    return Game."+getter+"()",
            "end",
            ""
        };
    };
    var workerSource=string.Join("\n",workerHeaders("PLAYER"))+"\n"+
        "local "+string.Join(",",Enumerable.Range(1,198).Select(i=>"author"+i))+
        "\nreturn __gcetGetPlayer(), Game.GetQuestsSystem()\n";
    File.WriteAllText(Path.Combine(workerMod,"init.lua"),workerSource,
        new UTF8Encoding(false));
    var workerStaged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    var workerCounts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var workerManifest=new List<object>();
    var workerSkips=new List<object>();
    object?[] workerArgs=[workerRoot,workerStaged,workerCounts,workerManifest,
                          workerSkips,workerHeaders];
    Check((int)workerStage.Invoke(null,workerArgs)==1,
        "Near-limit G-CET generated helpers were not moved to a separate Lua chunk");
    var workerMain=Encoding.UTF8.GetString(workerStaged["Ready/init.lua"]);
    var workerFile=workerStaged.Keys.Single(x=>x.StartsWith(
        "Ready/GCET_Semantic/gcet_native_reads_",StringComparison.Ordinal));
    var workerBody=Encoding.UTF8.GetString(workerStaged[workerFile]);
    Check(workerMain.Contains("local __gcetNativeReads = require(") &&
          workerMain.Contains("__gcetNativeReads.GetPlayer()") &&
          workerMain.Contains("__gcetNativeReads.GetQuestsSystem()") &&
          !workerMain.Contains("local function __gcetGetPlayer") &&
          workerMain.Contains("local author1,author2,author3") &&
          workerBody.Contains("return Game.GetPlayer()") &&
          workerBody.Contains("return Game.GetQuestsSystem()") &&
          workerManifest.Count==1 && workerSkips.Count==0,
        "Worker must own only generated helpers, preserve author locals and exact fallbacks");
    var budgetType=core.GetType("GCETRuntimeProfiler.Core.Services.LuaLocalBudgetAnalyzer",true)!;
    var budgetMethod=budgetType.GetMethods(BindingFlags.NonPublic|BindingFlags.Static)
        .Single(m=>m.Name=="Analyze" && m.GetParameters().Length==1 &&
                   m.GetParameters()[0].ParameterType==typeof(string));
    var beforeBudget=budgetMethod.Invoke(null,[workerSource])!;
    var afterBudget=budgetMethod.Invoke(null,[workerMain])!;
    var peakProperty=beforeBudget.GetType().GetProperty("MaxActiveLocals")!;
    Check((int)peakProperty.GetValue(beforeBudget)==200 &&
          (int)peakProperty.GetValue(afterBudget)==199,
        "Separate Lua import must reduce 200-local source to 199 without touching author code");
    var againWorkers=new List<object>();
    object?[] againArgs=[workerRoot,workerStaged,workerCounts,againWorkers,
                         new List<object>(),workerHeaders];
    Check((int)workerStage.Invoke(null,againArgs)==0&&againWorkers.Count==0,
        "Repeated generation duplicated read coordinator");
    workerStaged["Ready/init.lua"]=Encoding.UTF8.GetBytes(workerMain+
        "\nlocal extra = Game.GetStatsSystem()\n");
    // Existing worker and source can adopt newly introduced source getters
    // without a second import or a second copy of the old fallback helpers.
    var updatedManifest=new List<object>();
    object?[] newerWorkerArgs=[workerRoot,workerStaged,workerCounts,updatedManifest,
                               new List<object>(),workerHeaders];
    Check((int)workerStage.Invoke(null,newerWorkerArgs)==1,
        "Existing G-CET read worker did not adopt a new current-source getter");
    var upgraded=Encoding.UTF8.GetString(workerStaged["Ready/init.lua"]);
    var upgradedWorker=Encoding.UTF8.GetString(workerStaged[workerFile]);
    Check(upgraded.Contains("__gcetNativeReads.GetStatsSystem()") &&
          upgraded.Split("local __gcetNativeReads = require(",StringSplitOptions.None).Length==2 &&
          upgradedWorker.Contains("return Game.GetStatsSystem()") &&
          upgradedWorker.Split("-- G-CET shared provider: PLAYER").Length==2,
        "Worker upgrade duplicated an import/provider or lost a fallback");
    Check(File.ReadAllText(Path.Combine(workerMod,"init.lua"))==workerSource,
        "Worker upgrade changed installed mod source");
    Console.WriteLine("GCET near-limit source -> separate Lua worker; 200 to 199 locals, "+
        "current-source upgrade and idempotence: PASS");

    // Four previously refused live owners used the real G-CET 0-Engine
    // provider body, but with CRLF or cosmetic indentation differences.
    // Exercise that complete recipe (not a simplified synthetic helper).
    var recipeType=core.GetType(
        "GCETRuntimeProfiler.Core.Services.PassGeneratorService+SharedProviderRecipe",true)!;
    var mkRecipe=recipeType.GetMethod("Create",
        BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static)!;
    var mkHeader=typeof(PassGeneratorService).GetMethod(
        "BuildSharedProviderHeader",BindingFlags.NonPublic|BindingFlags.Static)!;
    Func<string,IEnumerable<string>> actualRecipes=p=>
    {
        var getter=p switch
        {
            "MAPPIN_SYSTEM"=>"GetMappinSystem",
            "BLACKBOARD_SYSTEM"=>"GetBlackboardSystem",
            "DYNAMIC_ENTITY_SYSTEM"=>"GetDynamicEntitySystem",
            "AI_NAVIGATION_SYSTEM"=>"GetAINavigationSystem",
            "PLAYER"=>"GetPlayer",
            "UI_SYSTEM"=>"GetUISystem",
            "STATS_SYSTEM"=>"GetStatsSystem",
            "ALL_BLACKBOARD_DEFS"=>"GetAllBlackboardDefs",
            "CAMERA_SYSTEM"=>"GetCameraSystem",
            "QUESTS_SYSTEM"=>"GetQuestsSystem",
            "WORKSPOT_SYSTEM"=>"GetWorkspotSystem",
            "STATUS_EFFECT_SYSTEM"=>"GetStatusEffectSystem",
            "SCRIPTABLE_SYSTEMS_CONTAINER"=>"GetScriptableSystemsContainer",
            "TELEPORTATION_FACILITY"=>"GetTeleportationFacility",
            "TIME_SYSTEM"=>"GetTimeSystem",
            "TRANSACTION_SYSTEM"=>"GetTransactionSystem",
            _=>throw new InvalidOperationException("Unreviewed test provider "+p)
        };
        var recipe=mkRecipe.Invoke(null,[p,getter,"__gcet"+getter])!;
        return (IEnumerable<string>)mkHeader.Invoke(null,[recipe])!;
    };
    // Independently verify every catalog recipe. An earlier C# literal
    // omitted '$' before a placeholder; tests built from that same recipe
    // falsely passed because expected and actual Lua were both malformed.
    var authType=core.GetType(
        "GCETRuntimeProfiler.Core.Services.SharedProviderCatalog",true)!;
    var authEntries=(Array)authType.GetField("Authorized",
        BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
    var reviewedHeaders=new Dictionary<string,(string Getter,string Header)>(
        StringComparer.Ordinal);
    foreach(var entry in authEntries)
    {
        var model=entry!;
        var provider=(string)model.GetType().GetProperty("Provider")!.GetValue(model)!;
        var getter=(string)model.GetType().GetProperty("Getter")!.GetValue(model)!;
        var recipe=mkRecipe.Invoke(null,[provider,getter,"__gcet"+getter])!;
        var header=string.Join("\n",(IEnumerable<string>)mkHeader.Invoke(null,[recipe])!);
        Check(!header.Contains("{recipe.",StringComparison.Ordinal) &&
              header.Contains("return Game."+getter+"()",StringComparison.Ordinal),
            "Unexpanded C# template or lost fallback: "+provider);
        if(provider!="PLAYER")
        {
            var independentlyExpected=
                "local __gcetApi = type(__gcetEngine.GCET) == \"table\" and type(__gcetEngine.GCET."+
                getter+") == \"function\" and __gcetEngine.GCET or __gcetEngine";
            Check(header.Contains(independentlyExpected,StringComparison.Ordinal),
                "Generated 0-Engine API selection must name the actual getter: "+provider);
        }
        reviewedHeaders.Add(provider,(getter,header));
    }
    Check(reviewedHeaders.Count==26,
        "Unexpected 0-Engine shared-provider catalog size; re-review full recipes.");
    Console.WriteLine("26 shared provider recipes have fully resolved getter names "+
        "and independently verified original fallbacks: PASS");

    // Full legacy-placeholder inventory from the supplied 08:14 live stack:
    // 45 invalid generated expressions in 16 Lua source files owned by two
    // installed mods. Recreate only G-CET-generated provider blocks.
    var brokenByFile=new (string Path,string[] Families)[]
    {
        ("DedraThirdPersonCombatAddon/init.lua",
            ["BLACKBOARD_SYSTEM","SCRIPTABLE_SYSTEMS_CONTAINER","TRANSACTION_SYSTEM"]),
        ("marmurbank/external/GameUI.lua",
            ["ALL_BLACKBOARD_DEFS","BLACKBOARD_SYSTEM","QUESTS_SYSTEM","TIME_SYSTEM"]),
        ("marmurbank/external/InteractionUI.lua",
            ["AUDIO_SYSTEM","STATUS_EFFECT_SYSTEM","BLACKBOARD_SYSTEM"]),
        ("marmurbank/external/Lang.lua",["SCRIPTABLE_SYSTEMS_CONTAINER"]),
        ("marmurbank/external/Util.lua",
            ["ALL_BLACKBOARD_DEFS","BLACKBOARD_SYSTEM","MAPPIN_SYSTEM",
             "QUESTS_SYSTEM","TIME_SYSTEM","TRANSACTION_SYSTEM","UI_SYSTEM"]),
        ("marmurbank/init.lua",
            ["ALL_BLACKBOARD_DEFS","BLACKBOARD_SYSTEM","JOURNAL_MANAGER","QUESTS_SYSTEM"]),
        ("marmurbank/module/Bank.lua",
            ["AUDIO_SYSTEM","DYNAMIC_ENTITY_SYSTEM","QUESTS_SYSTEM",
             "SCRIPTABLE_SYSTEMS_CONTAINER","STATUS_EFFECT_SYSTEM",
             "TIME_SYSTEM","TRANSACTION_SYSTEM"]),
        ("marmurbank/module/Calendar.lua",
            ["QUESTS_SYSTEM","SCRIPTABLE_SYSTEMS_CONTAINER","TIME_SYSTEM"]),
        ("marmurbank/module/PartsFinancing.lua",["SCRIPTABLE_SYSTEMS_CONTAINER"]),
        ("marmurbank/module/PhoneAlerts.lua",
            ["ALL_BLACKBOARD_DEFS","BLACKBOARD_SYSTEM","SYSTEM_REQUESTS_HANDLER"]),
        ("marmurbank/module/Spawn.lua",["DYNAMIC_ENTITY_SYSTEM","MAPPIN_SYSTEM"]),
        ("marmurbank/module/SpawnHub.lua",["STATUS_EFFECT_SYSTEM"]),
        ("marmurbank/module/SpawnListener.lua",["MAPPIN_SYSTEM"]),
        ("marmurbank/module/SpawnUtil.lua",["DYNAMIC_ENTITY_SYSTEM","WORKSPOT_SYSTEM"]),
        ("marmurbank/module/TheftProtection.lua",["TIME_SYSTEM"]),
        ("marmurbank/ui/ATMKeypad.lua",["AUDIO_SYSTEM","QUESTS_SYSTEM"])
    };
    var legacyRoot=Path.Combine(root,"legacy-interpolation-repair");
    foreach(var row in brokenByFile)
    {
        var file=Path.Combine(legacyRoot,row.Path.Replace('/',Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var blocks=row.Families.Select(f=>{
            var (getter,header)=reviewedHeaders[f];
            return header.TrimEnd('\r','\n').Replace(
                "__gcetEngine.GCET."+getter,
                "__gcetEngine.GCET.{recipe.EngineGetter}",StringComparison.Ordinal);
        });
        var source=string.Join("\n\n",blocks)+"\n\nreturn true\n";
        File.WriteAllText(file,source,new UTF8Encoding(false));
    }
    var repairType=core.GetType(
        "GCETRuntimeProfiler.Core.Services.SharedProviderTemplateRepairSource",true)!;
    var repairStage=repairType.GetMethod("TryStage",
        BindingFlags.NonPublic|BindingFlags.Static)!;
    Func<string,IEnumerable<string>> fullHeaders=f=>reviewedHeaders[f].Header.Split('\n');
    var repairedStaged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    var repairCounts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var repairManifest=new List<object>();
    var repairSkipped=new List<object>();
    object?[] repairArgs=[legacyRoot,repairedStaged,repairCounts,
        repairManifest,repairSkipped,fullHeaders];
    Check((int)repairStage.Invoke(null,repairArgs)==16 &&
          repairSkipped.Count==0 && repairManifest.Count==16 &&
          repairCounts.Values.Sum()==45,
        "Legacy placeholders not recovered for all 16 files/45 expressions: "+
        string.Join("; ",repairSkipped.Select(x=>
            System.Text.Json.JsonSerializer.Serialize(x))));
    foreach(var row in brokenByFile)
    {
        var result=Encoding.UTF8.GetString(repairedStaged[row.Path]);
        Check(!result.Contains("{recipe.EngineGetter}",StringComparison.Ordinal) &&
              result.Contains("return Game.",StringComparison.Ordinal) &&
              File.ReadAllText(Path.Combine(legacyRoot,row.Path.Replace('/',
                  Path.DirectorySeparatorChar))).Contains("{recipe.EngineGetter}"),
            "Legacy worker repair was incomplete or mutated installed Lua: "+row.Path);
        foreach(var family in row.Families)
        {
            var (getter,_) = reviewedHeaders[family];
            Check(result.Contains("GCET."+getter,StringComparison.Ordinal),
                "Correct getter missing in source-owned repair "+row.Path+"/"+family);
        }
    }
    var repeatLedger=new List<object>();
    object?[] repeatRepairArgs=[legacyRoot,repairedStaged,repairCounts,
        repeatLedger,new List<object>(),fullHeaders];
    Check((int)repairStage.Invoke(null,repeatRepairArgs)==0 &&
          repeatLedger.Count==0,
        "Repeat generation rewrote already-corrected legacy provider blocks");
    // A text replacement is NOT permission to accept other source drift.
    var invalidSource=File.ReadAllText(Path.Combine(legacyRoot,
        "marmurbank/external/GameUI.lua")).Replace(
        "return Game.GetQuestsSystem()",
        "return Game.GetChangedQuestsSystem()",StringComparison.Ordinal);
    var tryRepair=repairType.GetMethod("TryRepair",
        BindingFlags.NonPublic|BindingFlags.Static)!;
    object?[] invalidArgs=[invalidSource,fullHeaders,null,0,null,null];
    Check(!(bool)tryRepair.Invoke(null,invalidArgs)! &&
          (string)invalidArgs[2]! == invalidSource,
        "Semantic drift passed unreviewed legacy source repair");
    Console.WriteLine("Legacy shared provider migration: 45 malformed calls "+
        "in 16 mod files repaired, current sources retained, real drift refused: PASS");

    foreach(var (provider,getter) in new[]{
        ("MAPPIN_SYSTEM","GetMappinSystem"),
        ("BLACKBOARD_SYSTEM","GetBlackboardSystem"),
        ("DYNAMIC_ENTITY_SYSTEM","GetDynamicEntitySystem"),
        ("AI_NAVIGATION_SYSTEM","GetAINavigationSystem")})
    {
        var testRoot=Path.Combine(root,"reviewed-provider-"+provider);
        var owner=Path.Combine(testRoot,"Reviewed");
        Directory.CreateDirectory(owner);
        var helper=string.Join("\n",actualRecipes(provider))
            .Replace("    if ","\tif ",StringComparison.Ordinal);
        var code=helper+"\n"+"local "+
            string.Join(",",Enumerable.Range(1,198).Select(n=>"v"+n))+
            "\nreturn __gcet"+getter+"(), Game.GetPlayer()\n";
        code=code.Replace("\n","\r\n",StringComparison.Ordinal);
        var target=Path.Combine(owner,"init.lua");
        File.WriteAllText(target,code,new UTF8Encoding(false));
        var changed=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var diagnostics=new List<object>();
        object?[] arguments=[testRoot,changed,new Dictionary<string,int>(),
            new List<object>(),diagnostics,actualRecipes];
        Check((int)workerStage.Invoke(null,arguments)==1&&diagnostics.Count==0,
            "Production helper with CRLF/indentation was rejected: "+provider);
        var bridge=Encoding.UTF8.GetString(changed["Reviewed/init.lua"]);
        var workerPath=changed.Keys.Single(x=>x.Contains("GCET_Semantic/gcet_native_reads_"));
        var emitted=Encoding.UTF8.GetString(changed[workerPath]);
        Check(bridge.Contains("\r\n",StringComparison.Ordinal) &&
              bridge.Contains("__gcetNativeReads."+getter+"()") &&
              !bridge.Contains("local function __gcet"+getter+"()") &&
              emitted.Contains("return Game."+getter+"()") &&
              emitted.Contains("pcall(GetMod, \"0-Engine\")") &&
              File.ReadAllText(target)==code,
            "Relocation did not preserve exact 0-Engine/Game fallback "+provider);
        var drift=code.Replace("return Game."+getter+"()",
            "return Game.Nonmatching"+getter+"()",StringComparison.Ordinal);
        File.WriteAllText(target,drift,new UTF8Encoding(false));
        var negative=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
        var reasons=new List<object>();
        object?[] rejectArgs=[testRoot,negative,new Dictionary<string,int>(),
            new List<object>(),reasons,actualRecipes];
        Check((int)workerStage.Invoke(null,rejectArgs)==0 &&
              negative.Count==0 && reasons.Count==1,
            "Non-layout source change must refuse the "+provider+" worker");
    }
    Console.WriteLine("Production 0-Engine provider bodies: CRLF and indentation admitted, "+
        "unknown getter bodies refused: PASS");

    // Match the full generated helper inventories in the four owners that
    // reported false semantic source drift in the 07:21 pass. The 28 family
    // markers/ordering come from the supplied modsLIVER archive; only G-CET
    // generated helpers are recreated, no author mod source is committed.
    var currentStackSpecs=new[] {
        (Owner:"CourierJobs", File:"init.lua",
         Families:new[]{"MAPPIN_SYSTEM","UI_SYSTEM"}, AuthorLocals:196, CRLF:true),
        (Owner:"DedraJNCJoytoy", File:"init.lua",
         Families:new[]{"BLACKBOARD_SYSTEM","MAPPIN_SYSTEM","PLAYER",
            "SCRIPTABLE_SYSTEMS_CONTAINER","STATUS_EFFECT_SYSTEM",
            "TELEPORTATION_FACILITY","TIME_SYSTEM","TRANSACTION_SYSTEM",
            "UI_SYSTEM"}, AuthorLocals:182, CRLF:false),
        (Owner:"Dedrapanamgoondate", File:"init.lua",
         Families:new[]{"DYNAMIC_ENTITY_SYSTEM","MAPPIN_SYSTEM","PLAYER",
            "QUESTS_SYSTEM","SCRIPTABLE_SYSTEMS_CONTAINER",
            "STATUS_EFFECT_SYSTEM","TELEPORTATION_FACILITY","TIME_SYSTEM"},
         AuthorLocals:184, CRLF:false),
        (Owner:"hotscenes", File:"hotscenes_nc_delights.lua",
         Families:new[]{"AI_NAVIGATION_SYSTEM","ALL_BLACKBOARD_DEFS",
            "BLACKBOARD_SYSTEM","CAMERA_SYSTEM","DYNAMIC_ENTITY_SYSTEM",
            "MAPPIN_SYSTEM","QUESTS_SYSTEM",
            "SCRIPTABLE_SYSTEMS_CONTAINER","WORKSPOT_SYSTEM"},
         AuthorLocals:181, CRLF:true)
    };
    var liveCasesRoot=Path.Combine(root,"live-provider-inventory");
    foreach(var x in currentStackSpecs)
    {
        var folder=Path.Combine(liveCasesRoot,x.Owner);
        Directory.CreateDirectory(folder);
        var helpers=string.Join("\n\n",x.Families.Select(
            family=>string.Join("\n",actualRecipes(family)).TrimEnd('\r','\n')));
        var source=helpers+"\n\nlocal "+
            string.Join(",",Enumerable.Range(1,x.AuthorLocals).Select(i=>"v"+i))+
            "\nreturn Game.GetStatsSystem()\n";
        if(x.CRLF)source=source.Replace("\n","\r\n",StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(folder,x.File),source,new UTF8Encoding(false));
    }
    var groupedStaged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    var groupedCounts=new Dictionary<string,int>();
    var groupedManifest=new List<object>();
    var groupedSkips=new List<object>();
    object?[] groupedArgs=[liveCasesRoot,groupedStaged,groupedCounts,
        groupedManifest,groupedSkips,actualRecipes];
    Check((int)workerStage.Invoke(null,groupedArgs)==4 &&
          groupedSkips.Count==0 && groupedManifest.Count==4,
        "Full source helper inventories were rejected: "+
            string.Join("; ",groupedSkips.Select(item=>System.Text.Json.JsonSerializer.Serialize(item))));
    foreach(var x in currentStackSpecs)
    {
        var file=x.Owner+"/"+x.File;
        var groupedSource=Encoding.UTF8.GetString(groupedStaged[file]);
        var generatedFiles=groupedStaged.Keys
            .Where(k=>k.StartsWith(x.Owner+"/GCET_Semantic/gcet_native_reads_",
                StringComparison.OrdinalIgnoreCase)).ToArray();
        Check(generatedFiles.Length==1,
            "Missing or duplicated extracted Lua worker for "+x.Owner);
        var workerText=Encoding.UTF8.GetString(groupedStaged[generatedFiles[0]]);
        foreach(var family in x.Families)
        {
            var reviewedMarker="-- G-CET shared provider: "+family;
            Check(!groupedSource.Contains(reviewedMarker,StringComparison.Ordinal) &&
                  workerText.Contains(reviewedMarker,StringComparison.Ordinal),
                "Provider not moved exactly once to owner worker: "+x.Owner+"/"+family);
        }
        Check(groupedSource.Contains("__gcetNativeReads.GetStatsSystem()") &&
              workerText.Contains("return Game.GetStatsSystem()") &&
              groupedSource.Contains("local v1,v2,v3") &&
              File.ReadAllText(Path.Combine(liveCasesRoot,x.Owner,x.File))
                  .Contains("-- G-CET shared provider: "+x.Families[0],
                      StringComparison.Ordinal),
            "Owner source, fallback or author declarations changed: "+x.Owner);
    }
    Console.WriteLine("07:21 live provider inventories: 4 owners, 28 generated helpers "+
        "relocated, 4 workers, exact author source retained: PASS");


    // Drift must refuse without modifying staged file.
    File.AppendAllText(main,"-- changed\n");
    var before=staged["Example/init.lua"].ToArray();
    var newManifest=new List<object>();
    var drifts=new List<object>();
    object?[] driftArgs=[document.RootElement,mods,staged,applied,newManifest,drifts,headers];
    var afterDrift=(int)stage.Invoke(null,driftArgs)!;
    Check(afterDrift==0,"Drift accepted unexpectedly");
    Check(before.SequenceEqual(staged["Example/init.lua"]),"Drift mutated staged file");
    Check(!drifts.Any(x=>x.ToString()!.Contains("LIVE_SOURCE_CHANGED_DURING_GENERATION")),
        "Unrelated live-source change was incorrectly used as a digest gate");
    // Source evidence is refreshed from the actual installed tree. An update
    // arriving after the report was generated is not a compatibility failure.
    Save("AddedAfterCapture/init.lua","local late = Game.GetQuestsSystem()\n");
    Save("StagedOnly/init.lua","return 'author code'\n");
    var newStages=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["StagedOnly/init.lua"]=Encoding.UTF8.GetBytes(
            "-- prior semantic stage\nlocal introduced=Game.GetQuestsSystem()\n")
    };
    var newApplied=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var newerManifest=new List<object>();
    var newerSkipped=new List<object>();
    object?[] freshArgs=[document.RootElement,mods,newStages,
                         newApplied,newerManifest,newerSkipped,headers];
    var accepted=(int)stage.Invoke(null,freshArgs)!;
    Check(accepted>0 && newStages.ContainsKey("AddedAfterCapture/init.lua"),
        "Updated mod was wrongly gated by stale report file inventory/SHA");
    Check(Encoding.UTF8.GetString(newStages["AddedAfterCapture/init.lua"])
              .Contains("__gcetGetQuestsSystem()"),
        "Updated mod direct getter was not source-adopted");
    Check(Encoding.UTF8.GetString(newStages["StagedOnly/init.lua"])
            .Contains("local introduced=__gcetGetQuestsSystem()") &&
          Encoding.UTF8.GetString(newStages["StagedOnly/init.lua"])
            .Contains("-- prior semantic stage"),
        "Semantically staged new getter was not adopted after source scan");

    // Existing helpers stay intact. A newly added getter call gets adopted
    // without creating a duplicate provider or rewriting its own fallback.
    var partialProvider="""
        -- G-CET shared provider: PLAYER
        local function __gcetGetPlayer()
            return Game.GetPlayer()
        end
        local added = Game.GetPlayer()
        """;
    object?[] providerArgs=[partialProvider,headers,null,null];
    Check((bool)transform.Invoke(null,providerArgs)!,
        "New getter beside previously installed shared provider not adopted");
    var providerResult=(string)providerArgs[2]!;
    Check(providerResult.Contains("local added = __gcetGetPlayer()") &&
          providerResult.Contains("return Game.GetPlayer()") &&
          providerResult.Split("-- G-CET shared provider: PLAYER").Length==2,
        "Existing provider was duplicated or original fallback rewritten");
    object?[] providerAgain=[providerResult,headers,null,null];
    Check(!(bool)transform.Invoke(null,providerAgain)!,
        "Already completed provider callsites must skip on a second pass");

    // Native typed player Vector4 provider follows the same stage-local rule.
    var vectorType=core.GetType("GCETRuntimeProfiler.Core.Services.NativePlayerVectorStage",true)!;
    var vector=vectorType.GetMethod("TryTransform",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    object?[] vectorInit=["local pos = Game.GetPlayer():GetWorldPosition()\n",null,0];
    Check((bool)vector.Invoke(null,vectorInit)! && (int)vectorInit[2]==1,
        "Initial native player Vector4 source transform failed");
    var vectorFirst=(string)vectorInit[1]!;
    object?[] vectorUpdate=[vectorFirst+"\nlocal newPos = Game.GetPlayer():GetWorldPosition()\n",null,0];
    Check((bool)vector.Invoke(null,vectorUpdate)! && (int)vectorUpdate[2]==1,
        "A later source version did not adopt its new player position callsite");
    var vectorResult=(string)vectorUpdate[1]!;
    Check(vectorResult.Contains("local newPos = __gcetNativePlayerWorldPosition()") &&
          vectorResult.Contains("return Game.GetPlayer():GetWorldPosition()") &&
          vectorResult.Split("-- G-CET native player Vector4 on-demand live v1").Length==2,
        "Existing native player helper was duplicated or its live fallback lost");
    object?[] vectorAgain=[vectorResult,null,0];
    Check(!(bool)vector.Invoke(null,vectorAgain)!,
        "Native player stage is not idempotent after incremental source update");

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
    // absent from profiler registration traces. Only direct Game.GetPlayer
    // position calls qualify for guarded strict-live native substitution.
    Save("0-Engine/NativeProbe.lua",
        "local current=Game.GetPlayer():GetWorldPosition()\n");
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
    Check(files.Cast<object>().Any(x=>
        (string)x.GetType().GetProperty("RelativeFile")!.GetValue(x)! ==
            "0-Engine/NativeProbe.lua"),
        "Whole-stack inventory silently omitted the managed runtime");
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
    // Source discovery includes RTTI-grounded future candidates, but they
    // are NOT automatically rewritten just because a return type is known.
    var rttiType=catalogType.GetMethod("RttiReturnType",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    string TypeOf(string method)=>(string)rttiType.Invoke(null,[method])!;
    Check(TypeOf("GetWorldOrientation")=="Quaternion" &&
          TypeOf("GetVelocity")=="Vector4" &&
          TypeOf("GetMountedVehicle")=="handle:vehicleBaseObject" &&
          TypeOf("IsInCombat")=="Bool",
          "RTTI signature catalog mismatch");
    var future=(Array)analyzeOwnership.Invoke(null,
        ["local a=Game.GetPlayer():GetVelocity()\n"+
         "local b=Game.GetPlayer():GetWorldOrientation()\n"+
         "local c=Game.GetPlayer():GetMountedVehicle()",null,null,null,null])!;
    Check(future.Length==3&&future.Cast<object>().All(x=>
        Tag(x)=="CURRENT_PLAYER_SOURCE_PROVEN"),
        "RTTI getter source inventory lost direct player proof");

    // Exercise the full generator staging handoff, not just local rewrites.
    // No capture activity is needed; the installer source scan is authoritative.
    var vecInstalled=Save("NativeVectorFixture/init.lua", """
        local pos=Game.GetPlayer():GetWorldPosition()
        local npc=findNpc()
        local npcPos=npc:GetWorldPosition()
        """);
    var vecBefore=File.ReadAllText(vecInstalled);
    var stageVector=vectorStage.GetMethod("TryStage",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public)!;
    var vecStaged=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
    var vecApplied=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
    var vecManifest=new List<object>();
    var vecSkipped=new List<object>();
    var stagedCount=(int)stageVector.Invoke(null,
        [mods,vecStaged,vecApplied,vecManifest,vecSkipped])!;
    Check(stagedCount>=1&&vecStaged.ContainsKey("NativeVectorFixture/init.lua"),
        "Native live candidate was not auto-staged from the source catalog");
    var vecText=Encoding.UTF8.GetString(vecStaged["NativeVectorFixture/init.lua"]);
    Check(vecText.Contains("__gcetNativePlayerWorldPosition()") &&
          vecText.Contains("npc:GetWorldPosition()") &&
          vecText.Contains("GCETPlayerVector4Live"),
          "Auto-staged player native pass lost required guards");
    Check(File.ReadAllText(vecInstalled)==vecBefore,
        "Native auto-adoption modified installed author source");
    Check(!vecStaged.ContainsKey("0-Engine/NativeProbe.lua"),
        "Native author-mod auto-adopter must not rewrite managed 0-Engine");
    var afterCount=(int)stageVector.Invoke(null,
        [mods,vecStaged,vecApplied,vecManifest,vecSkipped])!;
    Check(afterCount==0,"Native stage not idempotent");
    Check(vecManifest.Any(m=>m.ToString()!.Contains("NATIVE_PLAYER_VECTOR_LIVE")),
        "Native live stage manifest missing");

    Console.WriteLine("GCET NativePlayerVector guarded live source PASS");

    // A reviewed semantic may emit an owned Lua coordinator in the same
    // overlay. It is a distinct Lua chunk with its own 200-local budget.
    // Never rewrite an unrelated existing module or allow path traversal.
    var coordinatorOwner="CoordinatorFixture";
    var coordinatorRoot=Path.Combine(mods,coordinatorOwner);
    Directory.CreateDirectory(coordinatorRoot);
    File.WriteAllText(Path.Combine(coordinatorRoot,"init.lua"),
        "local "+string.Join(",",Enumerable.Range(1,199).Select(i=>"v"+i))+"\n"+
        "(function() local ok,mod=pcall(require,'GCET_Semantic/Controller') end)()\n");
    var semanticContext=core.GetType(
        "GCETRuntimeProfiler.Core.Services.SemanticPatchContext",true)!;
    var createCoordinator=semanticContext.GetMethod("WriteCoordinator",
        BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!;
    var context=Activator.CreateInstance(semanticContext,
        BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,
        null,new object[]{mods,coordinatorOwner,"reviewed-controller",
            new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase)},null)!;
    var body="-- G-CET semantic coordinator:reviewed-controller\n"+
        "local "+string.Join(",",Enumerable.Range(1,160).Select(i=>"n"+i))+"\n"+
        "return function(author) return author.update end\n";
    createCoordinator.Invoke(context,["Controller.lua",body]);
    // Identical re-injection in the same pass is a no-op. The existing
    // conflicting-write assertion below must still refuse different bytes.
    createCoordinator.Invoke(context,["Controller.lua",body]);
    var changes=(System.Collections.IEnumerable)semanticContext.GetProperty(
        "Changes",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!
        .GetValue(context)!;
    var changeRows=changes.Cast<object>().ToArray();
    Check(changeRows.Length==1,"Reviewed coordinator must stage exactly one new owned file");
    var change=changeRows[0];
    var rel=(string)change.GetType().GetProperty("RelativeFile")!.GetValue(change)!;
    Check(rel=="CoordinatorFixture/GCET_Semantic/Controller.lua",
        "Coordinator path escaped its owning mod");
    Check(!File.Exists(Path.Combine(coordinatorRoot,"GCET_Semantic","Controller.lua")),
        "Semantic coordinator generation modified installed mods");
    var luaBudget=core.GetType(
        "GCETRuntimeProfiler.Core.Services.LuaLocalBudgetAnalyzer",true)!;
    var analyzeBudget=luaBudget.GetMethod("Analyze",
        BindingFlags.Static|BindingFlags.NonPublic|BindingFlags.Public,
        [typeof(string)])!;
    var sourceBudget=analyzeBudget.Invoke(null,
        [File.ReadAllText(Path.Combine(coordinatorRoot,"init.lua"))])!;
    var coordinatorBudget=analyzeBudget.Invoke(null,[body])!;
    int Active(object value)=>(int)value.GetType().GetProperty("MaxActiveLocals")!.GetValue(value)!;
    Check(Active(sourceBudget)<=200 && Active(coordinatorBudget)<=200,
        "Semantic sidecar must keep its Lua locals in an independent chunk");
    try {
        createCoordinator.Invoke(context,["../hijack.lua",body]);
        throw new InvalidOperationException("Path traversal was accepted");
    } catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException) {}
    try {
        createCoordinator.Invoke(context,["Controller.lua",
            "-- G-CET semantic coordinator:reviewed-controller\nreturn nil\n"]);
        throw new InvalidOperationException("Conflicting coordinator update was accepted");
    } catch(TargetInvocationException e) when(e.InnerException is InvalidOperationException) {}
    Console.WriteLine("GCET semantic coordinator packaging/source/local-budget PASS");

    Console.WriteLine("GCET NativeReadReuse source-first contracts PASS");
}
finally { Directory.Delete(root,true); }
