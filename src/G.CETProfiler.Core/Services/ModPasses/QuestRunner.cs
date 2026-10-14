using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyQuestRunner(
        SemanticPatchContext context)
    {
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

}
