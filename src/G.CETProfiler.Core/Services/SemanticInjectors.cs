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
