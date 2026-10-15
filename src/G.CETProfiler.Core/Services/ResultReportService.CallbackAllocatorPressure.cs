using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

public static partial class ResultReportService
{
    // Owner-scoped positive growth, never the shared pre-dispatch argument
    // conversion and never the native/C++ heap. This is an exact partition
    // of existing callback-owned G-GC bytes, not an additional allocation.
    private sealed record CallbackAllocatorPressureRow(
        long RegistrationId, string Mod, string Kind, string Target,
        long ReallocPositiveBytes, long ReallocOperations,
        long NewObjectPositiveBytes, long NewObjectOperations,
        long NativeBoundaryPositiveBytes, long NativeBoundaryOperations,
        long OutsideNativeBoundaryPositiveBytes, long OutsideNativeBoundaryOperations,
        long TotalPositiveBytes, long TotalOperations, bool Reconciled,
        bool IdentityVerified);

    private sealed class CallbackAllocatorPressureReport
    {
        public List<CallbackAllocatorPressureRow> Rows { get; init; } = [];
        public bool AllReconciled { get; init; }
    }

    private static CallbackAllocatorPressureReport? AnalyzeCallbackAllocatorPressure(
        IReadOnlyList<Dictionary<string, string>> pressure,
        IReadOnlyList<Dictionary<string, string>> registrations)
    {
        if (pressure.Count == 0)
            return null; // Legacy input: NOT_MEASURED, not zero.

        var lookup = registrations.Where(r => L(r, "RegistrationId") > 0)
            .GroupBy(r => L(r, "RegistrationId"))
            .ToDictionary(g => g.Key, g => g.First());
        var rows = pressure.Select(r =>
        {
            long id = L(r, "RegistrationId");
            long reallocBytes = L(r, "ReallocPositiveBytes");
            long reallocOps = L(r, "ReallocOperations");
            long newBytes = L(r, "NewObjectPositiveBytes");
            long newOps = L(r, "NewObjectOperations");
            long nativeBytes = L(r, "NativeBoundaryPositiveBytes");
            long nativeOps = L(r, "NativeBoundaryOperations");
            long outsideBytes = L(r, "OutsideNativeBoundaryPositiveBytes");
            long outsideOps = L(r, "OutsideNativeBoundaryOperations");
            long bytes = L(r, "TotalPositiveBytes");
            long ops = L(r, "TotalOperations");
            // Native registration IDs >=32767 share an overflow bucket;
            // never claim per-callback identity from its aggregate.
            var hasOwner = id > 0 && id < 32767 &&
                lookup.TryGetValue(id, out var registered) &&
                S(registered, "Mod") == S(r, "Mod") &&
                S(registered, "Kind") == S(r, "Kind") &&
                S(registered, "Target") == S(r, "Target") &&
                L(registered, "AllocatedBytes") == bytes &&
                L(registered, "AllocationOperations") == ops;
            bool reconciled = L(r, "Reconciled") == 1 &&
                reallocBytes >= 0 && reallocOps >= 0 &&
                newBytes >= 0 && newOps >= 0 &&
                nativeBytes >= 0 && nativeOps >= 0 &&
                outsideBytes >= 0 && outsideOps >= 0 &&
                bytes == reallocBytes + newBytes &&
                ops == reallocOps + newOps &&
                bytes == nativeBytes + outsideBytes &&
                ops == nativeOps + outsideOps &&
                (id == 32767 || hasOwner);
            return new CallbackAllocatorPressureRow(
                id, S(r, "Mod"), S(r, "Kind"), S(r, "Target"),
                reallocBytes, reallocOps, newBytes, newOps,
                nativeBytes, nativeOps, outsideBytes, outsideOps,
                bytes, ops, reconciled, hasOwner && reconciled);
        }).OrderByDescending(x => x.TotalPositiveBytes)
          .ThenByDescending(x => x.TotalOperations).ToList();
        return new CallbackAllocatorPressureReport
        {
            Rows = rows,
            AllReconciled = rows.All(x => x.Reconciled)
        };
    }

