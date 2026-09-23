namespace GCETRuntimeProfiler.Core.Models;

public sealed record ProfilerStatus(
    string CetState,
    bool Managed,
    string ManagedMode,
    bool ZeroEnginePresent,
    string ZeroEngineInit,
    string Scheduler,
    bool ControlsPresent,
    bool F11Binding,
    int LiveResultCount,
    string ResultsRoot);
