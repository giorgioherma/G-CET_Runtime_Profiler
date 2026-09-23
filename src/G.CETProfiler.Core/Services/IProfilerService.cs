using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

public interface IProfilerService
{
    ProfilerStatus GetStatus(string gameRoot);
    ProfilerStatus Install(string gameRoot, bool coreProfilerOnly);
    string? Collect(string gameRoot);
    string? ResetLive(string gameRoot);
    string? Restore(string gameRoot);
}
