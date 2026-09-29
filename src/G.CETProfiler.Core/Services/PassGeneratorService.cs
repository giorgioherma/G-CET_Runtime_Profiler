using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

public sealed record PassBuildResult(
    string ZipPath,
    int FileCount,
    int TransformCount,
    int SkippedCount);

/// <summary>
/// Generates a reversible overlay ZIP from resolver decisions only.
/// V1 intentionally supports only the two proven structural passes:
/// ACTION_ROUTING_* and FRAME_DISPATCH_CONSOLIDATION.
/// It never invents candidates from mod names or unclassified source.
/// </summary>
public static class PassGeneratorService
{
    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true
    };

    private static readonly Regex OnActionOpening = new(
        @"Observe\s*\(\s*(['""])PlayerPuppet\1\s*,\s*(['""])OnAction\2\s*,\s*function\s*\((?<args>[^)]*)\)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex OnActionClosing = new(
        @"end\s*\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex UnsafeLuaPatternChars = new(
        @"[%^$()%.\[\]*+\-?]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static PassBuildResult Generate(
        string captureRoot,
        string modsRoot,
        string? outputZipPath = null)
    {
        captureRoot = Path.GetFullPath(captureRoot);
        modsRoot = Path.GetFullPath(modsRoot);

        var resolverPath = Path.Combine(captureRoot, ResultReportService.ResolverResolutionFileName);
        if (!File.Exists(resolverPath))
            throw new InvalidOperationException(
                $"Resolver output is missing: {resolverPath}. Run ANALYZE first.");

        if (!Directory.Exists(modsRoot))
            throw new DirectoryNotFoundException($"CET mods folder was not found: {modsRoot}");

        using var resolver = JsonDocument.Parse(File.ReadAllText(resolverPath));
        var candidates = ReadCandidates(resolver.RootElement);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                "The resolver produced no V1 pass candidates. No ZIP was generated.");

        ValidateRuntimeInfrastructure(modsRoot, candidates);

        var groups = candidates
            .GroupBy(x => x.RelativeFile, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var fileManifest = new List<object>();
        var transformManifest = new List<object>();
        var skipped = new List<object>();

        foreach (var group in groups)
        {
            var expectedHashes = group
                .Select(x => x.SourceSha256)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (expectedHashes.Length != 1)
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(candidate, "Conflicting source hashes were recorded for the same file."));
                continue;
            }

            var livePath = ResolveInsideMods(modsRoot, group.Key);
            if (livePath is null || !File.Exists(livePath))
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(candidate, "Current deployed source file was not found."));
                continue;
            }

            var originalBytes = File.ReadAllBytes(livePath);
            var liveHash = Sha256(originalBytes);
            if (!liveHash.Equals(expectedHashes[0], StringComparison.OrdinalIgnoreCase))
            {
                foreach (var candidate in group)
                    skipped.Add(Skip(
                        candidate,
                        $"Current source SHA256 no longer matches the analyzed capture. Expected {expectedHashes[0]}, got {liveHash}."));
                continue;
            }

            var transformed = TransformFile(
                originalBytes,
                group.OrderByDescending(x => x.LineStart).ToList(),
                transformManifest,
                skipped);

            if (transformed.AppliedTransforms == 0)
                continue;

            staged[group.Key] = transformed.Bytes;
            fileManifest.Add(new
            {
                path = group.Key.Replace('\\', '/'),
                sourceSha256 = liveHash,
                generatedSha256 = Sha256(transformed.Bytes),
                appliedTransforms = transformed.AppliedTransforms
            });
        }

        if (staged.Count == 0)
            throw new InvalidOperationException(
                "All resolver candidates were rejected during current-source revalidation. No ZIP was generated.");

        var resolverBytes = File.ReadAllBytes(resolverPath);
        var captureName = new DirectoryInfo(captureRoot).Name;
        var generatedUtc = DateTime.UtcNow;
        outputZipPath ??= Path.Combine(
            captureRoot,
            $"G-CET_PASS_{generatedUtc:yyyyMMdd-HHmmss}.zip");
        outputZipPath = Path.GetFullPath(outputZipPath);

        var manifest = new
        {
            schemaVersion = "0.1",
            generatedUtc = generatedUtc.ToString("O"),
            capture = captureName,
            resolverSha256 = Sha256(resolverBytes),
            policy = new
            {
                selection = "ONLY_AUTOMATABLE_CANDIDATES_FROM_G-CET_Resolver.json",
                modNameRules = false,
                sourceShaRequired = true,
                fullFileOverlay = true,
                cadenceTransforms = false,
                supportedPasses = new[]
                {
                    "ACTION_ROUTING_*",
                    "FRAME_DISPATCH_CONSOLIDATION"
                },
                note = "V1 pass generation does not infer extra targets. It applies only resolver-authorized callback transforms and revalidates the current source SHA before writing a full replacement file."
            },
            runtimeRequirements = new
            {
                zeroEngine = "0-Engine",
                subscribeAction = candidates.Any(x => x.Kind == CandidateKind.Action),
                makeEventRegistrar = candidates.Any(x => x.Kind == CandidateKind.Frame)
            },
            summary = new
            {
                files = staged.Count,
                transforms = transformManifest.Count,
                skipped = skipped.Count
            },
            files = fileManifest,
            transforms = transformManifest,
            skipped
        };

        Directory.CreateDirectory(Path.GetDirectoryName(outputZipPath)!);
        if (File.Exists(outputZipPath))
            File.Delete(outputZipPath);

        using (var archive = ZipFile.Open(outputZipPath, ZipArchiveMode.Create))
        {
            foreach (var pair in staged.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                var entryName =
                    "bin/x64/plugins/cyber_engine_tweaks/mods/" +
                    pair.Key.Replace('\\', '/');
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(pair.Value, 0, pair.Value.Length);
            }

            var manifestEntry = archive.CreateEntry("G-CET_Pass_Manifest.json", CompressionLevel.Optimal);
            using var writer = new StreamWriter(
                manifestEntry.Open(),
                new UTF8Encoding(false));
            writer.Write(JsonSerializer.Serialize(manifest, ManifestJson));
            writer.WriteLine();
        }

        return new PassBuildResult(
            outputZipPath,
            staged.Count,
            transformManifest.Count,
            skipped.Count);
    }

    private static List<PassCandidate> ReadCandidates(JsonElement root)
    {
        var result = new List<PassCandidate>();

        if (!root.TryGetProperty("callbackFamilies", out var families) ||
            families.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var family in families.EnumerateArray())
        {
            var resolverFamily = JsonString(family, "resolverFamily");
            if (!family.TryGetProperty("topConsumers", out var consumers) ||
                consumers.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var consumer in consumers.EnumerateArray())
            {
                if (!consumer.TryGetProperty("generic", out var generic) ||
                    generic.ValueKind != JsonValueKind.Object ||
                    !JsonBool(generic, "Automatable"))
                    continue;

                if (!consumer.TryGetProperty("source", out var source) ||
                    source.ValueKind != JsonValueKind.Object)
                    continue;

                var recipes = JsonStringArray(generic, "RecipeFamilies");
                CandidateKind? kind = null;

                if (resolverFamily.Equals("ONACTION", StringComparison.OrdinalIgnoreCase) &&
                    recipes.Any(x =>
                        x.StartsWith("ACTION_ROUTING", StringComparison.OrdinalIgnoreCase) &&
                        !x.Equals("ACTION_ROUTING_OVERRIDE", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Action;
                }
                else if (resolverFamily.Equals("ONUPDATE", StringComparison.OrdinalIgnoreCase) &&
                         recipes.Any(x =>
                             x.Equals("FRAME_DISPATCH_CONSOLIDATION", StringComparison.OrdinalIgnoreCase)))
                {
                    kind = CandidateKind.Frame;
                }

                if (kind is null)
                    continue;

                var registrationId = JsonNullableLong(consumer, "registrationId");
                var owner = JsonString(consumer, "owner");
                var relativeFile = JsonString(source, "RelativeFile", "relativeFile");
                var sha = JsonString(source, "Sha256", "sha256");
                var lineStart = (int)(JsonNullableLong(source, "LineStart", "lineStart") ?? 0);
                var lineEnd = (int)(JsonNullableLong(source, "LineEnd", "lineEnd") ?? 0);
                var pattern = JsonString(generic, "Pattern", "pattern");

                if (registrationId is null ||
                    string.IsNullOrWhiteSpace(owner) ||
                    string.IsNullOrWhiteSpace(relativeFile) ||
                    string.IsNullOrWhiteSpace(sha) ||
                    lineStart <= 0 ||
                    lineEnd < lineStart)
                    continue;

                var facts = ReadFacts(generic);

                result.Add(new PassCandidate
                {
                    Kind = kind.Value,
                    RegistrationId = registrationId.Value,
                    Owner = owner,
                    RelativeFile = relativeFile.Replace('\\', '/'),
                    SourceSha256 = sha,
                    LineStart = lineStart,
                    LineEnd = lineEnd,
                    Pattern = pattern,
                    Actions = facts.Actions,
                    ActionPatterns = facts.ActionPatterns,
                    RequiresActionType = facts.RequiresActionType,
                    RequiresActionValue = facts.RequiresActionValue,
                    ConsumerMutation = facts.ConsumerMutation,
                    StateGatePresent = facts.StateGatePresent
                });
            }
        }

        return result;
    }

    private static CandidateFacts ReadFacts(JsonElement generic)
    {
        if (!generic.TryGetProperty("Facts", out var facts) ||
            facts.ValueKind != JsonValueKind.Object)
        {
            return new CandidateFacts();
        }

        return new CandidateFacts
        {
            Actions = JsonStringArray(facts, "actions"),
            ActionPatterns = JsonStringArray(facts, "actionPatterns"),
            RequiresActionType = JsonBool(facts, "requiresActionType"),
            RequiresActionValue = JsonBool(facts, "requiresActionValue"),
            ConsumerMutation = JsonBool(facts, "consumerMutation"),
            StateGatePresent = JsonBool(facts, "stateGatePresent")
        };
    }

    private static void ValidateRuntimeInfrastructure(
        string modsRoot,
        IReadOnlyCollection<PassCandidate> candidates)
    {
        var zeroInit = Path.Combine(modsRoot, "0-Engine", "init.lua");
        if (!File.Exists(zeroInit))
            throw new InvalidOperationException(
                "V1 pass generation requires the deployed 0-Engine runtime. 0-Engine/init.lua was not found.");

        var source = File.ReadAllText(zeroInit);

        if (candidates.Any(x => x.Kind == CandidateKind.Action) &&
            !source.Contains("SubscribeAction", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This capture contains ACTION_ROUTING candidates, but the deployed 0-Engine does not expose SubscribeAction. V1 will not fabricate runtime infrastructure.");
        }

        if (candidates.Any(x => x.Kind == CandidateKind.Frame) &&
            !source.Contains("MakeEventRegistrar", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This capture contains FRAME_DISPATCH_CONSOLIDATION candidates, but the deployed 0-Engine does not expose MakeEventRegistrar. V1 will not fabricate runtime infrastructure.");
        }
    }

    private static TransformResult TransformFile(
        byte[] originalBytes,
        IReadOnlyList<PassCandidate> candidates,
        List<object> transformManifest,
        List<object> skipped)
    {
        var hasBom =
            originalBytes.Length >= 3 &&
            originalBytes[0] == 0xEF &&
            originalBytes[1] == 0xBB &&
            originalBytes[2] == 0xBF;

        var text = (hasBom
                ? Encoding.UTF8.GetString(originalBytes, 3, originalBytes.Length - 3)
                : Encoding.UTF8.GetString(originalBytes))
            .Replace("\r\n", "\n")
            .Replace('\r', '\n');

        var newline = originalBytes.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";
        var hadTerminalNewline = text.EndsWith('\n');
        var lines = text.Split('\n').ToList();
        if (hadTerminalNewline && lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var applied = 0;
        var frameHelpers = new List<(PassCandidate Candidate, string Token)>();

        foreach (var candidate in candidates)
        {
            if (candidate.LineStart > lines.Count)
            {
                skipped.Add(Skip(candidate, "Recorded source range is outside the current file."));
                continue;
            }

            if (candidate.Kind == CandidateKind.Frame)
            {
                var index = candidate.LineStart - 1;
                var line = lines[index];

                if (!line.Contains("registerForEvent", StringComparison.Ordinal) ||
                    !line.Contains("onUpdate", StringComparison.OrdinalIgnoreCase))
                {
                    skipped.Add(Skip(
                        candidate,
                        "Recorded source line is no longer a direct registerForEvent(onUpdate) registration."));
                    continue;
                }

                var token = $"__gcetRegisterEvent_{candidate.RegistrationId}";
                var marker = line.IndexOf("registerForEvent", StringComparison.Ordinal);
                lines[index] =
                    line[..marker] +
                    token +
                    line[(marker + "registerForEvent".Length)..];

                frameHelpers.Add((candidate, token));
                transformManifest.Add(new
                {
                    registrationId = candidate.RegistrationId,
                    owner = candidate.Owner,
                    type = "FRAME_DISPATCH_CONSOLIDATION",
                    file = candidate.RelativeFile,
                    sourceLines = new[] { candidate.LineStart, candidate.LineEnd }
                });
                applied++;
                continue;
            }

            if (candidate.LineEnd > lines.Count)
            {
                skipped.Add(Skip(candidate, "Recorded OnAction source range is outside the current file."));
                continue;
            }

            var segmentLines = lines
                .Skip(candidate.LineStart - 1)
                .Take(candidate.LineEnd - candidate.LineStart + 1)
                .ToArray();
            var segment = string.Join("\n", segmentLines);

            var opening = OnActionOpening.Match(segment);
            if (!opening.Success)
            {
                skipped.Add(Skip(
                    candidate,
                    "The analyzed OnAction Observe opening could not be revalidated in the recorded source range."));
                continue;
            }

            if (candidate.ActionPatterns.Any(x => UnsafeLuaPatternChars.IsMatch(x)))
            {
                skipped.Add(Skip(
                    candidate,
                    "The action-name pattern contains Lua pattern metacharacters. V1 only generates literal substring routing."));
                continue;
            }

            if (candidate.Actions.Length == 0 && candidate.ActionPatterns.Length == 0)
            {
                skipped.Add(Skip(candidate, "No concrete action names or action-name patterns were emitted by the resolver."));
                continue;
            }

            var functionName = $"__gcetOnAction_{candidate.RegistrationId}";
            var callbackArgs = opening.Groups["args"].Value;
            var replaced =
                segment[..opening.Index] +
                $"local function {functionName}({callbackArgs})" +
                segment[(opening.Index + opening.Length)..];

            var closing = OnActionClosing.Match(replaced);
            if (!closing.Success)
            {
                skipped.Add(Skip(
                    candidate,
                    "The analyzed OnAction Observe closing end) could not be revalidated in the recorded source range."));
                continue;
            }

            replaced =
                replaced[..closing.Index] +
                "end" +
                replaced[(closing.Index + closing.Length)..];

            var indent = Regex.Match(segmentLines[0], @"^\s*").Value;
            var generated = BuildActionRegistration(candidate, functionName, indent);

            var replacementLines = (replaced + "\n\n" + generated)
                .Split('\n');

            lines.RemoveRange(
                candidate.LineStart - 1,
                candidate.LineEnd - candidate.LineStart + 1);
            lines.InsertRange(candidate.LineStart - 1, replacementLines);

            transformManifest.Add(new
            {
                registrationId = candidate.RegistrationId,
                owner = candidate.Owner,
                type = candidate.Pattern,
                file = candidate.RelativeFile,
                sourceLines = new[] { candidate.LineStart, candidate.LineEnd },
                facts = new
                {
                    actions = candidate.Actions,
                    actionPatterns = candidate.ActionPatterns,
                    candidate.StateGatePresent,
                    candidate.ConsumerMutation,
                    candidate.RequiresActionType,
                    candidate.RequiresActionValue
                }
            });
            applied++;
        }

        if (frameHelpers.Count > 0)
        {
            var header = new List<string>
            {
                "-- G-CET generated frame registrar: source-selected from resolver evidence."
            };

            foreach (var item in frameHelpers.OrderBy(x => x.Candidate.RegistrationId))
            {
                var owner = LuaQuote(item.Candidate.Owner);
                header.Add($"local {item.Token} = registerForEvent");
                header.Add("do");
                header.Add("    local __gcetOk, __gcetEngine = pcall(GetMod, \"0-Engine\")");
                header.Add("    if __gcetOk and type(__gcetEngine) == \"table\" and type(__gcetEngine.MakeEventRegistrar) == \"function\" then");
                header.Add($"        {item.Token} = __gcetEngine.MakeEventRegistrar({owner}, registerForEvent)");
                header.Add("    end");
                header.Add("end");
                header.Add("");
            }

            lines.InsertRange(0, header);
        }

        var outputText = string.Join(newline, lines);
        if (hadTerminalNewline)
            outputText += newline;

        var body = Encoding.UTF8.GetBytes(outputText);
        if (!hasBom)
            return new TransformResult(body, applied);

        var withBom = new byte[body.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
        return new TransformResult(withBom, applied);
    }

    private static string BuildActionRegistration(
        PassCandidate candidate,
        string functionName,
        string indent)
    {
        var lines = new List<string>();
        var idBase = $"G-CET.{candidate.RegistrationId}";
        var owner = LuaQuote(candidate.Owner);

        lines.Add($"{indent}local __gcetRouted_{candidate.RegistrationId} = false");
        lines.Add($"{indent}local __gcetHandles_{candidate.RegistrationId} = {{}}");
        lines.Add($"{indent}local __gcetOk_{candidate.RegistrationId}, __gcetEngine_{candidate.RegistrationId} = pcall(GetMod, \"0-Engine\")");
        lines.Add($"{indent}if __gcetOk_{candidate.RegistrationId} and type(__gcetEngine_{candidate.RegistrationId}) == \"table\" and type(__gcetEngine_{candidate.RegistrationId}.SubscribeAction) == \"function\" then");
        lines.Add($"{indent}    __gcetRouted_{candidate.RegistrationId} = pcall(function()");

        if (candidate.Actions.Length > 0)
        {
            var actions = string.Join(", ", candidate.Actions.Select(LuaQuote));
            lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetEngine_{candidate.RegistrationId}.SubscribeAction({{");
            lines.Add($"{indent}            id = {LuaQuote(idBase + ".Exact")},");
            lines.Add($"{indent}            actions = {{ {actions} }}{(candidate.RequiresActionType ? "" : ",")}");
            if (!candidate.RequiresActionType)
                lines.Add($"{indent}            decodeType = false");
            lines.Add($"{indent}        }}, {functionName}, {owner})");
        }

        if (candidate.ActionPatterns.Length > 0)
        {
            lines.Add($"{indent}        local __gcetExact_{candidate.RegistrationId} = {{}}");
            foreach (var action in candidate.Actions)
                lines.Add($"{indent}        __gcetExact_{candidate.RegistrationId}[{LuaQuote(action)}] = true");

            lines.Add($"{indent}        __gcetHandles_{candidate.RegistrationId}[#__gcetHandles_{candidate.RegistrationId} + 1] = __gcetEngine_{candidate.RegistrationId}.SubscribeAction({{");
            lines.Add($"{indent}            id = {LuaQuote(idBase + ".Pattern")},");
            lines.Add($"{indent}            actions = \"*\",");
            lines.Add($"{indent}            decodeType = false");
            lines.Add($"{indent}        }}, function(this, action, consumer, routedName)");

            var conditions = string.Join(
                " or ",
                candidate.ActionPatterns.Select(x =>
                    $"string.find(routedName, {LuaQuote(x)}, 1, true)"));

            lines.Add($"{indent}            if routedName and not __gcetExact_{candidate.RegistrationId}[routedName] and ({conditions}) then");
            lines.Add($"{indent}                {functionName}(this, action, consumer)");
            lines.Add($"{indent}            end");
            lines.Add($"{indent}        end, {owner})");
        }

        lines.Add($"{indent}    end)");
        lines.Add($"{indent}    if not __gcetRouted_{candidate.RegistrationId} then");
        lines.Add($"{indent}        for _, __gcetHandle in ipairs(__gcetHandles_{candidate.RegistrationId}) do");
        lines.Add($"{indent}            if __gcetHandle and type(__gcetHandle.unsubscribe) == \"function\" then pcall(__gcetHandle.unsubscribe) end");
        lines.Add($"{indent}        end");
        lines.Add($"{indent}    end");
        lines.Add($"{indent}end");
        lines.Add($"{indent}if not __gcetRouted_{candidate.RegistrationId} then Observe(\"PlayerPuppet\", \"OnAction\", {functionName}) end");

        return string.Join("\n", lines);
    }

    private static string? ResolveInsideMods(string modsRoot, string relativeFile)
    {
        var candidate = Path.GetFullPath(
            Path.Combine(
                modsRoot,
                relativeFile.Replace('/', Path.DirectorySeparatorChar)));

        var rootPrefix =
            Path.GetFullPath(modsRoot).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return candidate.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
            ? candidate
            : null;
    }

    private static object Skip(PassCandidate candidate, string reason) => new
    {
        registrationId = candidate.RegistrationId,
        owner = candidate.Owner,
        file = candidate.RelativeFile,
        type = candidate.Kind.ToString().ToUpperInvariant(),
        reason
    };

    private static string LuaQuote(string value) =>
        "\"" +
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) +
        "\"";

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

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

    private enum CandidateKind
    {
        Action,
        Frame
    }

    private sealed class PassCandidate
    {
        public CandidateKind Kind { get; init; }
        public long RegistrationId { get; init; }
        public string Owner { get; init; } = "";
        public string RelativeFile { get; init; } = "";
        public string SourceSha256 { get; init; } = "";
        public int LineStart { get; init; }
        public int LineEnd { get; init; }
        public string Pattern { get; init; } = "";
        public string[] Actions { get; init; } = Array.Empty<string>();
        public string[] ActionPatterns { get; init; } = Array.Empty<string>();
        public bool RequiresActionType { get; init; }
        public bool RequiresActionValue { get; init; }
        public bool ConsumerMutation { get; init; }
        public bool StateGatePresent { get; init; }
    }

    private sealed class CandidateFacts
    {
        public string[] Actions { get; init; } = Array.Empty<string>();
        public string[] ActionPatterns { get; init; } = Array.Empty<string>();
        public bool RequiresActionType { get; init; }
        public bool RequiresActionValue { get; init; }
        public bool ConsumerMutation { get; init; }
        public bool StateGatePresent { get; init; }
    }

    private sealed record TransformResult(byte[] Bytes, int AppliedTransforms);
}
