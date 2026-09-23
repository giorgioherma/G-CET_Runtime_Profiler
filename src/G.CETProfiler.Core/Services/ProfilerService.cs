using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// C# port target for the current alpha6c PowerShell lifecycle engine.
/// Port behavior 1:1 before changing product behavior.
/// </summary>
public sealed class ProfilerService : IProfilerService
{
    public ProfilerStatus GetStatus(string gameRoot) => throw PortPending();
    public ProfilerStatus Install(string gameRoot, bool coreProfilerOnly) => throw PortPending();
    public string? Collect(string gameRoot) => throw PortPending();
    public string? ResetLive(string gameRoot) => throw PortPending();
    public string? Restore(string gameRoot) => throw PortPending();

    private static NotImplementedException PortPending() =>
        new("C# lifecycle port is intentionally not wired yet. See docs/PORT_CONTRACT.md and reference/powershell-alpha6c/.");
}
