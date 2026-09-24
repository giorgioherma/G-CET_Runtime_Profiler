namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Resolves the portable package root independently from the managed app directory.
/// Development builds may run directly from their output directory; public builds run
/// the managed WinForms app from app\ beneath the package root.
/// </summary>
public static class PackageRootLocator
{
    public const string EnvironmentVariable = "G_CET_PROFILER_PACKAGE_ROOT";

    public static string Resolve(string? startingDirectory = null)
    {
        var explicitRoot = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (LooksLikePackageRoot(explicitRoot))
            return Path.GetFullPath(explicitRoot!);

        var start = Path.GetFullPath(startingDirectory ?? AppContext.BaseDirectory);
        if (LooksLikePackageRoot(start))
            return start;

        var parent = Directory.GetParent(start)?.FullName;
        if (LooksLikePackageRoot(parent))
            return Path.GetFullPath(parent!);

        // Keep the historical failure mode useful: callers that require a package
        // root will report the missing MANIFEST.json relative to where the app ran.
        return start;
    }

    private static bool LooksLikePackageRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(Path.Combine(path, "MANIFEST.json")) &&
        Directory.Exists(Path.Combine(path, "payload"));
}
