using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    internal static SemanticInjectionResult Apply(
        SemanticCandidate candidate,
        SemanticPatchContext context)
    {
        // The catalog verifies independently completed source stages before
        // admitting a composed rule. An existing marker on only one stage is
        // not a reason to suppress the other stage's safe incremental pass.
        // Legacy rule markers establish earlier passes, not newer *source*
        // stages. Only reviewed upgradeable rules bypass the blanket marker
        // veto; each must independently prove all missing source stages and
        // refuse unknown drift (including the v1->v2 third-person subscriber
        // lifecycle). All other single-stage rules honor their legacy marker.
        var versionFluidUpgrade = candidate.RuleId is
            "driveaerialvehicle" or
            "tunnel-rescue-v3" or
            "ziggy-last-play-navigation-sentinel-v3" or
            "immersivefirstperson" or
            "calllist-action-lazy-display-query" or
            "immersive-third-person-action-routing" or
            "backstep-duo" or
            "specter-recovery-prerequisite-reads" or
            "nativeinteractions" or
            "gigcore-blackboard-once" or
            "air-backflip" or
            "native-location-poker" or "native-location-tunnel" or
            "native-location-illegal" or "native-location-dedka" or
            "native-location-card-collection" or "native-location-marmurbank" or
            "native-location-yaiba-bathtub" or "native-location-ziggy-offered";
        if (context.OwnerAlreadyMarked() &&
            !candidate.StageComposed && !versionFluidUpgrade)
            return SemanticInjectionResult.Skip(
                "A G-CET semantic marker for this rule already exists in the live/staged mod.");

        return candidate.RuleId switch
        {
            "combat" => ApplyCombat(context),
            "gta-joyride" => ApplyJoyRide(context),
            "repeatable-cyberpsychos" => ApplyRepeatableCyberpsychos(context),
            "cybertrials" => ApplyCyberTrials(context),
            "cybertrials-closed-overlay-provider" => ApplyCyberTrialsClosedOverlayProvider(context),
            "streetgamespoker" => PokerPass4Source.Apply(context),
            "streetgamespoker-hud-invisible-native-idle" => PokerPass4Source.ApplyHudInvisibleIdle(context),
            "native-location-poker" => NativeLocationFourOwnersSource.ApplyPoker(context),
            "native-location-tunnel" => NativeLocationFourOwnersSource.ApplyTunnel(context),
            "native-location-illegal" => NativeLocationFourOwnersSource.ApplyIllegalV3(context),
            "native-location-dedka" => NativeLocationFourOwnersSource.ApplyDedka(context),
            "native-location-card-collection" => NativeLocationAdditionalOwnersSource.ApplyCards(context),
            "native-location-marmurbank" => NativeLocationAdditionalOwnersSource.ApplyMarmur(context),
            "native-location-yaiba-bathtub" => NativeLocationAdditionalOwnersSource.ApplyBathtub(context),
            "native-location-ziggy-offered" => NativeLocationAdditionalOwnersSource.ApplyZiggy(context),
            "nightcitybilliards" => ApplyNightCityBilliards(context),
            "npcd-hotline" => ApplyNpcdHotline(context),
            "gameentityexaminertool" => ApplyGameEntityExaminer(context),
            "shift" => ApplyShift(context),
            "givemeeverything" => ApplyGiveMeEverything(context),
            "questrunner" => ApplyQuestRunner(context),
            "driveaerialvehicle" => ApplyDriveAerialVehicle(context),
            "hotscenes-ui-action-dormancy" => ApplyHotscenesAfterActionUi(context),
            "roulette" => ApplyRoulette(context),
            "blackjack" => ApplyBlackjack(context),
            "illegal-mechanic" => ApplyIllegalMechanic(context),
            "illegal-mechanic-npc-position-reuse" => ApplyIllegalMechanicNpcPositionReuse(context),
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
            "good-feelings-hard-draw" => ApplyGoodFeelingsHardDraw(context),
            "air-backflip" => ApplyAirBackFlip(context),
            "auto-drop-weapon-dynamic-routing" => ApplyAutoDropWeaponOnPickupEquip(context),
            "joytoys-of-night-city-bridge-active-split-idle-facts-v2" => ApplyJoytoysOfNightCity(context),
            "tunnel-rescue-v3" => ApplyTunnelRescue(context),
            "ziggy-last-play-navigation-sentinel-v3" => ZiggyProximitySource.Apply(context),
            "aerial-race-active-idle-split" => ApplyAerialRaceActiveIdleSplit(context),
            "appearance-menu-mod-squeeze" => ApplyAppearanceMenuModSqueeze(context),
            "drone-companions-revamp" => ApplyDroneCompanionsRevamp(context),
            "ghost-void-system" => ApplyGhostVoidSystem(context),
            "straight-edged-controls-input-dormancy" => ApplyStraightEdgedControls(context),
            "immersive-head-inertia" => ApplyImmersiveHeadInertia(context),
            "advanced-settings" => ApplyAdvancedSettings(context),
            "auto-ammo-crafting" => ApplyAutoAmmoCrafting(context),
            "autoloot" => ApplyAutoLoot(context),
            "autoloot-world-pin-and-early-distance" => ApplyAutoLootWorldPinAndEarlyDistance(context),
            "better-loot-markers" => ApplyBetterLootMarkers(context),
            "npc-outfit-manager-idle-state" => NpcOutfitManagerSource.Apply(context),
            "npc-outfit-manager-id-normalization" => NpcOutfitManagerSource.ApplyRecordIdNormalization(context),
            "gigcore-blackboard-once" => ApplyGigCoreBlackboardReuse(context),
            "specter-recovery-prerequisite-reads" => ApplySpecterRecoveryPrerequisites(context),
            "drivebus" => ApplyDriveBus(context),
            "quest-tracking-toggle" => ApplyQuestTrackingToggle(context),
            "calllist-action-lazy-display-query" => ApplyCallListActionLazyDisplayQuery(context),
            "sitanywhere" => ApplySitAnywhere(context),
            "repeatable-increased-criminal-activity" => ApplyRepeatableIncreasedCriminalActivity(context),
            "dedka-auto-shop" => ApplyDedkaAutoShop(context),
            "marmurbank" => ApplyMarmurBank(context),
            "immersive-third-person" => ApplyImmersiveThirdPerson(context),
            "immersive-third-person-action-routing" => ApplyImmersiveThirdPersonActionRouting(context),
            "immersivefirstperson" => ApplyImmersiveFirstPerson(context),
            "nativeinteractions" => ApplyNativeInteractions(context),
            "minimap-widgets" => ApplyMinimapWidgets(context),
            "minimap-widgets-time-weather-snapshot" => ApplyMinimapTimeWeatherSnapshot(context),
            "minimap-stealth-widget-locality" => ApplyMinimapStealthWidgetLocality(context),
            "yaiba-penthouse-distant-bathtub" => ApplyYaibaDistantBathtub(context),
            _ => SemanticInjectionResult.Skip(
                $"No semantic source injector is implemented for rule '{candidate.RuleId}'.")
        };
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



    internal static SemanticInjectionResult ApplyZiggyLastPlayNavigation(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "navigation.lua",
            "function N.update(dt,path,phase,playerKey,generation,hooks,pinId,runtime,gameplay)",
            "N.age=N.age+dt;if N.age<2 then return end",
            "GetQuestMappinPosition(hash)",
            "system:GetAllMappins()",
            "hooks:SyncJournalPins(path",
            "navigation-status.json");

        var text = file.Text;

        // An installed v2 navigation sentinel already contains every bounded
        // mappin optimization. v3 retains it byte-for-byte and adds only the
        // new quest-proximity lane in init.lua (same semantic, no competing rule).
        if (Regex.IsMatch(text,
            @"(?m)^\s*local\s+N\s*=\s*\{\s*age\s*=\s*0\s*,\s*retries\s*=\s*0\s*,\s*scanKey\s*=\s*nil\s*,\s*lastMappins\s*=\s*\{\s*\}\s*,\s*missingScanAge\s*=\s*0\s*\}",
            RegexOptions.CultureInvariant))
        {
            context.Write(file, "-- G-CET Ziggy v2 navigation behavior preserved\n" + text);
            return SemanticInjectionResult.Success(
                "Retained the existing v2 two-second targeted navigation and bounded 12-second missing-pin scan.");
        }

        var originalState = "local N={age=0,retries=0}\n";
        var v1State = "local N={age=0,retries=0,scanKey=nil,lastMappins={}}\n";
        // Original and first-generation sentinel initializers are recognized
        // by their *semantic field list*, not indentation or literal spaces.
        // Replace only one complete initializer and retain the rest of file.
        var originalShape = @"(?m)^\s*local\s+N\s*=\s*\{\s*age\s*=\s*0\s*,\s*retries\s*=\s*0\s*\}\s*$";
        var v1Shape = @"(?m)^\s*local\s+N\s*=\s*\{\s*age\s*=\s*0\s*,\s*retries\s*=\s*0\s*,\s*scanKey\s*=\s*nil\s*,\s*lastMappins\s*=\s*\{\s*\}\s*\}\s*$";
        var isOriginal = Regex.IsMatch(text, originalShape,
                                     RegexOptions.CultureInvariant);
        if (isOriginal)
        {
            text = RegexReplaceOnce(text, originalShape, v1State,
                                    "Ziggy original diagnostic cache state");
        }
        else if (!Regex.IsMatch(text, v1Shape,
                               RegexOptions.CultureInvariant))
        {
            throw new InvalidOperationException(
                "Ziggy navigation state is not a proven original/v1/v2 field list.");
        }
        else
        {
            text = RegexReplaceOnce(text, v1Shape, v1State,
                                    "Ziggy v1 formatted initializer normalization");
        }

        var fullScan =
            "  report.mappins={}\n" +
            "  for _,m in ipairs(system:GetAllMappins())do\n" +
            "   if m:IsQuestMappin()and unsigned(m:GetJournalPathHash())==hash then\n" +
            "    report.mappins[#report.mappins+1]={active=m:IsActive(),visible=m:IsVisible(),tracked=m:IsPlayerTracked(),position=point(m:GetWorldPosition())}\n" +
            "   end\n" +
            "  end\n";

        var gatedScan =
            "  local scanKey=table.concat({tostring(path or ''),tostring(phase or ''),tostring(generation or ''),tostring(found==true),tostring(report.tracked==true),tostring(N.retries)},'|')\n" +
            "  if not found or N.scanKey ~= scanKey then\n" +
            "   local mappins={}\n" +
            "   for _,m in ipairs(system:GetAllMappins())do\n" +
            "    if m:IsQuestMappin()and unsigned(m:GetJournalPathHash())==hash then\n" +
            "     mappins[#mappins+1]={active=m:IsActive(),visible=m:IsVisible(),tracked=m:IsPlayerTracked(),position=point(m:GetWorldPosition())}\n" +
            "    end\n" +
            "   end\n" +
            "   N.scanKey=scanKey\n" +
            "   N.lastMappins=mappins\n" +
            "  end\n" +
            "  report.mappins=N.lastMappins or {}\n";

        // Original source first receives the v1 sentinel shape. Existing v1
        // installations are upgraded directly, without double-injecting the scan.
        if (isOriginal)
        {
            text = ReplaceOnce(
                text, fullScan, gatedScan,
                "Ziggy original global diagnostic mappin scan");
        }

        text = ReplaceOnce(
            text, v1State,
            "local N={age=0,retries=0,scanKey=nil,lastMappins={},missingScanAge=0}\n",
            "Ziggy v2 missing-pin diagnostic clock");

        text = ReplaceOnce(
            text,
            "N.age=N.age+dt;if N.age<2 then return end;N.age=0",
            "N.age=N.age+dt;if N.age<2 then return end;N.missingScanAge=N.missingScanAge+N.age;N.age=0",
            "Ziggy v2 preserve 2-second probe and accumulate diagnostic age");

        if (text.Contains("function N.reset()N.age=0;N.path=nil;N.retries=0 end", StringComparison.Ordinal))
            text = ReplaceOnce(
                text,
                "function N.reset()N.age=0;N.path=nil;N.retries=0 end",
                "function N.reset()N.age=0;N.path=nil;N.retries=0;N.scanKey=nil;N.lastMappins={};N.missingScanAge=0 end",
                "Ziggy v2 reset diagnostic cache");

        text = ReplaceOnce(
            text,
            "  local scanKey=table.concat({tostring(path or ''),tostring(phase or ''),tostring(generation or ''),tostring(found==true),tostring(report.tracked==true),tostring(N.retries)},'|')\n",
            "  local scanKey=table.concat({tostring(path or ''),tostring(phase or ''),tostring(generation or ''),tostring(report.pinId),tostring(hash),tostring(report.objectiveState),tostring(found==true),tostring(report.tracked==true),tostring(N.retries)},'|')\n",
            "Ziggy v2 pin identity/objective signature");

        text = ReplaceOnce(
            text,
            "  if not found or N.scanKey ~= scanKey then\n",
            "  if N.scanKey ~= scanKey or (not found and N.missingScanAge >= 12) then\n",
            "Ziggy v2 bound repeated missing-pin global scans");

        text = ReplaceOnce(
            text,
            "   N.lastMappins=mappins\n",
            "   N.lastMappins=mappins\n   N.missingScanAge=0\n",
            "Ziggy v2 reset diagnostic clock after scan");

        // Early exits cannot affect the gameplay navigation path. Invalidate
        // cached diagnostic rows so restoring the same pin forces fresh status.
        if (text.Contains("  if not path then return end", StringComparison.Ordinal))
            text = ReplaceOnce(
                text,
                "  if not path then return end",
                "  if not path then N.scanKey=nil;N.lastMappins={};N.missingScanAge=0;return end",
                "Ziggy v2 absent objective cache invalidation");

        if (text.Contains("  if not valid(objective)or not valid(pin)then return end", StringComparison.Ordinal))
            text = ReplaceOnce(
                text,
                "  if not valid(objective)or not valid(pin)then return end",
                "  if not valid(objective)or not valid(pin)then N.scanKey=nil;N.lastMappins={};N.missingScanAge=0;return end",
                "Ziggy v2 missing journal pin cache invalidation");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved 2-second targeted lookup, journal repair and bounded retries. Global diagnostic scans now occur on pin/objective state change or once per 12 seconds while the same missing-pin state persists; only plain Lua values are cached. Supports original and G-CET v1 source.");
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
            @"(?ms)^(?<indent>[ \t]*)(?<observer>(?:Event\.)?Observe)\s*\(\s*['""]PlayerPuppet['""]\s*,\s*['""]OnAction['""]\s*,\s*function\s*\((?<args>[^)]*)\)\s*\r?\n(?<body>.*?)(?<close>^\k<indent>end\s*\)\s*;?\s*(?:--[^\r\n]*)?$)",
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
            indent + "    " + routedName + " = pcall(function() " + clientName + ".SubscribeAction({ actions = " + tableName + ", decodeType = false }, " + functionName + ") end)\n" +
            indent + "elseif " + okName + " and type(" + engineName + ") == \"table\" and type(" + engineName + ".SubscribeAction) == \"function\" then\n" +
            indent + "    " + routedName + " = pcall(function() " + engineName + ".SubscribeAction({ actions = " + tableName + ", decodeType = false }, " + functionName + ", \"" + owner + "\") end)\n" +
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

    // Source-stage upgrade support: a pristine Nexus input receives every
    // replacement in one generation. Previously optimized or partially
    // optimized sources satisfy only stages whose resulting source is present.
    // Never require an earlier pass or trust its marker as proof.
    private static string ReplaceOrSkipCompletedStage(
        string text, string oldValue, string newValue, string label)
    {
        if (text.Contains(newValue, StringComparison.Ordinal) ||
            Regex.IsMatch(text, BuildFlexibleLiteralPattern(newValue),
                RegexOptions.CultureInvariant | RegexOptions.Singleline))
            return text;
        return ReplaceOnce(text, oldValue, newValue, label);
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
