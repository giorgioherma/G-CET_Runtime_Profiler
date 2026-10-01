using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal static class SemanticPassGeneratorService
{
    internal const double MaterialThresholdMsPerSecond = 3.0;

    internal static int Apply(
        JsonElement resolverRoot,
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        var candidates = ReadCandidates(resolverRoot)
            .GroupBy(x => x.Owner + "\n" + x.RuleId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.RuntimeMsPerSecond).First())
            .OrderByDescending(x => x.RuntimeMsPerSecond)
            .ToList();

        var applied = 0;

        foreach (var candidate in candidates)
        {
            var context = new SemanticPatchContext(
                modsRoot,
                candidate.Owner,
                candidate.RuleId,
                staged);

            try
            {
                var result = SemanticInjectors.Apply(candidate, context);
                if (!result.Applied)
                {
                    skipped.Add(new
                    {
                        type = "SEMANTIC_RULE",
                        candidate.RuleId,
                        candidate.Owner,
                        candidate.Handler,
                        candidate.PolicyClass,
                        candidate.RuntimeMsPerSecond,
                        reason = result.Reason
                    });
                    continue;
                }

                foreach (var change in context.Changes)
                {
                    var relative = change.RelativeFile.Replace('\\', '/');
                    var livePath = Path.Combine(
                        Path.GetFullPath(modsRoot),
                        relative.Replace('/', Path.DirectorySeparatorChar));

                    if (!liveSourceHashes.ContainsKey(relative))
                    {
                        liveSourceHashes[relative] = File.Exists(livePath)
                            ? Sha256(File.ReadAllBytes(livePath))
                            : null;
                    }

                    staged[relative] = change.Bytes;
                    appliedTransformsByFile[relative] =
                        appliedTransformsByFile.TryGetValue(relative, out var count)
                            ? count + 1
                            : 1;
                }

                transformManifest.Add(new
                {
                    type = "SEMANTIC_RULE",
                    candidate.RuleId,
                    candidate.Owner,
                    candidate.Handler,
                    candidate.PolicyClass,
                    runtimeMsPerSecond = Math.Round(candidate.RuntimeMsPerSecond, 6),
                    patchStyle = candidate.PatchStyle,
                    sourceProof = true,
                    files = context.Changes
                        .Select(x => x.RelativeFile.Replace('\\', '/'))
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    result.Note
                });
                applied++;
            }
            catch (Exception ex)
            {
                skipped.Add(new
                {
                    type = "SEMANTIC_RULE",
                    candidate.RuleId,
                    candidate.Owner,
                    candidate.Handler,
                    candidate.PolicyClass,
                    candidate.RuntimeMsPerSecond,
                    reason = "Semantic injection failed closed: " + ex.Message
                });
            }
        }

        return applied;
    }

    private static List<SemanticCandidate> ReadCandidates(JsonElement root)
    {
        var result = new List<SemanticCandidate>();

        if (!root.TryGetProperty("callbackFamilies", out var families) ||
            families.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var family in families.EnumerateArray())
        {
            if (!family.TryGetProperty("topConsumers", out var consumers) ||
                consumers.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var consumer in consumers.EnumerateArray())
            {
                if (!consumer.TryGetProperty("runtime", out var runtime) ||
                    runtime.ValueKind != JsonValueKind.Object)
                    continue;

                var runtimeMs = JsonDouble(runtime, "exclusiveMsPerSecond");
                if (runtimeMs < MaterialThresholdMsPerSecond)
                    continue;

                if (!consumer.TryGetProperty("semantic", out var semantic) ||
                    semantic.ValueKind != JsonValueKind.Object)
                    continue;

                if (!JsonBool(semantic, "Matched") ||
                    !JsonBool(semantic, "SourceProofSatisfied") ||
                    JsonBool(semantic, "AlreadySatisfied") ||
                    !JsonBool(semantic, "GenerationEnabled"))
                    continue;

                var ruleId = JsonString(semantic, "RuleId");
                var owner = JsonString(consumer, "owner");
                var handler = JsonString(semantic, "Handler");
                var policyClass = JsonString(semantic, "PolicyClass");
                var patchStyle = JsonString(semantic, "PatchStyle");

                if (string.IsNullOrWhiteSpace(ruleId) ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(handler))
                    continue;

                result.Add(new SemanticCandidate(
                    ruleId,
                    owner,
                    handler,
                    policyClass,
                    patchStyle,
                    runtimeMs));
            }
        }

        return result;
    }

    private static string JsonString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
            return "";
        return value.GetString() ?? "";
    }

    private static bool JsonBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return false;
        return value.ValueKind == JsonValueKind.True;
    }

    private static double JsonDouble(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return 0;
        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var number))
            return number;
        return 0;
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}

internal sealed record SemanticCandidate(
    string RuleId,
    string Owner,
    string Handler,
    string PolicyClass,
    string PatchStyle,
    double RuntimeMsPerSecond);

internal sealed record SemanticInjectionResult(
    bool Applied,
    string Reason,
    string Note)
{
    internal static SemanticInjectionResult Success(string note) =>
        new(true, "", note);

    internal static SemanticInjectionResult Skip(string reason) =>
        new(false, reason, "");
}