    private static void AppendCallbackAllocatorPressure(
        StringBuilder sb, CallbackAllocatorPressureReport? pressure)
    {
        if (pressure is null) return;
        var rows = pressure.Rows.Where(x => x.IdentityVerified).ToList();
        sb.Append("<div class=\"section\"><h2>Callback-owned LuaJIT allocation pressure</h2>")
          .Append("<p class=\"note\">Measured positive allocator growth inside known ")
          .Append("callback execution windows, not shared pre-dispatch native ")
          .Append("argument marshalling, native/C++ heap allocations, or savings. ")
          .Append("These bytes are already included in the G-GC registration totals ")
          .Append("and must not be added again.</p>")
          .Append("<p><b>All native partition rows reconcile:</b> ")
          .Append(pressure.AllReconciled ? "YES" : "NO — inspect raw CSV")
          .Append(" | <b>Verified registration owners:</b> ")
          .Append(N(rows.Count)).Append("</p>");
        sb.Append("<table><thead><tr><th>Owner</th><th>Callback</th>")
          .Append("<th>Positive growth bytes</th><th>Allocator operations</th>")
          .Append("<th>Inside native boundary bytes</th></tr></thead><tbody>");
        foreach(var row in rows.OrderByDescending(x=>x.TotalPositiveBytes).Take(35))
            sb.Append("<tr><td>").Append(H(row.Mod)).Append("</td><td>")
              .Append(H(row.Kind)).Append(" ").Append(H(row.Target))
              .Append("</td><td>").Append(N(row.TotalPositiveBytes))
              .Append("</td><td>").Append(N(row.TotalOperations))
              .Append("</td><td>").Append(N(row.NativeBoundaryPositiveBytes))
              .Append("</td></tr>");
        sb.Append("</tbody></table>");
        if (pressure.Rows.Any(x=>!x.IdentityVerified))
            sb.Append("<p class=\"note\">Unverified or overflow rows are excluded ")
              .Append("from owner rankings. See raw CSV for their original evidence.</p>");
        sb.Append("</div>");
    }

    private static object? BuildCallbackAllocatorPressureInterop(
        CallbackAllocatorPressureReport? pressure)
    {
        if (pressure is null) return null;
        // Only verified identities are ranked. Mismatches remain visible
        // as an explicit unsafe/unknown evidence class, not reattributed.
        var owned = pressure.Rows.Where(x => x.IdentityVerified).ToList();
        return new
        {
            metric = "REGISTRATION_SCOPED_POSITIVE_LUAJIT_ALLOCATOR_PRESSURE",
            input = "CET_Runtime_Profile_GGC_CallbackAllocatorPressure.csv",
            attribution = "EXACT_ACTIVE_CALLBACK_EXECUTION_WINDOW_NOT_PRE_DISPATCH_MARSHALLING",
            measurement = "POSITIVE_LUAJIT_ALLOCATOR_GROWTH_NOT_NATIVE_HEAP_OR_RETAINED_MEMORY",
            additiveToGcTracerRegistrationBytes = false,
            sharedNativeMarshallingAttributedToSubscribers = false,
            nativeAllocationSavingsMeasured = false,
            automaticRewriteAuthorized = false,
            pressureAvailable = true,
            allRowsReconciled = pressure.AllReconciled,
            callbackRows = owned.Count,
            positiveBytes = owned.Sum(x => x.TotalPositiveBytes),
            allocationOperations = owned.Sum(x => x.TotalOperations),
            nativeBoundaryPositiveBytes = owned.Sum(x => x.NativeBoundaryPositiveBytes),
            nativeBoundaryAllocationOperations = owned.Sum(x => x.NativeBoundaryOperations),
            unattributableRows = pressure.Rows.Where(x => !x.IdentityVerified)
                .Select(x => new { x.RegistrationId, x.Mod, x.TotalPositiveBytes,
                    x.TotalOperations, x.Reconciled }),
            byMod = owned.GroupBy(x => x.Mod).Select(g => new
            {
                owner = g.Key,
                positiveBytes = g.Sum(x => x.TotalPositiveBytes),
                allocationOperations = g.Sum(x => x.TotalOperations),
                nativeBoundaryPositiveBytes = g.Sum(x => x.NativeBoundaryPositiveBytes),
                nativeBoundaryAllocationOperations = g.Sum(x => x.NativeBoundaryOperations),
                callbackCount = g.Count()
            }).OrderByDescending(x => x.positiveBytes).ToArray(),
            callbacks = owned,
            interpretation = "Exact G-GC positive growth while a known callback owns the execution window. Shared pre-dispatch native->Lua marshalling remains unassigned and is never divided among subscribers."
        };
    }
}
