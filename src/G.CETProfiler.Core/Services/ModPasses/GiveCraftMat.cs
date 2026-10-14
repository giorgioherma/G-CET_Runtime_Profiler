using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGiveCraftMat(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function SaveWindowState",
            "function DrawButtons",
            "WindowHiderTool",
            "cetopen");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onDraw['""]\s*,\s*function\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*DrawButtons\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*local\s+WindowHiderTool\s*=\s*GetMod\s*\(\s*['""]WindowHiderTool['""]\s*\)\s*\r?\n" +
            @"[ \t]*if\s+WindowHiderTool\s+and\s+cetopen\s+then\s*\r?\n" +
            @"[ \t]*DrawWindowHider\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*elseif\s+not\s+WindowHiderTool\s+then\s*\r?\n" +
            @"[ \t]*windowstate\.Current\.mywindowhidden\s*=\s*false\s*\r?\n" +
            @"[ \t]*SaveWindowState\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*end\s*\r?\n" +
            @"[ \t]*end\s*\)\s*;?\s*$",
            "${indent}${registrar}(\"onDraw\", function()\n" +
            "${indent}    if not cetopen then return end\n" +
            "${indent}    DrawButtons()\n" +
            "${indent}    local WindowHiderTool = GetMod(\"WindowHiderTool\")\n" +
            "${indent}    if WindowHiderTool then\n" +
            "${indent}        DrawWindowHider()\n" +
            "${indent}    elseif not WindowHiderTool and windowstate.Current.mywindowhidden then\n" +
            "${indent}        windowstate.Current.mywindowhidden = false\n" +
            "${indent}        SaveWindowState()\n" +
            "${indent}    end\n" +
            "${indent}end)",
            "GiveCraftMat closed-overlay draw gate");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Made GiveCraftMat onDraw hard-dormant while CET is closed and changed WindowHiderTool absence persistence from every-frame disk writes to the actual hidden-to-visible transition.");
    }

}
