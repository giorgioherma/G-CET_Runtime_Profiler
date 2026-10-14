using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAutoAmmoCrafting(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "autoConvertTime = 6",
            "scriptInterval = scriptInterval + deltaTime",
            "function notReady()",
            "function playerInMenu()",
            "settings.combatCheck and inCombat",
            "__gcetGetTransactionSystem");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)player\s*=\s*Game\.GetPlayerSystem\(\):GetLocalPlayerMainGameObject\(\)\s*\r?\n[ \t]*if\s+not\s+ts\s+then\s+ts\s*=\s*__gcetGetTransactionSystem\(\)\s+end\s*\r?\n(?<ready>------------------------------------------------\r?\n-- Ready for take off\.\r?\n------------------------------------------------\r?\n)[ \t]*scriptInterval\s*=\s*scriptInterval\s*\+\s*deltaTime\s*\r?\n[ \t]*if\s+scriptInterval\s*<\s*settings\.autoConvertTime\s+then\s*\r?\n[ \t]*return\s*\r?\n[ \t]*else\s*\r?\n[ \t]*scriptInterval\s*=\s*0\s*\r?\n[ \t]*end\s*$",
            "${ready}${indent}scriptInterval = scriptInterval + deltaTime\n" +
            "${indent}if scriptInterval < settings.autoConvertTime then\n" +
            "${indent}\treturn\n" +
            "${indent}else\n" +
            "${indent}\tscriptInterval = 0\n" +
            "${indent}end\n" +
            "${indent}player = Game.GetPlayerSystem():GetLocalPlayerMainGameObject()\n" +
            "${indent}if not ts then ts = __gcetGetTransactionSystem() end",
            "Auto Ammo defer player/system acquisition until author cadence");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^function\s+notReady\s*\(\s*\)\s*\r?\n.*?^end\s*$",
            "function notReady()\n" +
            "\tinkMenuScenario = GetSingleton('inkMenuScenario'):GetSystemRequestsHandler()\n" +
            "\tif inkMenuScenario:IsGamePaused() or inkMenuScenario:IsPreGame() then\n" +
            "\t\treturn true\n" +
            "\tend\n" +
            "\tlocal __gcetPlayerSystem = Game.GetPlayerSystem()\n" +
            "\tif __gcetPlayerSystem == nil then\n" +
            "\t\treturn true\n" +
            "\tend\n" +
            "\tif __gcetPlayerSystem:GetLocalPlayerMainGameObject() == nil then\n" +
            "\t\treturn true\n" +
            "\tend\n" +
            "\tlocal __gcetPlayer = Game.GetPlayer()\n" +
            "\tif __gcetPlayer == nil or not __gcetPlayer:IsAttached() then\n" +
            "\t\treturn true\n" +
            "\tend\n" +
            "\treturn false\n" +
            "end",
            "Auto Ammo readiness duplicate lookup collapse");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^function\s+playerInMenu\s*\(\s*\)\s*\r?\n[ \t]*blackboard\s*=\s*Game\.GetBlackboardSystem\(\):Get\(Game\.GetAllBlackboardDefs\(\)\.UI_System\);\s*\r?\n[ \t]*uiSystemBB\s*=\s*\(Game\.GetAllBlackboardDefs\(\)\.UI_System\);\s*\r?\n[ \t]*return\s*\(blackboard:GetBool\(uiSystemBB\.IsInMenu\)\);\s*\r?\nend\s*$",
            "function playerInMenu()\n" +
            "\tlocal __gcetUIBB = Game.GetAllBlackboardDefs().UI_System\n" +
            "\tblackboard = Game.GetBlackboardSystem():Get(__gcetUIBB);\n" +
            "\tuiSystemBB = __gcetUIBB;\n" +
            "\treturn(blackboard:GetBool(uiSystemBB.IsInMenu));\n" +
            "end",
            "Auto Ammo UI blackboard definition reuse");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved the original combat/readiness/menu checks and autoConvertTime cadence exactly, but deferred player/transaction acquisition until the existing craft interval fires and collapsed duplicate readiness/UI lookups.");
    }

}
