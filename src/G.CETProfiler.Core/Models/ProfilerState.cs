using System.Text.Json.Serialization;

namespace GCETRuntimeProfiler.Core.Models;

public sealed class ProfilerState
{
    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; set; } = "";

    [JsonPropertyName("installedUtc")]
    public string InstalledUtc { get; set; } = "";

    [JsonPropertyName("coreProfilerOnly")]
    public bool CoreProfilerOnly { get; set; }

    [JsonPropertyName("asi")]
    public FileTransactionState Asi { get; set; } = new();

    [JsonPropertyName("zeroEngine")]
    public ZeroEngineTransactionState ZeroEngine { get; set; } = new();

    [JsonPropertyName("controls")]
    public DirectoryTransactionState Controls { get; set; } = new();

    [JsonPropertyName("binding")]
    public BindingTransactionState? Binding { get; set; }
}

public sealed class FileTransactionState
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "not-applicable";

    [JsonPropertyName("originalHash")]
    public string? OriginalHash { get; set; }

    [JsonPropertyName("installedHash")]
    public string? InstalledHash { get; set; }
}

public sealed class DirectoryTransactionState
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "not-applicable";

    [JsonPropertyName("originalFingerprint")]
    public string? OriginalFingerprint { get; set; }

    [JsonPropertyName("installedFingerprint")]
    public string? InstalledFingerprint { get; set; }
}

public sealed class ZeroEngineTransactionState
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "absent";

    [JsonPropertyName("presentBefore")]
    public bool PresentBefore { get; set; }

    [JsonPropertyName("bypass")]
    public DirectoryTransactionState Bypass { get; set; } = new();

    [JsonPropertyName("init")]
    public FileTransactionState Init { get; set; } = new();

    [JsonPropertyName("scheduler")]
    public FileTransactionState Scheduler { get; set; } = new();

    [JsonPropertyName("adaptiveScheduler")]
    public FileTransactionState AdaptiveScheduler { get; set; } = new();
}

public sealed class BindingTransactionState
{
    [JsonPropertyName("fileExistedBefore")]
    public bool FileExistedBefore { get; set; }

    [JsonPropertyName("hadNode")]
    public bool HadNode { get; set; }

    [JsonPropertyName("originalNodeJson")]
    public string OriginalNodeJson { get; set; } = "";

    [JsonPropertyName("installedToggle")]
    public long InstalledToggle { get; set; }

    [JsonPropertyName("originalFileHash")]
    public string? OriginalFileHash { get; set; }
}
