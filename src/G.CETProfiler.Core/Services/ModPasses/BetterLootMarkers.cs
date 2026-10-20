using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
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


    private static SemanticInjectionResult ApplyBetterLootMarkersCategoryAllocation(SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "function BetterLootMarkers.ResolveHighestQualityByCategory(itemList)",
            "BetterLootMarkers.GetItemScore",
            "RPGManager.IsItemIconic(item)");
        const string marker = "G-CET semantic:better-loot-markers-category-no-temporary-table";
        if (file.Text.Contains(marker, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("BetterLootMarkers category-allocation stage already present.");
        const string original =
            "        local newItem = {\n" +
            "            quality = quality,\n" +
            "            isIconic = RPGManager.IsItemIconic(item),\n" +
            "        }\n" +
            "        if categories[category] == nil then\n" +
            "            categories[category] = newItem\n" +
            "            categories[category].count = 1\n" +
            "        else\n" +
            "            categories[category].count = categories[category].count + 1\n" +
            "            local currentScore = BetterLootMarkers.GetItemScore(categories[category])\n" +
            "            local newScore = BetterLootMarkers.GetItemScore(newItem)\n" +
            "            if newScore > currentScore then\n" +
            "                local count = categories[category].count\n" +
            "                categories[category] = newItem\n" +
            "                categories[category].count = count\n" +
            "            end\n" +
            "        end";
        const string replacement =
            "        -- " + marker + "\n" +
            "        -- Preserve native item reads, categories, score ordering and counts;\n" +
            "        -- allocate a Lua item-summary only for a first or new best item.\n" +
            "        local iconic = RPGManager.IsItemIconic(item)\n" +
            "        local current = categories[category]\n" +
            "        if current == nil then\n" +
            "            categories[category] = { quality = quality, isIconic = iconic, count = 1 }\n" +
            "        else\n" +
            "            current.count = current.count + 1\n" +
            "            local currentScore = BetterLootMarkers.GetItemScore(current)\n" +
            "            local newScore\n" +
            "            if iconic then\n" +
            "                newScore = BetterLootMarkers.ItemTypes.Qualities.Iconic\n" +
            "            else\n" +
            "                local qualityValue = quality and quality.value\n" +
            "                newScore = BetterLootMarkers.ItemTypes.Qualities[qualityValue] or -1\n" +
            "            end\n" +
            "            if newScore > currentScore then\n" +
            "                categories[category] = { quality = quality, isIconic = iconic, count = current.count }\n" +
            "            end\n" +
            "        end";
        var normalized = file.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (normalized.Split(original, StringSplitOptions.None).Length != 2 ||
            normalized.Split("function BetterLootMarkers.GetItemScore(itemData)",
                StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException(
                "BetterLootMarkers category scoring/count behavior changed; refusing an allocation rewrite.");
        context.Write(file, normalized.Replace(original, replacement, StringComparison.Ordinal));
        return SemanticInjectionResult.Success(
            "Kept native quality/iconic reads and winner/count behavior; stopped creating a discardable Lua summary table for every nonwinning item in mappin category resolution.");
    }
}
