using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Source-shaped, version-fluent NPC Outfit Manager optimization. Rewrites
/// only source-proven live code and leaves unfamiliar author versions untouched.
/// </summary>
internal static class NpcOutfitManagerSource
{
    private const string Rule = "npc-outfit-manager-idle-state";
    private static readonly Regex TargetRead = new(
        @"(?m)^(?<indent>[ \t]*)local\s+currentTarget\s*=\s*GetLookAtNPC\s*\(\s*\)[ \t]*\r?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex RadarTick = new(
        @"(?ms)^(?<indent>[ \t]*)radarTimer\s*=\s*radarTimer\s*\+\s*delta[ \t]*\r?\n" +
        @"(?:(?!^\k<indent>end[ \t]*\r?$)[\s\S])*?" +
        @"^\k<indent>end[ \t]*\r?$",
        RegexOptions.CultureInvariant);

    private static readonly Regex StateAnchor = new(
        @"(?m)^(?<line>[ \t]*local\s+bodyPartStates\s*=\s*\{\}[^\r\n]*\r?\n)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Mutation = new(
        @"(?m)^(?<indent>[ \t]*)(?<assignment>(?:currentSelections|bodyPartStates|activeRefits|activeBodyTypes|equippedSavedMeshes|nativeMeshHiddenStates|hiddenRefits)" +
        @"\s*\[\s*(?<key>targetHash|tHash)\s*\](?:\s*\[[^\r\n]*?\])?\s*=\s*[^\r\n]+)$",
        RegexOptions.CultureInvariant);

    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "GetLookAtNPC()",
            "ProcessRadarOutfits()",
            "autoOutfitsData.outfits",
            "Cron.Update(delta)");
        var source = file.Text;
        if (source.Contains("G-CET semantic:" + Rule, StringComparison.OrdinalIgnoreCase))
            return SemanticInjectionResult.Skip("Current source already contains this NPC outfit optimization.");

        var reads = TargetRead.Matches(source);
        if (reads.Count != 1)
            return SemanticInjectionResult.Skip(
                "Author's active NPC target read is not uniquely recognizable.");
        var read = reads[0];
        var scanner = RadarTick.Matches(source);
        if (scanner.Count != 1)
            return SemanticInjectionResult.Skip(
                "Author's one-second auto-outfit radar tick is not uniquely recognizable.");
        var radar = scanner[0];
        if (radar.Index <= read.Index || radar.Index - read.Index > 2400)
            return SemanticInjectionResult.Skip("The radar no longer follows the targeting fallback.");
        var intervening = source.Substring(
            read.Index + read.Length, radar.Index - read.Index - read.Length);
        if (!Regex.IsMatch(intervening, @"\bif\s+currentTarget\s+then\b") ||
            !intervening.Contains("activeNPCs", StringComparison.Ordinal) ||
            !radar.Value.Contains("ProcessRadarOutfits()", StringComparison.Ordinal) ||
            !source.Substring(Math.Max(0, read.Index - 4400),
                Math.Min(4400, read.Index)).Contains("Cron.Update(delta)", StringComparison.Ordinal))
            return SemanticInjectionResult.Skip(
                "Target/radar author behavior changed; no unverified wrapper emitted.");

        var indent = read.Groups["indent"].Value;
        var radarText = radar.Value;
        var replacement =
            indent + "local currentTarget = nil\n" +
            indent + "local __gcetHasAutoOutfitRules = type(autoOutfitsData) == \"table\"\n" +
            indent + "    and type(autoOutfitsData.outfits) == \"table\"\n" +
            indent + "    and next(autoOutfitsData.outfits) ~= nil\n" +
            indent + "if __gcetHasAutoOutfitRules or isCustomizeWindowOpen or isCameraModeActive\n" +
            indent + "    or isAnimPlaying or autoRetryNakedTarget or isNCAWindowOpen\n" +
            indent + "    or isShopSystemActive then\n" +
            indent + "    currentTarget = GetLookAtNPC()\n" +
            intervening +
            indent + "end\n" +
            indent + "if __gcetHasAutoOutfitRules then\n" +
            radarText + "\n" +
            indent + "else\n" +
            indent + "    radarTimer = 0.0\n" +
            indent + "end";

        var output = source[..read.Index] + replacement +
            source[(radar.Index + radar.Length)..];

        // The author's mutable tables are already canonical live state. A
        // minimal revision/cache of their references is refreshed on known
        // writer paths, never reconstructed once per onUpdate/onDraw. It is
        // session-local and deliberately does NOT overwrite saved presets.
        var stateMatches = StateAnchor.Matches(output);
        if (stateMatches.Count != 1)
            return SemanticInjectionResult.Skip(
                "Current version has no uniquely identifiable body-state table.");
        var stateAnchor = stateMatches[0];
        var cache =
            "local __gcetLastEditedOutfits = {}\n" +
            "local function __gcetOutfitEdited(key)\n" +
            "    if key == nil then return end\n" +
            "    local state = __gcetLastEditedOutfits[key]\n" +
            "    if state == nil then\n" +
            "        state = { revision = 0 }\n" +
            "        __gcetLastEditedOutfits[key] = state\n" +
            "    end\n" +
            "    state.revision = state.revision + 1\n" +
            "    state.items = currentSelections[key]\n" +
            "    state.body_states = bodyPartStates[key]\n" +
            "    state.refits = activeRefits[key]\n" +
            "    state.body_type = activeBodyTypes[key]\n" +
            "    state.base_meshes = equippedSavedMeshes[key]\n" +
            "    state.native_meshes = nativeMeshHiddenStates[key]\n" +
            "    state.hidden_refits = hiddenRefits[key]\n" +
            "end\n";
        output = output.Insert(stateAnchor.Index + stateAnchor.Length, cache);

        var writes = 0;
        output = Mutation.Replace(output, m =>
        {
            // Do not instrument assignments from our own cache helper.
            if (m.Groups["indent"].Value.Length == 0 &&
                m.Groups["assignment"].Value.StartsWith("local ", StringComparison.Ordinal))
                return m.Value;
            ++writes;
            return m.Value + "\n" + m.Groups["indent"].Value +
                "__gcetOutfitEdited(" + m.Groups["key"].Value + ")";
        });
        if (writes < 10)
            return SemanticInjectionResult.Skip(
                "Too few recognizable author outfit mutation sites; no complete pass.");

        context.Write(file, output);
        return SemanticInjectionResult.Success(
            $"Gated idle radar/target native queries behind active outfit/UI work, " +
            $"and refreshed session-only canonical outfit state after {writes} author writes; " +
            "no disk autosave, changed gameplay cadence or guessed version SHA.");
    }

    internal static SemanticInjectionResult ApplyRecordIdNormalization(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua", "local function GetNPCID(target)",
            "ProcessRadarOutfits()", "target:GetRecordID()", "autoOutfitsData.outfits");
        var text = file.Text;
        const string stage = "__gcetNpcIdNormalizedByRawRecord";
        if (text.Contains(stage, StringComparison.Ordinal))
            return SemanticInjectionResult.Skip(
                "Record-string normalization stage already installed.");
        // The cached key is the CURRENT raw record-id string, never a native
        // entity handle or an assumed record identity from an earlier frame.
        var function = new Regex(
            @"(?ms)^(?<head>[ \t]*local\s+function\s+GetNPCID\(\s*target\s*\)\r?\n)" +
            @"(?<body>.*?)(?<tail>^[ \t]*end\s*\r?\n)(?=\s*local\s+function\s+CleanKey\()",
            RegexOptions.CultureInvariant);
        var functions = function.Matches(text);
        if (functions.Count != 1)
            throw new InvalidOperationException("NPC record-normalization function boundaries drifted.");
        var hit = functions[0];
        var body = hit.Groups["body"].Value;
        var raw = new Regex(
            @"(?m)^(?<indent>[ \t]*)local\s+rawID\s*=\s*tostring\(\s*target:GetRecordID\(\)\s*\)[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var cleaned = new Regex(
            @"(?m)^(?<indent>[ \t]*)if\s+match\s*~=\s*['""]['""]\s+then\s+return\s+match\s+end[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var fallback = new Regex(
            @"(?m)^(?<indent>[ \t]*)return\s+rawID[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        if (raw.Matches(body).Count != 1 || cleaned.Matches(body).Count != 1 ||
            fallback.Matches(body).Count != 1 ||
            !Regex.IsMatch(body, @"if\s+not\s+target\s+then\s+return\s+['""]Unknown['""]\s+end") ||
            !body.Contains("string.find(rawID", StringComparison.Ordinal) ||
            !body.Contains("rawID:match(", StringComparison.Ordinal) ||
            !body.Contains("match:gsub(", StringComparison.Ordinal))
            throw new InvalidOperationException("NPC record parser has unknown behavioral changes.");
        body = raw.Replace(body, m => m.Value.TrimEnd('\r') +
            "\n" + m.Groups["indent"].Value + "local __gcetNormalized = " + stage + "[rawID]" +
            "\n" + m.Groups["indent"].Value + "if __gcetNormalized ~= nil then return __gcetNormalized end");
        body = cleaned.Replace(body, m => m.Groups["indent"].Value +
            "if match ~= \"\" then return __gcetRememberNpcId(rawID, match) end");
        body = fallback.Replace(body, m => m.Groups["indent"].Value +
            "return __gcetRememberNpcId(rawID, rawID)");
        var helper =
            "-- G-CET raw record string normalization cache; preserves a fresh native GetRecordID read.\n" +
            "local " + stage + " = {}\n" +
            "local __gcetNpcIdCacheCount = 0\n" +
            "local function __gcetRememberNpcId(rawID, result)\n" +
            "    if __gcetNpcIdCacheCount >= 512 then\n" +
            "        " + stage + " = {}\n" +
            "        __gcetNpcIdCacheCount = 0\n" +
            "    end\n" +
            "    " + stage + "[rawID] = result\n" +
            "    __gcetNpcIdCacheCount = __gcetNpcIdCacheCount + 1\n" +
            "    return result\n" +
            "end\n\n";
        text = text[..hit.Index] + helper + hit.Groups["head"].Value + body +
            hit.Groups["tail"].Value + text[(hit.Index + hit.Length)..];
        if (!text.Contains("if __gcetNormalized ~= nil then return __gcetNormalized end",
                StringComparison.Ordinal) ||
            !text.Contains("return __gcetRememberNpcId(rawID, match)", StringComparison.Ordinal))
            throw new InvalidOperationException("NPC normalization cache postconditions failed.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Bounded 512-entry raw record-string normalization memo avoids repeated Lua find/match/gsub; preserves fresh per-NPC native GetRecordID, NCA fallback, live outfit lookup and attachment lifecycle.");
    }
}
