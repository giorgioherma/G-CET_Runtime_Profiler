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

        // The independently applied stealth-widget locality semantic
        // can already have localized the same callback's handles. Preserve
        // those invocation-local handles instead of demanding old author
        // source or restoring the original this.aboveWidget access.
        var localizedWidgets = text.Contains(
            "-- G-CET semantic:minimap-stealth-widget-locality",
            StringComparison.Ordinal);
        var aboveWidget = localizedWidgets ? "__gcetAboveWidget" : "this.aboveWidget";
        text = ReplaceOrSkipCompletedStage(
            text,
            "\t\t\telse \n" +
            "\t\t\t\tif " + aboveWidget + " then " + aboveWidget + ":SetVisible(isAbove) end\n",
            "\t\t\telse\n" +
            "\t\t\t\tlocal vertRelation = this:GetVerticalRelationToPlayer()\n" +
            "\t\t\t\tlocal isAbove = vertRelation == gamemappinsVerticalPositioning.Above\n" +
            "\t\t\t\tlocal isBelow = vertRelation == gamemappinsVerticalPositioning.Below\n" +
            "\t\t\t\tif " + aboveWidget + " then " + aboveWidget + ":SetVisible(isAbove) end\n",
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

        // This is the SAME authoritative Minimap Widgets semantic, not a
        // corrective second pass. Stable TweakDBID literals are lazily reused
        // per CET onInit lifecycle; live mappin controller events, variants,
        // visibility, spawn profiles and distance decisions are unchanged.
        text = ReuseMinimapMappinProfileIds(text);

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Moved widget timers ahead of native-system acquisition so dormant frames exit before Game getters, deferred elevation queries until unclamped, and added a version-open visibility prefilter that skips stealth-mappin native work when no hide option is enabled.");
    }


    /// <summary>
    /// Cache only immutable, literal TweakDBIDs used to instantiate actual
    /// mappin UI profiles. Do not cache controllers/widget handles or use
    /// player-distance gating: minimap icons may be tracked/clamped when the
    /// underlying world position is FAR, and the native location table does
    /// not own these dynamic pin controllers. This stage is inside the
    /// existing Minimap Widgets semantic generator.
    /// </summary>
    private static string ReuseMinimapMappinProfileIds(string text)
    {
        const string marker = "__gcetMinimapProfileIdsV1";
        const string getter = "__gcetMinimapProfileIdV1";
        // Older/minimal supported layouts may have no profile override.
        // Its absence is not grounds to break an otherwise valid timer and
        // elevation optimization. A PRESENT but drifted override fails closed.
        if (!text.Contains("CreateMappinUIProfile", StringComparison.Ordinal))
            return text;
        var callback = new Regex(
            @"(?ms)^(?<indent>[ \t]*)ObserveAfter\s*\(\s*['""]MinimapContainerController['""]" +
            @"\s*,\s*['""]CreateMappinUIProfile['""]\s*,\s*function\s*\(" +
            @"\s*this\s*,\s*mappin\s*,\s*mappinVariant\s*,\s*customData\s*\)" +
            @"(?<body>.*?)^(?<close>[ \t]*end\s*\)[ \t]*(?:--[^\r\n]*)?$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline);
        var matches = callback.Matches(text);
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "Minimap profile constructor is missing or ambiguous; refusing cache rewrite.");
        var m = matches[0];
        var body = m.Groups["body"].Value;
        var ids = new Regex(
            @"TweakDBID\.new\s*\(\s*(?<quote>['""])" +
            @"(?<id>(?:MinimapMappinUIProfile|MappinUISpawnProfile)\.[A-Za-z0-9_]+)" +
            @"\k<quote>\s*\)",
            RegexOptions.CultureInvariant);
        var raw = ids.Matches(body);
        var hasMarker = text.Contains(marker, StringComparison.Ordinal);
        if (hasMarker)
        {
            // Strong structural postcondition: marker AND the actual producer
            // sites replaced. A stale/partial stage must never count as done.
            if (Regex.Matches(text, @"\blocal\s+" + marker + @"\s*=\s*\{\s*\}").Count != 1 ||
                Regex.Matches(text, @"\blocal\s+function\s+" + getter + @"\s*\(").Count != 1 ||
                raw.Count != 0 ||
                Regex.Matches(body, @"\b" + getter + @"\s*\(").Count < 2 ||
                !text.Contains(marker + "[key] = value", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Minimap profile-ID cache is incomplete or drifted; refusing duplicated execution.");
            return text;
        }
        // The original callback has a default profile plus many branch-local
        // constant IDs; never cache dynamic strings or unrelated constructors.
        if (raw.Count < 2 ||
            !body.Contains("MappinUIProfile.Create", StringComparison.Ordinal) ||
            !body.Contains("TweakDBID.new('MinimapMappinUIProfile.Default')",
                           StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Minimap immutable profile-ID sources have drifted; refusing cache rewrite.");
        var allIds = Regex.Matches(body, @"TweakDBID\.new\s*\(").Count;
        if (allIds != raw.Count)
            throw new InvalidOperationException(
                "Minimap profile constructor contains non-literal TweakDBIDs; refusing partial cache.");
        var fixedBody = ids.Replace(body,
            x => getter + "(" + x.Groups["quote"].Value +
                 x.Groups["id"].Value + x.Groups["quote"].Value + ")");
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indent = m.Groups["indent"].Value;
        var declaration = string.Join(newline, new[]
        {
            indent + "-- G-CET source-owned lazy profile IDs: immutable TweakDBID values only.",
            indent + "local " + marker + " = {}",
            indent + "local function " + getter + "(key)",
            indent + "    local value = " + marker + "[key]",
            indent + "    if value == nil then",
            indent + "        value = TweakDBID.new(key)",
            indent + "        " + marker + "[key] = value",
            indent + "    end",
            indent + "    return value",
            indent + "end",
            ""
        });
        return text[..m.Index] + declaration + newline +
               m.Value.Replace(body, fixedBody, StringComparison.Ordinal) +
               text[(m.Index + m.Length)..];
    }

    private static SemanticInjectionResult ApplyMinimapTimeWeatherSnapshot(SemanticPatchContext context)
    {
        // Generic shared-getter reuse may have replaced the Game facade.
        // Source proof is the pair of author functions plus matching live
        // time-getter structure, never a pre-generic exact spelling.
        var file = context.FindFile("init.lua", "function updateTimeWidget()",
            "function updateWeatherWidget()", "TimeIsRunningOut");
        var text = file.Text;
        const string stage = "__gcetMinimapGameTimePerInvocation";
        if (text.Contains(stage, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Time/weather synchronous native time snapshot already installed.");
        // Both author functions independently read the same game-time object
        // twice in one invocation. No value is retained across frames.
        var original = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+hours\s*=\s*(?<getter>Game\.GetTimeSystem\(\)|__gcetGetTimeSystem\(\)):GetGameTime\(\):Hours\(\)\s*\r?\n" +
            @"\k<indent>local\s+minutes\s*=\s*\(?[ \t]*\k<getter>:GetGameTime\(\):Minutes\(\)\)?[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var matches = original.Matches(text);
        if (matches.Count != 2 ||
            text.IndexOf("function updateTimeWidget()", StringComparison.Ordinal) >= matches[0].Index ||
            text.IndexOf("function updateWeatherWidget()", StringComparison.Ordinal) >= matches[1].Index ||
            text.IndexOf("function updateWeatherWidget()", StringComparison.Ordinal) <= matches[0].Index)
            throw new InvalidOperationException("Time/weather source flow or two-read structure changed.");
        text = original.Replace(text, m =>
            m.Groups["indent"].Value + "local " + stage + " = " + m.Groups["getter"].Value + ":GetGameTime()\n" +
            m.Groups["indent"].Value + "local hours = " + stage + ":Hours()\n" +
            m.Groups["indent"].Value + "local minutes = " + stage + ":Minutes()");
        var provider = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+mod\s*=\s*GetMod\(\s*['""]TimeIsRunningOut['""]\s*\)[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        if (provider.Matches(text).Count != 1)
            throw new InvalidOperationException("TimeIsRunningOut provider is no longer one unique optional time-only lookup.");
        text = provider.Replace(text, m => m.Groups["indent"].Value +
            "local mod = nil\n" + m.Groups["indent"].Value +
            "if MinimapWidgetsConfig.TimeSwitch == 5 then mod = GetMod(\"TimeIsRunningOut\") end");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Read one fresh GameTime per Time/Weather callback and resolve optional TimeIsRunningOut only in author mode 5; original formatting and widget decisions retained.");
    }
}
