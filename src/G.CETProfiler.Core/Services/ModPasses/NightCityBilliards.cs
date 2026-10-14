using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNightCityBilliards(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "ChoiceHub.update()",
            "runtime:update(dt)",
            "session:isActive");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetBilliardsIdleElapsed = 0.0\n\n" + opening,
            "NightCityBilliards onUpdate opening");

        var body =
            "    ChoiceHub.update()\n" +
            "    local runtime = NightCityBilliardsRuntime\n" +
            "    if runtime then runtime:update(dt) end\n";

        var replacement =
            "    ChoiceHub.update()\n" +
            "    local runtime = NightCityBilliardsRuntime\n" +
            "    if not runtime then return end\n" +
            "    local __gcetActive = runtime.session and runtime.session:isActive()\n" +
            "    if __gcetActive then\n" +
            "        __gcetBilliardsIdleElapsed = 0.0\n" +
            "        runtime:update(dt)\n" +
            "        return\n" +
            "    end\n" +
            "    __gcetBilliardsIdleElapsed = __gcetBilliardsIdleElapsed + dt\n" +
            "    if __gcetBilliardsIdleElapsed >= 0.20 then\n" +
            "        runtime:update(__gcetBilliardsIdleElapsed)\n" +
            "        __gcetBilliardsIdleElapsed = 0.0\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            body,
            replacement,
            "NightCityBilliards active/idle update body");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "ChoiceHub remains frame-fed; active billiards session updates every frame, idle table/runtime discovery updates at 5 Hz.");
    }

}
