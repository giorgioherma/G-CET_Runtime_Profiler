using System.Text.Json.Serialization;

namespace GCETRuntimeProfiler.Core.Models;

public sealed class ProfilerStatus
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; } = true;

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; init; } = "";

    [JsonPropertyName("targetCETVersion")]
    public string TargetCetVersion { get; init; } = "";

    [JsonPropertyName("gameRoot")]
    public string GameRoot { get; init; } = "";

    [JsonPropertyName("cet")]
    public string CetState { get; init; } = "";

    [JsonPropertyName("cetHash")]
    public string? CetHash { get; init; }

    [JsonPropertyName("zeroEnginePresent")]
    public bool ZeroEnginePresent { get; init; }

    [JsonPropertyName("zeroEngineInitKind")]
    public string ZeroEngineInitKind { get; init; } = "";

    [JsonPropertyName("zeroEngineInit")]
    public string ZeroEngineInit { get; init; } = "";

    [JsonPropertyName("scheduler")]
    public string Scheduler { get; init; } = "";

    [JsonPropertyName("schedulerPresent")]
    public bool SchedulerPresent { get; init; }

    [JsonPropertyName("schedulerIntegrated")]
    public bool SchedulerIntegrated { get; init; }

    [JsonPropertyName("schedulerProfilerAware")]
    public bool SchedulerProfilerAware { get; init; }

    [JsonPropertyName("adaptiveProfilerSchedulerPresent")]
    public bool AdaptiveProfilerSchedulerPresent { get; init; }

    [JsonPropertyName("managed")]
    public bool Managed { get; init; }

    [JsonPropertyName("managedMode")]
    public string ManagedMode { get; init; } = "";

    [JsonPropertyName("controlsPresent")]
    public bool ControlsPresent { get; init; }

    [JsonPropertyName("f11Binding")]
    public bool F11Binding { get; init; }

    [JsonPropertyName("captureTitlePresent")]
    public bool CaptureTitlePresent { get; init; }

    [JsonPropertyName("captureTitle")]
    public string CaptureTitle { get; init; } = "";

    [JsonPropertyName("liveResultCount")]
    public int LiveResultCount { get; init; }

    [JsonPropertyName("resultsRoot")]
    public string ResultsRoot { get; init; } = "";

    [JsonPropertyName("state")]
    public ProfilerState? State { get; init; }

    public override string ToString() =>
        $"CET {CetState} · managed {(Managed ? "YES" : "NO")} · 0-Engine {ZeroEngineInit} · " +
        $"F11 {(F11Binding ? "READY" : "NOT CONFIGURED")} · live CSVs {LiveResultCount}";
}
