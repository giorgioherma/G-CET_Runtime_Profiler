using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyGigCoreBlackboardReuse(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local function _rawMenuOrPhotoPaused()",
            "Game.GetAllBlackboardDefs()",
            "local function computePauseState()",
            "computePauseState");
        var text = file.Text;
        var original = text;

        // Transform within this function only. Generic AUTO may already have
        // rewritten either getter into the verified shared-provider helper.
        var pauseScope = SemanticScopedStages.RequireFunction(
            text, "_rawMenuOrPhotoPaused");
        var body = pauseScope.Text;
        bool uiDone = Regex.IsMatch(body,
            @"local\s+ui\s*=\s*__gcetGigBoards\s*:\s*Get\s*\(\s*defs\.UI_System\s*\)",
            RegexOptions.CultureInvariant);
        bool pmDone = Regex.IsMatch(body,
            @"local\s+pm\s*=\s*__gcetGigBoards\s*:\s*Get\s*\(\s*defs\.PhotoMode\s*\)",
            RegexOptions.CultureInvariant);
        if (uiDone || pmDone)
        {
            if (!uiDone || !pmDone ||
                !Regex.IsMatch(body,
                    @"local\s+__gcetGigBoards\s*=\s*(?:Game\.GetBlackboardSystem|__gcetGetBlackboardSystem)\s*\(\s*\)",
                    RegexOptions.CultureInvariant))
                throw new InvalidOperationException(
                    "GigCore existing shared board source stage is incomplete.");
        }
        else
        {
            var ui = new Regex(
                @"(?m)^(?<indent>[ \t]*)local\s+ui\s*=\s*" +
                @"(?<getter>(?:Game\.GetBlackboardSystem|__gcetGetBlackboardSystem)\s*\(\s*\))" +
                @"\s*:\s*Get\s*\(\s*defs\.UI_System\s*\)[ \t]*$",
                RegexOptions.CultureInvariant);
            var pm = new Regex(
                @"(?m)^(?<indent>[ \t]*)local\s+pm\s*=\s*" +
                @"(?<getter>(?:Game\.GetBlackboardSystem|__gcetGetBlackboardSystem)\s*\(\s*\))" +
                @"\s*:\s*Get\s*\(\s*defs\.PhotoMode\s*\)[ \t]*$",
                RegexOptions.CultureInvariant);
            var uiMatches = ui.Matches(body);
            var pmMatches = pm.Matches(body);
            if (uiMatches.Count != 1 || pmMatches.Count != 1 ||
                uiMatches[0].Index >= pmMatches[0].Index ||
                !body.Contains("Game.GetAllBlackboardDefs()", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "GigCore original/shared-provider UI and PhotoMode getters are not uniquely proven.");
            var u = uiMatches[0];
            var p = pmMatches[0];
            var helper = u.Groups["getter"].Value.Contains("__gcetGetBlackboardSystem",
                             StringComparison.Ordinal) ||
                         p.Groups["getter"].Value.Contains("__gcetGetBlackboardSystem",
                             StringComparison.Ordinal)
                ? "__gcetGetBlackboardSystem()" : "Game.GetBlackboardSystem()";
            var uiIndent = u.Groups["indent"].Value;
            var pmIndent = p.Groups["indent"].Value;
            var replacementPm = pmIndent +
                "local pm = __gcetGigBoards:Get(defs.PhotoMode)";
            body = body[..p.Index] + replacementPm + body[(p.Index + p.Length)..];
            var replacementUi =
                uiIndent + "local __gcetGigBoards = " + helper + "\n" +
                uiIndent + "local ui = __gcetGigBoards:Get(defs.UI_System)";
            body = body[..u.Index] + replacementUi + body[(u.Index + u.Length)..];
            text = text[..pauseScope.Start] + body + text[pauseScope.End..];
        }

        var stableFunction = new Regex(
            @"(?m)^\s*local\s+function\s+__gcetGigCoreRefreshPausedFrame\s*\(",
            RegexOptions.CultureInvariant);
        var closure = new Regex(
            @"pcall\s*\(\s*function\s*\(\s*\)\s*" +
            @"pausedThisFrame\s*=\s*computePauseState\s*\(\s*\)\s*end\s*\)",
            RegexOptions.CultureInvariant);
        if (stableFunction.IsMatch(text))
        {
            if (stableFunction.Matches(text).Count != 1 ||
                !Regex.IsMatch(text,
                    @"pcall\s*\(\s*__gcetGigCoreRefreshPausedFrame\s*\)",
                    RegexOptions.CultureInvariant) ||
                closure.IsMatch(text))
                throw new InvalidOperationException(
                    "GigCore pause guard is partially transformed or ambiguous.");
        }
        else
        {
            var matches = closure.Matches(text);
            if (matches.Count == 0 &&
                Regex.IsMatch(text,
                    @"local\s+pausedThisFrame\s*=\s*computePauseState\s*\(\s*\)",
                    RegexOptions.CultureInvariant))
            {
                // Earlier GigCore releases compute pause state directly and
                // never create a protected per-frame closure. Nothing to reuse.
            }
            else if (matches.Count != 1)
                throw new InvalidOperationException(
                    "GigCore pause-state guard is not a uniquely proven closure.");
            if (matches.Count == 1)
            {
            var update = new Regex(
                @"(?m)^(?<indent>[ \t]*)(?<call>(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*delta\s*\))",
                RegexOptions.CultureInvariant);
            var updates = update.Matches(text);
            if (updates.Count != 1 || updates[0].Index >= matches[0].Index)
                throw new InvalidOperationException(
                    "GigCore onUpdate entrypoint is not uniquely proven.");
            var m = matches[0];
            text = text[..m.Index] + "pcall(__gcetGigCoreRefreshPausedFrame)" +
                text[(m.Index + m.Length)..];
            var frame = update.Match(text);
            var definition =
                "local function __gcetGigCoreRefreshPausedFrame()\n" +
                "  pausedThisFrame = computePauseState()\n" +
                "end\n\n";
            text = text[..frame.Index] + definition + text[frame.Index..];
            }
        }

        if (text == original)
            return SemanticInjectionResult.Skip(
                "GigCore blackboard and pause-guard source stages are already complete.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Resolved one blackboard system for UI/photo probes per invocation " +
            "and reused one protected pause-update function per frame; existing " +
            "gig timers, session/reconcile logic, callback order and exception " +
            "handling are preserved.");
    }

    // SPECTER 1.4.1 and 1.5 share their M9->M10 handoff.
    // 1.5 optionally adds a read-only music contract constructed every frame.
}
