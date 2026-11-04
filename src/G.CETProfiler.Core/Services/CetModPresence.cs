namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// CET source ownership. Residual Vortex backups and auxiliary Lua files do
/// not constitute an installed mod without its top-level init.lua.
/// </summary>
internal static class CetModPresence
{
    internal static bool HasEntrypoint(string ownerFolder) =>
        Directory.Exists(ownerFolder) &&
        File.Exists(Path.Combine(ownerFolder, "init.lua"));

    internal static bool HasInstalledSource(string modsRoot, string relativeFile)
    {
        var normalized = relativeFile.Replace('\\', '/');
        var slash = normalized.IndexOf('/');
        if (slash <= 0) return false;
        var owner = normalized[..slash];
        if (owner is "." or ".." || owner.Contains(':')) return false;
        var root = Path.GetFullPath(modsRoot);
        var folder = Path.GetFullPath(Path.Combine(root, owner));
        if (!folder.StartsWith(
                root.TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            return false;
        return HasEntrypoint(folder);
    }
}
