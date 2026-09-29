using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record CallbackResolverDocumentResult(
    object Document,
    int FamilyCount,
    int RankedCallbackCount,
    int GenericResolvedCount,
    int RegistryHintCount,
    int UnresolvedCount);

internal static class CallbackResolverService
{
    private const int TopConsumersPerFamily = 10;
    private const int TopFamilies = 12;

    private static readonly Regex NormalizeNonAlphaNumeric = new(
        @"[^a-z0-9]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static CallbackResolverDocumentResult Build(
        string handoffPath,
        string modsRoot,
        string? cadenceFinalPath,
        string? exceptionRegistryPath)
    {
        using var handoff = JsonDocument.Parse(File.ReadAllText(handoffPath));
        var callbacks = ReadCallbacks(handoff.RootElement)
            .Where(x => !x.Infrastructure && x.ExclusiveMsPerSecond > 0)
            .OrderByDescending(x => x.ExclusiveMsPerSecond)
            .ThenByDescending(x => x.CallsPerSecond)
            .ToList();

        var cadence = ReadCadenceDecisions(cadenceFinalPath);
        var registry = ExceptionRegistry.Load(exceptionRegistryPath);
        var sourceIndex = new LiveSourceIndex(modsRoot);

        var familyGroups = callbacks
            .GroupBy(x => FamilyKey(x.Kind, x.Target), StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Key = g.Key,
                Rows = g.OrderByDescending(x => x.ExclusiveMsPerSecond)
                    .ThenByDescending(x => x.CallsPerSecond)
                    .ToList(),
                Work = g.Sum(x => x.ExclusiveMsPerSecond),
                Calls = g.Sum(x => x.CallsPerSecond)
            })
            .OrderByDescending(x => x.Work)
            .ThenByDescending(x => x.Calls)
            .Take(TopFamilies)
            .ToList();

        var familyDocuments = new List<object>();
        var rankedCount = 0;
        var genericResolved = 0;
        var registryHints = 0;
        var unresolved = 0;

        foreach (var family in familyGroups)
        {
            var first = family.Rows[0];
            var consumers = new List<object>();

            foreach (var callback in family.Rows.Take(TopConsumersPerFamily))
            {
                rankedCount++;
                var generic = ResolveGeneric(callback, sourceIndex, cadence);
                ExceptionRegistryEntry? hint = null;

                // Registry is deliberately a last resort. Generic source/runtime
                // recognition always gets first attempt.
                if (!generic.Automatable)
                    hint = registry.Match(callback);

                if (generic.Automatable)
                    genericResolved++;
                else if (hint is not null)
                    registryHints++;
                else
                    unresolved++;

                consumers.Add(new
                {
                    callback.registrationId,
                    callback.owner,
                    callback.kind,
                    callback.target,
                    runtime = new
                    {
                        callback.callsPerSecond,
                        callback.exclusiveMsPerSecond,
                        callback.globalWorkSharePct,
                        callback.familyWorkSharePct,
                        callback.avgExclusiveUs,
                        callback.maxExclusiveMs,
                        callback.spikeCount,
                        callback.maxSpikeExclusiveMs
                    },
                    source = generic.Source,
                    generic = new
                    {
                        generic.Status,
                        generic.Automatable,
                        generic.Pattern,
                        generic.RecipeFamilies,
                        generic.Facts,
                        generic.Evidence,
                        generic.Blockers
                    },
                    registry = new
                    {
                        checkedAfterGenericExhausted = !generic.Automatable,
                        matched = hint is not null,
                        entryId = hint?.Id,
                        category = hint?.Category,
                        semanticHints = hint?.SemanticHints ?? Array.Empty<string>(),
                        suggestedRecipeFamilies = hint?.SuggestedRecipeFamilies ?? Array.Empty<string>(),
                        note = hint is null
                            ? "No curated exception knowledge was used."
                            : "Registry supplies semantic hints only. Current deployed source must still be analyzed; registry entries never contain or authorize patch code."
                    },
                    disposition = generic.Automatable
                        ? "GENERIC_PATTERN"
                        : hint is not null
                            ? "SPECIAL_HINT_AVAILABLE"
                            : "UNRESOLVED"
                });
            }

            familyDocuments.Add(new
            {
                kind = first.Kind,
                target = first.Target,
                exclusiveMsPerSecond = Round(family.Work),
                callsPerSecond = Round(family.Calls),
                consumerCount = family.Rows.Count,
                resolverFamily = ClassifyCallbackFamily(first.Kind, first.Target),
                topConsumers = consumers
            });
        }

        var globalTop = callbacks
            .Take(10)
            .Select((x, index) => new
            {
                rank = index + 1,
                x.registrationId,
                x.owner,
                x.kind,
                x.target,
                x.exclusiveMsPerSecond,
                x.globalWorkSharePct
            })
            .ToArray();

        var document = new
        {
            schemaVersion = "0.1",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            interop = new
            {
                producer = "G-CET-Resolver",
                input = Path.GetFileName(handoffPath),
                domain = "cet/callbacks"
            },
            policy = new
            {
                callbackOriented = true,
                familyFirst = true,
                topFamilies = TopFamilies,
                topConsumersPerFamily = TopConsumersPerFamily,
                genericPatternsBeforeRegistry = true,
                registryContainsPatchCode = false,
                registryCanAuthorizeRewrite = false,
                cadenceIsSubset = true,
                liveSourcesReadOnly = true,
                note = "Resolve proven callback/source patterns first. Only unresolved high-impact consumers are checked against the small curated exception registry. Registry knowledge is semantic guidance, never patch code."
            },
            cadence = new
            {
                available = !string.IsNullOrWhiteSpace(cadenceFinalPath) && File.Exists(cadenceFinalPath),
                sourceConfirmedOutput = cadenceFinalPath is null ? null : Path.GetFileName(cadenceFinalPath)
            },
            registry = new
            {
                path = exceptionRegistryPath,
                loaded = registry.Loaded,
                entryCount = registry.Entries.Count,
                policy = "Exceptional high-impact semantic hints only. If a generic recognizer can solve the structure, no registry entry should exist."
            },
            summary = new
            {
                familyCount = familyGroups.Count,
                rankedCallbackCount = rankedCount,
                genericResolved,
                registryHints,
                unresolved
            },
            globalTopCallbacks = globalTop,
            callbackFamilies = familyDocuments
        };

