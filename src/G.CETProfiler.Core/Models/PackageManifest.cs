using System.Text.Json.Serialization;

namespace GCETRuntimeProfiler.Core.Models;

public sealed class PackageManifest
{
    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "";


    [JsonPropertyName("targetCET")]
    public TargetCetManifest TargetCet { get; set; } = new();

    [JsonPropertyName("zeroEngine")]
    public ZeroEngineManifest ZeroEngine { get; set; } = new();

    [JsonPropertyName("liveResultFiles")]
    public List<string> LiveResultFiles { get; set; } = [];

    [JsonPropertyName("liveResultPatterns")]
    public List<string> LiveResultPatterns { get; set; } = [];
}

public sealed class TargetCetManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("officialSha256")]
    public string OfficialSha256 { get; set; } = "";

    [JsonPropertyName("profilerSha256")]
    public string ProfilerSha256 { get; set; } = "";
}

public sealed class ZeroEngineManifest
{
    [JsonPropertyName("profilerBridgeMarker")]
    public string ProfilerBridgeMarker { get; set; } = "";

    [JsonPropertyName("profilerSchedulerSha256")]
    public string ProfilerSchedulerSha256 { get; set; } = "";

    [JsonPropertyName("profilerAdaptiveSchedulerSha256")]
    public string ProfilerAdaptiveSchedulerSha256 { get; set; } = "";
}
