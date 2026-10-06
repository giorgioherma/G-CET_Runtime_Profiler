using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record SemanticRuleMatch(
    bool Matched,
    bool SourceProofSatisfied,
    bool AlreadySatisfied,
    bool PartialState,
    int MarkerFileCount,
    int ExpectedMarkerFileCount,
    string RuleId,
    string PolicyClass,
    string Handler,
    string PatchStyle,
    bool GenerationEnabled,
    bool ShipReferenceOverride,
    string[] MatchedAnchors,
    string[] MissingAnchors,
    object? Graph)
{
    internal static SemanticRuleMatch None { get; } = new(
        false, false, false, false, 0, 0, "", "", "", "", false, false,
        Array.Empty<string>(), Array.Empty<string>(), null);
}

internal sealed class SemanticLibraryService
{
    private static readonly Regex NormalizeNonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _modsRoot;
    private readonly List<SemanticRule> _entries;
    private readonly Dictionary<string, ModSourceGraph?> _graphs =
        new(StringComparer.OrdinalIgnoreCase);

    internal bool Loaded { get; }
    internal int EntryCount => _entries.Count;

    private SemanticLibraryService(
        string modsRoot,
        bool loaded,
        List<SemanticRule> entries)
    {
        _modsRoot = Path.GetFullPath(modsRoot);
        Loaded = loaded;
        _entries = entries;
    }

    internal static SemanticLibraryService Load(
        string? path,
        string modsRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException(
                "Semantic production library path is empty.");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                "Semantic production library is missing. Resolver AUTO will not silently continue without it.",
                path);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var entries = new List<SemanticRule>();

            if (doc.RootElement.TryGetProperty("entries", out var rows) &&
                rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var id = JsonString(row, "id");
                    var identityHints = JsonStringArray(row, "identityHints");
                    if (string.IsNullOrWhiteSpace(id) || identityHints.Length == 0)
                        continue;

                    var callbacks = new List<CallbackSelector>();
                    if (row.TryGetProperty("callbacks", out var callbackRows) &&
                        callbackRows.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var callback in callbackRows.EnumerateArray())
                        {
                            callbacks.Add(new CallbackSelector(
                                JsonString(callback, "kind"),
                                JsonString(callback, "target"),
                                JsonString(callback, "role")));
                        }
                    }

                    var proof = new SourceProof();
                    if (row.TryGetProperty("sourceProof", out var proofRow) &&
                        proofRow.ValueKind == JsonValueKind.Object)
                    {
                        proof = new SourceProof
                        {
                            OwnerAll = JsonStringArray(proofRow, "ownerAll"),
                            OwnerAnyGroups = JsonStringGroups(proofRow, "ownerAnyGroups"),
                            AlreadySatisfiedMarker = JsonString(
                                proofRow,
                                "alreadySatisfiedMarker"),
                            ExpectedMarkerFileCount = (int)Math.Max(
                                1,
                                JsonLong(proofRow, "expectedMarkerFileCount")),
                            AllowIdentityFallback = JsonBool(
                                proofRow,
                                "allowIdentityFallback")
                        };
                    }

                    var handler = "";
                    if (row.TryGetProperty("behavior", out var behavior) &&
                        behavior.ValueKind == JsonValueKind.Object)
                    {
                        handler = JsonString(behavior, "handler");
                    }

                    var generation = new GenerationPolicy();
                    if (row.TryGetProperty("generation", out var generationRow) &&
                        generationRow.ValueKind == JsonValueKind.Object)
                    {
                        generation = new GenerationPolicy
                        {
                            Enabled = JsonBool(generationRow, "enabled"),
                            PatchStyle = JsonString(generationRow, "patchStyle"),
                            ShipReferenceOverride = JsonBool(
                                generationRow,
                                "shipReferenceOverride")
                        };
                    }

