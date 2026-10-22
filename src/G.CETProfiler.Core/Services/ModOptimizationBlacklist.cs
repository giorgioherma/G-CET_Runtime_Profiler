using System.Text.Json;
using System.Text.Json.Nodes;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Version-independent Marmur Bank exclusion: its native package validates
/// files against its own release, including files modified by generic passes.
/// No Resolver output may change Marmur Bank or compile its native location.
/// This does not create a Lua SHA or version gate for other mods.
/// </summary>
public static class ModOptimizationBlacklist
{
    public const string Warning =
        "Marmur Bank detected: ALL versions are incompatible with G-CET Resolver " +
        "optimizations and are blacklisted. No generic, semantic, native, " +
        "provider repair or Lua worker changes will be generated for Marmur Bank. " +
        "Existing installed edits are not reverted; restore original mod files separately.";

    public static bool IsBlacklistedOwner(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) return false;
        var simple = new string(owner.Where(char.IsLetterOrDigit).ToArray())
            .ToLowerInvariant();
        const string name = "marmurbank";
        if (!simple.StartsWith(name, StringComparison.Ordinal)) return false;
        var suffix = simple[name.Length..];
        if (suffix.StartsWith("overhaul", StringComparison.Ordinal))
            suffix = suffix["overhaul".Length..];
        return suffix.Length == 0 || char.IsDigit(suffix[0]) ||
            (suffix.Length > 1 && suffix[0] == 'v' && char.IsDigit(suffix[1]));
    }

    public static bool IsBlacklistedPath(string? relativeFile)
    {
        if (string.IsNullOrWhiteSpace(relativeFile)) return false;
        var normalized = relativeFile.Replace('\\', '/').TrimStart('/');
        var separator = normalized.IndexOf('/');
        return separator > 0 && IsBlacklistedOwner(normalized[..separator]);
    }

    public static string[] FindInstalledOwners(string modsRoot)
    {
        if (!Directory.Exists(modsRoot)) return [];
        return Directory.EnumerateDirectories(modsRoot)
            .Select(Path.GetFileName)
            .Where(IsBlacklistedOwner)
            .Select(x => x!)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatWarning(IEnumerable<string> owners)
    {
        var installed = owners.ToArray();
        return installed.Length == 0 ? "" :
            "\r\n\r\nWARNING — BLACKLISTED MOD: " +
            string.Join(", ", installed) + "\r\n" + Warning + "\r\n";
    }

    // Retain the captured measured workload for diagnostics, but disallow
    // optimizations regardless of any previous/current profiler capture.
    internal static void ApplyToResolver(JsonNode root, string[] installed)
    {
        root["modOptimizationBlacklist"] = JsonSerializer.SerializeToNode(new
        {
            rule = "MARMUR_BANK_ALL_RELEASES",
            installedOwners = installed,
            warning = installed.Length > 0 ? Warning : null,
            appliedToSourceCandidates = true,
            affectsMeasurements = false
        });

        if (root["callbackFamilies"] is JsonArray families)
            foreach (var family in families)
                if (family?["topConsumers"] is JsonArray consumers)
                    foreach (var item in consumers)
                    {
                        if (item is not JsonObject consumer ||
                            !IsBlacklistedOwner(item["owner"]?.ToString())) continue;
                        if (consumer["generic"] is JsonObject generic)
                        {
                            generic["Automatable"] = false;
                            generic["Status"] = "MOD_BLACKLISTED_INCOMPATIBLE";
                        }
                        if (consumer["semantic"] is JsonObject semantic)
                        {
                            semantic["GenerationEnabled"] = false;
                            semantic["generationReady"] = false;
                            semantic["Status"] = "MOD_BLACKLISTED_INCOMPATIBLE";
                        }
                    }

        if (root["semanticSourceOnlyCandidates"] is JsonArray sourceOnly)
            foreach (var item in sourceOnly)
                if (item is JsonObject row &&
                    IsBlacklistedOwner(row["owner"]?.ToString()))
                    row["generationEnabled"] = false;

        if (root["sharedProviderOpportunities"] is JsonArray providers)
            foreach (var provider in providers)
                if (provider?["callbacks"] is JsonArray callbacks)
                    foreach (var item in callbacks)
                        if (item is JsonObject row &&
                            IsBlacklistedOwner(row["owner"]?.ToString()))
                            row["substitutionEligible"] = false;
    }
}
