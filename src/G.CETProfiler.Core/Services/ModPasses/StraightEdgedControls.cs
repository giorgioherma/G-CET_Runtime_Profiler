using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyStraightEdgedControls(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Lean.pollInput()",
            "Inspection.pollInput()",
            "SettingsPoll.poll",
            "Attachments.update(deltaTime)");

        var initText = init.Text.Replace(
            "    UIBlocking.beginFrame()\n",
            "",
            StringComparison.Ordinal);

        initText = ReplaceOnce(
            initText,
            "    SettingsPoll.poll()\n",
            "    SettingsPoll.poll(deltaTime)\n",
            "Straight Edged Controls low-rate settings tick input");

        if (!initText.Contains("Inspection.onSessionReset()", StringComparison.Ordinal))
        {
            initText = ReplaceOnce(
                initText,
                "        Lean.onSessionReset()\n        SettingsPoll.invalidateZoom()\n",
                "        Lean.onSessionReset()\n        Inspection.onSessionReset()\n        SettingsPoll.invalidateZoom()\n",
                "Straight Edged Controls inspection bridge reset");
        }
        context.Write(init, initText);

        var ui = context.FindFile(
            "modules/ui_blocking.lua",
            "function UIBlocking.isBlocked()",
            "isInMenuFlag()",
            "isPhoneActive()",
            "isDeviceUIActive()",
            "isScannerActive()",
            "isPhotoModeActive()");

        var uiText = ui.Text;
        if (uiText.Contains("local function __gcetComputeBlocked()", StringComparison.Ordinal))
        {
            uiText = RegexReplaceOnce(
                uiText,
                @"(?ms)^function\s+UIBlocking\.isBlocked\s*\(\s*\)\s*\r?\n.*?^end\s*$",
                "function UIBlocking.isBlocked()\n" +
                "    return __gcetComputeBlocked()\n" +
                "end",
                "Straight Edged Controls event-driven UI blocking");
        }
        else if (!uiText.Contains("function UIBlocking.beginFrame()", StringComparison.Ordinal))
        {
            uiText = ReplaceOnce(
                uiText,
                "\nreturn UIBlocking",
                "\n-- G-CET compatibility: callers may request an explicit fresh UI snapshot.\n" +
                "function UIBlocking.beginFrame()\n" +
                "    return UIBlocking.isBlocked()\n" +
                "end\n\n" +
                "return UIBlocking",
                "Straight Edged Controls UI snapshot compatibility");
        }
        context.Write(ui, uiText);

        var lean = context.FindFile(
            "modules/lean.lua",
            "function Lean.pollInput()",
            "StraightEdgedControls.SECInputState",
            "function Lean.onSessionReset()",
            "Lean.onKeyEvent");

        var leanText = ReplaceOnce(
            lean.Text,
            "local lastSeq = -1\nlocal wasInMenu = false",
            "local lastSeq = -1\n" +
            "local wasInMenu = false\n" +
            "local __gcetInputState = nil\n\n" +
            "local function __gcetGetInputState()\n" +
            "    if __gcetInputState ~= nil then return __gcetInputState end\n" +
            "    local ok, inputState = pcall(function()\n" +
            "        local container = Game.GetScriptableSystemsContainer()\n" +
            "        if not container then return nil end\n" +
            "        return container:Get(CName.new('StraightEdgedControls.SECInputState'))\n" +
            "    end)\n" +
            "    if ok and inputState then __gcetInputState = inputState end\n" +
            "    return ok and inputState or nil\n" +
            "end",
            "Straight Edged Controls cached lean input bridge");

        leanText = RegexReplaceOnce(
            leanText,
            @"(?ms)^function\s+Lean\.pollInput\s*\(\s*\)\s*\r?\n.*?^[ \t]*Lean\.onKeyEvent\s*\(\s*inputState\.side\s*,\s*inputState\.pressed\s*,\s*inputState\.mode\s*,\s*inputState\.switchSidesInstantly\s*\)\s*\r?\nend\s*$",
            "function Lean.pollInput()\n" +
            "    local inputState = __gcetGetInputState()\n" +
            "    if not inputState then return end\n" +
            "    local seq = inputState.seq\n" +
            "    if seq == lastSeq then return end\n" +
            "    lastSeq = seq\n\n" +
            "    -- The redscript bridge sequence is the wake signal. Only a real key\n" +
            "    -- transition pays for the expensive menu/phone/scanner blackboards.\n" +
            "    local inMenu = UIBlocking.beginFrame()\n" +
            "    if wasInMenu and not inMenu then\n" +
            "        state.leftHeld = false\n" +
            "        state.rightHeld = false\n" +
            "    end\n" +
            "    wasInMenu = inMenu\n" +
            "    if inMenu then return end\n\n" +
            "    Lean.onKeyEvent(inputState.side, inputState.pressed, inputState.mode, inputState.switchSidesInstantly)\n" +
            "end",
            "Straight Edged Controls lean sequence wake");

        leanText = ReplaceOnce(
            leanText,
            "    lastSeq = -1\n    wasInMenu = false\n",
            "    lastSeq = -1\n    wasInMenu = false\n    __gcetInputState = nil\n",
            "Straight Edged Controls lean bridge reset");
        context.Write(lean, leanText);

        var inspection = context.FindFile(
            "modules/inspection.lua",
            "function Inspection.pollInput()",
            "StraightEdgedControls.SECInputState",
            "state.lastInspectSeq",
            "Inspection.onKey");

        var inspectionText = ReplaceOnce(
            inspection.Text,
            "}\n\nlocal function getSettings()",
            "}\n\n" +
            "local __gcetInputState = nil\n" +
            "local function __gcetGetInputState()\n" +
            "    if __gcetInputState ~= nil then return __gcetInputState end\n" +
            "    local ok, inputState = pcall(function()\n" +
            "        local container = Game.GetScriptableSystemsContainer()\n" +
            "        if not container then return nil end\n" +
            "        return container:Get(CName.new('StraightEdgedControls.SECInputState'))\n" +
            "    end)\n" +
            "    if ok and inputState then __gcetInputState = inputState end\n" +
            "    return ok and inputState or nil\n" +
            "end\n\n" +
            "local function getSettings()",
            "Straight Edged Controls cached inspection input bridge");

        inspectionText = RegexReplaceOnce(
            inspectionText,
            @"(?ms)^function\s+Inspection\.pollInput\s*\(\s*\)\s*\r?\n.*?^[ \t]*Inspection\.onKey\s*\(\s*inputState\.inspectPressed\s*,\s*inputState\.inspectReleased\s*\)\s*\r?\nend\s*$",
            "function Inspection.pollInput()\n" +
            "    local inputState = __gcetGetInputState()\n" +
            "    if not inputState then return end\n" +
            "    local seq = inputState.inspectSeq\n" +
            "    if seq == state.lastInspectSeq then return end\n" +
            "    state.lastInspectSeq = seq\n" +
            "    Inspection.onKey(inputState.inspectPressed, inputState.inspectReleased)\n" +
            "end",
            "Straight Edged Controls inspection sequence wake");

        inspectionText = ReplaceOnce(
            inspectionText,
            "\nreturn Inspection",
            "\nfunction Inspection.onSessionReset()\n" +
            "    __gcetInputState = nil\n" +
            "    state.lastInspectSeq = 0\n" +
            "end\n\n" +
            "return Inspection",
            "Straight Edged Controls inspection bridge session reset");
        context.Write(inspection, inspectionText);

        var settings = context.FindFile(
            "modules/settings_poll.lua",
            "function Settings.poll",
            "StraightEdgedControls.SECSettings",
            "FasterAiming.tick",
            "lastMasterEnabled",
            "lastFasterAiming");

        var settingsText = ReplaceOnce(
            settings.Text,
            "local zoomReady = false",
            "local zoomReady = false\n" +
            "local __gcetSettings = nil\n" +
            "local __gcetPollElapsed = 0.5\n" +
            "local __gcetPollInterval = 0.5",
            "Straight Edged Controls settings cadence state");

        settingsText = RegexReplaceOnce(
            settingsText,
            @"(?ms)^local\s+function\s+getSettings\s*\(\s*\)\s*\r?\n.*?^end\s*$",
            "local function getSettings()\n" +
            "    if __gcetSettings ~= nil then return __gcetSettings end\n" +
            "    local ok, settings = pcall(function()\n" +
            "        local container = Game.GetScriptableSystemsContainer()\n" +
            "        if not container then return nil end\n" +
            "        return container:Get(CName.new('StraightEdgedControls.SECSettings'))\n" +
            "    end)\n" +
            "    if ok and settings then __gcetSettings = settings end\n" +
            "    return ok and settings or nil\n" +
            "end",
            "Straight Edged Controls cached settings bridge");

        settingsText = ReplaceOnce(
            settingsText,
            "function Settings.invalidateZoom()\n    zoomReady = false",
            "function Settings.invalidateZoom()\n" +
            "    __gcetSettings = nil\n" +
            "    __gcetPollElapsed = __gcetPollInterval\n" +
            "    zoomReady = false",
            "Straight Edged Controls settings bridge reset");

        settingsText = ReplaceOnce(
            settingsText,
            "function Settings.poll()\n    local settings = getSettings()",
            "function Settings.poll(deltaTime)\n" +
            "    -- Weapon swaps need frame maintenance only when Instant ADS is actually enabled.\n" +
            "    if lastMasterEnabled and lastFasterAiming then FasterAiming.tick(true) end\n" +
            "    __gcetPollElapsed = __gcetPollElapsed + (deltaTime or __gcetPollInterval)\n" +
            "    if __gcetPollElapsed < __gcetPollInterval then return end\n" +
            "    __gcetPollElapsed = 0.0\n" +
            "    local settings = getSettings()",
            "Straight Edged Controls settings 2Hz gate");

        settingsText = settingsText.Replace(
            "        ScrollWalk.tick()\n",
            "",
            StringComparison.Ordinal);
        context.Write(settings, settingsText);

        var attachments = context.FindFile(
            "modules/attachments.lua",
            "function Attachments.update(deltaTime)",
            "StraightEdgedControls.SECSettings",
            "StraightEdgedControls.SECAttachments",
            "state.lastSuppressorSeq",
            "state.lastSightSeq");

        var attachmentsText = ReplaceOnce(
            attachments.Text,
            "}\n\nlocal function getSettings()",
            "}\n\n" +
            "local __gcetSettings = nil\n" +
            "local __gcetAttachmentsBridge = nil\n\n" +
            "local function getSettings()",
            "Straight Edged Controls attachment bridge caches");

        attachmentsText = RegexReplaceOnce(
            attachmentsText,
            @"(?ms)^local\s+function\s+getSettings\s*\(\s*\)\s*\r?\n.*?^end\s*$",
            "local function getSettings()\n" +
            "    if __gcetSettings ~= nil then return __gcetSettings end\n" +
            "    local ok, settings = pcall(function()\n" +
            "        local container = Game.GetScriptableSystemsContainer()\n" +
            "        if not container then return nil end\n" +
            "        return container:Get(CName.new(\"StraightEdgedControls.SECSettings\"))\n" +
            "    end)\n" +
            "    if ok and settings then __gcetSettings = settings end\n" +
            "    return ok and settings or nil\n" +
            "end",
            "Straight Edged Controls cached attachment settings");

        attachmentsText = RegexReplaceOnce(
            attachmentsText,
            @"(?ms)^local\s+function\s+getAttachmentsBridge\s*\(\s*\)\s*\r?\n.*?^end\s*$",
            "local function getAttachmentsBridge()\n" +
            "    if __gcetAttachmentsBridge ~= nil then return __gcetAttachmentsBridge end\n" +
            "    local ok, bridge = pcall(function()\n" +
            "        local container = Game.GetScriptableSystemsContainer()\n" +
            "        if not container then return nil end\n" +
            "        return container:Get(CName.new(\"StraightEdgedControls.SECAttachments\"))\n" +
            "    end)\n" +
            "    if ok and bridge then __gcetAttachmentsBridge = bridge end\n" +
            "    return ok and bridge or nil\n" +
            "end",
            "Straight Edged Controls cached attachment input bridge");

        attachmentsText = ReplaceOnce(
            attachmentsText,
            "function Attachments.update(deltaTime)\n" +
            "    if not featureAllowed() then\n" +
            "        abortPending()\n" +
            "        return\n" +
            "    end\n\n" +
            "    local bridge = getAttachmentsBridge()\n" +
            "    if bridge then",
            "function Attachments.update(deltaTime)\n" +
            "    local bridge = getAttachmentsBridge()\n" +
            "    local suppressorChanged = bridge and bridge.suppressorSeq ~= state.lastSuppressorSeq\n" +
            "    local sightChanged = bridge and bridge.sightSeq ~= state.lastSightSeq\n" +
            "    local holdActive = state.holdArmed.suppressor or state.holdArmed.sight\n" +
            "    if not suppressorChanged and not sightChanged and not holdActive and not state.pending then return end\n\n" +
            "    if not featureAllowed() then\n" +
            "        abortPending()\n" +
            "        return\n" +
            "    end\n\n" +
            "    if bridge then",
            "Straight Edged Controls attachment idle sequence gate");

        attachmentsText = ReplaceOnce(
            attachmentsText,
            "function Attachments.reset()\n    abortPending()\n",
            "function Attachments.reset()\n" +
            "    abortPending()\n" +
            "    __gcetSettings = nil\n" +
            "    __gcetAttachmentsBridge = nil\n",
            "Straight Edged Controls attachment bridge reset");
        context.Write(attachments, attachmentsText);

        return SemanticInjectionResult.Success(
            "Reworked Straight Edged Controls around its redscript sequence counters: idle frames now return after cached bridge sequence reads; expensive UI/settings/native work runs only on real key transitions, active holds/pending swaps, or 2 Hz settings maintenance. FasterAiming keeps frame maintenance only while enabled.");
    }

}
