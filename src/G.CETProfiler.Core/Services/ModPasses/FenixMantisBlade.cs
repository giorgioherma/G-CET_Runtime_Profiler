using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyFenixMantisBlade(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "modules/wallhang.lua",
            "function wallhang.Update",
            "isHangPressed",
            "isStuck",
            "IsHighEnough",
            "MeleeBlock");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)local\s+player\s*=\s*(?<getter>Game\.GetPlayer\s*\(\s*\)|__gcetGetPlayer\s*\(\s*\))\s*\r?\n\s*if\s+not\s+player\s+or\s+not\s+player:IsAttached\s*\(\s*\)\s+then\s+return\s+end\s*\r?\n\s*\r?\n\s*--\s*DYNAMIC RESET:[^\r\n]*\r?\n\s*if\s+not\s+IsHighEnough\s*\(\s*player\s*\)\s+and\s+not\s+isStuck\s+then\s*\r?\n\s*jumpCount\s*=\s*0\s*\r?\n\s*end\s*\r?\n\s*\r?\n\s*--\s*STAGE 1:[^\r\n]*\r?\n\s*if\s+not\s+isHangPressed\s+and\s+not\s+isStuck\s+then\s+return\s+end\s*$",
            "${indent}-- G-CET semantic idle guard: Jump OnAction already resets jumpCount on ground,\n" +
            "${indent}-- so there is no reason to fetch the player and raycast every frame while inactive.\n" +
            "${indent}if not isHangPressed and not isStuck then return end\n\n" +
            "${indent}local player = ${getter}\n" +
            "${indent}if not player or not player:IsAttached() then return end\n\n" +
            "${indent}-- DYNAMIC RESET: If feet are on ground and we aren't currently grabbing, reset jump count.\n" +
            "${indent}if not IsHighEnough(player) and not isStuck then\n" +
            "${indent}    jumpCount = 0\n" +
            "${indent}end",
            "Fenix Mantis Blade inactive wallhang guard");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Hoisted the existing isHangPressed/isStuck guard ahead of player acquisition and IsHighEnough raycasting. Active wall-hang behavior is unchanged, and the existing Jump OnAction ground check still resets jumpCount before a new wall-jump sequence.");
    }




}
