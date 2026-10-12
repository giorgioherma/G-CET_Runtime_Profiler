using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// One source-proven AutoLoot full-pass rule. The onUpdate capture is the
// admission signal for both its loot cycle and the already-validated input and
// frame routing. Never apply a second, overlapping OnAction semantic rule.
// Original 3.11.x: both Lua files. V1/V2: add only missing source transforms.
// Preserve existing behavior, the original 0.33s loot cadence and native
// fallback registration when 0-Engine isn't available.
internal static class AutoLootKeyCycleSource
{
    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        var logic = context.FindFile(
            "modules/logic.lua",
            "function logic.lootInRange(",
            "function isWorldMapPinOfTypeInRange(",
            "local function vectorDistanceSquared(src, target)",
            "mappinSystem:GetMappinEntries(gamemappinsMappinTargetTypeWorld)",
            "mappinSystem:GetMappins(gamemappinsMappinTargetTypeWorld)");
        var init = context.FindFile(
            "init.lua",
            "handleButtonPressed",
            "handleButtonReleased",
            "useDefaultActionKey",
            "action:GetType(action)");
        var journal = context.FindFile(
            "modules/jmels.lua",
            "getAllJournalQuestEntries", "getActiveQuestsMappins");

        // Temporary local research probes are not production optimizations.
        if (new[] { logic.Text, init.Text, journal.Text }.Any(source =>
                source.Contains("__gcetProbe", StringComparison.Ordinal) ||
                source.Contains("modules/gcet_probe", StringComparison.Ordinal) ||
                source.Contains("GCET_AutoLoot_ProfileDump", StringComparison.Ordinal)))
        {
            return SemanticInjectionResult.Skip(
                "AutoLoot still contains temporary action/quest diagnostics or " +
                "their journal observer. Restore clean author/optimized files first.");
        }

        var initialLogic = logic.Text;
        var worldOptimized = HasOptimizedWorldPin(initialLogic);
        var worldOriginal = initialLogic.Contains("local closeMappins = {}", StringComparison.Ordinal) &&
            initialLogic.Contains("if takeIt then tableInsert(closeMappins, mappin) end", StringComparison.Ordinal) &&
            initialLogic.Contains("return closeMappins[lowestDistanceIndex]", StringComparison.Ordinal);

        if (worldOriginal == worldOptimized)
            throw new InvalidOperationException(
                "AutoLoot world-pin implementation is unknown or partial; no source-safe pass.");

        // Reuse the original source-proven allocation optimization, not a
        // hard-coded replacement file. It stages the same live logic.lua.
        if (worldOriginal)
        {
            SemanticInjectors.ApplyAutoLootWorldPinAllocationScan(context);
            logic = context.FindFile("modules/logic.lua",
                "function logic.lootInRange(",
                "if matchingMappins == 1 then return firstMappin end");
        }

        const string authorGuard = "if isNpcSafeToLoot(gameObj) then";
        const string radiusGuard =
            "if isNpcSafeToLoot(gameObj) and (not lootInRange or vectorDistanceSquared(playerPos, gameObj:GetWorldPosition()) <= rangeSquared) then";

