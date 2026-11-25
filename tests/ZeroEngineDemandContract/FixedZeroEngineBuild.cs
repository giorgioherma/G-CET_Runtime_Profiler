namespace GCETRuntimeProfiler.Core.Services;
internal sealed record FixedZeroEngineBuild(
    IReadOnlyDictionary<string, byte[]> Files, string LiveState,
    string LiveInitSha256, string FixedVersion, string FixedInitSha256);
