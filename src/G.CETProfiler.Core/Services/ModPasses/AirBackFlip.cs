using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private const string CoordinatorLua = """
        -- G-CET semantic coordinator:air-backflip
        -- Source-owned input and wake paths stay in the author's init.lua.
        -- No new CET callback or input registration is created by this module.
        local M = {}
        local state = { handle = nil, lastActive = nil }
        function M.sync(enabled, flipping)
            local h = state.handle
            local desired = enabled == true or flipping == true
            if type(h) == "table" and type(h.SetActive) == "function" and desired ~= state.lastActive then
                if pcall(h.SetActive, desired) then state.lastActive = desired end
            end
        end
        function M.adopt(handle, enabled, flipping)
            if type(handle) == "table" then
                state.handle = handle
                state.lastActive = nil
                M.sync(enabled, flipping)
            end
        end
        return M
        """;

    internal static bool IsAirBackFlipDormancyComplete(string folder)
    {
        var path = Path.Combine(folder, "init.lua");
        if (!File.Exists(path)) return false;
        var text = File.ReadAllText(path);
        if (!IsAirBackFlipDormancyCompleteText(text)) return false;
        if (!text.Contains("__gcetAirBackFlipCoordinator", StringComparison.Ordinal))
            return true; // previously reviewed inline v2 remains compatible
        var modulePath = Path.Combine(folder, "G-CET", "AirBackFlipCoordinator.lua");
        return File.Exists(modulePath) &&
            File.ReadAllText(modulePath).Replace("\r\n", "\n", StringComparison.Ordinal)
                .Equals(CoordinatorLua.Replace("\r\n", "\n", StringComparison.Ordinal),
                    StringComparison.Ordinal);
    }

    private static bool IsAirBackFlipDormancyCompleteText(string text)
    {
        var common = text.Contains("__gcetAirBackFlipDormancyV2", StringComparison.Ordinal) &&
            text.Contains("local function __gcetAirBackFlipSync()", StringComparison.Ordinal) &&
            text.Contains("Observe(\"PlayerPuppet\", \"OnAction\", gcetAirBackFlipOnAction)", StringComparison.Ordinal) &&
            Regex.IsMatch(text, @"cfg\.enabled\s*=\s*ImGui\.Checkbox\(\s*[""']Enabled[""']\s*,\s*cfg\.enabled\)\s*\r?\n\s*__gcetAirBackFlipSync\(\)", RegexOptions.CultureInvariant) &&
            Regex.Matches(text, @"\bcfg\.enabled\s*=(?![=])", RegexOptions.CultureInvariant).Count == 1;
        if (!common) return false;
        if (text.Contains("__gcetAirBackFlipCoordinator", StringComparison.Ordinal))
            return text.Contains("G-CET/AirBackFlipCoordinator", StringComparison.Ordinal) &&
                text.Contains("pcall(mod.sync, cfg.enabled == true, flip ~= nil)", StringComparison.Ordinal) &&
                Regex.Matches(text,
                    @"pcall\(__gcetAirBackFlipCoordinator\.adopt,\s*handle,\s*cfg\.enabled\s*==\s*true,\s*flip\s*~=\s*nil\)",
                    RegexOptions.CultureInvariant).Count == 2;
        return text.Contains("pcall(h.SetActive, desired)", StringComparison.Ordinal) &&
            Regex.Matches(text, @"__gcetAirBackFlipHandle\s*=\s*handle;\s*__gcetAirBackFlipLastActive\s*=\s*nil;\s*__gcetAirBackFlipSync\(\)",
                RegexOptions.CultureInvariant).Count == 2;
    }

    private static SemanticInjectionResult ApplyAirBackFlip(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua", "GAME_ACTIONS",
            "AirBackflip_Backflip", "AirBackflip_SwingOver", "MoveY",
            "ListenerAction.IsButtonJustPressed", "ListenerAction.IsButtonJustReleased");
        var text = file.Text;
        if (text.Contains("__gcetAirBackFlipDormancyV2", StringComparison.Ordinal))
        {
            if (!IsAirBackFlipDormancyCompleteText(text))
                throw new InvalidOperationException("AirBackFlip incomplete subscriber dormancy stage.");
            if (text.Contains("__gcetAirBackFlipCoordinator", StringComparison.Ordinal))
            {
                context.WriteOwnedModule("G-CET/AirBackFlipCoordinator.lua", CoordinatorLua);
                if (context.OwnedModules.Count > 0)
                    return SemanticInjectionResult.Success("Repaired missing semantic-owned coordinator without altering the author callback.");
            }
            return SemanticInjectionResult.Skip("AirBackFlip subscriber dormancy already installed.");
        }
        if (Regex.Matches(text, @"\bcfg\.enabled\s*=(?![=])", RegexOptions.CultureInvariant).Count != 1 ||
            !text.Contains("cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)", StringComparison.Ordinal) ||
            !text.Contains("local flip = nil", StringComparison.Ordinal) ||
            !text.Contains("local function loadConfig()", StringComparison.Ordinal) ||
            !text.Contains("registerForEvent(\"onUpdate\", function(dt)", StringComparison.Ordinal))
            throw new InvalidOperationException("AirBackFlip UI/config/active-flip wake graph changed.");

        if (!text.Contains("local function gcetAirBackFlipOnAction(", StringComparison.Ordinal))
            text = RouteExactOnActionObserver(text, "gcetAirBackFlipOnAction",
                "AirBackFlip", new[] {
                    "MoveY", "AirBackflip_Backflip", "AirBackflip_Frontflip",
                    "AirBackflip_SideflipLeft", "AirBackflip_SideflipRight",
                    "AirBackflip_SwingOver"
                }, "AirBackFlip PlayerPuppet OnAction",
                "GAME_ACTIONS", "MoveY",
                "ListenerAction.IsButtonJustPressed", "ListenerAction.IsButtonJustReleased");

        const string fn = "gcetAirBackFlipOnAction";
        const string client = "__gcetClient_" + fn;
        const string engine = "__gcetEngine_" + fn;
        const string actions = "__gcetActions_" + fn;
        const string sync = "__gcetAirBackFlipSync";
        const string handle = "__gcetAirBackFlipHandle";
        if (!text.Contains("Observe(\"PlayerPuppet\", \"OnAction\", " + fn + ")", StringComparison.Ordinal))
            throw new InvalidOperationException("AirBackFlip CET native Observe fallback changed.");

        text = ReplaceOnce(text,
            "local function loadConfig()",
            "-- __gcetAirBackFlipDormancyV2: keep actions active during an existing flip\n" +
            "-- __gcetAirBackFlipCoordinatorV1: coordinator owns only the SetActive state\n" +
            "local __gcetAirBackFlipCoordinator\n" +
            "do\n" +
            "    local ok, mod = pcall(require, \"G-CET/AirBackFlipCoordinator\")\n" +
            "    if ok and type(mod) == \"table\" and type(mod.sync) == \"function\" and type(mod.adopt) == \"function\" then\n" +
            "        __gcetAirBackFlipCoordinator = mod\n" +
            "    end\n" +
            "end\n" +
            "local function " + sync + "()\n" +
            "    local mod = __gcetAirBackFlipCoordinator\n" +
            "    if mod then pcall(mod.sync, cfg.enabled == true, flip ~= nil) end\n" +
            "end\n\n" +
            "local function loadConfig()",
            "AirBackFlip shared subscriber lifecycle");

        text = ReplaceOnce(text,
            "cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)\n",
            "cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)\n" +
            "        " + sync + "()\n",
            "AirBackFlip own UI wake transition");

        text = ReplaceOnce(text,
            "registerForEvent(\"onUpdate\", function(dt)\n",
            "registerForEvent(\"onUpdate\", function(dt)\n" +
            "    " + sync + "()\n",
            "AirBackFlip active flip completion sync");

        var opts = "{ actions = " + actions + ", decodeType = false }";
        var clientCall = client + ".SubscribeAction(" + opts + ", " + fn + ")";
        var engineCall = engine + ".SubscribeAction(" + opts + ", " + fn + ", \"AirBackFlip\")";
        text = ReplaceOnce(text, "pcall(function() " + clientCall + " end)",
            "pcall(function() local handle = " + clientCall +
            "; if type(handle) == \"table\" and __gcetAirBackFlipCoordinator then pcall(__gcetAirBackFlipCoordinator.adopt, handle, cfg.enabled == true, flip ~= nil) end end)",
            "AirBackFlip 0-Engine subscriber capture");
        text = ReplaceOnce(text, "pcall(function() " + engineCall + " end)",
            "pcall(function() local handle = " + engineCall +
            "; if type(handle) == \"table\" and __gcetAirBackFlipCoordinator then pcall(__gcetAirBackFlipCoordinator.adopt, handle, cfg.enabled == true, flip ~= nil) end end)",
            "AirBackFlip legacy shared subscriber capture");

        if (!IsAirBackFlipDormancyCompleteText(text))
            throw new InvalidOperationException("AirBackFlip native subscriber/source wake completion proof failed.");
        context.Write(file, text);
        context.WriteOwnedModule("G-CET/AirBackFlipCoordinator.lua", CoordinatorLua);
        return SemanticInjectionResult.Success("Preserved native fallback and six exact input actions; gated only shared AirBackFlip subscriber when feature disabled and no flip is active. Overlay checkbox wakes synchronously; active flip and cleanup retain input delivery.");
    }
}
