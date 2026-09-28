using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record CadenceResolverBuildResult(
    string RuntimeResolutionPath,
    string FinalResolutionPath,
    int CallbackCount);

/// <summary>
/// Explicit resolver stage. The profiler produces measurement-only capture data;
/// this service consumes that handoff plus deployed CET Lua source and writes
/// interpretation output. It never mutates a mod.
/// </summary>
public static class CadenceResolverService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static CadenceResolverBuildResult Resolve(string captureRoot, string modsRoot)
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

        var runtimePath = Path.Combine(captureRoot, ResultReportService.CadenceResolutionFileName);
        var finalPath = Path.Combine(captureRoot, ResultReportService.CadenceFinalFileName);

        File.WriteAllText(
            runtimePath,
            JsonSerializer.Serialize(ResultReportService.BuildCadenceResolution(captureRoot), JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        File.WriteAllText(
            finalPath,
            JsonSerializer.Serialize(ResultReportService.BuildSourceCadenceResolution(captureRoot, modsRoot), JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));

        var callbackCount = 0;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(finalPath));
            if (document.RootElement.TryGetProperty("quality", out var quality) &&
                quality.TryGetProperty("callbackCount", out var count) &&
                count.TryGetInt32(out var value))
                callbackCount = value;
        }
        catch
        {
        }

        return new CadenceResolverBuildResult(runtimePath, finalPath, callbackCount);
    }
}
