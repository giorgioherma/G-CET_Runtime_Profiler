using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
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

        // Existing or freshly composed Dedka callbacks must retain the
        // generic GetPlayer provider. Rewrite ONLY our two known fallback
        // callsites; never rewrite the original fallback inside the provider.
        if (PreserveGenericGetters(text,"Game.GetPlayer()")=="__gcetGetPlayer()")
        {
            const string oldFallback=
                "type(__gcetDedkaEngine.GetPlayer) == \"function\" and __gcetDedkaEngine.GetPlayer() or Game.GetPlayer()";
            const string newFallback=
                "type(__gcetDedkaEngine.GetPlayer) == \"function\" and __gcetDedkaEngine.GetPlayer() or __gcetGetPlayer()";
            var legacyCount=Regex.Matches(text,Regex.Escape(oldFallback),
                RegexOptions.CultureInvariant).Count;
            if (legacyCount!=0 && legacyCount!=2)
                throw new InvalidOperationException(
                    "Dedka player provider fallback drifted; refusing partial migration.");
            if (legacyCount==2)
                text=text.Replace(oldFallback,newFallback,StringComparison.Ordinal);
        }
        if (text==file.Text)
            return SemanticInjectionResult.Skip(
                "Dedka scheduler and generic player fallback already source-proven.");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved Dedka Auto Shop's proximity discovery to 0-Engine coarse zones plus a 5 Hz near-shop lane, replaced vehicle polling with mount lifecycle events, hard-slept the shop while far away, and kept only active toggle/spawn cleanup on the frame path.");
    }

}
