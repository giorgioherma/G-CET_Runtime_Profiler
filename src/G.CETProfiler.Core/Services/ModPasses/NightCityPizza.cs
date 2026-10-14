using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNightCityPizza(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "tickBossTexts",
            "Pizza.ctx",
            "clockPhoneAcc",
            "TakeClockIn");

        var opening = FindOnUpdateOpening(file.Text, "delta");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetPizzaMaintenanceElapsed = 0.0\n\n" + opening,
            "NightCityPizza maintenance cadence state");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)tickBossTexts\s*\(\s*delta\s*\).*$",
            "${indent}__gcetPizzaMaintenanceElapsed = __gcetPizzaMaintenanceElapsed + math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "${indent}if __gcetPizzaMaintenanceElapsed < 0.40 then return end\n" +
            "${indent}local __gcetPizzaElapsed = __gcetPizzaMaintenanceElapsed\n" +
            "${indent}__gcetPizzaMaintenanceElapsed = 0.0\n" +
            "${indent}tickBossTexts(__gcetPizzaElapsed)",
            "NightCityPizza global maintenance gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Pizza\.clockPhoneAcc\s*=\s*\(Pizza\.clockPhoneAcc\s+or\s+0\)\s*\+\s*\(delta\s+or\s+0\)\s*$",
            "${indent}Pizza.clockPhoneAcc = 0",
            "NightCityPizza retired frame accumulator");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)if\s+Pizza\.clockPhoneAcc\s*>=\s*0\.4\s+then\s*$",
            "${indent}do",
            "NightCityPizza 0.4 second maintenance body");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Collapsed the global/off-shift maintenance loop to the author's existing 0.4 s phone cadence, feeding accumulated delta to long-period Boss texts while leaving the active delivery ctx:onUpdate state machine untouched.");
    }



}
