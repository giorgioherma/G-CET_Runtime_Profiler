using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyAutoLootWorldPinAllocationScan(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "modules/logic.lua",
            "function isWorldMapPinOfTypeInRange(",
            "if takeIt then tableInsert(closeMappins, mappin) end",
            "local function vectorDistanceSquared(src, target)",
            "return dx * dx + dy * dy + dz * dz;",
            "mappinSystem:GetMappinEntries(gamemappinsMappinTargetTypeWorld)",
            "mappinSystem:GetMappins(gamemappinsMappinTargetTypeWorld)");
        var text = file.Text;
        var worldSpan = SemanticScopedStages.RequireFunction(
            text, "isWorldMapPinOfTypeInRange");
        var worldFunction = worldSpan.Text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string Native(string value) =>
            value.Replace("\n", newline, StringComparison.Ordinal);

        worldFunction = ReplaceOnce(
            worldFunction,
            Native("\tlocal closeMappins = {}\n\tlocal worldPosition\n"),
            Native("\tlocal firstMappin, nearestMappin\n" +
                   "\tlocal matchingMappins = 0\n" +
                   "\tlocal nearestIndex = 0\n" +
                   "\tlocal nearestDistanceSquared = 1000000000\n" +
                   "\tlocal worldPosition\n"),
            "AutoLoot fresh candidate buffer");
        worldFunction = ReplaceOnce(
            worldFunction,
            "if takeIt then tableInsert(closeMappins, mappin) end",
            Native("if takeIt then\n" +
                   "\t\t\t\t\t\tmatchingMappins = matchingMappins + 1\n" +
                   "\t\t\t\t\t\tif matchingMappins == 1 then\n" +
                   "\t\t\t\t\t\t\tfirstMappin = mappin\n" +
                   "\t\t\t\t\t\telse\n" +
                   "\t\t\t\t\t\t\tif matchingMappins == 2 then\n" +
                   "\t\t\t\t\t\t\t\tlocal dist = vectorDistanceSquared(objectPos, firstMappin.worldPosition)\n" +
                   "\t\t\t\t\t\t\t\tif dist < nearestDistanceSquared then\n" +
                   "\t\t\t\t\t\t\t\t\tnearestDistanceSquared = dist\n" +
                   "\t\t\t\t\t\t\t\t\tnearestIndex = 1\n" +
                   "\t\t\t\t\t\t\t\t\tnearestMappin = firstMappin\n" +
                   "\t\t\t\t\t\t\t\tend\n" +
                   "\t\t\t\t\t\t\tend\n" +
                   "\t\t\t\t\t\t\tlocal dist = vectorDistanceSquared(objectPos, mappin.worldPosition)\n" +
                   "\t\t\t\t\t\t\tif dist < nearestDistanceSquared then\n" +
                   "\t\t\t\t\t\t\t\tnearestDistanceSquared = dist\n" +
                   "\t\t\t\t\t\t\t\tnearestIndex = matchingMappins\n" +
                   "\t\t\t\t\t\t\t\tnearestMappin = mappin\n" +
                   "\t\t\t\t\t\t\tend\n" +
                   "\t\t\t\t\t\tend\n" +
                   "\t\t\t\t\tend"),
            "AutoLoot single-pass candidate selection");

        var oldTail = Native(
            "\tlocal size = #closeMappins\n" +
            "\tif size == 0 then return false end\n" +
            "\tif size == 1 then return closeMappins[1] end\n" +
            "\n" +
            "\tlowestDistanceIndex = 0\n" +
            "\tlowestDistanceSquared = 1000000000\n" +
            "\n" +
            "\tfor i = 1, size do\n" +
            "\t\tlocal distSquared = vectorDistanceSquared(objectPos, closeMappins[i].worldPosition)\n" +
            "\t\tif distSquared < lowestDistanceSquared then\n" +
            "\t\t\tlowestDistanceSquared = distSquared\n" +
            "\t\t\tlowestDistanceIndex = i\n" +
            "\t\tend\n" +
            "\tend\n" +
            "\n" +
            "\tif lowestDistanceIndex > 0 and lowestDistanceSquared <= range * range then\n" +
            "\t\treturn closeMappins[lowestDistanceIndex]\n" +
            "\tend");
        var newTail = Native(
            "\tif matchingMappins == 0 then return false end\n" +
            "\tif matchingMappins == 1 then return firstMappin end\n" +
            "\n" +
            "\tlowestDistanceIndex = nearestIndex\n" +
            "\tlowestDistanceSquared = nearestDistanceSquared\n" +
            "\tif lowestDistanceIndex > 0 and lowestDistanceSquared <= range * range then\n" +
            "\t\treturn nearestMappin\n" +
            "\tend");
        worldFunction = ReplaceOnce(
            worldFunction, oldTail, newTail,
            "AutoLoot nearest candidate and original global-distance results");
        text = text[..worldSpan.Start] + worldFunction + text[worldSpan.End..];
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Removed the fresh world-map pin candidate table and table.insert " +
            "calls; retained one live native query, filter order, single-candidate " +
            "behavior, first-nearest tie policy, and original update cadence.");
    }


    // Compose the proven allocation-only world-pin selection with the independently
    // in-game-tested radius-first rejection. Never touch loot transfer/quest/UI paths.
    // Input may be author 3.11.1, world-pin-only optimized, or radius-first-only.
    private static SemanticInjectionResult ApplyAutoLootWorldPinAndEarlyDistance(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "modules/logic.lua",
            "function logic.lootInRange(",
            "function isWorldMapPinOfTypeInRange(",
            "result, items = transactionSystem:GetItemList(gameObj)",
            "takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared",
            "local function vectorDistanceSquared(src, target)");
        var text = file.Text;
        var worldSource = SemanticScopedStages.RequireFunction(
            text, "isWorldMapPinOfTypeInRange").Text;
        // Diagnose each independent stage in the function that owns it.
        // Other quest-pin helpers may legitimately use closeMappins.
        var worldOriginal = SemanticScopedStages.ContainsToken(
            worldSource, "if takeIt then tableInsert(closeMappins, mappin) end");
        var worldOptimized = SemanticScopedStages.ContainsToken(
            worldSource, "local firstMappin, nearestMappin") &&
            SemanticScopedStages.ContainsToken(
                worldSource, "if matchingMappins == 1 then return firstMappin end") &&
            !SemanticScopedStages.ContainsToken(
                worldSource, "local closeMappins = {}");
        if (worldOriginal == worldOptimized)
            throw new InvalidOperationException(
                "AutoLoot world-pin selection is ambiguous or partly rewritten; refusing to guess.");

        if (worldOriginal)
        {
            // Reuse the existing verified scalar-selection source injector.
            // Its output marker uses this combined rule's own identity.
            var world = ApplyAutoLootWorldPinAllocationScan(context);
            if (!world.Applied)
                throw new InvalidOperationException(
                    "AutoLoot world-pin optimization was not generated.");
            text = context.FindFile(
                "modules/logic.lua",
                "local firstMappin, nearestMappin",
                "function logic.lootInRange(").Text;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string Native(string value) =>
            value.Replace("\n", newline, StringComparison.Ordinal);

        var oldGate = Native("\t\t\t\t\tif isNpcSafeToLoot(gameObj) then\n\t\t\t\t\t\tlocal hasItemsToLoot, objectHasLootItems, ownerHasLootItems = false, false, false\n\t\t\t\t\t\tresult, items = transactionSystem:GetItemList(gameObj)\n\t\t\t\t\t\tif result and #items > 0 then hasItemsToLoot = true objectHasLootItems = true end\n\t\t\t\t\t\tlocal gameObjOwner = nil\n\t\t\t\t\t\tresult = false\n\t\t\t\t\t\tpcall(function() gameObjOwner = gameObj:GetOwner() end)\n\t\t\t\t\t\tif gameObjOwner then result, items = transactionSystem:GetItemList(gameObjOwner) end\n\t\t\t\t\t\tif result and #items > 0 then hasItemsToLoot = true ownerHasLootItems = true end\n\t\t\t\t\t\tif hasItemsToLoot then\n\t\t\t\t\t\t\tlocal takeIt = true\n\t\t\t\t\t\t\tlocal gameObjPos\n\t\t\t\t\t\t\tif lootInRange then\n\t\t\t\t\t\t\t\tgameObjPos = gameObj:GetWorldPosition()\n\t\t\t\t\t\t\t\ttakeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared\n\t\t\t\t\t\t\tend\n\t\t\t\t\t\t\tif takeIt then\n");
        var newGate = Native("\t\t\t\t\tif isNpcSafeToLoot(gameObj) then\n\t\t\t\t\t\tlocal takeIt = true\n\t\t\t\t\t\tlocal gameObjPos\n\t\t\t\t\t\tif lootInRange then\n\t\t\t\t\t\t\tgameObjPos = gameObj:GetWorldPosition()\n\t\t\t\t\t\t\ttakeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared\n\t\t\t\t\t\tend\n\t\t\t\t\t\tif takeIt then\n\t\t\t\t\t\t\tlocal hasItemsToLoot, objectHasLootItems, ownerHasLootItems = false, false, false\n\t\t\t\t\t\t\tresult, items = transactionSystem:GetItemList(gameObj)\n\t\t\t\t\t\t\tif result and #items > 0 then hasItemsToLoot = true objectHasLootItems = true end\n\t\t\t\t\t\t\tlocal gameObjOwner = nil\n\t\t\t\t\t\t\tresult = false\n\t\t\t\t\t\t\tpcall(function() gameObjOwner = gameObj:GetOwner() end)\n\t\t\t\t\t\t\tif gameObjOwner then result, items = transactionSystem:GetItemList(gameObjOwner) end\n\t\t\t\t\t\t\tif result and #items > 0 then hasItemsToLoot = true ownerHasLootItems = true end\n\t\t\t\t\t\t\tif hasItemsToLoot then\n");
        var lootSpan = SemanticScopedStages.RequireFunction(
            text, "logic.lootInRange");
        var lootSource = lootSpan.Text;
        // Version compatibility: accept quote/whitespace variation, but
        // refuse changed transaction/visibility/protection order.
        bool MatchesShape(string fragment) =>
            lootSource.Contains(fragment, StringComparison.Ordinal) ||
            Regex.IsMatch(lootSource, BuildFlexibleLiteralPattern(fragment),
                RegexOptions.CultureInvariant | RegexOptions.Singleline);
        var hasOldGate = MatchesShape(oldGate);
        var hasNewGate = MatchesShape(newGate);
        if (hasOldGate == hasNewGate)
            throw new InvalidOperationException(
                "AutoLoot radius-first ordering is missing or ambiguous; refusing to change inventories.");

        if (hasOldGate)
        {
            lootSource = ReplaceOnce(lootSource, oldGate, newGate,
                "AutoLoot radius rejection before both inventory queries");
            text = text[..lootSpan.Start] + lootSource + text[lootSpan.End..];
        }

        // An entirely already-optimized but unmarked variant is not rewritten.
        if (text == file.Text)
            return SemanticInjectionResult.Skip(
                "Both AutoLoot source postconditions are already satisfied.");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved original transfer, quest, shard, visibility and marker code; " +
            "applied only missing world-pin scalar selection and early radius gate.");
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

        var alreadyRouted = Regex.IsMatch(file.Text,
            @"\b(?:zeroEngineMod|__gcetClient_gcetAutoLootOnAction)\.SubscribeAction\s*\(",
            RegexOptions.CultureInvariant) &&
            file.Text.Contains("zeroEngineOnAction", StringComparison.Ordinal)
                || (file.Text.Contains("gcetAutoLootOnAction", StringComparison.Ordinal) &&
                    file.Text.Contains("SubscribeAction", StringComparison.Ordinal) &&
                    file.Text.Contains("Observe", StringComparison.Ordinal));
        var text = alreadyRouted ? file.Text : RouteExactOnActionObserver(
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

        // Source-specific C-stage: the mod's own default-action setting
        // governs only its shared native OnAction subscriber. The independent
        // CET hotkey, event observers and onUpdate wake remain untouched.
        text = ExtendAutoLootDefaultActionSubscriber(text);
        if (text == file.Text)
            return SemanticInjectionResult.Skip(
                "AutoLoot finite action routing and source-supported subscriber dormancy already complete.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved AutoLoot trigger/looting paths, exact-action routing and native fallback; source-backed default-action subscriber sleeps when author setting is off.");
    }



    // GigCore 0.7.x: invocation-local, no persistent native handle.
    // Complete authoritative GigCore optimization: both pristine and
    // previously optimized sources converge in ONE Resolver pass.
    // All changes are invocation-local and retain protected frame semantics.

    private static string ExtendAutoLootDefaultActionSubscriber(string text)
    {
        const string stage = "-- __gcetAutoLootDefaultActionDormancyV2";
        if (text.Contains(stage, StringComparison.Ordinal))
        {
            if (!text.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) ||
                !text.Contains("__gcetAutoLootActionHandle = ", StringComparison.Ordinal) ||
                !text.Contains("__gcetAutoLootSyncDefaultAction()", StringComparison.Ordinal))
                throw new InvalidOperationException("AutoLoot default-action subscriber stage drifted.");
            return text;
        }
        if (text.Contains("__gcetAutoLootSyncDefaultAction", StringComparison.Ordinal) ||
            text.Contains("__gcetAutoLootActionHandle", StringComparison.Ordinal))
            throw new InvalidOperationException("AutoLoot source has a partial subscriber stage.");

        // Some older or synthetic AutoLoot sources do not contain the mod's
        // actual onInit/settings/UI wake graph. Preserve their existing
        // route; do not invent subscription state without real source proof.
        if (!text.Contains("autoLoot.ui.onInit()", StringComparison.Ordinal) ||
            !text.Contains("autoLoot.settings.useDefaultActionKey", StringComparison.Ordinal) ||
            !text.Contains("handleTriggers()", StringComparison.Ordinal))
            return text;

        var subscriptions = new Regex(
            @"(?<call>(?:zeroEngineMod|zeroEngine|__gcet(?:Client|Engine)_gcetAutoLootOnAction)\.SubscribeAction\([^\r\n]*\))",
            RegexOptions.CultureInvariant);
        if (subscriptions.Matches(text).Count != 2 ||
            !text.Contains("Observe(", StringComparison.Ordinal))
            throw new InvalidOperationException("AutoLoot known native action route/fallback changed.");
        text = subscriptions.Replace(text,
            m => "__gcetAutoLootActionHandle = " + m.Groups["call"].Value);
        var init = new Regex(
            @"(?m)^(?<opening>[ \t]*registerForEvent\s*\(\s*['""]onInit['""]\s*,\s*function\s*\(\s*\)\s*)$",
            RegexOptions.CultureInvariant);
        if (init.Matches(text).Count != 1)
            throw new InvalidOperationException("AutoLoot onInit not uniquely source-proven.");
        var guard =
            stage + ": only default action input, not custom CET triggers\n" +
            "local __gcetAutoLootActionHandle = nil\n" +
            "local __gcetAutoLootActionState = nil\n" +
            "local function __gcetAutoLootSyncDefaultAction()\n" +
            "    local handle = __gcetAutoLootActionHandle\n" +
            "    if type(handle) ~= \"table\" or type(handle.SetActive) ~= \"function\" then return end\n" +
            "    local needed = autoLoot.settings and autoLoot.settings.useDefaultActionKey == true\n" +
            "    if needed == __gcetAutoLootActionState then return end\n" +
            "    if pcall(handle.SetActive, needed) then __gcetAutoLootActionState = needed end\n" +
            "end\n";
        text = init.Replace(text, m => guard + "\n" + m.Value, 1);
        text = ReplaceOnce(text,
            "\tautoLoot.ui.onInit()\n",
            "\t__gcetAutoLootSyncDefaultAction()\n" +
            "\tautoLoot.ui.onInit()\n",
            "AutoLoot initial author default-action state sync");
        var update = new Regex(
            @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\)\s*)$",
            RegexOptions.CultureInvariant);
        if (update.Matches(text).Count != 1)
            throw new InvalidOperationException("AutoLoot onUpdate independent author setting wake changed.");
        text = update.Replace(text,
            m => m.Value + "\n\t__gcetAutoLootSyncDefaultAction()", 1);

        if (!text.Contains(stage, StringComparison.Ordinal) ||
            !text.Contains("pcall(handle.SetActive, needed)", StringComparison.Ordinal) ||
            Regex.Matches(text, @"__gcetAutoLootActionHandle\s*=\s*[^;\r\n]*SubscribeAction\(", RegexOptions.CultureInvariant).Count != 2)
            throw new InvalidOperationException("AutoLoot subscriber/source wake postconditions failed.");
        return text;
    }


}
