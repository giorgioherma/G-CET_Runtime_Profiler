using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    internal static bool IsAirBackFlipDormancyComplete(string folder)
    {
        var path = Path.Combine(folder, "init.lua");
        return File.Exists(path) && IsAirBackFlipDormancyCompleteText(File.ReadAllText(path));
    }

    private static bool IsAirBackFlipDormancyCompleteText(string text) =>
        text.Contains("__gcetAirBackFlipDormancyV2", StringComparison.Ordinal) &&
        text.Contains("local function __gcetAirBackFlipSync()", StringComparison.Ordinal) &&
        text.Contains("pcall(h.SetActive, desired)", StringComparison.Ordinal) &&
        text.Contains("Observe(\"PlayerPuppet\", \"OnAction\", gcetAirBackFlipOnAction)", StringComparison.Ordinal) &&
        Regex.Matches(text, @"__gcetAirBackFlipHandle\s*=\s*handle;\s*__gcetAirBackFlipLastActive\s*=\s*nil;\s*__gcetAirBackFlipSync\(\)", RegexOptions.CultureInvariant).Count == 2 &&
        Regex.IsMatch(text, @"cfg\.enabled\s*=\s*ImGui\.Checkbox\(\s*[""']Enabled[""']\s*,\s*cfg\.enabled\)\s*\r?\n\s*__gcetAirBackFlipSync\(\)", RegexOptions.CultureInvariant) &&
        Regex.Matches(text, @"\bcfg\.enabled\s*=(?![=])", RegexOptions.CultureInvariant).Count == 1;

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
            "local " + handle + " = nil\n" +
            "local __gcetAirBackFlipLastActive = nil\n" +
            "local function " + sync + "()\n" +
            "    local h = " + handle + "\n" +
            "    local desired = cfg.enabled == true or flip ~= nil\n" +
            "    if type(h) == \"table\" and type(h.SetActive) == \"function\" and desired ~= __gcetAirBackFlipLastActive then\n" +
            "        if pcall(h.SetActive, desired) then __gcetAirBackFlipLastActive = desired end\n" +
            "    end\n" +
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
            "; if type(handle) == \"table\" then " + handle + " = handle; __gcetAirBackFlipLastActive = nil; " + sync + "() end end)",
            "AirBackFlip 0-Engine subscriber capture");
        text = ReplaceOnce(text, "pcall(function() " + engineCall + " end)",
            "pcall(function() local handle = " + engineCall +
            "; if type(handle) == \"table\" then " + handle + " = handle; __gcetAirBackFlipLastActive = nil; " + sync + "() end end)",
            "AirBackFlip legacy shared subscriber capture");

        if (!IsAirBackFlipDormancyCompleteText(text))
            throw new InvalidOperationException("AirBackFlip native subscriber/source wake completion proof failed.");
        context.Write(file, text);
        return SemanticInjectionResult.Success("Preserved native fallback and six exact input actions; gated only shared AirBackFlip subscriber when feature disabled and no flip is active. Overlay checkbox wakes synchronously; active flip and cleanup retain input delivery.");
    }
}
