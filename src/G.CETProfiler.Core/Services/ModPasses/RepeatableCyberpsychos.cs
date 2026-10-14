using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyRepeatableCyberpsychos(
        SemanticPatchContext context)
    {
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
