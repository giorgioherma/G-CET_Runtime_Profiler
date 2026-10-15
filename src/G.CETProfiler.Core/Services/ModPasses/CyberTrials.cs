using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCyberTrials(
        SemanticPatchContext context)
    {
        var init = context.FindFile(
            "init.lua",
            "hubs.setupMappins",
            "world.update()",
            "raceLogic.raceInProgress");

        var initText = init.Text;
        // Retain existing frame/scheduler owners when upgrading a live stack.
        var existingInitLane =
            initText.Contains("__gcetCyberTrialsWorldElapsed", StringComparison.Ordinal) ||
            initText.Contains("cybertrials.world_far", StringComparison.Ordinal);
        if (!existingInitLane)
        {
        initText = ReplaceSemanticObserverBlockOnce(
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
        }
        else if (!initText.Contains("raceLogic.raceInProgress", StringComparison.Ordinal) ||
                 !initText.Contains("world.update()", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CyberTrials existing frame/scheduler lane is incomplete.");
        }

        var hubsFile = context.FindFile(
            "modules/utils/interactionHubs.lua",
            "interactionHubs.setupMappins",
            "gamedataMappinVariant.Zzz18_RacingVariant",
            "mappinIDs",
            "raceLogic.raceInProgress",
            "RegisterMappin",
            "UnregisterMappin");

        var hubsText = hubsFile.Text;
        var existingHubs = Regex.IsMatch(
            hubsText,
            @"if\s+#mappinIDs\s*==\s*#(?<tracks>[A-Za-z_]\w*)\s+and\s+#\k<tracks>\s*>\s*0\s+then\s+return\s+end",
            RegexOptions.CultureInvariant);
        if (!existingHubs)
        {
        hubsText = RegexReplaceOnce(
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
        }
        else if (!hubsText.Contains("UnregisterMappin", StringComparison.Ordinal) ||
                 !hubsText.Contains("raceLogic.raceInProgress", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CyberTrials hub-mappin lifecycle is incomplete.");
        }

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

        var worldText = world.Text;
        if (!worldText.Contains(
                "mappin:GetVariant() ~= gamedataMappinVariant.Zzz18_RacingVariant",
                StringComparison.Ordinal))
        {
        worldText = ReplaceSemanticObserverBlockOnce(
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
        }
        else if (!worldText.Contains(
                     "TweakDBInterface.GetUIIconRecord(\"ChoiceIcon.VehicleIcon\")",
                     StringComparison.Ordinal) ||
                 worldText.Contains("Vector4.Distance(pos, interaction.pos)",
                     StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "CyberTrials existing owned-mappin observer has drifted.");
        }

        worldText = CacheCyberTrialsRaceIcon(worldText);
        if (!string.Equals(worldText, world.Text, StringComparison.Ordinal))
            context.Write(world, worldText);

        return context.Changes.Count == 0
            ? SemanticInjectionResult.Skip("CyberTrials semantic lanes and race icon cache are complete.")
            : SemanticInjectionResult.Success(
                "Preserved current race/editor/near/far scheduler lanes and hub activation, " +
                "retained the owned-mappin prefilter and cached immutable race icon metadata.");
    }

    private static string CacheCyberTrialsRaceIcon(string source)
    {
        // Cache immutable atlas scalars, never a live mappin handle or hub list.
        const string declaration =
            "function world.init()\n" +
            "    local __gcetRaceIconAtlasPath = nil\n" +
            "    local __gcetRaceIconAtlasPart = nil\n";
        if (source.Contains("__gcetRaceIconAtlasPath", StringComparison.Ordinal))
        {
            if (!source.Contains(declaration, StringComparison.Ordinal) ||
                !source.Contains("this.iconWidget:SetAtlasResource(__gcetRaceIconAtlasPath)",
                    StringComparison.Ordinal) ||
                !source.Contains("this.iconWidget:SetTexturePart(__gcetRaceIconAtlasPart)",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "CyberTrials icon metadata cache is incomplete or changed.");
            return source;
        }

        source = ReplaceOnce(
            source,
            "function world.init()\n",
            declaration,
            "CyberTrials owned-mappin icon cache lifetime");

        const string original =
            "        local record = TweakDBInterface.GetUIIconRecord(\"ChoiceIcon.VehicleIcon\")\n" +
            "        this.iconWidget:SetAtlasResource(record:AtlasResourcePath())\n" +
            "        this.iconWidget:SetTexturePart(record:AtlasPartName())\n";
        const string cached =
            "        if __gcetRaceIconAtlasPath == nil then\n" +
            "            local record = TweakDBInterface.GetUIIconRecord(\"ChoiceIcon.VehicleIcon\")\n" +
            "            __gcetRaceIconAtlasPath = record:AtlasResourcePath()\n" +
            "            __gcetRaceIconAtlasPart = record:AtlasPartName()\n" +
            "        end\n" +
            "        this.iconWidget:SetAtlasResource(__gcetRaceIconAtlasPath)\n" +
            "        this.iconWidget:SetTexturePart(__gcetRaceIconAtlasPart)\n";
        return ReplaceOnce(
            source, original, cached,
            "CyberTrials static icon atlas/part cache");
    }

}
