using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Stage A (native 2.15 compatible): source execution overlap is a
    // review candidate, NEVER evidence that returned data is identical.
    // No mod execution is instrumented or altered by this analysis.
    private sealed record ProvenanceOwner(
        string Mod, long RegistrationId, long SampledCalls,
        long SampledAllocationOperations, long SampledPositiveBytes);

    private sealed record ProvenanceSite(
        string SourceFile, long SourceLine, string Function,
        string ExecutionKind, long SampledCalls,
        long SampledAllocationOperations, long SampledPositiveBytes,
        IReadOnlyList<ProvenanceOwner> Owners);

    private sealed record ProvenanceNativeCandidate(
        string Mod, long RegistrationId, string NativeFunction,
        string LuaCallerFile, long LuaCallerLine, string CallsiteResolution,
        long SampledCalls, long SampledAllocationOperations,
        long SampledPositiveBytes);

    private sealed class DataProvenanceAnalysis
    {
        public List<ProvenanceSite> Sites { get; init; } = [];
        public List<ProvenanceSite> CrossModSites { get; init; } = [];
        public List<ProvenanceNativeCandidate> NativeCandidates { get; init; } = [];
        public long MissingRegistrationSamples { get; init; }
        public long UnresolvedSourceSamples { get; init; }
        public long NativeSampleOps { get; init; }
        public long FunctionSampleOps { get; init; }
    }

    private static string NormalizedSource(string file) =>
        (file ?? "").Trim().Replace('\\', '/');

    // A relative "init.lua" or "./json/json.lua" can refer to separate
    // copies in different mod directories. Never merge such paths across mods.
    private static bool HasQualifiedSourceIdentity(string source)
    {
        var file = NormalizedSource(source);
        return file.StartsWith("mods/", StringComparison.OrdinalIgnoreCase) ||
               file.Contains("/mods/", StringComparison.OrdinalIgnoreCase) ||
               file.StartsWith('/') ||
               (file.Length >= 3 && char.IsLetter(file[0]) &&
                file[1] == ':' && file[2] == '/');
    }

    private static bool HasLuaSource(GcTracerFunction x)
    {
        var f = NormalizedSource(x.SourceFile);
        return x.RegistrationId > 0 &&
               x.SourceLineStart > 0 &&
               !string.IsNullOrWhiteSpace(x.FunctionName) &&
               f.Length > 0 && !f.StartsWith('=') &&
               !string.Equals(x.ExecutionKind, "NATIVE_C",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNativeSource(GcTracerFunction x) =>
        string.Equals(x.ExecutionKind, "NATIVE_C",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NormalizedSource(x.SourceFile), "=[C]",
            StringComparison.Ordinal);

    private static DataProvenanceAnalysis? AnalyzeDataProvenance(
        GcTracerTelemetry? gc)
    {
        if (gc is null) return null;
        var registrations = gc.Registrations
            .GroupBy(x => x.RegistrationId)
            .ToDictionary(x => x.Key, x => x.First());

        string ModFor(long id) =>
            registrations.TryGetValue(id, out var row)
                ? row.Mod : "<registration-not-resolved>";

        var sourceSites = gc.Functions
            .Where(HasLuaSource)
            .GroupBy(x => (
                File: NormalizedSource(x.SourceFile).ToLowerInvariant(),
                x.SourceLineStart,
                Function: x.FunctionName))
            .Select(group =>
            {
                var owners = group.GroupBy(x => x.RegistrationId)
                    .Select(rows => new ProvenanceOwner(
                        ModFor(rows.Key), rows.Key,
                        rows.Sum(x => x.SampledCalls),
                        rows.Sum(x => x.SampledExclusiveAllocationOperations),
                        rows.Sum(x => x.SampledExclusiveAllocatedBytes)))
                    .OrderByDescending(x => x.SampledAllocationOperations)
                    .ThenBy(x => x.Mod, StringComparer.Ordinal)
                    .ThenBy(x => x.RegistrationId)
                    .ToList();
                return new ProvenanceSite(
                    group.Key.File, group.Key.SourceLineStart,
                    group.Key.Function, "LUA_SOURCE_SAMPLED",
                    owners.Sum(x => x.SampledCalls),
                    owners.Sum(x => x.SampledAllocationOperations),
                    owners.Sum(x => x.SampledPositiveBytes),
                    owners);
            })
            .OrderByDescending(x => x.SampledAllocationOperations)
            .ThenByDescending(x => x.SampledPositiveBytes)
            .ThenBy(x => x.SourceFile, StringComparer.Ordinal)
            .ThenBy(x => x.SourceLine)
            .ThenBy(x => x.Function, StringComparer.Ordinal)
            .ToList();

        var overlap = sourceSites
            .Where(x => HasQualifiedSourceIdentity(x.SourceFile))
            .Where(x => x.Owners
                .Select(o => o.Mod)
                .Where(o => o != "<registration-not-resolved>")
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .ToList();

        // Native symbols are NOT grouped across mods: "=[C] __index"
        // with no proven Lua callsite would create entirely fake duplicates.
        var native = gc.Functions.Where(IsNativeSource)
            .GroupBy(x => (
                x.RegistrationId, x.FunctionName,
                Caller: NormalizedSource(x.LuaCallerFile),
                x.LuaCallerLine, x.CallsiteResolution))
            .Select(group => new ProvenanceNativeCandidate(
                ModFor(group.Key.RegistrationId),
                group.Key.RegistrationId, group.Key.FunctionName,
                group.Key.Caller, group.Key.LuaCallerLine,
                group.Key.CallsiteResolution,
                group.Sum(x => x.SampledCalls),
                group.Sum(x => x.SampledExclusiveAllocationOperations),
                group.Sum(x => x.SampledExclusiveAllocatedBytes)))
            .OrderByDescending(x => x.SampledAllocationOperations)
            .ThenBy(x => x.Mod, StringComparer.Ordinal)
            .ThenBy(x => x.RegistrationId)
            .ThenBy(x => x.NativeFunction, StringComparer.Ordinal)
            .ToList();

        return new DataProvenanceAnalysis {
            Sites = sourceSites,
            CrossModSites = overlap,
            NativeCandidates = native,
            MissingRegistrationSamples = gc.Functions
                .Where(x => !registrations.ContainsKey(x.RegistrationId))
                .Sum(x => x.SampledExclusiveAllocationOperations),
            UnresolvedSourceSamples = gc.Functions
                .Where(x => !IsNativeSource(x) && !HasLuaSource(x))
                .Sum(x => x.SampledExclusiveAllocationOperations),
            NativeSampleOps = gc.Functions
                .Where(IsNativeSource)
                .Sum(x => x.SampledExclusiveAllocationOperations),
            FunctionSampleOps = gc.Functions
                .Sum(x => x.SampledExclusiveAllocationOperations)
        };
    }

    private static object? BuildDataProvenanceCandidates(
        GcTracerTelemetry? gc)
    {
        var a = AnalyzeDataProvenance(gc);
        if (a is null) return null;
        static object Site(ProvenanceSite x) => new {
            sourceFile = x.SourceFile,
            sourceLine = x.SourceLine,
            function = x.Function,
            x.ExecutionKind,
            x.SampledCalls,
            x.SampledAllocationOperations,
            x.SampledPositiveBytes,
            crossModSourceOverlap = x.Owners
                .Select(o => o.Mod)
                .Where(o => o != "<registration-not-resolved>")
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2 &&
                HasQualifiedSourceIdentity(x.SourceFile),
            sourceIdentityQualified = HasQualifiedSourceIdentity(x.SourceFile),
            returnValueChangeRate = (double?)null,
            resultEqualityObserved = false,
            inputsEquivalent = false,
            safeToShare = false,
            sampledSavingsBytes = (long?)null,
            evidenceLevel = "SOURCE_EXECUTION_OVERLAP_ONLY",
            owners = x.Owners.Select(o => new {
                o.Mod, o.RegistrationId, o.SampledCalls,
                o.SampledAllocationOperations, o.SampledPositiveBytes
            })
        };
        return new {
            provenanceVersion = "1.0-stage-a",
            stage = "BOUNDED_EXISTING_G_GC_FUNCTION_SAMPLES",
            measurementMode = "OFFLINE_REVIEW_ONLY",
            timingMetricsPreserved = true,
            gcCollectionPolicyChanged = false,
            authorizesAutomaticRewrite = false,
            sourceExecutionOverlapDoesNotProveResultEquivalence = true,
            noAdditionalRuntimeHooks = true,
            observedReadFrequencyAvailable = false,
            valueChangeFrequencyAvailable = false,
            allocationSavingsProven = false,
            unknownRegistrationZeroReassigned = false,
            coverage = new {
                registrationRows = gc?.Registrations.Count ?? 0,
                sampledFunctionRows = gc?.Functions.Count ?? 0,
                a.FunctionSampleOps,
                a.NativeSampleOps,
                a.MissingRegistrationSamples,
                a.UnresolvedSourceSamples,
                identifiedLuaSourceSites = a.Sites.Count,
                crossModSourceSites = a.CrossModSites.Count,
                ambiguousRelativeSourceOverlaps = a.Sites.Count(x =>
                    !HasQualifiedSourceIdentity(x.SourceFile) &&
                    x.Owners.Select(o => o.Mod)
                        .Where(o => o != "<registration-not-resolved>")
                        .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2),
                unresolvedNativeCallsite = a.NativeCandidates.Count(x =>
                    x.CallsiteResolution != "SINGLE_OBSERVED_LUA_CALLSITE" ||
                    string.IsNullOrWhiteSpace(x.LuaCallerFile) ||
                    x.LuaCallerLine <= 0)
            },
            crossModSourceCandidates = a.CrossModSites.Take(60).Select(Site),
            topSampledLuaProducers = a.Sites.Take(60).Select(Site),
            nativeSourceUnknown = a.NativeCandidates.Take(50).Select(x => new {
                x.Mod, x.RegistrationId,
                x.NativeFunction, x.LuaCallerFile, x.LuaCallerLine,
                x.CallsiteResolution, x.SampledCalls,
                x.SampledAllocationOperations, x.SampledPositiveBytes,
                callerProvenInSample =
                    x.CallsiteResolution == "SINGLE_OBSERVED_LUA_CALLSITE" &&
                    x.LuaCallerLine > 0 &&
                    !string.IsNullOrWhiteSpace(x.LuaCallerFile),
                acrossModResultEquivalence = "NOT_MEASURED"
            })
        };
    }

    private static void AppendDataProvenance(StringBuilder sb,
        GcTracerTelemetry gc)
    {
        var a = AnalyzeDataProvenance(gc);
        if (a is null || gc.Functions.Count == 0) return;
        sb.Append("<div class=\"section\"><h2>Data producers and cross-mod source overlap</h2>")
          .Append("<div class=\"note\">Experimental analysis of EXISTING bounded G-GC ")
          .Append("function samples only. Repeated execution of a Lua source location ")
          .Append("across mod registrations is a candidate, NOT proof of equivalent ")
          .Append("inputs, repeated returned values, safe reuse, or wasted bytes. ")
          .Append("Short relative paths like init.lua or ./json/json.lua are ") 
          .Append("excluded from cross-mod matching because distinct mods may ")
          .Append("have independent files with those names. ")
          .Append("Native C source with no observed Lua callsite is never linked ")
          .Append("across mods by function name. Zero runtime hooks added.</div>")
          .Append("<div class=\"grid\">");
        MetricCard(sb, "Sampled Lua source sites", N(a.Sites.Count),
            "Source file + line + function identity");
        MetricCard(sb, "Cross-mod source overlaps", N(a.CrossModSites.Count),
            "Investigative source execution only");
        MetricCard(sb, "Sampled native operations", N(a.NativeSampleOps),
            "Sampled allocator requests in native functions");
        MetricCard(sb, "Result-value stability", "Not measured",
            "No value equality or dedup savings assumed");
        sb.Append("</div><h3>Cross-mod source execution candidates</h3>");
        if (a.CrossModSites.Count == 0)
            sb.Append("<p>No source-location overlap between distinct mod ")
              .Append("registrations was observed in this bounded sample. ")
              .Append("This does not prove there is no duplication.</p>");
        else
        {
            sb.Append("<table><thead><tr><th>Source</th><th>Function</th>")
              .Append("<th>Observed mods</th><th>Sampled calls</th>")
              .Append("<th>Sampled alloc. ops</th><th>Sampled bytes</th>")
              .Append("<th>Sharing proved?</th></tr></thead><tbody>");
            foreach (var x in a.CrossModSites.Take(25))
            {
                sb.Append("<tr><td>").Append(H(x.SourceFile)).Append(":")
                  .Append(x.SourceLine).Append("</td><td>")
                  .Append(H(x.Function)).Append("</td><td>")
                  .Append(H(string.Join(", ", x.Owners.Select(o => o.Mod)
                      .Distinct(StringComparer.OrdinalIgnoreCase))))
                  .Append("</td><td>").Append(N(x.SampledCalls))
                  .Append("</td><td>").Append(N(x.SampledAllocationOperations))
                  .Append("</td><td>").Append(N(x.SampledPositiveBytes))
                  .Append("</td><td>No; compare actual queries/results</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("<h3>Top sampled Lua producer functions</h3>")
          .Append("<table><thead><tr><th>Source</th><th>Function</th>")
          .Append("<th>Sampled calls</th><th>Sampled alloc. ops</th>")
          .Append("<th>Sampled bytes</th></tr></thead><tbody>");
        foreach (var x in a.Sites.Take(25))
        {
            sb.Append("<tr><td>").Append(H(x.SourceFile)).Append(":")
              .Append(x.SourceLine).Append("</td><td>")
              .Append(H(x.Function)).Append("</td><td>")
              .Append(N(x.SampledCalls)).Append("</td><td>")
              .Append(N(x.SampledAllocationOperations))
              .Append("</td><td>").Append(N(x.SampledPositiveBytes))
              .Append("</td></tr>");
        }
        sb.Append("</tbody></table><div class=\"note\">Next capture requirement: ")
          .Append("bounded in-situ provider/parameter/result probes to establish ")
          .Append("repeated reads and unchanged values WITHOUT extra native calls. ")
          .Append("Only the existing Resolver can authorize source-proven reuse ")
          .Append("after aliasing, lifetime and gameplay checks.</div></div>");
    }
}
