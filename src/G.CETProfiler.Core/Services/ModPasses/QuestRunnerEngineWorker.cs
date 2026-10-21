using System.Text.RegularExpressions;
namespace GCETRuntimeProfiler.Core.Services;
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyQuestRunnerEngineWorker(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua", "pass5ManagerIdleAccumulator",
            "pass5SpawnerAccumulator", "Manager:update(dt)", "Spawner.Update(elapsed)");
        const string marker="-- G-CET semantic:questrunner-engine-worker-v1";
        if(file.Text.Contains(marker,StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("QuestRunner PASS5 0-Engine worker already installed.");
        var crlf=file.Text.Contains("\r\n",StringComparison.Ordinal);
        var source=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        var declaration=new Regex(@"(?m)^[ \t]*local pass5ManagerIdleAccumulator = 0\.0\n[ \t]*local pass5SpawnerAccumulator = 0\.0\n",RegexOptions.CultureInvariant);
        var updates=new Regex(@"(?ms)^[ \t]*-- PASS5 R10: active quests preserve rendered-frame updates\..*?^[ \t]*Spawner\.Update\(elapsed\)\n[ \t]*end\n(?=\s*if Runner\.debounce then return end)",RegexOptions.CultureInvariant);
        if(declaration.Matches(source).Count!=1 || updates.Matches(source).Count!=1)
            throw new InvalidOperationException("QuestRunner PASS5 cadence source no longer uniquely provable.");
        var block=updates.Match(source).Value;
        foreach(var required in new[]{"if Manager.current then","Manager:update(dt)",
            "Manager:update(elapsed)","pass5ManagerIdleAccumulator >= 0.20",
            "pass5SpawnerAccumulator >= 0.25","Spawner.Update(elapsed)"})
            if(!block.Contains(required,StringComparison.Ordinal))
                throw new InvalidOperationException("QuestRunner PASS5 cadence semantics changed: "+required);
        // Only the original two cadence accumulators migrate. Original update,
        // Cron, UI, quest state, debounce and contact lifecycle remain in mod.
        source=declaration.Replace(source,
            "local __gcetQuestCadence = require(\"GCET_Semantic/questrunner_engine_worker\")\n",1);
        source=updates.Replace(source,
            "\t"+marker+"\n\t__gcetQuestCadence.update(dt, Manager, Spawner)\n\n",1);
        context.Write(file,crlf?source.Replace("\n","\r\n",StringComparison.Ordinal):source);
        context.WriteCoordinator("questrunner_engine_worker.lua", """
-- G-CET semantic coordinator:questrunner-engine-worker
-- Cadence adapter invoked by the existing 0-Engine-owned onUpdate lane.
-- It owns no timer, callback registration or gameplay state machine.
local M = {}
local managerIdleElapsed = 0.0
local spawnerElapsed = 0.0
function M.update(dt, manager, spawner)
  local step = tonumber(dt) or 0.0
  if manager.current then
    managerIdleElapsed = 0.0
    manager:update(dt)
  else
    managerIdleElapsed = managerIdleElapsed + step
    if managerIdleElapsed >= 0.20 then
      local elapsed = managerIdleElapsed
      managerIdleElapsed = 0.0
      manager:update(elapsed)
    end
  end
  spawnerElapsed = spawnerElapsed + step
  if spawnerElapsed >= 0.25 then
    local elapsed = spawnerElapsed
    spawnerElapsed = 0.0
    spawner.Update(elapsed)
  end
end
return M
""");
        return SemanticInjectionResult.Success(
            "QuestRunner PASS5 cadence moved to Lua worker; source event ownership, Manager active frame updates, idle 5Hz, Spawner 4Hz and accumulated delta unchanged.");
    }
}
