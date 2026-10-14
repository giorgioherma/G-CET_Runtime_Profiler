using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyEasyTrainer(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Event.Observe(\"PlayerPuppet\", \"OnAction\"",
            "SelfFeature.NoClip.HandleMouseLook(action)",
            "Utils.Weapon.HandleInputAction(action)",
            "Event.RegisterUpdate(function(dt)");

        _ = context.FindFile(
            "Core/Event.lua",
            "function Event.Observe(class, method, fn)",
            "Observe(class, method, fn)",
            "Logger.Log(string.format(\"Event: Observing %s.%s\"");

        var noClip = context.FindFile(
            "Features/Self/Abilities/NoClip.lua",
            "function Noclip.HandleMouseLook(action)",
            "CameraMouseX",
            "Game.GetSettingsSystem()");

        var weapon = context.FindFile(
            "Utils/Weapon.lua",
            "function Weapon.HandleInputAction(action)",
            "Weapon.isAiming = player.isAiming",
            "function Weapon.Tick(deltaTime)",
            "ReadEquippedRightHand");

        var registry = context.FindFile(
            "UI/Registry/OptionRegistry.lua",
            "local function HotkeyAction(id)",
            "function OptionRegistry.RegisterHotkeyActions()",
            "function OptionRegistry.UpdateHotkeys()");

        var restrictions = context.FindFile(
            "Controls/Restrictions.lua",
            "function Restrictions.Update()",
            "Input.UpdateDevice()",
            "if not menuOpen then return end");

        var mobility = context.FindFile(
            "Features/Self/Abilities/AdvancedMobility.lua",
            "local function HandleStatToggle(toggle, statName, appliedFlag, label)",
            "toggleDoubleJump",
            "doubleJumpApplied");

        var superSpeed = context.FindFile(
            "Features/Self/Abilities/SuperSpeed.lua",
            "function SuperSpeed.Tick()",
            "SuperSpeed.enabled.value",
            "local applied = false");

        var thrusters = context.FindFile(
            "Features/Self/Abilities/AirThrusterBoots.lua",
            "function AirThrusterBoots.Tick()",
            "AirThrusterBoots.enabled.value",
            "local applied = false");

        var invisibility = context.FindFile(
            "Features/Self/Abilities/Invisibility.lua",
            "function Invisibility.Tick()",
            "Invisibility.enabled.value",
            "local wasApplied = false");

        var oldAction =
            "    Event.Observe(\"PlayerPuppet\", \"OnAction\", function(_, action)\n" +
            "        if modulesLoaded then\n" +
            "            SelfFeature.NoClip.HandleMouseLook(action)\n" +
            "            if Utils then\n" +
            "                Utils.Weapon.HandleInputAction(action)\n" +
            "            end\n" +
            "        end\n" +
            "    end)";

        var newAction =
            "    local function __gcetEasyTrainerOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
            "        if type(State.IsGCETDormant) == \"function\" and State.IsGCETDormant() then return end\n" +
            "        if not modulesLoaded then return end\n" +
            "        local __gcetActionName = __gcetRoutedName or Game.NameToString(action:GetName(action))\n" +
            "        if __gcetActionName == \"CameraMouseX\" then\n" +
            "            SelfFeature.NoClip.HandleMouseLook(action, __gcetActionName)\n" +
            "        end\n" +
            "        if Utils and __gcetActionName == \"RangedAttack\" then\n" +
            "            Utils.Weapon.HandleInputAction(action, __gcetActionName)\n" +
            "        end\n" +
            "    end\n\n" +
            "    local __gcetEasyTrainerRouted = false\n" +
            "    local __gcetEasyTrainerHandles = {}\n" +
            "    local __gcetEasyTrainerOk, __gcetEasyTrainerEngine = pcall(GetMod, \"0-Engine\")\n" +
            "    local __gcetEasyTrainerApi = __gcetEasyTrainerEngine\n" +
            "    if __gcetEasyTrainerOk and type(__gcetEasyTrainerEngine) == \"table\" and type(__gcetEasyTrainerEngine.GCET) == \"table\" then\n" +
            "        __gcetEasyTrainerApi = __gcetEasyTrainerEngine.GCET\n" +
            "    end\n" +
            "    if __gcetEasyTrainerOk and type(__gcetEasyTrainerApi) == \"table\" and type(__gcetEasyTrainerApi.SubscribeAction) == \"function\" then\n" +
            "        __gcetEasyTrainerRouted = pcall(function()\n" +
            "            __gcetEasyTrainerHandles[#__gcetEasyTrainerHandles + 1] = __gcetEasyTrainerApi.SubscribeAction({\n" +
            "                id = \"G-CET.Semantic.EasyTrainer\",\n" +
            "                actions = { \"CameraMouseX\", \"RangedAttack\" },\n" +
            "                decodeType = false\n" +
            "            }, __gcetEasyTrainerOnAction, \"EasyTrainer\")\n" +
            "        end)\n" +
            "        if not __gcetEasyTrainerRouted then\n" +
            "            for _, __gcetHandle in ipairs(__gcetEasyTrainerHandles) do\n" +
            "                if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end\n" +
            "            end\n" +
            "        end\n" +
            "    end\n" +
            "    if not __gcetEasyTrainerRouted then\n" +
            "        Event.Observe(\"PlayerPuppet\", \"OnAction\", function(self, action)\n" +
            "            __gcetEasyTrainerOnAction(self, action, nil, nil)\n" +
            "        end)\n" +
            "    end";

        var initText = ReplaceOnce(
            init.Text,
            oldAction,
            newAction,
            "EasyTrainer source-proven routed OnAction");
        context.Write(init, initText);

        var noClipText = ReplaceOnce(
            noClip.Text,
            "function Noclip.HandleMouseLook(action)\n" +
            "    local actionName = Game.NameToString(action:GetName(action))",
            "function Noclip.HandleMouseLook(action, routedName)\n" +
            "    local actionName = routedName or Game.NameToString(action:GetName(action))",
            "EasyTrainer NoClip routed action name");
        context.Write(noClip, noClipText);

        var weaponText = ReplaceOnce(
            weapon.Text,
            "function Weapon.HandleInputAction(action)\n" +
            "    local player = Game.GetPlayer()\n" +
            "    if not player then return end\n" +
            "\n" +
            "    Weapon.isAiming = player.isAiming\n" +
            "\n" +
            "    local actionName = Game.NameToString(action:GetName(action))\n" +
            "    local actionType = action:GetType(action).value\n" +
            "\n" +
            "    if actionName == \"RangedAttack\" then",
            "function Weapon.HandleInputAction(action, routedName)\n" +
            "    local actionName = routedName or Game.NameToString(action:GetName(action))\n" +
            "    if actionName ~= \"RangedAttack\" then return end\n" +
            "    local actionType = action:GetType(action).value\n" +
            "\n" +
            "    if actionName == \"RangedAttack\" then",
            "EasyTrainer ranged-action hot path");

        weaponText = ReplaceOnce(
            weaponText,
            "    if not player or not ts then return nil, nil, nil end\n" +
            "\n" +
            "    local item = ts:GetItemInSlot(player, \"AttachmentSlots.WeaponRight\")\n" +
            "    if not item then return nil, nil, nil end\n" +
            "\n" +
            "    local itemData = item:GetItemData()\n" +
            "    if not itemData then return item, nil, item:GetItemID() end\n" +
            "\n" +
            "    return item, itemData, item:GetItemID()",
            "    if not player or not ts then return nil, nil, nil, player end\n" +
            "\n" +
            "    local item = ts:GetItemInSlot(player, \"AttachmentSlots.WeaponRight\")\n" +
            "    if not item then return nil, nil, nil, player end\n" +
            "\n" +
            "    local itemData = item:GetItemData()\n" +
            "    if not itemData then return item, nil, item:GetItemID(), player end\n" +
            "\n" +
            "    return item, itemData, item:GetItemID(), player",
            "EasyTrainer weapon snapshot player handoff");

        weaponText = ReplaceOnce(
            weaponText,
            "        local item, itemData, itemID = ReadEquippedRightHand()\n" +
            "        Weapon.currentItem = item",
            "        local item, itemData, itemID, player = ReadEquippedRightHand()\n" +
            "        Weapon.isAiming = player and player.isAiming or false\n" +
            "        Weapon.currentItem = item",
            "EasyTrainer frame aim-state maintenance");
        context.Write(weapon, weaponText);

        var registryText = ReplaceOnce(
            registry.Text,
            "local RefIndex = setmetatable({}, { __mode = \"k\" })\n" +
            "local HotkeyDown = {}\n",
            "local RefIndex = setmetatable({}, { __mode = \"k\" })\n" +
            "local HotkeyDown = {}\n" +
            "local __gcetBindings = nil\n" +
            "local function __gcetGetBindings()\n" +
            "    if __gcetBindings == nil then __gcetBindings = require(\"Controls/Bindings\") end\n" +
            "    return __gcetBindings\n" +
            "end\n",
            "EasyTrainer cached bindings module");

        registryText = RegexReplaceOnce(
            registryText,
            @"(?ms)^(?<prefix>function\s+OptionRegistry\.SetHotkey\s*\([^\r\n]*\)\s*\r?\n.*?)^(?<indent>[ \t]*)local\s+Bindings\s*=\s*require\(\s*['""]Controls/Bindings['""]\s*\)\s*$",
            "$" + "{prefix}$" + "{indent}local Bindings = __gcetGetBindings()",
            "EasyTrainer SetHotkey cached bindings");

        registryText = RegexReplaceOnce(
            registryText,
            @"(?ms)^(?<prefix>function\s+OptionRegistry\.RegisterHotkeyActions\s*\(\s*\)\s*\r?\n.*?)^(?<indent>[ \t]*)local\s+Bindings\s*=\s*require\(\s*['""]Controls/Bindings['""]\s*\)\s*$",
            "$" + "{prefix}$" + "{indent}local Bindings = __gcetGetBindings()",
            "EasyTrainer RegisterHotkeyActions cached bindings");

        registryText = RegexReplaceOnce(
            registryText,
            @"(?ms)^(?<prefix>function\s+OptionRegistry\.UpdateHotkeys\s*\(\s*\)\s*\r?\n.*?)^(?<indent>[ \t]*)local\s+Bindings\s*=\s*require\(\s*['""]Controls/Bindings['""]\s*\)\s*$",
            "$" + "{prefix}$" + "{indent}local Bindings = __gcetGetBindings()",
            "EasyTrainer UpdateHotkeys cached bindings");

        registryText = ReplaceOnce(
            registryText,
            "            local action = HotkeyAction(entry.Id)\n",
            "            local action = entry.Hotkey or HotkeyAction(entry.Id)\n",
            "EasyTrainer cached hotkey action id");
        context.Write(registry, registryText);

        var restrictionsText = ReplaceOnce(
            restrictions.Text,
            "function Restrictions.Update()\n" +
            "    local menuOpen = State.IsMenuOpen()\n" +
            "    Input.UpdateDevice()\n" +
            "\n" +
            "    local usingController = Input.IsController()\n" +
            "    local mouseEnabled = State.mouseEnabled\n" +
            "    local typingEnabled = State.typingEnabled\n" +
            "\n" +
            "    if not menuOpen and lastMenuOpen then\n" +
            "        Restrictions.Clear()\n" +
            "        lastMenuOpen, lastWasController, lastMouseEnabled, lastTypingEnabled = false, false, false, false\n" +
            "        return\n" +
            "    end\n" +
            "\n" +
            "    if not menuOpen then return end",
            "function Restrictions.Update()\n" +
            "    local menuOpen = State.IsMenuOpen()\n" +
            "    if not menuOpen then\n" +
            "        if lastMenuOpen then\n" +
            "            Restrictions.Clear()\n" +
            "            lastMenuOpen, lastWasController, lastMouseEnabled, lastTypingEnabled = false, false, false, false\n" +
            "        end\n" +
            "        return\n" +
            "    end\n" +
            "\n" +
            "    Input.UpdateDevice()\n" +
            "    local usingController = Input.IsController()\n" +
            "    local mouseEnabled = State.mouseEnabled\n" +
            "    local typingEnabled = State.typingEnabled",
            "EasyTrainer closed-menu input dormancy");
        context.Write(restrictions, restrictionsText);

        var mobilityText = ReplaceOnce(
            mobility.Text,
            "local function HandleStatToggle(toggle, statName, appliedFlag, label)\n" +
            "    local stats = Game.GetStatsSystem()",
            "local function HandleStatToggle(toggle, statName, appliedFlag, label)\n" +
            "    if toggle.value == state[appliedFlag] then return end\n" +
            "    local stats = Game.GetStatsSystem()",
            "EasyTrainer AdvancedMobility transition guard");
        context.Write(mobility, mobilityText);

        var superSpeedText = ReplaceOnce(
            superSpeed.Text,
            "function SuperSpeed.Tick()\n" +
            "    local timeSystem = Game.GetTimeSystem()",
            "function SuperSpeed.Tick()\n" +
            "    if SuperSpeed.enabled.value == applied then return end\n" +
            "    local timeSystem = Game.GetTimeSystem()",
            "EasyTrainer SuperSpeed transition guard");
        context.Write(superSpeed, superSpeedText);

        var thrusterText = ReplaceOnce(
            thrusters.Text,
            "function AirThrusterBoots.Tick()\n" +
            "    local stats = Game.GetStatsSystem()",
            "function AirThrusterBoots.Tick()\n" +
            "    if AirThrusterBoots.enabled.value == applied then return end\n" +
            "    local stats = Game.GetStatsSystem()",
            "EasyTrainer AirThrusterBoots transition guard");
        context.Write(thrusters, thrusterText);

        var invisibilityText = ReplaceOnce(
            invisibility.Text,
            "function Invisibility.Tick()\n" +
            "    local player = Game.GetPlayer()\n" +
            "    local statusSystem = Game.GetStatusEffectSystem()",
            "function Invisibility.Tick()\n" +
            "    if Invisibility.enabled.value == wasApplied then return end\n" +
            "    local player = Game.GetPlayer()",
            "EasyTrainer Invisibility transition guard");
        context.Write(invisibility, invisibilityText);

        return SemanticInjectionResult.Success(
            "Proved EasyTrainer's Event.Observe wrapper is transparent, routed only CameraMouseX/RangedAttack through 0-Engine with wrapper fallback, cached hotkey IDs/bindings, stopped closed-menu device polling, and reduced four native-heavy feature ticks to transition-only work without changing active behavior.");
    }


    private static SemanticInjectionResult ApplyEasyTrainerBackgroundDormancy(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Event.RegisterUpdate(function(dt)",
            "Event.RegisterDraw(function()",
            "registerForEvent(\"onOverlayOpen\", function() State.overlayOpen = true end)",
            "RenderMainMenu()");

        var state = context.FindFile(
            "Controls/State.lua",
            "State.overlayOpen = false",
            "function State.ToggleMenu()",
            "function State.SyncTracking()");

        var settings = context.FindFile(
            "View/Settings/SettingsView.lua",
            "local Buttons = require(\"UI\").Buttons",
            "Buttons.Option(L(\"settingsmenu.saveall.label\")",
            "return { title = \"settingsmenu.title\", view = DrawSettings }");

        var stateText = ReplaceOnce(
            state.Text,
            "State.overlayOpen = false\n" +
            "State.typingEnabled = false",
            "State.overlayOpen = false\n" +
            "State.typingEnabled = false\n" +
            "State.gCETDormant = false\n\n" +
            "function State.IsGCETDormant()\n" +
            "    return State.gCETDormant == true\n" +
            "end\n\n" +
            "function State.SetGCETDormant(value)\n" +
            "    State.gCETDormant = value == true\n" +
            "    State.typingEnabled = false\n" +
            "    if State.gCETDormant then\n" +
            "        State.menuOpen = false\n" +
            "        State.mouseEnabled = false\n" +
            "    else\n" +
            "        State.menuOpen = true\n" +
            "    end\n" +
            "end",
            "EasyTrainer explicit G-CET dormant state");
        context.Write(state, stateText);

        var settingsText = ReplaceOnce(
            settings.Text,
            "local Buttons = require(\"UI\").Buttons\n",
            "local Buttons = require(\"UI\").Buttons\n" +
            "local State = require(\"Controls/State\")\n" +
            "local Restrictions = require(\"Controls/Restrictions\")\n",
            "EasyTrainer dormant settings dependencies");

        settingsText = ReplaceOnce(
            settingsText,
            "    Buttons.Break(\"Configuration\", \"\")\n",
            "    Buttons.Break(\"G-CET Runtime\", \"\")\n" +
            "    Buttons.Option(\"Send EasyTrainer to Background\", \"Suspend EasyTrainer frame-driven UI and features until you wake it from the CET overlay. Existing one-shot or persistent game changes are not reverted.\", function()\n" +
            "        Restrictions.Clear()\n" +
            "        State.SetGCETDormant(true)\n" +
            "    end)\n\n" +
            "    Buttons.Break(\"Configuration\", \"\")\n",
            "EasyTrainer explicit background button");
        context.Write(settings, settingsText);

        var initText = ReplaceOnce(
            init.Text,
            "Event.RegisterUpdate(function(dt)\n" +
            "    Cron.Update(dt)",
            "Event.RegisterUpdate(function(dt)\n" +
            "    if State.IsGCETDormant() then return end\n" +
            "    Cron.Update(dt)",
            "EasyTrainer dormant update fast return");

        initText = ReplaceOnce(
            initText,
            "Event.RegisterDraw(function()\n" +
            "    Notification.Render()",
            "Event.RegisterDraw(function()\n" +
            "    if State.IsGCETDormant() then\n" +
            "        if not State.overlayOpen then return end\n" +
            "        ImGui.SetNextWindowSize(360, 110, ImGuiCond.FirstUseEver)\n" +
            "        if ImGui.Begin(\"EasyTrainer - Background###GCETEasyTrainerDormant\") then\n" +
            "            ImGui.Text(\"EasyTrainer is dormant.\")\n" +
            "            ImGui.Text(\"Wake it to restore trainer UI and runtime features.\")\n" +
            "            if ImGui.Button(\"Wake EasyTrainer\") then\n" +
            "                State.SetGCETDormant(false)\n" +
            "            end\n" +
            "        end\n" +
            "        ImGui.End()\n" +
            "        return\n" +
            "    end\n" +
            "    Notification.Render()",
            "EasyTrainer CET-overlay wake control");

        if (initText.Contains(
                "    local function __gcetEasyTrainerOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
                "        if not modulesLoaded then return end",
                StringComparison.Ordinal))
        {
            initText = ReplaceOnce(
                initText,
                "    local function __gcetEasyTrainerOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
                "        if not modulesLoaded then return end",
                "    local function __gcetEasyTrainerOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
                "        if State.IsGCETDormant() then return end\n" +
                "        if not modulesLoaded then return end",
                "EasyTrainer dormant routed-action guard");
        }

        initText = ReplaceOnce(
            initText,
            "    Event.Observe(\"BaseProjectile\", \"ProjectileHit\", function(self, eventData)\n" +
            "        if modulesLoaded then",
            "    Event.Observe(\"BaseProjectile\", \"ProjectileHit\", function(self, eventData)\n" +
            "        if not State.IsGCETDormant() and modulesLoaded then",
            "EasyTrainer dormant projectile guard");

        initText = ReplaceOnce(
            initText,
            "    Event.ObserveAfter(\"LocomotionAirEvents\", \"OnEnter\", function(self, context, result)\n" +
            "        if modulesLoaded then",
            "    Event.ObserveAfter(\"LocomotionAirEvents\", \"OnEnter\", function(self, context, result)\n" +
            "        if not State.IsGCETDormant() and modulesLoaded then",
            "EasyTrainer dormant locomotion-air guard");

        initText = ReplaceOnce(
            initText,
            "    Event.ObserveAfter(\"MinimapContainerController\", \"OnCountdownTimerActiveUpdated\", function(_, _)\n" +
            "        if modulesLoaded then",
            "    Event.ObserveAfter(\"MinimapContainerController\", \"OnCountdownTimerActiveUpdated\", function(_, _)\n" +
            "        if not State.IsGCETDormant() and modulesLoaded then",
            "EasyTrainer dormant vehicle-timer guard");

        initText = ReplaceOnce(
            initText,
            "    Event.Override(\"LocomotionTransition\", \"WantsToDodge\", function(transition, stateContext, scriptInterface, wrappedFunc)\n" +
            "        if modulesLoaded then",
            "    Event.Override(\"LocomotionTransition\", \"WantsToDodge\", function(transition, stateContext, scriptInterface, wrappedFunc)\n" +
            "        if State.IsGCETDormant() then return wrappedFunc(stateContext, scriptInterface) end\n" +
            "        if modulesLoaded then",
            "EasyTrainer dormant dodge override bypass");

        initText = ReplaceOnce(
            initText,
            "    Event.Override(\"scannerDetailsGameController\", \"ShouldDisplayTwintoneTab\", function(this, wrappedMethod)\n" +
            "        if not modulesLoaded then return wrappedMethod() end",
            "    Event.Override(\"scannerDetailsGameController\", \"ShouldDisplayTwintoneTab\", function(this, wrappedMethod)\n" +
            "        if State.IsGCETDormant() or not modulesLoaded then return wrappedMethod() end",
            "EasyTrainer dormant scanner override bypass");

        context.Write(init, initText);

        return SemanticInjectionResult.Success(
            "Added an explicit user-controlled EasyTrainer hard-dormancy mode: frame update/draw lanes stop while backgrounded, gameplay hooks bypass trainer behavior, and the CET overlay exposes only a tiny wake control. Normal EasyTrainer behavior is unchanged until the user presses the background button.");
    }

}
