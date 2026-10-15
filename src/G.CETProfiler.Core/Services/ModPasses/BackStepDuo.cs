using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    // Source-proven independent wake is the author's nativeSettings EnableDash
    // switch; game OnAction is not required for that switch to re-enable input.
    // Pause only this subscriber, retaining the shared native observer and the
    // original CET observer fallback on older/incompatible 0-Engine versions.
    internal static bool IsBackStepDuoDormancyComplete(string folder)
    {
        var path = Path.Combine(folder, "init.lua");
        if (!File.Exists(path)) return false;
        var t = File.ReadAllText(path);
        return t.Contains("__gcetBackStepDuoDormancyV2", StringComparison.Ordinal) &&
            t.Contains("local function __gcetBackStepDuoSync()", StringComparison.Ordinal) &&
            t.Contains("pcall(h.SetActive, settings.EnableDash == true)", StringComparison.Ordinal) &&
            t.Contains("gcetBackStepDuoOnAction", StringComparison.Ordinal) &&
            t.Contains("SubscribeAction", StringComparison.Ordinal) &&
            t.Contains("Observe(\"PlayerPuppet\", \"OnAction\", gcetBackStepDuoOnAction)", StringComparison.Ordinal) &&
            Regex.Matches(t, @"settings\.EnableDash\s*=\s*state\s*\r?\n\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count == 1 &&
            Regex.Matches(t, @"__gcetBackStepDuoHandle\s*=\s*handle;\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count == 2 &&
            Regex.Matches(t, @"\bsettings\.EnableDash\s*=(?![=])", RegexOptions.CultureInvariant).Count == 2;
    }

    private static SemanticInjectionResult ApplyBackStepDuo(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua", "processDirectionAction", "getActionName",
            "getActionType", "getActionValue", "BUTTON_PRESSED", "movey", "movex");

        if (file.Text.Contains("__gcetBackStepDuoDormancyV2", StringComparison.Ordinal))
        {
            if (!IsBackStepDuoDormancyCompleteText(file.Text))
                throw new InvalidOperationException("Partial BackStepDuo subscriber dormancy stage; refusing drift.");
            return SemanticInjectionResult.Skip("BackStepDuo action dormancy already source-complete.");
        }
        var source = file.Text;
        if (Regex.Matches(source, @"\bsettings\.EnableDash\s*=(?![=])", RegexOptions.CultureInvariant).Count != 2 ||
            !source.Contains("settings.EnableDash = state", StringComparison.Ordinal) ||
            !source.Contains("settings.EnableDash = loaded.EnableDash", StringComparison.Ordinal) ||
            !source.Contains("registerInput(\"InstantLookBack\"", StringComparison.Ordinal))
            throw new InvalidOperationException("BackStepDuo author settings/wake writers have changed.");

        var text = source.Contains("local function gcetBackStepDuoOnAction(", StringComparison.Ordinal)
            ? source
            : RouteExactOnActionObserver(
                source, "gcetBackStepDuoOnAction", "BackStepDuo",
                new[] { "Forward", "Back", "Left", "Right", "MoveY", "MoveX" },
                "BackStepDuo PlayerPuppet OnAction",
                "settings.EnableDash", "getActionName", "getActionType",
                "getActionValue", "processDirectionAction");

        const string fn = "gcetBackStepDuoOnAction";
        const string client = "__gcetClient_" + fn;
        const string engine = "__gcetEngine_" + fn;
        const string table = "__gcetActions_" + fn;
        const string handle = "__gcetBackStepDuoHandle";
        const string sync = "__gcetBackStepDuoSync";
        if (!text.Contains("Observe(\"PlayerPuppet\", \"OnAction\", " + fn + ")", StringComparison.Ordinal))
            throw new InvalidOperationException("BackStepDuo original native observer fallback not proven.");

        text = ReplaceOnce(text,
            "local function loadSettingsFile()",
            "-- __gcetBackStepDuoDormancyV2: author-nativeSettings EnableDash owns input activity\n" +
            "local " + handle + " = nil\n" +
            "local function " + sync + "()\n" +
            "    local h = " + handle + "\n" +
            "    if type(h) == \"table\" and type(h.SetActive) == \"function\" then\n" +
            "        pcall(h.SetActive, settings.EnableDash == true)\n" +
            "    end\n" +
            "end\n\n" +
            "local function loadSettingsFile()",
            "BackStepDuo dormancy helper");

        text = ReplaceOnce(text,
            "settings.EnableDash = state\n",
            "settings.EnableDash = state\n            " + sync + "()\n",
            "BackStepDuo authored nativeSettings wake");

        var options = "{ actions = " + table + ", decodeType = false }";
        var clientCall = client + ".SubscribeAction(" + options + ", " + fn + ")";
        var engineCall = engine + ".SubscribeAction(" + options + ", " + fn + ", \"BackStepDuo\")";
        text = ReplaceOnce(text,
            "pcall(function() " + clientCall + " end)",
            "pcall(function() local handle = " + clientCall + "; if type(handle) == \"table\" then " +
                handle + " = handle; " + sync + "() end end)",
            "BackStepDuo client subscriber handle");
        text = ReplaceOnce(text,
            "pcall(function() " + engineCall + " end)",
            "pcall(function() local handle = " + engineCall + "; if type(handle) == \"table\" then " +
                handle + " = handle; " + sync + "() end end)",
            "BackStepDuo engine subscriber handle");

        if (!IsBackStepDuoDormancyCompleteText(text))
            throw new InvalidOperationException(
                "BackStepDuo action routing / wake proof failed after transform: " +
                "marker=" + text.Contains("__gcetBackStepDuoDormancyV2", StringComparison.Ordinal) +
                ";sync=" + text.Contains("local function __gcetBackStepDuoSync()", StringComparison.Ordinal) +
                ";SetActive=" + text.Contains("pcall(h.SetActive, settings.EnableDash == true)", StringComparison.Ordinal) +
                ";fallback=" + text.Contains("Observe(\"PlayerPuppet\", \"OnAction\", gcetBackStepDuoOnAction)", StringComparison.Ordinal) +
                ";switchCount=" + Regex.Matches(text, @"settings\.EnableDash\s*=\s*state\s*\r?\n\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count +
                ";handles=" + Regex.Matches(text, @"__gcetBackStepDuoHandle\s*=\s*handle;\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count +
                ";writerCount=" + Regex.Matches(text, @"\bsettings\.EnableDash\s*=(?![=])", RegexOptions.CultureInvariant).Count);
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved six-action routing and native Observe fallback; paused only the optional BackStepDuo 0-Engine subscriber when EnableDash is off, with immediate author switch resume and safe old runtime fallback.");
    }

    private static bool IsBackStepDuoDormancyCompleteText(string t) =>
        t.Contains("__gcetBackStepDuoDormancyV2", StringComparison.Ordinal) &&
        t.Contains("local function __gcetBackStepDuoSync()", StringComparison.Ordinal) &&
        t.Contains("pcall(h.SetActive, settings.EnableDash == true)", StringComparison.Ordinal) &&
        t.Contains("Observe(\"PlayerPuppet\", \"OnAction\", gcetBackStepDuoOnAction)", StringComparison.Ordinal) &&
        Regex.Matches(t, @"settings\.EnableDash\s*=\s*state\s*\r?\n\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count == 1 &&
        Regex.Matches(t, @"__gcetBackStepDuoHandle\s*=\s*handle;\s*__gcetBackStepDuoSync\(\)", RegexOptions.CultureInvariant).Count == 2 &&
        Regex.Matches(t, @"\bsettings\.EnableDash\s*=(?![=])", RegexOptions.CultureInvariant).Count == 2;
}
