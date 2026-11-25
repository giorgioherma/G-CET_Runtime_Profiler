using System.Diagnostics;
using System.Text;
using GCETRuntimeProfiler.Core.Services;

internal static class NativeLocationShutdownContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Native location shutdown: " + message);
    }

    internal static void CheckStock(byte[] source)
    {
        var generated = FixedZeroEngineRuntime.AddNativeLocationBridge(source);
        var text = Encoding.UTF8.GetString(generated);
        Check(text.Contains("__gcetNativeShutdown.Register(", StringComparison.Ordinal) &&
            text.Contains("Storage.Save()", StringComparison.Ordinal), "private stock author shutdown not composed");
        Check(generated.SequenceEqual(FixedZeroEngineRuntime.AddNativeLocationBridge(generated)),
            "private stock generation not idempotent");
    }

    internal static void Run(string root)
    {
        const string stock = """
            local Engine = {}
            function Engine.Register() return {} end
            local Storage = {Save = function() record("save") end}
            -- registerForEvent("onShutdown", function() error("comment") end)
            local ignored = [=[registerForEvent("onShutdown", function() end)]=]
            registerForEvent("onShutdown", function(mode)
                Storage.Save()
                if mode == "early" then return end
                if mode == "author-error" or mode == "both-error" then error("author failure", 0) end
                record("after-save")
            end)
            return Engine
            """;
        const string legacyBlock = """
            -- G-CET native static location host v1
            do
                local source = require("modules/GCETNativeLocationHost")
                Engine.GCET = {NativeLocationHost = source.new(Engine)}
            end
            registerForEvent("onShutdown",function()
                if type(Engine)=="table" and type(Engine.GCET)=="table" and
                    type(Engine.GCET.NativeLocationHost)=="table" then
                    Engine.GCET.NativeLocationHost:shutdown()
                end
            end)
            return Engine
            """;
        void Execute(string label, string source, string scenario, int expectedRegistrations)
        {
            var generated = FixedZeroEngineRuntime.AddNativeLocationBridge(Encoding.UTF8.GetBytes(source));
            Check(generated.SequenceEqual(FixedZeroEngineRuntime.AddNativeLocationBridge(generated)),
                label + ": repeat changed output");
            var path = Path.Combine(root, "native-shutdown.lua");
            File.WriteAllBytes(path, generated);
            var info = new ProcessStartInfo("lua5.4") { UseShellExecute = false };
            info.ArgumentList.Add("tests/AdaptiveProfilerUpdateContract/ExecuteNativeShutdown.lua");
            info.ArgumentList.Add(path);
            info.ArgumentList.Add(scenario);
            info.ArgumentList.Add(expectedRegistrations.ToString());
            using var process = Process.Start(info)!;
            process.WaitForExit();
            Check(process.ExitCode == 0, label + ": generated execution failed");
        }
        Execute("stock", stock, "stock", 1);
        Execute("historical generated bridge", stock.Replace("return Engine", legacyBlock), "stock", 1);
        Execute("multiple author registrations", stock.Replace("return Engine",
            "registerForEvent('onShutdown', function() record('last') end)\nreturn Engine"), "last", 2);
        Execute("named callback / custom formatting", stock.Replace("registerForEvent(\"onShutdown\", function(mode)",
            "local function save(mode)").Replace("end)\nreturn Engine", "end\nregisterForEvent -- comment\n('onShutdown', save)\nreturn Engine"), "stock", 1);
        Execute("host without original shutdown", "local Engine={}\nfunction Engine.Register() end\nreturn Engine", "empty", 1);
        Execute("conditional registration", "local Engine={}\nfunction Engine.Register() end\nif false then registerForEvent('onShutdown', function() error('not registered') end) end\nreturn Engine", "empty", 1);
        Execute("mixed historical newlines", stock.Replace("\n", "\r\r\n"), "stock", 1);
        var ambiguous = Encoding.UTF8.GetBytes("local Engine={}\nlocal register=registerForEvent\nregister('onShutdown', function() end)\nreturn Engine");
        Check(ambiguous.SequenceEqual(FixedZeroEngineRuntime.AddNativeLocationBridge(ambiguous)),
            "ambiguous alias must not gain a competing cleanup");
        Console.WriteLine("Native location shutdown: author order, errors, old overlay, named/custom host, last-slot and narrow refusal PASS");
    }
}
