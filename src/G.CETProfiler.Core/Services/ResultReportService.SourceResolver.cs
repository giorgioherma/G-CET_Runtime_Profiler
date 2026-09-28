using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    private const string SourceGroupLeaveAlone = "LEAVE_ALONE";
    private const string SourceGroupExactCadence = "EXACT_CADENCE";
    private const string SourceGroupMixedSplit = "MIXED_SPLIT";
    private const string SourceGroupActiveDormant = "ACTIVE_DORMANT";
    private const int SourceMaxLuaFileBytes = 2 * 1024 * 1024;
    private const int SourceMaxLuaFilesPerMod = 4000;

    private static readonly Regex SourceOnUpdateRegistration = new(
        @"\b(?<registrar>registerForEvent|registerRuntimeEvent)\s*\(\s*(?<quote>['""])onUpdate\k<quote>\s*,\s*function\s*\((?<parameters>[^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static object BuildSourceCadenceResolution(string captureRoot, string? modsRoot)
    {
        var runtimePath = Path.Combine(captureRoot, CadenceResolutionFileName);
        if (!File.Exists(runtimePath))
        {
            return new
            {
                schemaVersion = "0.1",
                generatedUtc = DateTime.UtcNow.ToString("O"),
                policy = SourceResolverPolicy(),
                quality = new
                {
                    runtimeCadenceAvailable = false,
                    exactTimelineUsable = false,
                    sourceRootAvailable = false,
                    callbackCount = 0
                },
                summary = new
                {
                    leaveAlone = 0,
                    exactCadence = 0,
                    mixedSplit = 0,
                    activeDormant = 0,
                    transformCandidates = 0
                },
                callbacks = Array.Empty<object>()
            };
        }

        using var runtimeDocument = JsonDocument.Parse(File.ReadAllText(runtimePath));
        var runtimeRoot = runtimeDocument.RootElement;
        var exactTimelineUsable = SourceJsonNestedBool(runtimeRoot, "quality", "exactTimelineUsable");
        var stateRatioThreshold = SourceJsonNestedDouble(runtimeRoot, "thresholds", "scenarioSensitiveRatio", 1.80);

        var sourceRootAvailable =
            !string.IsNullOrWhiteSpace(modsRoot) &&
            Directory.Exists(modsRoot);

        var modFolders = sourceRootAvailable
            ? SourceBuildModFolderIndex(modsRoot!)
            : new List<SourceModFolder>();

        var resolved = new List<SourceCadenceDecision>();
        if (SourceTryProperty(runtimeRoot, "callbacks", out var callbacksElement) &&
            callbacksElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var callback in callbacksElement.EnumerateArray())
            {
                resolved.Add(SourceResolveCallback(
                    captureRoot,
                    callback,
                    modFolders,
                    exactTimelineUsable,
                    stateRatioThreshold));
            }
        }

        var ordered = resolved
            .OrderByDescending(x => x.Runtime.FamilyWorkSharePct)
            .ThenByDescending(x => x.Runtime.GlobalWorkSharePct)
            .ThenByDescending(x => x.Runtime.ExclusiveMsPerSecond)
            .ThenBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var cumulativeFamilyShare = 0.0;
        for (var i = 0; i < ordered.Length; i++)
        {
            cumulativeFamilyShare += ordered[i].Runtime.FamilyWorkSharePct;
            ordered[i].PriorityRank = i + 1;
            ordered[i].CumulativeFamilyWorkSharePct = Math.Min(100.0, cumulativeFamilyShare);
        }

        return new
        {
            schemaVersion = "0.1",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                producer = "G-CET-Runtime-Profiler",
                stage = "source-confirmed-cadence-resolver",
                input = CadenceResolutionFileName,
                domain = "cet/onUpdate"
            },
            policy = SourceResolverPolicy(),
            ranking = new
            {
                basis = "relative-runtime-share",
                fixedMillisecondsCutoff = false,
                primary = "familyWorkSharePct",
                secondary = "globalWorkSharePct",
                note = "Safety classification is independent from cost. Relative shares only order proven candidates; they never make an unsafe transform safe."
            },
            quality = new
            {
                runtimeCadenceAvailable = true,
                exactTimelineUsable,
                sourceRootAvailable,
                activeDormantScenarioRatio = stateRatioThreshold,
                callbackCount = ordered.Length
            },
            groups = new[]
            {
                SourceGroupLeaveAlone,
                SourceGroupExactCadence,
                SourceGroupMixedSplit,
                SourceGroupActiveDormant
            },
            summary = new
            {
                leaveAlone = ordered.Count(x => x.Group == SourceGroupLeaveAlone),
                exactCadence = ordered.Count(x => x.Group == SourceGroupExactCadence),
                mixedSplit = ordered.Count(x => x.Group == SourceGroupMixedSplit),
                activeDormant = ordered.Count(x => x.Group == SourceGroupActiveDormant),
                transformCandidates = ordered.Count(x => x.Group != SourceGroupLeaveAlone)
            },
            callbacks = ordered
        };
    }

    private static object SourceResolverPolicy() => new
    {
        stackAgnostic = true,
        modNameRules = false,
        exactFourGroups = true,
        uncertainMeansLeaveAlone = true,
        sourceAndRuntimeEvidenceRequired = true,
        directSourceMatchRequired = true,
        multipleOnUpdateRegistrationsMeanLeaveAlone = true,
        fullFileShaIsProvenanceOnly = true,
        unrelatedFileChangesAllowed = true,
        futureRewriteMustRevalidateCurrentTargetStructure = true,
        structuralRecipeMatchCanSurviveFileUpdates = true,
        staleResolverEvidenceNeverAuthorizesRewrite = true,
        note = "This pass classifies callback structures, never named mods. Full-file SHA is provenance only. A future rewrite must re-read the current deployed source and prove the same finite recipe/preconditions again; if that proof fails, only that target resolves to LEAVE_ALONE."
    };

    private static SourceCadenceDecision SourceResolveCallback(
        string captureRoot,
        JsonElement callback,
        IReadOnlyList<SourceModFolder> modFolders,
        bool exactTimelineUsable,
        double stateRatioThreshold)
    {
        var owner = SourceJsonString(callback, "Owner", "owner");
        var kind = SourceJsonString(callback, "Kind", "kind");
        var target = SourceJsonString(callback, "Target", "target");
        var infrastructure = SourceJsonBool(callback, "Infrastructure", "infrastructure");
        var runtimeClass = SourceJsonString(callback, "Classification", "classification");
        var runtimeRecommendation = SourceJsonString(callback, "Recommendation", "recommendation");
        var callsPerSecond = SourceJsonDouble(callback, "CallsPerSecond", "callsPerSecond");
        var exclusiveMsPerSecond = SourceJsonDouble(callback, "ExclusiveMsPerSecond", "exclusiveMsPerSecond");
        var scenarioCostRatio = SourceJsonDouble(callback, "ScenarioCostRatio", "scenarioCostRatio", 1.0);
        var runtimeIntervalMs = SourceJsonNullableDouble(callback, "ResolvedIntervalMs", "resolvedIntervalMs");
        var runtimeCadenceSupportPct = SourceJsonNullableDouble(callback, "CadenceSupportPct", "cadenceSupportPct");

        var importance = SourceReadImportance(captureRoot, owner, kind, target);
        var runtime = new SourceRuntimeEvidence
        {
            Classification = runtimeClass,
            Recommendation = runtimeRecommendation,
            CallsPerSecond = callsPerSecond,
            ExclusiveMsPerSecond = exclusiveMsPerSecond,
            GlobalWorkSharePct = importance.GlobalWorkSharePct,
            FamilyWorkSharePct = importance.FamilyWorkSharePct,
            OwnerWorkSharePct = importance.OwnerWorkSharePct,
            ScenarioCostRatio = scenarioCostRatio,
            ResolvedIntervalMs = runtimeIntervalMs,
            CadenceSupportPct = runtimeCadenceSupportPct,
            ExactTimelineUsable = exactTimelineUsable
        };

        SourceCadenceDecision Leave(string reason, SourceMatchEvidence? source = null) => new()
        {
            Owner = owner,
            Kind = kind,
            Target = target,
            Group = SourceGroupLeaveAlone,
            TransformCandidate = false,
            Confidence = 1.0,
            Reason = reason,
            Runtime = runtime,
            Source = source ?? SourceMatchEvidence.Empty()
        };

        if (!kind.Equals("event", StringComparison.OrdinalIgnoreCase) ||
            !target.Equals("onUpdate", StringComparison.OrdinalIgnoreCase))
            return Leave("Only exact event::onUpdate callbacks are eligible for this source pass.");

        if (!exactTimelineUsable)
            return Leave("Exact callback timeline quality is not sufficient for source-confirmed optimization.");

        if (infrastructure || IsInfrastructureOwner(owner))
            return Leave("Profiler/scheduler infrastructure is intentionally excluded from cadence transformation.");

        var folderMatches = SourceMatchModFolder(owner, modFolders);
        if (folderMatches.Count != 1)
        {
            return Leave(folderMatches.Count == 0
                ? "No unique CET mod source folder matches the runtime callback owner."
                : "Multiple CET mod source folders match the runtime callback owner.");
        }

        var folder = folderMatches[0];
        var discovery = SourceDiscoverCallbacks(folder);
        if (discovery.ParseErrors > 0)
        {
            return Leave(
                "The mod contains an onUpdate registration that could not be parsed conservatively.",
                SourceMatchEvidence.FromDiscovery(folder, discovery));
        }

        if (discovery.Callbacks.Count != 1)
        {
            return Leave(
                discovery.Callbacks.Count == 0
                    ? "No direct onUpdate callback body could be mapped to this runtime owner."
                    : "More than one direct onUpdate callback maps to this runtime owner, so per-callback cost attribution is ambiguous.",
                SourceMatchEvidence.FromDiscovery(folder, discovery));
        }

        var sourceCallback = discovery.Callbacks[0];
        var analysis = SourceAnalyzeCallback(sourceCallback);
        var sourceEvidence = SourceMatchEvidence.FromAnalysis(folder, discovery, sourceCallback, analysis);

        if (analysis.HasSupportedCadenceGate)
        {
            var mixed = analysis.HasMeaningfulFrameWorkBeforeCadenceGate ||
                        analysis.SupportedCadenceGateCount > 1;

            return new SourceCadenceDecision
            {
                Owner = owner,
                Kind = kind,
                Target = target,
                Group = mixed ? SourceGroupMixedSplit : SourceGroupExactCadence,
                TransformCandidate = true,
                Confidence = mixed ? 0.92 : 0.95,
                Reason = mixed
                    ? "Source contains a structurally proven timer/deadline gate plus meaningful work that must remain on the rendered-frame path."
                    : "Source contains a structurally proven timer/deadline gate with no meaningful frame work before the gated body.",
                Runtime = runtime,
                Source = sourceEvidence
            };
        }

        if (analysis.HasUnsupportedCadenceHint)
        {
            return Leave(
                "Source contains a cadence/polling hint, but its structure is not in the conservative transform-safe set.",
                sourceEvidence);
        }

        if (scenarioCostRatio >= stateRatioThreshold && analysis.HasSimpleStateGate)
        {
            return new SourceCadenceDecision
            {
                Owner = owner,
                Kind = kind,
                Target = target,
                Group = SourceGroupActiveDormant,
                TransformCandidate = true,
                Confidence = Math.Clamp(0.72 + (scenarioCostRatio - stateRatioThreshold) * 0.08, 0.72, 0.90),
                Reason = "Exact runtime evidence is state-sensitive and source has a simple structural state gate suitable for a later active/dormant transform pass.",
                Runtime = runtime,
                Source = sourceEvidence
            };
        }

        return Leave(
            "Runtime and source evidence do not prove a universally safe cadence transformation boundary.",
            sourceEvidence);
    }

    private static List<SourceModFolder> SourceBuildModFolderIndex(string modsRoot)
    {
        try
        {
            return Directory.EnumerateDirectories(modsRoot)
                .Select(path => new SourceModFolder
                {
                    Name = Path.GetFileName(path),
                    Path = path,
                    NormalizedName = SourceNormalizeName(Path.GetFileName(path))
                })
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<SourceModFolder> SourceMatchModFolder(
        string owner,
        IReadOnlyList<SourceModFolder> modFolders)
    {
        var exact = modFolders
            .Where(x => x.Name.Equals(owner, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (exact.Count > 0)
            return exact;

        var normalized = SourceNormalizeName(owner);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        return modFolders
            .Where(x => x.NormalizedName.Equals(normalized, StringComparison.Ordinal))
            .ToList();
    }

    private static string SourceNormalizeName(string value) =>
        new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static SourceDiscovery SourceDiscoverCallbacks(SourceModFolder folder)
    {
        var result = new SourceDiscovery();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder.Path, "*.lua", SearchOption.AllDirectories)
                .Take(SourceMaxLuaFilesPerMod)
                .ToArray();
        }
        catch
        {
            result.ParseErrors++;
            return result;
        }

        foreach (var file in files)
        {
            string text;
            try
            {
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > SourceMaxLuaFileBytes)
                    continue;

                text = File.ReadAllText(file);
                result.ScannedLuaFiles++;
            }
            catch
            {
                continue;
            }

            foreach (Match match in SourceOnUpdateRegistration.Matches(text))
            {
                if (!SourceTryFindFunctionEnd(text, match.Index + match.Length, out var endStart, out _))
                {
                    result.ParseErrors++;
                    continue;
                }

                var bodyStart = match.Index + match.Length;
                var body = text.Substring(bodyStart, Math.Max(0, endStart - bodyStart));
                result.Callbacks.Add(new SourceCallbackMatch
                {
                    Registrar = match.Groups["registrar"].Value,
                    Parameters = match.Groups["parameters"].Value,
                    RelativeFile = Path.GetRelativePath(folder.Path, file).Replace('\\', '/'),
                    FileSha256 = SourceSha256(file),
                    CallbackBodySha256 = SourceHashText(body),
                    RegistrationLine = SourceLineNumber(text, match.Index),
                    BodyStartLine = SourceLineNumber(text, bodyStart),
                    BodyEndLine = SourceLineNumber(text, endStart),
                    Body = body
                });
            }
        }

        return result;
    }

    private static SourceCallbackAnalysis SourceAnalyzeCallback(SourceCallbackMatch callback)
    {
        var lines = callback.Body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var deltaParameter = callback.Parameters
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? "delta";

        var evidence = new List<SourcePatternEvidence>();
        var ignoredFrameLines = new HashSet<int>();
        var supportedGateLines = new List<int>();

        var simpleAssignment = new Regex(
            @"^\s*(?<left>[A-Za-z_]\w*)\s*=\s*(?<right>[A-Za-z_]\w*)\s*(?<op>[+-])\s*(?<delta>[A-Za-z_]\w*)\b",
            RegexOptions.CultureInvariant);

        for (var i = 0; i < lines.Length; i++)
        {
            var assignment = simpleAssignment.Match(lines[i]);
            if (!assignment.Success ||
                !assignment.Groups["left"].Value.Equals(assignment.Groups["right"].Value, StringComparison.Ordinal) ||
                !assignment.Groups["delta"].Value.Equals(deltaParameter, StringComparison.Ordinal))
                continue;

            var variable = assignment.Groups["left"].Value;
            ignoredFrameLines.Add(i);
            var thresholdRegex = new Regex(
                @"\bif\s+" + Regex.Escape(variable) + @"\s*(?<op>>=|>|<=|<)\s*(?<threshold>[A-Za-z_]\w*|\d+(?:\.\d+)?)\s+then\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            for (var j = i; j < lines.Length; j++)
            {
                var threshold = thresholdRegex.Match(lines[j]);
                if (!threshold.Success)
                    continue;

                var rawThreshold = threshold.Groups["threshold"].Value;
                double? intervalMs = null;
                if (double.TryParse(rawThreshold, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    intervalMs = seconds * 1000.0;

                evidence.Add(new SourcePatternEvidence
                {
                    Pattern = "DELTA_ACCUMULATOR_GATE",
                    Line = callback.BodyStartLine + j,
                    Expression = $"{variable} {threshold.Groups["op"].Value} {rawThreshold}",
                    TransformSupported = true,
                    IntervalMs = intervalMs
                });
                supportedGateLines.Add(j);
                break;
            }
        }

        var clockAssignment = new Regex(
            @"^\s*(?<now>[A-Za-z_]\w*)\s*=\s*os\.clock\s*\(\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < lines.Length; i++)
        {
            var clock = clockAssignment.Match(lines[i]);
            if (!clock.Success)
                continue;

            ignoredFrameLines.Add(i);
            var now = clock.Groups["now"].Value;
            var window = string.Join("\n", lines.Skip(i).Take(10));
            var deadline = Regex.Match(
                window,
                @"if\s+" + Regex.Escape(now) + @"\s*(?:>|>=)\s*(?<deadline>[A-Za-z_]\w*)\s+then\s*\k<deadline>\s*=\s*" + Regex.Escape(now) + @"\s*\+\s*(?<interval>\d+(?:\.\d+)?|[A-Za-z_]\w*)\s+else\s+return\s+end",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!deadline.Success)
                continue;

            var intervalRaw = deadline.Groups["interval"].Value;
            double? intervalMs = null;
            if (double.TryParse(intervalRaw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                intervalMs = seconds * 1000.0;

            var gateLine = i;
            for (var j = i; j < Math.Min(lines.Length, i + 10); j++)
            {
                if (Regex.IsMatch(lines[j], @"\bif\s+" + Regex.Escape(now) + @"\b", RegexOptions.IgnoreCase))
                {
                    gateLine = j;
                    break;
                }
            }

            evidence.Add(new SourcePatternEvidence
            {
                Pattern = "CLOCK_DEADLINE_GATE",
                Line = callback.BodyStartLine + gateLine,
                Expression = $"os.clock deadline + {intervalRaw}",
                TransformSupported = true,
                IntervalMs = intervalMs
            });
            supportedGateLines.Add(gateLine);
        }

        var modulo = new Regex(
            @"(?<lhs>.+?)%\s*(?<divisor>\d+(?:\.\d+)?)\s*==\s*0",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        for (var i = 0; i < lines.Length; i++)
        {
            var match = modulo.Match(lines[i]);
            if (!match.Success)
                continue;

            double? intervalMs = null;
            if (double.TryParse(match.Groups["divisor"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var divisor) && divisor > 0 &&
                match.Groups["lhs"].Value.Contains("second", StringComparison.OrdinalIgnoreCase))
                intervalMs = divisor * 1000.0;

            evidence.Add(new SourcePatternEvidence
            {
                Pattern = "MODULO_POLL_HINT",
                Line = callback.BodyStartLine + i,
                Expression = match.Value.Trim(),
                TransformSupported = false,
                IntervalMs = intervalMs
            });
        }

        var frameCounter = new Regex(
            @"^\s*(?<left>[A-Za-z_]\w*)\s*=\s*(?<right>[A-Za-z_]\w*)\s*\+\s*1\s*$",
            RegexOptions.CultureInvariant);
        for (var i = 0; i < lines.Length; i++)
        {
            var increment = frameCounter.Match(lines[i]);
            if (!increment.Success ||
                !increment.Groups["left"].Value.Equals(increment.Groups["right"].Value, StringComparison.Ordinal))
                continue;

            var variable = increment.Groups["left"].Value;
            var threshold = new Regex(
                @"\bif\s+" + Regex.Escape(variable) + @"\s*(?:>=|>)\s*(?<count>\d+)\s+then\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var reset = new Regex(
                @"\b" + Regex.Escape(variable) + @"\s*=\s*0\b",
                RegexOptions.CultureInvariant);

            var window = string.Join("\n", lines.Skip(i).Take(30));
            var thresholdMatch = threshold.Match(window);
            if (!thresholdMatch.Success || !reset.IsMatch(window))
                continue;

            ignoredFrameLines.Add(i);
            var gateLine = i;
            for (var j = i; j < Math.Min(lines.Length, i + 30); j++)
            {
                if (threshold.IsMatch(lines[j]))
                {
                    gateLine = j;
                    break;
                }
            }

            evidence.Add(new SourcePatternEvidence
            {
                Pattern = "FRAME_COUNTER_GATE",
                Line = callback.BodyStartLine + gateLine,
                Expression = $"{variable} >= {thresholdMatch.Groups["count"].Value}",
                TransformSupported = true,
                IntervalMs = null
            });
            supportedGateLines.Add(gateLine);
        }

        var firstSupportedGate = supportedGateLines.Count > 0
            ? supportedGateLines.Min()
            : int.MaxValue;
        var meaningfulFrameWorkBeforeGate = false;
        if (firstSupportedGate != int.MaxValue)
        {
            for (var i = 0; i < firstSupportedGate; i++)
            {
                if (ignoredFrameLines.Contains(i))
                    continue;
                if (SourceIsMeaningfulFrameStatement(lines[i]))
                {
                    meaningfulFrameWorkBeforeGate = true;
                    break;
                }
            }
        }

        return new SourceCallbackAnalysis
        {
            Patterns = evidence,
            StructuralSignatureSha256 = SourceStructuralSignature(callback, evidence, meaningfulFrameWorkBeforeGate),
            RecipeSignatureSha256 = SourceRecipeSignature(evidence, meaningfulFrameWorkBeforeGate, SourceHasSimpleStateGate(lines)),
            HasSupportedCadenceGate = evidence.Any(x => x.TransformSupported),
            SupportedCadenceGateCount = evidence.Count(x => x.TransformSupported),
            HasUnsupportedCadenceHint = evidence.Any(x => !x.TransformSupported),
            HasMeaningfulFrameWorkBeforeCadenceGate = meaningfulFrameWorkBeforeGate,
            HasSimpleStateGate = SourceHasSimpleStateGate(lines),
            SourceIntervalCandidatesMs = evidence
                .Where(x => x.IntervalMs is > 0)
                .Select(x => Round(x.IntervalMs!.Value, 3))
                .Distinct()
                .OrderBy(x => x)
                .ToList()
        };
    }

    private static bool SourceIsMeaningfulFrameStatement(string rawLine)
    {
        var line = rawLine.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
            return false;
        if (line is "end" or "else" or "end)" || line.StartsWith("elseif ", StringComparison.Ordinal))
            return false;
        if (line.StartsWith("return", StringComparison.Ordinal))
            return false;
        if (Regex.IsMatch(line, @"^if\s+.*\s+then\s+return(?:\s+.*)?\s+end\s*$", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(line, @"^(if|for|while)\b.*\bthen\s*$", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(line, @"^(if|for|while)\b.*\bdo\s*$", RegexOptions.IgnoreCase))
            return false;
        if (Regex.IsMatch(line, @"^local\s+[A-Za-z_][\w,\s]*$"))
            return false;

        return Regex.IsMatch(line, @"[A-Za-z_][\w.:]*\s*\(") || line.Contains('=');
    }

    private static bool SourceHasSimpleStateGate(IReadOnlyList<string> lines)
    {
        var significant = lines
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith("--", StringComparison.Ordinal))
            .Take(20)
            .ToList();

        for (var i = 0; i < significant.Count; i++)
        {
            var line = significant[i];
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s+return(?:\s+.*)?\s+end\s*$", RegexOptions.IgnoreCase) &&
                !line.Contains("os.clock", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!Regex.IsMatch(line, @"^if\s+.*\s+then\s*$", RegexOptions.IgnoreCase))
                continue;

            for (var j = i + 1; j < Math.Min(significant.Count, i + 6); j++)
            {
                if (significant[j].StartsWith("return", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (significant[j].Equals("end", StringComparison.OrdinalIgnoreCase))
                    break;
            }
        }

        return false;
    }

    private static bool SourceTryFindFunctionEnd(
        string text,
        int scanStart,
        out int endStart,
        out int endEnd)
    {
        endStart = -1;
        endEnd = -1;
        var stack = new Stack<string>();
        stack.Push("function");
        var position = scanStart;

        while (SourceTryReadLuaToken(text, ref position, out var token, out var tokenStart, out var tokenEnd))
        {
            if (token.Equals("function", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push("function");
            }
            else if (token.Equals("if", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push("if");
            }
            else if (token.Equals("for", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push("for");
            }
            else if (token.Equals("while", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push("while");
            }
            else if (token.Equals("repeat", StringComparison.OrdinalIgnoreCase))
            {
                stack.Push("repeat");
            }
            else if (token.Equals("do", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0 || (stack.Peek() != "for" && stack.Peek() != "while"))
                    stack.Push("do");
            }
            else if (token.Equals("until", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count > 0 && stack.Peek() == "repeat")
                    stack.Pop();
            }
            else if (token.Equals("end", StringComparison.OrdinalIgnoreCase))
            {
                if (stack.Count == 0 || stack.Peek() == "repeat")
                    return false;

                stack.Pop();
                if (stack.Count == 0)
                {
                    endStart = tokenStart;
                    endEnd = tokenEnd;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SourceTryReadLuaToken(
        string text,
        ref int position,
        out string token,
        out int tokenStart,
        out int tokenEnd)
    {
        token = "";
        tokenStart = -1;
        tokenEnd = -1;

        while (position < text.Length)
        {
            var ch = text[position];
            if (char.IsWhiteSpace(ch))
            {
                position++;
                continue;
            }

            if (position + 1 < text.Length && text[position] == '-' && text[position + 1] == '-')
            {
                if (SourceTryLongBracket(text, position + 2, out _, out var longCloseEnd))
                {
                    position = longCloseEnd;
                    continue;
                }

                var newline = text.IndexOf('\n', position + 2);
                position = newline < 0 ? text.Length : newline + 1;
                continue;
            }

            if (ch is '\'' or '"')
            {
                var quote = ch;
                position++;
                while (position < text.Length)
                {
                    if (text[position] == '\\')
                    {
                        position = Math.Min(text.Length, position + 2);
                        continue;
                    }
                    if (text[position] == quote)
                    {
                        position++;
                        break;
                    }
                    position++;
                }
                continue;
            }

            if (ch == '[' && SourceTryLongBracket(text, position, out _, out var closeEnd))
            {
                position = closeEnd;
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                tokenStart = position;
                position++;
                while (position < text.Length &&
                       (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
                    position++;
                tokenEnd = position;
                token = text[tokenStart..tokenEnd];
                return true;
            }

            position++;
        }

        return false;
    }

    private static bool SourceTryLongBracket(
        string text,
        int start,
        out int openEnd,
        out int closeEnd)
    {
        openEnd = -1;
        closeEnd = -1;
        if (start >= text.Length || text[start] != '[')
            return false;

        var i = start + 1;
        while (i < text.Length && text[i] == '=')
            i++;
        if (i >= text.Length || text[i] != '[')
            return false;

        var equals = i - (start + 1);
        openEnd = i + 1;
        var close = "]" + new string('=', equals) + "]";
        var closeStart = text.IndexOf(close, openEnd, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            closeEnd = text.Length;
            return true;
        }

        closeEnd = closeStart + close.Length;
        return true;
    }

    private static int SourceLineNumber(string text, int position)
    {
        var line = 1;
        var limit = Math.Clamp(position, 0, text.Length);
        for (var i = 0; i < limit; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static string SourceSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string SourceHashText(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string SourceStructuralSignature(
        SourceCallbackMatch callback,
        IReadOnlyList<SourcePatternEvidence> patterns,
        bool meaningfulFrameWorkBeforeGate)
    {
        var normalizedParameters = string.Join(",",
            callback.Parameters
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select((_, index) => $"arg{index}"));

        var bodyShape = string.Join("\n",
            callback.Body
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n')
                .Select(line => Regex.Replace(line, @"--.*$", ""))
                .Select(line => Regex.Replace(line, @"\s+", " ").Trim())
                .Where(line => line.Length > 0));

        var patternShape = string.Join("|",
            patterns
                .OrderBy(x => x.Line)
                .Select(x => $"{x.Pattern}:{x.TransformSupported}:{x.IntervalMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "dynamic"}"));

        return SourceHashText(
            $"registrar={callback.Registrar.ToLowerInvariant()}\n" +
            $"parameters={normalizedParameters}\n" +
            $"frameBeforeGate={meaningfulFrameWorkBeforeGate}\n" +
            $"patterns={patternShape}\n" +
            $"body={bodyShape}");
    }

    private static string SourceRecipeSignature(
        IReadOnlyList<SourcePatternEvidence> patterns,
        bool meaningfulFrameWorkBeforeGate,
        bool simpleStateGate)
    {
        var recipe = string.Join("|",
            patterns
                .OrderBy(x => x.Pattern, StringComparer.Ordinal)
                .ThenBy(x => x.IntervalMs)
                .Select(x => $"{x.Pattern}:{x.TransformSupported}:{x.IntervalMs?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "dynamic"}"));

        return SourceHashText(
            $"frameBeforeGate={meaningfulFrameWorkBeforeGate};" +
            $"stateGate={simpleStateGate};" +
            $"recipes={recipe}");
    }

    private static bool SourceTryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string SourceJsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (SourceTryProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        return "";
    }

    private static bool SourceJsonBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (SourceTryProperty(element, name, out var value) &&
                value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();
        }
        return false;
    }

    private static bool SourceJsonNestedBool(JsonElement root, string objectName, string propertyName)
    {
        return SourceTryProperty(root, objectName, out var obj) &&
               SourceJsonBool(obj, propertyName);
    }

    private static double SourceJsonDouble(JsonElement element, string first, string second, double fallback = 0)
    {
        foreach (var name in new[] { first, second })
        {
            if (SourceTryProperty(element, name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetDouble(out var number))
                return number;
        }
        return fallback;
    }

    private static double SourceJsonNestedDouble(JsonElement root, string objectName, string propertyName, double fallback)
    {
        if (!SourceTryProperty(root, objectName, out var obj))
            return fallback;
        return SourceJsonDouble(obj, propertyName, propertyName, fallback);
    }

    private static double? SourceJsonNullableDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!SourceTryProperty(element, name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.Null)
                return null;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                return number;
        }
        return null;
    }

    private sealed class SourceModFolder
    {
        public string Name { get; init; } = "";
        public string Path { get; init; } = "";
        public string NormalizedName { get; init; } = "";
    }

    private sealed class SourceDiscovery
    {
        public int ScannedLuaFiles { get; set; }
        public int ParseErrors { get; set; }
        public List<SourceCallbackMatch> Callbacks { get; } = [];
    }

    private sealed class SourceCallbackMatch
    {
        public string Registrar { get; init; } = "";
        public string Parameters { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string FileSha256 { get; init; } = "";
        public string CallbackBodySha256 { get; init; } = "";
        public int RegistrationLine { get; init; }
        public int BodyStartLine { get; init; }
        public int BodyEndLine { get; init; }
        public string Body { get; init; } = "";
    }

    private sealed class SourcePatternEvidence
    {
        public string Pattern { get; init; } = "";
        public int Line { get; init; }
        public string Expression { get; init; } = "";
        public bool TransformSupported { get; init; }
        public double? IntervalMs { get; init; }
    }

    private sealed class SourceCallbackAnalysis
    {
        public List<SourcePatternEvidence> Patterns { get; init; } = [];
        public bool HasSupportedCadenceGate { get; init; }
        public int SupportedCadenceGateCount { get; init; }
        public bool HasUnsupportedCadenceHint { get; init; }
        public bool HasMeaningfulFrameWorkBeforeCadenceGate { get; init; }
        public bool HasSimpleStateGate { get; init; }
        public string StructuralSignatureSha256 { get; init; } = "";
        public string RecipeSignatureSha256 { get; init; } = "";
        public List<double> SourceIntervalCandidatesMs { get; init; } = [];
    }

    private sealed class SourceRuntimeEvidence
    {
        public string Classification { get; init; } = "";
        public string Recommendation { get; init; } = "";
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double GlobalWorkSharePct { get; init; }
        public double FamilyWorkSharePct { get; init; }
        public double OwnerWorkSharePct { get; init; }
        public double ScenarioCostRatio { get; init; }
        public double? ResolvedIntervalMs { get; init; }
        public double? CadenceSupportPct { get; init; }
        public bool ExactTimelineUsable { get; init; }
    }

    private sealed class SourceMatchEvidence
    {
        public bool SourceMatched { get; init; }
        public string ModFolder { get; init; } = "";
        public int ScannedLuaFiles { get; init; }
        public int DirectOnUpdateRegistrations { get; init; }
        public int ParseErrors { get; init; }
        public string File { get; init; } = "";
        public string FileSha256 { get; init; } = "";
        public string CallbackBodySha256 { get; init; } = "";
        public string StructuralSignatureSha256 { get; init; } = "";
        public string RecipeSignatureSha256 { get; init; } = "";
        public string CompatibilityBasis { get; init; } = "";
        public string Registrar { get; init; } = "";
        public int RegistrationLine { get; init; }
        public int CallbackBodyStartLine { get; init; }
        public int CallbackBodyEndLine { get; init; }
        public bool SimpleStateGate { get; init; }
        public bool MeaningfulFrameWorkBeforeCadenceGate { get; init; }
        public IReadOnlyList<double> SourceIntervalCandidatesMs { get; init; } = Array.Empty<double>();
        public IReadOnlyList<SourcePatternEvidence> Patterns { get; init; } = Array.Empty<SourcePatternEvidence>();

        public static SourceMatchEvidence Empty() => new();

        public static SourceMatchEvidence FromDiscovery(SourceModFolder folder, SourceDiscovery discovery) => new()
        {
            SourceMatched = true,
            ModFolder = folder.Name,
            ScannedLuaFiles = discovery.ScannedLuaFiles,
            DirectOnUpdateRegistrations = discovery.Callbacks.Count,
            ParseErrors = discovery.ParseErrors
        };

        public static SourceMatchEvidence FromAnalysis(
            SourceModFolder folder,
            SourceDiscovery discovery,
            SourceCallbackMatch callback,
            SourceCallbackAnalysis analysis) => new()
        {
            SourceMatched = true,
            ModFolder = folder.Name,
            ScannedLuaFiles = discovery.ScannedLuaFiles,
            DirectOnUpdateRegistrations = discovery.Callbacks.Count,
            ParseErrors = discovery.ParseErrors,
            File = callback.RelativeFile,
            FileSha256 = callback.FileSha256,
            CallbackBodySha256 = callback.CallbackBodySha256,
            StructuralSignatureSha256 = analysis.StructuralSignatureSha256,
            RecipeSignatureSha256 = analysis.RecipeSignatureSha256,
            CompatibilityBasis = "REVALIDATE_CURRENT_RECIPE_STRUCTURE",
            Registrar = callback.Registrar,
            RegistrationLine = callback.RegistrationLine,
            CallbackBodyStartLine = callback.BodyStartLine,
            CallbackBodyEndLine = callback.BodyEndLine,
            SimpleStateGate = analysis.HasSimpleStateGate,
            MeaningfulFrameWorkBeforeCadenceGate = analysis.HasMeaningfulFrameWorkBeforeCadenceGate,
            SourceIntervalCandidatesMs = analysis.SourceIntervalCandidatesMs,
            Patterns = analysis.Patterns
        };
    }

    private static SourceImportanceEvidence SourceReadImportance(
        string captureRoot,
        string owner,
        string kind,
        string target)
    {
        var path = Path.Combine(captureRoot, ResolverInputFileName);
        if (!File.Exists(path))
            return new SourceImportanceEvidence();

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!SourceTryProperty(document.RootElement, "callbacks", out var callbacks) ||
                callbacks.ValueKind != JsonValueKind.Array)
                return new SourceImportanceEvidence();

            foreach (var row in callbacks.EnumerateArray())
            {
                if (!SourceJsonString(row, "owner", "Owner").Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                    !SourceJsonString(row, "kind", "Kind").Equals(kind, StringComparison.OrdinalIgnoreCase) ||
                    !SourceJsonString(row, "target", "Target").Equals(target, StringComparison.OrdinalIgnoreCase))
                    continue;

                return new SourceImportanceEvidence
                {
                    GlobalWorkSharePct = SourceJsonDouble(row, "globalWorkSharePct", "GlobalWorkSharePct"),
                    FamilyWorkSharePct = SourceJsonDouble(row, "familyWorkSharePct", "FamilyWorkSharePct"),
                    OwnerWorkSharePct = SourceJsonDouble(row, "ownerWorkSharePct", "OwnerWorkSharePct")
                };
            }
        }
        catch
        {
        }

        return new SourceImportanceEvidence();
    }

    private sealed class SourceImportanceEvidence
    {
        public double GlobalWorkSharePct { get; init; }
        public double FamilyWorkSharePct { get; init; }
        public double OwnerWorkSharePct { get; init; }
    }

    private sealed class SourceCadenceDecision
    {
        public int PriorityRank { get; set; }
        public double CumulativeFamilyWorkSharePct { get; set; }
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string Group { get; init; } = SourceGroupLeaveAlone;
        public bool TransformCandidate { get; init; }
        public double Confidence { get; init; }
        public string Reason { get; init; } = "";
        public SourceRuntimeEvidence Runtime { get; init; } = new();
        public SourceMatchEvidence Source { get; init; } = SourceMatchEvidence.Empty();
    }
}
