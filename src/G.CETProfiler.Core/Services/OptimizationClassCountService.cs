using System.Text;
using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

// Counts are for DISTINCT LIVE OWNER MODS, not callback registrations, files,
// rule IDs or sampled native allocation events. Classes may overlap by owner.
public sealed record OptimizationClassCount(
    string Class, int ModCount, int RuleCount, string[] Mods);

public sealed record ResolverOptimizationCoverage(
    int MeasuredModCount,
    int ReadyModCount,
    int CapturedReadyModCount,
    int SourceOnlyReadyModCount,
    int ReadyRuleCount,
    int AlreadySourceProvenModCount,
    int AlreadySourceProvenRuleCount,
    int GenericReadyModCount,
    int MaterialRemainingModCount,
    int BelowThresholdModCount,
    int AlreadySatisfiedCallbackCount,
    OptimizationClassCount[] ReadySemanticClasses,
    OptimizationClassCount[] AlreadySemanticClasses,
    OptimizationClassCount[] GenericReadyClasses);

public sealed record PassOptimizationCoverage(
    int AppliedModCount,
    int AppliedRuleCount,
    int ModifiedModFileCount,
    int FixedRuntimeFileCount,
    int SourceOnlyAppliedModCount,
    OptimizationClassCount[] AppliedSemanticClasses,
    OptimizationClassCount[] AppliedGenericClasses);

public static class OptimizationClassCountService
{
    private sealed class Bucket
    {
        internal HashSet<string> Mods { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal HashSet<string> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal void Add(string mod, string rule)
        {
            if (string.IsNullOrWhiteSpace(mod)) return;
            Mods.Add(mod);
            if (!string.IsNullOrWhiteSpace(rule))
                Rules.Add(mod + "\n" + rule);
        }
    }

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private static JsonElement Field(JsonElement row, string key) =>
        row.ValueKind == JsonValueKind.Object &&
        row.TryGetProperty(key, out var item) ? item : default;

    private static string Str(JsonElement row, string key) =>
        Field(row, key) is { ValueKind: JsonValueKind.String } item
            ? item.GetString() ?? "" : "";

    private static bool Flag(JsonElement row, string key) =>
        Field(row, key).ValueKind == JsonValueKind.True;

    private static IEnumerable<JsonElement> Items(JsonElement row, string key) =>
        Field(row, key) is { ValueKind: JsonValueKind.Array } item
            ? item.EnumerateArray() : Array.Empty<JsonElement>();

    private static void Add(Dictionary<string, Bucket> map,
        string classification, string mod, string rule)
    {
        if (string.IsNullOrWhiteSpace(classification) || string.IsNullOrWhiteSpace(mod))
            return;
        if (!map.TryGetValue(classification, out var bucket))
            map[classification] = bucket = new Bucket();
        bucket.Add(mod, rule);
    }

    private static OptimizationClassCount[] Sorted(Dictionary<string, Bucket> data) =>
        data.OrderBy(pair => pair.Key, Names)
            .Select(pair => new OptimizationClassCount(
                pair.Key,
                pair.Value.Mods.Count,
                pair.Value.Rules.Count,
                pair.Value.Mods.OrderBy(x => x, Names).ToArray()))
            .ToArray();

    private static int UniqueMods(Dictionary<string, Bucket> map) =>
        map.Values.SelectMany(x => x.Mods).Distinct(Names).Count();

    private static int UniqueRules(Dictionary<string, Bucket> map) =>
        map.Values.SelectMany(x => x.Rules).Distinct(Names).Count();

