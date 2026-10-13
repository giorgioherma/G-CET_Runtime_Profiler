using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Native 2.15 already records exact sampled Lua callsite -> child-function
    // edges. This is REPORT-TIME evidence, never a cache or rewrite authority.
    // G-GC "own" allocation totals belong to a sampled function identity,
    // NOT independently to each caller of that function.
    private sealed class DeepReuseCaller
    {
        public string File { get; init; } = "";
        public long Line { get; init; }
        public string ParentFunctionKey { get; init; } = "";
        public long SampledEdgeCalls { get; set; }
        public double SampledChildInclusiveMs { get; set; }
        public HashSet<(long Frame, long Ordinal)> Invocations { get; } = [];
        public HashSet<long> Frames { get; } = [];
    }

    private sealed class DeepReuseFunction
    {
        public long RegistrationId { get; init; }
        public long ProfileEpoch { get; init; }
        public string FunctionKey { get; init; } = "";
        public string Mod { get; init; } = "";
        public string FunctionName { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public long SourceLine { get; init; }
        public long SampledFunctionCalls { get; init; }
        public long SampledOwnPositiveBytes { get; init; }
        public long SampledOwnAllocationOps { get; init; }
        public Dictionary<(string File, long Line, string Parent), DeepReuseCaller> Callers { get; } = [];
        public long SampledEdgeCalls => Callers.Values.Sum(x => x.SampledEdgeCalls);
        public bool HasMultipleCallsites => Callers.Count > 1;
    }

    private sealed class DeepReuseAnalysis
    {
        public string Status { get; init; } = "NOT_CAPTURED";
        public long FunctionRows { get; init; }
        public long CallsiteRows { get; init; }
        public long MatchedCallsiteRows { get; init; }
        public long UnmatchedCallsiteRows { get; init; }
        public long AmbiguousFunctionKeyRows { get; init; }
        public long ConflictingRegistrationIds { get; init; }
        public long SampledFunctionOwnBytesOnce { get; init; }
        public long SampledFunctionOwnOpsOnce { get; init; }
        public List<DeepReuseFunction> Candidates { get; init; } = [];
    }

    private static DeepReuseAnalysis AnalyzeDeepReuse(string captureRoot)
    {
        var functionPath = FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Deep_Functions.csv");
        var callsitePath = FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Deep_Callsites.csv");
        var registrationPath = FindProfilerFile(captureRoot,
            "CET_Runtime_Profile_Deep_Registrations.csv");

        // A legacy capture must still generate a valid normal report. Do not
        // manufacture empty measurements from absent CSVs.
        if (functionPath is null || callsitePath is null || registrationPath is null)
            return new DeepReuseAnalysis { Status = "NOT_CAPTURED" };

        var functions = ReadCsv(functionPath);
        var callsites = ReadCsv(callsitePath);
        var registrations = ReadCsv(registrationPath);
        if (functions.Count == 0 || callsites.Count == 0)
            return new DeepReuseAnalysis {
                Status = "NO_DEEP_SAMPLES",
                FunctionRows = functions.Count,
                CallsiteRows = callsites.Count
            };

        var names = new Dictionary<long, string>();
        var conflicts = new HashSet<long>();
        foreach (var row in registrations)
        {
            var id = L(row, "RegistrationId");
            if (id <= 0) continue;
            var mod = S(row, "Mod");
            if (!names.TryAdd(id, mod) &&
                !string.Equals(names[id], mod, StringComparison.Ordinal))
                conflicts.Add(id);
        }
        foreach (var id in conflicts) names.Remove(id);

        // ProfileEpoch in Deep_Registrations can differ from sampled epochs.
        // Registration ID resolves the owner; function -> callsite matching
        // MUST include the sampled epoch and complete function key.
        var byKey = new Dictionary<(long Id, long Epoch, string Key), DeepReuseFunction>();
        var duplicateKeys = new HashSet<(long Id, long Epoch, string Key)>();
        foreach (var row in functions)
        {
            var key = (L(row, "RegistrationId"), L(row, "ProfileEpoch"),
                S(row, "FunctionKey"));
            if (key.Item1 <= 0 || key.Item3.Length == 0) continue;
            if (byKey.ContainsKey(key))
            {
                duplicateKeys.Add(key);
                byKey.Remove(key);
                continue;
            }
            if (duplicateKeys.Contains(key)) continue;
            byKey[key] = new DeepReuseFunction {
                RegistrationId = key.Item1,
                ProfileEpoch = key.Item2,
                FunctionKey = key.Item3,
                Mod = names.GetValueOrDefault(key.Item1, "<unresolved-registration>"),
                FunctionName = S(row, "FunctionName"),
                SourceFile = S(row, "SourceFile"),
                SourceLine = L(row, "SourceLineStart"),
                SampledFunctionCalls = Math.Max(0, L(row, "Calls")),
                SampledOwnPositiveBytes = Math.Max(0, L(row,
                    "GGCTracerSampledOwnAllocatedBytes")),
                SampledOwnAllocationOps = Math.Max(0, L(row,
                    "GGCTracerSampledOwnAllocations"))
            };
        }

        long matched = 0;
        foreach (var row in callsites)
        {
            var key = (L(row, "RegistrationId"), L(row, "ProfileEpoch"),
                S(row, "ChildFunctionKey"));
            if (!byKey.TryGetValue(key, out var function)) continue;
            matched++;
            var file = NormalizedSource(S(row, "CallerSourceFile"));
            var siteKey = (file, L(row, "CallerLine"),
                S(row, "ParentFunctionKey"));
            if (!function.Callers.TryGetValue(siteKey, out var caller))
            {
                caller = new DeepReuseCaller {
                    File = file, Line = siteKey.Item2,
                    ParentFunctionKey = siteKey.Item3
                };
                function.Callers[siteKey] = caller;
            }
            caller.SampledEdgeCalls += Math.Max(0, L(row, "Calls"));
            caller.SampledChildInclusiveMs += Math.Max(0, D(row, "ChildInclusiveMs"));
            caller.Frames.Add(L(row, "Frame"));
            caller.Invocations.Add((L(row, "Frame"),
                L(row, "FrameInvocationOrdinal")));
        }

        var candidates = byKey.Values
            .Where(x => x.Callers.Count > 0)
            .OrderByDescending(x => x.SampledOwnPositiveBytes)
            .ThenByDescending(x => x.SampledOwnAllocationOps)
            .ThenByDescending(x => x.SampledEdgeCalls)
            .ThenBy(x => x.Mod, StringComparer.Ordinal)
            .ThenBy(x => x.FunctionKey, StringComparer.Ordinal)
            .ToList();

        return new DeepReuseAnalysis {
            Status = "BOUNDED_SAMPLE_ANALYZED",
            FunctionRows = functions.Count,
            CallsiteRows = callsites.Count,
            MatchedCallsiteRows = matched,
            UnmatchedCallsiteRows = callsites.Count - matched,
            AmbiguousFunctionKeyRows = duplicateKeys.Count,
            ConflictingRegistrationIds = conflicts.Count,
            // Count every sampled function's own allocation cost once, even
            // if there are 10 different observed caller sites.
            SampledFunctionOwnBytesOnce = byKey.Values
                .Sum(x => x.SampledOwnPositiveBytes),
            SampledFunctionOwnOpsOnce = byKey.Values
                .Sum(x => x.SampledOwnAllocationOps),
            Candidates = candidates
        };
    }

    private static object BuildDeepReuseCandidates(DeepReuseAnalysis? a)
    {
        if (a is null) a = new DeepReuseAnalysis();
        static object Caller(DeepReuseCaller x) => new {
            sourceFile = x.File,
            sourceLine = x.Line,
            parentFunctionKey = x.ParentFunctionKey,
            sampledEdgeCalls = x.SampledEdgeCalls,
            sampledChildInclusiveMs = Round(x.SampledChildInclusiveMs, 6),
            sampledFrames = x.Frames.Count,
            sampledInvocations = x.Invocations.Count,
            allocatedBytesAttributedToCaller = (long?)null,
            returnValueEquality = "NOT_MEASURED",
            canReuseTable = false
        };
        static object Candidate(DeepReuseFunction f) => new {
            mod = f.Mod, registrationId = f.RegistrationId,
            profileEpoch = f.ProfileEpoch, functionKey = f.FunctionKey,
            functionName = f.FunctionName, sourceFile = f.SourceFile,
            sourceLine = f.SourceLine,
            sampledFunctionCalls = f.SampledFunctionCalls,
            sampledEdgeCalls = f.SampledEdgeCalls,
            sampledOwnPositiveBytesOnce = f.SampledOwnPositiveBytes,
            sampledOwnAllocationOpsOnce = f.SampledOwnAllocationOps,
            distinctObservedCallerSites = f.Callers.Count,
            hasMultipleObservedCallsites = f.HasMultipleCallsites,
            callerAllocationSplit = "NOT_MEASURED",
            valuesAndParametersEquivalent = "NOT_MEASURED",
            underlyingDataChangeFrequency = "NOT_MEASURED",
            mutationAndAliasing = "NOT_PROVEN",
            lifetimeAndInvalidation = "NOT_PROVEN",
            reuseEligibility = "INVESTIGATE_ONLY",
            safeToShare = false,
            estimatedAvoidableBytes = (long?)null,
            callers = f.Callers.Values
                .OrderByDescending(c => c.SampledEdgeCalls)
                .ThenBy(c => c.File, StringComparer.Ordinal)
                .ThenBy(c => c.Line)
                .Take(12).Select(Caller)
        };
        return new {
            provenanceVersion = "1.1-deep-callsite-review",
            stage = "EXISTING_NATIVE_2_15_DEEP_SAMPLES",
            status = a.Status,
            measurementMode = "OFFLINE_REPORT_ONLY_NO_ADDITIONAL_HOOKS",
            nativeVersionRequirement = "2.15.0_OR_COMPATIBLE_DEEP_CSV",
            timingMetricsPreserved = true,
            authorizesAutomaticRewrite = false,
            queryValueStabilityMeasured = false,
            mutationOrAliasSafetyProven = false,
            crossModSharedTableEligibilityMeasured = false,
            globalCacheOrModChangesMade = false,
            ownFunctionAllocationNotDividedAmongCallers = true,
            gcCollectorTriggerIsNotAllocationProducer = true,
            positiveBytesNotEquivalentToDisposableGcHeap = true,
            coverage = new {
                functionRows = a.FunctionRows, callsiteRows = a.CallsiteRows,
                matchedCallsiteRows = a.MatchedCallsiteRows,
                unmatchedCallsiteRows = a.UnmatchedCallsiteRows,
                ambiguousFunctionKeyRows = a.AmbiguousFunctionKeyRows,
                conflictingRegistrationIds = a.ConflictingRegistrationIds,
                functionsWithObservedCallers = a.Candidates.Count,
                sampledFunctionOwnPositiveBytesCountedOnce =
                    a.SampledFunctionOwnBytesOnce,
                sampledFunctionOwnAllocationOpsCountedOnce =
                    a.SampledFunctionOwnOpsOnce,
                qualifiedSourceDoesNotProveResultIdentity = true,
                ownerZeroSourceAttribution = "NOT_MEASURED",
                sampledValueChangeEvents = (long?)null
            },
            topSampledAllocationProducers = a.Candidates
                .Take(100).Select(Candidate),
            topRepeatedProducerCallsites = a.Candidates
                .OrderByDescending(x => x.SampledEdgeCalls)
                .ThenByDescending(x => x.SampledOwnAllocationOps)
                .ThenBy(x => x.Mod, StringComparer.Ordinal)
                .ThenBy(x => x.FunctionKey, StringComparer.Ordinal)
                .Take(100).Select(Candidate),
            // The same native Get* name across owners is insufficient for
            // equality; do not fabricate a cross-mod equivalent-result group.
            confirmedReusableCrossModDataGroups = Array.Empty<object>()
        };
    }

    private static void AppendDeepReuseCandidates(StringBuilder sb, DeepReuseAnalysis? a)
    {
        if (a is null) return;
        sb.Append("<div class=\"section\"><h2>Data reuse investigation: sampled producers and callers</h2>")
          .Append("<div class=\"note\">Existing native 2.15 deep samples are joined by ")
          .Append("registration, sample epoch and function key. Allocation bytes and operations ")
          .Append("belong to the sampled function once, not separately to every caller. ")
          .Append("The collector trigger is not the allocating producer. ")
          .Append("No result values, real table-content change rate, writer ownership, ")
          .Append("parameters or lifecycle invalidation are measured here. ")
          .Append("<b>No tables have been approved for cross-mod sharing.</b></div>");

        if (a.Status != "BOUNDED_SAMPLE_ANALYZED")
        {
            sb.Append("<p>Deep data reuse: ").Append(H(a.Status))
              .Append(". This older/empty capture still supports the usual CPU and GC report.</p></div>");
            return;
        }

        sb.Append("<div class=\"grid\">");
        MetricCard(sb, "Matched deep callsite rows",
            N(a.MatchedCallsiteRows) + " / " + N(a.CallsiteRows),
            "Bounded sampled invocation edges");
        MetricCard(sb, "Functions with caller evidence",
            N(a.Candidates.Count), "Not total runtime producers");
        MetricCard(sb, "Function sample allocation operations",
            N(a.SampledFunctionOwnOpsOnce), "Own allocations counted once per sampled function");
        MetricCard(sb, "Safe-to-share groups", "0 proven",
            "Result equality and invalidation not measured");
        sb.Append("</div>");

        if (a.UnmatchedCallsiteRows > 0 || a.AmbiguousFunctionKeyRows > 0 ||
            a.ConflictingRegistrationIds > 0)
            sb.Append("<p class=\"note\">Coverage warning: unmatched edges ")
              .Append(N(a.UnmatchedCallsiteRows)).Append("; duplicate function keys ")
              .Append(N(a.AmbiguousFunctionKeyRows))
              .Append("; conflicting registration owners ")
              .Append(N(a.ConflictingRegistrationIds)).Append(".</p>");

        sb.Append("<h3>Repeated data-producer investigations (sampled edge calls)</h3>")
          .Append("<table><thead><tr><th>Mod / function</th><th>Executing source</th>")
          .Append("<th>Top observed Lua caller</th><th>Observed callsites</th>")
          .Append("<th>Sampled edge calls</th><th>Function own sampled allocation ops</th>")
          .Append("<th>Own sampled bytes</th><th>Reusable?</th></tr></thead><tbody>");
        foreach (var f in a.Candidates
            .OrderByDescending(x => x.SampledEdgeCalls)
            .ThenByDescending(x => x.SampledOwnAllocationOps)
            .ThenBy(x => x.Mod, StringComparer.Ordinal)
            .Take(35))
        {
            var caller = f.Callers.Values
                .OrderByDescending(x => x.SampledEdgeCalls)
                .ThenBy(x => x.File, StringComparer.Ordinal).First();
            sb.Append("<tr><td>").Append(H(f.Mod)).Append(" / ")
              .Append(H(f.FunctionName)).Append("</td><td>")
              .Append(H(f.SourceFile)).Append(":").Append(f.SourceLine)
              .Append("</td><td>").Append(H(caller.File)).Append(":")
              .Append(caller.Line)
              .Append("</td><td>").Append(N(f.Callers.Count))
              .Append("</td><td>").Append(N(f.SampledEdgeCalls))
              .Append("</td><td>").Append(N(f.SampledOwnAllocationOps))
              .Append("</td><td>").Append(N(f.SampledOwnPositiveBytes))
              .Append("</td><td>Unknown; source and result proof needed</td></tr>");
        }
        sb.Append("</tbody></table>")
          .Append("<p class=\"note\">These are sampled function-level own allocations, ")
          .Append("not exact caller allocation costs, whole-capture savings or retained heap. ")
          .Append("Native C helpers may appear as producers without establishing ")
          .Append("which Lua allocation source caused their observed requests. ")
          .Append("See <code>deepDataReuseCandidates</code> in both JSON reports for ")
          .Append("per-caller file/line, sampled frames and invocation details.</p></div>");
    }
}
