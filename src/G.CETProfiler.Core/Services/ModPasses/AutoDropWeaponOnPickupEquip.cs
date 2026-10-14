using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAutoDropWeaponOnPickupEquip(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "ADWOP_CaptureNextAction",
            "ADWOP_ToggleDebug",
            "triggerActions",
            "captureArmed",
            "cfg.debugPrint",
            "Observe(\"PlayerPuppet\", \"OnAction\"");

        var text = ReplaceOnce(
            file.Text,
            "local captureArmed = false\nlocal captureStart = 0.0",
            "local __gcetAutoDropApi = nil\n" +
            "local __gcetAutoDropExactHandles = {}\n" +
            "local __gcetAutoDropExactNames = {}\n" +
            "local __gcetAutoDropWildcardHandle = nil\n" +
            "local __gcetAutoDropFallbackBroad = false\n" +
            "local __gcetAutoDropWildcardDirty = false\n" +
            "local __gcetAutoDropRefreshWildcard\n" +
            "local __gcetAutoDropSubscribeExact\n\n" +
            "local captureArmed = false\n" +
            "local captureStart = 0.0",
            "AutoDrop routing forward declarations");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^[ \t]*registerForEvent\s*\(\s*[""']onInit[""']\s*,\s*function\s*\(\s*\)\s*\r?\n[ \t]*Observe\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*_\s*,\s*action\s*\)\s*\r?\n(?<body>.*?)[ \t]*end\s*\)\s*\r?\n[ \t]*\r?\n[ \t]*log\s*\(\s*[""']Loaded\.[^\r\n]*\r?\n[ \t]*end\s*\)\s*",
            """
local function __gcetAutoDropHandleAction(_, action, consumer, routedName)
    if not action then return end
    local name = routedName
    if name == nil or name == "" then
        local raw = action:GetName(action)
        name = raw and raw.value or nil
        if name == nil or name == "" then name = getActionName(action) end
    end

    local aType = action:GetType()
    if aType ~= gameinputActionType.BUTTON_PRESSED then return end

    if captureArmed and t >= captureStart then
        captureArmed = false
        if not triggerActions[name] then
            triggerActions[name] = true
            if __gcetAutoDropSubscribeExact then __gcetAutoDropSubscribeExact(name) end
        end
        log("CAPTURED action name: " .. tostring(name) .. "  (added as trigger)")
        __gcetAutoDropWildcardDirty = true
    end

    if cfg.debugPrint then
        print(string.format("[ADWOP] Action=%s", tostring(name)))
    end

    if not triggerActions[name] then return end

    local player = GetPlayer()
    if not player then return end
    local curID = getActiveWeaponItemID(player)
    if not curID or isUnarmedItemID(curID) then return end

    oldItemID = curID
    oldKey = itemKey(curID)
    pending = true
    pendingUntil = t + cfg.swapWindow
    dropAt = 0.0
end

__gcetAutoDropSubscribeExact = function(name)
    if __gcetAutoDropFallbackBroad or not __gcetAutoDropApi or __gcetAutoDropExactNames[name] then return end
    local ok, handle = pcall(function()
        return __gcetAutoDropApi.SubscribeAction({
            id = "G-CET.AutoDrop.Exact." .. tostring(name),
            actions = { name },
            decodeType = false
        }, __gcetAutoDropHandleAction, "AutoDropWeaponOnPickupEquip")
    end)
    if ok and handle then
        __gcetAutoDropExactNames[name] = true
        __gcetAutoDropExactHandles[#__gcetAutoDropExactHandles + 1] = handle
    end
end

__gcetAutoDropRefreshWildcard = function()
    __gcetAutoDropWildcardDirty = false
    if __gcetAutoDropFallbackBroad or not __gcetAutoDropApi then return end
    local wanted = captureArmed or cfg.debugPrint
    if wanted and not __gcetAutoDropWildcardHandle then
        local ok, handle = pcall(function()
            return __gcetAutoDropApi.SubscribeAction({
                id = "G-CET.AutoDrop.Wildcard",
                actions = "*",
                decodeType = false
            }, function(this, action, consumer, routedName)
                if routedName and triggerActions[routedName] then return end
                __gcetAutoDropHandleAction(this, action, consumer, routedName)
            end, "AutoDropWeaponOnPickupEquip")
        end)
        if ok then __gcetAutoDropWildcardHandle = handle end
    elseif not wanted and __gcetAutoDropWildcardHandle then
        if type(__gcetAutoDropWildcardHandle.unsubscribe) == "function" then
            pcall(__gcetAutoDropWildcardHandle.unsubscribe)
        end
        __gcetAutoDropWildcardHandle = nil
    end
end

registerForEvent("onInit", function()
    local ok, engine = pcall(GetMod, "0-Engine")
    if ok and type(engine) == "table" then
        __gcetAutoDropApi = type(engine.GCET) == "table" and engine.GCET or engine
    end

    if __gcetAutoDropApi and type(__gcetAutoDropApi.SubscribeAction) == "function" then
        for name, enabled in pairs(triggerActions) do
            if enabled then __gcetAutoDropSubscribeExact(name) end
        end
        __gcetAutoDropRefreshWildcard()
    else
        __gcetAutoDropFallbackBroad = true
        Observe("PlayerPuppet", "OnAction", __gcetAutoDropHandleAction)
    end

    log("Loaded. (Optional) bind ADWOP inputs in CET -> Bindings -> Inputs.")
end)
""" + "\n",
            "AutoDrop exact + temporary wildcard routing");

        text = ReplaceOnce(
            text,
            "  captureArmed = true\n  captureStart = t + 0.15",
            "  captureArmed = true\n  captureStart = t + 0.15\n  if __gcetAutoDropRefreshWildcard then __gcetAutoDropRefreshWildcard() end",
            "AutoDrop capture wildcard wake");

        text = ReplaceOnce(
            text,
            "  cfg.debugPrint = not cfg.debugPrint\n  log(\"debugPrint = \" .. tostring(cfg.debugPrint))",
            "  cfg.debugPrint = not cfg.debugPrint\n  if __gcetAutoDropRefreshWildcard then __gcetAutoDropRefreshWildcard() end\n  log(\"debugPrint = \" .. tostring(cfg.debugPrint))",
            "AutoDrop debug wildcard wake");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\)\s*)\r?\n(?<indent>[ \t]*)t\s*=\s*t\s*\+\s*\(dt\s+or\s+0\)\s*$",
            "${opening}\n" +
            "${indent}if __gcetAutoDropWildcardDirty and __gcetAutoDropRefreshWildcard then __gcetAutoDropRefreshWildcard() end\n" +
            "${indent}if not pending and not captureArmed then return end\n" +
            "${indent}t = t + (dt or 0)",
            "AutoDrop pending/capture-only update gate");

        // Named capture groups expand directly in the RegexReplaceOnce replacement.

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Replaced AutoDrop's global PlayerPuppet action observer with exact pickup/equip routes. Wildcard delivery exists only while capture/debug mode is explicitly active, newly captured names get exact subscriptions, and onUpdate sleeps unless capture timing or a pending swap needs it.");
    }

}
