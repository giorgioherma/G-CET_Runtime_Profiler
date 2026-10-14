using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyOverclockedJenkinsTendons(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Input.handleAction",
            "self._Ignition.update",
            "self._Swim.update",
            "tickSmokeVisionStrips");

        var helpers = context.FindFile(
            "helpers.lua",
            "function Helpers.isSprinting",
            "function Helpers.isSliding",
            "function Helpers.isInWater",
            "PlayerStateMachine");

        var ignition = context.FindFile(
            "ignition.lua",
            "function Ignition.update(dt)",
            "Helpers.isSprinting()",
            "Helpers.isSliding()",
            "Helpers.isInWater()");

        var swim = context.FindFile(
            "swim.lua",
            "function Swim.update(dt)",
            "local function shouldApply()",
            "return Helpers.isInWater()");

        var initText = RegexReplaceOnce(
            init.Text,
            @"(?m)^(?<opening>\s*function\s+OverclockedJenkinsTendons:New\s*\(\s*\)\s*)$",
            "${opening}\n" +
            "    local __gcetOjtIdleElapsed = 0.0\n" +
            "    local __gcetOjtWakeTail = 0.0",
            "Overclocked Jenkins Tendons semantic state");

        initText = RegexReplaceOnce(
            initText,
            @"(?ms)^(?<indent>[ \t]*)Observe\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*_\s*,\s*action\s*,\s*consumer\s*\)\s*\r?\n\s*if\s+not\s+self\.loaded\s+then\s+return\s+end\s*\r?\n\s*local\s+name\s*=\s*Game\.NameToString\s*\(\s*action:GetName\s*\(\s*\)\s*\)\s*\r?\n\s*local\s+atype\s*=\s*action:GetType\s*\(\s*action\s*\)\.value\s*\r?\n\s*Input\.handleAction\s*\(\s*name\s*,\s*atype\s*\)\s*\r?\n\s*end\s*\)\s*$",
            "${indent}local function __gcetOjtOnAction(_, action, __gcetConsumer, __gcetRoutedName, __gcetRoutedType)\n" +
            "${indent}    if not self.loaded then return end\n" +
            "${indent}    local name = __gcetRoutedName or Game.NameToString(action:GetName())\n" +
            "${indent}    local relevant = name == \"Sprint\" or name == \"ToggleSprint\"\n" +
            "${indent}        or name == \"Dodge\" or name == \"Dodge_Z\" or name == \"DodgeForward\"\n" +
            "${indent}    local wake = relevant or name == \"Crouch\" or name == \"ToggleCrouch\"\n" +
            "${indent}    if not relevant then\n" +
            "${indent}        local lname = string.lower(tostring(name))\n" +
            "${indent}        relevant = lname:find(\"sprint\", 1, true) ~= nil\n" +
            "${indent}            or lname:find(\"dash\", 1, true) ~= nil\n" +
            "${indent}            or lname:find(\"dodge\", 1, true) ~= nil\n" +
            "${indent}        wake = wake or relevant\n" +
            "${indent}    end\n" +
            "${indent}    if wake then __gcetOjtWakeTail = math.max(__gcetOjtWakeTail, 0.35) end\n" +
            "${indent}    if not relevant then return end\n" +
            "${indent}    local atype = __gcetRoutedType or action:GetType(action).value\n" +
            "${indent}    Input.handleAction(name, atype)\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetOjtRouted = false\n" +
            "${indent}local __gcetOjtHandles = {}\n" +
            "${indent}local __gcetOjtOk, __gcetOjtEngine = pcall(GetMod, \"0-Engine\")\n" +
            "${indent}local __gcetOjtApi = __gcetOjtEngine\n" +
            "${indent}if __gcetOjtOk and type(__gcetOjtEngine) == \"table\" and type(__gcetOjtEngine.GCET) == \"table\" then __gcetOjtApi = __gcetOjtEngine.GCET end\n" +
            "${indent}if __gcetOjtOk and type(__gcetOjtApi) == \"table\" and type(__gcetOjtApi.SubscribeAction) == \"function\" then\n" +
            "${indent}    __gcetOjtRouted = pcall(function()\n" +
            "${indent}        __gcetOjtHandles[#__gcetOjtHandles + 1] = __gcetOjtApi.SubscribeAction({\n" +
            "${indent}            id = \"G-CET.Semantic.OverclockedJenkinsTendons\",\n" +
            "${indent}            actions = \"*\",\n" +
            "${indent}            decodeType = false\n" +
            "${indent}        }, __gcetOjtOnAction, \"OverclockedJenkinsTendons\")\n" +
            "${indent}    end)\n" +
            "${indent}    if not __gcetOjtRouted then\n" +
            "${indent}        for _, __gcetHandle in ipairs(__gcetOjtHandles) do\n" +
            "${indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end\n" +
            "${indent}        end\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if not __gcetOjtRouted then Observe(\"PlayerPuppet\", \"OnAction\", __gcetOjtOnAction) end",
            "Overclocked Jenkins Tendons action routing");

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^(?<indent>\s*)if\s+not\s+self\.loaded\s+or\s+not\s+self\._Ignition\s+then\s+return\s+end\s*\r?\n\s*local\s+ok\s*,\s*err\s*=\s*pcall\s*\(\s*self\._Ignition\.update\s*,\s*delta\s*\)\s*$",
            "${indent}if not self.loaded or not self._Ignition then return end\n" +
            "${indent}local __gcetFrameDelta = math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "${indent}if __gcetOjtWakeTail > 0.0 then\n" +
            "${indent}    __gcetOjtWakeTail = math.max(0.0, __gcetOjtWakeTail - __gcetFrameDelta)\n" +
            "${indent}end\n" +
            "${indent}local __gcetState = self._state\n" +
            "${indent}local __gcetRealtime = __gcetOjtWakeTail > 0.0\n" +
            "${indent}    or (__gcetState and (__gcetState.phase == \"IGNITION\"\n" +
            "${indent}        or __gcetState.selfBurning == true\n" +
            "${indent}        or __gcetState.dashedThisCycle == true\n" +
            "${indent}        or __gcetState.speedBoostActive == true\n" +
            "${indent}        or __gcetState.swimBoostActive == true\n" +
            "${indent}        or (__gcetState.finisherPauseTimer or 0) > 0\n" +
            "${indent}        or (__gcetState.smokeVisionStrips and #__gcetState.smokeVisionStrips > 0)\n" +
            "${indent}        or (__gcetState.smokeBlockers and #__gcetState.smokeBlockers > 0)))\n" +
            "${indent}if __gcetRealtime then\n" +
            "${indent}    __gcetOjtIdleElapsed = 0.0\n" +
            "${indent}else\n" +
            "${indent}    __gcetOjtIdleElapsed = __gcetOjtIdleElapsed + __gcetFrameDelta\n" +
            "${indent}    if __gcetOjtIdleElapsed < 0.10 then return end\n" +
            "${indent}    delta = __gcetOjtIdleElapsed\n" +
            "${indent}    __gcetOjtIdleElapsed = 0.0\n" +
            "${indent}end\n" +
            "${indent}local __gcetLocomotion = self._Helpers and self._Helpers.getLocomotionSnapshot\n" +
            "${indent}    and self._Helpers.getLocomotionSnapshot() or nil\n" +
            "${indent}local ok, err = pcall(self._Ignition.update, delta, __gcetLocomotion)",
            "Overclocked Jenkins Tendons active/idle update gate");

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^(?<indent>\s*)pcall\s*\(\s*self\._Swim\.update\s*,\s*delta\s*\)\s*$",
            "${indent}pcall(self._Swim.update, delta, __gcetLocomotion)",
            "Overclocked Jenkins Tendons shared locomotion snapshot to swim");
        context.Write(init, initText);

        var helpersText = RegexReplaceOnce(
            helpers.Text,
            @"(?m)^(?<opening>\s*function\s+Helpers\.isSprinting\s*\(\s*\)\s*)$",
            "function Helpers.getLocomotionSnapshot()\n" +
            "    local snapshot = { sprinting = false, sliding = false, inWater = false }\n" +
            "    if not state.player then return snapshot end\n" +
            "    pcall(function()\n" +
            "        local defsRoot = Game.GetAllBlackboardDefs()\n" +
            "        local defs = defsRoot and defsRoot.PlayerStateMachine or nil\n" +
            "        local bbs = Game.GetBlackboardSystem()\n" +
            "        if not (defs and bbs) then return end\n" +
            "        local bb = bbs:GetLocalInstanced(state.player:GetEntityID(), defs)\n" +
            "        if not bb then return end\n" +
            "        if defs.LocomotionDetailed then\n" +
            "            local detailed = bb:GetInt(defs.LocomotionDetailed)\n" +
            "            snapshot.sprinting = detailed == EnumInt(gamePSMDetailedLocomotionStates.Sprint)\n" +
            "            snapshot.sliding = detailed == EnumInt(gamePSMDetailedLocomotionStates.Slide)\n" +
            "        end\n" +
            "        if defs.Swimming then\n" +
            "            local swimState = bb:GetInt(defs.Swimming) or 0\n" +
            "            snapshot.inWater = swimState == 1 or swimState == 2 or swimState == 3\n" +
            "        end\n" +
            "    end)\n" +
            "    return snapshot\n" +
            "end\n\n" +
            "${opening}",
            "Overclocked Jenkins Tendons locomotion snapshot helper");
        context.Write(helpers, helpersText);

        var ignitionText = RegexReplaceOnce(
            ignition.Text,
            @"(?m)^(?<indent>\s*)function\s+Ignition\.update\s*\(\s*dt\s*\)\s*$",
            "${indent}function Ignition.update(dt, locomotion)\n" +
            "${indent}    locomotion = locomotion or Helpers.getLocomotionSnapshot()",
            "Overclocked Jenkins Tendons ignition snapshot parameter");
        ignitionText = RegexReplaceOnce(
            ignitionText,
            @"(?m)^(?<indent>\s*)local\s+sprinting\s*=\s*Helpers\.isSprinting\s*\(\s*\)\s*$",
            "${indent}local sprinting = locomotion.sprinting == true",
            "Overclocked Jenkins Tendons sprint snapshot");
        ignitionText = RegexReplaceOnce(
            ignitionText,
            @"(?m)^(?<indent>\s*)local\s+sliding\s*=\s*Helpers\.isSliding\s*\(\s*\)\s*$",
            "${indent}local sliding = locomotion.sliding == true",
            "Overclocked Jenkins Tendons slide snapshot");
        ignitionText = RegexReplaceOnce(
            ignitionText,
            @"(?m)^(?<indent>\s*)if\s+cfg\.enabled\s+and\s+Helpers\.isSliding\s*\(\s*\)\s+and\s+playerIsBurningAny\s*\(\s*\)\s+and\s+not\s+state\.slideEntryActive\s+then\s*$",
            "${indent}if cfg.enabled and sliding and playerIsBurningAny() and not state.slideEntryActive then",
            "Overclocked Jenkins Tendons repeated slide read");
        ignitionText = RegexReplaceOnce(
            ignitionText,
            @"(?m)^(?<indent>\s*)elseif\s+cfg\.enabled\s+and\s+Helpers\.isInWater\s*\(\s*\)\s+and\s+playerIsBurningAny\s*\(\s*\)\s+then\s*$",
            "${indent}elseif cfg.enabled and locomotion.inWater == true and playerIsBurningAny() then",
            "Overclocked Jenkins Tendons water snapshot");
        context.Write(ignition, ignitionText);

        var swimText = RegexReplaceOnce(
            swim.Text,
            @"(?m)^(?<indent>\s*)local\s+function\s+shouldApply\s*\(\s*\)\s*$",
            "${indent}local function shouldApply(locomotion)",
            "Overclocked Jenkins Tendons swim predicate signature");
        swimText = RegexReplaceOnce(
            swimText,
            @"(?m)^(?<indent>\s*)return\s+Helpers\.isInWater\s*\(\s*\)\s*$",
            "${indent}if locomotion ~= nil then return locomotion.inWater == true end\n" +
            "${indent}return Helpers.isInWater()",
            "Overclocked Jenkins Tendons swim snapshot");
        swimText = RegexReplaceOnce(
            swimText,
            @"(?m)^(?<indent>\s*)function\s+Swim\.update\s*\(\s*dt\s*\)\s*$",
            "${indent}function Swim.update(dt, locomotion)",
            "Overclocked Jenkins Tendons swim update signature");
        swimText = RegexReplaceOnce(
            swimText,
            @"(?m)^(?<indent>\s*)local\s+want\s*=\s*shouldApply\s*\(\s*\)\s*$",
            "${indent}local want = shouldApply(locomotion)",
            "Overclocked Jenkins Tendons swim snapshot use");
        context.Write(swim, swimText);

        return SemanticInjectionResult.Success(
            "Centralized OJT input through one wildcard ActionRouter subscriber that decodes action type only for sprint/dash/dodge work, wakes the runtime on relevant input, runs stable idle maintenance at 10 Hz, preserves Ignition/swim/smoke active work in realtime, and collapses sprint/slide/swim state reads into one locomotion blackboard snapshot per executed update.");
    }

}
