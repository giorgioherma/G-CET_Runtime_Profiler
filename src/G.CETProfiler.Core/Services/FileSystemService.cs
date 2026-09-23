using System.Security.Cryptography;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

internal static class FileSystemService
{
    public static string? Sha256(string path)
    {
        if (!File.Exists(path)) return null;
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    public static string? DirectoryFingerprint(string path)
    {
        if (!Directory.Exists(path)) return null;

        var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var records = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(file => new
            {
                Relative = Path.GetRelativePath(root, file).Replace('\\', '/'),
                Hash = Sha256(file) ?? ""
            })
            .OrderBy(x => x.Relative, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Relative, StringComparer.Ordinal)
            .Select(x => x.Relative + "\t" + x.Hash);

        var material = string.Join("\n", records);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
    }

    public static void CopyDirectoryExact(string source, string destination)
    {
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source directory not found: {source}");

        if (Directory.Exists(destination))
            Directory.Delete(destination, true);

        Directory.CreateDirectory(destination);

        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    public static void CopyFileVerified(string source, string destination, string? expectedHash = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);

        var sourceHash = expectedHash ?? Sha256(source);
        var destinationHash = Sha256(destination);
        if (sourceHash is null || !string.Equals(sourceHash, destinationHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Copy verification failed for {Path.GetFileName(source)}.");
    }

    public static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
