using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Authorized one-source Stage A experiment: cache PSM *definition keys* at
/// BlackboardCache.Init, never GetInt/GetBool, the blackboard handle, or state.
/// The current 0-Engine source must prove every modified read and its lifecycle,
/// and in-situ native C-callsite evidence must justify the stage. No SHA pinning.
/// </summary>
internal static class BlackboardPsmDefinitionCacheSource
{
    internal const string Relative = "0-Engine/modules/BlackboardCache2.lua";
    internal const string Marker = "-- G-CET PSM_DEFS_STAGE_A_V1";
    private static readonly (string Name, string Field, string Getter)[] Reads =
    [
        ("locomotion", "Locomotion", "GetInt"),
        ("locomotionDetailed", "LocomotionDetailed", "GetInt"),
        ("upperBody", "UpperBody", "GetInt"),
        ("weapon", "Weapon", "GetInt"),
        ("vision", "Vision", "GetInt"),
        ("swimming", "Swimming", "GetInt"),
        ("sceneTier", "SceneTier", "GetInt"),
        ("takedown", "Takedown", "GetInt"),
        ("landing", "Landing", "GetInt"),
        ("bodyCarrying", "BodyCarrying", "GetInt"),
        ("carrying", "Carrying", "GetInt"),
        ("mounted", "MountedToVehicle", "GetBool")
    ];

