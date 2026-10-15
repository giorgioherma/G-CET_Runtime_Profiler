using System.Text.Json;

namespace GCETRuntimeProfiler.Core.Services;

// Resolver reads already-validated registration-scoped growth from the Profiler
// handoff. A shared native marshalling sample is never an owner/savings claim.
// Preserve full metadata, but use only rows with exact owner/kind/target/ID.
internal sealed record OwnedCallbackAllocationEvidence(
    long RegistrationId, string Owner, string Kind, string Target,
    long TotalPositiveBytes, long TotalOperations,
    long NativeBoundaryPositiveBytes, long NativeBoundaryOperations);

internal sealed class CallbackAllocatorPressureEvidence
{
    private readonly Dictionary<long, OwnedCallbackAllocationEvidence> _rows;
    public bool Available { get; }
    public JsonElement? FullEvidence { get; }

    public CallbackAllocatorPressureEvidence(bool available, JsonElement? fullEvidence,
        Dictionary<long, OwnedCallbackAllocationEvidence> rows)
    {
        Available = available;
        FullEvidence = fullEvidence;
        _rows = rows;
    }

    public OwnedCallbackAllocationEvidence? Lookup(long? id,
        string owner, string kind, string target)
    {
        if (id is not long key || !_rows.TryGetValue(key, out var row))
            return null;
        return string.Equals(row.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(row.Kind, kind, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(row.Target, target, StringComparison.OrdinalIgnoreCase)
               ? row : null;
    }
}

internal static class CallbackAllocatorPressureEvidenceReader
{
    internal static CallbackAllocatorPressureEvidence Read(JsonElement handoff)
    {
        var rows = new Dictionary<long, OwnedCallbackAllocationEvidence>();
        if (!handoff.TryGetProperty("callbackAllocatorPressure", out var evidence) ||
            evidence.ValueKind != JsonValueKind.Object)
            return new CallbackAllocatorPressureEvidence(false, null, rows);
        if (Text(evidence, "metric") !=
                "REGISTRATION_SCOPED_POSITIVE_LUAJIT_ALLOCATOR_PRESSURE" ||
            !Bool(evidence, "pressureAvailable") ||
            Bool(evidence, "sharedNativeMarshallingAttributedToSubscribers") ||
            Bool(evidence, "additiveToGcTracerRegistrationBytes") ||
            Bool(evidence, "automaticRewriteAuthorized"))
            return new CallbackAllocatorPressureEvidence(false, null, rows);
        if (evidence.TryGetProperty("callbacks", out var callbacks) &&
            callbacks.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in callbacks.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !Bool(item, "IdentityVerified") ||
                    !Bool(item, "Reconciled"))
                    continue;
                long id = Number(item, "RegistrationId");
                string owner = Text(item, "Mod");
                string kind = Text(item, "Kind");
                string target = Text(item, "Target");
                long bytes = Number(item, "TotalPositiveBytes");
                long ops = Number(item, "TotalOperations");
                long nativeBytes = Number(item, "NativeBoundaryPositiveBytes");
                long nativeOps = Number(item, "NativeBoundaryOperations");
                if (id <= 0 || id >= 32767 || owner.Length == 0 ||
                    kind.Length == 0 || target.Length == 0 ||
                    bytes < 0 || ops < 0 || nativeBytes < 0 || nativeOps < 0 ||
                    nativeBytes > bytes || nativeOps > ops)
                    continue;
                // Ambiguous duplicate IDs are not a valid exact owner mapping.
                if (rows.ContainsKey(id))
                    rows.Remove(id);
                else
                    rows[id] = new OwnedCallbackAllocationEvidence(
                        id, owner, kind, target, bytes, ops, nativeBytes, nativeOps);
            }
        }
        return new CallbackAllocatorPressureEvidence(true, evidence.Clone(), rows);
    }

    private static bool Bool(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static long Number(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number)
            ? number : -1;
}
