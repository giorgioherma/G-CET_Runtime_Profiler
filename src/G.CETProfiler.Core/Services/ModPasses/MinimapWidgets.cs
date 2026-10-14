using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyMinimapStealthWidgetLocality(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "MinimapStealthMappinController",
            "UpdateAboveBelowVerticalRelation",
            "MinimapWidgetsConfig.ShowElevationArrow",
            "this:GetVerticalRelationToPlayer()",
            "this.aboveWidget",
            "this.belowWidget");

        const string marker = "G-CET semantic:minimap-stealth-widget-locality";
        var pattern = new Regex(
            @"(?ms)(?<opening>^[ \t]*ObserveAfter\s*\(\s*['""]MinimapStealthMappinController['""]\s*,\s*['""]UpdateAboveBelowVerticalRelation['""]\s*,\s*function\s*\(\s*this\s*\)\s*\r?\n[ \t]*if\s+not\s+IsDefined\(this\)\s+then\s+return\s+end[ \t]*\r?\n)(?<body>.*?)(?<ending>^[ \t]*end\s*\)[ \t]*(?:--[^\r\n]*)?$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline |
            RegexOptions.Singleline);

        var matches = pattern.Matches(file.Text);
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "Minimap stealth-elevation callback is missing or ambiguous.");

        var match = matches[0];
        var opening = match.Groups["opening"].Value;
        var body = match.Groups["body"].Value;
        if (body.Contains(marker, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Minimap stealth widget pass already present.");

        // Fail closed for meaningful author changes. Whitespace and standalone
        // comments are intentionally flexible; callback control flow is not.
        // The proof is the exact PASS5 visibility logic in the live source,
        // not merely a set of words that could appear in unrelated functions.
        var compact = Regex.Replace(
            Regex.Replace(body, @"(?m)^[ \t]*--[^\r\n]*", ""),
            @"\s+", "");
        var proved = string.Concat(
            "ifMinimapWidgetsConfig.ShowElevationArrow==truethen",
            "localisClamped=this:IsClamped()",
            "ifisClampedthen",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(false)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(false)end",
            "else",
            "localvertRelation=this:GetVerticalRelationToPlayer()",
            "localisAbove=vertRelation==gamemappinsVerticalPositioning.Above",
            "localisBelow=vertRelation==gamemappinsVerticalPositioning.Below",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(isAbove)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(isBelow)end",
            "end",
            "else",
            "ifnotthis.aboveWidgetandnotthis.belowWidgetthenreturnend",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(false)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(false)end",
            "end");
        // The untouched author 3.1.7 source has a redundant shouldShow query
        // and calls IsClamped twice. The deployed PASS5 variant defers both.
        // Accept either complete control-flow family (not a hash or version),
        // tolerating whitespace/comment-only/source registrar changes.
        // Neither family changes the original SetVisible decision matrix.
        var originalAuthor = string.Concat(
            "ifMinimapWidgetsConfig.ShowElevationArrow==truethen",
            "localvertRelation=this:GetVerticalRelationToPlayer()",
            "localshouldShow=this:GetRootWidget():IsVisible()andnotthis:IsClamped()",
            "localisAbove=vertRelation==gamemappinsVerticalPositioning.Above",
            "localisBelow=vertRelation==gamemappinsVerticalPositioning.Below",
            "ifthis:IsClamped()then",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(false)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(false)end",
            "else",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(isAbove)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(isBelow)end",
            "end",
            "else",
            "ifnotthis.aboveWidgetandnotthis.belowWidgetthenreturnend",
            "ifthis.aboveWidgetthenthis.aboveWidget:SetVisible(false)end",
            "ifthis.belowWidgetthenthis.belowWidget:SetVisible(false)end",
            "end");
        if (!string.Equals(compact, proved, StringComparison.Ordinal) &&
            !string.Equals(compact, originalAuthor, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Current minimap callback's visibility semantics are not source-proven; refusing rewrite.");

        var guard = Regex.Match(
            opening,
            @"(?m)^(?<indent>[ \t]*)if\s+not\s+IsDefined\(this\)");
        if (!guard.Success)
            throw new InvalidOperationException(
                "Minimap IsDefined guard changed; refusing widget rewrite.");
        var indent = guard.Groups["indent"].Value;
        var newline = file.Text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        var introduction = string.Join(newline, new[]
        {
            indent + "-- " + marker,
            indent + "-- Invocation-local widget handles, never cached across frames.",
            indent + "local __gcetAboveWidget = this.aboveWidget",
            indent + "local __gcetBelowWidget = this.belowWidget",
            indent + "if not __gcetAboveWidget and not __gcetBelowWidget then return end",
            ""
        });
        var newBody = body.Replace("this.aboveWidget", "__gcetAboveWidget",
                                  StringComparison.Ordinal)
                          .Replace("this.belowWidget", "__gcetBelowWidget",
                                  StringComparison.Ordinal);
        var replacement = opening + introduction + newBody +
                          match.Groups["ending"].Value;
        var text = file.Text[..match.Index] + replacement +
                   file.Text[(match.Index + match.Length)..];

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Localized native widget references in the measured stealth-elevation callback, " +
            "skipped missing-widget queries, and preserved the original update cadence and " +
            "visibility branches without cross-frame caching.");
    }

    /// <summary>
    /// Yaiba Penthouse standalone author source: if neither the bathtub hub,
    /// proximity pin nor pending water-apply state exists and the player is well
    /// outside the 6m activation radius, the original update cannot mutate
    /// any bath/UI state. Avoid game blackboard widget polling in those frames.
    /// Entity attachment observers for lamps/candles continue independently.
    /// </summary>
    private static SemanticInjectionResult ApplyMinimapWidgets(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "MinimapWidgetsConfig.ShowElevationArrow",
            "UpdateAboveBelowVerticalRelation",
            "updateFastWidgets",
            "updateSlowWidgets",
            "timerCoord");

        _ = FindOnUpdateOpening(file.Text, "deltaTime");
        var text = RegexReplaceOnce(
            file.Text,
            @"(?m)^(?<opening>\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*deltaTime\s*\)\s*)\r?\n" +
            @"(?<comments>(?:[ \t]*--[^\r\n]*\r?\n)*)" +
            @"(?<game>[ \t]*if\s+isGameLoading\s+or\s+isPreGameState\s+or\s+not\s+isInitialized\s+then\s+return\s+end\s*\r?\n)" +
            @"(?<force>[ \t]*if\s+not\s+shouldForceUpdate\s+then\s+return\s+end\s*\r?\n)",
            "${opening}\n${comments}${game}${force}\n" +
            "    local __gcetMinimapDt = math.max(tonumber(deltaTime) or 0.0, 0.0)\n" +
            "    timerFast = timerFast + __gcetMinimapDt\n" +
            "    timerSlow = timerSlow + __gcetMinimapDt\n" +
            "    timerCoord = timerCoord + __gcetMinimapDt\n" +
            "    local __gcetMinimapFpsDue = MinimapWidgetsConfig.FPS == true and shouldCountFPS == true\n" +
            "    local __gcetMinimapFastDue = timerFast >= 0.2\n" +
            "    local __gcetMinimapSlowDue = timerSlow >= 1.0\n" +
            "    local __gcetMinimapCoordDue = timerCoord >= (tonumber(MinimapWidgetsConfig.CoordInterv) or 1.0)\n" +
            "    if not __gcetMinimapFpsDue and not __gcetMinimapFastDue and not __gcetMinimapSlowDue and not __gcetMinimapCoordDue then return end\n",
            "Minimap Widgets timer-first gate");

        text = RegexReplaceOnce(
            text,
            @"(?m)^(?<indent>[ \t]*)--\s*1\.\s*FPS[^\r\n]*\r?\n" +
            @"\k<indent>if\s+MinimapWidgetsConfig\.FPS\s*==\s*true\s+and\s+shouldCountFPS\s*==\s*true\s+then\s*\r?\n" +
            @"\k<indent>[ \t]+updateFpsCounter\(\)\s*\r?\n" +
            @"\k<indent>end\s*\r?\n",
            "${indent}-- 1. FPS\n${indent}if __gcetMinimapFpsDue then updateFpsCounter() end\n",
            "Minimap Widgets FPS due gate");

        text = ReplaceOnce(
            text,
            "    timerFast = timerFast + deltaTime\n" +
            "    if timerFast >= 0.2 then\n",
            "    if __gcetMinimapFastDue then\n",
            "Minimap Widgets fast timer");

        text = ReplaceOnce(
            text,
            "    timerSlow = timerSlow + deltaTime\n" +
            "    if timerSlow >= 1.0 then\n",
            "    if __gcetMinimapSlowDue then\n",
            "Minimap Widgets slow timer");

        text = ReplaceOnce(
            text,
            "    timerCoord = timerCoord + deltaTime\n" +
            "    if timerCoord >= MinimapWidgetsConfig.CoordInterv then\n",
            "    if __gcetMinimapCoordDue then\n",
            "Minimap Widgets coordinate timer");

        var elevationOld =
            "\t\t\tlocal vertRelation = this:GetVerticalRelationToPlayer()\n" +
            "\t\t\tlocal shouldShow = this:GetRootWidget():IsVisible() and not this:IsClamped()\n" +
            "\t\t\tlocal isAbove = vertRelation == gamemappinsVerticalPositioning.Above\n" +
            "\t\t\tlocal isBelow = vertRelation == gamemappinsVerticalPositioning.Below\n" +
            "\t\t\tif this:IsClamped() then \n";

        var elevationNew =
            "\t\t\tlocal isClamped = this:IsClamped()\n" +
            "\t\t\tif isClamped then\n";

        text = ReplaceOnce(
            text,
            elevationOld,
            elevationNew,
            "Minimap Widgets elevation prefilter");

        text = ReplaceOnce(
            text,
            "\t\t\telse \n" +
            "\t\t\t\tif this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end\n",
            "\t\t\telse\n" +
            "\t\t\t\tlocal vertRelation = this:GetVerticalRelationToPlayer()\n" +
            "\t\t\t\tlocal isAbove = vertRelation == gamemappinsVerticalPositioning.Above\n" +
            "\t\t\t\tlocal isBelow = vertRelation == gamemappinsVerticalPositioning.Below\n" +
            "\t\t\t\tif this.aboveWidget then this.aboveWidget:SetVisible(isAbove) end\n",
            "Minimap Widgets elevation deferred query");

        var visibilityGate =
            "\t\tlocal hideEnemies = not MinimapWidgetsConfig.ShowEnemies\n" +
            "\t\tlocal hideNCPD = not MinimapWidgetsConfig.ShowNCPD\n" +
            "\t\tlocal hideLoot = not MinimapWidgetsConfig.ShowLoot\n" +
            "\t\tlocal hideDevices = not MinimapWidgetsConfig.ShowDevices\n" +
            "\t\tif not hideEnemies and not hideNCPD and not hideLoot and not hideDevices then return end\n";

        text = RegexReplaceOnce(
            text,
            @"(?ms)(ObserveAfter\s*\(\s*['""]MinimapStealthMappinController['""]\s*,\s*['""]Intro['""]\s*,\s*function\s*\(\s*this\s*\)\s*\r?\n[ \t]*if not IsDefined\(this\) then return end\s*\r?\n)",
            "$1" + visibilityGate,
            "Minimap Widgets Intro visibility prefilter");

        text = RegexReplaceOnce(
            text,
            @"(?ms)(ObserveAfter\s*\(\s*['""]MinimapStealthMappinController['""]\s*,\s*['""]Update['""]\s*,\s*function\s*\(\s*this\s*\)\s*\r?\n[ \t]*if not IsDefined\(this\) then return end\s*\r?\n)",
            "$1" + visibilityGate,
            "Minimap Widgets Update visibility prefilter");

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved widget timers ahead of native-system acquisition so dormant frames exit before Game getters, deferred elevation queries until unclamped, and added a version-open visibility prefilter that skips stealth-mappin native work when no hide option is enabled.");
    }

}
