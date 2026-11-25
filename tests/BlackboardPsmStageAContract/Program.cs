using System.Reflection;
using System.Text;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
var assembly = typeof(GCETRuntimeProfiler.Core.Services.PassGeneratorService).Assembly;
var cls = assembly.GetType("GCETRuntimeProfiler.Core.Services.BlackboardPsmDefinitionCacheSource",
    throwOnError: true)!;
var transform = cls.GetMethod("TryTransformCore",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var pinned = cls.GetMethod("TryTransform",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var evidence = cls.GetMethod("HasMeasuredCallsiteEvidence",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var defs = new[]
{
    ("locomotion", "Locomotion", "GetInt"),
    ("locomotionDetailed", "LocomotionDetailed", "GetInt"),
    ("upperBody", "UpperBody", "GetInt"),
    ("weapon", "Weapon", "GetInt"),
    ("vision", "Vision", "GetInt"),
    ("swimming", "Swimming", "GetInt"),
    ("sceneTier", "SceneTier", "GetInt"),
    ("takedown", "Takedown", "GetInt"),
    ("landing", "Landing", "GetInt"),
    ("bodyCarrying", "BodyCarrying", "GetInt"),
    ("carrying", "Carrying", "GetInt"),
    ("mounted", "MountedToVehicle", "GetBool")
};
var before = new StringBuilder();
before.AppendLine("local PlayerStateDefs = nil")
      .AppendLine("local playerBB = nil")
      .AppendLine("local BlackboardCache = {}")
      .AppendLine("function BlackboardCache.Update(player)")
      .AppendLine("    if not playerBB then return end");
foreach (var (local, key, getter) in defs)
    before.AppendLine($"    local {local} = playerBB:{getter}(PlayerStateDefs.{key})");
before.AppendLine("    if mounted then Trigger('MountedToVehicleChanged', mounted) end")
      .AppendLine("end")
      .AppendLine("function BlackboardCache.Init(events)")
      .AppendLine("    PlayerStateDefs = GetAllBlackboardDefs().PlayerStateMachine")
      .AppendLine("end")
      .AppendLine("function BlackboardCache.Attach(player)")
      .AppendLine("    playerBB = Game.GetBlackboardSystem():GetLocalInstanced(player:GetEntityID(), PlayerStateDefs)")
      .AppendLine("end")
      .AppendLine("function BlackboardCache.Reset()")
      .AppendLine("    playerBB = nil")
      .AppendLine("end");
var original = Encoding.UTF8.GetBytes(before.ToString());
object?[] a = [original, null, null];
Check((bool)transform.Invoke(null, a)!, "Fixture structural rewrite rejected");
var output = (byte[])a[1]!;
var after = Encoding.UTF8.GetString(output);
Check((string)a[2]! == "APPLIED", "Unexpected success reason");
Check(after.Contains("G-CET PSM_DEFS_STAGE_A_V1"), "Stage marker missing");
Check(after.Contains("MountedToVehicleChanged"), "Original event logic lost");
Check(after.Contains("BlackboardCache.Attach(player)"), "Attach contract removed");
Check(after.Contains("BlackboardCache.Reset()"), "Reset contract removed");
Check(after.Split("playerBB:GetInt(").Length - 1 == 11, "Changed GetInt count");
Check(after.Split("playerBB:GetBool(").Length - 1 == 1, "Changed GetBool count");
foreach (var (_, field, getter) in defs)
{
    Check(after.Contains($"{field} = PlayerStateDefs.{field},"),
        $"Definition {field} not captured in Init");
    Check(after.Contains($"playerBB:{getter}(__gcetPsmFields.{field})"),
        $"Read {field} not replaced");
}
object?[] duplicate = [output, null, null];
Check(!(bool)transform.Invoke(null, duplicate)!, "Double-injected already optimized source");
Check((string)duplicate[2]! == "ALREADY_SATISFIED", "Bad idempotence status");
// An otherwise identical source with local author metadata is accepted
// by the production path without the legacy SHA pin.
var modified = Encoding.UTF8.GetBytes(before + "-- author version-fluid comment\n");
object?[] noPin = [modified, null, null];
Check((bool)pinned.Invoke(null, noPin)!, "Source-compatible local edit rejected by production");
var drift = before.ToString().Replace(
    "playerBB:GetInt(PlayerStateDefs.Weapon)",
    "playerBB:GetInt(PlayerStateDefs.SomethingElse)");
object?[] invalid = [Encoding.UTF8.GetBytes(drift), null, null];
Check(!(bool)transform.Invoke(null, invalid)!, "Structural drift was not rejected");
// Partial cache: existing declaration/table, missing entries, stock reads,
// and complete-cache marker must converge from the original generator.
string Rewrite(string input)
{
    object?[] args2=[Encoding.UTF8.GetBytes(input),null,null];
    Check((bool)transform.Invoke(null,args2)!, "Expected compatible incomplete cache: "+args2[2]);
    return Encoding.UTF8.GetString((byte[])args2[1]!);
}
void Complete(string input,string label)
{
    var generated=Rewrite(input);
    foreach(var (_,key,getter) in defs)
        Check(System.Text.RegularExpressions.Regex.IsMatch(generated,@"playerBB\s*:\s*"+getter+@"\s*\(\s*__gcetPsmFields\s*\.\s*"+key+@"\s*\)")&&
            generated.Contains(key+" = PlayerStateDefs."+key),label+": "+key);
    object?[] repeat=[Encoding.UTF8.GetBytes(generated),null,null];
    Check(!(bool)transform.Invoke(null,repeat)!&&(string)repeat[2]! == "ALREADY_SATISFIED",
        label+": generated result did not converge: "+repeat[2]);
}
var partial=after.Replace("playerBB:GetInt(__gcetPsmFields.Weapon)","playerBB:GetInt(PlayerStateDefs.Weapon)")
    .Replace("        Weapon = PlayerStateDefs.Weapon,"+Environment.NewLine,"");
Complete(partial,"partial snapshot and read");
Complete(after.Replace("local __gcetPsmFields = nil  -- G-CET PSM_DEFS_STAGE_A_V1","-- G-CET PSM_DEFS_STAGE_A_V1"),"missing local declaration");
Complete(after.Replace("  -- G-CET PSM_DEFS_STAGE_A_V1",""),"manual complete cache missing marker");
Complete(before.ToString().Replace("playerBB:GetInt(PlayerStateDefs.Weapon)","playerBB:GetInt( PlayerStateDefs . Weapon )"),"whitespace variant");
var noComma=partial.Replace("MountedToVehicle = PlayerStateDefs.MountedToVehicle,","MountedToVehicle = PlayerStateDefs.MountedToVehicle");
Complete(noComma,"last key without trailing comma");
foreach(var bad in new[]{
    after+"\nPlayerStateDefs = otherDefs",
    after+"\n__gcetPsmFields.Weapon = otherKey",
    after.Replace("Weapon = PlayerStateDefs.Weapon","Weapon = PlayerStateDefs.Vision"),
    before.ToString()+"\nlocal alias = PlayerStateDefs",
    after.Replace("local __gcetPsmFields = nil","local __gcetPsmFields = {}")
})
{
    object?[] refused=[Encoding.UTF8.GetBytes(bad),null,null];
    Check(!(bool)transform.Invoke(null,refused)!&&(string)refused[2]!="ALREADY_SATISFIED",
        "unproved definition ownership admitted");
}
if(args.Length==2&&args[0]=="--stock")
{
    var stock=File.ReadAllText(Path.Combine(args[1],"modules","BlackboardCache2.lua"));
    var generated=Rewrite(stock);
    Complete(generated.Replace("playerBB:GetInt(__gcetPsmFields.Weapon)","playerBB:GetInt(PlayerStateDefs.Weapon)")
        .Replace("        Weapon = PlayerStateDefs.Weapon,"+ (generated.Contains("\r\n")?"\r\n":"\n"),""),"actual stock partial");
    Check(generated.Split("playerBB:GetInt(").Length==stock.Split("playerBB:GetInt(").Length&&
        generated.Split("playerBB:GetBool(").Length==stock.Split("playerBB:GetBool(").Length,
        "actual PSM mutable state reads changed");
    Console.WriteLine("Actual original PSM definition-cache partial completion PASS");
}
var folder = Path.Combine(Path.GetTempPath(), "gcet-psm-defs-" + Guid.NewGuid().ToString("N"));
try
{
    var dir = Path.Combine(folder, "Data", "Runtime");
    Directory.CreateDirectory(dir);
    var csv = Path.Combine(dir, "CET_Runtime_Profile_LuaNative_Callsites.csv");
    var header = "RegistrationId,ProfileEpoch,SampleSequence,Frame,CallerSourceFile,CallerLine,ParentFunctionKey,ChildFunctionKey,SampledCCalls,SampledCOwnPositiveBytes,SampledCOwnPositiveOps,Attribution,Interpretation";
    var lines = new List<string> { header };
    for (int i = 0; i < 12; i++)
        lines.Add($"5,1,1,83,D:/Games/mods/0-Engine/modules/BlackboardCache2.lua,{193+i},Lua,|__index|C,1,167,1,SAMPLED_C_FRAME_EXCLUSIVE,NOTE");
    File.WriteAllLines(csv, lines);
    Check((bool)evidence.Invoke(null, [folder])!, "Full 12-line measured evidence rejected");
    File.WriteAllLines(csv, lines.Take(lines.Count - 1));
    Check(!(bool)evidence.Invoke(null, [folder])!, "Incomplete callsite coverage admitted");
    lines[1] = lines[1].Replace("167,1", "0,1");
    File.WriteAllLines(csv, lines);
    Check(!(bool)evidence.Invoke(null, [folder])!, "Zero positive allocator evidence admitted");
}
finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
Console.WriteLine("0-Engine BlackboardCache Stage A: source, idempotence, native evidence, and drift contracts PASS");
