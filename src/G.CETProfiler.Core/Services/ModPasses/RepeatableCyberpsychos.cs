using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRepeatableCyberpsychos(
        SemanticPatchContext context)
    {
        // Repair PASS4's current live bootstrap on regeneration. Retain all lanes.
        // Pristine inputs continue through the earlier source transformation below.
        var liveFile = context.FindFile("init.lua", "Mappins.sync", "Diagnostics.snapshot");
        const string handoff = "    TryAttachRepeatablePass4()\n\n    Mod.tickElapsed = Mod.tickElapsed + delta";
        const string fixedHandoff = "    TryAttachRepeatablePass4()\n    if pass4Attached then return end\n\n    Mod.tickElapsed = Mod.tickElapsed + delta";
        if (liveFile.Text.Contains("TryAttachRepeatablePass4()", StringComparison.Ordinal))
        {
            if (liveFile.Text.Contains(fixedHandoff, StringComparison.Ordinal))
                return SemanticInjectionResult.Skip("PASS4 bootstrap handoff already repaired.");
            if (!liveFile.Text.Contains("if pass4Attached then return end", StringComparison.Ordinal))
                throw new InvalidOperationException("Cyberpsychos PASS4 bootstrap source drifted.");
            var output = ReplaceOnce(liveFile.Text, handoff, fixedHandoff,
                "Cyberpsychos PASS4 same-frame native handoff");
            context.Write(liveFile, output);
            return SemanticInjectionResult.Success(
                "Stops fallback accumulators after successful same-frame PASS4 scheduler attachment.");
        }

        var file = context.FindFile(
            "init.lua",
            "local function runtimeTick()",
            "Mappins.sync",
            "Diagnostics.snapshot");

        var text = ReplaceOnce(
            file.Text,
            "local function runtimeTick()\n",
            "local function runtimeTick(includePresentation)\n",
            "Repeatable Cyberpsychos runtimeTick signature");

        var presentation =
            "    Mappins.sync(system, Sites)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n";

        var presentationGated =
            "    if includePresentation then\n" +
            "        Mappins.sync(system, Sites)\n" +
            "        Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            presentation,
            presentationGated,
            "Repeatable Cyberpsychos presentation lane");

        var oldUpdate =
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    if Mod.tickElapsed >= 1.0 then\n" +
            "        Mod.tickElapsed = 0.0\n" +
            "        local ok, err = pcall(runtimeTick)\n";

        var newUpdate =
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    Mod.presentationElapsed = (Mod.presentationElapsed or 5.0) + delta\n" +
            "    if Mod.tickElapsed >= 1.0 then\n" +
            "        Mod.tickElapsed = 0.0\n" +
            "        local includePresentation = Mod.presentationElapsed >= 5.0\n" +
            "        if includePresentation then Mod.presentationElapsed = 0.0 end\n" +
            "        local ok, err = pcall(runtimeTick, includePresentation)\n";

        text = ReplaceOnce(
            text,
            oldUpdate,
            newUpdate,
            "Repeatable Cyberpsychos runtime/presentation cadence");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved 1 Hz core runtime and 10 s scheduler; moved mappin/diagnostic presentation work to a 5 s lane.");
    }

}
