using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record ResolverBuildResult(
    string ResolverPath,
    int FamilyCount,
    int RankedCallbackCount,
    int GenericResolvedCount,
    int NonFrameOnlyAutoCount,
    int FrameOnlyAutoCount,
    int MaterialRemainingCount,
    int BelowThresholdCount,
    int SemanticReadyRuleCount,
    int SharedProviderReadyCallbackCount,
    int SharedProviderReadyReadCount,
    string[] SharedProviderReadyFamilies,
    int AlreadySatisfiedCount,
    int UnresolvedCount,
    ResolverOptimizationCoverage OptimizationCoverage,
    string[] BlacklistedMods);

/// <summary>
/// Top-level G-CET resolver. Callback families are the primary unit of work.
/// Behavior-changing cadence decisions live exclusively in source-proven semantic
/// rules. The resolver reads the deployed CET stack and capture data but never mutates a mod.
/// </summary>
public static class ResolverService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static ResolverBuildResult Resolve(
        string captureRoot,
        string modsRoot,
        string? semanticLibraryPath = null)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));
        if (string.IsNullOrWhiteSpace(modsRoot))
            throw new ArgumentException("CET mods folder is empty.", nameof(modsRoot));

        captureRoot = ResolveCaptureRoot(Path.GetFullPath(captureRoot));
        modsRoot = Path.GetFullPath(modsRoot);

        var handoff = Path.Combine(captureRoot, ResultReportService.ResolverInputFileName);
        if (!Directory.Exists(modsRoot))
            throw new DirectoryNotFoundException($"CET mods folder was not found: {modsRoot}");

        semanticLibraryPath ??= Path.Combine(
            AppContext.BaseDirectory,
            "knowledge",
            "semantic-library.json");

        var result = CallbackResolverService.Build(
            handoff,
            modsRoot,
            semanticLibraryPath);

        // Extract distinct owner/class counts from the same complete rows that
        // feed the report. Persist them under an explicit unit-bearing key.
        var report = JsonSerializer.SerializeToElement(result.Document);
        var augmented = JsonNode.Parse(report.GetRawText())!;
        var blacklistedMods = ModOptimizationBlacklist.FindInstalledOwners(modsRoot);
        ModOptimizationBlacklist.ApplyToResolver(augmented, blacklistedMods);
        var coverage = OptimizationClassCountService.Analyze(
            JsonSerializer.SerializeToElement(augmented));
        augmented["optimizationClassCoverage"] = JsonSerializer.SerializeToNode(coverage);

        // A blacklisted mod is still profiled, but must not inflate the
        // displayed Ready/Generate button counts. Retain other owners' counts.
        var genericReady = result.GenericResolvedCount;
        var nonFrameReady = result.NonFrameOnlyAutoCount;
        var frameReady = result.FrameOnlyAutoCount;
        var providerReady = result.SharedProviderReadyCallbackCount;
        var providerReads = result.SharedProviderReadyReadCount;
        var providerFamilies = result.SharedProviderReadyFamilies;
        if (blacklistedMods.Length > 0)
        {
            static bool Bool(JsonElement row, string name) =>
                row.ValueKind == JsonValueKind.Object &&
                row.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.True;
            static string Str(JsonElement row, string name) =>
                row.ValueKind == JsonValueKind.Object &&
                row.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? "" : "";
            static long Id(JsonElement row, string name) =>
                row.ValueKind == JsonValueKind.Object &&
                row.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt64(out var n) ? n : 0;

            if (report.TryGetProperty("callbackFamilies", out var families) &&
                families.ValueKind == JsonValueKind.Array)
                foreach (var family in families.EnumerateArray())
                {
                    if (!family.TryGetProperty("topConsumers", out var consumers) ||
                        consumers.ValueKind != JsonValueKind.Array) continue;
                    var kind = Str(family, "resolverFamily");
                    foreach (var row in consumers.EnumerateArray())
                    {
                        if (!ModOptimizationBlacklist.IsBlacklistedOwner(Str(row, "owner")) ||
                            !row.TryGetProperty("generic", out var generic) ||
                            !Bool(generic, "Automatable")) continue;
                        genericReady = Math.Max(0, genericReady - 1);
                        if (kind is "ONUPDATE" or "ONDRAW")
                            frameReady = Math.Max(0, frameReady - 1);
                        else nonFrameReady = Math.Max(0, nonFrameReady - 1);
                    }
                }

            var blockedIds = new HashSet<long>();
            var blockedReads = 0;
            var retainedFamilies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (report.TryGetProperty("sharedProviderOpportunities", out var providers) &&
                providers.ValueKind == JsonValueKind.Array)
                foreach (var family in providers.EnumerateArray())
                {
                    if (!family.TryGetProperty("callbacks", out var callbacks) ||
                        callbacks.ValueKind != JsonValueKind.Array) continue;
                    var familyName = Str(family, "provider");
                    foreach (var row in callbacks.EnumerateArray())
                    {
                        if (!Bool(row, "substitutionEligible")) continue;
                        if (ModOptimizationBlacklist.IsBlacklistedOwner(Str(row, "owner")))
                        {
                            var id = Id(row, "registrationId");
                            if (id > 0) blockedIds.Add(id);
                            blockedReads += (int)Id(row, "sourceRecognizedOccurrences");
                        }
                        else retainedFamilies.Add(familyName);
                    }
                }
            providerReady = Math.Max(0, providerReady - blockedIds.Count);
            providerReads = Math.Max(0, providerReads - blockedReads);
            providerFamilies = providerFamilies.Where(retainedFamilies.Contains).ToArray();
        }

        var resolverPath = Path.Combine(captureRoot, ResultReportService.ResolverResolutionFileName);
        File.WriteAllText(
            resolverPath,
            augmented.ToJsonString(JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        return new ResolverBuildResult(
            resolverPath,
            result.FamilyCount,
            result.RankedCallbackCount,
            genericReady,
            nonFrameReady,
            frameReady,
            result.MaterialRemainingCount,
            result.BelowThresholdCount,
            blacklistedMods.Length > 0 ? coverage.ReadyRuleCount : result.SemanticReadyRuleCount,
            providerReady,
            providerReads,
            providerFamilies,
            result.AlreadySatisfiedCount,
            result.UnresolvedCount,
            coverage,
            blacklistedMods);
    }
    public static PassBuildResult GeneratePass(
        string captureRoot,
        string modsRoot,
        string? outputZipPath = null,
        bool hotscenesEnglishOnly = false)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));
        if (string.IsNullOrWhiteSpace(modsRoot))
            throw new ArgumentException("CET mods folder is empty.", nameof(modsRoot));

        captureRoot = ResolveCaptureRoot(Path.GetFullPath(captureRoot));
        modsRoot = Path.GetFullPath(modsRoot);

        return PassGeneratorService.Generate(captureRoot, modsRoot, outputZipPath,
            hotscenesEnglishOnly);
    }

    internal static string ResolveCaptureRoot(string selectedPath)
    {
        if (!Directory.Exists(selectedPath))
            throw new DirectoryNotFoundException($"Capture/results folder was not found: {selectedPath}");

        var direct = Path.Combine(selectedPath, ResultReportService.ResolverInputFileName);
        if (File.Exists(direct))
            return selectedPath;

        // Dummy-proof path: users naturally select the package RESULTS folder,
        // not the timestamped capture inside it. Accept that and use the newest
        // collected capture that actually contains the resolver handoff.
        var candidates = Directory
            .EnumerateDirectories(selectedPath)
            .Select(path => new
            {
                Path = path,
                Handoff = Path.Combine(path, ResultReportService.ResolverInputFileName)
            })
            .Where(x => File.Exists(x.Handoff))
            .Select(x => new
            {
                x.Path,
                LastWriteUtc = File.GetLastWriteTimeUtc(x.Handoff)
            })
            .OrderByDescending(x => x.LastWriteUtc)
            .ToList();

        if (candidates.Count > 0)
            return candidates[0].Path;

        throw new InvalidOperationException(
            $"No collected G-CET capture was found in:\n{selectedPath}\n\n" +
            $"Select either the RESULTS folder or a timestamped CET-* capture folder containing {ResultReportService.ResolverInputFileName}.");
    }

}
