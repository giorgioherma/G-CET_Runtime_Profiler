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
    internal static bool TryTransformCore(byte[] source, out byte[] result, out string reason)
    {
        result = source;
        var text = Encoding.UTF8.GetString(source);
        var code = NativeReadReuseSource.MaskNonCode(text);
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        const string cache = "__gcetPsmFields";
        const string defs = "PlayerStateDefs";
        var edits = new List<(int Start, int Length, string Value)>();
        var known = new List<(int Start, int Length)>();
        bool Refuse(string message, out string why) { why = message; return false; }
        Match? Unique(string pattern)
        {
            var matches = Regex.Matches(code, pattern, RegexOptions.CultureInvariant);
            return matches.Count == 1 ? matches[0] : null;
        }
        void Own(Match m) => known.Add((m.Index, m.Length));
        // The original private definition object has only three lifecycle
        // uses: declaration, Init assignment, and Attach's board definition.
        // All further uses must be the proven key reads or our own snapshot.
        var declaration = Unique(@"(?m)^[ \t]*local[ \t]+PlayerStateDefs[ \t]*=[ \t]*nil[ \t]*(?=\r?$)");
        var init = Unique(@"(?m)^[ \t]*PlayerStateDefs[ \t]*=[ \t]*GetAllBlackboardDefs[ \t]*\([ \t]*\)[ \t]*\.[ \t]*PlayerStateMachine[ \t]*(?=\r?$)");
        var updateStart = Unique(@"(?m)^function[ \t]+BlackboardCache\.Update[ \t]*\([ \t]*player[ \t]*\)[ \t]*(?=\r?$)");
        var initStart = Unique(@"(?m)^function[ \t]+BlackboardCache\.Init[ \t]*\([ \t]*events[ \t]*\)[ \t]*(?=\r?$)");
        var attachStart = Unique(@"(?m)^function[ \t]+BlackboardCache\.Attach[ \t]*\([ \t]*player[ \t]*\)[ \t]*(?=\r?$)");
        var reset = Unique(@"(?m)^function[ \t]+BlackboardCache\.Reset[ \t]*\([ \t]*\)[ \t]*(?=\r?$)");
        var attach = Unique(@":GetLocalInstanced\s*\(\s*player:GetEntityID\s*\(\s*\)\s*,\s*PlayerStateDefs\s*\)");
        int FunctionEnd(Match m)
        {
            var e = Regex.Match(code[(m.Index+m.Length)..], @"(?m)^end[ \t]*(?=\r?$)");
            return e.Success ? m.Index+m.Length+e.Index : -1;
        }
        if (declaration is null || init is null || initStart is null || updateStart is null ||
            attachStart is null || attach is null || reset is null ||
            declaration.Index >= Math.Min(initStart.Index, updateStart.Index) ||
            init.Index <= initStart.Index || init.Index >= FunctionEnd(initStart) ||
            attach.Index <= attachStart.Index || attach.Index >= FunctionEnd(attachStart))
            return Refuse("Definition initialization, update or Attach ownership is ambiguous.", out reason);
        var updateEnd = FunctionEnd(updateStart);
        if (updateEnd < 0 || !Regex.IsMatch(code[updateStart.Index..updateEnd],
            @"\bif\s+not\s+playerBB\s+then\s+return\s+end\b"))
            return Refuse("Required live blackboard guard missing.", out reason);
        Own(declaration); Own(init); Own(attach);

        var markerCount = Count(text, Marker);
        if (markerCount > 1)
            return Refuse("Multiple generated definition-cache markers.", out reason);
        var localMatches = Regex.Matches(code, @"(?m)^[ \t]*local[ \t]+__gcetPsmFields[ \t]*=[ \t]*nil[ \t]*(?=\r?$)");
        if (localMatches.Count > 1 || (localMatches.Count == 1 &&
            localMatches[0].Index >= Math.Min(initStart.Index, updateStart.Index)))
            return Refuse("Ambiguous definition-cache declaration.", out reason);
        if (localMatches.Count == 1) Own(localMatches[0]);
        // An existing snapshot may contain only a subset of keys. Recognize
        // the exact private initializer, not a version marker or whole hash.
        var blocks = Regex.Matches(code, @"\b__gcetPsmFields[ \t]*=[ \t]*\{(?<body>[^{}]*)\}");
        if (blocks.Count > 1)
            return Refuse("Ambiguous definition-cache initializer.", out reason);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (blocks.Count == 1)
        {
            var block = blocks[0];
            if (block.Index < init.Index+init.Length || block.Index >= FunctionEnd(initStart) ||
                !string.IsNullOrWhiteSpace(code[(init.Index+init.Length)..block.Index]))
                return Refuse("Definition-cache initializer is not immediately after the original Init assignment.", out reason);
            var body = block.Groups["body"];
            var fieldPattern = new Regex(@"\G\s*(?<key>[A-Za-z_]\w*)\s*=\s*PlayerStateDefs\s*\.\s*\k<key>(?<rhsEnd>)\s*(?<comma>,|(?=\s*$))",
                RegexOptions.CultureInvariant);
            int at = 0; Match? last = null;
            while (!string.IsNullOrWhiteSpace(body.Value[at..]))
            {
                var field = fieldPattern.Match(body.Value, at);
                if (!field.Success || field.Index != at ||
                    !Reads.Any(r => r.Field == field.Groups["key"].Value) ||
                    !keys.Add(field.Groups["key"].Value))
                    return Refuse("Unknown, duplicate or changed snapshot key.", out reason);
                last = field; at = field.Index+field.Length;
            }
            Own(block);
            var missing = Reads.Where(r => !keys.Contains(r.Field)).ToArray();
            if (missing.Length > 0)
            {
                // Preserve existing entries and comments; insert only missing
                // keys before the existing closing brace. Add a separator if
                // a manually edited final field omitted its trailing comma.
                if (last is not null && last.Groups["comma"].Length == 0)
                    edits.Add((body.Index+last.Groups["rhsEnd"].Index,0,","));
                int closing = block.Index+block.Length-1;
                int lineStart = text.LastIndexOf('\n',Math.Max(0,closing-1))+1;
                bool ownLine = string.IsNullOrWhiteSpace(code[lineStart..closing]) &&
                               string.IsNullOrWhiteSpace(text[lineStart..closing]);
                var insert = string.Concat(missing.Select(r => "        "+r.Field+" = "+defs+"."+r.Field+","+nl));
                edits.Add((ownLine?lineStart:closing,0,(ownLine?"":nl)+insert+(ownLine?"":"    ")));
            }
        }
        else
        {
            var block = nl+"    "+cache+" = {"+nl+
                string.Concat(Reads.Select(r => "        "+r.Field+" = "+defs+"."+r.Field+","+nl))+"    }";
            // Insert after original assignment line (including its comment).
            var eol = text.IndexOf('\n',init.Index);
            int at = eol<0?text.Length:eol-(eol>0&&text[eol-1]=='\r'?1:0);
            edits.Add((at,0,block));
        }
        foreach (var read in Reads)
        {
            // Recognize each original OR previously cached key read. Only
            // the definition receiver changes, never the native call, result
            // local, original order, or the transition/derivation body.
            var pattern = @"(?m)^[ \t]*local[ \t]+"+Regex.Escape(read.Name)+
                @"[ \t]*=[ \t]*playerBB[ \t]*:[ \t]*"+read.Getter+
                @"[ \t]*\([ \t]*(?<owner>PlayerStateDefs|__gcetPsmFields)[ \t]*\.[ \t]*"+
                Regex.Escape(read.Field)+@"[ \t]*\)[ \t]*(?=\r?$)";
            var match=Unique(pattern);
            if (match is null || match.Index <= updateStart.Index || match.Index >= updateEnd)
                return Refuse("Exact PSM read proof failed for "+read.Name, out reason);
            Own(match);
            var owner=match.Groups["owner"];
            if (owner.Value==defs) edits.Add((owner.Index,owner.Length,cache));
        }
        // No new alias, field write, reflective access or reference escape
        // may invalidate the initialization-time definition snapshot. Refuse
        // only this cache optimization, leaving the rest of the module intact.
        var unowned = code.ToCharArray();
        foreach (var span in known)
            for (int i=span.Start;i<span.Start+span.Length;i++) unowned[i]=' ';
        if (Regex.IsMatch(new string(unowned),@"\b(?:PlayerStateDefs|__gcetPsmFields)\b"))
            return Refuse("Definition or generated snapshot has an unproven use/write.", out reason);
        if (localMatches.Count == 0)
        {
            var eol=text.IndexOf('\n',declaration.Index);
            int at=eol<0?text.Length:eol-(eol>0&&text[eol-1]=='\r'?1:0);
            edits.Add((at,0,nl+"local "+cache+" = nil"+(markerCount==0?"  "+Marker:"")));
        }
        else if(markerCount==0)
        {
            // Comment-only ownership marker; retain the manual declaration.
            var eol=text.IndexOf('\n',localMatches[0].Index);
            int at=eol<0?text.Length:eol-(eol>0&&text[eol-1]=='\r'?1:0);
            edits.Add((at,0,nl+Marker));
        }
        if (edits.Count==0){reason="ALREADY_SATISFIED";return false;}
        var output = new StringBuilder(text);
        foreach (var edit in edits.OrderByDescending(e=>e.Start))
            output.Remove(edit.Start,edit.Length).Insert(edit.Start,edit.Value);
        result=Encoding.UTF8.GetBytes(output.ToString());
        reason="APPLIED";
        return true;
    }

    internal static bool TryStage(
        string captureRoot,
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        var livePath = Path.Combine(modsRoot, "0-Engine", "modules",
            "BlackboardCache2.lua");
        if (!File.Exists(livePath)) return false;
        var original = File.ReadAllBytes(livePath);
        var current = staged.TryGetValue(Relative,out var earlierStage)
            ? earlierStage : original;
        // Always re-prove the current source: a marker alone is not completion.
        // Historical capture established the benefit. Admission for updated
        // sources is governed by the complete 12-key source/lifecycle proof,
        // not by captured line numbers or current-session activity.
        if (!TryTransform(current, out var transformed, out var reason))
        {
            if (reason != "ALREADY_SATISFIED")
                skipped.Add(new { type = "SEMANTIC_RULE",
                    Owner = "0-Engine", RuleId = "0engine-psm-definitions-cache-stage-a",
                    reason });
            return false;
        }
        staged[Relative] = transformed;
        appliedTransformsByFile[Relative] =
            appliedTransformsByFile.GetValueOrDefault(Relative)+1;
        transformManifest.Add(new
        {
            type = "SEMANTIC_RULE",
            RuleId = "0engine-psm-definitions-cache-stage-a",
            Owner = "0-Engine",
            Handler = "BlackboardPsmDefinitionCacheSource",
            PolicyClass = "SOURCE_VERIFIED_STATIC_DEFINITION_SNAPSHOT",
            patchStyle = "INIT_ONCE_12_PSM_DEFS_ONLY",
            evidence = "HISTORICALLY_MEASURED_12_PSM_DEFS; CURRENT_SOURCE_LIFECYCLE_PROVEN",
            sourceProof = true,
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
