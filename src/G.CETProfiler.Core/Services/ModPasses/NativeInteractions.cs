using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNativeInteractions(
        SemanticPatchContext context)
    {
        // Upgrade the user's genuinely installed PASS9/PASS10 source without
        // replaying its original three-file injector or undoing the prior
        // manager, world and pin-index patches. Pristine input still takes the
        // original one-pass path below.
        var currentInit = context.FindFile("init.lua",
            "resourceHelper.onUpdate()", "self.runtimeData.inGame");
        // A complete externally owned pass is idempotent. A bridge without its
        // source-owned worker is a broken partial deployment, never satisfied.
        if (currentInit.Text.Contains("GCET_Semantic/nativeinteractions_engine_worker", StringComparison.Ordinal))
        {
            var worker = context.TryReadOwnedCoordinator("nativeinteractions_engine_worker.lua");
            if (worker is null ||
                !worker.StartsWith("-- G-CET semantic coordinator:nativeinteractions\n", StringComparison.Ordinal) ||
                !worker.Contains("function M.reset()", StringComparison.Ordinal) ||
                !worker.Contains("manager.updateDormant(position)", StringComparison.Ordinal) ||
                !currentInit.Text.Contains("__gcetNifCadence.update(dt, self, manager, world, sceneLaunchPending,", StringComparison.Ordinal))
                throw new InvalidOperationException("Native Interactions external cadence worker missing or incomplete.");
            context.FindFile("modules/projectsManager.lua",
                "function manager.updateDormant(position)",
                "manager.dormantPhase = (manager.dormantPhase % 4) + 1");
            context.FindFile("modules/utils/worldInteraction.lua",
                "function world.update(player, posPlayer, playerForward, quests)",
                "pinInteractions");
            return SemanticInjectionResult.Skip(
                "Native Interactions externally owned cadence and four-phase dependencies already installed.");
        }
        if (currentInit.Text.Contains("local managerRealtime = manager.hasRealtimeWork()",
                                      StringComparison.Ordinal))
        {
            context.FindFile("modules/projectsManager.lua",
                "function manager.hasRealtimeWork()",
                "function manager.update(position)",
                "manager.updateList");
            context.FindFile("modules/utils/worldInteraction.lua",
                "pinInteractions", "findPinInteraction", "world.getGridInteractions");
            var upgraded = UpgradeNativeInteractionsRealtimeProbe(currentInit.Text);
            context.Write(currentInit, upgraded);
            return SemanticInjectionResult.Success(
                "Upgraded installed nativeInteractions partial A/B source: " +
                "skip the author realtime-status scan only when an existing scene, " +
                "pending signal, interaction or queued launch already requires full rate. " +
                "Retained Cron, every scene/recovery event, native observers, " +
                "original world and manager cadences, and pin registry.");
        }

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

        // One mod-owned cadence coordinator; keep the author callback, Cron,
        // cooperative scene processing and all original lifecycle hooks inline.
        // Lua local scope begins at its declaration. The OnSessionStart/End
        // closures are authored within the onInit callback, before onUpdate.
        // Hoist the external coordinator ahead of mod:new(), otherwise those
        // earlier closures resolve __gcetNifCadence as an unset GLOBAL.
        var initText = ReplaceOnce(init.Text,
            "function mod:new()\n",
            "local __gcetNifCadence = require(\"GCET_Semantic/nativeinteractions_engine_worker\")\n\n" +
            "function mod:new()\n",
            "nativeInteractions lexical-scope-safe worker import");

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
            "            resourceHelper.onUpdate()\n" +
            "            __gcetNifCadence.update(dt, self, manager, world, sceneLaunchPending, " +
            (init.Text.Contains("local function __gcetGetQuestsSystem()", StringComparison.Ordinal)
                ? "__gcetGetQuestsSystem" : "Game.GetQuestsSystem") + ")\n" +
            "        end\n";
        initText = ReplaceOnce(initText, originalBody, optimizedBody,
            "nativeInteractions G-CET coordinator bridge");

        var questGetter = init.Text.Contains("local function __gcetGetQuestsSystem()", StringComparison.Ordinal)
            ? "__gcetGetQuestsSystem()" : "Game.GetQuestsSystem()";
        initText = RegexReplaceOnce(initText,
            @"(?m)^([ \t]*)CName\.add\(""nif""\)\r?\n[ \t]*(?:Game\.GetQuestsSystem\(\)|__gcetGetQuestsSystem\(\)):SetFactStr\(""nif_iguana_idle"", 0\)",
            "$1CName.add(\"nif\")\n$1self.quests = " + questGetter +
            "\n$1if self.quests then self.quests:SetFactStr(\"nif_iguana_idle\", 0) end",
            "nativeInteractions initial session quest cache");
        initText = RegexReplaceOnce(initText,
            @"(?m)^([ \t]*)self\.runtimeData\.inGame = true\r?\n[ \t]*(?:Game\.GetQuestsSystem\(\)|__gcetGetQuestsSystem\(\)):SetFactStr\(""nif_iguana_idle"", 0\)\r?\n[ \t]*(?:Game\.GetQuestsSystem\(\)|__gcetGetQuestsSystem\(\)):SetFactStr\(""nif_scene_active"", 0\)",
            "$1self.runtimeData.inGame = true\n$1self.quests = " + questGetter +
            "\n$1if self.quests then\n$1    self.quests:SetFactStr(\"nif_iguana_idle\", 0)\n$1    self.quests:SetFactStr(\"nif_scene_active\", 0)\n$1end",
            "nativeInteractions save-load quest handle refresh");
        initText = ReplaceOnce(initText, "    player = nil,\n",
            "    player = nil,\n    quests = nil,\n",
            "nativeInteractions session-owned QuestsSystem field");
        initText = ReplaceOnce(initText,
            "            manager.sessionStart()\n            world.onSessionStart()\n",
            "            __gcetNifCadence.reset()\n            manager.sessionStart()\n            world.onSessionStart()\n",
            "nativeInteractions session cadence reset");
        initText = ReplaceOnce(initText,
            "            self.runtimeData.inGame = false\n            Cron.HaltAll()\n",
            "            self.runtimeData.inGame = false\n            self.quests = nil\n            __gcetNifCadence.reset()\n            Cron.HaltAll()\n",
            "nativeInteractions session exit owner invalidation");
        initText = ReplaceOnce(initText,
            "    registerForEvent(\"onShutdown\", function ()\n        world.shutdown()\n",
            "    registerForEvent(\"onShutdown\", function ()\n        __gcetNifCadence.reset()\n        self.quests = nil\n        world.shutdown()\n",
            "nativeInteractions worker shutdown lifecycle");

        // Retain the author-created player position table but accept a shared
        // sample; dormant groups each visit just one stable phase per tick.
        managerText = ReplaceOnce(managerText,
            "    mod = nil\n}",
            "    mod = nil,\n    dormantPhase = 0\n}",
            "nativeInteractions four-phase manager state");
        managerText = ReplaceOnce(managerText,
            "    manager.updateList = list\n",
            "    manager.updateList = list\n    manager.dormantPhase = 0\n",
            "nativeInteractions phased list rebuild reset");
        managerText = ReplaceOnce(managerText,
            "function manager.update()\n    local list = manager.updateList\n    local nUpdates = #list\n    if nUpdates == 0 then return end\n\n    local position = GetPlayer():GetWorldPosition()\n    playerPosition.x = position.x\n    playerPosition.y = position.y\n    playerPosition.z = position.z\n\n    for i = 1, nUpdates do\n        list[i]:onUpdate(playerPosition)\n    end\nend\n",
            "local function sharedPosition(position)\n" +
            "    if not position then\n" +
            "        local player = GetPlayer()\n" +
            "        if not player then return nil end\n" +
            "        position = player:GetWorldPosition()\n" +
            "    end\n" +
            "    if not position then return nil end\n" +
            "    playerPosition.x, playerPosition.y, playerPosition.z = position.x, position.y, position.z\n" +
            "    return playerPosition\n" +
            "end\n\n" +
            "function manager.update(position)\n" +
            "    local list = manager.updateList\n" +
            "    if #list == 0 then return end\n" +
            "    local pos = sharedPosition(position)\n" +
            "    if not pos then return end\n" +
            "    for i = 1, #list do list[i]:onUpdate(pos) end\n" +
            "end\n\n" +
            "function manager.updateDormant(position)\n" +
            "    local list = manager.updateList\n" +
            "    if #list == 0 then return end\n" +
            "    local pos = sharedPosition(position)\n" +
            "    if not pos then return end\n" +
            "    manager.dormantPhase = (manager.dormantPhase % 4) + 1\n" +
            "    for i = manager.dormantPhase, #list, 4 do list[i]:onUpdate(pos) end\n" +
            "end\n",
            "nativeInteractions shared position and four-phase dormant selection");
        managerText = ReplaceOnce(managerText,
            "function manager.sessionStart()\n",
            "function manager.sessionStart()\n    manager.dormantPhase = 0\n",
            "nativeInteractions dormant phase session reset");

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

        // The same native player position and QuestsSystem reference must be
        // shared by manager and world on each due tick, with original getters
        // available when author code invokes world.update() independently.
        worldText = RegexReplaceOnce(worldText,
            @"(?m)^function world\.update\(\)\r?\n[ \t]*if (?:Game\.GetQuestsSystem\(\)|__gcetGetQuestsSystem\(\)):GetFactStr\(""nif_scene_active""\) == 1 then return end[^\r\n]*\r?\n\s*local posPlayer = GetPlayer\(\):GetWorldPosition\(\)\r?\n[ \t]*local playerForward = GetPlayer\(\):GetWorldForward\(\)",
            "function world.update(player, posPlayer, playerForward, quests)\n" +
            "    quests = quests or " + (world.Text.Contains("local function __gcetGetQuestsSystem()", StringComparison.Ordinal)
                ? "__gcetGetQuestsSystem()" : "Game.GetQuestsSystem()") + "\n" +
            "    if quests and quests:GetFactStr(\"nif_scene_active\") == 1 then return end\n" +
            "    player = player or GetPlayer()\n" +
            "    if not player then return end\n" +
            "    posPlayer = posPlayer or player:GetWorldPosition()\n" +
            "    if not posPlayer then return end\n" +
            "    playerForward = playerForward or player:GetWorldForward()\n" +
            "    if not playerForward then return end",
            "nativeInteractions shared world position/forward/quest handle");

        context.WriteCoordinator("nativeinteractions_engine_worker.lua", """
-- G-CET semantic coordinator:nativeinteractions
-- Single per-mod active/dormant state owner; no additional CET registration.
local M = {}
local worldElapsed, managerElapsed = 0.0, 0.0
local wasFullRate = true
local WORLD_INTERVAL, MANAGER_INTERVAL = 1 / 30, 1 / 12

function M.reset()
    worldElapsed, managerElapsed = 0.0, 0.0
    wasFullRate = true
end

function M.update(dt, mod, manager, world, sceneLaunchPending, getQuests)
    local quests = mod.quests
    if not quests then
        quests = getQuests()
        mod.quests = quests
    end
    if not quests then return end

    local sceneActive = quests:GetFactStr("nif_scene_active") == 1
    local sceneStartPending = not sceneActive and quests:GetFactStr("nif_start_signal") ~= 0
    local interactionActive = next(world.activeInteractions) ~= nil
    local fullRate = sceneActive or sceneStartPending or interactionActive or sceneLaunchPending
    if not fullRate then fullRate = manager.hasRealtimeWork() end

    local runManager = fullRate
    local runWorld = fullRate and not sceneActive
    local reconcileAll = false
    if fullRate then
        worldElapsed, managerElapsed = 0.0, 0.0
        wasFullRate = true
    elseif wasFullRate then
        wasFullRate = false
        worldElapsed, managerElapsed = 0.0, 0.0
        runWorld, runManager = true, true
        reconcileAll = true
    else
        local step = math.max(tonumber(dt) or 0, 0)
        worldElapsed = worldElapsed + step
        managerElapsed = managerElapsed + step
        if worldElapsed >= WORLD_INTERVAL then
            worldElapsed = worldElapsed % WORLD_INTERVAL
            runWorld = true
        end
        if managerElapsed >= MANAGER_INTERVAL then
            managerElapsed = managerElapsed % MANAGER_INTERVAL
            runManager = true
        end
    end
    if not runManager and not runWorld then return end

    local player = GetPlayer()
    if not player then return end
    local position = player:GetWorldPosition()
    if not position then return end
    if runManager then
        if fullRate or reconcileAll then
            manager.update(position)
        else
            manager.updateDormant(position)
        end
    end
    if runWorld then
        local forward = player:GetWorldForward()
        if forward then world.update(player, position, forward, quests) end
    end
end
return M
""");
        context.Write(init, initText);
        context.Write(manager, managerText);
        context.Write(world, worldText);
        return SemanticInjectionResult.Success(
            "Source-owned NIF coordinator restores 30 Hz idle world, 12 Hz four-phase idle manager (~3 Hz per interaction), cached session quests, one player sample per due tick, immediate transitions and native pin index.");
    }


    // The author has independent full-rate reasons. Checking the director
    // status for a reason that cannot affect that already-true decision is
    // redundant. This is the current implementation's existing pristine
    // short-circuit behavior, applied to the older patched live variant.
    private static string UpgradeNativeInteractionsRealtimeProbe(string text)
    {
        const string oldSource =
            "            local managerRealtime = manager.hasRealtimeWork()\n" +
            "            local fullRate = sceneActive or sceneStartPending or interactionActive or sceneLaunchPending or managerRealtime";
        const string newSource =
            "            local fullRate = sceneActive or sceneStartPending or interactionActive or sceneLaunchPending\n" +
            "            if not fullRate then fullRate = manager.hasRealtimeWork() end";
        const string oldFact = "local sceneStartPending = quests:GetFactStr(\"nif_start_signal\") ~= 0";
        const string newFact = "local sceneStartPending = not sceneActive and quests:GetFactStr(\"nif_start_signal\") ~= 0";
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var oldProbe = normalized.Contains(oldSource, StringComparison.Ordinal);
        var newProbe = normalized.Contains(newSource, StringComparison.Ordinal);
        var oldFactPresent = normalized.Contains(oldFact, StringComparison.Ordinal);
        var newFactPresent = normalized.Contains(newFact, StringComparison.Ordinal);
        if (oldProbe == newProbe || oldFactPresent == newFactPresent ||
            !normalized.Contains("local sceneActive = quests:GetFactStr(\"nif_scene_active\") == 1",
                                 StringComparison.Ordinal) ||
            !normalized.Contains("local interactionActive = next(world.activeInteractions) ~= nil",
                                 StringComparison.Ordinal) ||
            !normalized.Contains("local sceneLaunchPending = #resourceHelper.sceneQueue > 0",
                                 StringComparison.Ordinal) ||
            !normalized.Contains("if self.idleWorldElapsed >= IDLE_WORLD_INTERVAL then",
                                 StringComparison.Ordinal))
            throw new InvalidOperationException(
                "nativeInteractions source-defined full-rate/fact wake stages changed.");
        if (oldProbe)
            normalized = normalized.Replace(oldSource, newSource, StringComparison.Ordinal);
        // This fact is read only to select full-rate processing. While the
        // real nif_scene_active fact is true, full-rate work already runs;
        // do not issue the second Lua-to-native GetFactStr call.
        if (oldFactPresent)
            normalized = normalized.Replace(oldFact, newFact, StringComparison.Ordinal);
        return text.Contains("\r\n", StringComparison.Ordinal)
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;
    }

    /// <summary>
    /// Observed 73,989 MinimapStealthMappinController elevation callbacks in
    /// CET-20261014-170941_FOURTH_G-GC_CAPTURE (5.679764 exclusive ms/s).
    /// Native widget properties are references marshalled into Lua; rereading
    /// the same property inside one callback recreates wrapper traffic.
    /// This pass never retains widgets across callbacks or suppresses updates.
    /// </summary>
}
