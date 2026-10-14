using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCallListActionLazyDisplayQuery(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "VehiclesManagerListItemController",
            "OnSelected", "GetDisplayResolution", "animOpen", "popupOpen", "moveup");
        var text = file.Text;
        const string guard =
            @"if\s+not\s+animOpen\s+or\s+not\s+popupOpen\s+or\s+not\s+playerPosX\s+or\s+not\s+settings\.minigame\s+then\s+return\s+end";
        const string press =
            @"if\s+action:IsButtonJustPressed\s*\(\s*action\s*\)\s+then";
        const string name =
            @"local\s+name\s*=\s*Game\.GetLocalizedText\s*\(\s*action:GetName\s*\(\s*action\s*\)\.value\s*\)";
        const string filter =
            @"if\s+string\.find\s*\(\s*name:lower\s*\(\s*\)\s*,\s*['""]moveup['""]\s*\)\s+then";
        const string display =
            @"local\s+sizeX\s*,\s*sizeY\s*=\s*GetDisplayResolution\s*\(\s*\)";
        const RegexOptions options = RegexOptions.Multiline |
            RegexOptions.Singleline | RegexOptions.CultureInvariant;

        // Verify both the new stage token and source ordering. A stray marker
        // cannot suppress work after an author edits the handler.
        var finalShape = new Regex(
            @"(?m)^(?<indent>[ \t]*)" + guard + @"\s+" + press +
            @"\s+--\s*G-CET:CallListActionLazyInputV2\s+" + name +
            @"\s+" + filter + @"\s+" + display, options);
        var complete = finalShape.Matches(text);
        if (complete.Count == 1)
            return SemanticInjectionResult.Skip(
                "CallList V2 action-localized and display queries are already lazy.");
        if (complete.Count > 1 || text.Contains("G-CET:CallListActionLazyInputV2",
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "CallList V2 stage marker is duplicated or source ordering changed.");

        var initial = new Regex(
            @"(?m)^(?<indent>[ \t]*)(?<guard>" + guard + @")(?<gap>\s+)" +
            @"(?<display>" + display + @")\s+" +
            @"(?<name>" + name + @")\s+" +
            @"(?<press>" + press + @")\s+" +
            @"(?<filter>" + filter + @")", options);

        var older = new Regex(
            @"(?m)^(?<indent>[ \t]*)(?<guard>" + guard + @")(?<gap>\s+)" +
            @"(?<name>" + name + @")\s+" +
            @"(?<press>" + press + @")\s+" +
            @"(?<filter>" + filter + @")\s+" +
            @"(?:--\s*G-CET:CallListActionLazyDisplayV1\s+)?" +
            @"(?<display>" + display + @")", options);

        var pristineMatches = initial.Matches(text);
        var olderMatches = older.Matches(text);
        if (pristineMatches.Count + olderMatches.Count != 1)
            throw new InvalidOperationException(
                "CallList source does not uniquely prove the original or display-only OnAction branch.");
        var selected = pristineMatches.Count == 1
            ? pristineMatches[0] : olderMatches[0];
        var indent = selected.Groups["indent"].Value;
        var unit = indent.Contains('\t') ? "\t" : "    ";
        var child = indent + unit;
        var nested = child + unit;
        var replacement =
            indent + selected.Groups["guard"].Value.Trim() + "\n" +
            indent + selected.Groups["press"].Value.Trim() + "\n" +
            child + "-- G-CET:CallListActionLazyInputV2\n" +
            child + selected.Groups["name"].Value.Trim() + "\n" +
            child + selected.Groups["filter"].Value.Trim() + "\n" +
            nested + selected.Groups["display"].Value.Trim();

        text = text[..selected.Index] + replacement +
            text[(selected.Index + selected.Length)..];
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "CallList defers localized action-name conversion until JustPressed, " +
            "then queries display resolution only for moveup; existing localized " +
            "matching, position math, observer registration and UI state remain intact.");
    }

}
