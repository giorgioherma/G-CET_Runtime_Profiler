using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAlternativeMidairMovement(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "input:SetInputData(action)",
            "OnLocomotionStateChanged",
            "SetDetailedLocomotionStates",
            "MidairMovementProcessor");

        var input = context.FindFile(
            "input.lua",
            "function Input:SetInputData",
            "MoveX",
            "MoveY",
            "Vector4.ToRotation");

        var inputText = RegexReplaceOnce(
            input.Text,
            @"(?m)^function\s+Input:SetInputData\s*\(\s*action\s*\)\s*$",
            "function Input:SetInputData(action, __gcetRoutedName)",
            "Alternative Midair Movement input signature");

        inputText = RegexReplaceOnce(
            inputText,
            @"(?m)^(?<indent>\s*)local\s+actionName\s*=\s*Game\.NameToString\s*\(\s*action:GetName\s*\(\s*(?:action\s*)?\)\s*\)\s*$",
            "${indent}local actionName = __gcetRoutedName or Game.NameToString(action:GetName())",
            "Alternative Midair Movement routed action name");

        inputText = RegexReplaceOnce(
            inputText,
            @"(?m)^(?<indent>\s*)self\.analogRotation\s*=\s*Vector4\.ToRotation\s*\(\s*Vector4\.new\s*\(\s*self\.analogX\s*,\s*self\.analogY\s*,\s*0\s*,\s*1\s*\)\s*\)\s*$",
            "${indent}if actionName == \"MoveX\" or actionName == \"MoveY\" then\n" +
            "${indent}    self.analogRotation = Vector4.ToRotation(Vector4.new(self.analogX, self.analogY, 0, 1))\n" +
            "${indent}end",
            "Alternative Midair Movement analog rotation gate");
        context.Write(input, inputText);

        var initText = RegexReplaceOnce(
            init.Text,
            @"(?m)^(?<opening>\s*function\s+AltJump:new\s*\(\s*\)\s*)$",
            "local __gcetMidairWakeTail = 0.0\n" +
            "local __gcetMidairIdleElapsed = 0.0\n\n" +
            "${opening}",
            "Alternative Midair Movement semantic state");

        initText = RegexReplaceOnce(
            initText,
            @"(?ms)^(?<indent>[ \t]*)(?<observer>(?:Event\.)?Observe)\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*_\s*,\s*action\s*\)\s*\r?\n\s*input:SetInputData\s*\(\s*action\s*\)\s*\r?\n\s*end\s*\)\s*$",
            "${indent}local __gcetMidairActionSet = {\n" +
            "${indent}    MoveX = true, MoveY = true, Jump = true,\n" +
            "${indent}    Left = true, Right = true, Forward = true, Back = true\n" +
            "${indent}}\n" +
            "${indent}local function __gcetMidairOnAction(_, action, __gcetConsumer, __gcetRoutedName)\n" +
            "${indent}    local __gcetActionName = __gcetRoutedName or Game.NameToString(action:GetName())\n" +
            "${indent}    if not __gcetMidairActionSet[__gcetActionName] then return end\n" +
            "${indent}    input:SetInputData(action, __gcetActionName)\n" +
            "${indent}    if __gcetActionName == \"Jump\" then\n" +
            "${indent}        __gcetMidairWakeTail = math.max(__gcetMidairWakeTail, 0.90)\n" +
            "${indent}    end\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetMidairRouted = false\n" +
            "${indent}local __gcetMidairHandles = {}\n" +
            "${indent}local __gcetMidairOk, __gcetMidairEngine = pcall(GetMod, \"0-Engine\")\n" +
            "${indent}local __gcetMidairApi = __gcetMidairEngine\n" +
            "${indent}if __gcetMidairOk and type(__gcetMidairEngine) == \"table\" and type(__gcetMidairEngine.GCET) == \"table\" then __gcetMidairApi = __gcetMidairEngine.GCET end\n" +
            "${indent}if __gcetMidairOk and type(__gcetMidairApi) == \"table\" and type(__gcetMidairApi.SubscribeAction) == \"function\" then\n" +
            "${indent}    __gcetMidairRouted = pcall(function()\n" +
            "${indent}        __gcetMidairHandles[#__gcetMidairHandles + 1] = __gcetMidairApi.SubscribeAction({\n" +
            "${indent}            id = \"G-CET.Semantic.AlternativeMidairMovement\",\n" +
            "${indent}            actions = { \"MoveX\", \"MoveY\", \"Jump\", \"Left\", \"Right\", \"Forward\", \"Back\" },\n" +
            "${indent}            decodeType = false\n" +
            "${indent}        }, __gcetMidairOnAction, \"Alternative Midair Movement\")\n" +
            "${indent}    end)\n" +
            "${indent}    if not __gcetMidairRouted then\n" +
            "${indent}        for _, __gcetHandle in ipairs(__gcetMidairHandles) do\n" +
            "${indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end\n" +
            "${indent}        end\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if not __gcetMidairRouted then Observe(\"PlayerPuppet\", \"OnAction\", __gcetMidairOnAction) end",
            "Alternative Midair Movement exact action routing");

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^(?<indent>\s*)loc:SetLocomotionStates\s*\(\s*player\.object\s*\)\s*$",
            "${indent}local __gcetTail = 0.90\n" +
            "${indent}if config and config.lowersprintaccel and config.lowersprintaccel.enabled then\n" +
            "${indent}    __gcetTail = math.max(__gcetTail, (tonumber(config.lowersprintaccel.duration) or 0.0) + 0.10)\n" +
            "${indent}end\n" +
            "${indent}__gcetMidairWakeTail = math.max(__gcetMidairWakeTail, __gcetTail)\n" +
            "${indent}loc:SetLocomotionStates(player.object)",
            "Alternative Midair Movement locomotion wake");

        var midairOpening = FindOnUpdateOpening(initText, "delta");
        var midairStart = midairOpening + "        if AltJump.loaded then\n";
        var midairGate =
            midairOpening +
            "        if AltJump.loaded then\n" +
            "            local __gcetFrameDelta = math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "            if __gcetMidairWakeTail > 0.0 then\n" +
            "                __gcetMidairWakeTail = math.max(0.0, __gcetMidairWakeTail - __gcetFrameDelta)\n" +
            "            end\n" +
            "            local __gcetSimpleState = loc and loc.currentState or nil\n" +
            "            local __gcetRealtime = __gcetMidairWakeTail > 0.0\n" +
            "                or AltJump.inFlight == true\n" +
            "                or (loc and loc.isWallbouncing == true)\n" +
            "                or (player and player.canWallbounce == true)\n" +
            "                or __gcetSimpleState == gamePSMLocomotionStates.Jump\n" +
            "                or __gcetSimpleState == gamePSMLocomotionStates.Kereznikov\n" +
            "            if __gcetRealtime then\n" +
            "                __gcetMidairIdleElapsed = 0.0\n" +
            "            else\n" +
            "                __gcetMidairIdleElapsed = __gcetMidairIdleElapsed + __gcetFrameDelta\n" +
            "                if __gcetMidairIdleElapsed < 0.10 then return end\n" +
            "                delta = __gcetMidairIdleElapsed\n" +
            "                __gcetMidairIdleElapsed = 0.0\n" +
            "            end\n";
        initText = ReplaceOnce(
            initText,
            midairStart,
            midairGate,
            "Alternative Midair Movement active/idle update gate");
        context.Write(init, initText);

        return SemanticInjectionResult.Success(
            "Routed only the seven action names consumed by Input:SetInputData, avoided non-axis rotation work, kept airborne/transition-sensitive runtime frame-responsive, and reduced stable-ground maintenance to 10 Hz with accumulated delta.");
    }

}
