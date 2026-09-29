using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record ResolverBuildResult(
    string ResolverPath,
    string? CadenceRuntimePath,
    string? CadenceFinalPath,
    int FamilyCount,
    int RankedCallbackCount,
    int GenericResolvedCount,
    int RegistryHintCount,
    int UnresolvedCount);

/// <summary>
/// Top-level G-CET resolver. Callback families are the primary unit of work.
/// Cadence is one optional subset. The resolver reads the deployed CET stack
/// and capture data but never mutates a mod.
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
        string? exceptionRegistryPath = null)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));
        if (string.IsNullOrWhiteSpace(modsRoot))
            throw new ArgumentException("CET mods folder is empty.", nameof(modsRoot));

        captureRoot = Path.GetFullPath(captureRoot);
        modsRoot = Path.GetFullPath(modsRoot);

        var handoff = Path.Combine(captureRoot, ResultReportService.ResolverInputFileName);
        if (!File.Exists(handoff))
            throw new InvalidOperationException($"Measurement-only resolver handoff is missing: {handoff}");
        if (!Directory.Exists(modsRoot))
            throw new DirectoryNotFoundException($"CET mods folder was not found: {modsRoot}");

        CadenceResolverBuildResult? cadence = null;
        var onUpdateTimeline = Directory
            .EnumerateFiles(captureRoot, "CET_Runtime_Profile_OnUpdateTimeline.csv", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(onUpdateTimeline))
        {
            try
            {
                cadence = CadenceResolverService.Resolve(captureRoot, modsRoot);
            }
            catch
            {
                // Cadence is a subset. Failure or absence of its exact timeline
                // must not prevent callback-family resolution.
                cadence = null;
            }
        }

        exceptionRegistryPath ??= Path.Combine(
            AppContext.BaseDirectory,
            "knowledge",
            "high-impact-exceptions.json");

        var result = CallbackResolverService.Build(
            handoff,
            modsRoot,
            cadence?.FinalResolutionPath,
            exceptionRegistryPath);

        var resolverPath = Path.Combine(captureRoot, ResultReportService.ResolverResolutionFileName);
        File.WriteAllText(
            resolverPath,
            JsonSerializer.Serialize(result.Document, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        return new ResolverBuildResult(
            resolverPath,
            cadence?.RuntimeResolutionPath,
            cadence?.FinalResolutionPath,
            result.FamilyCount,
            result.RankedCallbackCount,
            result.GenericResolvedCount,
            result.RegistryHintCount,
            result.UnresolvedCount);
    }
}