    public static ResolverOptimizationCoverage Analyze(JsonElement report)
    {
        var semanticReady = new Dictionary<string, Bucket>(Names);
        var semanticAlready = new Dictionary<string, Bucket>(Names);
        var genericReady = new Dictionary<string, Bucket>(Names);
        var measured = new HashSet<string>(Names);
        var capturedReady = new HashSet<string>(Names);
        var sourceOnlyReady = new HashSet<string>(Names);
        var material = new HashSet<string>(Names);
        var belowThreshold = new HashSet<string>(Names);
        var alreadyCallbacks = 0;

        foreach (var family in Items(report, "callbackFamilies"))
        foreach (var row in Items(family, "topConsumers"))
        {
            var owner = Str(row, "owner");
            if (string.IsNullOrWhiteSpace(owner)) continue;
            measured.Add(owner);
            var semantic = Field(row, "semantic");
            var generic = Field(row, "generic");
            var rule = Str(semantic, "RuleId");
            var policy = Str(semantic, "PolicyClass");
            var disposition = Str(row, "disposition");

            if (Flag(semantic, "generationReady"))
            {
                Add(semanticReady, policy, owner, rule);
                capturedReady.Add(owner);
            }
            if (Flag(semantic, "Matched") && Flag(semantic, "SourceProofSatisfied") &&
                Flag(semantic, "AlreadySatisfied"))
                Add(semanticAlready, policy, owner, rule);

            if (Flag(generic, "Automatable"))
                foreach (var recipe in Items(generic, "RecipeFamilies"))
                    if (recipe.ValueKind == JsonValueKind.String)
                        Add(genericReady, recipe.GetString() ?? "", owner, 
                            Str(row, "registrationId"));
            // Measured disposition remains diagnostic-only: it is never counted
            // as an actual optimization or a source-authorized class.
            if (disposition == "UNRESOLVED")
            {
                var mat = Field(row, "materiality");
                if (Flag(mat, "material")) material.Add(owner);
                else belowThreshold.Add(owner);
            }
            if (disposition == "ALREADY_SATISFIED") alreadyCallbacks++;
        }

        foreach (var row in Items(report, "semanticSourceOnlyCandidates"))
        {
            if (!Flag(row, "sourceProofSatisfied") ||
                !Flag(row, "generationEnabled") ||
                Flag(row, "alreadySatisfied")) continue;
            var owner = Str(row, "owner");
            Add(semanticReady, Str(row, "policyClass"), owner, Str(row, "ruleId"));
            if (!string.IsNullOrWhiteSpace(owner))
                sourceOnlyReady.Add(owner);
        }

        return new ResolverOptimizationCoverage(
            measured.Count, UniqueMods(semanticReady), capturedReady.Count,
            sourceOnlyReady.Count, UniqueRules(semanticReady),
            UniqueMods(semanticAlready), UniqueRules(semanticAlready),
            UniqueMods(genericReady), material.Count, belowThreshold.Count,
            alreadyCallbacks, Sorted(semanticReady), Sorted(semanticAlready),
            Sorted(genericReady));
    }

    public static PassOptimizationCoverage FromManifest(JsonElement manifest)
    {
        var semantics = new Dictionary<string, Bucket>(Names);
        var generics = new Dictionary<string, Bucket>(Names);
        var sourceOnly = new HashSet<string>(Names);
        var allOwners = new HashSet<string>(Names);
        var count = 0;
        foreach (var row in Items(manifest, "transforms"))
        {
            var owner = Str(row, "Owner");
            if (string.IsNullOrWhiteSpace(owner))
                owner = Str(row, "owner");
            var type = Str(row, "type");
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(type))
                continue;

            // One manifest transform may touch 2+ Lua source files but counts
            // as ONE rule, ONE mod, and ONE application to its policy class.
            count++;
            allOwners.Add(owner);
            if (type == "SEMANTIC_RULE")
            {
                Add(semantics, Str(row, "PolicyClass"), owner, Str(row, "RuleId"));
                if (Str(row, "admissionEvidence") == "SOURCE_PROVEN_NO_CAPTURE")
                    sourceOnly.Add(owner);
            }
            else
            {
                var klass = type == "SHARED_PROVIDER_READ"
                    ? type + "/" + Str(row, "provider")
                    : type;
                Add(generics, klass, owner, type + ":" + Str(row, "registrationId") +
                    ":" + Str(row, "file"));
            }
        }

