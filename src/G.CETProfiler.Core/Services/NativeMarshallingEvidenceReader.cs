using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

// The native marshalling metric is one shared native event-envelope measurement.
// Class+method disambiguates game hooks, not the Lua mod/subscriber that received
// one invocation. This reader never feeds generic or semantic safety admission.
internal sealed record NativeMarshallingResolverEvidence(
    string Status,
    bool HasClassQualifiedNativeSamples,
    bool NativeMethodRecorded,
    long NativeSampleCount,
    long OnActionMethodSamples,
    long OnActionClassQualifiedSamples,
    long PlayerPuppetOnActionSamples,
    JsonElement? FullEvidence)
{
    internal static NativeMarshallingResolverEvidence Unavailable { get; } =
        new("NOT_CAPTURED", false, false, 0, 0, 0, 0, null);
}

internal static class NativeMarshallingEvidenceReader
{
    internal static NativeMarshallingResolverEvidence Read(JsonElement handoff)
    {
        if (!handoff.TryGetProperty("nativeMarshallingAllocationPressure",
                out var pressure) || pressure.ValueKind != JsonValueKind.Object)
            return NativeMarshallingResolverEvidence.Unavailable;

        var methodRecorded = Boolean(pressure, "nativeMethodHashRecorded");
        var classRecorded = Boolean(pressure, "nativeClassHashRecorded");
        var sampleCount = Number(pressure, "sampleCount");
        var action = pressure.TryGetProperty("onAction", out var a) &&
            a.ValueKind == JsonValueKind.Object ? a : default;

        return new NativeMarshallingResolverEvidence(
            !methodRecorded ? "METHOD_NOT_RECORDED" :
            !classRecorded ? "CLASS_NOT_RECORDED" :
            Number(action, "observedClassQualifiedSampleCount") > 0
                ? "CLASS_METHOD_SAMPLES_OBSERVED"
                : "CLASS_METHOD_NOT_OBSERVED",
            classRecorded, methodRecorded, sampleCount,
            Number(action, "observedMethodSampleCount"),
            Number(action, "observedClassQualifiedSampleCount"),
            Number(action, "observedPlayerPuppetSampleCount"),
            pressure.Clone());
    }

    private static long Number(JsonElement root, string key)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var result) && result >= 0)
            return result;
        return 0;
    }

    private static bool Boolean(JsonElement root, string key) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(key, out var value) &&
        value.ValueKind == JsonValueKind.True;
}
