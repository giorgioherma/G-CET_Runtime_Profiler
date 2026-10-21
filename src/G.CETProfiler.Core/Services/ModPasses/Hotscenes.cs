using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Source-authored hotscenes navigation. Never move menu control into 0-Engine.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyHotscenesAfterActionUi(
        SemanticPatchContext context)
    {
        var ui = context.FindFile(
            "hotscenes_native_ui.lua",
            "nativeUI.isShowtime",
            "nativeUI.isMainMenu",
            "nativeUI.isSelectionList",
            "nativeUI.activeInstances",
            "OnAction",
            "handleShardButtonNavigation");
        var text = ui.Text;
        if (text.Contains("__gcetHotscenesAfterV1", StringComparison.Ordinal))
        {
            if (!text.Contains("phase = \"after\"", StringComparison.Ordinal) ||
                !text.Contains("ObserveAfter('PlayerPuppet', 'OnAction', __gcetHotscenesOnAction)", StringComparison.Ordinal) ||
                !text.Contains("local function __gcetHotscenesSync()", StringComparison.Ordinal) ||
                Regex.Matches(text, @"(?m)^\s*__gcetHotscenesSync\(\)", RegexOptions.CultureInvariant).Count < 7)
                throw new InvalidOperationException("Hotscenes after-phase source is partially installed or changed.");
            const string oldCall = "pcall(engine.SubscribeAction,";
            const string newCall = "pcall(api.SubscribeAction,";
            if (text.Contains(newCall, StringComparison.Ordinal))
            {
                if (!text.Contains("type(engine.GCET.SubscribeAction)", StringComparison.Ordinal))
                    throw new InvalidOperationException("Hotscenes API migration is incomplete.");
                return SemanticInjectionResult.Skip("Hotscenes after-phase action API already method-resolved.");
            }
            if (!text.Contains(oldCall, StringComparison.Ordinal) ||
                !text.Contains("if found and type(engine) == \"table\" and type(engine.SubscribeAction) == \"function\" then", StringComparison.Ordinal))
                throw new InvalidOperationException("Hotscenes previous action API shape changed.");
            var crlf = text.Contains("\r\n", StringComparison.Ordinal);
            var normalized = crlf ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;
            var migrated = normalized.Replace(
                "if found and type(engine) == \"table\" and type(engine.SubscribeAction) == \"function\" then",
                "if found and type(engine) == \"table\" then\n" +
                "            local api = engine\n" +
                "            if type(engine.GCET) == \"table\" and type(engine.GCET.SubscribeAction) == \"function\" then api = engine.GCET end\n" +
                "            if type(api.SubscribeAction) == \"function\" then",
                StringComparison.Ordinal).Replace(oldCall, newCall, StringComparison.Ordinal);
            // Exactly one additional end closes the method-capability branch.
            const string oldEnd = "                    __gcetHotscenesRouted = true\n                end\n            end\n        end\n    end";
            const string newEnd = "                    __gcetHotscenesRouted = true\n                end\n            end\n            end\n        end\n    end";
            if (!migrated.Contains(oldEnd, StringComparison.Ordinal))
                throw new InvalidOperationException("Hotscenes subscriber closing anchor changed.");
            migrated = migrated.Replace(oldEnd, newEnd, StringComparison.Ordinal);
            context.Write(ui, crlf ? migrated.Replace("\n", "\r\n", StringComparison.Ordinal) : migrated);
            return SemanticInjectionResult.Success("Upgraded deployed after-phase subscriber to method-resolved Engine/Engine.GCET action API.");
        }

        // Both initialization and every later author isShowtime writer are
        // source-proven. A newly added writer or changed widget lifecycle must
        // fail closed instead of silently losing the first menu navigation.
        var flags = new Regex(
            @"nativeUI\.isShowtime[ \t]*=[ \t]*false\r?\n" +
            @"nativeUI\.isMainMenu[ \t]*=[ \t]*false\r?\n" +
            @"nativeUI\.isSelectionList[ \t]*=[ \t]*false",
            RegexOptions.CultureInvariant);
        var initial = flags.Matches(text);
        if (initial.Count != 1)
            throw new InvalidOperationException("Hotscenes initial native UI lifecycle drifted.");

        const string sync = """
-- __gcetHotscenesAfterV1: UI visibility is the independent wake path.
-- The entire video/scene player must NOT gate this navigation-only observer.
local __gcetHotscenesAfterHandle = nil
local function __gcetHotscenesSync()
    local h = __gcetHotscenesAfterHandle
    if h and type(h.SetActive) == "function" then
        pcall(h.SetActive, nativeUI.isShowtime == true)
    end
end
""";
        var insert = initial[0].Index + initial[0].Length;
        text = text[..insert] + "\n" + sync + text[insert..];

        var writers = new Regex(
            @"(?m)^(?<indent>[ \t]*)[^\r\n]*\bnativeUI\.isShowtime[ \t]*=[ \t]*(?:true|false)[^\r\n]*",
            RegexOptions.CultureInvariant);
        var hits = writers.Matches(text);
        if (hits.Count != 8)
            throw new InvalidOperationException(
                "Hotscenes UI writer count changed; expected init plus seven open/close sites.");
        var index = 0;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        text = writers.Replace(text, match =>
        {
            if (index++ == 0) return match.Value; // init assignment precedes helper
            return match.Value + newline + match.Groups["indent"].Value +
                "__gcetHotscenesSync()";
        });

        const string opening = "ObserveAfter('PlayerPuppet', 'OnAction', function(this, action, consumer)";
        var start = text.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0 || text.IndexOf(opening, start + opening.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("Hotscenes navigation observer not unique.");
        var end = text.IndexOf("\n\tend)", start, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("Hotscenes after observer cannot be bounded.");
        var body = text[(start + opening.Length)..end];
        if (!body.Contains("action:GetType(action).value", StringComparison.Ordinal) ||
            !body.Contains("'REPEAT'", StringComparison.Ordinal) ||
            !body.Contains("getNavigation(action)", StringComparison.Ordinal) ||
            !body.Contains("lastShardNotificationController", StringComparison.Ordinal))
            throw new InvalidOperationException("Hotscenes navigation logic changed.");

        const string route = """
    -- Preserve exact CET after-observer semantics and every original action.
    -- A shared bridge still receives native events; this is subscriber-level
    -- dormancy, NOT proof of eliminating the shared native CET crossing.
    local __gcetHotscenesRouted = false
    if type(GetMod) == "function" then
        local found, engine = pcall(GetMod, "0-Engine")
        if found and type(engine) == "table" then
            local api = engine
            if type(engine.GCET) == "table"
                and type(engine.GCET.SubscribeAction) == "function" then
                api = engine.GCET
            end
            if type(api.SubscribeAction) == "function" then
            local subscribed, handle = pcall(api.SubscribeAction,
                { actions = "*", phase = "after", decodeType = false,
                  active = true, id = "hotscenes-menu-navigation" },
                __gcetHotscenesOnAction, "hotscenes")
            if subscribed and type(handle) == "table"
                and type(handle.SetActive) == "function"
                and type(handle.IsActive) == "function" then
                local checked, available = pcall(handle.IsActive)
                if checked and available == true then
                    __gcetHotscenesAfterHandle = handle
                    __gcetHotscenesSync()
                    __gcetHotscenesRouted = true
                end
            end
            end
        end
    end
    if not __gcetHotscenesRouted then
        ObserveAfter('PlayerPuppet', 'OnAction', __gcetHotscenesOnAction)
    end
""";
        text = text[..start] +
            "local function __gcetHotscenesOnAction(this, action, consumer)" +
            body + "\n\tend\n" + route + text[(end + "\n\tend)".Length)..];
        context.Write(ui, text);
        return SemanticInjectionResult.Success(
            "Hotscenes exact REPEAT/navigation observer now uses a lazily shared after-phase " +
            "0-Engine action subscriber only when the existing Showtime UI is visible. " +
            "All seven author UI show/hide sites wake/pause synchronously; original CET " +
            "ObserveAfter and full video/scene functionality remain as fallback. " +
            "The one shared after-phase native observer remains live after registration.");
    }
}
