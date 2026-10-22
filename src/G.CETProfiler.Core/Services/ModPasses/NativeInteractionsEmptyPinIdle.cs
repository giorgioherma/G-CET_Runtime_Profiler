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
        const string old=
            "    ObserveAfter(\"BaseMappinBaseController\", \"UpdateRootState\", function(this) -- Custom pin texture\n";
        const string updated=old+
            "        -- "+marker+": empty owner inventories mean no NIF pin can be styled.\n"+
            "        if next(world.pinInteractions) == nil and next(world.pinnedInteractions) == nil then return end\n";
        if(s.Split(old,StringSplitOptions.None).Length!=2 ||
           !s.Contains("world.pinnedInteractions = {}",StringComparison.Ordinal) ||
           !s.Contains("world.pinInteractions = {}",StringComparison.Ordinal) ||
           !s.Contains("indexPin(interaction)",StringComparison.Ordinal) ||
           !s.Contains("unindexPin(interaction)",StringComparison.Ordinal))
            throw new InvalidOperationException("NIF pin inventory/lifecycle source changed.");
        s=s.Replace(old,updated,StringComparison.Ordinal);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            s=s.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,s);
        return SemanticInjectionResult.Success(
            "Skip the global native mappin/profile reads when both source-owned " +
            "NIF marker tables are empty; preserve unchanged normal and cold " +
            "spatial fallback while any mod marker may be registered.");
    }
}
