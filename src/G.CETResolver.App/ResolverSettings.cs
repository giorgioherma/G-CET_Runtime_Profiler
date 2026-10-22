using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler.Resolver;

/// <summary>Personal pass preferences live in the Resolver package, not CET.</summary>
internal sealed class ResolverSettings
{
    public bool HotscenesEnglishOnly { get; set; }

    private static string SettingsPath =>
        Path.Combine(PackageRootLocator.Resolve(), "G-CET-Resolver.settings.json");

    internal static ResolverSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new ResolverSettings();
            return JsonSerializer.Deserialize<ResolverSettings>(
                File.ReadAllText(SettingsPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ResolverSettings();
        }
        catch { return new ResolverSettings(); }
    }

    internal void Save()
    {
        var path = SettingsPath;
        var json = JsonSerializer.Serialize(this,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, true);
    }
}
