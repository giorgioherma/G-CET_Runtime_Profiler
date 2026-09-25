using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

public interface IProfilerService
{
    string PackageVersion { get; }
    string ResultsRoot { get; }

    ProfilerStatus GetStatus(string gameRoot);
    ProfilerStatus Install(string gameRoot, bool coreProfilerOnly);
    string SaveCaptureTitle(string gameRoot, string captureTitle);
    string? Collect(string gameRoot);
    string? ResetLive(string gameRoot);
    string? Restore(string gameRoot);
    EmergencyRestoreResult EmergencyRestore(string gameRoot);
}
