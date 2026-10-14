using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAdvancedSettings(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "ConfigSystem:OnUpdate()",
            "CPS:setThemeBegin()",
            "registerForEvent(\"onOverlayOpen\"",
            "draw = true");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<indent>[ \t]*)ConfigSystem:OnUpdate\s*\(\s*\)\s*$",
            "${opening}\n" +
            "${indent}if not draw then return end\n" +
            "${indent}ConfigSystem:OnUpdate()",
            "Advanced Settings closed update gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>\s*registerForEvent\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<indent>[ \t]*)CPS:setThemeBegin\s*\(\s*\)\s*$",
            "${opening}\n" +
            "${indent}if not draw then return end\n" +
            "${indent}CPS:setThemeBegin()",
            "Advanced Settings closed draw gate");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Skipped option-table scans and CPStyling theme setup while the Advanced Settings overlay is closed; overlay-open behavior and live option editing remain frame-responsive.");
    }


}