internal sealed class SemanticPatchContext
{
    private static readonly Regex NormalizeNonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _modsRoot;
    private readonly Dictionary<string, byte[]> _staged;
    private readonly Dictionary<string, SemanticFileChange> _changes =
        new(StringComparer.OrdinalIgnoreCase);
    private string? _ownerFolder;

    internal string Owner { get; }
    internal string RuleId { get; }

    internal IReadOnlyCollection<SemanticFileChange> Changes =>
        _changes.Values;

    internal SemanticPatchContext(
        string modsRoot,
        string owner,
        string ruleId,
        Dictionary<string, byte[]> staged)
    {
        _modsRoot = Path.GetFullPath(modsRoot);
        Owner = owner;
        RuleId = ruleId;
        _staged = staged;
    }

    internal SemanticTextFile FindFile(
        string preferredRelative,
        params string[] requiredTokens)
    {
        var ownerFolder = ResolveOwnerFolder()
            ?? throw new InvalidOperationException(
                $"Live mod folder could not be resolved for {Owner}.");

        if (!string.IsNullOrWhiteSpace(preferredRelative))
        {
            var preferred = Path.Combine(
                ownerFolder,
                preferredRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(preferred))
            {
                var file = Read(preferred);
                if (requiredTokens.All(token => Contains(file.Text, token)))
                    return file;
            }
        }

        var matches = new List<SemanticTextFile>();
        foreach (var path in Directory.EnumerateFiles(
                     ownerFolder,
                     "*.lua",
                     SearchOption.AllDirectories))
        {
            var file = Read(path);
            if (requiredTokens.All(token => Contains(file.Text, token)))
                matches.Add(file);
            if (matches.Count > 1)
                break;
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No current Lua source uniquely proves [{string.Join(", ", requiredTokens)}]."),
            _ => throw new InvalidOperationException(
                $"Current Lua source is ambiguous for [{string.Join(", ", requiredTokens)}].")
        };
    }

    internal void Write(SemanticTextFile file, string text)
    {
        if (text == file.Text)
            return;

        var marker = $"G-CET semantic:{RuleId}";
        if (!text.Contains(marker, StringComparison.OrdinalIgnoreCase))
        {
            text = $"-- {marker}\n" + text;
        }

        _changes[file.RelativeFile] = new SemanticFileChange(
            file.RelativeFile,
            file.Encode(text));
    }

    internal bool OwnerAlreadyMarked()
    {
        var folder = ResolveOwnerFolder();
        if (folder is null)
            return false;

        var marker = $"G-CET semantic:{RuleId}";
        foreach (var path in Directory.EnumerateFiles(folder, "*.lua", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/');
            string text;
            if (_changes.TryGetValue(relative, out var changed))
                text = Encoding.UTF8.GetString(changed.Bytes);
            else if (_staged.TryGetValue(relative, out var stagedBytes))
                text = Encoding.UTF8.GetString(stagedBytes);
            else
                text = File.ReadAllText(path);

            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private SemanticTextFile Read(string path)
    {
        var relative = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/');

        byte[] bytes;
        if (_changes.TryGetValue(relative, out var changed))
            bytes = changed.Bytes;
        else if (_staged.TryGetValue(relative, out var staged))
            bytes = staged;
        else
            bytes = File.ReadAllBytes(path);

        return SemanticTextFile.From(relative, bytes);
    }

    private string? ResolveOwnerFolder()
    {
        if (_ownerFolder is not null)
            return _ownerFolder;

        var exact = Directory.EnumerateDirectories(_modsRoot)
            .FirstOrDefault(path =>
                Path.GetFileName(path).Equals(
                    Owner,
                    StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return _ownerFolder = exact;

        var normalized = Normalize(Owner);
        var matches = Directory.EnumerateDirectories(_modsRoot)
            .Where(path => Normalize(Path.GetFileName(path)) == normalized)
            .Take(2)
            .ToList();

        return _ownerFolder = matches.Count == 1 ? matches[0] : null;
    }

    private static bool Contains(string text, string token) =>
        text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string Normalize(string value) =>
        NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");
}

internal sealed record SemanticFileChange(
    string RelativeFile,
    byte[] Bytes);

internal sealed class SemanticTextFile
{
    private readonly bool _bom;
    private readonly string _newline;

    internal string RelativeFile { get; }
    internal string Text { get; }

    private SemanticTextFile(
        string relativeFile,
        string text,
        bool bom,
        string newline)
    {
        RelativeFile = relativeFile;
        Text = text;
        _bom = bom;
        _newline = newline;
    }

    internal static SemanticTextFile From(
        string relativeFile,
        byte[] bytes)
    {
        var bom =
            bytes.Length >= 3 &&
            bytes[0] == 0xEF &&
            bytes[1] == 0xBB &&
            bytes[2] == 0xBF;

        var raw = bom
            ? Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
            : Encoding.UTF8.GetString(bytes);

        var newline = raw.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var text = raw
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        return new SemanticTextFile(
            relativeFile,
            text,
            bom,
            newline);
    }

    internal byte[] Encode(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var output = _newline == "\r\n"
            ? normalized.Replace("\n", "\r\n", StringComparison.Ordinal)
            : normalized;

        var payload = Encoding.UTF8.GetBytes(output);
        if (!_bom)
            return payload;

        var withBom = new byte[payload.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Buffer.BlockCopy(payload, 0, withBom, 3, payload.Length);
        return withBom;
    }
}
