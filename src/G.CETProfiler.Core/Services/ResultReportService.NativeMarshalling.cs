using System.Globalization;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // This is the outgoing metric for reducing native-to-Lua dispatch pressure.
    // These samples describe the native marshalling envelope, not Lua-exclusive
    // CPU time, the allocating Lua mod, or all bytes crossing that method.
    private sealed record NativeMarshallingGroup(
        string MethodHash, string ClassHash, string Boundary,
        long SampledEvents, long SampledPositiveBytes);

    private const string NativeOnActionMethodHash = "9658066991423262154";

    private static object? BuildNativeMarshallingPressureInterop(
        UnscopedDiagnosticReport? diagnostics)
    {
        if (diagnostics is null)
            return null;

        var samples = diagnostics.NativeSampleRows;
        var methodRecorded = samples.Count > 0 &&
            samples.All(row => row.ContainsKey("NativeMethodHash"));
        var classRecorded = samples.Count > 0 &&
            samples.All(row => row.ContainsKey("NativeClassHash"));

        static string ValidHash(Dictionary<string, string> row, string key) =>
            ulong.TryParse(S(row, key), NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) && value != 0
                ? value.ToString(CultureInfo.InvariantCulture)
                : "0";

        var groups = samples.GroupBy(row => (
                Method: methodRecorded ? ValidHash(row, "NativeMethodHash") : "0",
                Class: classRecorded ? ValidHash(row, "NativeClassHash") : "0",
                Boundary: S(row, "NativeExecutionBoundary")))
            .Select(group => new NativeMarshallingGroup(
                group.Key.Method, group.Key.Class, group.Key.Boundary,
                group.LongCount(), group.Sum(x => L(x, "PositiveBytes"))))
            .Where(x => x.MethodHash != "0")
            .OrderByDescending(x => x.SampledEvents)
            .ThenByDescending(x => x.SampledPositiveBytes)
            .ToArray();

        var onAction = groups.Where(x =>
            x.MethodHash == NativeOnActionMethodHash).ToArray();
        var knownPlayerPuppetClassHash = Fnv1a64("PlayerPuppet")
            .ToString(CultureInfo.InvariantCulture);
        var playerPuppetOnAction = onAction.Where(x =>
            x.ClassHash == knownPlayerPuppetClassHash).ToArray();
        var qualifiedOnAction = onAction.Where(x => x.ClassHash != "0").ToArray();

        // Exact owner-zero outer-boundary population. This is *not* a breakdown
        // of method samples and is not additive to their sampled bytes.
        var exactBoundaries = diagnostics.Boundaries.Where(x =>
            x.Name is "CET_FUNCTION_OVERRIDE_PURE" or
                "CET_FUNCTION_OVERRIDE_SCRIPT" or
                "CET_NATIVE_PROXY_CALLBACK").ToArray();

        return new
        {
            metric = "NATIVE_TO_LUA_ARGUMENT_MARSHALLING_ALLOCATION_PRESSURE",
            unit = "BOUNDED_SAMPLE_EVENTS_AND_POSITIVE_GROWTH_BYTES",
            input = "CET_Runtime_Profile_GGC_UnscopedNativeSamples.csv",
            scope = "OWNER_ZERO_NATIVE_METHOD_CONTEXT_ONLY",
            nativeMethodHashRecorded = methodRecorded,
            nativeClassHashRecorded = classRecorded,
            captureHasNativeSamples = diagnostics.HasNativeSamples,
            classQualifiedCoverage =
                !classRecorded ? "NOT_RECORDED_LEGACY" :
                qualifiedOnAction.Length > 0 ? "OBSERVED_SAMPLED_ONLY" :
                "NOT_OBSERVED_IN_THIS_CAPTURE",
            numericHashEncoding = "DECIMAL_STRING_UINT64",
            sampleCount = diagnostics.NativeSamples,
            droppedSampleCandidates = diagnostics.DroppedNativeSamples,
            methodTaggedSamples = groups.Sum(x => x.SampledEvents),
            sampledPressureDoesNotRepresentPopulation = true,
            samplingDoesNotMeasureDispatchLatency = true,
            modAllocationAttributionProven = false,
            nativeAllocationSavingsMeasured = false,
            automaticRewriteAuthorized = false,
            onAction = new
            {
                method = "OnAction",
                methodHash = NativeOnActionMethodHash,
                observedMethodSampleCount = onAction.Sum(x => x.SampledEvents),
                observedSamplePositiveBytes = onAction.Sum(x => x.SampledPositiveBytes),
                observedClassQualifiedSampleCount = qualifiedOnAction.Sum(x => x.SampledEvents),
                playerPuppetClassHash = knownPlayerPuppetClassHash,
                observedPlayerPuppetSampleCount = playerPuppetOnAction.Sum(x => x.SampledEvents),
                observedPlayerPuppetSamplePositiveBytes = playerPuppetOnAction.Sum(x => x.SampledPositiveBytes),
                perSubscriberAllocationBytes = (long?)null
            },
            nativeBoundaryExactOwnerZero = exactBoundaries.Select(x => new
            {
                nativeBoundary = x.Name,
                exactPositiveBytes = x.Bytes,
                exactAllocationOperations = x.Operations,
                reconciled = diagnostics.ExtendedReconciled,
                additiveToSampledPositiveBytes = false,
                methodOrClassAttribution = "NOT_MEASURED"
            }),
            methodGroups = groups.Take(50).Select(x => new
            {
                nativeMethodHash = x.MethodHash,
                nativeClassHash = x.ClassHash == "0" ? null : x.ClassHash,
                className = x.ClassHash == knownPlayerPuppetClassHash
                    ? "PlayerPuppet" : null,
                methodName = x.MethodHash == NativeOnActionMethodHash
                    ? "OnAction" : null,
                boundary = x.Boundary,
                x.SampledEvents,
                observedSamplePositiveBytes = x.SampledPositiveBytes,
                classMethodIdentityRecorded = x.ClassHash != "0",
                modOwner = (string?)null,
                subscriberRegistrationId = (long?)null,
                observedSampleNotTotalPopulation = true
            }),
            interpretation = "Sampled positive LuaJIT allocation growth inside a native method marshalling envelope. Class+method is not a mod, subscriber, unique allocator, or measured savings."
        };
    }

    private static ulong Fnv1a64(string name)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(name))
            hash = unchecked((hash ^ value) * prime);
        return hash;
    }
}
