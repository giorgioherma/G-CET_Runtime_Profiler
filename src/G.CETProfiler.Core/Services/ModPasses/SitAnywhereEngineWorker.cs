namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySitAnywhereEngineWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "TryAttachSitAnywherePass4 = function()");
        const string marker="-- G-CET semantic:sitanywhere-engine-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("SitAnywhere native scheduler coordinator already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string start="        local created = {}\n        local okAttach = pcall(function()\n";
        const string finish="            pass4FrameJob = frameJob\n            pass4Attached = true\n        end)\n";
        var owner=s.IndexOf("TryAttachSitAnywherePass4 = function()",StringComparison.Ordinal);
        var a=s.IndexOf(start,owner,StringComparison.Ordinal);
        var b=a<0?-1:s.IndexOf(finish,a,StringComparison.Ordinal);
        if(owner<0||a<0||b<0 || s.IndexOf(start,a+start.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("SitAnywhere PASS4 scheduler transaction source changed.");
        var old=s.Substring(a,b+finish.Length-a);
        foreach(var token in new[]{"engine.Register(\"sitAnywhere\")",
            "client.Schedule.EveryFrame({","id = \"sitanywhere.realtime\"",
            "pause = \"never\"","runSitAnywhereFrame(ctx.dt or 0)",
            "table.insert(created, frameJob)"})
            if(!old.Contains(token,StringComparison.Ordinal))
                throw new InvalidOperationException("SitAnywhere source scheduling semantics changed: "+token);
        if(old.Split("Schedule.EveryFrame(",StringSplitOptions.None).Length!=2 ||
           old.Contains("Schedule.Every(",StringComparison.Ordinal))
            throw new InvalidOperationException("SitAnywhere expected exactly one native frame job.");
        const string bridge="""
        -- G-CET semantic:sitanywhere-engine-worker-v1
        local created = {}
        local okAttach = pcall(function()
            pass4FrameJob = require("GCET_Semantic/sitanywhere_engine_worker").attach(
                created, runSitAnywhereFrame)
            pass4Attached = true
        end)
""";
        s=s[..a]+bridge+"\n"+s[(b+finish.Length)..];
        context.Write(file,crlf?s.Replace("\n","\r\n",StringComparison.Ordinal):s);
        context.WriteCoordinator("sitanywhere_engine_worker.lua", """
-- G-CET semantic coordinator:sitanywhere-engine-worker
-- One PASS4 frame job registered through 0-Engine. No private scheduler.
local M = {}
function M.attach(created, onFrame)
    local ok, engine = pcall(GetMod, "0-Engine")
    if not ok or type(engine) ~= "table" or type(engine.Register) ~= "function" then
        error("0-Engine Register API not ready")
    end
    local client = engine.Register("sitAnywhere")
    if type(client) ~= "table" or type(client.Schedule) ~= "table"
        or type(client.Schedule.EveryFrame) ~= "function" then
        error("0-Engine Scheduler API not ready")
    end
    local job = client.Schedule.EveryFrame({
        id = "sitanywhere.realtime",
        pause = "never"
    }, function(ctx)
        onFrame(ctx.dt or 0)
    end)
    table.insert(created, job)
    return job
end
return M
""");
        return SemanticInjectionResult.Success(
            "SitAnywhere PASS4 0-Engine frame registration moved to source-owned worker; author frame guard, scanner, workspots, fallback and cancellation remain unchanged.");
    }
}
