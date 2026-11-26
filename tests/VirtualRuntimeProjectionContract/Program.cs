using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool yes, string reason)
{
    if (!yes) throw new InvalidOperationException(reason);
}
static string Hash(string file) => Convert.ToHexString(
    SHA256.HashData(File.ReadAllBytes(file)));
var core = typeof(PassGeneratorService).Assembly;
var cls = core.GetType("GCETRuntimeProfiler.Core.Services.EffectiveRuntimeView", true)!;
const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance |
    BindingFlags.NonPublic | BindingFlags.Public;
var build = cls.GetMethod("Build", flags)!;
var frameworkProperty = cls.GetProperty("Framework", flags)!;
var frameworkType = core.GetType("GCETRuntimeProfiler.Core.Services.FixedZeroEngineBuild", true)!;
var filesProperty = frameworkType.GetProperty("Files", flags)!;
var root = Path.Combine(Path.GetTempPath(), "gcet-runtime-projection-" + Guid.NewGuid().ToString("N"));
var installedMods = Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
var installedEngine = Path.Combine(installedMods, "0-Engine");
var originalInit = Path.Combine(installedEngine, "init.lua");
// The native package filename and compiled data lookup must agree.
// Exercise both historical and canonical layouts without altering a
// downloaded artifact or treating filename alone as capability evidence.
var bundleType=core.GetType("GCETRuntimeProfiler.Core.Services.NativeRuntimeBundle",true)!;
var loadBundle=bundleType.GetMethod("TryLoadFrom",flags)!;
var deployment=bundleType.GetProperty("DeploymentPath",flags)!;
var locationDeployment=bundleType.GetProperty("LocationDeploymentPath",flags)!;
var guard=bundleType.GetMethod("RequireNoConflictingInstalledRuntime",flags)!;
var namingRoot=Path.Combine(Path.GetTempPath(),"gcet-naming-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(namingRoot);
try
{
    var fixture=Path.Combine(namingRoot,"bundle");
    Directory.CreateDirectory(fixture);
    byte[] Payload(bool canonical)
    {
        var bytes=new byte[2048];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';
        Encoding.ASCII.GetBytes("GCETStaticAddAnchor GCETStaticCoordinatorState GCETPlayerVector4Live GCETPlayerVector4Valid").CopyTo(bytes,16);
        Encoding.Unicode.GetBytes(canonical?"G-Native.locations":"G-CET.locations").CopyTo(bytes,301);
        return bytes;
    }
    void Refuses(Action f,string reason)
    {
        try{f();}catch(TargetInvocationException ex) when(ex.InnerException is InvalidOperationException){return;}
        throw new InvalidOperationException(reason);
    }
    object Read()=>loadBundle.Invoke(null,[fixture,true])!;
    var legacy=Path.Combine(fixture,"GCETStaticLocations.dll");
    var modern=Path.Combine(fixture,"G-Engine.dll");
    File.WriteAllBytes(legacy,Payload(false));
    var old=Read();
    Check((string)deployment.GetValue(old)=="red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll" &&
        (string)locationDeployment.GetValue(old)=="red4ext/plugins/G-CET-StaticLocations/G-CET.locations","historical package path contract");
    File.WriteAllBytes(modern,Payload(true));
    Refuses(()=>Read(),"duplicate packaged native owners must be refused");
    File.Delete(legacy);
    var renamed=Read();
    Check((string)deployment.GetValue(renamed)=="red4ext/plugins/G-NativeRuntime/G-Engine.dll" &&
        (string)locationDeployment.GetValue(renamed)=="red4ext/plugins/G-NativeRuntime/G-Native.locations","canonical package path contract");
    File.WriteAllBytes(modern,Payload(false));
    Refuses(()=>Read(),"renaming legacy bytes without changing lookup must be refused");
    var game=Path.Combine(namingRoot,"game");
    var mods=Path.Combine(game,"bin","x64","plugins","cyber_engine_tweaks","mods");
    Directory.CreateDirectory(mods);
    var installedOld=Path.Combine(game,((string)deployment.GetValue(old)!).Replace('/',Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(installedOld)!);
    File.WriteAllBytes(installedOld,Payload(false));
    Refuses(()=>guard.Invoke(renamed,[mods]),"enabled historical native DLL must block canonical overlay");
    Check(File.Exists(installedOld),"migration may not delete installed files");
    File.Delete(installedOld);
    guard.Invoke(renamed,[mods]);
    var installedNew=Path.Combine(game,((string)deployment.GetValue(renamed)!).Replace('/',Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(installedNew)!);
    File.WriteAllBytes(installedNew,Payload(true));
    Refuses(()=>guard.Invoke(old,[mods]),"enabled canonical DLL must block historical overlay");
    guard.Invoke(renamed,[mods]);
    Console.WriteLine("Native naming: exact paired layouts, mixed package refusal, compiled manifest lookup proof and read-only migration guard PASS");
}
finally { Directory.Delete(namingRoot,true); }
var bundledRoot = Path.Combine(AppContext.BaseDirectory, "runtime", "0-Engine");
var repoRoot = Directory.GetCurrentDirectory();
Directory.CreateDirectory(installedEngine);
Directory.CreateDirectory(bundledRoot);
try
{
    var shippedRuntime = Path.Combine(repoRoot, "resolver-runtime", "0-Engine");
    Check(Directory.Exists(shippedRuntime), "CI must run this contract from repo root");
    foreach (var source in Directory.GetFiles(shippedRuntime, "*", SearchOption.AllDirectories))
    {
        var dest = Path.Combine(bundledRoot, Path.GetRelativePath(shippedRuntime, source));
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    // Source-structural host with missing 0-Engine extensions. The complete
    // overlay must be synthesized without touching this installed file.
    var stock = """
-- stock 0-Engine, no routing or scheduler integration
local Engine = {}
function Engine.Register(name)
    return {name=name}
end
return Engine
""";
    File.WriteAllText(originalInit, stock, new UTF8Encoding(false));
    string originalHash = Hash(originalInit);
    object view = build.Invoke(null, [installedMods])!;
    var fw = frameworkProperty.GetValue(view)!;
    Check(fw is not null, "Minimal exported-table host must admit additive runtime");
    var files = (IReadOnlyDictionary<string, byte[]>)filesProperty.GetValue(fw)!;
    var init = Encoding.UTF8.GetString(files["0-Engine/init.lua"]);
    foreach (var api in new[] { "MakeEventRegistrar", "SubscribeAction",
                                "GCETNativeLocationHost",
                                "modules/G-CET/Scheduler" })
        Check(init.Contains(api), "Missing virtual API: " + api);
    // The namespaced adapter defines methods from __names at runtime;
    // no literal "Schedule.Every" source token is emitted.
    Check(init.Contains("EveryFrame") && init.Contains("\"Every\"") &&
          init.Contains("__host.Schedule = __schedule") &&
          init.Contains("__scheduler.Update"),
          "Projected scheduler is not wired to its dynamic Every/Update API");
    Check(init.Contains("G-CET namespaced scheduler compatibility v1"),
        "Missing Scheduler executable bridge");
    Check(files.ContainsKey("0-Engine/modules/G-CET/Scheduler.lua"),
        "Bundled namespaced Scheduler dependency missing");
    Check(files.ContainsKey("0-Engine/modules/G-CET/ActionRouter.lua"),
        "Bundled namespaced ActionRouter dependency missing");

    // Unknown/foreign 0-Engine is host-preserving: its original root
    // modules must not be silently replaced with G-CET aliases.
    Check(!files.ContainsKey("0-Engine/modules/Scheduler.lua"),
        "Foreign host root Scheduler must be preserved");
    Check(!files.ContainsKey("0-Engine/modules/ActionRouter.lua"),
        "Resolver overlay must not overwrite upstream root ActionRouter");
    var namespacedScheduler = Encoding.UTF8.GetString(
        files["0-Engine/modules/G-CET/Scheduler.lua"]);
    foreach (var instrumentation in new[] {
        "CETProfilerSchedulerRegisterJob", "CETProfilerSchedulerFrameBegin",
        "CETProfilerSchedulerFrameEnd", "CETProfilerSchedulerJobBegin",
        "CETProfilerSchedulerJobEnd"
    })
        Check(namespacedScheduler.Contains(instrumentation, StringComparison.Ordinal),
            "G-CET Scheduler payload lacks native job instrumentation: " + instrumentation);

    // Older G-CET-integrated sources must migrate their private imports,
    // while unrelated future upstream module requires remain untouched.
    var routeLegacy = core.GetType("GCETRuntimeProfiler.Core.Services.FixedZeroEngineRuntime", true)!.GetMethod("RouteLegacyGcetImportsToNamespace", flags)!;
    string ResolveImports(string value) => Encoding.UTF8.GetString(
        (byte[])routeLegacy.Invoke(null, [Encoding.UTF8.GetBytes(value)])!);
    var legacyGcet = """
local Scheduler = require('modules/Scheduler')
local ActionRouter = require("modules/ActionRouter")
Scheduler.Init(Logger, Health)
ActionRouter.Init(Logger, Health, function() return 0 end)
Scheduler.Update(ctx)
ActionRouter.Dispatch(player, action, consumer)
Engine.Schedule = {}
Engine.SubscribeAction = function() end
""";
    var migrated = ResolveImports(legacyGcet);
    Check(migrated.Contains("require('modules/G-CET/Scheduler')", StringComparison.Ordinal) &&
          migrated.Contains("require(\"modules/G-CET/ActionRouter\")", StringComparison.Ordinal),
        "Source-proven legacy G-CET integration did not adopt the namespace");
    Check(ResolveImports(migrated) == migrated,
        "G-CET namespace migration is not idempotent");
    var upstream = """
local Scheduler = require('modules/Scheduler')
local ActionRouter = require("modules/ActionRouter")
Scheduler.Init(Logger, Health)
return Engine
""";
    Check(ResolveImports(upstream) == upstream,
        "Resolver rewrote unrelated upstream Scheduler/ActionRouter requires");

    // Phase after must bypass a legacy host SubscribeAction which may
    // silently ignore the phase, and dispatch through our phase-aware module.
    Check(init.Contains("phase ~= \"after\" and type(__gcetHostSubscribeAction)", StringComparison.Ordinal),
        "After-phase action incorrectly admitted by a legacy host subscriber");
    Check(init.Contains("__gcetEnsureActionRouter(phase)", StringComparison.Ordinal),
        "After-phase route does not select its explicit action router");
    var router = Encoding.UTF8.GetString(files["0-Engine/modules/G-CET/ActionRouter.lua"]);
    Check(router.Contains("function ActionRouter.DispatchAfter(", StringComparison.Ordinal) &&
          router.Contains("phase == \"after\"", StringComparison.Ordinal),
        "Generated namespaced action router cannot dispatch CET after-phase");
    Check(init.Contains("phase ~= \"after\" and not __gcetActionObserverInstalled", StringComparison.Ordinal),
        "After-only subscriber unnecessarily installs a before-phase native observer");
    Check(files.ContainsKey("0-Engine/modules/GCETNativeLocationHost.lua"),
        "Bundled native location host dependency missing");
    Check(Hash(originalInit) == originalHash && File.ReadAllText(originalInit) == stock,
        "Analysis modified the user's installed 0-Engine");
    // Repeating Analyze must not change output or accumulate bridges.
    var repeat = build.Invoke(null, [installedMods])!;
    var repeatFiles = (IReadOnlyDictionary<string, byte[]>)
        filesProperty.GetValue(frameworkProperty.GetValue(repeat)!)!;
    Check(files.Keys.Order().SequenceEqual(repeatFiles.Keys.Order()) &&
          files.All(x => x.Value.AsSpan().SequenceEqual(repeatFiles[x.Key])),
        "Projected runtime composition is not repeatable");

    // A live profiler bridge must be projected from its original backup;
    // neither profiler instrumentation nor the backup can leak into a ZIP.
    var backupPath = Path.Combine(root, "bin", "x64", "plugins",
        ".cet_runtime_profiler", "0-Engine.init.ORIGINAL.lua");
    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
    File.WriteAllText(backupPath, stock);
    var temporaryProfilerInit = stock.Replace("return Engine",
        "-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2\nreturn Engine");
    File.WriteAllText(originalInit, temporaryProfilerInit);
    view = build.Invoke(null, [installedMods])!;
    fw = frameworkProperty.GetValue(view)!;
    files = (IReadOnlyDictionary<string, byte[]>)filesProperty.GetValue(fw)!;
    init = Encoding.UTF8.GetString(files["0-Engine/init.lua"]);
    Check(!init.Contains("CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN"),
        "Temporary profiler bridge leaked into projected optimization runtime");
    Check(File.ReadAllText(originalInit) == temporaryProfilerInit,
        "Projection modified profiler-managed live source");
    File.Delete(backupPath);
    view = build.Invoke(null, [installedMods])!;
    Check(frameworkProperty.GetValue(view) is null,
        "A missing pre-profiler backup must fail closed rather than embed the bridge");

    // An installed 0-Engine author change must remain authoritative.
    File.WriteAllText(originalInit, stock.Replace("return Engine",
        "Engine.CustomAuthorExtension = true\nreturn Engine"));
    view = build.Invoke(null, [installedMods])!;
    fw = frameworkProperty.GetValue(view)!;
    files = (IReadOnlyDictionary<string, byte[]>)filesProperty.GetValue(fw)!;
    init = Encoding.UTF8.GetString(files["0-Engine/init.lua"]);
    Check(init.Contains("Engine.CustomAuthorExtension = true"),
        "Projected framework lost an author-owned extension");
    Check(Hash(originalInit) != originalHash, "Test fixture did not change");
    Console.WriteLine("Virtual runtime composition, source ownership and backup isolation: PASS");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    // Isolated dotnet test output is not part of the source checkout.
    if (Directory.Exists(bundledRoot)) Directory.Delete(bundledRoot, recursive: true);
}