        var logicText = logic.Text;
        var cycleStart = logicText.IndexOf("function logic.lootInRange(", StringComparison.Ordinal);
        var cycleEnd = cycleStart < 0 ? -1 :
            logicText.IndexOf("function lootNpc(", cycleStart, StringComparison.Ordinal);
        if (cycleStart < 0 || cycleEnd < 0 ||
            logicText.IndexOf("function logic.lootInRange(",
                cycleStart + 1, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("AutoLoot loot-cycle boundaries have changed.");

        var cycle = logicText[cycleStart..cycleEnd];
        var hasGuard = cycle.Contains(radiusGuard, StringComparison.Ordinal);
        if (!hasGuard)
        {
            var firstInventory = cycle.IndexOf(
                "transactionSystem:GetItemList(gameObj)", StringComparison.Ordinal);
            var ownerInventory = cycle.IndexOf(
                "transactionSystem:GetItemList(gameObjOwner)", StringComparison.Ordinal);
            var originalRange = cycle.IndexOf("if lootInRange then", StringComparison.Ordinal);
            var laterDistance = cycle.IndexOf(
                "takeIt = vectorDistanceSquared(playerPos, gameObjPos) <= rangeSquared",
                StringComparison.Ordinal);
            var laterFrustum = cycle.IndexOf(
                "cameraSystem:IsInCameraFrustum(gameObj, 0.5, 0.2)",
                StringComparison.Ordinal);
            var laterVisibility = cycle.IndexOf(
                "isObjectVisibleToPlayer(gameObj, gameObjPos, player, playerPos, true)",
                StringComparison.Ordinal);
            var transfer = cycle.IndexOf(
                "lootObjectItems(gameObj, forceShard", StringComparison.Ordinal);

            if (Regex.Matches(cycle, Regex.Escape(authorGuard)).Count != 1 ||
                firstInventory <= 0 || ownerInventory <= firstInventory ||
                originalRange <= ownerInventory || laterDistance <= originalRange ||
                laterFrustum <= laterDistance || laterVisibility <= laterFrustum ||
                transfer <= laterVisibility)
            {
                throw new InvalidOperationException(
                    "AutoLoot inventory-to-radius-to-visibility sequence is no longer source-proven.");
            }

            // This exact cheap prefilter has no effect on out-of-range=false.
            // Preserve the original later range, frustum, quest and LoS checks.
            cycle = cycle.Replace(authorGuard, radiusGuard, StringComparison.Ordinal);
            logicText = logicText[..cycleStart] + cycle + logicText[cycleEnd..];
        }

        if (!HasOptimizedWorldPin(logicText) ||
            !logicText.Contains(radiusGuard, StringComparison.Ordinal))
            throw new InvalidOperationException("AutoLoot partial logic state after transformation.");

        var (routedInit, actionsWereOriginal, framesWereOriginal) =
            UpgradeInit(init.Text);

        // Atomic two-file version marker: already-optimized input and logic
        // still receive only a harmless v3 marker line. This allows a partial
        // v2 install to be upgraded without the marker admitting incomplete
        // two-file states as fully optimized.
        context.Write(logic, ForceMarker(logicText));
        context.Write(init, ForceMarker(routedInit));

        return SemanticInjectionResult.Success(
            "Applied/retained AutoLoot world-pin scalar selection, pre-inventory " +
            "radius filtering, exact 0-Engine action routing and frame registration. " +
            (actionsWereOriginal ? "OnAction observer routed with native fallback. " :
                "Previously routed OnAction retained. ") +
            (framesWereOriginal ? "Frame registrar added. " :
                "Existing frame registrar retained. ") +
            "Kept original quest protection, markers, visibility, loot transfers " +
            "and keypress/hold timing.");
    }

    private static bool HasOptimizedWorldPin(string source) =>
        source.Contains("local firstMappin, nearestMappin", StringComparison.Ordinal) &&
        source.Contains("if matchingMappins == 1 then return firstMappin end", StringComparison.Ordinal) &&
        source.Contains("lowestDistanceIndex = nearestIndex", StringComparison.Ordinal) &&
        source.Contains("return nearestMappin", StringComparison.Ordinal);

    private static string ForceMarker(string text)
    {
        const string marker = "-- G-CET semantic:autoloot-input-frame-v3";
        return text.Contains(marker, StringComparison.OrdinalIgnoreCase)
            ? text
            : marker + "\n" + text;
    }

    private static (string Text, bool Routed, bool Framed) UpgradeInit(string text)
    {
        var wasOriginalAction = false;
        var wasOriginalFrame = false;

        const string originalObserve =
            "Observe('PlayerPuppet', 'OnAction', function(this, action)";
        var existingRoute =
            text.Contains("local function zeroEngineOnAction(this, action)", StringComparison.Ordinal) &&
            text.Contains("SubscribeAction(zeroEngineConfig, zeroEngineOnAction)", StringComparison.Ordinal) &&
            text.Contains("Observe('PlayerPuppet', 'OnAction', zeroEngineOnAction)", StringComparison.Ordinal);
        var genericPreviousRoute =
            text.Contains("local function gcetAutoLootOnAction(", StringComparison.Ordinal) &&
            text.Contains("__gcetRouted_gcetAutoLootOnAction", StringComparison.Ordinal) &&
            text.Contains("SubscribeAction(", StringComparison.Ordinal);

        if (!existingRoute && !genericPreviousRoute)
        {
            if (Regex.Matches(text, Regex.Escape(originalObserve)).Count != 1)
                throw new InvalidOperationException(
                    "AutoLoot direct PlayerPuppet::OnAction observer no longer uniquely matches.");

            const string callback =
                "Observe('PlayerPuppet', 'OnAction', function(this, action)\r\n" +
                "\t\tif not autoLoot.settings.useDefaultActionKey then return end\r\n" +
                "\t\tactionType = action:GetType(action)\r\n" +
                "\t\tif actionType == gameinputActionTypeBUTTON_PRESSED then handleButtonPressed(this, action) return end\r\n" +
                "\t\tif actionType == gameinputActionTypeBUTTON_RELEASED then handleButtonReleased(this, action) return end\r\n" +
                "\tend)";
            // Match LF/CRLF without widening to unrelated OnAction observers.
            var regex = new Regex(
                @"(?m)^[ \t]*Observe\('PlayerPuppet', 'OnAction', function\(this, action\)\r?\n" +
                @"[ \t]*if not autoLoot\.settings\.useDefaultActionKey then return end\r?\n" +
                @"[ \t]*actionType = action:GetType\(action\)\r?\n" +
                @"[ \t]*if actionType == gameinputActionTypeBUTTON_PRESSED then handleButtonPressed\(this, action\) return end\r?\n" +
                @"[ \t]*if actionType == gameinputActionTypeBUTTON_RELEASED then handleButtonReleased\(this, action\) return end\r?\n" +
                @"[ \t]*end\)",
                RegexOptions.CultureInvariant);
            var match = regex.Match(text);
            if (!match.Success || match.NextMatch().Success)
                throw new InvalidOperationException(
                    "AutoLoot original OnAction press/release semantics are not proven.");

            var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var indent = Regex.Match(match.Value, @"^[ \t]*").Value;
            string WithIndent(string line) => indent + line;
            var replacement = new[]
            {
                "local function zeroEngineOnAction(this, action)",
                "\tif not autoLoot.settings.useDefaultActionKey then return end",
                "\tactionType = action:GetType(action)",
                "\tif actionType == gameinputActionTypeBUTTON_PRESSED then handleButtonPressed(this, action) return end",
                "\tif actionType == gameinputActionTypeBUTTON_RELEASED then handleButtonReleased(this, action) return end",
                "end",
                "local zeroEngineActions = {",
                "\t\"Pause\", \"OpenPauseMenu\", \"OpenMapMenu\", \"OpenCraftingMenu\", \"OpenJournalMenu\", \"OpenPerksMenu\",",
                "\t\"OpenInventoryMenu\", \"OpenHubMenu\", \"TogglePhotoMode\", \"UI_Apply\", \"Choice1\", \"click\", \"Ping\",",
                "\t\"Forward\", \"Back\", \"Left\", \"Right\", \"Jump\", \"ToggleCrouch\", \"MeleeAttack\", \"UI_DPadWeapons\"",
                "}",
                "local zeroEngineOk, zeroEngine = pcall(GetMod, \"0-Engine\")",
                "local zeroEngineMod = zeroEngineOk and type(zeroEngine) == \"table\" and type(zeroEngine.Register) == \"function\" and zeroEngine.Register(\"AutoLoot\") or nil",
                "local zeroEngineConfig = { actions = zeroEngineActions, types = { \"BUTTON_PRESSED\", \"BUTTON_RELEASED\" } }",
                "if type(zeroEngineMod) == \"table\" and type(zeroEngineMod.SubscribeAction) == \"function\" then",
                "\tzeroEngineMod.SubscribeAction(zeroEngineConfig, zeroEngineOnAction)",
                "elseif zeroEngineOk and type(zeroEngine) == \"table\" and type(zeroEngine.SubscribeAction) == \"function\" then",
                "\tzeroEngine.SubscribeAction(zeroEngineConfig, zeroEngineOnAction, \"AutoLoot\")",
                "else",
                "\tObserve('PlayerPuppet', 'OnAction', zeroEngineOnAction)",
                "end"
            };
            text = text[..match.Index] +
                string.Join(nl, replacement.Select(WithIndent)) +
                text[(match.Index + match.Length)..];
            wasOriginalAction = true;
        }

        // Mirror the source-validated LIVE frame registrar. On-update may
        // already have a generated generic registrar, in which case retain it.
        var update = new Regex(
            @"(?m)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\(""onUpdate"", function\(delta\)",
            RegexOptions.CultureInvariant);
        var draws = new Regex(
            @"(?m)^(?<indent>[ \t]*)(?<registrar>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\(""onDraw"", function\(\)",
            RegexOptions.CultureInvariant);
        var u = update.Match(text);
        var d = draws.Match(text);
        if (!u.Success || u.NextMatch().Success || !d.Success || d.NextMatch().Success)
            throw new InvalidOperationException(
                "AutoLoot onUpdate/onDraw registration no longer uniquely source-proven.");

        var hasRuntimeRegistrar =
            text.Contains("local registerRuntimeEvent = registerForEvent", StringComparison.Ordinal) &&
            text.Contains("MakeEventRegistrar(\"AutoLoot\", registerForEvent)", StringComparison.Ordinal);
        if (!hasRuntimeRegistrar)
        {
            if (text.Contains("registerRuntimeEvent", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "AutoLoot partial/unknown frame registrar state.");
            var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var bootstrap = string.Join(nl, new[]
            {
                "local registerRuntimeEvent = registerForEvent",
                "do",
                "    local ok, engine = pcall(GetMod, \"0-Engine\")",
                "    if ok and type(engine) == \"table\" and type(engine.MakeEventRegistrar) == \"function\" then",
                "        registerRuntimeEvent = engine.MakeEventRegistrar(\"AutoLoot\", registerForEvent)",
                "    end",
                "end",
                ""
            }) + nl;
            text = text[..u.Index] + bootstrap + text[u.Index..];
            wasOriginalFrame = true;
            u = update.Match(text);
            d = draws.Match(text);
        }

        // Leave existing generic frame wrapper untouched; don't double-route.
        if (u.Groups["registrar"].Value == "registerForEvent")
        {
            text = text[..u.Groups["registrar"].Index] +
                "registerRuntimeEvent" +
                text[(u.Groups["registrar"].Index + u.Groups["registrar"].Length)..];
            d = draws.Match(text);
        }
        if (d.Groups["registrar"].Value == "registerForEvent")
        {
            text = text[..d.Groups["registrar"].Index] +
                "registerRuntimeEvent" +
                text[(d.Groups["registrar"].Index + d.Groups["registrar"].Length)..];
        }

        if (!text.Contains("MakeEventRegistrar(\"AutoLoot\", registerForEvent)", StringComparison.Ordinal) ||
            !text.Contains("SubscribeAction(", StringComparison.Ordinal))
            throw new InvalidOperationException("AutoLoot routed source postconditions failed.");

        return (text, wasOriginalAction, wasOriginalFrame);
    }
}
