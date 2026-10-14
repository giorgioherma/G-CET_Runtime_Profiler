using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySimpleNotepadCet(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function saveWindowState",
            "Buttons.Draw3",
            "WindowHiderTool",
            "state.open");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onDraw['""]\s*,\s*function\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*local\s+WindowHiderTool\s*=\s*GetMod\s*\(\s*['""]WindowHiderTool['""]\s*\)\s*\r?\n" +
            @"[ \t]*if\s+WindowHiderTool\s+and\s+state\.open\s+then\s*\r?\n" +
            @"[ \t]*Buttons\.DrawWindowHider\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*elseif\s+not\s+WindowHiderTool\s+then\s*\r?\n" +
            @"[ \t]*windowhidden\s*=\s*false\s*\r?\n" +
            @"[ \t]*saveWindowState\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*end\s*\r?\n" +
            @"[ \t]*Buttons\.Draw3\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*Buttons\.Draw2\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*Buttons\.Draw\s*\(\s*\)\s*\r?\n" +
            @"[ \t]*end\s*\)\s*;?\s*$",
            "${indent}${registrar}(\"onDraw\", function()\n" +
            "${indent}    if not state.open then return end\n" +
            "${indent}    local WindowHiderTool = GetMod(\"WindowHiderTool\")\n" +
            "${indent}    if WindowHiderTool then\n" +
            "${indent}        Buttons.DrawWindowHider()\n" +
            "${indent}    elseif not WindowHiderTool and windowhidden then\n" +
            "${indent}        windowhidden = false\n" +
            "${indent}        saveWindowState()\n" +
            "${indent}    end\n" +
            "${indent}    Buttons.Draw3()\n" +
            "${indent}    Buttons.Draw2()\n" +
            "${indent}    Buttons.Draw()\n" +
            "${indent}end)",
            "Simple Notepad closed-overlay draw gate");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Made Simple Notepad onDraw hard-dormant while CET is closed and persisted WindowHiderTool absence only when the hidden state actually changes.");
    }

}
