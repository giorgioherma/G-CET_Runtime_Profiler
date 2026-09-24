using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

internal sealed class AppSettings
{
    public string GameRoot { get; set; } = "";
    public bool PairFrameTimeProfiler { get; set; } = true;
    public string ExternalProfilerExe { get; set; } = "";
    public string ExternalResultsDirectory { get; set; } = "";
    public string LastCollectedExternalSource { get; set; } = "";
    public string LastCollectedExternalFingerprint { get; set; } = "";

    private static string SettingsPath =>
        Path.Combine(PackageRootLocator.Resolve(), "G-CET-Runtime-Profiler.settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(SettingsPath),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, json + Environment.NewLine);
            File.Move(temp, SettingsPath, true);
        }
        catch
        {
            // Settings are convenience-only. Never block profiler operation because
            // the extracted package folder is read-only or unavailable.
        }
    }
}
