using System.Diagnostics;
using System.Text;
using GCETRuntimeProfiler.Core.Services;
static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
var root = Path.Combine(Path.GetTempPath(), "gcet-demand-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var scheduler = File.ReadAllText("payload/0-Engine/modules/G-CET/Scheduler.lua");
    var host = """
local Proximity = require('modules/Proximity')
local SpatialHash = require('modules/SpatialHash')
local currentFrame = 0
local function UpdateFrame(delta)
    currentFrame = currentFrame + 1
    local pos = {x=0}
    Proximity.SetPlayerPos(pos)
    pcall(Proximity.Update, currentFrame)
    SpatialHash.SetPlayerPos(pos)
    pcall(SpatialHash.Update, currentFrame)
end
return UpdateFrame
""";
    var proximity = """
local Proximity = {}
local zones = {}
local zoneCounter = 0
function Proximity.RegisterZone(config)
    zoneCounter = zoneCounter + 1
    local zone = {throttle=config.throttle or 5, callback=config.callback}
    zones[zoneCounter] = zone
    local capturedId = zoneCounter
    return {unregister=function()
        zones[capturedId] = nil
    end}
end
function Proximity.SetPlayerPos(pos) end
function Proximity.Update(frame)
    count("proximity-pass")
    for _, zone in pairs(zones) do
        if frame % zone.throttle == 0 then zone.callback(frame) end
    end
end
return Proximity
""";
    var spatial = """
local SpatialHash = {}
local sets = {}
function SpatialHash.RegisterSpatialSet(name, entries, config, source)
    local setData = {config=config}
    sets[name] = setData
    return {unregister=function()
        sets[name] = nil
    end}
end
function SpatialHash.SetPlayerPos(pos) end
function SpatialHash.Update(frame)
    count("spatial-pass")
    for _, setData in pairs(sets) do
        local cfg = setData.config
        if cfg.pollRadiusSq and frame % cfg.pollThrottle == 0 then cfg.callback(frame) end
    end
end
return SpatialHash
""";
    var derived = File.ReadAllText("tests/ZeroEngineDemandContract/DerivedFixture.lua");
    var derivedHost = File.ReadAllText("tests/ZeroEngineDemandContract/DerivedHostFixture.lua");
    var lifecycle = File.ReadAllText("tests/ZeroEngineDemandContract/LifecycleFixture.lua");
    var moduleDir = Path.Combine(root, "0-Engine", "modules");
    Directory.CreateDirectory(moduleDir);
    var privateSource = args.Length == 2 && args[0] == "--stock";
    if (privateSource)
    {
        var author = Path.GetFullPath(args[1]);
        host = File.ReadAllText(Path.Combine(author, "init.lua"));
        proximity = File.ReadAllText(Path.Combine(author, "modules", "Proximity.lua"));
        spatial = File.ReadAllText(Path.Combine(author, "modules", "SpatialHash.lua"));
        derived = File.ReadAllText(Path.Combine(author, "modules", "DerivedState.lua"));
        derivedHost = host;
        lifecycle = File.ReadAllText(Path.Combine(author, "modules", "Lifecycle.lua"));
    }
    File.WriteAllText(Path.Combine(moduleDir, "Proximity.lua"), proximity);
    File.WriteAllText(Path.Combine(moduleDir, "SpatialHash.lua"), spatial);
    var files = new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(host),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(scheduler)
    };
    var framework = new FixedZeroEngineBuild(files, "", "", "", "");
    var staged = new Dictionary<string, byte[]>();
    var counts = new Dictionary<string, int>();
    var manifest = new List<object>(); var skipped = new List<object>();
    Check(ZeroEngineDemandSource.TryStage(root, ref framework, staged, counts, manifest, skipped) == 2,
        "expected both central producer integrations");
    Check(staged.Count == 2 && skipped.Count == 0, "producer admission");
    var generatedHost = framework.Files[ZeroEngineDemandSource.InitPath];
    var generatedScheduler = framework.Files[ZeroEngineDemandSource.SchedulerPath];
    var originalStaged = staged.ToDictionary(x => x.Key, x => x.Value);
    Check(ZeroEngineDemandSource.TryStage(root, ref framework, staged, counts, manifest, skipped) == 0,
        "second generation must apply zero changes");
    Check(generatedHost.SequenceEqual(framework.Files[ZeroEngineDemandSource.InitPath]) &&
        generatedScheduler.SequenceEqual(framework.Files[ZeroEngineDemandSource.SchedulerPath]) &&
        originalStaged.All(x => x.Value.SequenceEqual(staged[x.Key])), "repeat generation changed bytes");
    // Partial install: producer already generated but host and scheduler stock.
    var partial = new FixedZeroEngineBuild(files, "", "", "", "");
    var partialStage = new Dictionary<string, byte[]>(originalStaged);
    Check(ZeroEngineDemandSource.TryStage(root, ref partial, partialStage, new(), new(), new()) == 2,
        "partial installation must complete missing scheduler/host");
    Check(generatedHost.SequenceEqual(partial.Files[ZeroEngineDemandSource.InitPath]) &&
        generatedScheduler.SequenceEqual(partial.Files[ZeroEngineDemandSource.SchedulerPath]),
        "partial completion differs");
    Check(!ZeroEngineDemandSource.TryProducer(proximity.Replace(
        "zones[zoneCounter] = zone", "zones[zoneCounter] = zone\n    zones[999] = zone"),
        "Proximity", out _), "unproved extra registry mutation accepted");
    // Keep the existing spatial fixture independent; exercise the new
    // scalar transformation using the same production generator stage.
    File.WriteAllText(Path.Combine(moduleDir, "DerivedState.lua"), derived);
    File.WriteAllText(Path.Combine(moduleDir, "Lifecycle.lua"), lifecycle);
    Check(ZeroEngineDemandSource.TryDerivedProducer(derived, out _), "actual camera producer proof");
    Check(ZeroEngineDemandSource.TryDerivedHost(derivedHost, out _), "actual scalar host proof");
    Check(ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle, out _), "actual raw Lifecycle ownership proof");
    Check(!ZeroEngineDemandSource.TryDerivedLifecycle(lifecycle.Replace("State = state", "State = state\n    externalState = State"), out _), "unproved raw Lifecycle escape accepted");
    var scalarFramework = new FixedZeroEngineBuild(new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(derivedHost),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(scheduler)
    }, "", "", "", "");
    var scalarStage = new Dictionary<string, byte[]>();
    var scalarCount = ZeroEngineDemandSource.TryStage(root, ref scalarFramework, scalarStage, new(), new(), new());
    Check(scalarCount == (privateSource ? 3 : 1), "central scalar sample admission: " + scalarCount);
    var scalarHost = scalarFramework.Files[ZeroEngineDemandSource.InitPath];
    var scalarScheduler = scalarFramework.Files[ZeroEngineDemandSource.SchedulerPath];
    var scalarModule = scalarStage["0-Engine/modules/DerivedState.lua"];
    var scalarLifecycle = scalarStage["0-Engine/modules/Lifecycle.lua"];
    Check(ZeroEngineDemandSource.TryStage(root, ref scalarFramework, scalarStage, new(), new(), new()) == 0,
        "central scalar repeat generation");
    Check(scalarHost.SequenceEqual(scalarFramework.Files[ZeroEngineDemandSource.InitPath]) &&
        scalarScheduler.SequenceEqual(scalarFramework.Files[ZeroEngineDemandSource.SchedulerPath]) &&
        scalarModule.SequenceEqual(scalarStage["0-Engine/modules/DerivedState.lua"]), "scalar repeat changed bytes");
    Check(!ZeroEngineDemandSource.TryDerivedProducer(derived.Replace(
        "cached.lookPitch = resolveLookPitch()", "cached.lookPitch = resolveLookPitch()\n    print(cached.lookPitch)"),
        out _), "internal scalar dependency must refuse");
    var idleHostFixture = File.ReadAllText("tests/ZeroEngineDemandContract/IdleHostFixture.lua");
    Check(ZeroEngineDemandSource.TryIdleScheduler(scheduler, out var idleScheduler), "canonical scheduler active job accounting proof");
    Check(ZeroEngineDemandSource.TryIdleHost(idleHostFixture, out var idleHost), "canonical idle host proof");
    Check(ZeroEngineDemandSource.TryIdleScheduler(idleScheduler, out var idleRepeat) &&
        idleRepeat == idleScheduler, "idle scheduler idempotence");
    Check(ZeroEngineDemandSource.TryIdleHost(idleHost, out var idleHostRepeat) &&
        idleHostRepeat == idleHost, "idle host idempotence");
    Check(!ZeroEngineDemandSource.TryIdleScheduler(scheduler.Replace(
        "function Scheduler.SetGeneration(value)", "function Scheduler.SetGeneration(value)\n    job.active = false"),
        out _), "unproved job state mutation must refuse");
    // Independent framework-only stage, including missing host/scheduler.
    var idleRoot = Path.Combine(root, "idle");
    Directory.CreateDirectory(idleRoot);
    FixedZeroEngineBuild IdleFramework(string h, string s) => new(new Dictionary<string, byte[]> {
        [ZeroEngineDemandSource.InitPath] = Encoding.UTF8.GetBytes(h),
        [ZeroEngineDemandSource.SchedulerPath] = Encoding.UTF8.GetBytes(s)
    }, "", "", "", "");
    var idleBuild = IdleFramework(idleHostFixture, scheduler);
    Check(ZeroEngineDemandSource.TryStage(idleRoot, ref idleBuild, new(), new(), new(), new()) == 1,
        "framework-only idle scheduler stage");
    var idleFullScheduler = Encoding.UTF8.GetString(idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]);
    foreach (var partialPair in new[] { (idleHost, scheduler), (idleHostFixture, idleFullScheduler) })
    {
        var incomplete = IdleFramework(partialPair.Item1, partialPair.Item2);
        Check(ZeroEngineDemandSource.TryStage(idleRoot, ref incomplete, new(), new(), new(), new()) == 1,
            "complete missing idle integration");
        Check(incomplete.Files[ZeroEngineDemandSource.InitPath].SequenceEqual(idleBuild.Files[ZeroEngineDemandSource.InitPath]) &&
            incomplete.Files[ZeroEngineDemandSource.SchedulerPath].SequenceEqual(idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]),
            "partial idle completion output differs");
    }
    Check(ZeroEngineDemandSource.TryStage(idleRoot, ref idleBuild, new(), new(), new(), new()) == 0,
        "repeat framework-only idle stage");
    if (privateSource)
    {
        Console.WriteLine("Private stock 0-Engine central demand generation and idempotence PASS");
        return;
    }
    var ns = Path.Combine(moduleDir, "G-CET"); Directory.CreateDirectory(ns);
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), generatedScheduler);
    File.WriteAllBytes(Path.Combine(root, "0-Engine", "init.lua"), generatedHost);
    foreach (var pair in staged) File.WriteAllBytes(Path.Combine(root, pair.Key), pair.Value);
    void RunLua(string scenario)
    {
        var psi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
        psi.ArgumentList.Add("tests/ZeroEngineDemandContract/Execute.lua");
        psi.ArgumentList.Add(Path.Combine(root, "0-Engine"));
        psi.ArgumentList.Add(scenario);
        using var process = Process.Start(psi)!; process.WaitForExit();
        Check(process.ExitCode == 0, "actual generated execution failed: " + scenario);
    }
    RunLua("complete");
    // An overlay may contain the generated host but an author producer wins
    // source precedence. It must retain stock work and delivery, not silently
    // lose callbacks just because no demand was tracked.
    File.WriteAllText(Path.Combine(moduleDir, "Proximity.lua"), proximity);
    RunLua("missing-producer");
    File.WriteAllBytes(Path.Combine(moduleDir, "Proximity.lua"),
        staged["0-Engine/modules/Proximity.lua"]);
    File.WriteAllText(Path.Combine(ns, "Scheduler.lua"), scheduler);
    RunLua("missing-controller");
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), scalarScheduler);
    File.WriteAllBytes(Path.Combine(root, "0-Engine", "init.lua"), scalarHost);
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllBytes(Path.Combine(moduleDir, "Lifecycle.lua"), scalarLifecycle);
    void RunScalar(string scenario)
    {
        var psi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
        psi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteDerived.lua");
        psi.ArgumentList.Add(Path.Combine(root, "0-Engine"));
        psi.ArgumentList.Add(scenario);
        using var process = Process.Start(psi)!; process.WaitForExit();
        Check(process.ExitCode == 0, "generated scalar execution failed: " + scenario);
    }
    foreach (var scenario in new[] { "scalar", "invalid-epoch", "raw-root", "raw-derived", "raw-module", "raw-root-before-update" })
        RunScalar(scenario);
    File.WriteAllText(Path.Combine(moduleDir, "DerivedState.lua"), derived);
    RunScalar("old-producer");
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllText(Path.Combine(moduleDir, "Lifecycle.lua"), lifecycle);
    RunScalar("old-lifecycle");
    File.WriteAllBytes(Path.Combine(moduleDir, "Lifecycle.lua"), scalarLifecycle);
    File.WriteAllBytes(Path.Combine(moduleDir, "DerivedState.lua"), scalarModule);
    File.WriteAllText(Path.Combine(ns, "Scheduler.lua"), scheduler);
    RunScalar("old-controller");
    File.WriteAllText(Path.Combine(root, "0-Engine", "init.lua"), derivedHost);
    File.WriteAllBytes(Path.Combine(ns, "Scheduler.lua"), scalarScheduler);
    RunScalar("old-host");
    var idleSchedulerPath = Path.Combine(ns, "Scheduler.lua");
    File.WriteAllBytes(idleSchedulerPath, idleBuild.Files[ZeroEngineDemandSource.SchedulerPath]);
    File.WriteAllText(Path.Combine(root, "idle-original.lua"), scheduler);
    File.WriteAllText(Path.Combine(root, "idle-host-original.lua"), idleHostFixture);
    File.WriteAllBytes(Path.Combine(root, "idle-host-generated.lua"), idleBuild.Files[ZeroEngineDemandSource.InitPath]);
    var idlePsi = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
    idlePsi.ArgumentList.Add("tests/ZeroEngineDemandContract/ExecuteIdle.lua");
    idlePsi.ArgumentList.Add(root);
    idlePsi.ArgumentList.Add(idleSchedulerPath);
    using (var idleProcess = Process.Start(idlePsi)!)
    {
        idleProcess.WaitForExit();
        Check(idleProcess.ExitCode == 0, "generated idle scheduler execution");
    }

}
finally { Directory.Delete(root, true); }