    internal static bool HasMeasuredCallsiteEvidence(string captureRoot)
    {
        // Raw rows, not the top-200 summary: BlackboardCache callsites may be
        // outside that report cap. Capture-specific original line numbers.
        var path = Path.Combine(captureRoot, "Data", "Runtime",
            "CET_Runtime_Profile_LuaNative_Callsites.csv");
        if (!File.Exists(path)) return false;

        var sourceLines = new HashSet<int>();
        using var reader = new StreamReader(path);
        var header = reader.ReadLine();
        if (header != "RegistrationId,ProfileEpoch,SampleSequence,Frame," +
                "CallerSourceFile,CallerLine,ParentFunctionKey,ChildFunctionKey," +
                "SampledCCalls,SampledCOwnPositiveBytes,SampledCOwnPositiveOps," +
                "Attribution,Interpretation")
            return false;

        string? row;
        while ((row = reader.ReadLine()) is not null)
        {
            // Native writer emits CSV-quoted source when required. This pinned
            // source has no commas; skip ambiguous rows, never misattribute.
            if (!row.Contains("BlackboardCache2.lua", StringComparison.OrdinalIgnoreCase))
                continue;
            var parts = row.Split(',');
            if (parts.Length != 13) continue;
            var src = parts[4].Replace('\\', '/').Trim('"');
            if (!src.EndsWith("/mods/0-Engine/modules/BlackboardCache2.lua",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (!parts[7].Contains("|__index|C", StringComparison.Ordinal))
                continue;
            if (!int.TryParse(parts[5], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var line) ||
                line <= 0)
                continue;
            if (!long.TryParse(parts[9], NumberStyles.None,
                    CultureInfo.InvariantCulture, out var bytes) || bytes <= 0)
                continue;
            sourceLines.Add(line);
            if (sourceLines.Count == Reads.Length) return true;
        }
        return false;
    }

    internal static bool TryTransform(byte[] source, out byte[] result, out string reason) =>
        TryTransformCore(source, out result, out reason);

    // The SAME exact shape checks apply in tests and production.
    internal static bool TryTransformCore(byte[] source,
        out byte[] result, out string reason)
    {
        result = source;
        var text = Encoding.UTF8.GetString(source);
        if (text.Contains(Marker, StringComparison.Ordinal))
        {
            // A comment is not sufficient proof. Partial or subsequently
            // edited player-state snapshots must not silently pass as complete.
            var complete =
                text.Split(Marker, StringSplitOptions.None).Length == 2 &&
                text.Contains("function BlackboardCache.Init(events)", StringComparison.Ordinal) &&
                text.Contains("function BlackboardCache.Attach(player)", StringComparison.Ordinal) &&
                text.Contains("function BlackboardCache.Reset()", StringComparison.Ordinal) &&
                Regex.Matches(text, @"\b__gcetPsmFields\s*=\s*\{").Count == 1 &&
                Reads.All(read =>
                    Regex.Matches(text, @"\b" + Regex.Escape(read.Field) +
                        @"\s*=\s*PlayerStateDefs\." + Regex.Escape(read.Field) +
                        @"\s*[,}]").Count == 1 &&
                    Regex.Matches(text, @"\blocal\s+" + Regex.Escape(read.Name) +
                        @"\s*=\s*playerBB:" + read.Getter +
                        @"\(\s*__gcetPsmFields\." + Regex.Escape(read.Field) +
                        @"\s*\)").Count == 1);
            reason = complete ? "ALREADY_SATISFIED" :
                "PARTIAL_PSM_STAGE_SOURCE_POSTCONDITION_FAILED";
            return false;
        }
        if (!text.Contains("function BlackboardCache.Init(events)", StringComparison.Ordinal) ||
            !text.Contains("function BlackboardCache.Attach(player)", StringComparison.Ordinal) ||
            !text.Contains("function BlackboardCache.Reset()", StringComparison.Ordinal) ||
            !text.Contains("if not playerBB then return end", StringComparison.Ordinal))
        {
            reason = "Required player/blackboard lifecycle anchors missing.";
            return false;
        }

        // Preserve original line endings. Only insert a fixed local table in
        // Init; never alter Update frequency, game getters, or the event graph.
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        const string declaration = "local PlayerStateDefs = nil";
        const string init = "    PlayerStateDefs = GetAllBlackboardDefs().PlayerStateMachine";
        if (Count(text, declaration) != 1 || Count(text, init) != 1)
        {
            reason = "Definition source/initialization is ambiguous.";
            return false;
        }

        var updated = text.Replace(declaration,
            declaration + newline + "local __gcetPsmFields = nil  " + Marker,
            StringComparison.Ordinal);
        var snapshot = new StringBuilder();
        snapshot.Append(init).Append(newline)
            .Append("    __gcetPsmFields = {").Append(newline);
        foreach (var read in Reads)
            snapshot.Append("        ").Append(read.Field).Append(" = PlayerStateDefs.")
                .Append(read.Field).Append(',').Append(newline);
        snapshot.Append("    }");
        updated = updated.Replace(init, snapshot.ToString(), StringComparison.Ordinal);

        foreach (var read in Reads)
        {
            var pattern = new Regex(
                @"(?m)^(?<indent>[ \t]*)local[ \t]+" + Regex.Escape(read.Name) +
                @"[ \t]*=[ \t]*playerBB:" + read.Getter +
                @"\([ \t]*PlayerStateDefs\." + read.Field + @"[ \t]*\)(?<end>\r?$)",
                RegexOptions.CultureInvariant);
            if (pattern.Matches(updated).Count != 1)
            {
                reason = "Exact 12-read source proof failed for " + read.Name;
                return false;
            }
            updated = pattern.Replace(updated, match =>
                match.Groups["indent"].Value + "local " + read.Name +
                " = playerBB:" + read.Getter +
                "(__gcetPsmFields." + read.Field + ")" +
                match.Groups["end"].Value);
        }
        if (Count(updated, "playerBB:GetInt(") < 11 ||
            Count(updated, "playerBB:GetBool(") < 1 ||
            Count(updated, "__gcetPsmFields.") != Reads.Length ||
            Count(updated, Marker) != 1)
        {
            reason = "Transformed call-count/postcondition mismatch.";
            return false;
        }
        result = Encoding.UTF8.GetBytes(updated);
        reason = "APPLIED";
        return true;
    }

    internal static bool TryStage(
        string captureRoot,
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        if (!HasMeasuredCallsiteEvidence(captureRoot)) return false;
        var livePath = Path.Combine(modsRoot, "0-Engine", "modules",
            "BlackboardCache2.lua");
        if (!File.Exists(livePath)) return false;
        if (staged.ContainsKey(Relative))
        {
            skipped.Add(new { type = "SEMANTIC_RULE",
                Owner = "0-Engine", RuleId = "0engine-psm-definitions-cache-stage-a",
                reason = "Another transform already staged this 0-Engine module; refusing composition." });
            return false;
        }
        var original = File.ReadAllBytes(livePath);
        if (!TryTransform(original, out var transformed, out var reason))
        {
            if (reason != "ALREADY_SATISFIED")
                skipped.Add(new { type = "SEMANTIC_RULE",
                    Owner = "0-Engine", RuleId = "0engine-psm-definitions-cache-stage-a",
                    reason });
            return false;
        }
        staged[Relative] = transformed;
        liveSourceHashes[Relative] =
            Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
        appliedTransformsByFile[Relative] = 1;
        transformManifest.Add(new
        {
            type = "SEMANTIC_RULE",
            RuleId = "0engine-psm-definitions-cache-stage-a",
            Owner = "0-Engine",
            Handler = "BlackboardPsmDefinitionCacheSource",
            PolicyClass = "SOURCE_VERIFIED_STATIC_DEFINITION_SNAPSHOT",
            patchStyle = "INIT_ONCE_12_PSM_DEFS_ONLY",
            evidence = "12 distinct captured positive Lua/C __index callsites",
            sourceProof = true,
            sourceSha256 = liveSourceHashes[Relative],
            expectedMarker = Marker,
            nativeMethodCaching = false,
            callbackOrCadenceChanged = false,
            gameplayVerification = "PENDING"
        });
        return true;
    }

    private static int Count(string haystack, string needle) =>
        (haystack.Length - haystack.Replace(needle, "",
            StringComparison.Ordinal).Length) / needle.Length;
}
