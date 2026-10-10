using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Independent, capture-free 0-Engine upgrade. Reuses the same pinned,
/// source-hash-checked runtime used by optimization passes but stages *only*
/// changed framework files. No unrelated CET mod is read or rewritten.
/// </summary>
public static class FrameworkUpdateService
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    public static PassBuildResult Generate(string modsRoot, string? outputZipPath = null)
    {
        if (string.IsNullOrWhiteSpace(modsRoot))
            throw new ArgumentException("Specify a live CET mods folder.", nameof(modsRoot));
        modsRoot = Path.GetFullPath(modsRoot);
        if (!Directory.Exists(modsRoot))
            throw new DirectoryNotFoundException($"CET mods directory not found: {modsRoot}");

        // Recognized fixed installations and source-proven custom hosts both
        // go through the existing compatibility verifier. Never guess a host.
        var prepared = FixedZeroEngineRuntime.Prepare(modsRoot);
        var changes = new List<(string Path, byte[] Data, string? OldSha, string NewSha)>();
        foreach (var candidate in prepared.Files.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            var relative = candidate.Key.Replace('\\', '/');
            if (!relative.StartsWith("0-Engine/", StringComparison.OrdinalIgnoreCase) ||
                relative.Split('/').Any(part => part == ".." || part == "."))
                throw new InvalidOperationException("Unsafe framework file path: " + relative);
            var currentPath = Path.GetFullPath(Path.Combine(
                modsRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var allowedRoot = Path.GetFullPath(Path.Combine(modsRoot, "0-Engine")) +
                Path.DirectorySeparatorChar;
            if (!currentPath.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Framework file escapes 0-Engine: " + relative);

            var existing = File.Exists(currentPath) ? File.ReadAllBytes(currentPath) : null;
            var intendedSha = Hash(candidate.Value);
            var oldSha = existing is null ? null : Hash(existing);
            if (oldSha is not null && oldSha.Equals(intendedSha, StringComparison.OrdinalIgnoreCase))
                continue;

            changes.Add((relative, candidate.Value, oldSha, intendedSha));
        }

        if (changes.Count == 0)
            throw new InvalidOperationException(
                "0-Engine is already up to date. No framework ZIP was generated.");

        var at = DateTime.UtcNow;
        outputZipPath ??= Path.Combine(
            modsRoot, "..", "..", "..", "..", "..", "G-CET_0ENGINE_UPDATE_" +
            at.ToString("yyyyMMdd-HHmmss") + ".zip");
        outputZipPath = Path.GetFullPath(outputZipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
        var manifestPath = Path.ChangeExtension(outputZipPath, ".json");

        // The JSON is intentionally outside the game overlay; existing
        // optimization-pass workflow treats manifests as review/backup metadata.
        var manifest = new
        {
            schemaVersion = "1.0",
            kind = "ZERO_ENGINE_ONLY",
            createdUtc = at.ToString("O"),
            installedState = prepared.LiveState,
            installedInitSha256 = prepared.LiveInitSha256,
            intendedInitSha256 = prepared.FixedInitSha256,
            intendedFrameworkVersion = prepared.FixedVersion,
            policy = "FRAMEWORK_ONLY_NO_CAPTURE_NO_OTHER_MOD_EDITS",
            changes = changes.Select(x => new
            {
                path = x.Path,
                sourceSha256 = x.OldSha,
                generatedSha256 = x.NewSha,
                frameworkInfrastructure = true
            }).ToArray()
        };

        // Avoid silently replacing a prior result with the same filename.
        if (File.Exists(outputZipPath) || File.Exists(manifestPath))
            throw new IOException("Framework update output already exists: " + outputZipPath);

        using (var zip = ZipFile.Open(outputZipPath, ZipArchiveMode.Create))
        {
            foreach (var changed in changes)
            {
                var entry = zip.CreateEntry(
                    "bin/x64/plugins/cyber_engine_tweaks/mods/" + changed.Path,
                    CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(changed.Data, 0, changed.Data.Length);
            }
        }
        File.WriteAllText(manifestPath,
            JsonSerializer.Serialize(manifest, ManifestOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        return new PassBuildResult(outputZipPath, manifestPath, changes.Count, 0, 0);
    }

    private static string Hash(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
}
