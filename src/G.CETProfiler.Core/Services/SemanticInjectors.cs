using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static class SemanticInjectors
{
    internal static SemanticInjectionResult Apply(
        SemanticCandidate candidate,
        SemanticPatchContext context)
    {
        if (context.OwnerAlreadyMarked())
            return SemanticInjectionResult.Skip(
                "A G-CET semantic marker for this rule already exists in the live/staged mod.");

        return candidate.RuleId switch
        {
            "combat" => ApplyCombat(context),
            "gta-joyride" => ApplyJoyRide(context),
            "repeatable-cyberpsychos" => ApplyRepeatableCyberpsychos(context),
            "cybertrials" => ApplyCyberTrials(context),
            "streetgamespoker" => ApplyStreetGamesPoker(context),
            "nightcitybilliards" => ApplyNightCityBilliards(context),
            "npcd-hotline" => ApplyNpcdHotline(context),
            "gameentityexaminertool" => ApplyGameEntityExaminer(context),
            "shift" => ApplyShift(context),
            "givemeeverything" => ApplyGiveMeEverything(context),
            "questrunner" => ApplyQuestRunner(context),
            "driveaerialvehicle" => ApplyDriveAerialVehicle(context),
            "roulette" => ApplyRoulette(context),
            "blackjack" => ApplyBlackjack(context),
            "illegal-mechanic" => ApplyIllegalMechanic(context),
            "alternative-midair-movement" => ApplyAlternativeMidairMovement(context),
            "metro-system" => ApplyMetroSystem(context),
            "nightcitypizza" => ApplyNightCityPizza(context),
            "overclocked-jenkins-tendons" => ApplyOverclockedJenkinsTendons(context),
            "songsdeck" => ApplySongsDeck(context),
            "dynamic-outfits-judy" => ApplyDynamicOutfitsJudy(context),
            "fov-sentinel" => ApplyFovSentinel(context),
            "fenix-mantis-blade" => ApplyFenixMantisBlade(context),
            "easytrainer" => ApplyEasyTrainer(context),
            "easytrainer-background-dormancy" => ApplyEasyTrainerBackgroundDormancy(context),
            "teleport-gateway-system" => ApplyTeleportGatewaySystem(context),
            "discard-ammo-on-reload" => ApplyDiscardAmmoOnReload(context),
            "give-craft-mat" => ApplyGiveCraftMat(context),
            "simple-notepad-cet" => ApplySimpleNotepadCet(context),
            "dedra-palmjet-quickslot" => ApplyDedraPalmjetQuickslot(context),
            "backstep-duo" => ApplyBackStepDuo(context),
            "nightcity-allies-missions" => ApplyNightCityAlliesMissions(context),
            "good-feelings" => ApplyGoodFeelings(context),
            "air-backflip" => ApplyAirBackFlip(context),
            "auto-drop-weapon-on-pickup-equip" => ApplyAutoDropWeaponOnPickupEquip(context),
            "drone-companions-revamp" => ApplyDroneCompanionsRevamp(context),
            "ghost-void-system" => ApplyGhostVoidSystem(context),
            "straight-edged-controls" => ApplyStraightEdgedControls(context),
            "immersive-head-inertia" => ApplyImmersiveHeadInertia(context),
            "advanced-settings" => ApplyAdvancedSettings(context),
            "auto-ammo-crafting" => ApplyAutoAmmoCrafting(context),
            "autoloot" => ApplyAutoLoot(context),
            "better-loot-markers" => ApplyBetterLootMarkers(context),
            "drivebus" => ApplyDriveBus(context),
            "quest-tracking-toggle" => ApplyQuestTrackingToggle(context),
            "sitanywhere" => ApplySitAnywhere(context),
            "repeatable-increased-criminal-activity" => ApplyRepeatableIncreasedCriminalActivity(context),
            "dedka-auto-shop" => ApplyDedkaAutoShop(context),
            "marmurbank" => ApplyMarmurBank(context),
            "immersive-third-person" => ApplyImmersiveThirdPerson(context),
            "immersivefirstperson" => ApplyImmersiveFirstPerson(context),
            "nativeinteractions" => ApplyNativeInteractions(context),
            "minimap-widgets" => ApplyMinimapWidgets(context),
            _ => SemanticInjectionResult.Skip(
                $"No semantic source injector is implemented for rule '{candidate.RuleId}'.")
        };
    }

    private static SemanticInjectionResult ApplyCombat(
        SemanticPatchContext context)
    {
        var enemy = context.FindFile(
            "Modules/EnemyBase.lua",
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\"",
            "throttledUpdateT1",
            "throttledUpdateT4");

        var enemyOpening =
            "---@param this PlayerPuppet\n" +
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\", function(this)\n";

        var enemyReplacement =
            "-- G-CET transition cache: IsInCombat is queried extremely often; the original\n" +
            "-- Combat body only needs to run when its combat/locomotion class changes.\n" +
            "local __gcetCombatEnemyBaseLastState = nil\n\n" +
            enemyOpening +
            "\tlocal __gcetInCombat = this.inCombat == true\n" +
            "\tlocal __gcetMovingHorizontally = __gcetInCombat and this:IsMovingHorizontally() == true or false\n" +
            "\tlocal __gcetMovingVertically = __gcetInCombat and this:IsMovingVertically() == true or false\n" +
            "\tlocal __gcetState = (__gcetInCombat and 4 or 0) + (__gcetMovingHorizontally and 1 or 0) + (__gcetMovingVertically and 2 or 0)\n" +
            "\tif __gcetState == __gcetCombatEnemyBaseLastState then return end\n" +
            "\t__gcetCombatEnemyBaseLastState = __gcetState\n";

        var enemyText = ReplaceOnce(
            enemy.Text,
            enemyOpening,
            enemyReplacement,
            "Combat EnemyBase IsInCombat observer");
        context.Write(enemy, enemyText);

        var immersive = context.FindFile(
            "Modules/Immersive Effect.lua",
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\"",
            "DisableCameraShakeState",
            "TweakDB:SetFlat");

        var immersiveOpening =
            "---@param this PlayerPuppet\n" +
            "ObserveAfter(\"PlayerPuppet\", \"IsInCombat\",function(this)\n";

        var immersiveReplacement =
            "-- G-CET transition cache: preserve the author's body, but execute it only\n" +
            "-- when locomotion or the two settings that affect its output change.\n" +
            "local __gcetCombatImmersiveLastState = nil\n\n" +
            immersiveOpening +
            "    local __gcetMovingHorizontally = this:IsMovingHorizontally() == true\n" +
            "    local __gcetMovingVertically = this:IsMovingVertically() == true\n" +
            "    local __gcetState = (__gcetMovingHorizontally and 1 or 0) + (__gcetMovingVertically and 2 or 0)\n" +
            "        + (Setting.DisableStunState and 4 or 0) + (Setting.DisableCameraShakeState and 8 or 0)\n" +
            "    if __gcetState == __gcetCombatImmersiveLastState then return end\n" +
            "    __gcetCombatImmersiveLastState = __gcetState\n";

        var immersiveText = ReplaceOnce(
            immersive.Text,
            immersiveOpening,
            immersiveReplacement,
            "Combat Immersive Effect IsInCombat observer");
        context.Write(immersive, immersiveText);

        return SemanticInjectionResult.Success(
            "Injected per-module transition guards around the two hot Combat IsInCombat observers; original Combat work remains unchanged behind the guards.");
    }

    private static SemanticInjectionResult ApplyJoyRide(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "SESSION.active",
            "pendingSpawns",
            "Cron.Update(deltaTime)");

        var opening = "__gcetRegisterEvent_157(\"onUpdate\", function(deltaTime)\n";
        if (!file.Text.Contains(opening, StringComparison.Ordinal))
        {
            opening = FindOnUpdateOpening(file.Text, "deltaTime");
        }

        var prefix =
            "local __gcetJoyRideElapsed = 0.0\n" +
            "local __gcetJoyRideWasBusy = false\n\n" +
            opening;

        var text = ReplaceOnce(
            file.Text,
            opening,
            prefix,
            "JoyRide onUpdate opening");

        var cron = "    Cron.Update(deltaTime)\n";
        var cadence =
            cron +
            "    __gcetJoyRideElapsed = __gcetJoyRideElapsed + deltaTime\n" +
            "    local __gcetJoyRideBusy = SESSION.active == true\n" +
            "        or (SESSION.state ~= nil and SESSION.state ~= \"idle\")\n" +
            "        or #pendingSpawns > 0\n" +
            "        or SESSION.npc ~= nil\n" +
            "        or SESSION.vehicle ~= nil\n" +
            "        or SESSION.camEntityID ~= nil\n" +
            "        or SESSION.camEntity ~= nil\n" +
            "        or SESSION.mountTimeoutTask ~= nil\n" +
            "        or (SESSION.gracePeriod or 0) > 0\n" +
            "    local __gcetJoyRideWake = __gcetJoyRideBusy and not __gcetJoyRideWasBusy\n" +
            "    __gcetJoyRideWasBusy = __gcetJoyRideBusy\n" +
            "    local __gcetJoyRideInterval = __gcetJoyRideBusy and 0.10 or 5.0\n" +
            "    if not __gcetJoyRideWake and __gcetJoyRideElapsed < __gcetJoyRideInterval then return end\n" +
            "    deltaTime = __gcetJoyRideElapsed\n" +
            "    __gcetJoyRideElapsed = 0.0\n";

        text = ReplaceOnce(
            text,
            cron,
            cadence,
            "JoyRide frame-fed Cron boundary");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept Cron frame-fed, added immediate idle-to-busy wake, 10 Hz active runtime and 0.2 Hz fully-idle runtime.");
    }

    private static SemanticInjectionResult ApplyStreetGamesPoker(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "StreetTables.update",
            "SeatController.update",
            "CardRenderer.update");

        var opening = "__gcetRegisterEvent_317(\"onUpdate\", function(dt)\n";
        if (!file.Text.Contains(opening, StringComparison.Ordinal))
            opening = FindOnUpdateOpening(file.Text, "dt");

        var state =
            "local __gcetPokerWorldElapsed = 0.0\n" +
            "local __gcetPokerSeatElapsed = 0.0\n\n" +
            opening;
        var text = ReplaceOnce(
            file.Text,
            opening,
            state,
            "StreetGamesPoker onUpdate opening");

        var oldBody =
            "    if not StreetGamesPoker.ready or inMenu or not inGame then return end\n" +
            "    Cron.Update(dt)\n" +
            "    SeatController.update(dt)\n" +
            "    StreetTables.update(dt)\n" +
            "    InteractionMenu.update()\n" +
            "    CardRenderer.update(dt)\n" +
            "    TableTalk.update(dt)\n";

        var newBody =
            "    if not StreetGamesPoker.ready or inMenu or not inGame then return end\n" +
            "    Cron.Update(dt)\n\n" +
            "    local __gcetPokerActive = SeatController.activeSpotId ~= nil\n" +
            "        or SeatController.forcedCam == true\n" +
            "        or StreetTables.activeSpotId ~= nil\n\n" +
            "    __gcetPokerWorldElapsed = __gcetPokerWorldElapsed + dt\n" +
            "    __gcetPokerSeatElapsed = __gcetPokerSeatElapsed + dt\n\n" +
            "    if __gcetPokerActive then\n" +
            "        SeatController.update(dt)\n" +
            "        __gcetPokerSeatElapsed = 0.0\n" +
            "    elseif __gcetPokerSeatElapsed >= 0.10 then\n" +
            "        SeatController.update(__gcetPokerSeatElapsed)\n" +
            "        __gcetPokerSeatElapsed = 0.0\n" +
            "    end\n\n" +
            "    local __gcetWorldInterval = __gcetPokerActive and 0.20 or 1.0\n" +
            "    if __gcetPokerWorldElapsed >= __gcetWorldInterval then\n" +
            "        StreetTables.update(__gcetPokerWorldElapsed)\n" +
            "        __gcetPokerWorldElapsed = 0.0\n" +
            "    end\n\n" +
            "    if InteractionMenu.visible then InteractionMenu.update() end\n" +
            "    if CardRenderer.shuffleActive == true or next(CardRenderer.motions) ~= nil or next(CardRenderer.flips) ~= nil then\n" +
            "        CardRenderer.update(dt)\n" +
            "    end\n" +
            "    if TableTalk.active or (TableTalk.clock or 0) > 0 then TableTalk.update(dt) end\n";

        text = ReplaceOnce(
            text,
            oldBody,
            newBody,
            "StreetGamesPoker callback body");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept Cron and active gameplay frame-responsive; seat discovery 10 Hz, table/world sweep 1 Hz idle / 5 Hz active, and renderer/UI work only while active.");
    }

    private static SemanticInjectionResult ApplyNightCityBilliards(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "ChoiceHub.update()",
            "runtime:update(dt)",
            "session:isActive");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetBilliardsIdleElapsed = 0.0\n\n" + opening,
            "NightCityBilliards onUpdate opening");

        var body =
            "    ChoiceHub.update()\n" +
            "    local runtime = NightCityBilliardsRuntime\n" +
            "    if runtime then runtime:update(dt) end\n";

        var replacement =
            "    ChoiceHub.update()\n" +
            "    local runtime = NightCityBilliardsRuntime\n" +
            "    if not runtime then return end\n" +
            "    local __gcetActive = runtime.session and runtime.session:isActive()\n" +
            "    if __gcetActive then\n" +
            "        __gcetBilliardsIdleElapsed = 0.0\n" +
            "        runtime:update(dt)\n" +
            "        return\n" +
            "    end\n" +
            "    __gcetBilliardsIdleElapsed = __gcetBilliardsIdleElapsed + dt\n" +
            "    if __gcetBilliardsIdleElapsed >= 0.20 then\n" +
            "        runtime:update(__gcetBilliardsIdleElapsed)\n" +
            "        __gcetBilliardsIdleElapsed = 0.0\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            body,
            replacement,
            "NightCityBilliards active/idle update body");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "ChoiceHub remains frame-fed; active billiards session updates every frame, idle table/runtime discovery updates at 5 Hz.");
    }

    private static SemanticInjectionResult ApplyRepeatableCyberpsychos(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local function runtimeTick()",
            "Mappins.sync",
            "Diagnostics.snapshot");

        var text = ReplaceOnce(
            file.Text,
            "local function runtimeTick()\n",
            "local function runtimeTick(includePresentation)\n",
            "Repeatable Cyberpsychos runtimeTick signature");

        var presentation =
            "    Mappins.sync(system, Sites)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n";

        var presentationGated =
            "    if includePresentation then\n" +
            "        Mappins.sync(system, Sites)\n" +
            "        Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            presentation,
            presentationGated,
            "Repeatable Cyberpsychos presentation lane");

        var oldUpdate =
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    if Mod.tickElapsed >= 1.0 then\n" +
            "        Mod.tickElapsed = 0.0\n" +
            "        local ok, err = pcall(runtimeTick)\n";

        var newUpdate =
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    Mod.presentationElapsed = (Mod.presentationElapsed or 5.0) + delta\n" +
            "    if Mod.tickElapsed >= 1.0 then\n" +
            "        Mod.tickElapsed = 0.0\n" +
            "        local includePresentation = Mod.presentationElapsed >= 5.0\n" +
            "        if includePresentation then Mod.presentationElapsed = 0.0 end\n" +
            "        local ok, err = pcall(runtimeTick, includePresentation)\n";

        text = ReplaceOnce(
            text,
            oldUpdate,
            newUpdate,
            "Repeatable Cyberpsychos runtime/presentation cadence");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved 1 Hz core runtime and 10 s scheduler; moved mappin/diagnostic presentation work to a 5 s lane.");
    }

    private static SemanticInjectionResult ApplyCyberTrials(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "hubs.setupMappins",
            "world.update()",
            "raceLogic.raceInProgress");

        var initText = ReplaceSemanticObserverBlockOnce(
            init.Text,
            "ObserveAfter",
            "BaseMappinBaseController",
            "UpdateRootState",
            "this",
            "",
            "CyberTrials global mappin refresh observer",
            "hubs.setupMappins(timeTrials.availableRaces)",
            "rootStateUpdateCount");

        var opening = "__gcetRegisterEvent_93(\"onUpdate\", function(dt)\n";
        if (!initText.Contains(opening, StringComparison.Ordinal))
            opening = FindOnUpdateOpening(initText, "dt");

        initText = ReplaceOnce(
            initText,
            opening,
            "local __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "local __gcetCyberTrialsLastRaceMappinState = nil\n" +
            "local __gcetCyberTrialsLastTrackCount = -1\n\n" +
            opening +
            "    local __gcetRaceMappinState = raceLogic.raceInProgress == true\n" +
            "    local __gcetTrackCount = #timeTrials.availableRaces\n" +
            "    if timeTrials.runtimeData.inGame and (__gcetRaceMappinState ~= __gcetCyberTrialsLastRaceMappinState or __gcetTrackCount ~= __gcetCyberTrialsLastTrackCount) then\n" +
            "        hubs.setupMappins(timeTrials.availableRaces)\n" +
            "        __gcetCyberTrialsLastRaceMappinState = __gcetRaceMappinState\n" +
            "        __gcetCyberTrialsLastTrackCount = __gcetTrackCount\n" +
            "    end\n" +
            "    __gcetCyberTrialsWorldElapsed = __gcetCyberTrialsWorldElapsed + dt\n",
            "CyberTrials onUpdate semantic state");

        var earlyWorldReplacement =
            "    if (timeTrials.runtimeData.inMenu or not timeTrials.runtimeData.inGame)\n" +
            "        and #timeTrials.availableRaces > 0 and #world.interactions > 0\n" +
            "        and __gcetCyberTrialsWorldElapsed >= 0.25 then\n" +
            "        interactionUI.update()\n" +
            "        world.update()\n" +
            "        __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "    end";

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^[ \t]*if\s+#timeTrials\.availableRaces\s*>\s*0\s+and\s+#world\.interactions\s*>\s*0\s+then[ \t]*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*interactionUI\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*world\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"^[ \t]*end\s*(?:--[^\r\n]*)?$",
            earlyWorldReplacement,
            "CyberTrials duplicated world/UI update");

        var gameplayWorldReplacement =
            "        Cron.Update(dt)\n" +
            "        interactionUI.update()\n" +
            "        local __gcetWorldRealtime = raceLogic.raceInProgress == true or readyToPlace == true\n" +
            "        if __gcetWorldRealtime or __gcetCyberTrialsWorldElapsed >= 0.10 then\n" +
            "            world.update()\n" +
            "            __gcetCyberTrialsWorldElapsed = 0.0\n" +
            "        end\n" +
            "        if raceLogic.raceInProgress == true then";

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^[ \t]*Cron\.Update\s*\(\s*dt\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*interactionUI\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*world\.update\s*\(\s*\)\s*(?:--[^\r\n]*)?\r?\n" +
            @"(?:^[ \t]*(?:--[^\r\n]*)?\r?\n)*" +
            @"^[ \t]*if\s+raceLogic\.raceInProgress\s*==\s*true\s+then\s*(?:--[^\r\n]*)?$",
            gameplayWorldReplacement,
            "CyberTrials gameplay world cadence");

        context.Write(init, initText);

        var hubsFile = context.FindFile(
            "modules/utils/interactionHubs.lua",
            "interactionHubs.setupMappins",
            "gamedataMappinVariant.Zzz18_RacingVariant",
            "mappinIDs",
            "raceLogic.raceInProgress",
            "RegisterMappin",
            "UnregisterMappin");

        var hubsText = RegexReplaceOnce(
            hubsFile.Text,
            @"(?m)^(?<indent>[ \t]*)(?<opening>function\s+interactionHubs\.setupMappins\s*\(\s*(?<tracks>[A-Za-z_]\w*)\s*\)\s*(?:--[^\r\n]*)?)$",
            "${indent}${opening}\n" +
            "${indent}    ${tracks} = ${tracks} or {}\n\n" +
            "${indent}    local function clearMappins()\n" +
            "${indent}        for index = #mappinIDs, 1, -1 do\n" +
            "${indent}            local mappin = mappinIDs[index]\n" +
            "${indent}            if mappin then Game.GetMappinSystem():UnregisterMappin(mappin) end\n" +
            "${indent}            mappinIDs[index] = nil\n" +
            "${indent}        end\n" +
            "${indent}    end\n\n" +
            "${indent}    if raceLogic.raceInProgress then\n" +
            "${indent}        if #mappinIDs > 0 then clearMappins() end\n" +
            "${indent}        return\n" +
            "${indent}    end\n\n" +
            "${indent}    if #mappinIDs == #${tracks} and #${tracks} > 0 then return end\n" +
            "${indent}    if #mappinIDs > 0 then clearMappins() end\n" +
            "${indent}    if #${tracks} == 0 then return end",
            "CyberTrials idempotent state-driven mappin registry");
        context.Write(hubsFile, hubsText);

        var world = context.FindFile(
            "modules/external/world.lua",
            "BaseMappinBaseController",
            "UpdateRootState",
            "world.interactions");

        var newObserver =
            "    ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\", function(this) -- Custom race pin texture\n" +
            "        local mappin = this:GetMappin()\n" +
            "        if not mappin or mappin:GetVariant() ~= gamedataMappinVariant.Zzz18_RacingVariant then return end\n" +
            "        local record = TweakDBInterface.GetUIIconRecord(\"ChoiceIcon.VehicleIcon\")\n" +
            "        this.iconWidget:SetAtlasResource(record:AtlasResourcePath())\n" +
            "        this.iconWidget:SetTexturePart(record:AtlasPartName())\n" +
            "        this.iconWidget:SetTintColor(HDRColor.new({ Red = 1, Green = 219 / 255, Blue = 78 / 255 }))\n" +
            "    end)\n";

        var worldText = ReplaceSemanticObserverBlockOnce(
            world.Text,
            "ObserveAfter",
            "BaseMappinBaseController",
            "UpdateRootState",
            "this",
            newObserver,
            "CyberTrials owned-mappin prefilter",
            "GetMappin",
            "GetWorldPosition",
            "world.interactions",
            "Vector4.Distance",
            "iconWidget");
        context.Write(world, worldText);

        return SemanticInjectionResult.Success(
            "Removed global mappin-driven hub refresh, made hub mappins state-driven and idempotent, gated world scans to 10 Hz outside realtime placement/race state, and added an owned-variant mappin prefilter.");
    }

    private static SemanticInjectionResult ApplyNpcdHotline(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "processTaskQueue()",
            "sms.processSmsQueue(delta)");

        var initText = ReplaceOnce(
            init.Text,
            "\tprocessTaskQueue()\n\tsms.processSmsQueue(delta)\n",
            "\tif queuedTasks.isTaskQueued then processTaskQueue() end\n" +
            "\tif sms.hasPending and sms.hasPending() then sms.processSmsQueue(delta) end\n",
            "NPCD exact pending-work gates");
        context.Write(init, initText);

        var sms = context.FindFile(
            "sms.lua",
            "local smsQueue = {}",
            "function sms.processSmsQueue(delta)");

        var smsText = ReplaceOnce(
            sms.Text,
            "local currTime, nextTime = 0, 0\nfunction sms.processSmsQueue(delta)\n",
            "local currTime, nextTime = 0, 0\n" +
            "function sms.hasPending()\n" +
            "\treturn smsQueue ~= nil and #smsQueue > 0\n" +
            "end\n\n" +
            "function sms.processSmsQueue(delta)\n",
            "NPCD SMS pending query");
        context.Write(sms, smsText);

        return SemanticInjectionResult.Success(
            "Skipped only the task/SMS workers when their queues are empty; all interaction/UI timing and the author's existing 0.5 s police/subscription core remain untouched.");
    }

    private static SemanticInjectionResult ApplyGameEntityExaminer(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "settings.autoFreeze",
            "GetComponentClosestToCrosshair",
            "Cron.Update(delta)");

        var opening = "__gcetRegisterEvent_125('onUpdate', function(delta)\n";
        if (!file.Text.Contains(opening, StringComparison.Ordinal))
            opening = FindOnUpdateOpening(file.Text, "delta");

        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetExaminerScanElapsed = 0.0\n\n" + opening,
            "GameEntityExaminerTool onUpdate opening");

        var boundary =
            "    if rcEnt and not rcEnt:IsVehicleRemoteControlled() then\n" +
            "        rcEnt = nil\n" +
            "    end\n" +
            "    if settings.autoFreeze == true then\n";

        var gatedBoundary =
            "    if rcEnt and not rcEnt:IsVehicleRemoteControlled() then\n" +
            "        rcEnt = nil\n" +
            "    end\n" +
            "    local __gcetScanActive = settings.autoFreeze == true\n" +
            "        or settings.buttonsOn == true\n" +
            "        or settings.highlightEnts == true\n" +
            "        or cetopen == true\n" +
            "    -- Entity-target hotkeys consume the cached ent/entID even with the\n" +
            "    -- overlay/scanner hidden, so the dormant lane must keep a low-rate\n" +
            "    -- target refresh instead of going fully asleep.\n" +
            "    __gcetExaminerScanElapsed = __gcetExaminerScanElapsed + delta\n" +
            "    local __gcetScanInterval = __gcetScanActive and (1 / 30) or 0.20\n" +
            "    if __gcetExaminerScanElapsed < __gcetScanInterval then return end\n" +
            "    delta = __gcetExaminerScanElapsed\n" +
            "    __gcetExaminerScanElapsed = 0.0\n" +
            "    if settings.autoFreeze == true then\n";

        text = ReplaceOnce(
            text,
            boundary,
            gatedBoundary,
            "GameEntityExaminerTool active scan boundary");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept Cron and remote-control vehicle following frame-responsive; scanner/entity examination reconciles at 30 Hz while active and retains a 5 Hz dormant target-refresh lane for hotkey correctness.");
    }


    private static SemanticInjectionResult ApplyGiveMeEverything(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "tabs/teleport.lua",
            "BaseMappinBaseController",
            "UpdateRootState",
            "CustomPositionVariant",
            "GetWorldPosition");

        var pattern =
            @"(?m)^(?<opening>\s*Observe\s*\(\s*[""']BaseMappinBaseController[""']\s*,\s*[""']UpdateRootState[""']\s*,\s*function\s*\(\s*self\s*\)\s*)$";

        var replacement =
            "${opening}\n" +
            "        -- G-CET semantic ownership prefilter: this observer is global but\n" +
            "        -- GiveMeEverything only owns the custom waypoint variant.\n" +
            "        local __gcetMappin = self:GetMappin()\n" +
            "        if not __gcetMappin or __gcetMappin:GetVariant() ~= gamedataMappinVariant.CustomPositionVariant then return end";

        var text = RegexReplaceOnce(
            file.Text,
            pattern,
            replacement,
            "GiveMeEverything custom waypoint mappin prefilter");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Rejected unrelated BaseMappin UpdateRootState callbacks before GiveMeEverything performs custom-waypoint world/native work.");
    }

    private static SemanticInjectionResult ApplyQuestRunner(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Manager:update(dt)",
            "Spawner.Update(dt)",
            "Cron.Update(dt)",
            "Runner.gameState");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetQuestRunnerManagerElapsed = 0.0\n" +
            "local __gcetQuestRunnerSpawnerElapsed = 0.0\n\n" +
            opening,
            "QuestRunner cadence declarations");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Manager:update\(dt\)\s*$",
            "${indent}if Manager.current then\n" +
            "${indent}\t__gcetQuestRunnerManagerElapsed = 0.0\n" +
            "${indent}\tManager:update(dt)\n" +
            "${indent}else\n" +
            "${indent}\t__gcetQuestRunnerManagerElapsed = __gcetQuestRunnerManagerElapsed + (tonumber(dt) or 0)\n" +
            "${indent}\tif __gcetQuestRunnerManagerElapsed >= 0.20 then\n" +
            "${indent}\t\tlocal __gcetElapsed = __gcetQuestRunnerManagerElapsed\n" +
            "${indent}\t\t__gcetQuestRunnerManagerElapsed = 0.0\n" +
            "${indent}\t\tManager:update(__gcetElapsed)\n" +
            "${indent}\tend\n" +
            "${indent}end",
            "QuestRunner active/idle Manager lane");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Spawner\.Update\(dt\)\s*$",
            "${indent}__gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed + (tonumber(dt) or 0)\n" +
            "${indent}if __gcetQuestRunnerSpawnerElapsed >= 0.25 then\n" +
            "${indent}\tlocal __gcetElapsed = __gcetQuestRunnerSpawnerElapsed\n" +
            "${indent}\t__gcetQuestRunnerSpawnerElapsed = 0.0\n" +
            "${indent}\tSpawner.Update(__gcetElapsed)\n" +
            "${indent}end",
            "QuestRunner Spawner feed lane");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved active QuestRunner Manager frame cadence, moved idle Manager refresh to 5 Hz, and fed Spawner at 4 Hz with accumulated delta.");
    }

    private static SemanticInjectionResult ApplyDriveAerialVehicle(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "DAV.core_obj.av_obj.engine_obj:Update(delta)",
            "Def.Situation.Idle",
            "Cron.Update(delta)");

        var initText = RegexReplaceOnce(
            init.Text,
            @"(?m)^(?<indent>\s*)DAV\.core_obj\.av_obj\.engine_obj:Update\(delta\)\s*$",
            "${indent}local __gcetEvent = DAV.core_obj.event_obj\n" +
            "${indent}local __gcetSituation = __gcetEvent and __gcetEvent.current_situation or Def.Situation.Idle\n" +
            "${indent}if __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal then\n" +
            "${indent}\tDAV.core_obj.av_obj.engine_obj:Update(delta)\n" +
            "${indent}end",
            "DriveAerialVehicle realtime physics state gate");
        context.Write(init, initText);

        var core = context.FindFile(
            "Modules/core.lua",
            "Cron.Every(DAV.time_resolution",
            "self.event_obj:CheckAllEvents()",
            "self:GetActions()",
            "Def.Situation.Normal");

        var pattern =
            @"Cron\.Every\(DAV\.time_resolution\s*,\s*function\s*\(\s*\)\s*" +
            @"self\.event_obj:CheckAllEvents\(\)\s*" +
            @"self:GetActions\(\)\s*" +
            @"end\s*\)";

        var replacement =
            "local __gcetIdleEventTick = 0\n" +
            "    Cron.Every(DAV.time_resolution, function()\n" +
            "        local __gcetSituation = self.event_obj.current_situation\n" +
            "        local __gcetActiveVehicleState = __gcetSituation ~= Def.Situation.Idle and __gcetSituation ~= Def.Situation.Normal\n" +
            "        if __gcetActiveVehicleState then\n" +
            "            __gcetIdleEventTick = 0\n" +
            "            self.event_obj:CheckAllEvents()\n" +
            "        elseif __gcetSituation == Def.Situation.Normal then\n" +
            "            __gcetIdleEventTick = __gcetIdleEventTick + 1\n" +
            "            if __gcetIdleEventTick >= 8 then\n" +
            "                __gcetIdleEventTick = 0\n" +
            "                self.event_obj:CheckAllEvents()\n" +
            "            end\n" +
            "        end\n" +
            "        if __gcetActiveVehicleState or not self.queue_obj:IsEmpty() then\n" +
            "            self:GetActions()\n" +
            "        end\n" +
            "    end)";

        var coreText = RegexReplaceOnce(
            core.Text,
            pattern,
            replacement,
            "DriveAerialVehicle active/normal event cadence");
        context.Write(core, coreText);

        return SemanticInjectionResult.Success(
            "Kept flight/landing/waiting/takeoff physics and event checks realtime, stopped physics in Normal/Idle, slowed Normal reconciliation, and preserved immediate queued actions.");
    }

    private static SemanticInjectionResult ApplyRoulette(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "SpotManager.update",
            "Cron.Update",
            "TableManager.GetActiveTable",
            "RouletteMainMenu.Update");

        var opening = FindOnUpdateOpening(init.Text, "dt");
        var initText = ReplaceOnce(
            init.Text,
            opening,
            "local __gcetRouletteIdleElapsed = 0.0\n\n" +
            opening +
            "    local __gcetFrameDt = math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "    local __gcetRealtime = SpotManager.IsPlayerInSpot() or TableManager.GetActiveTable() ~= nil\n" +
            "    if not __gcetRealtime then\n" +
            "        __gcetRouletteIdleElapsed = __gcetRouletteIdleElapsed + __gcetFrameDt\n" +
            "        if __gcetRouletteIdleElapsed < 0.10 then return end\n" +
            "        dt = __gcetRouletteIdleElapsed\n" +
            "        __gcetRouletteIdleElapsed = 0.0\n" +
            "    else\n" +
            "        __gcetRouletteIdleElapsed = 0.0\n" +
            "    end\n",
            "Roulette active/idle runtime gate");
        context.Write(init, initText);

        ApplyVisibleSpotMappinPrefilter(
            context,
            "SpotManager.lua",
            "roulette");

        return SemanticInjectionResult.Success(
            "Restored frame cadence while a roulette table is active, reduced far/idle runtime to 10 Hz with accumulated delta, and rejected global mappin callbacks when no roulette prompt is visible.");
    }

    private static SemanticInjectionResult ApplyBlackjack(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "SpotManager.update",
            "Cron.Update",
            "BlackjackMainMenu.Update",
            "SpotManager.IsPlayerInSpot");

        var opening = FindOnUpdateOpening(init.Text, "dt");
        var initText = ReplaceOnce(
            init.Text,
            opening,
            "local __gcetBlackjackIdleElapsed = 0.0\n\n" +
            opening +
            "    local __gcetFrameDt = math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "    if not SpotManager.IsPlayerInSpot() then\n" +
            "        __gcetBlackjackIdleElapsed = __gcetBlackjackIdleElapsed + __gcetFrameDt\n" +
            "        if __gcetBlackjackIdleElapsed < 0.05 then return end\n" +
            "        dt = __gcetBlackjackIdleElapsed\n" +
            "        __gcetBlackjackIdleElapsed = 0.0\n" +
            "    else\n" +
            "        __gcetBlackjackIdleElapsed = 0.0\n" +
            "    end\n",
            "Blackjack active/idle runtime gate");
        context.Write(init, initText);

        ApplyVisibleSpotMappinPrefilter(
            context,
            "SpotManager.lua",
            "blackjack");

        return SemanticInjectionResult.Success(
            "Preserved frame cadence while seated at blackjack, reduced idle/proximity runtime to 20 Hz with accumulated delta, and rejected global mappin callbacks when no blackjack prompt is visible.");
    }

    private static void ApplyVisibleSpotMappinPrefilter(
        SemanticPatchContext context,
        string preferredFile,
        string label)
    {
        var file = context.FindFile(
            preferredFile,
            "BaseMappinBaseController",
            "UpdateRootState",
            "SpotManager.spots",
            "spot_showingInteractUI");

        var pattern =
            @"(?m)^(?<opening>\s*ObserveAfter\s*\(\s*[""']BaseMappinBaseController[""']\s*,\s*[""']UpdateRootState[""']\s*,\s*function\s*\(\s*this\s*\)[^\r\n]*)$";

        var replacement =
            "${opening}\n" +
            "        local __gcetVisiblePrompt = false\n" +
            "        for _, __gcetSpotTable in pairs(SpotManager.spots) do\n" +
            "            if __gcetSpotTable.spotObject and __gcetSpotTable.spotObject.spot_showingInteractUI then\n" +
            "                __gcetVisiblePrompt = true\n" +
            "                break\n" +
            "            end\n" +
            "        end\n" +
            "        if not __gcetVisiblePrompt then return end";

        var text = RegexReplaceOnce(
            file.Text,
            pattern,
            replacement,
            label + " visible-prompt mappin prefilter");
        context.Write(file, text);
    }



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

    private static SemanticInjectionResult ApplyMetroSystem(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "ts.entrySys:update()",
            "ts.stationSys:update(deltaTime)",
            "ts.Cron.Update(deltaTime)",
            "ts.hud.draw(ts)");

        var entry = context.FindFile(
            "modules/entrySystem.lua",
            "function entrySys:getClosestEntry",
            "self.entries",
            "looksAtEntry",
            "waypointPosition");

        var hud = context.FindFile(
            "modules/ui/hud.lua",
            "function hud.draw(ts)",
            "hud.destVisible",
            "observers.nextStationPoint",
            "drawDestinations");

        var metroOpening = FindOnUpdateOpening(init.Text, "deltaTime");
        var initText = ReplaceOnce(
            init.Text,
            metroOpening,
            "local __gcetMetroIdleElapsed = 0.0\n\n" + metroOpening,
            "Metro System idle cadence state");

        initText = RegexReplaceOnce(
            initText,
            @"(?m)^(?<indent>\s*)if\s+\(not\s+ts\.runtimeData\.inMenu\)\s+and\s+ts\.runtimeData\.inGame\s+and\s+\(math\.floor\(observers\.timeDilation\)\s*~=\s*0\).*then\s*$",
            "${indent}if (not ts.runtimeData.inMenu) and ts.runtimeData.inGame and (math.floor(observers.timeDilation) ~= 0) and ts.archiveInstalled and ts.axlInstalled and ts.cwInstalled then\n" +
            "${indent}    local __gcetMetroActive = ts.runtimeData.cetOpen == true\n" +
            "${indent}        or ts.entrySys.forceRunCron == true\n" +
            "${indent}        or ts.stationSys.currentStation ~= nil\n" +
            "${indent}        or ts.stationSys.activeTrain ~= nil\n" +
            "${indent}        or ts.observers.noSave == true\n" +
            "${indent}        or ts.observers.noTrains == true\n" +
            "${indent}        or ts.input.interactKey == true\n" +
            "${indent}    if __gcetMetroActive then\n" +
            "${indent}        __gcetMetroIdleElapsed = 0.0\n" +
            "${indent}    else\n" +
            "${indent}        __gcetMetroIdleElapsed = __gcetMetroIdleElapsed + math.max(tonumber(deltaTime) or 0.0, 0.0)\n" +
            "${indent}        if __gcetMetroIdleElapsed < 0.20 then return end\n" +
            "${indent}        deltaTime = __gcetMetroIdleElapsed\n" +
            "${indent}        __gcetMetroIdleElapsed = 0.0\n" +
            "${indent}    end",
            "Metro System active/idle world cadence");
        context.Write(init, initText);

        var entryText = RegexReplaceOnce(
            entry.Text,
            @"(?m)^(?<opening>\s*function\s+entrySys:getClosestEntry\s*\(\s*\)\s*)$",
            "${opening}\n" +
            "    local __gcetPlayer = GetPlayer()\n" +
            "    if __gcetPlayer == nil then return nil end\n" +
            "    local __gcetPlayerPos = __gcetPlayer:GetWorldPosition()",
            "Metro System closest entry player cache");

        entryText = RegexReplaceOnce(
            entryText,
            @"utils\.distanceVector\s*\(\s*GetPlayer\(\):GetWorldPosition\(\)\s*,\s*v\.center\s*\)",
            "utils.distanceVector(__gcetPlayerPos, v.center)",
            "Metro System entry sweep cached player position");
        context.Write(entry, entryText);

        var hudText = RegexReplaceOnce(
            hud.Text,
            @"(?m)^(?<indent>\s*)destVisible\s*=\s*false\s*,\s*$",
            "${indent}destVisible = false,\n" +
            "${indent}destinationWasVisible = false,",
            "Metro System destination visibility state");

        hudText = RegexReplaceOnce(
            hudText,
            @"(?ms)^(?<indent>\s*)if\s+hud\.destVisible\s+then\s*\r?\n\s*hud\.drawDestinations\s*\(\s*ts\.stationSys\s*\)\s*\r?\n\s*hud\.destVisible\s*=\s*false\s*\r?\n\s*else\s*\r?\n\s*observers\.nextStationText\s*=\s*[""'][""']\s*\r?\n\s*Game\.GetMappinSystem\(\):UnregisterMappin\s*\(\s*observers\.nextStationPoint\s*\)\s*\r?\n\s*if\s+ts\.observers\.hudText\s+then\s*\r?\n\s*ts\.observers\.hudText:SetVisible\s*\(\s*false\s*\)\s*\r?\n\s*end\s*\r?\n\s*end\s*$",
            "${indent}if hud.destVisible then\n" +
            "${indent}    hud.drawDestinations(ts.stationSys)\n" +
            "${indent}    hud.destVisible = false\n" +
            "${indent}    hud.destinationWasVisible = true\n" +
            "${indent}elseif hud.destinationWasVisible or observers.nextStationPoint ~= nil or observers.nextStationText ~= \"\" then\n" +
            "${indent}    observers.nextStationText = \"\"\n" +
            "${indent}    if observers.nextStationPoint ~= nil then\n" +
            "${indent}        Game.GetMappinSystem():UnregisterMappin(observers.nextStationPoint)\n" +
            "${indent}        observers.nextStationPoint = nil\n" +
            "${indent}    end\n" +
            "${indent}    if ts.observers.hudText then\n" +
            "${indent}        ts.observers.hudText:SetVisible(false)\n" +
            "${indent}    end\n" +
            "${indent}    hud.destinationWasVisible = false\n" +
            "${indent}end",
            "Metro System destination cleanup transition");
        context.Write(hud, hudText);

        return SemanticInjectionResult.Success(
            "Kept station/train/elevator activity frame-responsive, reduced idle world discovery to 5 Hz with accumulated delta, cached one player position per entry sweep, and made destination mappin teardown transition-driven.");
    }

    private static SemanticInjectionResult ApplyNightCityPizza(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "tickBossTexts",
            "Pizza.ctx",
            "clockPhoneAcc",
            "TakeClockIn");

        var opening = FindOnUpdateOpening(file.Text, "delta");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetPizzaMaintenanceElapsed = 0.0\n\n" + opening,
            "NightCityPizza maintenance cadence state");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)tickBossTexts\s*\(\s*delta\s*\).*$",
            "${indent}__gcetPizzaMaintenanceElapsed = __gcetPizzaMaintenanceElapsed + math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "${indent}if __gcetPizzaMaintenanceElapsed < 0.40 then return end\n" +
            "${indent}local __gcetPizzaElapsed = __gcetPizzaMaintenanceElapsed\n" +
            "${indent}__gcetPizzaMaintenanceElapsed = 0.0\n" +
            "${indent}tickBossTexts(__gcetPizzaElapsed)",
            "NightCityPizza global maintenance gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)Pizza\.clockPhoneAcc\s*=\s*\(Pizza\.clockPhoneAcc\s+or\s+0\)\s*\+\s*\(delta\s+or\s+0\)\s*$",
            "${indent}Pizza.clockPhoneAcc = 0",
            "NightCityPizza retired frame accumulator");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>\s*)if\s+Pizza\.clockPhoneAcc\s*>=\s*0\.4\s+then\s*$",
            "${indent}do",
            "NightCityPizza 0.4 second maintenance body");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Collapsed the global/off-shift maintenance loop to the author's existing 0.4 s phone cadence, feeding accumulated delta to long-period Boss texts while leaving the active delivery ctx:onUpdate state machine untouched.");
    }



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

    private static SemanticInjectionResult ApplySongsDeck(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Override(\"PlayerPuppet\", \"OnAction\"",
            "StopDeviceControl",
            "VisionHold",
            "IconicCyberware",
            "BlackwallUpload.Execute");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<indent>\s*)Override\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*this\s*,\s*action\s*,\s*consumer\s*,\s*wrappedMethod\s*\)\s*\r?\n\s*local\s+actionName\s*=\s*Game\.NameToString\s*\(\s*ListenerAction\.GetName\s*\(\s*action\s*\)\s*\)\s*\r?\n\s*local\s+actionType\s*=\s*ListenerAction\.GetType\s*\(\s*action\s*\)\s*$",
            "${indent}local __gcetSongsDeckActions = {\n" +
            "${indent}  StopDeviceControl = true,\n" +
            "${indent}  VisionHold = true,\n" +
            "${indent}  IconicCyberware = true,\n" +
            "${indent}  MeleeBlock = true,\n" +
            "${indent}  RangedAttack = true,\n" +
            "${indent}  MeleeAttack = true\n" +
            "${indent}}\n" +
            "${indent}Override(\"PlayerPuppet\", \"OnAction\", function(this, action, consumer, wrappedMethod)\n" +
            "${indent}  local actionName = Game.NameToString(ListenerAction.GetName(action))\n" +
            "${indent}  if not actionLog and not __gcetSongsDeckActions[actionName] then\n" +
            "${indent}    return wrappedMethod(action, consumer)\n" +
            "${indent}  end\n" +
            "${indent}  local actionType = ListenerAction.GetType(action)",
            "SongsDeck exact override prefilter");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept SongsDeck's PlayerPuppet Override and consume/return semantics intact, but returns directly to wrappedMethod for every action outside the six source-proven names; actionLog still preserves all-action diagnostics when explicitly enabled.");
    }

    private static SemanticInjectionResult ApplyDynamicOutfitsJudy(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "DynamicFramework.isGameLoaded",
            "ScriptedPuppet",
            "PrefetchAppearanceChange",
            "ScheduleAppearanceChange",
            "DynamicFramework:GetActiveShieldInfo");

        var opening = FindOnUpdateOpening(file.Text, "dt");
        var text = ReplaceOnce(
            file.Text,
            opening,
            "local __gcetDynamicIdleElapsed = 0.0\n\n" + opening,
            "Dynamic Outfits Judy idle cadence state");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^(?<indent>[ \t]*)local\s+srh\s*=\s*__gcetGetSystemRequestsHandler\s*\(\s*\)\s*\r?\n\s*if\s+not\s+srh\s+or\s+srh:IsPreGame\s*\(\s*\)\s+then\s*\r?\n\s*DynamicFramework\.isGameLoaded\s*=\s*false\s*\r?\n\s*return\s*\r?\n\s*end\s*\r?\n\s*\r?\n\s*local\s+player\s*=\s*__gcetGetPlayer\s*\(\s*\)\s*$",
            "${indent}local srh = __gcetGetSystemRequestsHandler()\n" +
            "${indent}if not srh or srh:IsPreGame() then\n" +
            "${indent}    DynamicFramework.isGameLoaded = false\n" +
            "${indent}    return\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetAnySpawned = false\n" +
            "${indent}for _, __gcetCharState in pairs(DynamicFramework.states) do\n" +
            "${indent}    if __gcetCharState.is_spawned and __gcetCharState.npc_entity then\n" +
            "${indent}        __gcetAnySpawned = true\n" +
            "${indent}        break\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if DynamicFramework.isGameLoaded and not __gcetAnySpawned then\n" +
            "${indent}    __gcetDynamicIdleElapsed = __gcetDynamicIdleElapsed + math.max(tonumber(dt) or 0.0, 0.0)\n" +
            "${indent}    if __gcetDynamicIdleElapsed < 0.25 then return end\n" +
            "${indent}    dt = __gcetDynamicIdleElapsed\n" +
            "${indent}    __gcetDynamicIdleElapsed = 0.0\n" +
            "${indent}else\n" +
            "${indent}    __gcetDynamicIdleElapsed = 0.0\n" +
            "${indent}end\n\n" +
            "${indent}local player = __gcetGetPlayer()",
            "Dynamic Outfits Judy event-woken idle lane");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved full frame-rate scene/appearance processing whenever a managed NPC is attached, while event-driven OnGameAttached/OnDetach state drops the unspawned framework to 4 Hz with accumulated delta; first-load/session initialization still bypasses the idle gate.");
    }

    private static SemanticInjectionResult ApplyFovSentinel(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "FindVehicleCameraManager",
            "##FakeWidget",
            "local ac=a9()",
            "ad(a9(),true,0)");

        var text = ReplaceOnce(
            file.Text,
            "local aA=ad(a9(),true,0)",
            "local aA=ad(ac,true,0)",
            "FOV Sentinel duplicate TPP camera lookup");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Reused the already-computed TPP camera-state boolean inside the widget path instead of repeating FindVehicleCameraManager through a second a9() call; rendering cadence and visibility semantics are unchanged.");
    }

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
            @"(?ms)^(?<indent>[ \t]*)local\s+player\s*=\s*Game\.GetPlayer\s*\(\s*\)\s*\r?\n\s*if\s+not\s+player\s+or\s+not\s+player:IsAttached\s*\(\s*\)\s+then\s+return\s+end\s*\r?\n\s*\r?\n\s*--\s*DYNAMIC RESET:[^\r\n]*\r?\n\s*if\s+not\s+IsHighEnough\s*\(\s*player\s*\)\s+and\s+not\s+isStuck\s+then\s*\r?\n\s*jumpCount\s*=\s*0\s*\r?\n\s*end\s*\r?\n\s*\r?\n\s*--\s*STAGE 1:[^\r\n]*\r?\n\s*if\s+not\s+isHangPressed\s+and\s+not\s+isStuck\s+then\s+return\s+end\s*$",
            "${indent}-- G-CET semantic idle guard: Jump OnAction already resets jumpCount on ground,\n" +
            "${indent}-- so there is no reason to fetch the player and raycast every frame while inactive.\n" +
            "${indent}if not isHangPressed and not isStuck then return end\n\n" +
            "${indent}local player = Game.GetPlayer()\n" +
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

    private static SemanticInjectionResult ApplyTeleportGatewaySystem(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "TeleportGatewaySystem",
            "playerPos = TGS.player:GetWorldPosition()",
            "GWDist = math.sqrt",
            "TGS.player:GetWorldPosition().x-gatewayDB[index].gwx",
            "TGS.teleportFac:Teleport",
            "TGS.showMainWindow");

        var text = ReplaceOnce(
            file.Text,
            "GWDist = math.sqrt(((TGS.player:GetWorldPosition().x-gatewayDB[index].gwx)^2)+((TGS.player:GetWorldPosition().y-gatewayDB[index].gwy)^2)+((TGS.player:GetWorldPosition().z-gatewayDB[index].gwz)^2))",
            "GWDist = math.sqrt(((playerPos.x-gatewayDB[index].gwx)^2)+((playerPos.y-gatewayDB[index].gwy)^2)+((playerPos.z-gatewayDB[index].gwz)^2))",
            "Teleport Gateway per-gateway position reuse");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)$",
            "${opening}\n\tif not TGS.showMainWindow then return end",
            "Teleport Gateway hidden-window draw gate");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved every-frame gateway detection and the original state machine/cooldown behavior, reused the already-read player position inside the gateway loop, and skipped ImGui work while the gateway window is hidden.");
    }

    private static SemanticInjectionResult ApplyDiscardAmmoOnReload(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local swapActions",
            "local nonSwapActions",
            "Observe('PlayerPuppet','OnAction'",
            "ReloadSystem.weaponSwap");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\s*\(\s*self\s*,\s*action\s*\)\s*\r?\n\s*local\s+actionName\s*=\s*Game\.NameToString\s*\(\s*action:GetName\s*\(\s*\)\s*\)\s*\r?\n\s*if\s*\(\s*ReloadSystem\.weaponSwap\s*\)\s*then\s*\r?\n\s*if\s*\(\s*Utility\.filter\s*\(\s*nonSwapActions\s*,\s*actionName\s*\)\s*\)\s*then\s*\r?\n\s*ReloadSystem\.weaponSwap\s*=\s*false\s*\r?\n\s*end\s*\r?\n\s*elseif\s*\(\s*Utility\.filter\s*\(\s*swapActions\s*,\s*actionName\s*\)\s*\)\s*then\s*\r?\n\s*ReloadSystem\.weaponSwap\s*=\s*true\s*\r?\n\s*end\s*\r?\n\s*end\s*\)\s*$",
            "${indent}local function __gcetDiscardAmmoOnAction(self, action, __gcetConsumer, __gcetRoutedName)\n" +
            "${indent}    local actionName = __gcetRoutedName or Game.NameToString(action:GetName())\n" +
            "${indent}    if ReloadSystem.weaponSwap then\n" +
            "${indent}        if Utility.filter(nonSwapActions, actionName) then\n" +
            "${indent}            ReloadSystem.weaponSwap = false\n" +
            "${indent}        end\n" +
            "${indent}    elseif Utility.filter(swapActions, actionName) then\n" +
            "${indent}        ReloadSystem.weaponSwap = true\n" +
            "${indent}    end\n" +
            "${indent}end\n\n" +
            "${indent}local __gcetDiscardAmmoRouted = false\n" +
            "${indent}local __gcetDiscardAmmoHandles = {}\n" +
            "${indent}local __gcetDiscardAmmoOk, __gcetDiscardAmmoEngine = pcall(GetMod, \"0-Engine\")\n" +
            "${indent}local __gcetDiscardAmmoApi = __gcetDiscardAmmoEngine\n" +
            "${indent}if __gcetDiscardAmmoOk and type(__gcetDiscardAmmoEngine) == \"table\" and type(__gcetDiscardAmmoEngine.GCET) == \"table\" then __gcetDiscardAmmoApi = __gcetDiscardAmmoEngine.GCET end\n" +
            "${indent}if __gcetDiscardAmmoOk and type(__gcetDiscardAmmoApi) == \"table\" and type(__gcetDiscardAmmoApi.SubscribeAction) == \"function\" then\n" +
            "${indent}    __gcetDiscardAmmoRouted = pcall(function()\n" +
            "${indent}        __gcetDiscardAmmoHandles[#__gcetDiscardAmmoHandles + 1] = __gcetDiscardAmmoApi.SubscribeAction({\n" +
            "${indent}            id = \"G-CET.Semantic.DiscardAmmoOnReload\",\n" +
            "${indent}            actions = { \"PreviousWeapon\", \"NextWeapon\", \"WeaponSlot1\", \"WeaponSlot2\", \"WeaponSlot3\", \"WeaponWheel\", \"RangedAttack\", \"MeleeAttack\" },\n" +
            "${indent}            decodeType = false\n" +
            "${indent}        }, __gcetDiscardAmmoOnAction, \"DiscardAmmoOnReload\")\n" +
            "${indent}    end)\n" +
            "${indent}    if not __gcetDiscardAmmoRouted then\n" +
            "${indent}        for _, __gcetHandle in ipairs(__gcetDiscardAmmoHandles) do\n" +
            "${indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end\n" +
            "${indent}        end\n" +
            "${indent}    end\n" +
            "${indent}end\n" +
            "${indent}if not __gcetDiscardAmmoRouted then Observe('PlayerPuppet','OnAction', function(self, action) __gcetDiscardAmmoOnAction(self, action, nil, nil) end) end",
            "Discard Ammo exact action routing");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Routed only the eight source-proven weapon-swap/reset actions through 0-Engine while preserving the original ReloadSystem state transitions and direct Observe fallback.");
    }

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

    private static SemanticInjectionResult ApplyDedraPalmjetQuickslot(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function installPalmjetHooks",
            "isUseCombatGadgetPress",
            "UseCombatGadget",
            "BUTTON_PRESSED",
            "activateSelectedPalmjet");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetDedraPalmjetOnAction",
            "DedraPalmjetQuickslot",
            new[] { "UseCombatGadget" },
            "DedraPalmjetQuickslot PlayerPuppet OnAction",
            "isUseCombatGadgetPress",
            "getActivePalmjetVariant",
            "activateSelectedPalmjet");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed Dedra Palmjet's single source-proven UseCombatGadget action through 0-Engine while retaining the original BUTTON_PRESSED and active-variant gates.");
    }

    private static SemanticInjectionResult ApplyBackStepDuo(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "processDirectionAction",
            "getActionName",
            "getActionType",
            "getActionValue",
            "BUTTON_PRESSED",
            "movey",
            "movex");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetBackStepDuoOnAction",
            "BackStepDuo",
            new[] { "Forward", "Back", "Left", "Right", "MoveY", "MoveX" },
            "BackStepDuo PlayerPuppet OnAction",
            "settings.EnableDash",
            "getActionName",
            "getActionType",
            "getActionValue",
            "processDirectionAction");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed BackStepDuo's six source-proven movement actions through 0-Engine; ability, BUTTON_PRESSED, value and double-tap semantics remain in the original callback body.");
    }

    private static SemanticInjectionResult ApplyNightCityAlliesMissions(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function NCA_Missions:SaveMissions",
            "function NCA_Missions:LoadMissions",
            "CargoHeist.Update(dt, db)",
            "BountySystem.UpdateRadar(db)",
            "NCA_MissionDB");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>[ \t]*function\s+NCA_Missions:SaveMissions\s*\(\s*db\s*\)\s*)\r?\n",
            "${opening}\n    NCA_MissionDB = db\n",
            "NCA Missions SaveMissions cache synchronization");

        text = RegexReplaceOnce(
            text,
            @"(?m)^[ \t]*db\.bmi\s*=\s*db\.bmi\s*or\s*0\s*\r?\n(?:[ \t]*\r?\n)?[ \t]*return\s+db\s*$",
            "    db.bmi = db.bmi or 0\n    NCA_MissionDB = db\n    return db",
            "NCA Missions LoadMissions cache synchronization");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<header>[ \t]*--\s*2\.[^\r\n]*\r?\n)(?<comment>[ \t]*--[^\r\n]*\r?\n)(?<indent>[ \t]*)local\s+db\s*=\s*NCA_Missions:LoadMissions\s*\(\s*\)\s*$",
            "${header}${comment}${indent}local db = NCA_MissionDB or NCA_Missions:LoadMissions()",
            "NCA Missions per-frame mission database load");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept Cargo Heist, bounty and mission timers at the author's original cadence while making the already-global mission table authoritative between explicit loads/saves, removing the per-frame JSON disk round-trip.");
    }


    private static SemanticInjectionResult ApplyGoodFeelings(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Event.Observe(\"PlayerPuppet\", \"OnAction\"",
            "SelfFeature.NoClip.HandleMouseLook",
            "Utils.Weapon.HandleInputAction",
            "Handler.Update()");

        var initText = RouteExactOnActionObserver(
            init.Text,
            "gcetGoodFeelingsOnAction",
            "GoodFeelings",
            new[] { "CameraMouseX", "RangedAttack" },
            "GoodFeelings PlayerPuppet OnAction",
            "modulesLoaded",
            "SelfFeature.NoClip.HandleMouseLook",
            "Utils.Weapon.HandleInputAction");
        context.Write(init, initText);

        var handler = context.FindFile(
            "Controls/Handler.lua",
            "function Handler.Update()",
            "State.InitializeTracking()",
            "BindManager.Update()",
            "Bindings.IsActionDown(\"TOGGLE\")",
            "Restrictions.Update()",
            "Cursor.Update()");

        var handlerText = RegexReplaceOnce(
            handler.Text,
            @"(?ms)^function\s+Handler\.Update\s*\(\s*\)\s*\r?\n.*?^[ \t]*if\s+not\s+State\.menuOpen\s+then\s*\r?\n[ \t]*holdStart\.up\s*,\s*holdStart\.down\s*,\s*holdStart\.left\s*,\s*holdStart\.right\s*=\s*0\s*,\s*0\s*,\s*0\s*,\s*0\s*\r?\n[ \t]*return\s*\r?\n[ \t]*end\s*\r?\n",
            "function Handler.Update()\n" +
            "    local now = os.clock() * 1000\n" +
            "    State.upPressed, State.downPressed = false, false\n" +
            "    State.leftPressed, State.rightPressed = false, false\n" +
            "    State.selectPressed, State.backPressed = false, false\n" +
            "    State.miscPressed = false\n\n" +
            "    if not initialized then\n" +
            "        State.InitializeTracking()\n" +
            "        BindManager.Initialize()\n" +
            "        initialized = true\n" +
            "    end\n\n" +
            "    BindManager.Update()\n\n" +
            "    if State.bindingKey then\n" +
            "        Restrictions.Update()\n" +
            "        Cursor.Update()\n" +
            "        __gcetHandlerLastMenuOpen = State.menuOpen\n" +
            "        return\n" +
            "    end\n\n" +
            "    if Bindings.IsActionDown(\"TOGGLE\") and now - lastTick.toggle > Handler.scrollDelayBase then\n" +
            "        State.ToggleMenu()\n" +
            "        Logger.Log(\"Controls: Menu toggled \" .. tostring(State.menuOpen))\n" +
            "        lastTick.toggle = now\n" +
            "    end\n\n" +
            "    local __gcetMenuChanged = __gcetHandlerLastMenuOpen ~= nil and __gcetHandlerLastMenuOpen ~= State.menuOpen\n" +
            "    if State.menuOpen or __gcetMenuChanged then\n" +
            "        Restrictions.Update()\n" +
            "        Cursor.Update()\n" +
            "    end\n" +
            "    __gcetHandlerLastMenuOpen = State.menuOpen\n\n" +
            "    if not State.menuOpen then\n" +
            "        holdStart.up, holdStart.down, holdStart.left, holdStart.right = 0, 0, 0, 0\n" +
            "        return\n" +
            "    end\n",
            "GoodFeelings closed-menu Handler gate");

        handlerText = ReplaceOnce(
            handlerText,
            "local initialized = false",
            "local initialized = false\nlocal __gcetHandlerLastMenuOpen = nil",
            "GoodFeelings Handler menu-state cache");

        context.Write(handler, handlerText);
        return SemanticInjectionResult.Success(
            "Routed GoodFeelings' two source-proven PlayerPuppet actions through 0-Engine and made menu-only restriction/cursor/navigation work dormant while the cheat menu is closed; BindManager hotkeys, the toggle wake path, and the configured status overlay remain live.");
    }

    private static SemanticInjectionResult ApplyAirBackFlip(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "GAME_ACTIONS",
            "AirBackflip_Backflip",
            "AirBackflip_SwingOver",
            "MoveY",
            "BUTTON_PRESSED");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetAirBackFlipOnAction",
            "AirBackFlip",
            new[] {
                "MoveY",
                "AirBackflip_Backflip",
                "AirBackflip_Frontflip",
                "AirBackflip_SideflipLeft",
                "AirBackflip_SideflipRight",
                "AirBackflip_SwingOver"
            },
            "AirBackFlip PlayerPuppet OnAction",
            "GAME_ACTIONS",
            "MoveY",
            "BUTTON_PRESSED");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed only MoveY and AirBackFlip's five game actions through 0-Engine. Airborne eligibility, press/release semantics, flip counters and active-flip frame cadence are unchanged.");
    }

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

        var text = RegexReplaceOnce(
            file.Text,
            @"(?ms)^(?<indent>[ \t]*)if\s+not\s+action\s+then\s+return\s+end\s*\r?\n\s*local\s+aType\s*=\s*action:GetType\(\)\s*\r?\n\s*if\s+aType\s*~=\s*gameinputActionType\.BUTTON_PRESSED\s+then\s+return\s+end\s*\r?\n\s*local\s+name\s*=\s*getActionName\(action\)\s*$",
            "    if not action then return end\n" +
            "    local __gcetRawName = action:GetName(action)\n" +
            "    local name = __gcetRawName and __gcetRawName.value or nil\n" +
            "    if name == nil or name == \"\" then name = getActionName(action) end\n" +
            "    -- G-CET: normal gameplay rejects unrelated actions before type decoding,\n" +
            "    -- pcall/name conversion, player lookup or weapon inspection. Capture/debug\n" +
            "    -- explicitly re-open the broad path, and captured names remain dynamic.\n" +
            "    if not captureArmed and not cfg.debugPrint and not triggerActions[name] then return end\n" +
            "    local aType = action:GetType()\n" +
            "    if aType ~= gameinputActionType.BUTTON_PRESSED then return end",
            "AutoDrop action-name hot prefilter");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved AutoDrop's source-proven trigger-name rejection ahead of action-type decoding, string conversion, player acquisition and weapon inspection. Capture/debug modes and dynamically learned trigger names retain the original broad behavior.");
    }

    private static SemanticInjectionResult ApplyDroneCompanionsRevamp(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "DroneLogic/Drone AI - Mech.lua",
            "Override('TweakAIActionAbstract', 'Update'",
            "MinotaurMech.AimAttackHMG",
            "MinotaurMech.RotateToTargetNoLimit",
            "DroneOctantActions.ShootDefault",
            "DroneBombusActions.FollowTargetFast",
            "TagsContains(CName.new(\"Robot\"))");

        var text = ReplaceOnce(
            file.Text,
            "    local mechcount, mechcount2, octantcount, bombuscount = 0, 0, 0, 0\n" +
            "    Override('TweakAIActionAbstract', 'Update', function(self, context, wrappedMethod)\n" +
            "        local owner = ScriptExecutionContext.GetOwner(context)\n",
            "    local mechcount, mechcount2, octantcount, bombuscount = 0, 0, 0, 0\n" +
            "    local __gcetDcoAimHmg = TweakDBID.new(\"MinotaurMech.AimAttackHMG\")\n" +
            "    local __gcetDcoRotate = TweakDBID.new(\"MinotaurMech.RotateToTargetNoLimit\")\n" +
            "    local __gcetDcoOctantShoot = TweakDBID.new(\"DroneOctantActions.ShootDefault\")\n" +
            "    local __gcetDcoBombusFollow = TweakDBID.new(\"DroneBombusActions.FollowTargetFast\")\n" +
            "    Override('TweakAIActionAbstract', 'Update', function(self, context, wrappedMethod)\n" +
            "        local recordID = (self.actionRecord and self.actionRecord:GetID()) or TweakDBID.new(\"\")\n" +
            "        if recordID ~= __gcetDcoAimHmg and recordID ~= __gcetDcoRotate\n" +
            "           and recordID ~= __gcetDcoOctantShoot and recordID ~= __gcetDcoBombusFollow then\n" +
            "            return wrappedMethod(context)\n" +
            "        end\n" +
            "        local owner = ScriptExecutionContext.GetOwner(context)\n",
            "Drone Companions exact AI action-record prefilter");

        text = ReplaceOnce(
            text,
            "            local recordID = (self.actionRecord and self.actionRecord:GetID()) or TweakDBID.new(\"\")\n" +
            "            if recordID == TweakDBID.new(\"MinotaurMech.AimAttackHMG\") then",
            "            if recordID == __gcetDcoAimHmg then",
            "Drone Companions cached first record ID");

        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"MinotaurMech.RotateToTargetNoLimit\") then",
            "            elseif recordID == __gcetDcoRotate then",
            "Drone Companions cached rotate record ID");
        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"DroneOctantActions.ShootDefault\") then",
            "            elseif recordID == __gcetDcoOctantShoot then",
            "Drone Companions cached octant record ID");
        text = ReplaceOnce(
            text,
            "            elseif recordID == TweakDBID.new(\"DroneBombusActions.FollowTargetFast\") then",
            "            elseif recordID == __gcetDcoBombusFollow then",
            "Drone Companions cached bombus record ID");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Prefiltered the global TweakAIActionAbstract::Update override by the only four action records it can modify, before owner/tag/player work. Robot/FistFight behavior for those records remains unchanged.");
    }

    private static SemanticInjectionResult ApplyGhostVoidSystem(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "GVS.GhostVoid.GVSStateSystem",
            "local function addVoidEnergy",
            "local function addStability",
            "corruptionGlitchTimer",
            "corruptionDrainTimer",
            "voidEnergyRegenPerSecond",
            "stabilityRegenPerSecond");

        var text = ReplaceOnce(
            file.Text,
            "local function addVoidEnergy(amount)\n  local state = getState()",
            "local function addVoidEnergy(amount, state)\n  state = state or getState()",
            "Ghost Void optional state reuse for void energy");

        text = ReplaceOnce(
            text,
            "local function addStability(amount)\n  local state = getState()",
            "local function addStability(amount, state)\n  state = state or getState()",
            "Ghost Void optional state reuse for stability");

        text = ReplaceOnce(
            text,
            "  local corruptionState = getState()\n",
            "  local __gcetGvsState = getState()\n  local corruptionState = __gcetGvsState\n",
            "Ghost Void frame state acquisition");

        text = ReplaceOnce(
            text,
            "  local drainState = getState()\n",
            "  local drainState = __gcetGvsState\n",
            "Ghost Void drain state reuse");

        text = ReplaceOnce(
            text,
            "    addVoidEnergy(gvs.voidEnergyRegenPerSecond * deltaTime)\n",
            "    addVoidEnergy(gvs.voidEnergyRegenPerSecond * deltaTime, __gcetGvsState)\n",
            "Ghost Void energy regeneration state reuse");

        text = ReplaceOnce(
            text,
            "  local stabilityState = getState()\n",
            "  local stabilityState = __gcetGvsState\n",
            "Ghost Void stability state reuse");

        text = ReplaceOnce(
            text,
            "      addStability(\n" +
            "        gvs.stabilityRegenPerSecond * deltaTime\n" +
            "      )",
            "      addStability(\n" +
            "        gvs.stabilityRegenPerSecond * deltaTime,\n" +
            "        __gcetGvsState\n" +
            "      )",
            "Ghost Void stability regeneration state reuse");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Resolved Ghost Void's ScriptableSystem once per update and reused that authoritative object for corruption/drain/regen work. Timers, delta-time amounts, HUD and visual-effect cadence are unchanged.");
    }

    private static SemanticInjectionResult ApplyStraightEdgedControls(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "UIBlocking = require('modules/ui_blocking')",
            "Lean.update(deltaTime)",
            "Inspection.update(deltaTime)",
            "ScrollWalk.tick()",
            "Attachments.update(deltaTime)");

        var initText = ReplaceOnce(
            init.Text,
            "__gcetRegisterEvent_1093('onUpdate', function(deltaTime)\n" +
            "    Lean.update(deltaTime)",
            "__gcetRegisterEvent_1093('onUpdate', function(deltaTime)\n" +
            "    UIBlocking.beginFrame()\n" +
            "    Lean.update(deltaTime)",
            "Straight Edged Controls per-frame UI-blocking snapshot");
        context.Write(init, initText);

        var ui = context.FindFile(
            "modules/ui_blocking.lua",
            "function UIBlocking.isBlocked()",
            "isInMenuFlag()",
            "isPhoneActive()",
            "isDeviceUIActive()",
            "isScannerActive()",
            "isPhotoModeActive()");

        var uiText = ReplaceOnce(
            ui.Text,
            "local hooksReady = false",
            "local hooksReady = false\nlocal __gcetFrameBlocked = false\nlocal __gcetFrameCacheReady = false",
            "Straight Edged Controls UI-blocking frame cache state");

        uiText = ReplaceOnce(
            uiText,
            "function UIBlocking.isBlocked()\n" +
            "    if shardReading or codexPopupOpen then\n" +
            "        return true\n" +
            "    end\n" +
            "    if isInMenuFlag() then\n" +
            "        return true\n" +
            "    end\n" +
            "    if isPhoneActive() then\n" +
            "        return true\n" +
            "    end\n" +
            "    if isDeviceUIActive() then\n" +
            "        return true\n" +
            "    end\n" +
            "    if isScannerActive() then\n" +
            "        return true\n" +
            "    end\n" +
            "    if isPhotoModeActive() then\n" +
            "        return true\n" +
            "    end\n" +
            "    return false\n" +
            "end",
            "local function __gcetComputeBlocked()\n" +
            "    if shardReading or codexPopupOpen then return true end\n" +
            "    if isInMenuFlag() then return true end\n" +
            "    if isPhoneActive() then return true end\n" +
            "    if isDeviceUIActive() then return true end\n" +
            "    if isScannerActive() then return true end\n" +
            "    if isPhotoModeActive() then return true end\n" +
            "    return false\n" +
            "end\n\n" +
            "function UIBlocking.beginFrame()\n" +
            "    __gcetFrameBlocked = __gcetComputeBlocked()\n" +
            "    __gcetFrameCacheReady = true\n" +
            "    return __gcetFrameBlocked\n" +
            "end\n\n" +
            "function UIBlocking.isBlocked()\n" +
            "    if __gcetFrameCacheReady then return __gcetFrameBlocked end\n" +
            "    return __gcetComputeBlocked()\n" +
            "end",
            "Straight Edged Controls shared UI-blocking frame cache");

        context.Write(ui, uiText);
        return SemanticInjectionResult.Success(
            "Computed Straight Edged Controls' expensive UI-blocking blackboard state once at the start of each update and reused it across the feature modules. All input/update callbacks still run at the author's original cadence.");
    }

    private static SemanticInjectionResult ApplyImmersiveHeadInertia(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "Inertia.onAction",
            "ConsumeSingleAction",
            "Observe(\"PlayerPuppet\", \"OnAction\"");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetImmersiveHeadInertiaOnAction",
            "ImmersiveHeadInertia",
            new[] { "CameraMouseX", "CameraMouseY" },
            "ImmersiveHeadInertia PlayerPuppet OnAction",
            "Inertia.onAction",
            "ConsumeSingleAction");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed ImmersiveHeadInertia's only two source-proven PlayerPuppet actions, CameraMouseX/Y, through 0-Engine while retaining action consumption and all inertia timing.");
    }

    private static SemanticInjectionResult ApplyAdvancedSettings(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "ConfigSystem:OnUpdate()",
            "CPS:setThemeBegin()",
            "registerForEvent(\"onOverlayOpen\"",
            "draw = true");

        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<indent>[ \t]*)ConfigSystem:OnUpdate\s*\(\s*\)\s*$",
            "${opening}\n" +
            "${indent}if not draw then return end\n" +
            "${indent}ConfigSystem:OnUpdate()",
            "Advanced Settings closed update gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>\s*registerForEvent\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)\r?\n(?<indent>[ \t]*)CPS:setThemeBegin\s*\(\s*\)\s*$",
            "${opening}\n" +
            "${indent}if not draw then return end\n" +
            "${indent}CPS:setThemeBegin()",
            "Advanced Settings closed draw gate");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Skipped option-table scans and CPStyling theme setup while the Advanced Settings overlay is closed; overlay-open behavior and live option editing remain frame-responsive.");
    }


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

    private static SemanticInjectionResult ApplyIllegalMechanic(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "hack_signal_interaction.lua",
            "function HackSignalInteraction.Update(delta)",
            "state.nextInteractionUpdateAt",
            "updatePulseVisual",
            "interactionUI.update()");

        var anchor =
            "  if not state.initialized then\n" +
            "    interactionUI.update()\n" +
            "    return\n" +
            "  end\n\n";

        var gate =
            anchor +
            "  -- G-CET semantic split: an active HACK search pulse retains rendered-frame\n" +
            "  -- visual timing. With no pulse/hub, defer the expensive ScriptableSystem\n" +
            "  -- lookup until the mod's existing interaction polling deadline.\n" +
            "  if not state.pulseActive and not state.hubVisible and currentTime < state.nextInteractionUpdateAt then\n" +
            "    interactionUI.update()\n" +
            "    return\n" +
            "  end\n\n";

        var text = ReplaceOnce(
            file.Text,
            anchor,
            gate,
            "Illegal Mechanic HACK pulse/interaction cadence boundary");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept active HACK pulse visuals frame-responsive while avoiding ScriptableSystem and interaction discovery work between the existing 0.20 s polling deadlines when idle.");
    }

    private static SemanticInjectionResult ApplyShift(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "Modules/Lifecycle/UpdateLoop.lua",
            "VehicleState.update(delta)",
            "WeaponState.update(delta)",
            "CameraEngine.update(delta)");

        var anchor = "local UpdateLoop = {}\n";
        var text = ReplaceOnce(
            file.Text,
            anchor,
            anchor +
            "local __gcetShiftStateElapsed = 0.0\n" +
            "local __gcetShiftStateInterval = 0.05\n",
            "Shift state cadence declarations");

        var stateUpdates =
            "    VehicleState.update(delta)\n" +
            "    WeaponState.update(delta)\n";

        var stateGated =
            "    __gcetShiftStateElapsed = __gcetShiftStateElapsed + delta\n" +
            "    if __gcetShiftStateElapsed >= __gcetShiftStateInterval then\n" +
            "        local __gcetStateDelta = __gcetShiftStateElapsed\n" +
            "        __gcetShiftStateElapsed = 0.0\n" +
            "        VehicleState.update(__gcetStateDelta)\n" +
            "        WeaponState.update(__gcetStateDelta)\n" +
            "    end\n";

        text = ReplaceOnce(
            text,
            stateUpdates,
            stateGated,
            "Shift vehicle/weapon state lane");
        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Preserved camera, shake, locomotion, Cron and speed interpolation every frame; moved vehicle/weapon state reconciliation to 20 Hz.");
    }


    private static SemanticInjectionResult ApplyAutoLoot(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "handleButtonPressed",
            "handleButtonReleased",
            "useDefaultActionKey",
            "isContinuousLooting");

        var text = RouteExactOnActionObserver(
            file.Text,
            "gcetAutoLootOnAction",
            "AutoLoot",
            new[]
            {
                "Pause",
                "OpenPauseMenu",
                "OpenMapMenu",
                "OpenCraftingMenu",
                "OpenJournalMenu",
                "OpenPerksMenu",
                "OpenInventoryMenu",
                "OpenHubMenu",
                "TogglePhotoMode",
                "UI_Apply",
                "Choice1",
                "click",
                "Ping",
                "Forward",
                "Back",
                "Left",
                "Right",
                "Jump",
                "ToggleCrouch",
                "MeleeAttack",
                "UI_DPadWeapons"
            },
            "AutoLoot PlayerPuppet OnAction",
            "useDefaultActionKey",
            "handleButtonPressed",
            "handleButtonReleased",
            "action:GetType");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Applied AutoLoot's known-good finite action routing without changing its looting cadence or trigger state machine.");
    }

    private static SemanticInjectionResult ApplyBetterLootMarkers(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "BetterLootMarkers.ImmersiveMode.Tick",
            "BetterLootMarkers.ImmersiveMode.Init",
            "BetterLootMarkers.Settings");

        var regex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*dt\s*\)\s*\r?\n\k<indent>[ \t]+BetterLootMarkers\.ImmersiveMode\.Tick\s*\(\s*dt\s*\)\s*\r?\n\k<indent>end\s*\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        var matches = regex.Matches(file.Text);
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "BetterLootMarkers onUpdate wrapper no longer uniquely matches the semantic off-state gate.");

        var match = matches[0];
        var indent = match.Groups["indent"].Value;
        var registrar = match.Groups["registrar"].Value;
        var text = file.Text[..match.Index] +
            indent + registrar + "(\"onUpdate\", function(dt)\n" +
            indent + "    if BetterLootMarkers.Settings.immersiveMode then\n" +
            indent + "        BetterLootMarkers.ImmersiveMode.Tick(dt)\n" +
            indent + "    end\n" +
            indent + "end)" +
            file.Text[(match.Index + match.Length)..];

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept BetterLootMarkers' proven frame behavior, but bypassed ImmersiveMode.Tick entirely while immersive mode is disabled; no discarded PASS4L cadence was restored.");
    }

    private static SemanticInjectionResult ApplyDriveBus(
        SemanticPatchContext context)
    {
        var core = context.FindFile(
            "Modules/core.lua",
            "function Core:SetObserve()",
            "exception_in_choice_list",
            "exception_in_mount_list",
            "self:ChoiceAction(action_name");

        var coreText = RouteExactOnActionObserver(
            core.Text,
            "gcetDriveBusOnAction",
            "DriveBus",
            new[]
            {
                "QuickExit",
                "NextWeapon",
                "PreviousWeapon",
                "ChoiceApply",
                "ChoiceScrollUp",
                "ChoiceScrollDown"
            },
            "DriveBus PlayerPuppet OnAction",
            "exception_in_choice_list",
            "exception_in_mount_list",
            "self:ChoiceAction(action_name");

        var cron = context.FindFile(
            "External/Cron.lua",
            "local timers = {}",
            "function Cron.Update",
            "function Cron.Every");

        var cronText = cron.Text;
        if (!Regex.IsMatch(
                cronText,
                @"(?m)^\s*function\s+Cron\.HasActiveTimers\s*\(",
                RegexOptions.CultureInvariant))
        {
            cronText = RegexReplaceOnce(
                cronText,
                @"(?m)^(?<indent>[ \t]*)function\s+Cron\.Update\s*\(\s*delta\s*\)",
                "${indent}function Cron.HasActiveTimers()\n" +
                "${indent}\tfor _, timer in ipairs(timers) do\n" +
                "${indent}\t\tif timer.active then return true end\n" +
                "${indent}\tend\n" +
                "${indent}\treturn false\n" +
                "${indent}end\n\n" +
                "${indent}function Cron.Update(delta)",
                "DriveBus Cron active-timer probe");
        }

        var init = context.FindFile(
            "init.lua",
            "Cron = require",
            "Cron.Update(delta)",
            "onUpdate");

        var updateRegex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\)\s*\r?\n\k<indent>[ \t]+Cron\.Update\s*\(\s*delta\s*\)\s*\r?\n\k<indent>end\s*\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);

        var updateMatches = updateRegex.Matches(init.Text);
        if (updateMatches.Count != 1)
            throw new InvalidOperationException(
                "DriveBus Cron onUpdate source no longer uniquely matches the dormant-timer gate.");

        var update = updateMatches[0];
        var updateIndent = update.Groups["indent"].Value;
        var registrar = update.Groups["registrar"].Value;
        var initText = init.Text[..update.Index] +
            updateIndent + registrar + "(\"onUpdate\", function(delta)\n" +
            updateIndent + "    if Cron.HasActiveTimers() then Cron.Update(delta) end\n" +
            updateIndent + "end)" +
            init.Text[(update.Index + update.Length)..];

        context.Write(core, coreText);
        context.Write(cron, cronText);
        context.Write(init, initText);
        return SemanticInjectionResult.Success(
            "Routed DriveBus's six source-proven actions through 0-Engine and made its Cron frame callback hard-dormant whenever no active timer exists.");
    }

    private static SemanticInjectionResult ApplyQuestTrackingToggle(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "n_ToggleSprint",
            "handlePadAction",
            "handlePadLongPressTrigger",
            "toggleTrackedQuest");

        var text = RouteExactOnActionObserver(
            file.Text,
            "questTrackingOnAction",
            "QuestTrackingToggle",
            new[]
            {
                "LeanFB",
                "ToggleSprint",
                "CameraAim",
                "world_map_menu_rotate_mouse",
                "world_map_menu_zoom_to_mappin",
                "PhoneInteract",
                "Jump",
                "Handbrake",
                "world_map_filter_navigation_down",
                "world_map_menu_track_waypoint"
            },
            "QuestTrackingToggle PlayerPuppet OnAction",
            "handlePadAction",
            "handlePadLongPressTrigger",
            "toggleTrackedQuest");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Routed QuestTrackingToggle's ten finite source-proven actions through 0-Engine; the original callback body, Consume calls, hold/release handling and LeanFB axis behavior remain intact.");
    }

    private static SemanticInjectionResult ApplyDedkaAutoShop(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "runDueTasks()",
            "checkPlayerVehicleEntry()",
            "showEntranceHub()",
            "showShopExitHub()",
            "ui.update()");

        var text = file.Text;

        if (!text.Contains("__gcetDedkaSemanticReady", StringComparison.Ordinal))
        {
            var schedulerAnchor = new Regex(
                @"(?m)^(?<indent>[ \t]*)--\s*-+\s*tiny scheduler\s*-+\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var schedulerMatches = schedulerAnchor.Matches(text);
            if (schedulerMatches.Count != 1)
                throw new InvalidOperationException(
                    "Dedka Auto Shop scheduler boundary is no longer uniquely identifiable.");
            var scheduler = schedulerMatches[0];
            var prefix =
                "local __gcetDedkaNearby = false\n" +
                "local __gcetDedkaEngine = nil\n" +
                "local __gcetDedkaMod = nil\n" +
                "local __gcetDedkaSemanticReady = false\n\n";
            text = text[..scheduler.Index] + prefix + text[scheduler.Index..];

            var laterRegex = new Regex(
                @"(?ms)^(?<indent>[ \t]*)local\s+__tasks\s*=\s*\{\s*\}\s*\r?\n\k<indent>local\s+function\s+later\s*\(\s*delay\s*,\s*fn\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*$",
                RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
            var laterMatches = laterRegex.Matches(text);
            if (laterMatches.Count != 1)
                throw new InvalidOperationException(
                    "Dedka Auto Shop tiny scheduler source no longer proves the timeout conversion.");
            var later = laterMatches[0];
            var li = later.Groups["indent"].Value;
            var replacementLater =
                li + "local __tasks = {}\n" +
                li + "local function later(delay, fn)\n" +
                li + "  if __gcetDedkaMod and type(__gcetDedkaMod.SetTimeout) == \"function\" then\n" +
                li + "    __gcetDedkaMod.SetTimeout(delay or 0.01, fn)\n" +
                li + "    return\n" +
                li + "  end\n" +
                li + "  __tasks[#__tasks+1] = {t = os.clock() + (delay or 0), fn = fn}\n" +
                li + "end";
            text = text[..later.Index] +
                replacementLater +
                text[(later.Index + later.Length)..];

            var initLine = new Regex(
                @"(?m)^(?<indent>[ \t]*)load_catalog_from_json_dir\s*\(\s*(?<catalog>[A-Za-z_]\w*)\s*\)\s*$",
                RegexOptions.CultureInvariant);
            var initMatches = initLine.Matches(text);
            if (initMatches.Count != 1)
                throw new InvalidOperationException(
                    "Dedka Auto Shop catalog initialization is no longer uniquely identifiable.");
            var initMatch = initMatches[0];
            var ind = initMatch.Groups["indent"].Value;
            var catalogVar = initMatch.Groups["catalog"].Value;

            var semanticSetup =
                initMatch.Value + "\n\n" +
                ind + "local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")\n" +
                ind + "if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.Register) == \"function\"\n" +
                ind + "  and type(__gcetEngine.RegisterZone) == \"function\" then\n" +
                ind + "  __gcetDedkaEngine = __gcetEngine\n" +
                ind + "  __gcetDedkaMod = __gcetEngine.Register(\"Dedka Auto Shop\")\n" +
                ind + "  if type(__gcetDedkaMod) == \"table\" and type(__gcetDedkaMod.SetInterval) == \"function\"\n" +
                ind + "    and type(__gcetDedkaMod.Subscribe) == \"function\" then\n" +
                ind + "    local shopNearCount = 0\n" +
                ind + "    local shopDetailTimer = nil\n\n" +
                ind + "    local function startShopDetail()\n" +
                ind + "      if shopDetailTimer then return end\n" +
                ind + "      __gcetDedkaNearby = true\n" +
                ind + "      shopDetailTimer = __gcetDedkaMod.SetInterval(0.2, function()\n" +
                ind + "        if type(__gcetDedkaEngine.IsPlaying) == \"function\" and not __gcetDedkaEngine.IsPlaying() then return end\n" +
                ind + "        local p = type(__gcetDedkaEngine.GetPlayer) == \"function\" and __gcetDedkaEngine.GetPlayer() or Game.GetPlayer()\n" +
                ind + "        if not p then return end\n" +
                ind + "        local snapshot = type(__gcetDedkaEngine.GetState) == \"function\" and __gcetDedkaEngine.GetState() or nil\n" +
                ind + "        local pos = snapshot and snapshot.pos or p:GetWorldPosition()\n" +
                ind + "        if not pos then return end\n\n" +
                ind + "        local nearEntrance = isNear(pos, ENTRANCE_POS, OPEN_RADIUS_M)\n" +
                ind + "        local farFromEntrance = not isNear(pos, ENTRANCE_POS, CLOSE_RADIUS_M)\n" +
                ind + "        local nearEntrance2 = isNear(pos, ENTRANCE_POS_2, OPEN_RADIUS_M)\n" +
                ind + "        local farFromEntrance2 = not isNear(pos, ENTRANCE_POS_2, CLOSE_RADIUS_M)\n" +
                ind + "        local nearSeller = isNear(pos, SELLER_POS, OPEN_RADIUS_M)\n" +
                ind + "        local farFromSeller = not isNear(pos, SELLER_POS, CLOSE_RADIUS_M)\n" +
                ind + "        local nearShopExit = isNear(pos, SHOP_EXIT_POS, OPEN_RADIUS_M)\n" +
                ind + "        local farFromShopExit = not isNear(pos, SHOP_EXIT_POS, CLOSE_RADIUS_M)\n\n" +
                ind + "        if nearEntrance and not state.showing and not state.atEntrance then\n" +
                ind + "          showEntranceHub()\n" +
                ind + "        elseif state.atEntrance and farFromEntrance then\n" +
                ind + "          hideHub(); state.atEntrance = false\n" +
                ind + "        elseif nearEntrance2 and not state.showing and not state.atEntrance2 then\n" +
                ind + "          showEntrance2Hub()\n" +
                ind + "        elseif state.atEntrance2 and farFromEntrance2 then\n" +
                ind + "          hideHub(); state.atEntrance2 = false\n" +
                ind + "        elseif nearShopExit and not state.showing and not state.atShopExit then\n" +
                ind + "          showShopExitHub()\n" +
                ind + "        elseif state.atShopExit and farFromShopExit then\n" +
                ind + "          hideHub(); state.atShopExit = false\n" +
                ind + "        elseif state.phase == \"root\" and not state.showing and nearSeller\n" +
                ind + "          and not state.atEntrance and not state.atEntrance2 and not state.atShopExit then\n" +
                ind + "          showRootHub()\n" +
                ind + "        elseif state.showing and farFromSeller\n" +
                ind + "          and not state.atEntrance and not state.atEntrance2 and not state.atShopExit then\n" +
                ind + "          hideHub()\n" +
                ind + "        elseif not state.showing and nearSeller and state.phase ~= \"root\"\n" +
                ind + "          and not state.atEntrance and not state.atEntrance2 and not state.atShopExit then\n" +
                ind + "          if state.phase == \"buy\" then showBuyHub() else showRootHub() end\n" +
                ind + "        end\n" +
                ind + "      end)\n" +
                ind + "    end\n\n" +
                ind + "    local function stopShopDetail()\n" +
                ind + "      if shopDetailTimer and type(__gcetDedkaMod.ClearTimer) == \"function\" then\n" +
                ind + "        __gcetDedkaMod.ClearTimer(shopDetailTimer)\n" +
                ind + "      end\n" +
                ind + "      shopDetailTimer = nil\n" +
                ind + "      __gcetDedkaNearby = false\n" +
                ind + "      if state.showing then hideHub() end\n" +
                ind + "      if toggleSystem.active then\n" +
                ind + "        toggleSystem.active = false\n" +
                ind + "        toggleSystem.currentIteration = 0\n" +
                ind + "        playerVehicleState.monitoringActive = false\n" +
                ind + "      end\n" +
                ind + "    end\n\n" +
                ind + "    local function registerWakeZone(id, pos)\n" +
                ind + "      __gcetDedkaEngine.RegisterZone({\n" +
                ind + "        id = id, x = pos.x, y = pos.y, z = pos.z, radius = 200, throttle = 10,\n" +
                ind + "        onEnter = function() shopNearCount = shopNearCount + 1; startShopDetail() end,\n" +
                ind + "        onExit = function()\n" +
                ind + "          shopNearCount = shopNearCount - 1\n" +
                ind + "          if shopNearCount <= 0 then shopNearCount = 0; stopShopDetail() end\n" +
                ind + "        end\n" +
                ind + "      })\n" +
                ind + "    end\n" +
                ind + "    registerWakeZone(\"dedka_entrance1\", ENTRANCE_POS)\n" +
                ind + "    registerWakeZone(\"dedka_entrance2_cluster\", ENTRANCE_POS_2)\n\n" +
                ind + "    __gcetDedkaMod.Subscribe(\"VehicleMount\", function()\n" +
                ind + "      local p = type(__gcetDedkaEngine.GetPlayer) == \"function\" and __gcetDedkaEngine.GetPlayer() or Game.GetPlayer()\n" +
                ind + "      if not p then return end\n" +
                ind + "      local mv = Game['GetMountedVehicle;GameObject'](p)\n" +
                ind + "      if mv then\n" +
                ind + "        playerVehicleState.wasInVehicle = true\n" +
                ind + "        playerVehicleState.currentVehicle = mv\n" +
                ind + "        playerVehicleState.lastCheckedVehicle = mv\n" +
                ind + "      end\n" +
                ind + "    end)\n" +
                ind + "    __gcetDedkaMod.Subscribe(\"VehicleUnmount\", function()\n" +
                ind + "      playerVehicleState.wasInVehicle = false\n" +
                ind + "      playerVehicleState.lastCheckedVehicle = playerVehicleState.currentVehicle\n" +
                ind + "    end)\n" +
                ind + "    __gcetDedkaMod.Subscribe(\"PlayerInvalidated\", function()\n" +
                ind + "      stopShopDetail(); shopNearCount = 0; despawnAll()\n" +
                ind + "      playerVehicleState.wasInVehicle = false\n" +
                ind + "      playerVehicleState.currentVehicle = nil\n" +
                ind + "      playerVehicleState.lastCheckedVehicle = nil\n" +
                ind + "      playerVehicleState.monitoringActive = false\n" +
                ind + "      state.showing = false; state.phase = \"root\"\n" +
                ind + "    end)\n" +
                ind + "    __gcetDedkaSemanticReady = true\n" +
                ind + "  end\n" +
                ind + "end";
            text = text[..initMatch.Index] +
                semanticSetup +
                text[(initMatch.Index + initMatch.Length)..];

            var drawRegex = new Regex(
                @"(?m)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onDraw['""]\s*,\s*function\s*\(\s*\)\s*pcall\s*\(\s*function\s*\(\s*\)\s*ui\.update\s*\(\s*\)\s*end\s*\)\s*end\s*\)\s*$",
                RegexOptions.CultureInvariant);
            var drawMatches = drawRegex.Matches(text);
            if (drawMatches.Count != 1)
                throw new InvalidOperationException(
                    "Dedka Auto Shop onDraw source no longer uniquely matches the visibility gate.");
            var draw = drawMatches[0];
            var di = draw.Groups["indent"].Value;
            var dr = draw.Groups["registrar"].Value;
            var drawReplacement =
                di + dr + "(\"onDraw\", function()\n" +
                di + "  if state.showing then pcall(function() ui.update() end) end\n" +
                di + "end)";
            text = text[..draw.Index] +
                drawReplacement +
                text[(draw.Index + draw.Length)..];

            var updateRegex = new Regex(
                @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*(?<arg>[A-Za-z_]\w*)\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*\)\s*$",
                RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
            var updates = updateRegex.Matches(text)
                .Cast<Match>()
                .Where(match =>
                    match.Value.Contains("runDueTasks()", StringComparison.Ordinal) &&
                    match.Value.Contains("checkPlayerVehicleEntry()", StringComparison.Ordinal) &&
                    match.Value.Contains("activeSpawns", StringComparison.Ordinal) &&
                    match.Value.Contains("nearEntrance", StringComparison.Ordinal) &&
                    match.Value.Contains("nearSeller", StringComparison.Ordinal) &&
                    match.Value.Contains("nearShopExit", StringComparison.Ordinal))
                .ToList();
            if (updates.Count != 1)
                throw new InvalidOperationException(
                    "Dedka Auto Shop onUpdate source no longer uniquely proves the old polling state machine.");
            var update = updates[0];
            var ui = update.Groups["indent"].Value;
            var ur = update.Groups["registrar"].Value;
            var arg = update.Groups["arg"].Value;
            var originalBody = update.Groups["body"].Value;
            var optimized =
                ui + ur + "(\"onUpdate\", function(" + arg + ")\n" +
                ui + "  if not __gcetDedkaSemanticReady then\n" +
                originalBody +
                ui + "    return\n" +
                ui + "  end\n" +
                ui + "  if not __gcetDedkaNearby then return end\n" +
                ui + "  if toggleSystem.active then executeToggleCommand() end\n" +
                ui + "  for i = #activeSpawns, 1, -1 do\n" +
                ui + "    local spawn = activeSpawns[i]\n" +
                ui + "    if type(spawn) == \"table\" and spawn.type == \"timer\" and spawn.check and spawn.check() then\n" +
                ui + "      table.remove(activeSpawns, i)\n" +
                ui + "    end\n" +
                ui + "  end\n" +
                ui + "end)";
            text = text[..update.Index] +
                optimized +
                text[(update.Index + update.Length)..];
        }

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved Dedka Auto Shop's proximity discovery to 0-Engine coarse zones plus a 5 Hz near-shop lane, replaced vehicle polling with mount lifecycle events, hard-slept the shop while far away, and kept only active toggle/spawn cleanup on the frame path.");
    }

    private static SemanticInjectionResult ApplyRepeatableIncreasedCriminalActivity(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "processBodyRewards(system)",
            "processCompletionRewards(system)",
            "Mappins.sync(system, Sites)",
            "Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)");

        var text = ReplaceOnce(
            file.Text,
            "    tickElapsed = 0.0,\n    schedulerElapsed = 0.0,\n",
            "    tickElapsed = 0.0,\n" +
            "    rewardElapsed = 0.0,\n" +
            "    mappinElapsed = 0.0,\n" +
            "    diagnosticsElapsed = 0.0,\n" +
            "    schedulerElapsed = 0.0,\n",
            "RICA multi-rate cadence state");

        text = ReplaceOnce(
            text,
            "local function processBodyRewards(system)\n" +
            "    local processed = 0\n" +
            "    while processed < 64 do\n",
            "local function processBodyRewards(system, limit)\n" +
            "    local processed = 0\n" +
            "    limit = limit or 64\n" +
            "    while processed < limit do\n",
            "RICA bounded body rewards");

        text = ReplaceOnce(
            text,
            "local function processCompletionRewards(system)\n" +
            "    local processed = 0\n" +
            "    while processed < 5 do\n",
            "local function processCompletionRewards(system, limit)\n" +
            "    local processed = 0\n" +
            "    limit = limit or 5\n" +
            "    while processed < limit do\n",
            "RICA bounded completion rewards");

        text = ReplaceOnce(
            text,
            "    processBodyRewards(system)\n" +
            "    processCompletionRewards(system)\n" +
            "    Mappins.sync(system, Sites)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "end\n",
            "end\n\n" +
            "local function rewardTick()\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    processBodyRewards(system, 4)\n" +
            "    processCompletionRewards(system, 1)\n" +
            "end\n\n" +
            "local function mappinTick()\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    Mappins.sync(system, Sites)\n" +
            "end\n\n" +
            "local function diagnosticsTick()\n" +
            "    if Mod.settings.diagnostics.enabled == false then return end\n" +
            "    local system = Bridge.system()\n" +
            "    if not system or not Game.GetPlayer() then return end\n" +
            "    Diagnostics.setEnabled(Mod.settings.diagnostics.enabled)\n" +
            "    Diagnostics.configuration(Mod.settings)\n" +
            "    Diagnostics.snapshot(system, Sites, Mod.settings, Mappins)\n" +
            "end\n",
            "RICA split runtime lanes");

        var opening = FindOnUpdateOpening(text, "delta");
        text = ReplaceOnce(
            text,
            opening +
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n",
            opening +
            "    Mod.rewardElapsed = Mod.rewardElapsed + delta\n" +
            "    Mod.tickElapsed = Mod.tickElapsed + delta\n" +
            "    Mod.mappinElapsed = Mod.mappinElapsed + delta\n" +
            "    Mod.diagnosticsElapsed = Mod.diagnosticsElapsed + delta\n" +
            "    Mod.schedulerElapsed = Mod.schedulerElapsed + delta\n" +
            "    if Mod.rewardElapsed >= 0.25 then\n" +
            "        Mod.rewardElapsed = Mod.rewardElapsed % 0.25\n" +
            "        local ok, err = pcall(rewardTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Reward tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"reward_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n",
            "RICA reward cadence");

        text = ReplaceOnce(
            text,
            "    if Mod.schedulerElapsed >= 10.0 then\n",
            "    if Mod.mappinElapsed >= 1.0 then\n" +
            "        Mod.mappinElapsed = Mod.mappinElapsed % 1.0\n" +
            "        local ok, err = pcall(mappinTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Mappin tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"mappin_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n" +
            "    if Mod.diagnosticsElapsed >= 5.0 then\n" +
            "        Mod.diagnosticsElapsed = Mod.diagnosticsElapsed % 5.0\n" +
            "        local ok, err = pcall(diagnosticsTick)\n" +
            "        if not ok then\n" +
            "            print(\"[RICA] Diagnostics tick failed: \" .. tostring(err))\n" +
            "            Diagnostics.event(\"diagnostics_tick_failed\", { error = tostring(err) })\n" +
            "        end\n" +
            "    end\n" +
            "    if Mod.schedulerElapsed >= 10.0 then\n",
            "RICA mappin diagnostics cadence");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Applied RICA's validated multi-rate answer without version-locking Scheduler plumbing: rewards at 4 Hz with bounded batches, runtime state and mappins at 1 Hz, diagnostics at 0.2 Hz, and the scriptable-system tick at 0.1 Hz.");
    }

    private static SemanticInjectionResult ApplySitAnywhere(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Cron.Update(dt)",
            "interaction.update()",
            "world.update()",
            "self.logic:onUpdate()");

        var cron = context.FindFile(
            "modules/external/Cron.lua",
            "local timers",
            "function Cron.Update",
            "function Cron.Halt");

        var logic = context.FindFile(
            "modules/logic.lua",
            "function logic:new",
            "function logic:hideAllWorkspots",
            "function logic:onUpdate");

        var world = context.FindFile(
            "modules/worldInteraction.lua",
            "world.interactions",
            "function world.update",
            "function world.onSessionStart");

        var cronText = cron.Text;
        if (!Regex.IsMatch(
                cronText,
                @"(?m)^\s*function\s+Cron\.HasActiveTimers\s*\(",
                RegexOptions.CultureInvariant))
        {
            cronText = RegexReplaceOnce(
                cronText,
                @"(?m)^(?<indent>[ \t]*)function\s+Cron\.Halt\s*\(",
                "${indent}function Cron.HasActiveTimers()\n" +
                "${indent}\tfor _, timer in ipairs(timers) do\n" +
                "${indent}\t\tif timer.active then return true end\n" +
                "${indent}\tend\n" +
                "${indent}\treturn false\n" +
                "${indent}end\n\n" +
                "${indent}function Cron.Halt(",
                "sitAnywhere Cron active-timer probe");
        }

        var worldText = world.Text;
        if (!Regex.IsMatch(
                worldText,
                @"(?m)^\s*function\s+world\.hasVisibleState\s*\(",
                RegexOptions.CultureInvariant))
        {
            worldText = RegexReplaceOnce(
                worldText,
                @"(?m)^(?<indent>[ \t]*)function\s+world\.onSessionStart\s*\(",
                "${indent}function world.hasVisibleState()\n" +
                "${indent}    for _, interaction in pairs(world.interactions) do\n" +
                "${indent}        if interaction.shown or interaction.pinID ~= nil then return true end\n" +
                "${indent}    end\n" +
                "${indent}    return false\n" +
                "${indent}end\n\n" +
                "${indent}function world.onSessionStart(",
                "sitAnywhere visible-world sentinel");
        }

        var logicText = logic.Text;
        if (!Regex.IsMatch(
                logicText,
                @"\bworkspotsHidden\s*=",
                RegexOptions.CultureInvariant))
        {
            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)o\.sittables\s*=\s*\{\s*\}\s*$",
                "${indent}o.sittables = {}\n${indent}o.workspotsHidden = true",
                "sitAnywhere parked-workspot state");

            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)function\s+logic:hideAllWorkspots\s*\(\s*\)\s*$",
                "${indent}function logic:hideAllWorkspots()\n" +
                "${indent}    if self.workspotsHidden then return end",
                "sitAnywhere hideAllWorkspots idle guard");

            var hideBlock = new Regex(
                @"(?ms)^(?<indent>[ \t]*)function\s+logic:hideAllWorkspots\s*\(\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*$",
                RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
            var hideMatches = hideBlock.Matches(logicText);
            if (hideMatches.Count != 1)
                throw new InvalidOperationException(
                    "sitAnywhere hideAllWorkspots source no longer uniquely proves the parked-state boundary.");
            var hide = hideMatches[0];
            var hideBody = hide.Groups["body"].Value;
            if (!hideBody.Contains("self.workspotsHidden = true", StringComparison.Ordinal))
            {
                var replacement =
                    hide.Groups["indent"].Value + "function logic:hideAllWorkspots()\n" +
                    hideBody +
                    hide.Groups["indent"].Value + "    self.workspotsHidden = true\n" +
                    hide.Groups["indent"].Value + "end";
                logicText = logicText[..hide.Index] +
                    replacement +
                    logicText[(hide.Index + hide.Length)..];
            }

            logicText = RegexReplaceOnce(
                logicText,
                @"(?m)^(?<indent>[ \t]*)if\s+position\s+then\s*$",
                "${indent}if position then\n${indent}    self.workspotsHidden = false",
                "sitAnywhere workspot wake state");
        }

        var eventRegex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*dt\s*\)\s*\r?\n(?<body>.*?)^\k<indent>end\s*\)\s*$",
            RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);
        var candidates = eventRegex.Matches(init.Text)
            .Cast<Match>()
            .Where(match =>
                match.Value.Contains("Cron.Update(dt)", StringComparison.Ordinal) &&
                match.Value.Contains("interaction.update()", StringComparison.Ordinal) &&
                match.Value.Contains("world.update()", StringComparison.Ordinal) &&
                match.Value.Contains("self.logic:onUpdate()", StringComparison.Ordinal))
            .ToList();
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                "sitAnywhere onUpdate source no longer uniquely proves the active/dormant work boundary.");

        var selected = candidates[0];
        var indent = selected.Groups["indent"].Value;
        var registrar = selected.Groups["registrar"].Value;
        var i1 = indent + "    ";
        var i2 = indent + "        ";
        var i3 = indent + "            ";

        var replacementUpdate =
            indent + registrar + "(\"onUpdate\", function(dt)\n" +
            i1 + "if self.runtimeData.inMenu or not self.runtimeData.inGame then return end\n\n" +
            i1 + "local hasTimers = Cron.HasActiveTimers()\n" +
            i1 + "local scanning = self.logic.isScanning == true or self.runtimeData.forceScan == true\n" +
            i1 + "local inWorkspot = self.logic:inWorkspot() == true\n" +
            i1 + "local inTransition = self.logic:inTransition() == true\n" +
            i1 + "local workspotRealtime = false\n" +
            i1 + "for _, spot in pairs(self.logic.sittables) do\n" +
            i2 + "local workspot = spot.workspot\n" +
            i2 + "if workspot.enableCamera or workspot.camTransition or workspot.slide then\n" +
            i3 + "workspotRealtime = true\n" +
            i3 + "break\n" +
            i2 + "end\n" +
            i1 + "end\n\n" +
            i1 + "if not scanning\n" +
            i2 + "and not inWorkspot\n" +
            i2 + "and not inTransition\n" +
            i2 + "and not workspotRealtime\n" +
            i2 + "and not hasTimers\n" +
            i2 + "and not interaction.hubShown\n" +
            i2 + "and self.logic.workspotsHidden\n" +
            i2 + "and not world.hasVisibleState() then\n" +
            i2 + "return\n" +
            i1 + "end\n\n" +
            i1 + "if hasTimers then Cron.Update(dt) end\n" +
            i1 + "if interaction.hubShown then interaction.update() end\n" +
            i1 + "world.update()\n" +
            i1 + "self.logic:onUpdate()\n" +
            i1 + "for _, spot in pairs(self.logic.sittables) do\n" +
            i2 + "local workspot = spot.workspot\n" +
            i2 + "if workspot.enableCamera or workspot.camTransition or workspot.slide then\n" +
            i3 + "workspot.yaw = self.yaw\n" +
            i3 + "workspot.pitch = self.pitch\n" +
            i3 + "spot:update(dt)\n" +
            i2 + "end\n" +
            i1 + "end\n" +
            indent + "end)";

        var initText = init.Text[..selected.Index] +
            replacementUpdate +
            init.Text[(selected.Index + selected.Length)..];

        context.Write(init, initText);
        context.Write(cron, cronText);
        context.Write(logic, logicText);
        context.Write(world, worldText);
        return SemanticInjectionResult.Success(
            "Made Sit Anywhere hard-dormant when scanner/workspot/transition/timer/hub/mappin state is fully idle, while preserving rendered-frame cadence for live scanner, world, timer and workspot work.");
    }

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

    private static SemanticInjectionResult ApplyImmersiveThirdPerson(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "updateTppMaintenance",
            "updateThirdPersonCamera",
            "updateAutoPerspective",
            "mod.headLookTick");

        var opening = FindOnUpdateOpening(file.Text, "delta");

        var helpers =
            "local __gcetItppSupervisorElapsed = 0.0\n" +
            "local __gcetItppMaintenanceElapsed = 0.0\n\n" +
            "local function __gcetItppHasStandbyWork()\n" +
            "  local ap = state.autoPerspective or {}\n" +
            "  return (ap.userWantsTpp == true and ap.override ~= \"none\")\n" +
            "    or state.quickFppEngaged == true\n" +
            "    or state.pendingSessionTppRestore ~= nil\n" +
            "    or state.pendingSceneTppReapply ~= nil\n" +
            "    or state.pendingFppCleanup ~= nil\n" +
            "    or state.pendingFppRestore == true\n" +
            "    or state.fppRestoreWatchdog ~= nil\n" +
            "    or state.pendingTppRepReassert ~= nil\n" +
            "    or state.pendingMirrorHeadVerify ~= nil\n" +
            "    or state.pendingFacialMute ~= nil\n" +
            "    or state.pendingTppAnimPoke ~= nil\n" +
            "    or state.photoModeWasActive == true\n" +
            "    or state.photoModeHeadRestore ~= nil\n" +
            "    or state.pickupPulseUntil ~= nil\n" +
            "    or state.consumableIdleUntil ~= nil\n" +
            "    or state.consumableGateUntil ~= nil\n" +
            "    or state.consumableGraceUntil ~= nil\n" +
            "end\n\n" +
            opening;

        var text = ReplaceOnce(
            file.Text,
            opening,
            helpers,
            "immersive_third_person cadence declarations");

        var supervisorBlock =
            "  local inMenuNow = isPlayerInAnyMenu()\n" +
            "  if state.menuWasOpen and not inMenuNow then\n" +
            "    mod.clearDigitalMoveLatches(\"menu close\")\n" +
            "  end\n" +
            "  state.menuWasOpen = inMenuNow\n" +
            "  pcall(mod.nativeSettingsSaveTick, delta)\n" +
            "  pcall(mod.pollNativeToggle, delta)\n" +
            "  if (state.frameSeq % 6) == 0 then\n" +
            "    pcall(mod.nativeSettingsComboTick)\n" +
            "  end\n" +
            "  pcall(mod.headLookTick, delta)\n" +
            "  pcall(mod.fallCommitTick, delta)\n" +
            "  if state.pendingFaultNotice then\n" +
            "    state.faultNotifyTimer = (state.faultNotifyTimer or 0) + (delta or 0)\n" +
            "    if state.faultNotifyTimer >= 5.0 then\n" +
            "      state.faultNotifyTimer = 0\n" +
            "      pcall(mod.notifyFaultTick)\n" +
            "    end\n" +
            "  end\n" +
            "  if state.enabled or state.photoModeWasActive then\n" +
            "    state.photoModePollTimer = (state.photoModePollTimer or 0) + (delta or 0)\n" +
            "    if state.photoModePollTimer >= 0.15 then\n" +
            "      state.photoModePollTimer = 0\n" +
            "      local photoModeNow = autoReadPhotoMode()\n" +
            "      if state.photoModeWasActive and not photoModeNow and state.enabled then\n" +
            "        state.photoModeHeadRestore = { at = (state.modClock or 0) + 0.10, passes = 0 }\n" +
            "      end\n" +
            "      state.photoModeWasActive = photoModeNow\n" +
            "    end\n" +
            "  end\n" +
            "  mod.updatePhotoModeHeadRestore()\n\n" +
            "  safeCallQuiet(function() updateSessionGuard(delta) end)\n" +
            "  safeCallQuiet(function() updateAutoPerspective(delta) end)\n";

        var gatedSupervisorBlock =
            "  __gcetItppSupervisorElapsed = __gcetItppSupervisorElapsed + math.max(delta or 0, 0)\n" +
            "  local __gcetItppSupervisorInterval = (state.enabled or __gcetItppHasStandbyWork()) and 0.10 or 0.50\n" +
            "  if __gcetItppSupervisorElapsed >= __gcetItppSupervisorInterval then\n" +
            "    local __gcetItppSupervisorDelta = __gcetItppSupervisorElapsed\n" +
            "    __gcetItppSupervisorElapsed = 0.0\n" +
            "    local inMenuNow = isPlayerInAnyMenu()\n" +
            "    if state.menuWasOpen and not inMenuNow then\n" +
            "      mod.clearDigitalMoveLatches(\"menu close\")\n" +
            "    end\n" +
            "    state.menuWasOpen = inMenuNow\n" +
            "    pcall(mod.nativeSettingsSaveTick, __gcetItppSupervisorDelta)\n" +
            "    pcall(mod.pollNativeToggle, __gcetItppSupervisorDelta)\n" +
            "    pcall(mod.nativeSettingsComboTick)\n" +
            "    pcall(mod.headLookTick, __gcetItppSupervisorDelta)\n" +
            "    if state.pendingFaultNotice then\n" +
            "      state.faultNotifyTimer = (state.faultNotifyTimer or 0) + __gcetItppSupervisorDelta\n" +
            "      if state.faultNotifyTimer >= 5.0 then\n" +
            "        state.faultNotifyTimer = 0\n" +
            "        pcall(mod.notifyFaultTick)\n" +
            "      end\n" +
            "    end\n" +
            "    if state.enabled or state.photoModeWasActive then\n" +
            "      state.photoModePollTimer = (state.photoModePollTimer or 0) + __gcetItppSupervisorDelta\n" +
            "      if state.photoModePollTimer >= 0.15 then\n" +
            "        state.photoModePollTimer = 0\n" +
            "        local photoModeNow = autoReadPhotoMode()\n" +
            "        if state.photoModeWasActive and not photoModeNow and state.enabled then\n" +
            "          state.photoModeHeadRestore = { at = (state.modClock or 0) + 0.10, passes = 0 }\n" +
            "        end\n" +
            "        state.photoModeWasActive = photoModeNow\n" +
            "      end\n" +
            "    end\n" +
            "    mod.updatePhotoModeHeadRestore()\n" +
            "    safeCallQuiet(function() updateSessionGuard(__gcetItppSupervisorDelta) end)\n" +
            "    safeCallQuiet(function() updateAutoPerspective(__gcetItppSupervisorDelta) end)\n" +
            "    guardStep(\"updateDependencyGuard\", updateDependencyGuard, __gcetItppSupervisorDelta)\n" +
            "  end\n" +
            "  pcall(mod.fallCommitTick, delta)\n";

        text = ReplaceOnce(
            text,
            supervisorBlock,
            gatedSupervisorBlock,
            "immersive_third_person supervisor lane");

        text = ReplaceOnce(
            text,
            "  guardStep(\"updateDependencyGuard\", updateDependencyGuard, delta)\n\n" +
            "  if state.enabled then\n" +
            "    guardStep(\"tppMaintenance\", updateTppMaintenance, delta)\n" +
            "  end\n",
            "  if state.enabled then\n" +
            "    __gcetItppMaintenanceElapsed = __gcetItppMaintenanceElapsed + math.max(delta or 0, 0)\n" +
            "    if __gcetItppMaintenanceElapsed >= 0.25 then\n" +
            "      local __gcetMaintenanceDelta = __gcetItppMaintenanceElapsed\n" +
            "      __gcetItppMaintenanceElapsed = 0.0\n" +
            "      guardStep(\"tppMaintenance\", updateTppMaintenance, __gcetMaintenanceDelta)\n" +
            "    end\n" +
            "  else\n" +
            "    __gcetItppMaintenanceElapsed = 0.0\n" +
            "  end\n",
            "immersive_third_person maintenance cadence");

        text = ReplaceOnce(
            text,
            "  guardStep(\"updateCameraTransition\", updateCameraTransition, delta)\n" +
            "  guardStep(\"updateLootAssist\", updateLootAssist, delta)\n" +
            "  guardStep(\"updateThirdPersonCamera\", updateThirdPersonCamera, delta)\n",
            "  if state.enabled or state.cameraTransition then\n" +
            "    guardStep(\"updateCameraTransition\", updateCameraTransition, delta)\n" +
            "    guardStep(\"updateLootAssist\", updateLootAssist, delta)\n" +
            "    guardStep(\"updateThirdPersonCamera\", updateThirdPersonCamera, delta)\n" +
            "  end\n",
            "immersive_third_person camera realtime gate");

        // Realtime camera/transition work remains frame-cadence, but dormant
        // pending-state handlers should not run when their state is absent.
        text = ReplaceOnce(
            text,
            "  pcall(mod.fallCommitTick, delta)\n",
            "  if state.enabled or state.fallCommitSet or state.stepHoldSet or state.stepHoldUntil then\n" +
            "    pcall(mod.fallCommitTick, delta)\n" +
            "  end\n",
            "immersive_third_person fall commit gate");

        var pendingGates = new Dictionary<string, string>
        {
            ["updateHeadGuard"] = "state.enabled or state.headGuardApplied",
            ["updatePendingFppCleanup"] = "state.pendingFppCleanup",
            ["updateFppRestoreWatchdog"] = "state.fppRestoreWatchdog",
            ["updateTppRepReassert"] = "state.pendingTppRepReassert",
            ["updateMirrorHeadVerify"] = "state.pendingMirrorHeadVerify",
            ["updatePostSceneTppReapply"] = "state.pendingSceneTppReapply",
            ["updateFacialMute"] = "state.pendingFacialMute",
            ["updatePendingTppAnimPoke"] = "state.pendingTppAnimPoke",
            ["updateItemPickupPulse"] = "state.enabled or state.pickupPulseUntil",
            ["updateConsumableIdle"] = "state.enabled or state.consumableIdleUntil or state.consumableGateUntil or state.consumableGraceUntil"
        };

        foreach (var pair in pendingGates)
        {
            var call = pair.Key == "updateHeadGuard"
                ? "  guardStep(\"updateHeadGuard\", mod.updateHeadGuard)\n"
                : pair.Key == "updateConsumableIdle"
                    ? "  guardStep(\"updateConsumableIdle\", mod.updateConsumableIdle, delta)\n"
                    : "  guardStep(\"" + pair.Key + "\", " + pair.Key + ", delta)\n";
            var gated =
                "  if " + pair.Value + " then\n" +
                "    " + call.TrimStart() +
                "  end\n";
            text = ReplaceOnce(
                text,
                call,
                gated,
                "immersive_third_person " + pair.Key + " gate");
        }

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Kept active camera/transition work at rendered-frame cadence, reduced supervisory polling to 10 Hz active/standby and 2 Hz idle, reduced maintenance to 4 Hz, and gated pending-state handlers when their state is absent.");
    }


    private static SemanticInjectionResult ApplyImmersiveFirstPerson(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "Helpers.RefreshPlayerState",
            "collectPlayerState",
            "CameraCore.Update");

        var helpers = context.FindFile(
            "Modules/Helpers.lua",
            "function Helpers.RefreshPlayerState",
            "GetInspectionComponent",
            "IsActorInWorkspot",
            "StatusEffectSystem.ObjectHasStatusEffectOfType");

        var initText = ReplaceOnce(
            init.Text,
            "local function collectPlayerState()\n" +
            "    return Helpers.RefreshPlayerState(cachedPlayerState)\n" +
            "end\n",
            "local function collectPlayerState(delta)\n" +
            "    return Helpers.RefreshPlayerState(cachedPlayerState, delta)\n" +
            "end\n",
            "ImmersiveFirstPerson collectPlayerState delta");

        initText = ReplaceOnce(
            initText,
            "        local playerState = isPaused and nil or collectPlayerState()\n",
            "        local playerState = isPaused and nil or collectPlayerState(delta)\n",
            "ImmersiveFirstPerson onUpdate player-state probe");

        var helperText = ReplaceOnce(
            helpers.Text,
            "    weaponSlot = nil,\n",
            "    weaponSlot = nil,\n" +
            "    slowProbeElapsed = 1.0,\n" +
            "    slowProbe = {\n" +
            "        mountedVehicle = false,\n" +
            "        knockedStatus = false,\n" +
            "        inspecting = false,\n" +
            "        inWorkspot = false,\n" +
            "        crouching = false,\n" +
            "    },\n",
            "ImmersiveFirstPerson slow-probe session state");

        helperText = ReplaceOnce(
            helperText,
            "    session.weaponSlot = nil\n",
            "    session.weaponSlot = nil\n" +
            "    session.slowProbeElapsed = 1.0\n" +
            "    session.slowProbe.mountedVehicle = false\n" +
            "    session.slowProbe.knockedStatus = false\n" +
            "    session.slowProbe.inspecting = false\n" +
            "    session.slowProbe.inWorkspot = false\n" +
            "    session.slowProbe.crouching = false\n",
            "ImmersiveFirstPerson slow-probe reset");

        helperText = ReplaceOnce(
            helperText,
            "local function refreshPlayerState(target)\n",
            "local function refreshPlayerState(target, delta)\n",
            "ImmersiveFirstPerson refreshPlayerState signature");

        var hotProbeBlock =
            "    local inspectionComponent = player:GetInspectionComponent()\n" +
            "    local playerState = player:GetPS()\n" +
            "    local mountedVehicle = vehicleState == 0\n" +
            "        and Game['GetMountedVehicle;GameObject'](player)\n" +
            "        or nil\n" +
            "    local knockedDown = detailedLocomotion == 29\n" +
            "        or detailedLocomotion == 31\n" +
            "        or landingState > 1\n" +
            "        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"VehicleKnockdown\")\n" +
            "        or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"BikeKnockdown\")\n";

        var sampledProbeBlock =
            "    session.slowProbeElapsed = session.slowProbeElapsed + math.max(tonumber(delta) or 0.0, 0.0)\n" +
            "    if session.slowProbeElapsed >= 0.05 then\n" +
            "        session.slowProbeElapsed = session.slowProbeElapsed % 0.05\n\n" +
            "        local mountedVehicle = vehicleState == 0\n" +
            "            and Game['GetMountedVehicle;GameObject'](player)\n" +
            "            or nil\n" +
            "        session.slowProbe.mountedVehicle = mountedVehicle ~= nil\n\n" +
            "        session.slowProbe.knockedStatus =\n" +
            "            StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"VehicleKnockdown\")\n" +
            "            or StatusEffectSystem.ObjectHasStatusEffectOfType(player, \"BikeKnockdown\")\n\n" +
            "        local inspectionComponent = player:GetInspectionComponent()\n" +
            "        session.slowProbe.inspecting = inspectionComponent ~= nil\n" +
            "            and inspectionComponent:GetIsPlayerInspecting() == true\n\n" +
            "        session.slowProbe.inWorkspot = session.workspotSystem ~= nil\n" +
            "            and session.workspotSystem:IsActorInWorkspot(player) == true\n\n" +
            "        local playerState = player:GetPS()\n" +
            "        session.slowProbe.crouching = playerState ~= nil and playerState:IsCrouch() == true\n" +
            "    end\n\n" +
            "    local knockedDown = detailedLocomotion == 29\n" +
            "        or detailedLocomotion == 31\n" +
            "        or landingState > 1\n" +
            "        or session.slowProbe.knockedStatus\n";

        helperText = ReplaceOnce(
            helperText,
            hotProbeBlock,
            sampledProbeBlock,
            "ImmersiveFirstPerson expensive player-state probes");

        helperText = ReplaceOnce(
            helperText,
            "    target.inVehicle = vehicleState ~= 0 or mountedVehicle ~= nil\n",
            "    target.inVehicle = vehicleState ~= 0 or session.slowProbe.mountedVehicle\n",
            "ImmersiveFirstPerson mounted-vehicle cache");

        helperText = ReplaceOnce(
            helperText,
            "    target.inspecting = inspectionComponent ~= nil\n" +
            "        and inspectionComponent:GetIsPlayerInspecting() == true\n",
            "    target.inspecting = session.slowProbe.inspecting\n",
            "ImmersiveFirstPerson inspection cache");

        helperText = ReplaceOnce(
            helperText,
            "    target.inWorkspot = session.workspotSystem ~= nil\n" +
            "        and session.workspotSystem:IsActorInWorkspot(player) == true\n",
            "    target.inWorkspot = session.slowProbe.inWorkspot\n",
            "ImmersiveFirstPerson workspot cache");

        helperText = ReplaceOnce(
            helperText,
            "    target.crouching = playerState ~= nil and playerState:IsCrouch() == true\n",
            "    target.crouching = session.slowProbe.crouching\n",
            "ImmersiveFirstPerson crouch cache");

        helperText = ReplaceOnce(
            helperText,
            "function Helpers.RefreshPlayerState(target)\n" +
            "    target = target or {}\n" +
            "    local ok, refreshed = pcall(refreshPlayerState, target)\n",
            "function Helpers.RefreshPlayerState(target, delta)\n" +
            "    target = target or {}\n" +
            "    local ok, refreshed = pcall(refreshPlayerState, target, delta)\n",
            "ImmersiveFirstPerson public RefreshPlayerState signature");

        context.Write(init, initText);
        context.Write(helpers, helperText);
        return SemanticInjectionResult.Success(
            "Preserved rendered-frame camera/height work, but sampled expensive mounted/workspot/inspection/crouch/knockdown probes at 20 Hz using the validated optimized reference. Frame registration and action routing remain generic Resolver responsibilities.");
    }

    private static SemanticInjectionResult ApplyNativeInteractions(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "resourceHelper.onUpdate()",
            "manager.update()",
            "world.update()",
            "self.runtimeData.inGame");

        var manager = context.FindFile(
            "modules/projectsManager.lua",
            "function manager.update()",
            "manager.updateList",
            "project.interactions",
            "interaction.needsUpdate");

        var world = context.FindFile(
            "modules/utils/worldInteraction.lua",
            "WorldMappinUIProfile.nif",
            "world.getGridInteractions",
            "world.pinnedInteractions",
            "function world.togglePin");

        var managerText = ReplaceOnce(
            manager.Text,
            "local playerPosition = { x = 0, y = 0, z = 0 }\n\n" +
            "function manager.update()\n",
            "local playerPosition = { x = 0, y = 0, z = 0 }\n\n" +
            "function manager.hasRealtimeWork()\n" +
            "    for i = 1, #manager.updateList do\n" +
            "        local interaction = manager.updateList[i]\n" +
            "        if interaction.sceneRunning == true or interaction.transitionActive == true then\n" +
            "            return true\n" +
            "        end\n" +
            "        local director = interaction.sceneDirector\n" +
            "        if director and type(director.status) == \"function\" then\n" +
            "            local ok, status = pcall(director.status)\n" +
            "            if ok and status and status.running == true then return true end\n" +
            "        end\n" +
            "    end\n" +
            "    return false\n" +
            "end\n\n" +
            "function manager.update()\n",
            "nativeInteractions realtime manager sentinel");

        var opening = FindOnUpdateOpening(init.Text, "dt");
        var initText = ReplaceOnce(
            init.Text,
            opening,
            "local __gcetNifIdleWorldElapsed = 0.0\n" +
            "local __gcetNifIdleManagerElapsed = 0.0\n" +
            "local __gcetNifWasFullRate = true\n" +
            "local __gcetNifIdleWorldInterval = 1 / 30\n" +
            "local __gcetNifIdleManagerInterval = 1 / 12\n\n" +
            opening,
            "nativeInteractions dormant cadence state");

        var originalBody =
            "        if self.runtimeData.inGame and not self.runtimeData.inMenu then\n" +
            "            Cron.Update(dt)\n" +
            "            manager.update()\n" +
            "            world.update()\n" +
            "            resourceHelper.onUpdate()\n" +
            "        end\n";

        var optimizedBody =
            "        if self.runtimeData.inGame and not self.runtimeData.inMenu then\n" +
            "            Cron.Update(dt)\n" +
            "            local sceneLaunchPending = #resourceHelper.sceneQueue > 0\n" +
            "            resourceHelper.onUpdate()\n\n" +
            "            local quests = Game.GetQuestsSystem()\n" +
            "            if not quests then return end\n" +
            "            local sceneActive = quests:GetFactStr(\"nif_scene_active\") == 1\n" +
            "            local sceneStartPending = quests:GetFactStr(\"nif_start_signal\") ~= 0\n" +
            "            local interactionActive = next(world.activeInteractions) ~= nil\n" +
            "            local fullRate = sceneActive or sceneStartPending or interactionActive or sceneLaunchPending\n" +
            "            if not fullRate then fullRate = manager.hasRealtimeWork() end\n\n" +
            "            local runWorld = fullRate and not sceneActive\n" +
            "            local runManager = fullRate\n" +
            "            if fullRate then\n" +
            "                __gcetNifIdleWorldElapsed = 0.0\n" +
            "                __gcetNifIdleManagerElapsed = 0.0\n" +
            "                __gcetNifWasFullRate = true\n" +
            "            elseif __gcetNifWasFullRate then\n" +
            "                __gcetNifWasFullRate = false\n" +
            "                __gcetNifIdleWorldElapsed = 0.0\n" +
            "                __gcetNifIdleManagerElapsed = 0.0\n" +
            "                runWorld = true\n" +
            "                runManager = true\n" +
            "            else\n" +
            "                __gcetNifIdleWorldElapsed = __gcetNifIdleWorldElapsed + math.max(dt or 0, 0)\n" +
            "                __gcetNifIdleManagerElapsed = __gcetNifIdleManagerElapsed + math.max(dt or 0, 0)\n" +
            "                if __gcetNifIdleWorldElapsed >= __gcetNifIdleWorldInterval then\n" +
            "                    __gcetNifIdleWorldElapsed = __gcetNifIdleWorldElapsed % __gcetNifIdleWorldInterval\n" +
            "                    runWorld = true\n" +
            "                end\n" +
            "                if __gcetNifIdleManagerElapsed >= __gcetNifIdleManagerInterval then\n" +
            "                    __gcetNifIdleManagerElapsed = __gcetNifIdleManagerElapsed % __gcetNifIdleManagerInterval\n" +
            "                    runManager = true\n" +
            "                end\n" +
            "            end\n\n" +
            "            if runManager then manager.update() end\n" +
            "            if runWorld then world.update() end\n" +
            "        end\n";

        initText = ReplaceOnce(
            initText,
            originalBody,
            optimizedBody,
            "nativeInteractions active/dormant onUpdate split");

        var worldText = ReplaceOnce(
            world.Text,
            "    pinnedInteractions = {},\n",
            "    pinnedInteractions = {},\n" +
            "    pinInteractions = {},\n",
            "nativeInteractions pin index state");

        worldText = ReplaceOnce(
            worldText,
            "local function getGridKey(position)\n",
            "local function indexPin(interaction)\n" +
            "    if not interaction or not interaction.pinID then return end\n" +
            "    local id = interaction.pinID.value\n" +
            "    if id ~= nil then world.pinInteractions[id] = interaction end\n" +
            "end\n\n" +
            "local function unindexPin(interaction)\n" +
            "    if not interaction or not interaction.pinID then return end\n" +
            "    local id = interaction.pinID.value\n" +
            "    if id ~= nil and world.pinInteractions[id] == interaction then\n" +
            "        world.pinInteractions[id] = nil\n" +
            "    end\n" +
            "end\n\n" +
            "local function findPinInteraction(mappin, mappinID)\n" +
            "    if mappinID == nil then return nil end\n" +
            "    local interaction = world.pinInteractions[mappinID]\n" +
            "    if interaction then return interaction end\n" +
            "    local pos = mappin:GetWorldPosition()\n" +
            "    for _, candidate in pairs(world.getGridInteractions(pos, true)) do\n" +
            "        if candidate.pinID and candidate.pinID.value == mappinID then\n" +
            "            world.pinInteractions[mappinID] = candidate\n" +
            "            return candidate\n" +
            "        end\n" +
            "    end\n" +
            "    return nil\n" +
            "end\n\n" +
            "local function getGridKey(position)\n",
            "nativeInteractions pin index helpers");

        var mappinReplacement =
            "    ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\", function(this)\n" +
            "        local mappin = this:GetMappin()\n" +
            "        if not mappin then return end\n" +
            "        local profile = this:GetProfile()\n" +
            "        if not profile or profile:GetID().value ~= \"WorldMappinUIProfile.nif\" then return end\n" +
            "        local interaction = findPinInteraction(mappin, mappin:GetNewMappinID().value)\n" +
            "        if not interaction then return end\n" +
            "        local record = TweakDBInterface.GetUIIconRecord(interaction.icon)\n" +
            "        this.iconWidget:SetAtlasResource(record:AtlasResourcePath())\n" +
            "        this.iconWidget:SetTexturePart(record:AtlasPartName())\n" +
            "        if interaction.iconColor then\n" +
            "            this.iconWidget:SetTintColor(HDRColor.new(interaction.iconColor))\n" +
            "        else\n" +
            "            this.iconWidget.widget:BindProperty(\"tintColor\", \"MainColors.Blue\")\n" +
            "        end\n" +
            "        interaction.pinController = ref.Weak(this)\n" +
            "    end)\n";

        worldText = ReplaceSemanticObserverBlockOnce(
            worldText,
            "ObserveAfter",
            "BaseMappinBaseController",
            "UpdateRootState",
            "this",
            mappinReplacement,
            "nativeInteractions mappin identity lookup",
            "WorldMappinUIProfile.nif",
            "world.getGridInteractions",
            "GetNewMappinID");

        worldText = ReplaceOnce(
            worldText,
            "    Override(\"NativeInteractions\", \"IsCustomMappin\", function (_, mappin)\n" +
            "        if mappin then\n" +
            "            local pos = mappin:GetWorldPosition()\n" +
            "            for _, interaction in pairs(world.getGridInteractions(pos, true)) do\n" +
            "                if interaction.pinID and interaction.pinID.value == mappin:GetNewMappinID().value then\n" +
            "                    return true\n" +
            "                end\n" +
            "            end\n" +
            "        end\n\n" +
            "        return false\n" +
            "    end)\n",
            "    Override(\"NativeInteractions\", \"IsCustomMappin\", function (_, mappin)\n" +
            "        if not mappin then return false end\n" +
            "        return findPinInteraction(mappin, mappin:GetNewMappinID().value) ~= nil\n" +
            "    end)\n",
            "nativeInteractions IsCustomMappin identity lookup");

        worldText = ReplaceOnce(
            worldText,
            "        if world.interactions[key].pinID then\n" +
            "            Game.GetMappinSystem():UnregisterMappin(world.interactions[key].pinID)\n" +
            "        end\n",
            "        if world.interactions[key].pinID then\n" +
            "            unindexPin(world.interactions[key])\n" +
            "            Game.GetMappinSystem():UnregisterMappin(world.interactions[key].pinID)\n" +
            "        end\n",
            "nativeInteractions remove pin index");

        worldText = ReplaceOnce(
            worldText,
            "        if interaction.pinID then\n" +
            "            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n" +
            "            local data = MappinData.new(",
            "        if interaction.pinID then\n" +
            "            unindexPin(interaction)\n" +
            "            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n" +
            "            local data = MappinData.new(",
            "nativeInteractions forceIcons unindex");

        worldText = ReplaceOnce(
            worldText,
            "            interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)\n" +
            "        end\n" +
            "    end\n" +
            "end\n\n" +
            "function world.togglePin",
            "            interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)\n" +
            "            indexPin(interaction)\n" +
            "        end\n" +
            "    end\n" +
            "end\n\n" +
            "function world.togglePin",
            "nativeInteractions forceIcons reindex");

        worldText = ReplaceOnce(
            worldText,
            "    if not state and interaction.pinID then\n" +
            "        Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n",
            "    if not state and interaction.pinID then\n" +
            "        unindexPin(interaction)\n" +
            "        Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n",
            "nativeInteractions togglePin unindex");

        worldText = ReplaceOnce(
            worldText,
            "        interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)\n" +
            "        world.pinnedInteractions[interaction] = true\n",
            "        interaction.pinID = Game.GetMappinSystem():RegisterMappin(data, interaction.pos)\n" +
            "        indexPin(interaction)\n" +
            "        world.pinnedInteractions[interaction] = true\n",
            "nativeInteractions togglePin index");

        worldText = ReplaceOnce(
            worldText,
            "function world.onSessionStart() -- Save loaded, all pins are gone\n" +
            "    world.activeInteractions = {}\n" +
            "    world.pinnedInteractions = {}\n",
            "function world.onSessionStart() -- Save loaded, all pins are gone\n" +
            "    world.activeInteractions = {}\n" +
            "    world.pinnedInteractions = {}\n" +
            "    world.pinInteractions = {}\n",
            "nativeInteractions session pin-index reset");

        worldText = ReplaceOnce(
            worldText,
            "        if interaction.pinID then\n" +
            "            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n" +
            "        end\n" +
            "    end\n" +
            "end\n\n" +
            "return world",
            "        if interaction.pinID then\n" +
            "            unindexPin(interaction)\n" +
            "            Game.GetMappinSystem():UnregisterMappin(interaction.pinID)\n" +
            "        end\n" +
            "    end\n" +
            "    world.pinInteractions = {}\n" +
            "end\n\n" +
            "return world",
            "nativeInteractions shutdown pin-index reset");

        context.Write(init, initText);
        context.Write(manager, managerText);
        context.Write(world, worldText);
        return SemanticInjectionResult.Success(
            "Kept scene/interaction work at rendered-frame cadence, moved dormant world discovery to ~30 Hz and manager maintenance to ~12 Hz with an immediate active-to-idle reconciliation tick, and indexed NIF mappins by ID while preserving a cold spatial fallback.");
    }

    private static SemanticInjectionResult ApplyMinimapWidgets(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "MinimapWidgetsConfig.ShowElevationArrow",
            "UpdateAboveBelowVerticalRelation",
            "updateFastWidgets",
            "updateSlowWidgets",
            "timerCoord");

        _ = FindOnUpdateOpening(file.Text, "deltaTime");
        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*deltaTime\s*\)\s*)\r?\n" +
            @"(?<comments>(?:[ \t]*--[^\r\n]*\r?\n)*)" +
            @"(?<game>[ \t]*if\s+isGameLoading\s+or\s+isPreGameState\s+or\s+not\s+isInitialized\s+then\s+return\s+end\s*\r?\n)" +
            @"(?<force>[ \t]*if\s+not\s+shouldForceUpdate\s+then\s+return\s+end\s*\r?\n)",
            "${opening}\n${comments}${game}${force}\n" +
            "    local __gcetMinimapDt = math.max(tonumber(deltaTime) or 0.0, 0.0)\n" +
            "    timerFast = timerFast + __gcetMinimapDt\n" +
            "    timerSlow = timerSlow + __gcetMinimapDt\n" +
            "    timerCoord = timerCoord + __gcetMinimapDt\n" +
            "    local __gcetMinimapFpsDue = MinimapWidgetsConfig.FPS == true and shouldCountFPS == true\n" +
            "    local __gcetMinimapFastDue = timerFast >= 0.2\n" +
            "    local __gcetMinimapSlowDue = timerSlow >= 1.0\n" +
            "    local __gcetMinimapCoordDue = timerCoord >= (tonumber(MinimapWidgetsConfig.CoordInterv) or 1.0)\n" +
            "    if not __gcetMinimapFpsDue and not __gcetMinimapFastDue and not __gcetMinimapSlowDue and not __gcetMinimapCoordDue then return end\n",
            "Minimap Widgets timer-first gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>[ \t]*)--\s*1\.\s*FPS[^\r\n]*\r?\n" +
            @"\k<indent>if\s+MinimapWidgetsConfig\.FPS\s*==\s*true\s+and\s+shouldCountFPS\s*==\s*true\s+then\s*\r?\n" +
            @"\k<indent>[ \t]+updateFpsCounter\(\)\s*\r?\n" +
            @"\k<indent>end\s*\r?\n",
            "${indent}-- 1. FPS\n${indent}if __gcetMinimapFpsDue then updateFpsCounter() end\n",
            "Minimap Widgets FPS due gate");

        text = ReplaceOnce(
            text,
            "    timerFast = timerFast + deltaTime\n" +
            "    if timerFast >= 0.2 then\n",
            "    if __gcetMinimapFastDue then\n",
            "Minimap Widgets fast timer");

        text = ReplaceOnce(
            text,
            "    timerSlow = timerSlow + deltaTime\n" +
            "    if timerSlow >= 1.0 then\n",
            "    if __gcetMinimapSlowDue then\n",
            "Minimap Widgets slow timer");

        text = ReplaceOnce(
            text,
            "    timerCoord = timerCoord + deltaTime\n" +
            "    if timerCoord >= MinimapWidgetsConfig.CoordInterv then\n",
            "    if __gcetMinimapCoordDue then\n",
            "Minimap Widgets coordinate timer");

        var elevationOld =
            "\t\t\tlocal vertRelation = this:GetVerticalRelationToPlayer()\n" +
            "\t\t\tlocal shouldShow = this:GetRootWidget():IsVisible() and not this:IsClamped()\n" +
            "\t\t\tlocal isAbove = vertRelation == gamemappinsVerticalPositioning.Above\n" +
            "\t\t\tlocal isBelow = vertRelation == gamemappinsVerticalPositioning.Below\n" +
            "\t\t\tif this:IsClamped() then \n";

        var elevationNew =
            "\t\t\tlocal isClamped = this:IsClamped()\n" +
            "\t\t\tif isClamped then\n";

        text = ReplaceOnce(
            text,
            elevationOld,
            elevationNew,
            "Minimap Widgets elevation prefilter");

        text = ReplaceOnce(
            text,
            "\t\t\telse \n" +
            "\t\t\t\tif this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end\n",
            "\t\t\telse\n" +
            "\t\t\t\tlocal vertRelation = this:GetVerticalRelationToPlayer()\n" +
            "\t\t\t\tlocal isAbove = vertRelation == gamemappinsVerticalPositioning.Above\n" +
            "\t\t\t\tlocal isBelow = vertRelation == gamemappinsVerticalPositioning.Below\n" +
            "\t\t\t\tif this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end\n",
            "Minimap Widgets elevation deferred query");

        var visibilityGate =
            "\t\tlocal hideEnemies = not MinimapWidgetsConfig.ShowEnemies\n" +
            "\t\tlocal hideNCPD = not MinimapWidgetsConfig.ShowNCPD\n" +
            "\t\tlocal hideLoot = not MinimapWidgetsConfig.ShowLoot\n" +
            "\t\tlocal hideDevices = not MinimapWidgetsConfig.ShowDevices\n" +
            "\t\tif not hideEnemies and not hideNCPD and not hideLoot and not hideDevices then return end\n";

        text = RegexReplaceOnce(
            text,
            @"(?ms)(ObserveAfter\s*\(\s*['""]MinimapStealthMappinController['""]\s*,\s*['""]Intro['""]\s*,\s*function\s*\(\s*this\s*\)\s*\r?\n[ \t]*if not IsDefined\(this\) then return end\s*\r?\n)",
            "$1" + visibilityGate,
            "Minimap Widgets Intro visibility prefilter");

        text = RegexReplaceOnce(
            text,
            @"(?ms)(ObserveAfter\s*\(\s*['""]MinimapStealthMappinController['""]\s*,\s*['""]Update['""]\s*,\s*function\s*\(\s*this\s*\)\s*\r?\n[ \t]*if not IsDefined\(this\) then return end\s*\r?\n)",
            "$1" + visibilityGate,
            "Minimap Widgets Update visibility prefilter");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved widget timers ahead of native-system acquisition so dormant frames exit before Game getters, deferred elevation queries until unclamped, and added a version-open visibility prefilter that skips stealth-mappin native work when no hide option is enabled.");
    }

    private static string RouteExactOnActionObserver(
        string text,
        string functionName,
        string owner,
        IReadOnlyList<string> actions,
        string label,
        params string[] requiredTokens)
    {
        var regex = new Regex(
            @"(?ms)^(?<indent>[ \t]*)Observe\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\s*\((?<args>[^)]*)\)\s*\r?\n(?<body>.*?)(?<close>^\k<indent>end\s*\)\s*;?\s*(?:--[^\r\n]*)?$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Singleline);

        var candidates = regex.Matches(text)
            .Cast<Match>()
            .Where(match => requiredTokens.All(token =>
                match.Value.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
            .ToList();

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"Current source no longer proves semantic OnAction structure: {label}.");
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                $"Semantic OnAction structure is ambiguous in current source: {label}.");

        var selected = candidates[0];
        var indent = selected.Groups["indent"].Value;
        var args = selected.Groups["args"].Value.Trim();
        var body = selected.Groups["body"].Value;
        var actionValues = string.Join(
            ", ",
            actions.Select(action => "\"" + action.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""));

        var stem = Regex.Replace(
            functionName,
            @"[^A-Za-z0-9_]+",
            "_",
            RegexOptions.CultureInvariant);
        var tableName = "__gcetActions_" + stem;
        var okName = "__gcetOk_" + stem;
        var engineName = "__gcetEngine_" + stem;
        var clientName = "__gcetClient_" + stem;
        var routedName = "__gcetRouted_" + stem;

        var replacement =
            indent + "local function " + functionName + "(" + args + ")\n" +
            body +
            indent + "end\n\n" +
            indent + "local " + tableName + " = { " + actionValues + " }\n" +
            indent + "local " + okName + ", " + engineName + " = pcall(GetMod, \"0-Engine\")\n" +
            indent + "local " + clientName + " = nil\n" +
            indent + "if " + okName + " and type(" + engineName + ") == \"table\" and type(" + engineName + ".Register) == \"function\" then\n" +
            indent + "    " + clientName + " = " + engineName + ".Register(\"" + owner + "\")\n" +
            indent + "end\n" +
            indent + "local " + routedName + " = false\n" +
            indent + "if type(" + clientName + ") == \"table\" and type(" + clientName + ".SubscribeAction) == \"function\" then\n" +
            indent + "    " + routedName + " = pcall(function() " + clientName + ".SubscribeAction({ actions = " + tableName + " }, " + functionName + ") end)\n" +
            indent + "elseif " + okName + " and type(" + engineName + ") == \"table\" and type(" + engineName + ".SubscribeAction) == \"function\" then\n" +
            indent + "    " + routedName + " = pcall(function() " + engineName + ".SubscribeAction({ actions = " + tableName + " }, " + functionName + ", \"" + owner + "\") end)\n" +
            indent + "end\n" +
            indent + "if not " + routedName + " then " + selected.Groups["observer"].Value + "(\"PlayerPuppet\", \"OnAction\", " + functionName + ") end";

        return text[..selected.Index] +
            replacement +
            text[(selected.Index + selected.Length)..];
    }

    private static string FindOnUpdateOpening(
        string text,
        string parameter)
    {
        var match = Regex.Match(
            text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*" +
            Regex.Escape(parameter) +
            @"\s*\)\s*(?:--[^\r\n]*)?$)",
            RegexOptions.CultureInvariant);

        if (!match.Success)
            throw new InvalidOperationException(
                $"Could not uniquely find onUpdate({parameter}) opening.");

        var second = match.NextMatch();
        if (second.Success)
            throw new InvalidOperationException(
                $"More than one onUpdate({parameter}) opening matched.");

        return match.Groups["opening"].Value + "\n";
    }


    private static string ReplaceSemanticObserverBlockOnce(
        string text,
        string observerFunction,
        string typeName,
        string methodName,
        string parameter,
        string replacement,
        string label,
        params string[] requiredTokens)
    {
        var pattern =
            @"^[ \t]*" + Regex.Escape(observerFunction) +
            @"\s*\(\s*['""]" + Regex.Escape(typeName) + @"['""]\s*,\s*['""]" +
            Regex.Escape(methodName) + @"['""]\s*,\s*function\s*\(\s*" +
            Regex.Escape(parameter) +
            @"\s*\)[^\r\n]*\r?\n.*?^[ \t]*end\s*\)\s*(?:--[^\r\n]*)?(?:\r?\n|$)";

        var regex = new Regex(
            pattern,
            RegexOptions.CultureInvariant |
            RegexOptions.Multiline |
            RegexOptions.Singleline);

        var candidates = new List<Match>();
        foreach (Match match in regex.Matches(text))
        {
            if (requiredTokens.All(token =>
                    match.Value.IndexOf(
                        token,
                        StringComparison.OrdinalIgnoreCase) >= 0))
            {
                candidates.Add(match);
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"Current source no longer proves semantic observer structure: {label}.");
        if (candidates.Count != 1)
            throw new InvalidOperationException(
                $"Semantic observer structure is ambiguous in current source: {label}.");

        var selected = candidates[0];
        return text[..selected.Index] +
            replacement +
            text[(selected.Index + selected.Length)..];
    }


    private static string RegexReplaceOnce(
        string text,
        string pattern,
        string replacement,
        string label)
    {
        var regex = new Regex(
            pattern,
            RegexOptions.CultureInvariant);

        var matches = regex.Matches(text);
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Current source no longer matches semantic anchor: {label}.");
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Semantic anchor is ambiguous in current source: {label}.");

        return regex.Replace(
            text,
            replacement,
            1);
    }

    private static string ReplaceOnce(
        string text,
        string oldValue,
        string newValue,
        string label)
    {
        var first = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (first >= 0)
        {
            var second = text.IndexOf(
                oldValue,
                first + oldValue.Length,
                StringComparison.Ordinal);
            if (second >= 0)
                throw new InvalidOperationException(
                    $"Semantic anchor is ambiguous in current source: {label}.");

            return text[..first] + newValue + text[(first + oldValue.Length)..];
        }

        // Updated mod versions often change indentation, line wrapping, or
        // single/double quote style without changing behavior. Fall back to a
        // token-preserving literal match: whitespace is flexible, quotes may
        // differ, but identifiers/operators/punctuation must remain identical.
        var pattern = BuildFlexibleLiteralPattern(oldValue);
        var regex = new Regex(
            pattern,
            RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var matches = regex.Matches(text);
        if (matches.Count == 0)
            throw new InvalidOperationException(
                $"Current source no longer matches semantic anchor: {label}.");
        if (matches.Count != 1)
            throw new InvalidOperationException(
                $"Semantic anchor is ambiguous in current source: {label}.");

        var match = matches[0];
        return text[..match.Index] +
            newValue +
            text[(match.Index + match.Length)..];
    }

    private static string BuildFlexibleLiteralPattern(string literal)
    {
        var pattern = new StringBuilder(literal.Length * 2);
        var i = 0;
        while (i < literal.Length)
        {
            var ch = literal[i];
            if (char.IsWhiteSpace(ch))
            {
                var start = i;
                while (i < literal.Length && char.IsWhiteSpace(literal[i]))
                    i++;

                var previous = start > 0 ? literal[start - 1] : '\0';
                var next = i < literal.Length ? literal[i] : '\0';
                var requiresSeparator =
                    IsIdentifierChar(previous) &&
                    IsIdentifierChar(next);
                pattern.Append(requiresSeparator ? @"\s+" : @"\s*");
                continue;
            }

            if (ch == '\'' || ch == '"')
            {
                pattern.Append(@"['""]");
                i++;
                continue;
            }

            pattern.Append(Regex.Escape(ch.ToString()));
            i++;
        }

        return pattern.ToString();
    }

    private static bool IsIdentifierChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';
}
