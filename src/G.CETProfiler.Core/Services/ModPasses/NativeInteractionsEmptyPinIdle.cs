using System.Text.RegularExpressions;
namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNativeInteractionsEmptyPinIdle(SemanticPatchContext context)
    {
        var file=context.FindFile("modules/utils/worldInteraction.lua",
            "ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\"",
            "world.pinInteractions",
            "world.pinnedInteractions",
            "local function findPinInteraction(mappin, mappinID)");
        const string marker="__gcetNifEmptyPinIdleV1";
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        if(s.Contains(marker,StringComparison.Ordinal))
        {
            if(!s.Contains("if next(world.pinInteractions) == nil and next(world.pinnedInteractions) == nil then return end",
                StringComparison.Ordinal))
                throw new InvalidOperationException("Partial NIF empty-pin filter.");
            return SemanticInjectionResult.Skip("NIF no-pins observer fast path already complete.");
        }
        // Both pin tables are cleared together on session load, and populated
        // before any known pin-controller callback can pass the existing
        // author findPinInteraction cold fallback. If no pinned markers exist,
        // the observer cannot modify a marker. Keep all native calls/cold
        // spatial fallback unchanged while any marker is active.
        // The original observer carries "-- Custom pin texture"; the
        // existing base nativeInteractions injector retains the identical
        // observer but normalizes that trailing comment away. Recognize both
        // source-equivalent openings, never arbitrary observer bodies.
        var opening = new Regex(
            @"(?m)^(?<indent>[ \t]*)ObserveAfter\(""BaseMappinBaseController"",[ \t]*""UpdateRootState"",[ \t]*function\(this\)(?:[ \t]*-- Custom pin texture)?[ \t]*$",
            RegexOptions.CultureInvariant);
        if(opening.Matches(s).Count!=1 ||
           !s.Contains("world.pinnedInteractions = {}",StringComparison.Ordinal) ||
           !s.Contains("world.pinInteractions = {}",StringComparison.Ordinal) ||
           !s.Contains("indexPin(interaction)",StringComparison.Ordinal) ||
           !s.Contains("unindexPin(interaction)",StringComparison.Ordinal) ||
           !s.Contains("local function findPinInteraction(mappin, mappinID)",StringComparison.Ordinal))
            throw new InvalidOperationException("NIF pin inventory/lifecycle source changed.");
        s=opening.Replace(s, m => m.Value+"\n"+
            m.Groups["indent"].Value+"    -- "+marker+
            ": empty owner inventories mean no NIF pin can be styled.\n"+
            m.Groups["indent"].Value+
            "    if next(world.pinInteractions) == nil and next(world.pinnedInteractions) == nil then return end",
            1);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            s=s.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,s);
        return SemanticInjectionResult.Success(
            "Skip the global native mappin/profile reads when both source-owned " +
            "NIF marker tables are empty; preserve unchanged normal and cold " +
            "spatial fallback while any mod marker may be registered.");
    }
}
