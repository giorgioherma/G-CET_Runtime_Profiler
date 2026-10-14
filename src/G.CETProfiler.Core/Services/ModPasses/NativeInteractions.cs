using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
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


    /// <summary>
    /// Observed 73,989 MinimapStealthMappinController elevation callbacks in
    /// CET-20261014-170941_FOURTH_G-GC_CAPTURE (5.679764 exclusive ms/s).
    /// Native widget properties are references marshalled into Lua; rereading
    /// the same property inside one callback recreates wrapper traffic.
    /// This pass never retains widgets across callbacks or suppresses updates.
    /// </summary>
}
