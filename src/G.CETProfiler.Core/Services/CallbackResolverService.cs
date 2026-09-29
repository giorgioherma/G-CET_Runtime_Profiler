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
            return ResolveOnAction(callback, source, sourceEvidence);

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

        if (source is not null &&
            Regex.IsMatch(
                source.Window,
                @"\b(?:registerForEvent|registerRuntimeEvent)\s*\(\s*['""]onUpdate['""]",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            recipes.Add("FRAME_DISPATCH_CONSOLIDATION");
            evidence.Add("Direct onUpdate registration is present in the current deployed source.");
        }
        else
        {
            blockers.Add("Current deployed source could not prove the direct onUpdate registration.");
        }

        var cadenceKey = CadenceKey(callback.Owner, callback.Kind, callback.Target);
        if (cadence.TryGetValue(cadenceKey, out var cadenceDecision) &&
            cadenceDecision.TransformCandidate &&
            !cadenceDecision.Group.Equals("LEAVE_ALONE", StringComparison.OrdinalIgnoreCase))
        {
            recipes.Add(cadenceDecision.Group);
            evidence.Add($"Cadence subset source-confirmed {cadenceDecision.Group}.");
        }

        return new GenericResolution
        {
            Status = recipes.Count > 0 ? "RESOLVED" : "SOURCE_UNRESOLVED",
            Automatable = recipes.Count > 0,
            Pattern = recipes.Count > 1
                ? "FRAME_CALLBACK_WITH_PROVEN_SUBPATTERN"
                : recipes.FirstOrDefault() ?? "ONUPDATE_UNRESOLVED",
            RecipeFamilies = recipes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = sourceEvidence
        };
    }

    private static GenericResolution ResolveOnAction(
        CallbackMetric callback,
        ResolvedSource? source,
        SourceEvidence? sourceEvidence)
    {
        if (source is null)
        {
            return new GenericResolution
            {
                Status = "SOURCE_UNRESOLVED",
                Automatable = false,
                Pattern = "ONACTION_SOURCE_UNRESOLVED",
                RecipeFamilies = Array.Empty<string>(),
                Evidence = Array.Empty<string>(),
                Blockers = new[] { "The current deployed OnAction callback source could not be mapped uniquely." },
                Source = sourceEvidence
            };
        }

        var window = source.Window;
        var full = source.FullText;
        var evidence = new List<string>();
        var blockers = new List<string>();
        var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var orderSensitive =
            Regex.IsMatch(window, @"\bconsumer\s*:\s*Consume\s*\(", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(window, @"\bConsume\s*\(", RegexOptions.IgnoreCase);
        if (orderSensitive)
            blockers.Add("Input consumer/order-sensitive behavior is present.");

        var nameVars = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(
                     window,
                     @"(?<var>[A-Za-z_]\w*)\s*=\s*(?:Game\.NameToString\s*\(\s*)?(?<action>[A-Za-z_]\w*)\s*:\s*GetName\s*\(\s*\)\s*\)?",
                     RegexOptions.CultureInvariant))
        {
            nameVars.Add(match.Groups["var"].Value);
        }

        foreach (Match match in Regex.Matches(
                     window,
                     @"[A-Za-z_]\w*\s*:\s*IsAction\s*\(\s*['""](?<action>[^'""]+)['""]\s*\)",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
        }

        foreach (Match match in Regex.Matches(
                     window,
                     @"[A-Za-z_]\w*\s*:\s*GetName\s*\(\s*\)\s*==\s*(?:CName\.new\s*\(\s*)?['""](?<action>[^'""]+)['""]\s*\)?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            actions.Add(match.Groups["action"].Value);
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

        var patterns = new List<string>();
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

        if (actions.Count > 0)
            evidence.Add($"Finite action interest was proven from current source ({actions.Count} action name(s)).");
        if (patterns.Count > 0)
            evidence.Add($"Action-name pattern interest was proven from current source ({patterns.Count} pattern(s)).");
        if (stateGated)
            evidence.Add("An early state gate is present before the callback's main work.");

        var hasActionFilter = actions.Count > 0 || patterns.Count > 0 || staticTables.Count > 0;
        var prefilterSideEffect = hasActionFilter && HasMeaningfulWorkBeforeFirstActionFilter(window, nameVars);
        if (prefilterSideEffect)
            blockers.Add("Meaningful work occurs before the first proven action-interest filter.");

        var isOverride =
            callback.Kind.Contains("override", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(window, @"\bOverride\s*\(", RegexOptions.IgnoreCase);

        if (isOverride)
            blockers.Add("Override semantics require the dedicated override routing template.");

        var recipe = staticTables.Count > 0
            ? stateGated ? "ACTION_ROUTING_STATE_GATED_STATIC_SET" : "ACTION_ROUTING_STATIC_SET"
            : actions.Count > 0
                ? stateGated ? "ACTION_ROUTING_STATE_GATED_EXACT_SET" : "ACTION_ROUTING_EXACT_SET"
                : patterns.Count > 0
                    ? stateGated ? "ACTION_ROUTING_STATE_GATED_PATTERN" : "ACTION_ROUTING_PATTERN"
                    : "ONACTION_FULL_STREAM_OR_UNRESOLVED";

        var automatable =
            hasActionFilter &&
            !orderSensitive &&
            !prefilterSideEffect &&
            !isOverride;

        return new GenericResolution
        {
            Status = automatable
                ? "RESOLVED"
                : hasActionFilter ? "RECOGNIZED_WITH_BLOCKER" : "UNRESOLVED",
            Automatable = automatable,
            Pattern = recipe,
            RecipeFamilies = hasActionFilter
                ? new[] { isOverride ? "ACTION_ROUTING_OVERRIDE" : recipe }
                : Array.Empty<string>(),
            Evidence = evidence.ToArray(),
            Blockers = blockers.ToArray(),
            Source = sourceEvidence
        };
    }

    private static bool HasMeaningfulWorkBeforeFirstActionFilter(
        string window,
        IReadOnlySet<string> nameVars)
    {
        var lines = window.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var firstFilter = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var isFilter =
                line.Contains("IsAction(", StringComparison.OrdinalIgnoreCase) ||
                line.Contains(":GetName()", StringComparison.OrdinalIgnoreCase) && line.Contains("==", StringComparison.Ordinal) ||
                nameVars.Any(v => line.Contains(v + " ==", StringComparison.Ordinal)) ||
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
            if (Regex.IsMatch(line, @"^local\s+[A-Za-z_]\w*\s*=\s*(?:Game\.NameToString\s*\()?\s*[A-Za-z_]\w*\s*:\s*(?:GetName|GetType)\s*\("))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s+return(?:\s+.*)?\s+end\s*$", RegexOptions.IgnoreCase))
                continue;
            if (Regex.IsMatch(line, @"^if\s+.*\s+then\s*$", RegexOptions.IgnoreCase) ||
                line.Equals("return", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("end", StringComparison.OrdinalIgnoreCase))
                continue;

            // A function call or non-local assignment before the action filter is
            // treated as observable work. This is intentionally stricter than the
            // DEV policy used later by the patch generator.
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
            var candidates = new List<(string Path, string Mode)>();
            var source = (callback.SourceFile ?? "").Trim().TrimStart('@').Replace('/', Path.DirectorySeparatorChar);

            if (!string.IsNullOrWhiteSpace(source))
            {
                if (Path.IsPathRooted(source) && File.Exists(source) && IsInsideMods(source))
                    candidates.Add((source, "profiler-absolute"));
                else
                {
                    var normalized = source.Replace('\\', '/');
                    var modsIndex = normalized.IndexOf("/mods/", StringComparison.OrdinalIgnoreCase);
                    if (modsIndex >= 0)
                    {
                        var relative = normalized[(modsIndex + "/mods/".Length)..]
                            .Replace('/', Path.DirectorySeparatorChar);
                        var direct = Path.Combine(_modsRoot, relative);
                        if (File.Exists(direct))
                            candidates.Add((direct, "profiler-mods-relative"));
                    }

                    var combined = Path.Combine(_modsRoot, source);
                    if (File.Exists(combined))
                        candidates.Add((combined, "profiler-relative"));

                    var suffix = normalized.TrimStart('/');
                    candidates.AddRange(_luaFiles
                        .Where(x => x.Replace('\\', '/').EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        .Select(x => (x, "profiler-suffix")));
                }
            }

            if (candidates.Select(x => x.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 0)
            {
                var ownerFolder = ResolveOwnerFolder(callback.Owner);
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
                        candidates.Add((matches[0], "owner-unique-token"));
                }
            }

            var distinct = candidates
                .GroupBy(x => Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            if (distinct.Count != 1)
                return null;

            try
            {
                var chosen = distinct[0];
                var full = File.ReadAllText(chosen.Path);
                var lines = full.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                var lineStart = callback.SourceLineStart is > 0
                    ? (int)Math.Min(callback.SourceLineStart.Value, lines.Length)
                    : FindTokenLine(lines, callback.Target);
                var lineEnd = callback.SourceLineEnd is > 0
                    ? (int)Math.Min(callback.SourceLineEnd.Value, lines.Length)
                    : Math.Min(lines.Length, Math.Max(1, lineStart) + 140);

                var windowStart = Math.Max(1, lineStart - 3);
                var windowEnd = Math.Min(lines.Length, Math.Max(lineEnd + 3, windowStart + 40));
                var window = string.Join("\n", lines.Skip(windowStart - 1).Take(windowEnd - windowStart + 1));

                return new ResolvedSource
                {
                    Path = chosen.Path,
                    RelativeFile = Path.GetRelativePath(_modsRoot, chosen.Path).Replace('\\', '/'),
                    Sha256 = Sha256(chosen.Path),
                    FullText = full,
                    Window = window,
                    LineStart = lineStart > 0 ? lineStart : null,
                    LineEnd = lineEnd > 0 ? lineEnd : null,
                    MatchMode = chosen.Mode
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

    private sealed record CadenceDecision(string Group, bool TransformCandidate);

    private sealed class GenericResolution
    {
        public string Status { get; init; } = "";
        public bool Automatable { get; init; }
        public string Pattern { get; init; } = "";
        public string[] RecipeFamilies { get; init; } = Array.Empty<string>();
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
        public string Window { get; init; } = "";
        public int? LineStart { get; init; }
        public int? LineEnd { get; init; }
        public string MatchMode { get; init; } = "";
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
