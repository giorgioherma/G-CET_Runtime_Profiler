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
            "teleport-gateway-system" => ApplyTeleportGatewaySystem(context),
            "discard-ammo-on-reload" => ApplyDiscardAmmoOnReload(context),
            "advanced-settings" => ApplyAdvancedSettings(context),
            "auto-ammo-crafting" => ApplyAutoAmmoCrafting(context),
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
            @"(?ms)^(?<indent>[ \t]*)Observe\s*\(\s*[""']PlayerPuppet[""']\s*,\s*[""']OnAction[""']\s*,\s*function\s*\(\s*_\s*,\s*action\s*\)\s*\r?\n\s*input:SetInputData\s*\(\s*action\s*\)\s*\r?\n\s*end\s*\)\s*$",
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
            "function AdvancedMobility.Tick()",
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

        for (var i = 0; i < 3; i++)
        {
            registryText = ReplaceOnce(
                registryText,
                "    local Bindings = require(\"Controls/Bindings\")\n",
                "    local Bindings = __gcetGetBindings()\n",
                $"EasyTrainer cached bindings lookup {i + 1}");
        }

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
            "function AdvancedMobility.Tick()\n" +
            "    HandleStatToggle(AdvancedMobility.toggleDoubleJump, \"HasDoubleJump\", \"doubleJumpApplied\", \"Double Jump\")",
            "function AdvancedMobility.Tick()\n" +
            "    if not AdvancedMobility.toggleDoubleJump.value\n" +
            "        and not AdvancedMobility.toggleAirHover.value\n" +
            "        and not AdvancedMobility.toggleChargeJump.value\n" +
            "        and not state.doubleJumpApplied\n" +
            "        and not state.airHoverApplied\n" +
            "        and not state.chargeJumpApplied then return end\n" +
            "    HandleStatToggle(AdvancedMobility.toggleDoubleJump, \"HasDoubleJump\", \"doubleJumpApplied\", \"Double Jump\")",
            "EasyTrainer AdvancedMobility dormant guard");
        context.Write(mobility, mobilityText);

        var superSpeedText = ReplaceOnce(
            superSpeed.Text,
            "function SuperSpeed.Tick()\n" +
            "    local timeSystem = Game.GetTimeSystem()",
            "function SuperSpeed.Tick()\n" +
            "    if not SuperSpeed.enabled.value and not applied then return end\n" +
            "    local timeSystem = Game.GetTimeSystem()",
            "EasyTrainer SuperSpeed dormant guard");
        context.Write(superSpeed, superSpeedText);

        var thrusterText = ReplaceOnce(
            thrusters.Text,
            "function AirThrusterBoots.Tick()\n" +
            "    local stats = Game.GetStatsSystem()",
            "function AirThrusterBoots.Tick()\n" +
            "    if not AirThrusterBoots.enabled.value and not applied then return end\n" +
            "    local stats = Game.GetStatsSystem()",
            "EasyTrainer AirThrusterBoots dormant guard");
        context.Write(thrusters, thrusterText);

        return SemanticInjectionResult.Success(
            "Proved EasyTrainer's Event.Observe wrapper is transparent, routed only CameraMouseX/RangedAttack through 0-Engine with wrapper fallback, cached hotkey IDs/bindings, stopped closed-menu input-device polling, and added transition-safe dormant guards around three measured native-heavy disabled features.");
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

        var opening = Regex.Match(
            file.Text,
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*deltaTime\s*\)\s*)$");
        if (!opening.Success)
            throw new InvalidOperationException("Teleport Gateway onUpdate opening was not found.");

        var text = ReplaceOnce(
            file.Text,
            opening.Value,
            "local __gcetGatewayScanElapsed = 0.0\n" +
            "local __gcetGatewayNear = true\n" +
            "local __gcetGatewayFarInterval = 0.10\n" +
            "local __gcetGatewayNearMargin = 30.0\n\n" +
            opening.Value,
            "Teleport Gateway adaptive scan state");

        var oldScan =
            "\tif (TGS.activated) then\n" +
            "\t\tplayerPos = TGS.player:GetWorldPosition()\n" +
            "\t\tplayerAng = TGS.cameraSys:GetActiveCameraForward()\n" +
            "\t\tplayerYaw = TGS.player:GetWorldYaw()\n" +
            "\t\t\n" +
            "\t\tsenseGWCheck = false\n" +
            "\t\t\n" +
            "\t\tif (#gatewayDB > 0) then\n" +
            "\t\t\tfor index = 1, #gatewayDB, 1 do\n" +
            "\t\t\t\tGWDist = math.sqrt(((TGS.player:GetWorldPosition().x-gatewayDB[index].gwx)^2)+((TGS.player:GetWorldPosition().y-gatewayDB[index].gwy)^2)+((TGS.player:GetWorldPosition().z-gatewayDB[index].gwz)^2))\n" +
            "\t\t\t\tif (TGS.senseInhibited or senseSystemOff) then\n" +
            "\t\t\t\t\tif (GWDist <= gatewayDB[index].gwr) then\n" +
            "\t\t\t\t\t\tsenseGWCheck = true\n" +
            "\t\t\t\t\tend\n" +
            "\t\t\t\telseif (GWDist <= gatewayDB[index].gwr) then\n" +
            "\t\t\t\t\tprint(\"TeleportGatewaySystem: Teleport TGS.activated -\",gatewayDB[index].name)\n" +
            "\t\t\t\t\t\n" +
            "\t\t\t\t\tTGS.teleportFac:Teleport(TGS.player, Vector4.new(gatewayDB[index].spx, gatewayDB[index].spy, gatewayDB[index].spz, 1), EulerAngles.new(playerAng.x, playerAng.y, gatewayDB[index].yaw))\n" +
            "\t\t\t\t\tTGS.senseInhibited = true\n" +
            "\t\t\t\tend\n" +
            "\t\t\tend\n" +
            "\t\tend\n" +
            "\tend";

        var newScan =
            "\tif (TGS.activated) then\n" +
            "\t\t__gcetGatewayScanElapsed = __gcetGatewayScanElapsed + deltaTime\n" +
            "\t\tlocal __gcetGatewayRealtime = TGS.showMainWindow == true or ((not senseSystemOff) and __gcetGatewayNear)\n" +
            "\t\tif __gcetGatewayRealtime or __gcetGatewayScanElapsed >= __gcetGatewayFarInterval then\n" +
            "\t\t\t__gcetGatewayScanElapsed = 0.0\n" +
            "\t\t\tplayerPos = TGS.player:GetWorldPosition()\n" +
            "\t\t\tplayerAng = TGS.cameraSys:GetActiveCameraForward()\n" +
            "\t\t\tplayerYaw = TGS.player:GetWorldYaw()\n" +
            "\t\t\tsenseGWCheck = false\n" +
            "\t\t\tlocal __gcetGatewayNearNow = false\n" +
            "\t\t\tif (#gatewayDB > 0) then\n" +
            "\t\t\t\tfor index = 1, #gatewayDB, 1 do\n" +
            "\t\t\t\t\tlocal __gcetDx = playerPos.x - gatewayDB[index].gwx\n" +
            "\t\t\t\t\tlocal __gcetDy = playerPos.y - gatewayDB[index].gwy\n" +
            "\t\t\t\t\tlocal __gcetDz = playerPos.z - gatewayDB[index].gwz\n" +
            "\t\t\t\t\tGWDist = math.sqrt((__gcetDx * __gcetDx) + (__gcetDy * __gcetDy) + (__gcetDz * __gcetDz))\n" +
            "\t\t\t\t\tif GWDist <= gatewayDB[index].gwr + __gcetGatewayNearMargin then __gcetGatewayNearNow = true end\n" +
            "\t\t\t\t\tif (TGS.senseInhibited or senseSystemOff) then\n" +
            "\t\t\t\t\t\tif (GWDist <= gatewayDB[index].gwr) then\n" +
            "\t\t\t\t\t\t\tsenseGWCheck = true\n" +
            "\t\t\t\t\t\tend\n" +
            "\t\t\t\t\telseif (GWDist <= gatewayDB[index].gwr) then\n" +
            "\t\t\t\t\t\tprint(\"TeleportGatewaySystem: Teleport TGS.activated -\",gatewayDB[index].name)\n" +
            "\t\t\t\t\t\tTGS.teleportFac:Teleport(TGS.player, Vector4.new(gatewayDB[index].spx, gatewayDB[index].spy, gatewayDB[index].spz, 1), EulerAngles.new(playerAng.x, playerAng.y, gatewayDB[index].yaw))\n" +
            "\t\t\t\t\t\tTGS.senseInhibited = true\n" +
            "\t\t\t\t\tend\n" +
            "\t\t\t\tend\n" +
            "\t\t\tend\n" +
            "\t\t\t__gcetGatewayNear = __gcetGatewayNearNow\n" +
            "\t\tend\n" +
            "\tend";

        text = ReplaceOnce(
            text,
            oldScan,
            newScan,
            "Teleport Gateway far-idle / near-realtime scan split");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<opening>[ \t]*registerForEvent\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)$",
            "$" + "{opening}\n\tif not TGS.showMainWindow then return end",
            "Teleport Gateway hidden-window draw gate");

        context.Write(file, text);

        return SemanticInjectionResult.Success(
            "Kept the gateway state machine/cooldown frame-fed, but reduced the expensive world scan to 10 Hz while farther than 30 m, automatically restored full-rate sensing near a gateway or while its window is open, reused one player position per scan, and skipped hidden-window ImGui work.");
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
            "settings.combatCheck and inCombat");

        var text = ReplaceOnce(
            file.Text,
            "scriptInterval = 0\n" +
            "combatMagsCrafted = 0",
            "scriptInterval = 0\n" +
            "local __gcetAutoAmmoReadyProbeElapsed = 0.10\n" +
            "local __gcetAutoAmmoReadyCached = false\n" +
            "combatMagsCrafted = 0",
            "Auto Ammo readiness sentinel state");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^(?<indent>[ \t]*)if\s+pauseTime\s*>\s*os\.time\(\)\s*then\s*\r?\n[ \t]*return\s*\r?\n[ \t]*end\s*\r?\n[ \t]*if\s+settings\.combatCheck\s+and\s+inCombat\s+then\s*\r?\n[ \t]*pauseTime\s*=\s*os\.time\(\)\s*\+\s*3\s*\r?\n[ \t]*return\s*\r?\n[ \t]*end\s*\r?\n[ \t]*if\s+notReady\(\)\s*then\s*\r?\n[ \t]*pauseTime\s*=\s*os\.time\(\)\s*\+\s*3\s*\r?\n[ \t]*return\s*\r?\n[ \t]*end\s*\r?\n[ \t]*if\s+playerInMenu\(\)\s*then\s*\r?\n[ \t]*pauseTime\s*=\s*os\.time\(\)\s*\+\s*3\s*\r?\n[ \t]*return\s*\r?\n[ \t]*end\s*$",
            "${indent}if pauseTime > os.time() then\n" +
            "${indent}\treturn\n" +
            "${indent}end\n" +
            "${indent}if settings.combatCheck and inCombat then\n" +
            "${indent}\t__gcetAutoAmmoReadyCached = false\n" +
            "${indent}\t__gcetAutoAmmoReadyProbeElapsed = 0.10\n" +
            "${indent}\tpauseTime = os.time() + 3\n" +
            "${indent}\treturn\n" +
            "${indent}end\n" +
            "${indent}scriptInterval = scriptInterval + deltaTime\n" +
            "${indent}__gcetAutoAmmoReadyProbeElapsed = __gcetAutoAmmoReadyProbeElapsed + deltaTime\n" +
            "${indent}local __gcetAutoAmmoCraftDue = scriptInterval >= settings.autoConvertTime\n" +
            "${indent}local __gcetAutoAmmoProbeDue = (not __gcetAutoAmmoReadyCached)\n" +
            "${indent}\tor __gcetAutoAmmoReadyProbeElapsed >= 0.10\n" +
            "${indent}\tor __gcetAutoAmmoCraftDue\n" +
            "${indent}if __gcetAutoAmmoProbeDue then\n" +
            "${indent}\t__gcetAutoAmmoReadyProbeElapsed = 0.0\n" +
            "${indent}\tif notReady() or playerInMenu() then\n" +
            "${indent}\t\t__gcetAutoAmmoReadyCached = false\n" +
            "${indent}\t\tpauseTime = os.time() + 3\n" +
            "${indent}\t\treturn\n" +
            "${indent}\tend\n" +
            "${indent}\t__gcetAutoAmmoReadyCached = true\n" +
            "${indent}end",
            "Auto Ammo 10 Hz readiness sentinel");

        text = RegexReplaceOnce(
            text,
            @"(?ms)^(?<indent>[ \t]*)player\s*=\s*Game\.GetPlayerSystem\(\):GetLocalPlayerMainGameObject\(\)\s*\r?\n[ \t]*if\s+not\s+ts\s+then\s+ts\s*=\s*__gcetGetTransactionSystem\(\)\s+end\s*\r?\n(?<ready>------------------------------------------------\r?\n-- Ready for take off\.\r?\n------------------------------------------------\r?\n)[ \t]*scriptInterval\s*=\s*scriptInterval\s*\+\s*deltaTime\s*\r?\n[ \t]*if\s+scriptInterval\s*<\s*settings\.autoConvertTime\s+then\s*\r?\n[ \t]*return\s*\r?\n[ \t]*else\s*\r?\n[ \t]*scriptInterval\s*=\s*0\s*\r?\n[ \t]*end\s*$",
            "${ready}${indent}if not __gcetAutoAmmoCraftDue then\n" +
            "${indent}\treturn\n" +
            "${indent}end\n" +
            "${indent}scriptInterval = 0\n" +
            "${indent}player = Game.GetPlayerSystem():GetLocalPlayerMainGameObject()\n" +
            "${indent}if not ts then ts = __gcetGetTransactionSystem() end",
            "Auto Ammo full work only when author cadence is due");

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
            "Kept the author's combat pause and autoConvertTime craft cadence, replaced per-frame readiness/menu probing with a 10 Hz sentinel plus a mandatory fresh probe before every craft pass, deferred player/transaction acquisition until the craft pass is due, and collapsed duplicate PlayerSystem/Player/UI blackboard reads.");
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

    private static string FindOnUpdateOpening(
        string text,
        string parameter)
    {
        var match = Regex.Match(
            text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*" +
            Regex.Escape(parameter) +
            @"\s*\)\s*$)",
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
        if (first < 0)
            throw new InvalidOperationException(
                $"Current source no longer matches semantic anchor: {label}.");

        var second = text.IndexOf(
            oldValue,
            first + oldValue.Length,
            StringComparison.Ordinal);
        if (second >= 0)
            throw new InvalidOperationException(
                $"Semantic anchor is ambiguous in current source: {label}.");

        return text[..first] + newValue + text[(first + oldValue.Length)..];
    }
}