        return new CallbackResolverDocumentResult(
            document,
            familyGroups.Count,
            rankedCount,
            genericResolved,
            registryHints,
            unresolved);
    }

    private static GenericResolution ResolveGeneric(
        CallbackMetric callback,
        LiveSourceIndex sourceIndex,
        IReadOnlyDictionary<string, CadenceDecision> cadence)
    {
        var family = ClassifyCallbackFamily(callback.Kind, callback.Target);
        var source = sourceIndex.Resolve(callback);
        var sourceEvidence = source is null
            ? null
            : new SourceEvidence
            {
                RelativeFile = source.RelativeFile,
                Sha256 = source.Sha256,
                LineStart = source.LineStart,
                LineEnd = source.LineEnd,
                MatchMode = source.MatchMode
            };

        if (family == "ONACTION")
            return ResolveOnAction(callback, source, sourceEvidence, sourceIndex);

        if (family == "ONUPDATE")
            return ResolveOnUpdate(callback, source, sourceEvidence, cadence);

        return new GenericResolution
        {
            Status = "NO_GENERIC_RESOLVER",
            Automatable = false,
            Pattern = "UNSUPPORTED_CALLBACK_FAMILY",
            RecipeFamilies = Array.Empty<string>(),
            Evidence = Array.Empty<string>(),
            Blockers = new[] { "No proven generic recipe has been implemented for this callback family yet." },
            Source = sourceEvidence
        };
    }

    private static GenericResolution ResolveOnUpdate(
        CallbackMetric callback,
        ResolvedSource? source,
        SourceEvidence? sourceEvidence,
        IReadOnlyDictionary<string, CadenceDecision> cadence)
    {
        var recipes = new List<string>();
        var evidence = new List<string>();
        var blockers = new List<string>();
        object? facts = null;

        var directOnUpdate = source is not null &&
            Regex.IsMatch(
                source.CallbackText,
                @"\b(?:registerForEvent|registerRuntimeEvent)\s*\(\s*['""]onUpdate['""]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (directOnUpdate)
        {
            recipes.Add("FRAME_DISPATCH_CONSOLIDATION");
            evidence.Add("Direct onUpdate registration is present in the current deployed source.");
        }
        else
        {
            blockers.Add("Current deployed source could not prove the direct onUpdate registration.");
        }

        // Highest-confidence cadence recipe: the callback itself contains only
        // author-written fixed timer accumulators and their gated work. Runtime
        // data decides whether eliminating the frame-rate entry is materially
        // useful; source decides the cadence. No owner/mod-name knowledge is
        // consulted.
        if (source is not null &&
            directOnUpdate &&
            TryResolveWholeCallbackAuthorCadence(
                source,
                callback,
                out var authorCadence,
                out var cadenceBlocker))
        {
            recipes.Add("AUTHOR_CADENCE_WHOLE_CALLBACK");
            evidence.Add(
                $"Current source proves whole-callback author cadence at {authorCadence.BaseIntervalSeconds:0.######} s " +
                $"with {authorCadence.TimerIntervalsSeconds.Length} fixed author timer(s).");
            evidence.Add(
                $"Measured callback entry rate is {callback.CallsPerSecond:0.###}/s versus " +
                $"{authorCadence.ExpectedCallsPerSecond:0.###}/s at the preserved author base cadence.");
            facts = new
            {
                authorCadenceWholeCallback = true,
                baseIntervalSeconds = authorCadence.BaseIntervalSeconds,
                timerIntervalsSeconds = authorCadence.TimerIntervalsSeconds,
                accumulatorVariables = authorCadence.AccumulatorVariables,
                expectedCallsPerSecond = authorCadence.ExpectedCallsPerSecond,
                runtimeEntryReductionFactor = authorCadence.RuntimeEntryReductionFactor,
                deltaParameter = authorCadence.DeltaParameter
            };
        }
        else if (!string.IsNullOrWhiteSpace(cadenceBlocker))
        {
            blockers.Add(cadenceBlocker);
        }

        // Keep the broader cadence classifier visible as evidence, but do not
        // let an inferred cadence authorize generation. Only finite generator
        // recipes above are automatable.
        var cadenceKey = CadenceKey(callback.Owner, callback.Kind, callback.Target);
        if (cadence.TryGetValue(cadenceKey, out var cadenceDecision) &&
            cadenceDecision.TransformCandidate &&
            !cadenceDecision.Group.Equals("LEAVE_ALONE", StringComparison.OrdinalIgnoreCase))
        {
            evidence.Add($"Cadence subset source-classified {cadenceDecision.Group}; generation still requires a finite author-cadence recipe.");
        }

        var automatable = recipes.Contains(
            "AUTHOR_CADENCE_WHOLE_CALLBACK",
            StringComparer.OrdinalIgnoreCase) ||
            recipes.Contains(
                "FRAME_DISPATCH_CONSOLIDATION",
                StringComparer.OrdinalIgnoreCase);

        var pattern = recipes.Contains(
                "AUTHOR_CADENCE_WHOLE_CALLBACK",
                StringComparer.OrdinalIgnoreCase)
            ? "AUTHOR_CADENCE_WHOLE_CALLBACK"
            : recipes.FirstOrDefault() ?? "ONUPDATE_UNRESOLVED";

        return new GenericResolution
        {
            Status = recipes.Count > 0 ? "RESOLVED" : "SOURCE_UNRESOLVED",
            Automatable = automatable,
            Pattern = pattern,
            RecipeFamilies = recipes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Facts = facts,
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = sourceEvidence
        };
    }

    private static bool TryResolveWholeCallbackAuthorCadence(
        ResolvedSource source,
        CallbackMetric callback,
        out AuthorCadenceResolution resolution,
        out string blocker)
    {
        resolution = new AuthorCadenceResolution();
        blocker = "";

        var match = Regex.Match(
            source.CallbackText,
            @"(?s)^\s*(?:registerForEvent|registerRuntimeEvent)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*(?<delta>[A-Za-z_]\w*)\s*\)\s*(?<body>.*)\bend\s*\)\s*;?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            blocker = "Author cadence: exact single-parameter onUpdate callback shape was not proven.";
            return false;
        }

        var deltaParameter = match.Groups["delta"].Value;
        var body = match.Groups["body"].Value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');
        var lines = body.Split('\n');

        var significant = lines
            .Select((text, index) => new
            {
                Text = text,
                Trimmed = text.Trim(),
                Index = index,
                Indent = text.Length - text.TrimStart().Length
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Trimmed) &&
                        !x.Trimmed.StartsWith("--", StringComparison.Ordinal))
            .ToList();

        if (significant.Count == 0)
        {
            blocker = "Author cadence: callback body is empty.";
            return false;
        }

        var baseIndent = significant.Min(x => x.Indent);
        var increments = new Dictionary<string, int>(StringComparer.Ordinal);
        var gates = new Dictionary<string, double>(StringComparer.Ordinal);
        var gateRanges = new List<(int Start, int End)>();

        var escapedDelta = Regex.Escape(deltaParameter);
        var incrementRegex = new Regex(
            @"^\s*(?<var>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:" +
            @"=\s*\k<var>\s*\+\s*" + escapedDelta +
            @"|\+=\s*" + escapedDelta + @")\s*;?\s*$",
            RegexOptions.CultureInvariant);
        var gateRegex = new Regex(
            @"^\s*if\s+(?<var>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*(?:>=|>)\s*(?<seconds>\d+(?:\.\d+)?)\s+then\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(trimmed) ||
                trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;

            var indent = lines[i].Length - lines[i].TrimStart().Length;
            if (indent != baseIndent)
                continue;

            var increment = incrementRegex.Match(lines[i]);
            if (increment.Success)
            {
                var variable = increment.Groups["var"].Value;
                increments[variable] = i;
                continue;
            }

            var gate = gateRegex.Match(lines[i]);
            if (!gate.Success)
            {
                blocker = $"Author cadence: top-level frame work remains outside author timer gates (line {i + 1}).";
                return false;
            }

            var variable = gate.Groups["var"].Value;
            if (!double.TryParse(
                    gate.Groups["seconds"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var seconds) ||
                seconds <= 0 ||
                !double.IsFinite(seconds))
            {
                blocker = "Author cadence: timer threshold is not a positive fixed author rate.";
                return false;
            }

            var end = FindSameIndentEnd(lines, i, baseIndent);
            if (end <= i)
            {
                blocker = "Author cadence: timer gate block boundary could not be proven.";
                return false;
            }

            var block = string.Join("\n", lines.Skip(i + 1).Take(end - i - 1));
            if (!Regex.IsMatch(
                    block,
                    @"\b" + Regex.Escape(variable) + @"\s*=\s*0(?:\.0+)?\b",
                    RegexOptions.CultureInvariant))
            {
                blocker = $"Author cadence: timer '{variable}' is not reset inside its author gate.";
                return false;
            }

            gates[variable] = seconds;
            gateRanges.Add((i, end));
            i = end;
        }

        if (gates.Count == 0 || increments.Count == 0)
        {
            blocker = "Author cadence: no complete fixed accumulator timer was proven.";
            return false;
        }

        if (increments.Keys.Except(gates.Keys, StringComparer.Ordinal).Any() ||
            gates.Keys.Except(increments.Keys, StringComparer.Ordinal).Any())
        {
            blocker = "Author cadence: timer increments and author gates do not form a closed set.";
            return false;
        }

        // The callback may use delta only to advance the proven accumulators.
        for (var i = 0; i < lines.Length; i++)
        {
            if (!Regex.IsMatch(lines[i], @"\b" + escapedDelta + @"\b"))
                continue;
            if (incrementRegex.IsMatch(lines[i]))
                continue;

            blocker = "Author cadence: delta is consumed by work other than the proven timer accumulators.";
            return false;
        }

        // Timer state must be private to the cadence mechanism. This avoids
        // changing externally observed continuously-increasing timer values.
        var callbackIndex = source.FullText.IndexOf(source.CallbackText, StringComparison.Ordinal);
        var outside = callbackIndex >= 0
            ? source.FullText.Remove(callbackIndex, source.CallbackText.Length)
            : source.FullText;

        foreach (var variable in gates.Keys)
        {
            if (!HasZeroAuthorTimerInitializer(outside, variable))
            {
                blocker = $"Author cadence: zero initialization for timer '{variable}' was not proven.";
                return false;
            }

            if (HasExternalTimerReference(outside, variable))
            {
                blocker = $"Author cadence: timer '{variable}' is referenced outside the callback.";
                return false;
            }
        }

        var intervalsUs = gates.Values
            .Select(x => checked((long)Math.Round(x * 1_000_000.0, MidpointRounding.AwayFromZero)))
            .Where(x => x > 0)
            .ToArray();
        if (intervalsUs.Length != gates.Count)
        {
            blocker = "Author cadence: one or more author rates could not be represented safely.";
            return false;
        }

        var baseUs = intervalsUs.Aggregate(GreatestCommonDivisor);
        if (baseUs <= 0)
        {
            blocker = "Author cadence: common author cadence could not be resolved.";
            return false;
        }

        var baseSeconds = baseUs / 1_000_000.0;
        var expectedCallsPerSecond = 1.0 / baseSeconds;
        var reductionFactor = expectedCallsPerSecond > 0
            ? callback.CallsPerSecond / expectedCallsPerSecond
            : 0;

        // Runtime decides whether this structurally safe recipe is worthwhile.
        // This is relative to the measured callback rate, not a mod identity or
        // a fixed millisecond cost threshold.
        if (reductionFactor < 2.0)
        {
            blocker =
                $"Author cadence is source-proven, but measured entry reduction would be only {reductionFactor:0.##}x.";
            return false;
        }

        resolution = new AuthorCadenceResolution
        {
            DeltaParameter = deltaParameter,
            BaseIntervalSeconds = baseSeconds,
            TimerIntervalsSeconds = gates.Values
                .OrderBy(x => x)
                .ToArray(),
            AccumulatorVariables = gates.Keys
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            ExpectedCallsPerSecond = expectedCallsPerSecond,
            RuntimeEntryReductionFactor = reductionFactor
        };
        return true;
    }

    private static int FindSameIndentEnd(
        IReadOnlyList<string> lines,
        int start,
        int indent)
    {
        for (var i = start + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!Regex.IsMatch(
                    trimmed,
                    @"^end\s*;?\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            var candidateIndent = lines[i].Length - lines[i].TrimStart().Length;
            if (candidateIndent == indent)
                return i;
        }

        return -1;
    }

    private static long GreatestCommonDivisor(long left, long right)
    {
        left = Math.Abs(left);
        right = Math.Abs(right);
        while (right != 0)
        {
            var t = left % right;
            left = right;
            right = t;
        }
        return left;
    }

    private static bool HasZeroAuthorTimerInitializer(
        string outside,
        string variable)
    {
        if (!variable.Contains('.', StringComparison.Ordinal))
        {
            return Regex.IsMatch(
                outside,
                @"\b(?:local\s+)?" + Regex.Escape(variable) +
                @"\s*=\s*0(?:\.0+)?\b",
                RegexOptions.CultureInvariant);
        }

        if (Regex.IsMatch(
                outside,
                @"\b" + Regex.Escape(variable) + @"\s*=\s*0(?:\.0+)?\b",
                RegexOptions.CultureInvariant))
            return true;

        var pieces = variable.Split('.');
        if (pieces.Length != 2)
            return false;

        var tableName = pieces[0];
        var fieldName = pieces[1];
        var declaration = Regex.Match(
            outside,
            @"\b(?:local\s+)?" + Regex.Escape(tableName) + @"\s*=\s*\{",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return false;

        var open = outside.IndexOf('{', declaration.Index);
        if (open < 0)
            return false;

        var close = FindBalancedBraceEnd(outside, open);
        if (close <= open)
            return false;

        var body = outside.Substring(open + 1, close - open - 1);
        return Regex.IsMatch(
            body,
            @"\b" + Regex.Escape(fieldName) + @"\s*=\s*0(?:\.0+)?\b",
            RegexOptions.CultureInvariant);
    }

    private static bool HasExternalTimerReference(
        string outside,
        string variable)
    {
        if (variable.Contains('.', StringComparison.Ordinal))
        {
            if (Regex.IsMatch(
                    outside,
                    @"\b" + Regex.Escape(variable) + @"\b",
                    RegexOptions.CultureInvariant))
                return true;

            var pieces = variable.Split('.');
            if (pieces.Length == 2 &&
                Regex.IsMatch(
                    outside,
                    @"\b" + Regex.Escape(pieces[0]) +
                    @"\s*\[\s*['""]" + Regex.Escape(pieces[1]) + @"['""]\s*\]",
                    RegexOptions.CultureInvariant))
                return true;

            return false;
        }

        var withoutInitializer = Regex.Replace(
            outside,
            @"\b(?:local\s+)?" + Regex.Escape(variable) +
            @"\s*=\s*0(?:\.0+)?\b",
            "",
            1,
            RegexOptions.CultureInvariant);

        return Regex.IsMatch(
            withoutInitializer,
            @"\b" + Regex.Escape(variable) + @"\b",
            RegexOptions.CultureInvariant);
    }

    private static int FindBalancedBraceEnd(string text, int open)
    {
        var depth = 0;
        var quote = '\0';
        var escaped = false;

        for (var i = open; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return i;
            }
        }

        return -1;
    }

    private static GenericResolution ResolveOnAction(
        CallbackMetric callback,
        ResolvedSource? source,
        SourceEvidence? sourceEvidence,
        LiveSourceIndex sourceIndex)
    {
        if (source is null)
        {
            return new GenericResolution
            {
                Status = "SOURCE_UNRESOLVED",
                Automatable = false,
                Pattern = "ONACTION_SOURCE_UNRESOLVED",
                RecipeFamilies = Array.Empty<string>(),
                Facts = null,
                Evidence = Array.Empty<string>(),
                Blockers = new[] { "The current deployed OnAction callback source could not be mapped uniquely." },
                Source = sourceEvidence
            };
        }

        var window = source.CallbackText;
        var full = source.FullText;
        var evidence = new List<string>();
        var blockers = new List<string>();
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolvedActionSelectors = new HashSet<string>(StringComparer.Ordinal);

        var consumerMutation =
            Regex.IsMatch(window, @"\bconsumer\s*[:.]\s*Consume(?:SingleAction)?\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(window, @"\bListenerActionConsumer\s*\.\s*Consume\s*\(", RegexOptions.IgnoreCase);
        if (consumerMutation)
            evidence.Add("The callback mutates the original input consumer; generated routing must preserve the same consumer object and callback ordering.");

        // Name decoding is structurally equivalent whether the callback uses
        // action:GetName(), ListenerAction.GetName(action), a singleton receiver,
        // or another wrapper. The assigned variable is what later filters use.
        var nameVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     window,
                     @"(?m)\b(?<var>[A-Za-z_]\w*)\s*=\s*[^\r\n;]*\bGetName\s*\(",
                     RegexOptions.CultureInvariant))
        {
            nameVars.Add(match.Groups["var"].Value);
        }

        // Literal IsAction forms, including CET's common
        // action:IsAction(action, "Name") shape.
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bIsAction\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?['""](?<action>[^'""]+)['""]\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
        }

        // Variable IsAction selectors are accepted only when all values assigned
        // to the selector in current source are finite string/CName literals.
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bIsAction\s*\(\s*(?:[A-Za-z_]\w*\s*,\s*)?(?<selector>[A-Za-z_]\w*)\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var selector = match.Groups["selector"].Value;
            var values = ReadFiniteStringValues(full, selector);
            if (values.Count == 0)
            {
                unresolvedActionSelectors.Add(selector);
                continue;
            }

            foreach (var value in values)
                actions.Add(value);
            evidence.Add($"Finite action selector '{selector}' resolved to {values.Count} literal value(s).");
        }

        // Direct CName/string comparisons against GetName().
        foreach (Match match in Regex.Matches(
                     window,
                     @"\bGetName\s*\(\s*\)\s*==\s*(?:CName\.new\s*\(\s*)?['""](?<action>[^'""]+)['""]\s*\)?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
        }

        // Early-return inverse comparison: if action:GetName() ~= turnX then return.
        foreach (Match match in Regex.Matches(
                     window,
                     @"(?m)^\s*if\s+[^\r\n]*\bGetName\s*\(\s*\)\s*~=\s*(?<selector>[A-Za-z_]\w*)\s+then\s+return",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var selector = match.Groups["selector"].Value;
            var values = ReadFiniteStringValues(full, selector);
            if (values.Count == 0)
                unresolvedActionSelectors.Add(selector);
            else
                foreach (var value in values) actions.Add(value);
        }

        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"\b" + Regex.Escape(variable) + @"\s*==\s*['""](?<action>[^'""]+)['""]",
                         RegexOptions.CultureInvariant))
            {
                actions.Add(match.Groups["action"].Value);
            }

            foreach (Match match in Regex.Matches(
                         window,
                         @"(?m)^\s*if\s+" + Regex.Escape(variable) + @"\s*~=\s*['""](?<action>[^'""]+)['""]\s+then\s+return",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                actions.Add(match.Groups["action"].Value);
            }
        }

        var staticTables = new List<string>();
        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"\b(?<table>[A-Za-z_]\w*)\s*\[\s*" + Regex.Escape(variable) + @"\s*\]",
                         RegexOptions.CultureInvariant))
            {
                var table = match.Groups["table"].Value;
                if (TryReadStaticStringSet(full, table, out var tableActions, out var dynamicWrites))
                {
                    foreach (var action in tableActions)
                        actions.Add(action);
                    staticTables.Add(table);
                    evidence.Add($"Static action table '{table}' resolved with {tableActions.Count} entries.");
                    if (dynamicWrites)
                        blockers.Add($"Action table '{table}' has writes outside its literal definition.");
                }
            }
        }

        foreach (var variable in nameVars)
        {
            foreach (Match match in Regex.Matches(
                         window,
                         @"(?:string\.find\s*\(\s*" + Regex.Escape(variable) + @"\s*,\s*['""](?<p>[^'""]+)['""]|" +
                         Regex.Escape(variable) + @"\s*:\s*find\s*\(\s*['""](?<p2>[^'""]+)['""])",
                         RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                var value = match.Groups["p"].Success
                    ? match.Groups["p"].Value
                    : match.Groups["p2"].Value;
                if (!string.IsNullOrWhiteSpace(value))
                    patterns.Add(value);
            }
        }

        var stateGated = Regex.IsMatch(
            window,
            @"\bif\s+(?:not\s+)?[A-Za-z_][\w.]*\s+then\s+(?:return|[^\n]*\n\s*return)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var dynamicActionForward = HasDynamicActionForward(window);
        var downstreamExpanded = false;
        var downstreamMethods = Array.Empty<string>();
        var downstreamFiles = Array.Empty<string>();

        // Raw action forwarding can still close over a finite exact set when
        // the callback also forwards its decoded action-name value to an
        // owner-local method whose implementations all use finite literal
        // action selectors. This is source-structural, never a mod-name rule.
        if (dynamicActionForward &&
            TryResolveStaticDownstreamActionInterest(
                window,
                nameVars,
                callback.Owner,
                sourceIndex,
                out var downstreamActions,
                out downstreamMethods,
                out downstreamFiles))
        {
            foreach (var action in downstreamActions)
                actions.Add(action);

            dynamicActionForward = false;
            downstreamExpanded = true;
            evidence.Add(
                $"Raw action forwarding was closed over {downstreamMethods.Length} owner-local method name(s) " +
                $"and {downstreamActions.Length} finite action name(s).");
        }

        var gatedWildcardResolved = false;
        var dynamicGateExpression = "";

        // Some callbacks genuinely need the full action stream, but only while
        // a simple owner-local state gate is active. If every raw-action
        // forward is enclosed by side-effect-free boolean member checks and all
        // other callback work is either local decoding or already-proven exact
        // action branches, keep wildcard semantics only behind that gate.
        if (dynamicActionForward &&
            TryResolveSimpleDynamicGate(
                window,
                nameVars,
                out dynamicGateExpression))
        {
            dynamicActionForward = false;
            gatedWildcardResolved = true;
            stateGated = true;
            evidence.Add(
                $"Full-stream downstream action forwarding is bounded by a proven simple state gate: {dynamicGateExpression}.");
        }

        if (dynamicActionForward)
        {
            blockers.Add("Raw action data is forwarded to downstream logic, so this callback alone does not prove a finite action set.");
            evidence.Add("A downstream handler receives the raw action; deeper source analysis is required before exact routing.");
        }

        if (unresolvedActionSelectors.Count > 0)
            blockers.Add($"Action selector(s) could not be reduced to literals: {string.Join(", ", unresolvedActionSelectors.OrderBy(x => x))}.");

        if (actions.Count > 0)
            evidence.Add($"Finite action interest was proven from current source ({actions.Count} action name(s)).");
        if (patterns.Count > 0)
            evidence.Add($"Action-name pattern interest was proven from current source ({patterns.Count} pattern(s)).");
        if (stateGated)
            evidence.Add("An early state gate is present before the callback's main work.");

        var hasActionFilter = actions.Count > 0 || patterns.Count > 0 || staticTables.Count > 0;
        var hasRoutableInterest = hasActionFilter || gatedWildcardResolved;
        var prefilterSideEffect = hasActionFilter &&
            HasMeaningfulWorkBeforeFirstActionFilter(window, nameVars, downstreamMethods);
        if (prefilterSideEffect)
            blockers.Add("Observable work occurs before the first proven action-interest filter.");

        // Callback kind is authoritative. A neighboring Override() elsewhere in
        // the same source window must never poison an Observe classification.
        var isOverride = callback.Kind.Contains("override", StringComparison.OrdinalIgnoreCase);
        if (isOverride)
            blockers.Add("Override semantics require the dedicated override routing template.");

        string recipe;
        if (dynamicActionForward)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DYNAMIC_DOWNSTREAM"
                : "ACTION_ROUTING_DYNAMIC_DOWNSTREAM";
        else if (gatedWildcardResolved)
            recipe = "ACTION_ROUTING_GATED_WILDCARD";
        else if (downstreamExpanded && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DOWNSTREAM_STATIC_SET_WITH_PATTERN"
                : "ACTION_ROUTING_DOWNSTREAM_STATIC_SET_WITH_PATTERN";
        else if (downstreamExpanded)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_DOWNSTREAM_STATIC_SET"
                : "ACTION_ROUTING_DOWNSTREAM_STATIC_SET";
        else if (staticTables.Count > 0 && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_STATIC_SET_WITH_PATTERN"
                : "ACTION_ROUTING_STATIC_SET_WITH_PATTERN";
        else if (staticTables.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_STATIC_SET" : "ACTION_ROUTING_STATIC_SET";
        else if (actions.Count > 0 && patterns.Count > 0)
            recipe = stateGated
                ? "ACTION_ROUTING_STATE_GATED_EXACT_SET_WITH_PATTERN"
                : "ACTION_ROUTING_EXACT_SET_WITH_PATTERN";
        else if (actions.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_EXACT_SET" : "ACTION_ROUTING_EXACT_SET";
        else if (patterns.Count > 0)
            recipe = stateGated ? "ACTION_ROUTING_STATE_GATED_PATTERN" : "ACTION_ROUTING_PATTERN";
        else
            recipe = "ONACTION_FULL_STREAM_OR_UNRESOLVED";

        var automatable =
            hasRoutableInterest &&
            !dynamicActionForward &&
            unresolvedActionSelectors.Count == 0 &&
            !prefilterSideEffect &&
            !isOverride &&
            blockers.All(x => !x.Contains("writes outside", StringComparison.OrdinalIgnoreCase));

        var facts = new
        {
            actions = actions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            actionPatterns = patterns.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            stateGatePresent = stateGated,
            consumerMutation,
            dynamicActionForward,
            gatedWildcardResolved,
            dynamicGateExpression,
            downstreamExpanded,
            downstreamMethods,
            downstreamFiles,
            requiresActionType =
                Regex.IsMatch(window, @"\bGetType\s*\(", RegexOptions.IgnoreCase),
            requiresActionValue =
                Regex.IsMatch(window, @"\bGetValue\s*\(", RegexOptions.IgnoreCase),
            unresolvedActionSelectors = unresolvedActionSelectors.OrderBy(x => x).ToArray()
        };

        return new GenericResolution
        {
            Status = automatable
                ? "RESOLVED"
                : hasRoutableInterest || dynamicActionForward ? "RECOGNIZED_WITH_BLOCKER" : "UNRESOLVED",
            Automatable = automatable,
            Pattern = recipe,
            RecipeFamilies = hasRoutableInterest || dynamicActionForward
                ? new[] { isOverride ? "ACTION_ROUTING_OVERRIDE" : recipe }
                : Array.Empty<string>(),
            Facts = facts,
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = sourceEvidence
        };
    }

    private static List<string> ReadFiniteStringValues(string fullText, string variable)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(
                     fullText,
                     @"\b" + Regex.Escape(variable) + @"\s*=\s*['""](?<value>[^'""]+)['""]",
                     RegexOptions.CultureInvariant))
        {
            values.Add(match.Groups["value"].Value);
        }

        foreach (Match match in Regex.Matches(
                     fullText,
                     @"\b" + Regex.Escape(variable) + @"\s*=\s*CName\.new\s*\(\s*['""](?<value>[^'""]+)['""]\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            values.Add(match.Groups["value"].Value);
        }

        return values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool HasDynamicActionForward(string window)
    {
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (IsRawActionForwardLine(line))
                return true;
        }

        return false;
    }

    private static bool TryResolveSimpleDynamicGate(
        string window,
        IReadOnlySet<string> nameVars,
        out string gateExpression)
    {
        gateExpression = "";
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        var dynamicLines = new List<int>();
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsRawActionForwardLine(lines[i]))
                dynamicLines.Add(i);
        }

        if (dynamicLines.Count == 0)
            return false;

        var protectedRanges = new List<(int Start, int End)>();
        var gateGroups = new List<string>();

        foreach (var index in dynamicLines)
        {
            var enclosing = new List<(int Start, int End, string Expr)>();

            for (var i = 0; i < index; i++)
            {
                var match = Regex.Match(
                    lines[i],
                    @"^\s*if\s+(?<expr>(?:not\s+)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!match.Success)
                    continue;

                var end = FindIndentedBlockEnd(lines, i);
                if (end >= index)
                    enclosing.Add((i, end, match.Groups["expr"].Value.Trim()));
            }

            if (enclosing.Count == 0)
                return false;

            var ordered = enclosing.OrderBy(x => x.Start).ToList();
            gateGroups.Add("(" + string.Join(" and ", ordered.Select(x => x.Expr)) + ")");

            // Protect the outermost proven gate. Its complete body is only
            // entered when the same generated prefilter evaluates true.
            protectedRanges.Add((ordered[0].Start, ordered[0].End));
        }

        // Exact action branches outside the dynamic gate remain independently
        // routable and are protected as complete blocks.
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isExactBranch = nameVars.Any(name =>
                Regex.IsMatch(
                    line,
                    @"^\s*if\s+" + Regex.Escape(name) +
                    @"\s*==\s*['""][^'""]+['""]\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"^\s*if\s+['""][^'""]+['""]\s*==\s*" +
                    Regex.Escape(name) + @"\s+then\s*$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

            if (!isExactBranch)
                continue;

            var end = FindIndentedBlockEnd(lines, i);
            if (end > i)
                protectedRanges.Add((i, end));
        }

        bool IsProtected(int line) =>
            protectedRanges.Any(range => line >= range.Start && line <= range.End);

        // Outside proven exact/gated branches, permit only wrapper syntax and
        // side-effect-free local decoding/reads. Any other call or write means
        // we cannot move the callback behind a generated prefilter.
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsProtected(i))
                continue;

            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end)", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*[^()]+$",
                    RegexOptions.CultureInvariant))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*.*(?:GetName|GetType|GetValue|NameToString)\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            return false;
        }

        gateExpression = string.Join(
            " or ",
            gateGroups
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal));
        return !string.IsNullOrWhiteSpace(gateExpression);
    }

    private static int FindIndentedBlockEnd(IReadOnlyList<string> lines, int start)
    {
        if (start < 0 || start >= lines.Count)
            return -1;

        var indent = lines[start].Length - lines[start].TrimStart().Length;
        for (var i = start + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (!Regex.IsMatch(
                    trimmed,
                    @"^end\s*(?:--.*)?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            var candidateIndent = lines[i].Length - lines[i].TrimStart().Length;
            if (candidateIndent == indent)
                return i;
        }

        return -1;
    }

    private static bool IsRawActionForwardLine(string raw)
    {
        var line = raw.Trim();
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
            return false;
        if (!Regex.IsMatch(line, @"\([^\r\n)]*\baction\b", RegexOptions.IgnoreCase))
            return false;

        return
            !line.Contains("GetName", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("GetType", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("GetValue", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("IsAction", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("NameToString", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("Override(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveStaticDownstreamActionInterest(
        string window,
        IReadOnlySet<string> nameVars,
        string owner,
        LiveSourceIndex sourceIndex,
        out string[] actions,
        out string[] methods,
        out string[] files)
    {
        actions = Array.Empty<string>();
        methods = Array.Empty<string>();
        files = Array.Empty<string>();

        var forwards = new List<ForwardedActionCall>();

        foreach (Match match in Regex.Matches(
                     window,
                     @"(?<method>[A-Za-z_]\w*)\s*\((?<args>[^()\r\n]*)\)",
                     RegexOptions.CultureInvariant))
        {
            var method = match.Groups["method"].Value;
            if (method.Equals("GetName", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("GetType", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("GetValue", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("IsAction", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("NameToString", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("Observe", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("ObserveAfter", StringComparison.OrdinalIgnoreCase) ||
                method.Equals("Override", StringComparison.OrdinalIgnoreCase))
                continue;

            var callArgs = match.Groups["args"].Value
                .Split(',')
                .Select(x => x.Trim())
                .ToArray();

            var rawActionIndex = Array.FindIndex(
                callArgs,
                x => x.Equals("action", StringComparison.Ordinal));
            if (rawActionIndex < 0)
                continue;

            var nameIndex = Array.FindIndex(
                callArgs,
                x => nameVars.Contains(x));
            if (nameIndex < 0)
                continue;

            forwards.Add(new ForwardedActionCall(method, nameIndex, rawActionIndex));
        }

        if (forwards.Count == 0)
            return false;

        var allActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var methodNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var forward in forwards
                     .DistinctBy(
                         x => $"{x.Method}|{x.NameArgumentIndex}|{x.RawActionArgumentIndex}",
                         StringComparer.OrdinalIgnoreCase))
        {
            var definitions = sourceIndex.FindOwnerMethodDefinitions(owner, forward.Method);
            if (definitions.Count == 0)
                return false;

            methodNames.Add(forward.Method);

            foreach (var definition in definitions)
            {
                if (forward.NameArgumentIndex >= definition.Parameters.Length ||
                    forward.RawActionArgumentIndex >= definition.Parameters.Length)
                    return false;

                var nameParameter = definition.Parameters[forward.NameArgumentIndex];
                var actionParameter = definition.Parameters[forward.RawActionArgumentIndex];
                if (string.IsNullOrWhiteSpace(nameParameter) ||
                    string.IsNullOrWhiteSpace(actionParameter))
                    return false;

                if (!TryReadFiniteDownstreamActionSet(
                        definition.Body,
                        nameParameter,
                        actionParameter,
                        out var definitionActions))
                    return false;

                foreach (var action in definitionActions)
                    allActions.Add(action);
                sourceFiles.Add(definition.RelativeFile);
            }
        }

        if (allActions.Count == 0)
            return false;

        actions = allActions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        methods = methodNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        files = sourceFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static bool TryReadFiniteDownstreamActionSet(
        string body,
        string nameParameter,
        string actionParameter,
        out string[] actions)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = Regex.Escape(nameParameter);

        foreach (Match match in Regex.Matches(
                     body,
                     @"\b" + name + @"\s*==\s*['""](?<action>[^'""]+)['""]",
                     RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"['""](?<action>[^'""]+)['""]\s*==\s*\b" + name + @"\b",
                     RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"(?m)^\s*if\s+" + name + @"\s*~=\s*['""](?<action>[^'""]+)['""]\s+then\s+return(?:\s+.*)?\s+end\s*$",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            found.Add(match.Groups["action"].Value);
        }

        var downstreamStaticTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(
                     body,
                     @"\b(?<table>[A-Za-z_]\w*)\s*\[\s*" + name + @"\s*\]",
                     RegexOptions.CultureInvariant))
        {
            var table = match.Groups["table"].Value;
            if (!TryReadStaticStringSet(body, table, out var tableActions, out var dynamicWrites) ||
                dynamicWrites)
            {
                actions = Array.Empty<string>();
                return false;
            }

            downstreamStaticTables.Add(table);
            foreach (var action in tableActions)
                found.Add(action);
        }

        if (found.Count == 0)
        {
            actions = Array.Empty<string>();
            return false;
        }

        foreach (Match match in Regex.Matches(
                     body,
                     @"\b" + name + @"\s*~=\s*['""][^'""]+['""]",
                     RegexOptions.CultureInvariant))
        {
            var lineStart = body.LastIndexOf('\n', Math.Max(0, match.Index - 1));
            var lineEnd = body.IndexOf('\n', match.Index);
            var lineOffset = lineStart < 0 ? 0 : lineStart + 1;
            var line = body.Substring(
                lineOffset,
                (lineEnd < 0 ? body.Length : lineEnd) - lineOffset);

            if (!Regex.IsMatch(
                    line,
                    @"^\s*if\s+.*~=.*\s+then\s+return(?:\s+.*)?\s+end\s*$",
                    RegexOptions.IgnoreCase))
            {
                actions = Array.Empty<string>();
                return false;
            }
        }

        foreach (var rawLine in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                !Regex.IsMatch(line, @"\b" + name + @"\b"))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"\b" + name + @"\s*(?:==|~=)\s*['""]",
                    RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"['""][^'""]+['""]\s*==\s*\b" + name + @"\b",
                    RegexOptions.CultureInvariant) ||
                downstreamStaticTables.Any(table =>
                    Regex.IsMatch(
                        line,
                        @"\b" + Regex.Escape(table) + @"\s*\[\s*" + name + @"\s*\]",
                        RegexOptions.CultureInvariant)) ||
                Regex.IsMatch(
                    line,
                    @"^function\b.*\b" + name + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            actions = Array.Empty<string>();
            return false;
        }

        foreach (var rawLine in body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            var action = Regex.Escape(actionParameter);
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("--", StringComparison.Ordinal) ||
                !Regex.IsMatch(line, @"\b" + action + @"\b"))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"^function\b.*\b" + action + @"\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                Regex.IsMatch(
                    line,
                    @"\b" + action + @"\s*[:.]\s*(?:GetName|GetType|GetValue|IsAction)\s*\(",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                continue;

            if (Regex.IsMatch(
                    line,
                    @"\([^\r\n)]*\b" + action + @"\b",
                    RegexOptions.CultureInvariant))
            {
                actions = Array.Empty<string>();
                return false;
            }
        }

        actions = found.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private sealed record ForwardedActionCall(
        string Method,
        int NameArgumentIndex,
        int RawActionArgumentIndex);

    private static bool HasMeaningfulWorkBeforeFirstActionFilter(
        string window,
        IReadOnlySet<string> nameVars,
        IReadOnlyCollection<string>? resolvedForwardMethods = null)
    {
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstFilter = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isFilter =
                line.Contains("IsAction(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("GetName()", StringComparison.OrdinalIgnoreCase) &&
                    (line.Contains("==", StringComparison.Ordinal) || line.Contains("~=", StringComparison.Ordinal)) ||
                nameVars.Any(v =>
                    line.Contains(v + " ==", StringComparison.Ordinal) ||
                    line.Contains(v + " ~=", StringComparison.Ordinal)) ||
                line.Contains("string.find", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(":find(", StringComparison.OrdinalIgnoreCase) ||
                nameVars.Any(v => Regex.IsMatch(line, @"\[[\s]*" + Regex.Escape(v) + @"[\s]*\]"));

            if (isFilter)
            {
                firstFilter = i;
                break;
            }
        }

        if (firstFilter <= 0)
            return false;

        for (var i = 0; i < firstFilter; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (line.Contains("Observe(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ObserveAfter(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Override(", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("function", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s+return(?:\s+.*)?\s+end\s*$", RegexOptions.IgnoreCase))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s*$", RegexOptions.IgnoreCase) ||
                line.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            // A downstream call whose complete action interest was proven by
            // owner-local source inspection is itself the expanded filter.
            if (resolvedForwardMethods is not null &&
                resolvedForwardMethods.Any(method =>
                    Regex.IsMatch(
                        line,
                        @"[:.]\s*" + Regex.Escape(method) + @"\s*\(",
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)))
                continue;

            // Local reads/decodes are allowed before the filter. They disappear
            // for unrelated actions after routing but have no externally visible
            // assignment target. Restrict call-shaped locals to getter/read names.
            if (Regex.IsMatch(line, @"^local\s+[A-Za-z_]\w*\s*(?:,\s*[A-Za-z_]\w*)*\s*$"))
                continue;
            if (Regex.IsMatch(line, @"^local\s+[A-Za-z_]\w*\s*=\s*[^()]+$"))
                continue;
            if (Regex.IsMatch(
                    line,
                    @"^local\s+[A-Za-z_]\w*\s*=\s*(?:Game\.NameToString\s*\(|GetSingleton\s*\(|[A-Za-z_][\w.]*[.:](?:Get|get|Is|is|Has|has)[A-Za-z_\w]*\s*\()",
                    RegexOptions.IgnoreCase))
                continue;

            if (Regex.IsMatch(line, @"[A-Za-z_][\w.:]*\s*\(") ||
                Regex.IsMatch(line, @"^(?!local\b)[A-Za-z_][\w.\[\]]*\s*="))
                return true;
        }

        return false;
    }

    private static bool TryReadStaticStringSet(
        string fullText,
        string tableName,
        out List<string> actions,
        out bool dynamicWrites)
    {
        actions = new List<string>();
        dynamicWrites = false;

        var declaration = Regex.Match(
            fullText,
            @"(?:local\s+)?" + Regex.Escape(tableName) + @"\s*=\s*\{",
            RegexOptions.CultureInvariant);
        if (!declaration.Success)
            return false;

        var open = fullText.IndexOf('{', declaration.Index);
        if (open < 0)
            return false;

        var depth = 0;
        var close = -1;
        var quote = '\0';
        var escaped = false;

        for (var i = open; i < fullText.Length; i++)
        {
            var c = fullText[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c == '\'' || c == '"')
            {
                quote = c;
                continue;
            }

            if (c == '{') depth++;
            if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    close = i;
                    break;
                }
            }
        }

        if (close <= open)
            return false;

        var body = fullText.Substring(open + 1, close - open - 1);
        foreach (Match match in Regex.Matches(
                     body,
                     @"\[\s*['""](?<key>[^'""]+)['""]\s*\]\s*=|(?<bare>[A-Za-z_]\w*)\s*=\s*true|['""](?<list>[^'""]+)['""]\s*,",
                     RegexOptions.CultureInvariant))
        {
            var value = match.Groups["key"].Success
                ? match.Groups["key"].Value
                : match.Groups["bare"].Success
                    ? match.Groups["bare"].Value
                    : match.Groups["list"].Value;
            if (!string.IsNullOrWhiteSpace(value))
                actions.Add(value);
        }

        if (actions.Count == 0)
            return false;

        var outside = fullText.Remove(open, close - open + 1);
        dynamicWrites =
            Regex.IsMatch(outside, @"\b" + Regex.Escape(tableName) + @"\s*\[.*?\]\s*=", RegexOptions.Singleline) ||
            Regex.IsMatch(outside, @"\btable\.(?:insert|remove)\s*\(\s*" + Regex.Escape(tableName) + @"\b");

        actions = actions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static IReadOnlyDictionary<string, CadenceDecision> ReadCadenceDecisions(string? path)
    {
        var result = new Dictionary<string, CadenceDecision>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("callbacks", out var callbacks) ||
                callbacks.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var row in callbacks.EnumerateArray())
            {
                var owner = JsonString(row, "Owner", "owner");
                var kind = JsonString(row, "Kind", "kind");
                var target = JsonString(row, "Target", "target");
                var group = JsonString(row, "Group", "group");
                var transform = JsonBool(row, "TransformCandidate", "transformCandidate");
                if (!string.IsNullOrWhiteSpace(owner))
                    result[CadenceKey(owner, kind, target)] = new CadenceDecision(group, transform);
            }
        }
        catch
        {
        }

        return result;
    }

    private static List<CallbackMetric> ReadCallbacks(JsonElement root)
    {
        var result = new List<CallbackMetric>();
        if (!root.TryGetProperty("callbacks", out var callbacks) ||
            callbacks.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var row in callbacks.EnumerateArray())
        {
            var sourceFile = "";
            long? lineStart = null;
            long? lineEnd = null;
            if (row.TryGetProperty("source", out var source) &&
                source.ValueKind == JsonValueKind.Object)
            {
                sourceFile = JsonString(source, "file");
                lineStart = JsonNullableLong(source, "lineStart");
                lineEnd = JsonNullableLong(source, "lineEnd");
            }

            result.Add(new CallbackMetric
            {
                RegistrationId = JsonNullableLong(row, "registrationId"),
                Owner = JsonString(row, "owner", "Owner"),
                Infrastructure = JsonBool(row, "infrastructure", "Infrastructure"),
                Kind = JsonString(row, "kind", "Kind"),
                Target = JsonString(row, "target", "Target"),
                SourceFile = sourceFile,
                SourceLineStart = lineStart,
                SourceLineEnd = lineEnd,
                CallsPerSecond = JsonDouble(row, "callsPerSecond", "CallsPerSecond"),
                ExclusiveMsPerSecond = JsonDouble(row, "exclusiveMsPerSecond", "ExclusiveMsPerSecond"),
                GlobalWorkSharePct = JsonDouble(row, "globalWorkSharePct", "GlobalWorkSharePct"),
                FamilyWorkSharePct = JsonDouble(row, "familyWorkSharePct", "FamilyWorkSharePct"),
                AvgExclusiveUs = JsonDouble(row, "avgExclusiveUs", "AvgExclusiveUs"),
                MaxExclusiveMs = JsonDouble(row, "maxExclusiveMs", "MaxExclusiveMs"),
                SpikeCount = (long)JsonDouble(row, "spikeCount", "SpikeCount"),
                MaxSpikeExclusiveMs = JsonDouble(row, "maxSpikeExclusiveMs", "MaxSpikeExclusiveMs")
            });
        }

        return result;
    }

    private static string ClassifyCallbackFamily(string kind, string target)
    {
        if (target.Equals("onUpdate", StringComparison.OrdinalIgnoreCase))
            return "ONUPDATE";
        if (target.EndsWith("::OnAction", StringComparison.OrdinalIgnoreCase) ||
            target.Equals("OnAction", StringComparison.OrdinalIgnoreCase) ||
            target.Contains("OnAction", StringComparison.OrdinalIgnoreCase))
            return "ONACTION";
        return "OTHER";
    }

    private static string FamilyKey(string kind, string target) =>
        $"{kind.Trim().ToLowerInvariant()}::{target.Trim().ToLowerInvariant()}";

    private static string CadenceKey(string owner, string kind, string target) =>
        $"{owner}\u001f{kind}\u001f{target}";

    private static double Round(double value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero);

    private static string JsonString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";
        }
        return "";
    }

    private static bool JsonBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value))
                continue;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
        }
        return false;
    }

    private static double JsonDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) &&
                value.TryGetDouble(out var number))
                return number;
        }
        return 0;
    }

    private static long? JsonNullableLong(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value) ||
                value.ValueKind == JsonValueKind.Null)
                continue;
            if (value.TryGetInt64(out var number))
                return number;
        }
        return null;
    }

    private sealed class LiveSourceIndex
    {
        private readonly string _modsRoot;
        private readonly List<string> _luaFiles;

        public LiveSourceIndex(string modsRoot)
        {
            _modsRoot = Path.GetFullPath(modsRoot);
            try
            {
                _luaFiles = Directory.EnumerateFiles(_modsRoot, "*.lua", SearchOption.AllDirectories)
                    .Take(20000)
                    .ToList();
            }
            catch
            {
                _luaFiles = new List<string>();
            }
        }

        public ResolvedSource? Resolve(CallbackMetric callback)
        {
            var source = (callback.SourceFile ?? "").Trim().TrimStart('@')
                .Replace('/', Path.DirectorySeparatorChar);
            var ownerFolder = ResolveOwnerFolder(callback.Owner);

            // Registration source frequently arrives as only "init.lua". That is
            // ambiguous across the whole CET tree but unambiguous inside the
            // measured owner. Prefer owner-relative provenance before any global
            // suffix search.
            if (!string.IsNullOrWhiteSpace(source) &&
                !Path.IsPathRooted(source) &&
                ownerFolder is not null)
            {
                var ownerRelative = Path.Combine(ownerFolder, source);
                if (File.Exists(ownerRelative))
                    return BuildResolvedSource(ownerRelative, "profiler-owner-relative", callback);
            }

            if (!string.IsNullOrWhiteSpace(source))
            {
                if (Path.IsPathRooted(source) && File.Exists(source) && IsInsideMods(source))
                    return BuildResolvedSource(source, "profiler-absolute", callback);

                var normalized = source.Replace('\\', '/');
                var modsIndex = normalized.IndexOf("/mods/", StringComparison.OrdinalIgnoreCase);
                if (modsIndex >= 0)
                {
                    var relative = normalized[(modsIndex + "/mods/".Length)..]
                        .Replace('/', Path.DirectorySeparatorChar);
                    var direct = Path.Combine(_modsRoot, relative);
                    if (File.Exists(direct))
                        return BuildResolvedSource(direct, "profiler-mods-relative", callback);
                }

                var combined = Path.Combine(_modsRoot, source);
                if (File.Exists(combined))
                    return BuildResolvedSource(combined, "profiler-relative", callback);

                var suffix = normalized.TrimStart('/');
                var suffixMatches = _luaFiles
                    .Where(x => x.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                if (suffixMatches.Count == 1)
                    return BuildResolvedSource(suffixMatches[0], "profiler-suffix", callback);
            }

            if (ownerFolder is not null)
            {
                var token = ClassifyCallbackFamily(callback.Kind, callback.Target) == "ONACTION"
                    ? "OnAction"
                    : callback.Target;
                var matches = Directory.EnumerateFiles(ownerFolder, "*.lua", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        try
                        {
                            return File.ReadAllText(path).Contains(token, StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    })
                    .Take(3)
                    .ToList();

                if (matches.Count == 1)
                    return BuildResolvedSource(matches[0], "owner-unique-token", callback);
            }

            return null;
        }

        public IReadOnlyList<OwnerMethodDefinition> FindOwnerMethodDefinitions(
            string owner,
            string methodName)
        {
            var result = new List<OwnerMethodDefinition>();
            var ownerFolder = ResolveOwnerFolder(owner);
            if (ownerFolder is null)
                return result;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(ownerFolder, "*.lua", SearchOption.AllDirectories);
            }
            catch
            {
                return result;
            }

            var methodPattern =
                @"^(?<indent>\s*)function\s+[A-Za-z_][\w.]*\s*[:.]\s*" +
                Regex.Escape(methodName) +
                @"\s*\((?<args>[^)]*)\)";

            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file)
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n');
                }
                catch
                {
                    continue;
                }

                var lines = text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    var match = Regex.Match(
                        lines[i],
                        methodPattern,
                        RegexOptions.CultureInvariant);
                    if (!match.Success)
                        continue;

                    var indent = match.Groups["indent"].Value.Length;
                    var endLine = -1;

                    for (var j = i + 1; j < lines.Length; j++)
                    {
                        var candidate = lines[j];
                        var trimmed = candidate.Trim();
                        if (!Regex.IsMatch(
                                trimmed,
                                @"^end\s*(?:--.*)?$",
                                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            continue;

                        var candidateIndent = candidate.Length - candidate.TrimStart().Length;
                        if (candidateIndent == indent)
                        {
                            endLine = j;
                            break;
                        }
                    }

                    if (endLine <= i)
                        continue;

                    var parameters = match.Groups["args"].Value
                        .Split(',')
                        .Select(x => x.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();

                    result.Add(new OwnerMethodDefinition
                    {
                        RelativeFile = Path.GetRelativePath(_modsRoot, file).Replace('\\', '/'),
                        Parameters = parameters,
                        Body = string.Join("\n", lines.Skip(i).Take(endLine - i + 1))
                    });

                    i = endLine;
                }
            }

            return result;
        }

        private ResolvedSource? BuildResolvedSource(
            string path,
            string mode,
            CallbackMetric callback)
        {
            try
            {
                var full = File.ReadAllText(path);
                var lines = full.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var lineStart = callback.SourceLineStart is > 0
                    ? (int)Math.Min(callback.SourceLineStart.Value, lines.Length)
                    : FindTokenLine(lines, callback.Target);
                var lineEnd = callback.SourceLineEnd is > 0
                    ? (int)Math.Min(callback.SourceLineEnd.Value, lines.Length)
                    : Math.Min(lines.Length, Math.Max(1, lineStart) + 140);

                var callbackStart = Math.Max(1, lineStart);
                var callbackEnd = Math.Min(lines.Length, Math.Max(lineEnd, callbackStart));
                var callbackText = string.Join("\n", lines.Skip(callbackStart - 1).Take(callbackEnd - callbackStart + 1));

                var windowStart = Math.Max(1, lineStart - 3);
                var windowEnd = Math.Min(lines.Length, Math.Max(lineEnd + 3, windowStart + 40));
                var window = string.Join("\n", lines.Skip(windowStart - 1).Take(windowEnd - windowStart + 1));

                return new ResolvedSource
                {
                    Path = path,
                    RelativeFile = Path.GetRelativePath(_modsRoot, path).Replace('\\', '/'),
                    Sha256 = Sha256(path),
                    FullText = full,
                    CallbackText = callbackText,
                    Window = window,
                    LineStart = lineStart > 0 ? lineStart : null,
                    LineEnd = lineEnd > 0 ? lineEnd : null,
                    MatchMode = mode
                };
            }
            catch
            {
                return null;
            }
        }

        private string? ResolveOwnerFolder(string owner)
        {
            var exact = Directory.EnumerateDirectories(_modsRoot)
                .FirstOrDefault(x => Path.GetFileName(x).Equals(owner, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;

            var normalized = Normalize(owner);
            var matches = Directory.EnumerateDirectories(_modsRoot)
                .Where(x => Normalize(Path.GetFileName(x)) == normalized)
                .Take(2)
                .ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        private bool IsInsideMods(string path)
        {
            var full = Path.GetFullPath(path);
            return full.StartsWith(_modsRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private static int FindTokenLine(IReadOnlyList<string> lines, string target)
        {
            var token = target.Contains("OnAction", StringComparison.OrdinalIgnoreCase)
                ? "OnAction"
                : target;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains(token, StringComparison.OrdinalIgnoreCase))
                    return i + 1;
            }
            return 1;
        }

        private static string Normalize(string value) =>
            NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
    }

    private sealed class ExceptionRegistry
    {
        public bool Loaded { get; init; }
        public List<ExceptionRegistryEntry> Entries { get; init; } = new();

        public static ExceptionRegistry Load(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new ExceptionRegistry();

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var entries = new List<ExceptionRegistryEntry>();
                if (doc.RootElement.TryGetProperty("entries", out var rows) &&
                    rows.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in rows.EnumerateArray())
                    {
                        var hints = JsonStringArray(row, "identityHints");
                        var callbackKind = "";
                        var callbackTarget = "";
                        if (row.TryGetProperty("callback", out var callback) &&
                            callback.ValueKind == JsonValueKind.Object)
                        {
                            callbackKind = JsonString(callback, "kind");
                            callbackTarget = JsonString(callback, "target");
                        }

                        entries.Add(new ExceptionRegistryEntry
                        {
                            Id = JsonString(row, "id"),
                            Category = JsonString(row, "category"),
                            IdentityHints = hints,
                            CallbackKind = callbackKind,
                            CallbackTarget = callbackTarget,
                            SemanticHints = JsonStringArray(row, "semanticHints"),
                            SuggestedRecipeFamilies = JsonStringArray(row, "suggestedRecipeFamilies")
                        });
                    }
                }

                return new ExceptionRegistry { Loaded = true, Entries = entries };
            }
            catch
            {
                return new ExceptionRegistry();
            }
        }

        public ExceptionRegistryEntry? Match(CallbackMetric callback)
        {
            var owner = Normalize(callback.Owner);
            return Entries.FirstOrDefault(entry =>
                entry.IdentityHints.Any(h => Normalize(h) == owner) &&
                (string.IsNullOrWhiteSpace(entry.CallbackKind) ||
                 entry.CallbackKind.Equals(callback.Kind, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(entry.CallbackTarget) ||
                 entry.CallbackTarget.Equals(callback.Target, StringComparison.OrdinalIgnoreCase)));
        }

        private static string Normalize(string value) =>
            NormalizeNonAlphaNumeric.Replace(value.ToLowerInvariant(), "");

        private static string[] JsonStringArray(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out var array) ||
                array.ValueKind != JsonValueKind.Array)
                return Array.Empty<string>();

            return array.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString() ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();
        }
    }

    private sealed class CallbackMetric
    {
        public long? RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public bool Infrastructure { get; init; }
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string SourceFile { get; init; } = "";
        public long? SourceLineStart { get; init; }
        public long? SourceLineEnd { get; init; }
        public double CallsPerSecond { get; init; }
        public double ExclusiveMsPerSecond { get; init; }
        public double GlobalWorkSharePct { get; init; }
        public double FamilyWorkSharePct { get; init; }
        public double AvgExclusiveUs { get; init; }
        public double MaxExclusiveMs { get; init; }
        public long SpikeCount { get; init; }
        public double MaxSpikeExclusiveMs { get; init; }

        public long? registrationId => RegistrationId;
        public string owner => Owner;
        public string kind => Kind;
        public string target => Target;
        public double callsPerSecond => Round(CallsPerSecond);
        public double exclusiveMsPerSecond => Round(ExclusiveMsPerSecond);
        public double globalWorkSharePct => Round(GlobalWorkSharePct);
        public double familyWorkSharePct => Round(FamilyWorkSharePct);
        public double avgExclusiveUs => Round(AvgExclusiveUs);
        public double maxExclusiveMs => Round(MaxExclusiveMs);
        public long spikeCount => SpikeCount;
        public double maxSpikeExclusiveMs => Round(MaxSpikeExclusiveMs);
    }

    private sealed class AuthorCadenceResolution
    {
        public string DeltaParameter { get; init; } = "delta";
        public double BaseIntervalSeconds { get; init; }
        public double[] TimerIntervalsSeconds { get; init; } = Array.Empty<double>();
        public string[] AccumulatorVariables { get; init; } = Array.Empty<string>();
        public double ExpectedCallsPerSecond { get; init; }
        public double RuntimeEntryReductionFactor { get; init; }
    }

    private sealed record CadenceDecision(string Group, bool TransformCandidate);

    private sealed class GenericResolution
    {
        public string Status { get; init; } = "";
        public bool Automatable { get; init; }
        public string Pattern { get; init; } = "";
        public string[] RecipeFamilies { get; init; } = Array.Empty<string>();
        public object? Facts { get; init; }
        public string[] Evidence { get; init; } = Array.Empty<string>();
        public string[] Blockers { get; init; } = Array.Empty<string>();
        public SourceEvidence? Source { get; init; }
    }

    private sealed class SourceEvidence
    {
        public string RelativeFile { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public string MatchMode { get; init; } = "";
    }

    private sealed class ResolvedSource
    {
        public string Path { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string Sha256 { get; init; } = "";
        public string FullText { get; init; } = "";
        public string CallbackText { get; init; } = "";
        public string Window { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public string MatchMode { get; init; } = "";
    }

    private sealed class OwnerMethodDefinition
    {
        public string RelativeFile { get; init; } = "";
        public string[] Parameters { get; init; } = Array.Empty<string>();
        public string Body { get; init; } = "";
    }

    private sealed class ExceptionRegistryEntry
    {
        public string Id { get; init; } = "";
        public string Category { get; init; } = "";
        public string[] IdentityHints { get; init; } = Array.Empty<string>();
        public string CallbackKind { get; init; } = "";
        public string CallbackTarget { get; init; } = "";
        public string[] SemanticHints { get; init; } = Array.Empty<string>();
        public string[] SuggestedRecipeFamilies { get; init; } = Array.Empty<string>();
    }
}
