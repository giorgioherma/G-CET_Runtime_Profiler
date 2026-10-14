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

}