        var modFiles = Items(manifest, "files").Count(f =>
            !Flag(f, "fixedInfrastructure"));
        var infraFiles = Items(manifest, "files").Count(f =>
            Flag(f, "fixedInfrastructure"));
        return new PassOptimizationCoverage(
            allOwners.Count, count, modFiles, infraFiles, sourceOnly.Count,
            Sorted(semantics), Sorted(generics));
    }

    public static PassOptimizationCoverage FromManifestFile(string manifestPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return FromManifest(doc.RootElement);
    }

    public static string FormatReady(ResolverOptimizationCoverage coverage)
    {
        var lines = new List<string>
        {
            "SOURCE-PROVEN OPTIMIZATION COUNTS — DISTINCT MODS:",
            $"  Measured mod owners: {coverage.MeasuredModCount}",
            $"  Semantic ready: {coverage.ReadyModCount} mods / {coverage.ReadyRuleCount} rules " +
            $"({coverage.CapturedReadyModCount} callback-linked mods; " +
            $"{coverage.SourceOnlyReadyModCount} source-only mods)",
            $"  Already semantic-complete: {coverage.AlreadySourceProvenModCount} mods / " +
            $"{coverage.AlreadySourceProvenRuleCount} source-proven rules",
            $"  Generic ready: {coverage.GenericReadyModCount} mods",
            "",
            "READY SEMANTIC CLASSES (mods per class):"
        };
        AddLines(lines, coverage.ReadySemanticClasses);
        lines.Add("");
        lines.Add("ALREADY SOURCE-PROVEN SEMANTIC CLASSES (mods per class):");
        AddLines(lines, coverage.AlreadySemanticClasses);
        lines.Add("");
        lines.Add("GENERIC READY CLASSES (mods per class):");
        AddLines(lines, coverage.GenericReadyClasses);
        lines.Add("");
        lines.Add("DIAGNOSTIC COUNTS (not applied optimizations):");
        lines.Add($"  Material unresolved: {coverage.MaterialRemainingModCount} mods; " +
            $"below-threshold unresolved: {coverage.BelowThresholdModCount} mods");
        lines.Add($"  Already satisfied callback registrations: " +
            $"{coverage.AlreadySatisfiedCallbackCount} (NOT mods)");
        lines.Add("Class counts may overlap: one mod can contain several proven rules.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatApplied(PassOptimizationCoverage coverage)
    {
        var lines = new List<string>
        {
            "ACTUALLY GENERATED — DISTINCT MODS / OPTIMIZATION CLASSES:",
            $"  Applied: {coverage.AppliedModCount} mods / {coverage.AppliedRuleCount} transforms",
            $"  Modified author files: {coverage.ModifiedModFileCount}; " +
            $"fixed runtime infrastructure: {coverage.FixedRuntimeFileCount}",
            $"  Source-only admission: {coverage.SourceOnlyAppliedModCount} mods",
            "",
            "APPLIED SEMANTIC CLASSES (mods per class):"
        };
        AddLines(lines, coverage.AppliedSemanticClasses);
        lines.Add("");
        lines.Add("APPLIED GENERIC CLASSES (mods per class):");
        AddLines(lines, coverage.AppliedGenericClasses);
        lines.Add("");
        lines.Add("Counts describe generated source transforms, NOT measured savings or" +
            " gameplay validation. A mod may appear in multiple classes.");
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddLines(List<string> lines, OptimizationClassCount[] rows)
    {
        if (rows.Length == 0) { lines.Add("  (none)"); return; }
        foreach (var row in rows)
            lines.Add($"  {row.Class}: {row.ModCount} mod(s) / " +
                $"{row.RuleCount} rule(s) — {string.Join(", ", row.Mods)}");
    }
}
