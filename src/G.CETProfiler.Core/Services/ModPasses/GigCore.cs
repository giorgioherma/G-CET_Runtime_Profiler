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
            "defs.UI_System",
            "defs.PhotoMode",
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
                !(body.Contains("Game.GetAllBlackboardDefs()", StringComparison.Ordinal) ||
                  body.Contains("__gcetGetAllBlackboardDefs()", StringComparison.Ordinal)))
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

        // Move the original per-frame PhoneSystem protected closure to a
        // stable module thunk. Same fresh native reads and exception fallback:
        // do NOT cache PhoneSystem across sessions or gate the phone wake.
        text = ReduceGigCorePhoneClosure(text);

        // Phase 3 source-owned idle service. This module is optional on
        // older supported author layouts; their prior stages still compose.
        bool damageChanged = false;
        if (context.TryReadComposedModFile(context.Owner,
                "core/vehicle_damage.lua") is not null)
        {
            var damage = context.FindFile(
                "core/vehicle_damage.lua",
                "function VD.tick(dt)",
                "local subs",
                "Bus.takeDamageEvent()",
                "backstopPoll()");
            var damageText = ReduceGigCoreUnsubscribedDamageWork(damage.Text);
            damageChanged = damageText != damage.Text;
            if (damageChanged)
                context.Write(damage, damageText);
        }

        if (text == original && !damageChanged)
            return SemanticInjectionResult.Skip(
                "GigCore blackboard, pause, phone and idle damage stages are already complete.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Reused blackboards and protected pause/phone thunks; eliminated idle " +
            "vehicle-handle/stat health polling and subscriberless coalescer work " +
            "while retaining reds event draining, active damage processing, " +
            "gig timers, markers, commands, session cleanup and reconcile.");
    }

    private static string ReduceGigCoreUnsubscribedDamageWork(string text)
    {
        const string stage = "__gcetGigDamageNoSubscribersV1";
        if (text.Contains(stage, StringComparison.Ordinal))
        {
            var start = text.IndexOf("function VD.tick(dt)", StringComparison.Ordinal);
            var end = start < 0 ? -1 : text.IndexOf("\nend", start, StringComparison.Ordinal);
            if (end < 0)
                throw new InvalidOperationException("GigCore damage idle stage unbounded.");
            var body = text[start..end];
            if (!body.Contains("if #subs == 0 then", StringComparison.Ordinal) ||
                !body.Contains("healthVid, healthPrev = nil, nil", StringComparison.Ordinal) ||
                !body.Contains("Bus.takeDamageEvent()", StringComparison.Ordinal) ||
                !body.Contains("for k in pairs(accum) do accum[k] = nil end",
                    StringComparison.Ordinal) ||
                !body.Contains("backstopPoll()", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "GigCore damage idle marker exists but source postconditions fail.");
            return text;
        }

        var tick = new Regex(
            @"(?m)^function VD\.tick\(dt\)\r?\n" +
            @"[ \t]*now\s*=\s*now\s*\+\s*\(dt or 0\)\r?\n" +
            @"[ \t]*local guard = 0",
            RegexOptions.CultureInvariant);
        if (tick.Matches(text).Count != 1)
            throw new InvalidOperationException(
                "GigCore vehicle damage tick source changed; cannot prove one owner.");

        var startIndex = text.IndexOf("function VD.tick(dt)", StringComparison.Ordinal);
        var afterTick = text.IndexOf("\nend", startIndex, StringComparison.Ordinal);
        if (afterTick < 0)
            throw new InvalidOperationException("GigCore damage tick is unbounded.");
        var originalTick = text[startIndex..afterTick];
        if (!originalTick.Contains("local ki = Bus.takeDamageEvent()", StringComparison.Ordinal) ||
            !originalTick.Contains("local amt = Bus.lastDamageAmount()", StringComparison.Ordinal) ||
            !originalTick.Contains("backstopPoll()", StringComparison.Ordinal) ||
            !originalTick.Contains("flushDue()", StringComparison.Ordinal) ||
            !text.Contains("local subs        = {}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "GigCore damage queue/backstop ownership drifted; refuse idle optimization.");

        // Without subscribers the old native mounted-vehicle health backstop
        // cannot reach a consumer. Still drain the REDS event stream so stale
        // damage never reaches a later subscriber, and reset any prior pending
        // coalesced samples and health baseline. Active subscribers use the
        // unmodified full-rate author path.
        const string inserted =
            "  -- __gcetGigDamageNoSubscribersV1: keep REDS drained without native Health reads.\n" +
            "  if #subs == 0 then\n" +
            "    healthVid, healthPrev = nil, nil\n" +
            "    local drained = 0\n" +
            "    while drained < 64 do\n" +
            "      drained = drained + 1\n" +
            "      local ki = Bus.takeDamageEvent()\n" +
            "      if not ki or ki < 0 then break end\n" +
            "    end\n" +
            "    for k in pairs(accum) do accum[k] = nil end\n" +
            "    return\n" +
            "  end\n";
        var m = tick.Match(text);
        var matchText = m.Value;
        var newLine = matchText.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        var guardLine = matchText.LastIndexOf('\n') + 1;
        if (guardLine == 0)
            throw new InvalidOperationException("GigCore damage guard line not bounded.");
        var replacement = matchText[..guardLine] +
            inserted.Replace("\n", newLine, StringComparison.Ordinal) +
            matchText[guardLine..];
        return text[..m.Index] + replacement + text[(m.Index + m.Length)..];
    }

    private static string ReduceGigCorePhoneClosure(string text)
    {
        const string readName = "__gcetGigPhoneOpenedRead";
        const string newCall = "local ok, open = pcall(__gcetGigPhoneOpenedRead)";
        if (text.Contains(newCall, StringComparison.Ordinal))
        {
            if (!text.Contains("local function " + readName + "()", StringComparison.Ordinal) ||
                text.Contains("pcall(function()\n    local ps = Game.GetScriptableSystemsContainer():Get(\"PhoneSystem\")",
                              StringComparison.Ordinal))
                throw new InvalidOperationException("GigCore phone protected source stage is partial.");
            return text;
        }
        if (text.Contains("local function " + readName + "()", StringComparison.Ordinal))
            throw new InvalidOperationException("GigCore phone read helper exists without its callsite.");
        // Prove the entire author function body as a line-sequenced control
        // flow. This avoids brittle mixed-quote regex backreferences while
        // rejecting any additional work or changed native accessor.
        var starts = Regex.Matches(text,
            @"(?m)^[ \t]*local function phoneIsOpen\(\)[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var ends = Regex.Matches(text,
            @"(?m)^[ \t]*local function phoneUiOpen\(\)[ \t]*return[ \t]+phoneIsOpen\(\)[ \t]*end[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        // Older supported GigCore source/CI fixtures have no authored phone
        // detector. The earlier blackboard/pause stages must still generate
        // in one pass; do not manufacture a phone subsystem in those versions.
        if (starts.Count == 0 && ends.Count == 0 &&
            !text.Contains("phoneIsOpen(", StringComparison.Ordinal) &&
            !text.Contains("__gcetGigPhoneOpenedRead", StringComparison.Ordinal))
            return text;
        if (starts.Count != 1 || ends.Count != 1 ||
            starts[0].Index >= ends[0].Index)
            throw new InvalidOperationException(
                "GigCore author phone-open function boundaries changed.");
        var segment = text[starts[0].Index..ends[0].Index];
        var actual = segment.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
        var expected = new[]
        {
            "local function phoneIsOpen()",
            "local open",
            "pcall(function()",
            "local ps = Game.GetScriptableSystemsContainer():Get(\"PhoneSystem\")",
            "if ps then open = ps:IsPhoneOpened() end",
            "end)",
            "return open == true",
            "end"
        };
        expected[3]=PreserveGenericGetters(text,expected[3]);
        if (actual.Length != expected.Length)
            throw new InvalidOperationException(
                "GigCore author protected phone-open function changed.");
        for (var i = 0; i < expected.Length; i++)
        {
            var value = actual[i];
            if (i == 3 && value ==
                PreserveGenericGetters(text,
                  "local ps = Game.GetScriptableSystemsContainer():Get('PhoneSystem')"))
                continue;
            if (!string.Equals(value, expected[i], StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "GigCore protected PhoneSystem access or failure behavior changed.");
        }
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var replacement =
            "local function __gcetGigPhoneOpenedRead()" + nl +
            "  " + PreserveGenericGetters(text,
                "local ps = Game.GetScriptableSystemsContainer():Get(\"PhoneSystem\")") + nl +
            "  return ps and ps:IsPhoneOpened()" + nl +
            "end" + nl +
            "local function phoneIsOpen()" + nl +
            "  local ok, open = pcall(__gcetGigPhoneOpenedRead)" + nl +
            "  return ok and open == true" + nl +
            "end" + nl;
        return text[..starts[0].Index] + replacement + text[ends[0].Index..];
    }

    internal static bool IsGigCorePhoneClosureComplete(string ownerFolder)
    {
        try
        {
            var source = File.ReadAllText(Path.Combine(ownerFolder, "init.lua"));
            // Existing and new GigCore stages are independently optional by
            // authored source layout. A completed init.lua does NOT prove the
            // damage subservice's own stage in a modern installed version.
            var damagePath = Path.Combine(ownerFolder, "core", "vehicle_damage.lua");
            if (File.Exists(damagePath))
            {
                var damage = File.ReadAllText(damagePath);
                var tickStart = damage.IndexOf("function VD.tick(dt)", StringComparison.Ordinal);
                if (tickStart < 0)
                    return false;
                var tickEnd = damage.IndexOf("\nend", tickStart, StringComparison.Ordinal);
                if (tickEnd < 0)
                    return false;
                var tickBody = damage[tickStart..tickEnd];
                if (!tickBody.Contains("__gcetGigDamageNoSubscribersV1", StringComparison.Ordinal) ||
                    !tickBody.Contains("if #subs == 0 then", StringComparison.Ordinal) ||
                    !tickBody.Contains("healthVid, healthPrev = nil, nil", StringComparison.Ordinal) ||
                    !tickBody.Contains("for k in pairs(accum) do accum[k] = nil end",
                        StringComparison.Ordinal) ||
                    !tickBody.Contains("backstopPoll()", StringComparison.Ordinal) ||
                    !tickBody.Contains("flushDue()", StringComparison.Ordinal))
                    return false;
            }
            var phone = Regex.IsMatch(source,
                @"(?m)^[ \t]*local function phoneIsOpen\(\)",
                RegexOptions.CultureInvariant);
            var alias = Regex.IsMatch(source,
                @"(?m)^[ \t]*local function phoneUiOpen\(\)",
                RegexOptions.CultureInvariant);
            // The old GigCore fixture and original author variants without
            // this service cannot require a non-existent phone read stage.
            if (!phone && !alias)
                return !source.Contains("__gcetGigPhoneOpenedRead", StringComparison.Ordinal);
            if (!phone || !alias) return false;
            return source.Contains("local function __gcetGigPhoneOpenedRead()", StringComparison.Ordinal) &&
                   source.Contains("local ok, open = pcall(__gcetGigPhoneOpenedRead)", StringComparison.Ordinal) &&
                   source.Contains("return ok and open == true", StringComparison.Ordinal) &&
                   !source.Contains("pcall(function()\n    local ps = Game.GetScriptableSystemsContainer():Get(\"PhoneSystem\")", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // SPECTER 1.4.1 and 1.5 share their M9->M10 handoff.
    // 1.5 optionally adds a read-only music contract constructed every frame.
}
