using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAerialRaceActiveIdleSplit(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Cron.Update(dt)",
            "settings.usePlatforms",
            "checkpointsSpawned",
            "nc1.Update()");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetAerialIdleElapsed = 0.0\n\n" + opening,
            "AerialRace idle accumulator");

        // Keep the optional world-platform feature at the author's frame
        // cadence. It has its own cell-change early return and is useful outside
        // an active race, so race dormancy must not make grapple platforms lag.
        var platformBlock = Regex.Match(
            text,
            @"(?ms)^[ \t]*if\s+settings\.usePlatforms\s+then\s*\r?\n[ \t]*updatePlatforms\s*\(\s*\)\s*\r?\n[ \t]*end\s*$",
            RegexOptions.CultureInvariant);
        if (!platformBlock.Success)
            throw new InvalidOperationException(
                "AerialRace platform lane could not be revalidated.");

        var split =
            platformBlock.Value +
            "\n\n" +
            "    local __gcetAerialBusy = nc1.raceActive == true or nc1.resetTimeTarget ~= nil\n" +
            "    if not __gcetAerialBusy then\n" +
            "        __gcetAerialIdleElapsed = __gcetAerialIdleElapsed + math.max(0.0, dt or 0.0)\n" +
            "        if __gcetAerialIdleElapsed < 0.20 then return end\n" +
            "        __gcetAerialIdleElapsed = 0.0\n" +
            "    else\n" +
            "        __gcetAerialIdleElapsed = 0.0\n" +
            "    end";

        text =
            text[..platformBlock.Index] +
            split +
            text[(platformBlock.Index + platformBlock.Length)..];

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept Cron and optional grapple-platform updates at author frame cadence; active race/reset-grace work remains realtime, while only inactive race checkpoint/discovery polling runs at 5 Hz with at most a 200 ms wake delay.");
    }


}
