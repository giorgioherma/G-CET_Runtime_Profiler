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
    int NonFrameOnlyAutoCount,
    int FrameOnlyAutoCount,
    int MaterialRemainingCount,
    int BelowThresholdCount,
    int SemanticReadyRuleCount,
    int SharedProviderReadyCallbackCount,
    int SharedProviderReadyReadCount,
    int AlreadySatisfiedCount,
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

        semanticLibraryPath ??= Path.Combine(
            AppContext.BaseDirectory,
            "knowledge",
            "semantic-library.json");

        var result = CallbackResolverService.Build(
            handoff,
            modsRoot,
            cadence?.FinalResolutionPath,
            semanticLibraryPath);

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
            result.NonFrameOnlyAutoCount,
            result.FrameOnlyAutoCount,
            result.MaterialRemainingCount,
            result.BelowThresholdCount,
            result.SemanticReadyRuleCount,
            result.SharedProviderReadyCallbackCount,
            result.SharedProviderReadyReadCount,
            result.AlreadySatisfiedCount,
            result.UnresolvedCount);
    }
    public static PassBuildResult GeneratePass(
        string captureRoot,
        string modsRoot,
        string? outputZipPath = null)
    {
        if (string.IsNullOrWhiteSpace(captureRoot))
            throw new ArgumentException("Capture folder is empty.", nameof(captureRoot));
        if (string.IsNullOrWhiteSpace(modsRoot))
            throw new ArgumentException("CET mods folder is empty.", nameof(modsRoot));

        captureRoot = ResolveCaptureRoot(Path.GetFullPath(captureRoot));
        modsRoot = Path.GetFullPath(modsRoot);

        return PassGeneratorService.Generate(captureRoot, modsRoot, outputZipPath);
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
