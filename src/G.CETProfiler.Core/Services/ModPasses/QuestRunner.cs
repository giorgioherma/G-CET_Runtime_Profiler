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
        const string workerMarker="-- G-CET semantic:questrunner-engine-worker-v1";
        if(liveFile.Text.Contains(workerMarker,StringComparison.Ordinal))
        {
            if(!liveFile.Text.Contains(
                    "require(\"GCET_Semantic/questrunner_engine_worker\")",
                    StringComparison.Ordinal) ||
               !liveFile.Text.Contains(
                    "__gcetQuestCadence.update(dt, Manager, Spawner)",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "QuestRunner extracted 0-Engine cadence bridge incomplete.");
            return SemanticInjectionResult.Skip(
                "QuestRunner completed extracted 0-Engine worker already installed.");
        }
        if (liveFile.Text.Contains("pass5ManagerIdleAccumulator", StringComparison.Ordinal))
        {
            if (!liveFile.Text.Contains("pass5SpawnerAccumulator", StringComparison.Ordinal)
                || !liveFile.Text.Contains("if Manager.current then", StringComparison.Ordinal)
                || !liveFile.Text.Contains("Manager:update(dt)", StringComparison.Ordinal))
                throw new InvalidOperationException("QuestRunner PASS5 active/idle stage incomplete.");
            return SemanticInjectionResult.Skip("QuestRunner deployed PASS5 active/idle stages preserved.");
        }

        var file = context.FindFile(
            "init.lua",
            "Manager:update(dt)",
            "Spawner.Update(dt)",
            "Cron.Update(dt)",
            "Runner.gameState");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetQuestRunnerManagerElapsed = 0.0\n" +
            "local __gcetQuestRunnerSpawnerElapsed = 0.0\n\n" +
            opening,
            "QuestRunner cadence declarations");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Manager:update\(dt\)\s*$",
            "${indent}if Manager.current then\n" +
            "${indent}\t__gcetQuestRunnerManagerElapsed = 0.0\n" +
            "${indent}\tManager:update(dt)\n" +
            "${indent}else\n" +
            "${indent}\t__gcetQuestRunnerManagerElapsed = __gcetQuestRunnerManagerElapsed + (tonumber(dt) or 0)\n" +
            "${indent}\tif __gcetQuestRunnerManagerElapsed >= 0.20 then\n" +
            "${indent}\t\tlocal __gcetElapsed = __gcetQuestRunnerManagerElapsed\n" +
            "${indent}\t\t__gcetQuestRunnerManagerElapsed = 0.0\n" +
            "${indent}\t\tManager:update(__gcetElapsed)\n" +
            "${indent}\tend\n" +
            "${indent}end",
            "QuestRunner active/idle Manager lane");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Spawner\.Update\(dt\)\s*$",
            "${indent}__gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed + (tonumber(dt) or 0)\n" +
            "${indent}if __gcetQuestRunnerSpawnerElapsed >= 0.25 then\n" +
            "${indent}\tlocal __gcetElapsed = __gcetQuestRunnerSpawnerElapsed\n" +
            "${indent}\t__gcetQuestRunnerSpawnerElapsed = 0.0\n" +
            "${indent}\tSpawner.Update(__gcetElapsed)\n" +
            "${indent}end",
            "QuestRunner Spawner feed lane");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved active QuestRunner Manager frame cadence, moved idle Manager refresh to 5 Hz, and fed Spawner at 4 Hz with accumulated delta.");
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
