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

        // AppContext.BaseDirectory normally ends with a directory separator. Walk
        // actual DirectoryInfo ancestors rather than assuming a single GetParent()
        // call lands on the package root.
        for (DirectoryInfo? current = new(start); current is not null; current = current.Parent)
        {
            if (LooksLikePackageRoot(current.FullName))
                return current.FullName;
        }

        // Keep the failure mode useful: callers that require a package root will
        // report the missing MANIFEST.json relative to where the app actually ran.
        return start;
    }

    private static bool LooksLikePackageRoot(string? path) =>
        !string.IsNullOrWhiteSpace(path) &&
        File.Exists(Path.Combine(path, "MANIFEST.json")) &&
        Directory.Exists(Path.Combine(path, "payload"));
}
