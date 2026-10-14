using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMarmurBank(
        SemanticPatchContext context)
    {
        var interaction = context.FindFile(
            "external/InteractionUI.lua",
            "WORLD_INTERACTION_ACTIONS",
            "shouldBlockWorldAction",
            "local wrapped = wrappedMethod",
            "local wrappedConsumer = consumer",
            "Override(\"PlayerPuppet\", \"OnAction\"",
            "Observe('PlayerPuppet', 'OnAction'");

        var init = context.FindFile(
            "init.lua",
            "local bank = require(\"module/Bank\")",
            "local function startTimers()",
            "bank:distanceListener()",
            "bankTimerFirstRun",
            "Mod.Subscribe(\"MenuOpen\"");

        _ = context.FindFile(
            "external/GameUI.lua",
            "GameUI.Event.MenuClose",
            "function GameUI.Observe");

        var interactionText = interaction.Text;

        if (!interactionText.Contains(
                "__gcetMarmurActionRelevant",
                StringComparison.Ordinal))
        {
            interactionText = RegexReplaceOnce(
                interactionText,
                @"(?m)^([ \t]*)(local\s+function\s+isPressed\s*\(\s*actionType\s*\)[^\r\n]*)$",
                "$1local function __gcetMarmurActionRelevant(actionName)\n" +
                "$1    local name = tostring(actionName or \"\")\n" +
                "$1    return WORLD_INTERACTION_ACTIONS[name] == true\n" +
                "$1        or name == \"ChoiceScrollUp\"\n" +
                "$1        or name == \"ChoiceScrollDown\"\n" +
                "$1        or name == \"ChoiceApply\"\n" +
                "$1end\n\n" +
                "$1$2",
                "MarmurBank finite action-interest helper");

            interactionText = RegexReplaceOnce(
                interactionText,
                @"(?m)^([ \t]*)if\s+action\s+then\s*\r?\n([ \t]*)local\s+actionName\s*,\s*actionType\s*=\s*getActionDetails\s*\(\s*action\s*\)\s*$",
                "$1if not ui.hubShown and ui.suppressVanillaDialogs ~= true and type(ui.vanillaSuppressPredicate) ~= \"function\" then\n" +
                "$1    if wrapped then\n" +
                "$1        if wrappedConsumer ~= nil then return wrapped(action, wrappedConsumer) end\n" +
                "$1        return wrapped(action)\n" +
                "$1    end\n" +
                "$1    return false\n" +
                "$1end\n\n" +
                "$1if action then\n" +
                "$2local __gcetActionName = \"\"\n" +
                "$2pcall(function() __gcetActionName = Game.NameToString(action:GetName(action)) or \"\" end)\n" +
                "$2if not __gcetMarmurActionRelevant(__gcetActionName) then\n" +
                "$2    if wrapped then\n" +
                "$2        if wrappedConsumer ~= nil then return wrapped(action, wrappedConsumer) end\n" +
                "$2        return wrapped(action)\n" +
                "$2    end\n" +
                "$2    return false\n" +
                "$2end\n" +
                "$2local actionName, actionType = getActionDetails(action)",
                "MarmurBank dormant Override gate and finite prefilter");

            interactionText = RegexReplaceOnce(
                interactionText,
                @"(?m)^([ \t]*)Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\s*\(\s*_\s*,\s*action\s*\)\s*\r?\n([ \t]*)if\s+shouldBlockWorldAction\s*\(\s*getActionDetails\s*\(\s*action\s*\)\s*\)\s+then",
                "$1Observe(\"PlayerPuppet\", \"OnAction\", function(_, action)\n" +
                "$2if not ui.hubShown and ui.suppressVanillaDialogs ~= true and type(ui.vanillaSuppressPredicate) ~= \"function\" then return end\n" +
                "$2local __gcetActionName = Game.NameToString(action:GetName(action))\n" +
                "$2if not __gcetMarmurActionRelevant(__gcetActionName) then return end\n" +
                "$2if shouldBlockWorldAction(getActionDetails(action)) then",
                "MarmurBank dormant Observe gate and finite prefilter");
        }

        var initText = init.Text;

        if (!Regex.IsMatch(
                initText,
                @"(?m)^\s*local\s+GameUI\s*=\s*require\s*\(\s*['""]external/GameUI['""]\s*\)\s*$",
                RegexOptions.CultureInvariant))
        {
            initText = RegexReplaceOnce(
                initText,
                @"(?m)^([ \t]*)local\s+GameSettings\s*=\s*require\s*\(\s*['""]external/GameSettings['""]\s*\)\s*$",
                "$0\n$1local GameUI = require(\"external/GameUI\")",
                "MarmurBank GameUI dependency");
        }

        if (!Regex.IsMatch(
                initText,
                @"(?m)^\s*local\s+atmExitZone\s*=",
                RegexOptions.CultureInvariant))
        {
            initText = RegexReplaceOnce(
                initText,
                @"(?m)^([ \t]*)local\s+atmTimer\s*=\s*nil\s*$",
                "$0\n$1local atmExitZone = nil",
                "MarmurBank active ATM exit zone state");
        }

        if (!initText.Contains(
                "__gcetMarmurQueueAfterDropPoint",
                StringComparison.Ordinal))
        {
            var helpers =
                "local function __gcetMarmurClearAtmExitZone()\n" +
                "\tif not atmExitZone then return end\n" +
                "\tlocal handle = atmExitZone\n" +
                "\tatmExitZone = nil\n" +
                "\tif type(handle.unregister) == \"function\" then pcall(handle.unregister) end\n" +
                "end\n\n" +
                "local function __gcetMarmurClearAtmWake()\n" +
                "\tif not atmTimer then return end\n" +
                "\tif engine then engine.ClearTimer(atmTimer) end\n" +
                "\tatmTimer = nil\n" +
                "end\n\n" +
                "local function __gcetMarmurStopActiveUi()\n" +
                "\tif not uiTimer then return end\n" +
                "\tif engine then engine.ClearTimer(uiTimer) end\n" +
                "\tuiTimer = nil\n" +
                "end\n\n" +
                "local function __gcetMarmurStartActiveUi()\n" +
                "\tif uiTimer or not Mod then return end\n" +
                "\tuiTimer = Mod.SetInterval(0.5, function()\n" +
                "\t\tif not engine or not engine.IsPlaying() then return end\n" +
                "\t\tlocal active = bank.hub ~= nil\n" +
                "\t\tif bank.interactionUI then\n" +
                "\t\t\tactive = active\n" +
                "\t\t\t\tor bank.interactionUI.hubShown == true\n" +
                "\t\t\t\tor bank.interactionUI.suppressVanillaDialogs == true\n" +
                "\t\t\t\tor bank.interactionUI.clearingVanillaDialogs == true\n" +
                "\t\tend\n" +
                "\t\tif not active then\n" +
                "\t\t\t__gcetMarmurStopActiveUi()\n" +
                "\t\t\treturn\n" +
                "\t\tend\n" +
                "\t\tif bank.interactionUI then bank.interactionUI.update() end\n" +
                "\t\tbank:checkSubTitle()\n" +
                "\tend)\n" +
                "end\n\n" +
                "local function __gcetMarmurArmExitZone(location)\n" +
                "\t__gcetMarmurClearAtmExitZone()\n" +
                "\tif not Mod or type(Mod.RegisterZone) ~= \"function\" or not location then return end\n" +
                "\tlocal radius = math.max((tonumber(settings.atmDistance) or 2.0) + 0.75, 2.75)\n" +
                "\tatmExitZone = Mod.RegisterZone({\n" +
                "\t\tid = \"marmurbank.active-atm\",\n" +
                "\t\tx = location.x,\n" +
                "\t\ty = location.y,\n" +
                "\t\tz = location.z,\n" +
                "\t\tradius = radius,\n" +
                "\t\tthrottle = 5,\n" +
                "\t\tonExit = function()\n" +
                "\t\t\tbank:hideHub()\n" +
                "\t\t\t__gcetMarmurStopActiveUi()\n" +
                "\t\t\tlocal handle = atmExitZone\n" +
                "\t\t\tatmExitZone = nil\n" +
                "\t\t\tif handle and type(handle.unregister) == \"function\" then pcall(handle.unregister) end\n" +
                "\t\tend,\n" +
                "\t})\n" +
                "end\n\n" +
                "local function __gcetMarmurShowAfterDropPoint()\n" +
                "\tatmTimer = nil\n" +
                "\tif not engine or not Mod or not engine.IsPlaying() then return end\n" +
                "\tlocal state = engine.GetState()\n" +
                "\tif not state or state.inMenu or state.inVehicle or state.inCombat then return end\n" +
                "\tlocal num = bank:distanceListener()\n" +
                "\tlocal location = bank.nearestLocation\n" +
                "\tif num <= 0 or not location or location.type ~= \"atm\" then return end\n" +
                "\tbank:showHub()\n" +
                "\tbank:checkSubTitle()\n" +
                "\t__gcetMarmurStartActiveUi()\n" +
                "\t__gcetMarmurArmExitZone(location)\n" +
                "end\n\n" +
                "local function __gcetMarmurQueueAfterDropPoint()\n" +
                "\tif not Mod then return end\n" +
                "\t__gcetMarmurClearAtmWake()\n" +
                "\tatmTimer = Mod.SetTimeout(0.35, __gcetMarmurShowAfterDropPoint)\n" +
                "end\n\n";

            initText = RegexReplaceOnce(
                initText,
                @"(?m)^([ \t]*)local\s+function\s+stopTimers\s*\(\s*\)\s*$",
                helpers + "$1local function stopTimers()",
                "MarmurBank event-driven ATM helpers");

            initText = RegexReplaceOnce(
                initText,
                @"(?ms)^(local\s+function\s+stopTimers\s*\(\s*\).*?)([ \t]*)bank:hideHub\s*\(\s*\)\s*\r?\nend\s*$",
                "$1$2__gcetMarmurClearAtmExitZone()\n" +
                "$2bank:hideHub()\n" +
                "end",
                "MarmurBank stop active ATM zone");

            initText = RegexReplaceOnce(
                initText,
                @"(?ms)^local\s+function\s+startTimers\s*\(\s*\)\s*\r?\n.*?(?=^[ \t]*local\s+firstInterval\s*=\s*10(?:\.0)?\s*$)",
                "local function startTimers()\n" +
                "\tif bankTimer then return end\n\n" +
                "\t-- Drop-point ATM banking is event-driven. The bank prompt is armed only\n" +
                "\t-- after the vanilla Vendor/Trade menu closes; no proximity/UI poll runs while idle.\n",
                "MarmurBank remove permanent ATM and UI polling");

            initText = RegexReplaceOnce(
                initText,
                @"(?ms)^[ \t]*Mod\.Subscribe\s*\(\s*['""]MenuOpen['""]\s*,\s*function\s*\(\s*\)\s*\r?\n[ \t]*bank:hideHub\s*\(\s*\)\s*\r?\n[ \t]*end\s*\)\s*$",
                "\tMod.Subscribe(\"MenuOpen\", function()\n" +
                "\t\t__gcetMarmurClearAtmWake()\n" +
                "\t\t__gcetMarmurClearAtmExitZone()\n" +
                "\t\t__gcetMarmurStopActiveUi()\n" +
                "\t\tbank:hideHub()\n" +
                "\tend)\n\n" +
                "\tGameUI.Observe(GameUI.Event.MenuClose, function(state)\n" +
                "\t\tif not state or state.lastMenu ~= \"Vendor\" then return end\n" +
                "\t\tif state.lastSubmenu ~= nil and state.lastSubmenu ~= false and state.lastSubmenu ~= \"Trade\" then return end\n" +
                "\t\t__gcetMarmurQueueAfterDropPoint()\n" +
                "\tend)",
                "MarmurBank post-drop-point vendor close wake");
        }

        context.Write(interaction, interactionText);
        context.Write(init, initText);
        return SemanticInjectionResult.Success(
            "Made MarmurBank ATM/drop-point banking event-driven: the vanilla Vendor/Trade interaction wins first, then a delayed one-shot spatial check can show the bank prompt after menu exit. Permanent ATM/UI polling is removed while dormant, the active prompt gets only a temporary exit zone/UI timer, and the PlayerPuppet action hooks fast-pass when banking is inactive.");
    }

}
