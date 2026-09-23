using System.Text.Json.Serialization;

namespace GCETRuntimeProfiler.Core.Models;

public sealed class EmergencyRestoreResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("complete")]
    public bool Complete { get; init; }

    [JsonPropertyName("archived")]
    public string? Archived { get; init; }

    [JsonPropertyName("statePreserved")]
    public bool StatePreserved { get; init; }

    [JsonPropertyName("reportPath")]
    public string ReportPath { get; init; } = "";

    [JsonPropertyName("actions")]
    public List<EmergencyRestoreAction> Actions { get; init; } = [];

    [JsonPropertyName("manualReview")]
    public List<string> ManualReview { get; init; } = [];
}

public sealed class EmergencyRestoreAction
{
    [JsonPropertyName("component")]
    public string Component { get; init; } = "";

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";

    [JsonPropertyName("path")]
    public string Path { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}
