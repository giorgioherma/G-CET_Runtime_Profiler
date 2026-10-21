namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRicaEngineWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "TryAttachRicaPass4 = function()");
        const string marker="-- G-CET semantic:rica-engine-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("RICA six-lane 0-Engine worker already attached.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=crlf?file.Text.Replace("\r\n","\n",StringComparison.Ordinal):file.Text;
        // Bound the substitution to the original atomic attach transaction.
        // Leave outer retry, in-progress guard, rollback and logging untouched.
        const string begin="    local created = {}\n    local okAttach = pcall(function()\n";
        const string finish="        pass4Attached = true\n    end)\n";
        var pos=source.IndexOf("TryAttachRicaPass4 = function()",StringComparison.Ordinal);
        var start=source.IndexOf(begin,pos,StringComparison.Ordinal);
        var end=start<0?-1:source.IndexOf(finish,start,StringComparison.Ordinal);
        if(start<0 || end<0 ||
            source.IndexOf(begin,start+begin.Length,StringComparison.Ordinal)>=0 ||
            source.IndexOf(finish,end+finish.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("RICA atomic PASS4 attachment is not uniquely provable.");
        var original=source.Substring(start,end+finish.Length-start);
        var ids=new[]{"rica.diagnostics_worker","rica.rewards","rica.state","rica.mappins","rica.diagnostics","rica.system"};
        if(ids.Any(id=>original.Split("id = \""+id+"\"",StringSplitOptions.None).Length!=2) ||
           !original.Contains("client.Schedule.EveryFrame({",StringComparison.Ordinal) ||
           original.Split("client.Schedule.Every(",StringSplitOptions.None).Length!=6 ||
           !original.Contains("pass4DiagnosticsWorkerJob = diagnosticsWorkerJob",StringComparison.Ordinal))
            throw new InvalidOperationException("RICA PASS4 scheduler shape or jobs changed.");
        const string bridge= """
    -- G-CET semantic:rica-engine-worker-v1
    local created = {}
    local okAttach = pcall(function()
        local worker = require("GCET_Semantic/rica_engine_worker")
        pass4DiagnosticsWorkerJob = worker.attach(created, {
            diagnosticsWorkerTick = diagnosticsWorkerTick,
            rewardTick = rewardTick,
            runtimeStateTick = runtimeStateTick,
            mappinTick = mappinTick,
            scheduledDiagnosticsTick = scheduledDiagnosticsTick,
            schedulerTick = schedulerTick
        })
        pass4Attached = true
    end)
""";
        var revised=source[..start]+bridge+"\n"+source[(end+finish.Length)..];
        context.Write(file,crlf?revised.Replace("\n","\r\n",StringComparison.Ordinal):revised);
        context.WriteCoordinator("rica_engine_worker.lua", """
-- G-CET semantic coordinator:rica-engine-worker
-- This worker registers jobs with 0-Engine; it owns no clock or quest state.
local M = {}
function M.attach(created, callbacks)
    local okEngine, engine = pcall(GetMod, "0-Engine")
    if not okEngine or type(engine) ~= "table" or type(engine.Register) ~= "function" then
        error("0-Engine Register API not ready")
    end
    local client = engine.Register("RICA")
    if type(client) ~= "table" or type(client.Schedule) ~= "table"
        or type(client.Schedule.Every) ~= "function"
        or type(client.Schedule.EveryFrame) ~= "function" then
        error("0-Engine Scheduler API not ready")
    end
    local function add(handle)
        created[#created + 1] = handle
        return handle
    end
    local diagnosticsWorkerJob = add(client.Schedule.EveryFrame({
        id = "rica.diagnostics_worker", active = false, pause = "never"
    }, callbacks.diagnosticsWorkerTick))
    add(client.Schedule.Every(0.25, {
        id = "rica.rewards", pause = "never", clock = "game", catchUp = false
    }, function() callbacks.rewardTick(4, 1) end))
    add(client.Schedule.Every(1.0, {
        id = "rica.state", pause = "never", clock = "game", catchUp = false
    }, callbacks.runtimeStateTick))
    add(client.Schedule.Every(1.0, {
        id = "rica.mappins", pause = "never", clock = "game", catchUp = false
    }, callbacks.mappinTick))
    add(client.Schedule.Every(5.0, {
        id = "rica.diagnostics", pause = "never", clock = "game", catchUp = false
    }, callbacks.scheduledDiagnosticsTick))
    add(client.Schedule.Every(10.0, {
        id = "rica.system", pause = "never", clock = "game", catchUp = false
    }, callbacks.schedulerTick))
    return diagnosticsWorkerJob
end
return M
""");
        return SemanticInjectionResult.Success(
            "Moved six unchanged 0-Engine PASS4 jobs into a generated Lua worker; original retry, atomic cancellation, scheduler ownership and reward thresholds retained.");
    }
}