                    entries.Add(new SemanticRule
                    {
                        Id = id,
                        IdentityHints = identityHints,
                        PolicyClass = JsonString(row, "policyClass"),
                        Callbacks = callbacks.ToArray(),
                        Proof = proof,
                        Handler = handler,
                        Generation = generation
                    });
                }
            }

            if (entries.Count == 0)
                throw new InvalidOperationException(
                    "Semantic production library contains no usable rules.");

            return new SemanticLibraryService(modsRoot, true, entries);
        }
        catch (Exception ex) when (ex is not FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"Semantic production library could not be loaded: {path}. {ex.Message}",
                ex);
        }
    }

    internal SemanticRuleMatch Match(
        string owner,
        string kind,
        string target)
    {
        var callbackCandidates = _entries
            .Where(entry =>
                entry.Callbacks.Any(selector =>
                    WildcardEquals(selector.Kind, kind) &&
                    WildcardEquals(selector.Target, target)))
            .ToList();

        var candidates = callbackCandidates
            .Where(entry =>
                entry.IdentityHints.Any(h => OwnerHintMatches(h, owner)))
            .ToList();

        var graph = GetGraph(owner);
        var sourceFingerprintFallback = false;

        // Mod names and folder names are only hints. Updated releases and forks
        // frequently append versions or rename the folder while retaining the
        // same behavior. If identity does not select a rule, admit a semantic
        // candidate only when exactly one current-source fingerprint proves it.
        if (candidates.Count == 0 && graph is not null)
        {
            candidates = callbackCandidates
                .Where(entry =>
                    MarkerPresent(entry, graph) ||
                    (entry.Proof.AllowIdentityFallback &&
                     HasStrongIdentityFingerprint(entry) &&
                     SourceProofSatisfied(entry, graph)))
                .Take(3)
                .ToList();
            sourceFingerprintFallback = candidates.Count > 0;
        }

        if (candidates.Count == 0)
            return SemanticRuleMatch.None;

        if (candidates.Count != 1)
        {
            return new SemanticRuleMatch(
                true,
                false,
                false,
                false,
                0,
                0,
                string.Join(",", candidates.Select(x => x.Id).OrderBy(x => x)),
                "AMBIGUOUS",
                "",
                "",
                false,
                false,
                Array.Empty<string>(),
                new[] { "Multiple semantic rules matched the same owner/callback; Resolver refused to guess across mod variants." },
                graph is null ? null : GraphSummary(graph, 0, 0, sourceFingerprintFallback));
        }

        var rule = candidates[0];
        if (graph is null)
        {
            return new SemanticRuleMatch(
                true,
                false,
                false,
                false,
                0,
                Math.Max(1, rule.Proof.ExpectedMarkerFileCount),
                rule.Id,
                rule.PolicyClass,
                rule.Handler,
                rule.Generation.PatchStyle,
                rule.Generation.Enabled,
                rule.Generation.ShipReferenceOverride,
                Array.Empty<string>(),
                new[] { "Live mod folder could not be resolved." },
                null);
        }

        EvaluateProof(rule, graph, out var matched, out var missing);

        var markerCount =
            string.IsNullOrWhiteSpace(rule.Proof.AlreadySatisfiedMarker)
                ? 0
                : graph.FileTexts.Count(text =>
                    Contains(text, rule.Proof.AlreadySatisfiedMarker));
        var expectedMarkerFileCount = Math.Max(1, rule.Proof.ExpectedMarkerFileCount);
        var alreadySatisfied = markerCount >= expectedMarkerFileCount;
        var partialState = markerCount > 0 && markerCount < expectedMarkerFileCount;

        return new SemanticRuleMatch(
            true,
            missing.Count == 0,
            alreadySatisfied,
            partialState,
            markerCount,
            expectedMarkerFileCount,
            rule.Id,
            rule.PolicyClass,
            rule.Handler,
            rule.Generation.PatchStyle,
            rule.Generation.Enabled,
            rule.Generation.ShipReferenceOverride,
            matched.ToArray(),
            missing.ToArray(),
            GraphSummary(
                graph,
                markerCount,
                expectedMarkerFileCount,
                sourceFingerprintFallback));
    }

    private static void EvaluateProof(
        SemanticRule rule,
        ModSourceGraph graph,
        out List<string> matched,
        out List<string> missing)
    {
        matched = new List<string>();
        missing = new List<string>();

        foreach (var anchor in rule.Proof.OwnerAll)
        {
            if (ContainsProof(graph.ProofCorpus, anchor))
                matched.Add(anchor);
            else
                missing.Add(anchor);
        }

        foreach (var group in rule.Proof.OwnerAnyGroups)
        {
            var hit = group.FirstOrDefault(anchor =>
                ContainsProof(graph.ProofCorpus, anchor));
            if (!string.IsNullOrWhiteSpace(hit))
                matched.Add("(" + string.Join(" | ", group) + ") => " + hit);
            else
                missing.Add("(" + string.Join(" | ", group) + ")");
        }
    }

    private static bool HasStrongIdentityFingerprint(
        SemanticRule rule)
    {
        // Name-independent identification is deliberately harder than normal
        // source proof. A couple of generic words (e.g. Runner + Manager) are
        // enough to describe behavior but nowhere near enough to establish mod
        // identity across a 60-100+ mod CET stack.
        var points =
            rule.Proof.OwnerAll.Length +
            rule.Proof.OwnerAnyGroups.Length;
        if (points < 4)
            return false;

        var anchors = rule.Proof.OwnerAll
            .Concat(rule.Proof.OwnerAnyGroups.SelectMany(group => group))
            .Where(anchor => !string.IsNullOrWhiteSpace(anchor))
            .ToArray();

        // Require at least one source-shaped anchor as well as breadth. This
        // keeps four generic English identifiers from becoming an identity.
        return anchors.Any(anchor =>
            anchor.IndexOfAny(new[] { '.', ':', '(', ')', '=', '[', ']' }) >= 0);
    }

    private static bool SourceProofSatisfied(
        SemanticRule rule,
        ModSourceGraph graph)
    {
        EvaluateProof(rule, graph, out _, out var missing);
        return missing.Count == 0;
    }

    private static bool MarkerPresent(
        SemanticRule rule,
        ModSourceGraph graph) =>
        !string.IsNullOrWhiteSpace(rule.Proof.AlreadySatisfiedMarker) &&
        graph.FileTexts.Any(text =>
            Contains(text, rule.Proof.AlreadySatisfiedMarker));

    private static object GraphSummary(
        ModSourceGraph graph,
        int markerCount,
        int expectedMarkerFileCount,
        bool sourceFingerprintFallback) => new
    {
        ownerFolder = Path.GetFileName(graph.OwnerFolder),
        luaFileCount = graph.Files.Length,
        moduleEdgeCount = graph.ModuleEdges.Length,
        callbackRegistrationCount = graph.CallbackRegistrations.Length,
        markerFileCount = markerCount,
        expectedMarkerFileCount,
        identityMode = sourceFingerprintFallback
            ? "SOURCE_FINGERPRINT"
            : "NAME_HINT_PLUS_SOURCE_PROOF",
        graph.ModuleEdges,
        graph.CallbackRegistrations
    };

    private ModSourceGraph? GetGraph(string owner)
    {
        if (_graphs.TryGetValue(owner, out var cached))
            return cached;

        var folder = ResolveOwnerFolder(owner);
        if (folder is null)
        {
            _graphs[owner] = null;
            return null;
        }

        var files = new List<string>();
        var fileTexts = new List<string>();
        var moduleEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var callbackRegistrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var corpus = new StringBuilder();
        var proofCorpus = new StringBuilder();

        try
        {
            foreach (var file in Directory
                .EnumerateFiles(folder, "*.lua", SearchOption.AllDirectories)
                .Take(2000))
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch
                {
                    continue;
                }

                files.Add(Path.GetRelativePath(folder, file).Replace('\\', '/'));
                fileTexts.Add(text);
                corpus.AppendLine(text);
                proofCorpus.AppendLine(NormalizeProof(text));

                foreach (Match match in Regex.Matches(
                    text,
                    @"\b(?:require|needModule)\s*\(\s*['""](?<path>[^'""]+)['""]\s*\)",
                    RegexOptions.CultureInvariant))
                {
                    moduleEdges.Add(match.Groups["path"].Value);
                }

                foreach (Match match in Regex.Matches(
                    text,
                    @"\b(?<kind>registerForEvent|registerRuntimeEvent|Observe|ObserveAfter|Override)\s*\(\s*['""](?<a>[^'""]+)['""](?:\s*,\s*['""](?<b>[^'""]+)['""])?",
                    RegexOptions.CultureInvariant))
                {
                    var kindName = match.Groups["kind"].Value;
                    var a = match.Groups["a"].Value;
                    var b = match.Groups["b"].Value;
                    callbackRegistrations.Add(
                        string.IsNullOrWhiteSpace(b)
                            ? $"{kindName}:{a}"
                            : $"{kindName}:{a}::{b}");
                }

                if (corpus.Length >= 24_000_000)
                    break;
            }
        }
        catch
        {
            _graphs[owner] = null;
            return null;
        }

        var graph = new ModSourceGraph(
            folder,
            files.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            moduleEdges.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            callbackRegistrations.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            fileTexts.ToArray(),
            corpus.ToString(),
            proofCorpus.ToString());

        _graphs[owner] = graph;
        return graph;
    }

    private string? ResolveOwnerFolder(string owner)
    {
        try
        {
            var exact = Directory.EnumerateDirectories(_modsRoot)
                .FirstOrDefault(path =>
                    Path.GetFileName(path).Equals(
                        owner,
                        StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;

            var normalized = Normalize(owner);
            var matches = Directory.EnumerateDirectories(_modsRoot)
                .Where(path => Normalize(Path.GetFileName(path)) == normalized)
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool WildcardEquals(string expected, string actual) =>
        string.IsNullOrWhiteSpace(expected) ||
        expected == "*" ||
        expected.Equals(actual, StringComparison.OrdinalIgnoreCase);

    private static bool OwnerHintMatches(string hint, string owner)
    {
        var normalizedHint = Normalize(hint);
        var normalizedOwner = Normalize(owner);
        if (normalizedHint == normalizedOwner)
            return true;

        // Accept only a version-like suffix after the known identity. This
        // handles folders such as EasyTrainer-v2.4 or advanced_settings_1.9
        // without turning a short hint into a broad prefix match.
        if (!normalizedOwner.StartsWith(normalizedHint, StringComparison.Ordinal) ||
            normalizedOwner.Length <= normalizedHint.Length)
            return false;

        var suffix = normalizedOwner[normalizedHint.Length..];
        if (suffix.StartsWith("v", StringComparison.Ordinal))
            suffix = suffix[1..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }

    private static bool Contains(string text, string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool ContainsProof(string normalizedCorpus, string anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
            return false;
        var normalizedAnchor = NormalizeProof(anchor);
        return normalizedAnchor.Length > 0 &&
            normalizedCorpus.Contains(
                normalizedAnchor,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProof(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == '\'' || ch == '"')
                continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    private static string Normalize(string value) =>
        NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");

    private static string JsonString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static long JsonLong(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt64(out var number))
            return 0;
        return number;
    }

    private static bool JsonBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind == JsonValueKind.True;
    }

    private static string[] JsonStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString() ?? "")
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static string[][] JsonStringGroups(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
            return Array.Empty<string[]>();

        return array.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.Array)
            .Select(x => x.EnumerateArray()
                .Where(y => y.ValueKind == JsonValueKind.String)
                .Select(y => y.GetString() ?? "")
                .Where(y => !string.IsNullOrWhiteSpace(y))
                .ToArray())
            .Where(x => x.Length > 0)
            .ToArray();
    }

    private sealed class SemanticRule
    {
        internal string Id { get; init; } = "";
        internal string[] IdentityHints { get; init; } = Array.Empty<string>();
        internal string PolicyClass { get; init; } = "";
        internal CallbackSelector[] Callbacks { get; init; } = Array.Empty<CallbackSelector>();
        internal SourceProof Proof { get; init; } = new();
        internal string Handler { get; init; } = "";
        internal GenerationPolicy Generation { get; init; } = new();
    }

    private sealed record CallbackSelector(
        string Kind,
        string Target,
        string Role);

    private sealed class SourceProof
    {
        internal string[] OwnerAll { get; init; } = Array.Empty<string>();
        internal string[][] OwnerAnyGroups { get; init; } = Array.Empty<string[]>();
        internal string AlreadySatisfiedMarker { get; init; } = "";
        internal int ExpectedMarkerFileCount { get; init; } = 1;
        internal bool AllowIdentityFallback { get; init; }
    }

    private sealed class GenerationPolicy
    {
        internal bool Enabled { get; init; }
        internal string PatchStyle { get; init; } = "";
        internal bool ShipReferenceOverride { get; init; }
    }

    private sealed record ModSourceGraph(
        string OwnerFolder,
        string[] Files,
        string[] ModuleEdges,
        string[] CallbackRegistrations,
        string[] FileTexts,
        string Corpus,
        string ProofCorpus);
}
