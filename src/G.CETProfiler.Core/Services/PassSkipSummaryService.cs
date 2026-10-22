using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Non-applied work is not automatically a failure: completed source stages
/// and Lua 200-local deferrals are independently actionable outcomes.
/// Keep the original manifest "skipped" array for older consumers.
/// </summary>
public static class PassSkipSummaryService
{
    public const string AlreadyImplemented = "ALREADY_IMPLEMENTED";
    public const string LuaLocalLimit = "LUA_LOCAL_LIMIT";
    public const string GenerationFailure = "GENERATION_FAILURE";
    public const string Blacklisted = "MOD_BLACKLIST";

    public sealed record Diagnostic(
        string Category, string Type, string? Owner, string? RuleId,
        string? File, string Reason);

    public sealed record Summary(
        int AlreadyImplementedCount, int LuaLocalLimitCount,
        int GenerationFailureCount, int BlacklistedCount, Diagnostic[] Entries)
    {
        public int TotalNotApplied => Entries.Length;

        public string FormatForUi()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("GENERATION OUTCOMES:");
            sb.AppendLine($"Already implemented (verified, no duplicate generated): {AlreadyImplementedCount}");
            sb.AppendLine($"Deferred — Lua 200-local capacity: {LuaLocalLimitCount}");
            sb.AppendLine($"Generation failures — examine source/design compatibility: {GenerationFailureCount}");
            sb.AppendLine($"Blacklisted incompatible mods (all versions): {BlacklistedCount}");
            sb.AppendLine($"Total not reapplied: {TotalNotApplied}");
            foreach (var group in new[] { AlreadyImplemented, LuaLocalLimit, GenerationFailure, Blacklisted })
            {
                var entries = Entries.Where(row => row.Category == group).ToArray();
                if (entries.Length == 0) continue;
                var title = group switch
                {
                    AlreadyImplemented => "ALREADY IMPLEMENTED:",
                    LuaLocalLimit => "LUA LOCAL LIMIT — DEFERRED:",
                    GenerationFailure => "GENERATION FAILURES — NEEDS REVIEW:",
                    _ => "BLACKLISTED — NO FILES MODIFIED:"
                };
                sb.AppendLine().AppendLine(title);
                foreach (var entry in entries)
                {
                    var identity = !string.IsNullOrWhiteSpace(entry.File)
                        ? entry.File
                        : string.Join("/", new[] { entry.Owner, entry.RuleId }
                            .Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (string.IsNullOrWhiteSpace(identity)) identity = entry.Type;
                    sb.Append("  ").Append(identity).Append(" — ").AppendLine(entry.Reason);
                }
            }
            return sb.ToString().TrimEnd();
        }
    }

    public static Summary FromManifestFile(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return FromManifest(doc.RootElement);
    }

    public static Summary FromManifest(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("skipped", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            return Build(Array.Empty<JsonElement>());
        return Build(rows.EnumerateArray());
    }

    internal static Summary FromSkipped(IEnumerable<object> rows) =>
        Build(rows.Select(item => JsonSerializer.SerializeToElement(item)));

    private static Summary Build(IEnumerable<JsonElement> rows)
    {
        var entries = rows.Select(row =>
        {
            static string? Field(JsonElement element, string key)
            {
                return element.ValueKind == JsonValueKind.Object &&
                       element.TryGetProperty(key, out var item) &&
                       item.ValueKind == JsonValueKind.String
                    ? item.GetString() : null;
            }

            var reason = Field(row, "reason") ?? "";
            var type = Field(row, "type") ?? "UNKNOWN";
            var category = Classify(type, reason, Field(row, "category"));
            return new Diagnostic(category, type,
                Field(row, "Owner") ?? Field(row, "owner"),
                Field(row, "RuleId") ?? Field(row, "ruleId"),
                Field(row, "file"), reason);
        }).ToArray();

        return new Summary(
            entries.Count(x => x.Category == AlreadyImplemented),
            entries.Count(x => x.Category == LuaLocalLimit),
            entries.Count(x => x.Category == GenerationFailure),
            entries.Count(x => x.Category == Blacklisted),
            entries);
    }

    private static string Classify(string type, string reason, string? explicitCategory)
    {
        // Forward-compatible: emitters can supply explicit outcome categories.
        if (explicitCategory is AlreadyImplemented or LuaLocalLimit or GenerationFailure or Blacklisted)
            return explicitCategory;

        if (type.Equals("LUA_LOCAL_LIMIT", StringComparison.OrdinalIgnoreCase) ||
            reason.StartsWith("LUA_LOCAL_LIMIT", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("Lua's hard per-function limit", StringComparison.OrdinalIgnoreCase))
            return LuaLocalLimit;

        // A thrown injector exception is NEVER an "already complete" result,
        // even when its explanation mentions a prior installation.
        if (reason.Contains("failed closed:", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("refused:", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("incomplete", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("partial", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("conflict", StringComparison.OrdinalIgnoreCase))
            return GenerationFailure;

        if (Regex.IsMatch(reason,
                @"\balready\s+(?:complete|satisfied|installed|present|applied|implemented|optimized|hold|holds)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return AlreadyImplemented;

        return GenerationFailure;
    }
}
