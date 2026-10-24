using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyQuestRunner(
        SemanticPatchContext context)
    {
        // PASS5 deployed sources already separate active quest, idle Manager,
        // and Spawner feeds. Keep them intact on regeneration.
        var liveFile = context.FindFile("init.lua", "Cron.Update(dt)", "Runner.gameState");
        const string workerMarker = "-- G-CET semantic:questrunner-engine-worker-v1";
        const string workerImport = "local __gcetQuestCadence = require(\"GCET_Semantic/questrunner_engine_worker\")";
        const string workerCall = "__gcetQuestCadence.update(dt, Manager, Spawner)";
        var source = liveFile.Text;

        if (source.Contains(workerMarker, StringComparison.Ordinal))
        {
            // Existing external worker owns the cadence; never inline it.
            var worker = context.TryReadOwnedCoordinator("questrunner_engine_worker.lua");
            if (worker is null ||
                !(worker.StartsWith("-- G-CET semantic coordinator:questrunner\n", StringComparison.Ordinal) ||
                  worker.StartsWith("-- G-CET semantic coordinator:questrunner-engine-worker\n", StringComparison.Ordinal)) ||
                !worker.Contains("managerIdleElapsed >= 0.20", StringComparison.Ordinal) ||
                !worker.Contains("spawnerElapsed >= 0.25", StringComparison.Ordinal) ||
                !source.Contains(workerImport, StringComparison.Ordinal) ||
                !source.Contains(workerCall, StringComparison.Ordinal))
                throw new InvalidOperationException("QuestRunner external worker missing or incompatible.");
            var guarded = source.Contains("ui.update()", StringComparison.Ordinal)
                ? EnsureQuestRunnerUiPendingGuard(source) : source;
            if (source != guarded)
            {
                context.Write(liveFile, guarded);
                return SemanticInjectionResult.Success("QuestRunner extracted worker retained; pending-input UI guard restored.");
            }
            return SemanticInjectionResult.Skip("QuestRunner external worker already installed; existing supported UI state preserved.");
        }

        if (source.Contains("pass5ManagerIdleAccumulator", StringComparison.Ordinal))
        {
            if (!source.Contains("pass5SpawnerAccumulator", StringComparison.Ordinal) ||
                !source.Contains("if Manager.current then", StringComparison.Ordinal))
                throw new InvalidOperationException("QuestRunner legacy PASS5 source incomplete.");
            return SemanticInjectionResult.Skip("QuestRunner historical PASS5 source preserved.");
        }

        var opening = FindOnUpdateOpening(source, "dt");
        if (source.Contains("__gcetQuestRunnerManagerElapsed", StringComparison.Ordinal))
        {
            // Migrate the known old inline-generator form to its original
            // dedicated worker. Do not accept unknown partially rewritten Lua.
            var inline = new Regex(
                @"(?s)[ \t]*if Manager\.current then\s+__gcetQuestRunnerManagerElapsed = 0\.0\s+Manager:update\(dt\)\s+else\s+__gcetQuestRunnerManagerElapsed = __gcetQuestRunnerManagerElapsed \+ \(tonumber\(dt\) or 0\)\s+if __gcetQuestRunnerManagerElapsed >= 0\.20 then\s+local __gcetElapsed = __gcetQuestRunnerManagerElapsed\s+__gcetQuestRunnerManagerElapsed = 0\.0\s+Manager:update\(__gcetElapsed\)\s+end\s+end\s+__gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed \+ \(tonumber\(dt\) or 0\)\s+if __gcetQuestRunnerSpawnerElapsed >= 0\.25 then\s+local __gcetElapsed = __gcetQuestRunnerSpawnerElapsed\s+__gcetQuestRunnerSpawnerElapsed = 0\.0\s+Spawner\.Update\(__gcetElapsed\)\s+end",
                RegexOptions.CultureInvariant);
            if (inline.Matches(source).Count != 1)
                throw new InvalidOperationException("QuestRunner prior inline cadence differs from supported source.");
            source = inline.Replace(source, "\n\t" + workerMarker + "\n\t" + workerCall, 1);
            source = RegexReplaceOnce(source,
                @"(?m)^local __gcetQuestRunnerManagerElapsed = 0\.0\r?\nlocal __gcetQuestRunnerSpawnerElapsed = 0\.0\r?\n",
                "", "QuestRunner obsolete inline state removal");
        }
        else
        {
            context.FindFile("init.lua", "Manager:update(dt)", "Spawner.Update(dt)",
                "Cron.Update(dt)", "Runner.gameState");
            source = RegexReplaceOnce(source,
                @"(?m)^([ \t]*)Manager:update\(dt\)[ \t]*\r?\n[ \t]*Spawner\.Update\(dt\)[ \t]*\r?$",
                "$1" + workerMarker + "\n$1" + workerCall,
                "QuestRunner source worker bridge");
        }

        source = EnsureQuestRunnerUiPendingGuard(source);
        source = ReplaceOnce(source, opening, workerImport + "\n\n" + opening,
            "QuestRunner worker import");

        context.WriteCoordinator("questrunner_engine_worker.lua", """
-- G-CET semantic coordinator:questrunner
-- One cadence owner; the author onUpdate remains the only frame callback.
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
        context.Write(liveFile, source);
        return SemanticInjectionResult.Success(
            "QuestRunner external worker from pristine source, active/idle Manager and Spawner cadence, pending-input-safe UI.");
    }

    private static string EnsureQuestRunnerUiPendingGuard(string source)
    {
        if (Regex.IsMatch(source,
                @"\bif\s+ui\.hubShown\s+or\s+ui\.input\s+then\s+ui\.update\(\)\s+end\b",
                RegexOptions.CultureInvariant))
            return source;
        return RegexReplaceOnce(source,
            @"(?m)^([ \t]*)(?:if\s+ui\.hubShown\s+then\s+ui\.update\(\)\s+end|ui\.update\(\))[ \t]*\r?$",
            "$1if ui.hubShown or ui.input then ui.update() end",
            "QuestRunner hidden and pending-input UI guard");
    }


    private static SemanticInjectionResult ApplyQuestRunnerStaticDebounce(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua", "Runner.debounce", "Cron.After(0.1",
            "registerForEvent(\"onInit\"");
        const string marker = "G-CET semantic:questrunner-debounce-static-closure";
        if (file.Text.Contains(marker, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("QuestRunner single debounce callback already installed.");
        var source = file.Text;
        var anchor = new Regex(
            @"(?m)^(?<indent>[ \t]*)Cron\.After\(0\.1,\s*function\(\)\s*Runner\.debounce\s*=\s*false\s+end\)\s*\r?$",
            RegexOptions.CultureInvariant);
        if (anchor.Matches(source).Count != 1 ||
            source.IndexOf("if Runner.debounce then return end", StringComparison.Ordinal) < 0)
            throw new InvalidOperationException("QuestRunner debounce schedule or callback changed.");
        var registration = new Regex(
            @"(?m)^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]",
            RegexOptions.CultureInvariant);
        var matches = registration.Matches(source);
        if (matches.Count != 1 || matches[0].Index >= anchor.Match(source).Index)
            throw new InvalidOperationException("QuestRunner frame callback not uniquely recognized.");
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var head = "-- " + marker + nl +
            "local function __gcetQuestRunnerReleaseDebounce()" + nl +
            "    Runner.debounce = false" + nl +
            "end" + nl + nl;
        var revised = source.Insert(matches[0].Index, head);
        revised = anchor.Replace(revised, m => m.Groups["indent"].Value +
            "Cron.After(0.1, __gcetQuestRunnerReleaseDebounce)");
        context.Write(file, revised);
        return SemanticInjectionResult.Success(
            "Reuses a single identical Lua debounce callback for every 0.1-second timer; timer scheduling, input wake and active quest callbacks remain untouched.");
    }
}
