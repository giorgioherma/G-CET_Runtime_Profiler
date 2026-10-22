namespace GCETRuntimeProfiler.Core.Services;

// Separate from existing outfit radar/normalization stages: only current
// source-proven idle reads and attach-owned record identity are changed.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyNpcOutfitRadarGc(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local function ProcessRadarOutfits()",
            "local function GetNPCID(target)",
            "activeNPCs[entId] = self",
            "activeNPCs[entId] = nil",
            "local __gcetHasAutoOutfitRules");
        var s=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string marker="__gcetNpcRadarGcV1";
        if(s.Contains(marker,StringComparison.Ordinal))
        {
            if(!s.Contains("__gcetRadarRecordCache[entIdStr]",StringComparison.Ordinal) ||
               !s.Contains("radarTimer + delta >= 1.0",StringComparison.Ordinal) ||
               !s.Contains("__gcetRadarRecordCache[entId] = nil",StringComparison.Ordinal))
                throw new InvalidOperationException("Partial NPC outfit radar stage.");
            return SemanticInjectionResult.Skip("NPC outfit radar stage already present.");
        }
        static string Once(string s,string old,string newer,string what)
        {
            if(s.Split(old,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("NPC Outfit Manager source changed: "+what);
            return s.Replace(old,newer,StringComparison.Ordinal);
        }
        // Per-instance NPC record ids are immutable for a living entity;
        // attach/detach both invalidate the scoped cache on streaming changes.
        s=Once(s,"local function ProcessRadarOutfits()",
            "-- "+marker+": radar only, never alter live GetNPCID used by UI.\n" +
            "local __gcetRadarRecordCache = {}\n" +
            "local function ProcessRadarOutfits()","record cache owner");
        s=Once(s,"            local recordIdStr = GetNPCID(npc)",
            "            local recordIdStr = __gcetRadarRecordCache[entIdStr]\n" +
            "            if not recordIdStr then\n" +
            "                recordIdStr = GetNPCID(npc)\n" +
            "                __gcetRadarRecordCache[entIdStr] = recordIdStr\n" +
            "            end","radar native GetRecordID");
        s=Once(s,"            activeNPCs[entIdStr] = nil",
            "            activeNPCs[entIdStr] = nil\n" +
            "            __gcetRadarRecordCache[entIdStr] = nil",
            "invalid stream cleanup");
        s=Once(s,"                    activeNPCs[entId] = self",
            "                    activeNPCs[entId] = self\n" +
            "                    __gcetRadarRecordCache[entId] = nil",
            "attach invalidation");
        s=Once(s,"                    activeNPCs[entId] = nil",
            "                    activeNPCs[entId] = nil\n" +
            "                    __gcetRadarRecordCache[entId] = nil",
            "detach invalidation");
        s=Once(s,"    if __gcetHasAutoOutfitRules or isCustomizeWindowOpen or isCameraModeActive",
            "    if (__gcetHasAutoOutfitRules and radarTimer + delta >= 1.0)\n" +
            "        or isCustomizeWindowOpen or isCameraModeActive",
            "passive look-at fallback");
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            s=s.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,s);
        return SemanticInjectionResult.Success(
            "Event-owned NPC record cache invalidated on attachment/detachment " +
            "and invalid NPC cleanup; passive look-at fallback runs at the existing " +
            "1 Hz radar cadence, while open UI, live animations, camera, naked " +
            "retries and shop target acquisition retain immediate timing.");
    }
}
