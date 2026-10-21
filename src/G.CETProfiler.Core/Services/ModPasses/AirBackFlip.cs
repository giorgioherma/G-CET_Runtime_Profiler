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


    // 0-Engine generic action routing precedes semantic dormancy. The generic
    // registrar owns the subscriber handle array; reuse it instead of trying
    // to register a duplicate OnAction observer.
    private static bool IsAirBackFlipGenericRouteCompleteText(string source)
    {
        const string marker = "__gcetAirBackFlipDormancyV3";
        if (!source.Contains(marker, StringComparison.Ordinal) ||
            !source.Contains("local function __gcetAirBackFlipSync()", StringComparison.Ordinal) ||
            !source.Contains("local __gcetAirBackFlipHandles = nil", StringComparison.Ordinal) ||
            !source.Contains("__gcetAirBackFlipHandles = __gcetHandles_", StringComparison.Ordinal) ||
            !source.Contains("pcall(h.SetActive, desired)", StringComparison.Ordinal) ||
            !source.Contains("local desired = cfg.enabled == true or flip ~= nil", StringComparison.Ordinal) ||
            !Regex.IsMatch(source,
                @"(?m)cfg\.enabled\s*=\s*ImGui\.Checkbox\(\s*[""']Enabled[""']\s*,\s*cfg\.enabled\)\s*\r?\n\s*__gcetAirBackFlipSync\(\)",
                RegexOptions.CultureInvariant) ||
            Regex.Matches(source, @"\bcfg\.enabled\s*=(?![=])",
                RegexOptions.CultureInvariant).Count != 1)
            return false;
        var m=Regex.Match(source, @"local\s+__gcetAirBackFlipHandles\s*=\s*__gcetHandles_(?<id>\d+)\b",
            RegexOptions.CultureInvariant);
        if (!m.Success) return false;
        var id=m.Groups["id"].Value;
        return Regex.Matches(source,
                @"\blocal\s+__gcetHandles_"+id+@"\s*=\s*\{\s*\}",
                RegexOptions.CultureInvariant).Count==1 &&
            source.Contains("local function __gcetOnAction_"+id+"(", StringComparison.Ordinal) &&
            source.Contains(
                "if not __gcetRouted_"+id+
                " then Observe(\"PlayerPuppet\", \"OnAction\", __gcetOnAction_"+id+") end",
                StringComparison.Ordinal);
    }

    private static SemanticInjectionResult ApplyAirBackFlipGenericRoute(
        SemanticPatchContext context, SemanticTextFile file, string source)
    {
        // Only a unique, already-generated generic owner with the original
        // AirBackFlip action body is eligible. No historical SHA or ID list.
        var matches=Regex.Matches(source,
            @"\blocal\s+function\s+__gcetOnAction_(?<id>\d+)\s*\(",
            RegexOptions.CultureInvariant);
        var routes=matches.Cast<Match>().Where(m =>
        {
            var id=m.Groups["id"].Value;
            return source.Contains("local __gcetHandles_"+id+" = {}",StringComparison.Ordinal) &&
                source.Contains(
                    "if not __gcetRouted_"+id+
                    " then Observe(\"PlayerPuppet\", \"OnAction\", __gcetOnAction_"+id+") end",
                    StringComparison.Ordinal);
        }).ToList();
        if(routes.Count!=1)
            throw new InvalidOperationException(
                "AirBackFlip current generic OnAction subscriber owner is absent or ambiguous.");
        var id=routes[0].Groups["id"].Value;
        var opening=new Regex(
            @"(?m)^(?<line>[ \t]*(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\))\s*$",
            RegexOptions.CultureInvariant);
        var update=opening.Matches(source);
        if(update.Count!=1 ||
           Regex.Matches(source,@"\bcfg\.enabled\s*=(?![=])",
               RegexOptions.CultureInvariant).Count!=1 ||
           !source.Contains("cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)", StringComparison.Ordinal) ||
           !source.Contains("local function loadConfig()", StringComparison.Ordinal) ||
           !source.Contains("local flip = nil", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "AirBackFlip generic-first UI/flip/wake source proof changed.");

        var newline=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var text=source;
        text=ReplaceOnce(text, "local function loadConfig()",
            "-- __gcetAirBackFlipDormancyV3: use existing generic 0-Engine action handles"+newline+
            "local __gcetAirBackFlipHandles = nil"+newline+
            "local __gcetAirBackFlipLastActive = nil"+newline+
            "local function __gcetAirBackFlipSync()"+newline+
            "    local handles = __gcetAirBackFlipHandles"+newline+
            "    local desired = cfg.enabled == true or flip ~= nil"+newline+
            "    if type(handles) == \"table\" and desired ~= __gcetAirBackFlipLastActive then"+newline+
            "        local allOk = #handles > 0"+newline+
            "        for _, h in ipairs(handles) do"+newline+
            "            if type(h) ~= \"table\" or type(h.SetActive) ~= \"function\" or not pcall(h.SetActive, desired) then"+newline+
            "                allOk = false"+newline+
            "            end"+newline+
            "        end"+newline+
            "        if allOk then __gcetAirBackFlipLastActive = desired end"+newline+
            "    end"+newline+
            "end"+newline+newline+
            "local function loadConfig()",
            "AirBackFlip generic-first subscriber lifecycle");

        text=ReplaceOnce(text,
            "local __gcetHandles_"+id+" = {}",
            "local __gcetHandles_"+id+" = {}"+newline+
            "__gcetAirBackFlipHandles = __gcetHandles_"+id,
            "AirBackFlip generic subscriber handles");

        text=ReplaceOnce(text,
            "cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)"+newline,
            "cfg.enabled = ImGui.Checkbox(\"Enabled\", cfg.enabled)"+newline+
            "        __gcetAirBackFlipSync()"+newline,
            "AirBackFlip configuration wake");

        var m=opening.Match(text);
        if(!m.Success)throw new InvalidOperationException("AirBackFlip frame wake disappeared.");
        text=text.Insert(m.Index+m.Length,newline+"    __gcetAirBackFlipSync()");

        var fallback="if not __gcetRouted_"+id+
            " then Observe(\"PlayerPuppet\", \"OnAction\", __gcetOnAction_"+id+") end";
        text=ReplaceOnce(text,fallback,fallback+newline+
            "__gcetAirBackFlipSync()","AirBackFlip initial subscriber activation");

        if(!IsAirBackFlipGenericRouteCompleteText(text))
            throw new InvalidOperationException("AirBackFlip generic subscriber final postcondition failed.");
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Reuses generic 0-Engine OnAction subscriber handles, with original CET "+
            "fallback; disables handles only when feature is off and no flip is active. "+
            "Original onUpdate/checkbox wakes and gameplay callbacks remain intact.");
    }

    private static SemanticInjectionResult ApplyAirBackFlip(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua", "GAME_ACTIONS",
            "AirBackflip_Backflip", "AirBackflip_SwingOver", "MoveY",
            "ListenerAction.IsButtonJustPressed", "ListenerAction.IsButtonJustReleased");
        var text = file.Text;
        if (text.Contains("__gcetAirBackFlipDormancyV3", StringComparison.Ordinal))
        {
            if (!IsAirBackFlipGenericRouteCompleteText(text))
                throw new InvalidOperationException("AirBackFlip generic subscriber stage is incomplete.");
            return SemanticInjectionResult.Skip("AirBackFlip generic-first dormancy already installed.");
        }
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
            !Regex.IsMatch(text,
                @"\b(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\)",
                RegexOptions.CultureInvariant))
            throw new InvalidOperationException("AirBackFlip UI/config/active-flip wake graph changed.");

        if (Regex.IsMatch(text, @"\blocal\s+function\s+__gcetOnAction_\d+\s*\(",
                RegexOptions.CultureInvariant))
            return ApplyAirBackFlipGenericRoute(context, file, text);

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

        var updateOpening=Regex.Match(text,
            @"(?m)^[ \t]*(?:registerForEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\)",
            RegexOptions.CultureInvariant);
        if (!updateOpening.Success)
            throw new InvalidOperationException("AirBackFlip onUpdate wake not found.");
        text=text.Insert(updateOpening.Index+updateOpening.Length,
            (text.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n")+
            "    "+sync+"()");

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
