using System.Diagnostics;
using System.Text;
using GCETRuntimeProfiler.Core.Models;
using GCETRuntimeProfiler.Core.Services;

static void Check(bool condition, string reason)
{
    if (!condition) throw new InvalidOperationException(reason);
}

var root = Path.Combine(Path.GetTempPath(), "gcet-adaptive-update-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var manager = new ZeroEngineService(new PackageManifest
    {
        ZeroEngine = new ZeroEngineManifest
        {
            ProfilerBridgeMarker = "CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2"
        }
    });
    var paths = ProfilerPaths.FromGameRoot(root);
    Directory.CreateDirectory(paths.ZeroRoot);
    // The private-source lane uses the actual current stock host, never
    // publishes it and does not need a game or Lua mock for structural proof.
    if (args.Length == 2 && args[0] == "--stock")
    {
        var authorPath = Path.GetFullPath(args[1]);
        var author = File.ReadAllBytes(authorPath);
        File.WriteAllBytes(paths.ZeroInit, author);
        Check(manager.GetInitState(paths).Kind == "adaptive",
            "Private untouched 0-Engine is not admitted to adaptive composition; inspect current source.");
        manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
        var generated = File.ReadAllText(paths.ZeroInit);
        var original = File.ReadAllText(authorPath);
        NativeLocationShutdownContract.CheckStock(author);
        var bridgeStart = generated.IndexOf("-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2",
            StringComparison.Ordinal);
        var bridgeEnd = generated.IndexOf("-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_END v2",
            StringComparison.Ordinal);
        var firstOriginalLine = original.Split('\n')[0].TrimEnd('\r');
        var originalStart = generated.IndexOf(firstOriginalLine, StringComparison.Ordinal);
        Check(originalStart > 0 && bridgeStart > originalStart && bridgeEnd > bridgeStart,
            "Private original host composition markers missing.");
        // Independently reconstruct the original source around the single
        // temporary bridge and compare every original character. The only
        // replaced token is the original onUpdate registration callee.
        const string endMarker = "-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_END v2";
        var beforeBridge = generated[originalStart..bridgeStart]
            .Replace("__CETRP_RegisterAdaptiveUpdate", "registerForEvent", StringComparison.Ordinal);
        var tail = generated[(bridgeEnd + endMarker.Length)..].TrimStart('\r', '\n');
        Check(beforeBridge + tail == original, "Original host source changed outside update callee/temporary bridge.");
        Check(generated.Contains("__CETRP_RegisterAdaptiveUpdate(", StringComparison.Ordinal),
            "Original host has no composed update registration.");
        Check(!generated.Contains("registerForEvent(\"onUpdate\", __CETRP_RunAdaptiveFrame)", StringComparison.Ordinal),
            "Existing host gained an independent update registration.");
        manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
        Check(File.ReadAllText(paths.ZeroInit) == generated, "Private source composition not idempotent.");
        Check(author.SequenceEqual(File.ReadAllBytes(authorPath)), "Private original source was modified.");
        Console.WriteLine("Private untouched 0-Engine: original source preserved, composed update and idempotence PASS");
        return;
    }
    NativeLocationShutdownContract.Run(root);
    var harness = Path.GetFullPath("tests/AdaptiveProfilerUpdateContract/Execute.lua");
    var stock = """
local Engine = {}
local state = 0
local Cron = { Update = function(dt) record("cron", dt) end }
function Engine.Register(name) return {name=name} end
function Engine.GetState() return {count=state} end
local function UpdateFrame(dt)
    state = state + 1
    record("state", dt)
end
-- registerForEvent("onUpdate", function() error("comment") end)
local ignored = [=[registerForEvent("onUpdate", function() end)]=]
registerForEvent("onInit", function() record("init") end)
registerForEvent("onUpdate", function(delta)
    Cron.Update(delta)
    if delta == 0 then return end
    if delta < 0 then error("host failure") end
    UpdateFrame(delta)
end)
registerForEvent("onShutdown", function() record("shutdown") end)
return Engine
""";

    void Execute(string label, string source, string scenario)
    {
        File.WriteAllText(paths.ZeroInit, source, new UTF8Encoding(false));
        Check(manager.GetInitState(paths).Kind == "adaptive", label + ": compatible host not admitted");
        manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
        var generated = File.ReadAllBytes(paths.ZeroInit);
        manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
        Check(generated.SequenceEqual(File.ReadAllBytes(paths.ZeroInit)), label + ": repeat mutation changed output");
        var processInfo = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
        processInfo.ArgumentList.Add(harness);
        processInfo.ArgumentList.Add(paths.ZeroInit);
        processInfo.ArgumentList.Add(scenario);
        using var proc = Process.Start(processInfo)!;
        proc.WaitForExit();
        Check(proc.ExitCode == 0, label + ": generated Lua execution failed");
    }
    Execute("original closure", stock, "stock");
    Execute("custom last effective registration", stock.Replace(
        "return Engine", "registerForEvent('onUpdate', function(dt) record('last', dt) end)\nreturn Engine"),
        "last");
    Execute("no original update", "local Engine = {}\nreturn Engine\n", "empty");
    Execute("named callback and custom formatting", """
local Engine = {}
local function tick(delta) record("last", delta) end
registerForEvent -- comment between tokens
    ( 'onUpdate', tick )
return Engine
""", "named");

    // Preserve the existing handoff/ownership contract for a marker-only
    // host and a legacy bridge that has no original callback to replace.
    foreach (var existing in new[]
    {
        "local Engine={}\n-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2\nreturn Engine",
        "local Engine={}\n-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2\nregisterForEvent('onUpdate', function() end)\nreturn Engine"
    })
    {
        File.WriteAllText(paths.ZeroInit, existing);
        var before = File.ReadAllBytes(paths.ZeroInit);
        Check(manager.GetInitState(paths).Kind == "profiler-bridge", "safe preexisting handoff rejected");
        manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
        Check(before.SequenceEqual(File.ReadAllBytes(paths.ZeroInit)), "preexisting handoff was rewritten");
    }

    foreach (var source in new[]
    {
        "local Engine={}\nlocal register = registerForEvent\nregister('onUpdate', function() end)\nreturn Engine",
        "local Engine={}\nregisterForEvent(eventName, function() end)\nreturn Engine",
        "local Engine={}\nrequire('unknown')\nreturn Engine",
        "local Engine={}\nregisterForEvent('onUpdate', function() record('original') end)\n-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2\nregisterForEvent('onUpdate', function() record('profiler') end)\nreturn Engine",
        "local Engine={}\nrequire('unknown')\n-- CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2\nregisterForEvent('onUpdate', function() end)\nreturn Engine",
        "local Engine={}\nreturn Engine\n-- no actual exported table after a dynamic wrapper\n"
            + "other()"
    })
    {
        File.WriteAllText(paths.ZeroInit, source);
        var before = File.ReadAllBytes(paths.ZeroInit);
        Check(manager.GetInitState(paths).Kind == "unsafe", "ambiguous or old bridge incorrectly admitted");
        try
        {
            manager.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
            throw new Exception("Unsafe bridge unexpectedly installed");
        }
        catch (InvalidOperationException) { }
        Check(before.SequenceEqual(File.ReadAllBytes(paths.ZeroInit)), "refusal changed source bytes");
    }
    Console.WriteLine("Adaptive profiler update: original execution, scheduler, early-return/error, lifecycle, last-slot semantics, idempotence and narrow refusal PASS");
}
finally
{
    Directory.Delete(root, true);
}
