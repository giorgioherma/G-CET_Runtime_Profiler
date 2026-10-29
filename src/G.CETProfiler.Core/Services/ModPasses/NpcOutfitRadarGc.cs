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
            s=UpgradeRadarQueueV2(s);
            if(file.Text.Contains("\r\n",StringComparison.Ordinal))
                s=s.Replace("\n","\r\n",StringComparison.Ordinal);
            context.Write(file,s);
            return SemanticInjectionResult.Success("Upgraded existing NPC outfit radar to attachment-owned candidate scanning with bounded fallback.");
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
        s=UpgradeRadarQueueV2(s);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            s=s.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,s);
        return SemanticInjectionResult.Success(
            "Event-owned NPC record cache invalidated on attachment/detachment " +
            "and invalid NPC cleanup; passive look-at fallback runs at the existing " +
            "1 Hz radar cadence, while open UI, live animations, camera, naked " +
            "retries and shop target acquisition retain immediate timing.");
    }

    // An author attach event queues only changed entities. Exact automatic
    // outfit rule mutations force a full scan; a bounded ten-second full scan
    // retains the original safety net for missed native streaming events.
    // The author 1Hz radar tick and UI target acquisition stay unchanged.
    private static string UpgradeRadarQueueV2(string source)
    {
        const string stage="__gcetNpcRadarQueueV2";
        if(source.Contains(stage,StringComparison.Ordinal))
        {
            if(!source.Contains("__gcetNpcRadarQueueV2[entId] = self",StringComparison.Ordinal) ||
               !source.Contains("__gcetNpcRadarSweepV2 >= 10.0",StringComparison.Ordinal) ||
               !source.Contains("__gcetNpcRadarRulesChangedV2()",StringComparison.Ordinal) ||
               !source.Contains("__gcetNpcRadarQueueV2[entIdStr] = nil",StringComparison.Ordinal))
                throw new InvalidOperationException("NPC Outfit queue stage incomplete.");
            return source;
        }
        static string Once(string s,string old,string replacement,string why)
        {
            if(s.Split(old,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("NPC Outfit v2 "+why+" author source drift.");
            return s.Replace(old,replacement,StringComparison.Ordinal);
        }
        source=Once(source,
            "local __gcetRadarRecordCache = {}\nlocal function ProcessRadarOutfits()",
            "local __gcetRadarRecordCache = {}\n" +
            "local __gcetNpcRadarQueueV2 = {}\n" +
            "local __gcetNpcRadarSweepV2 = 10.0\n" +
            "local __gcetNpcRadarRuleStateV2 = {}\n" +
            "local function __gcetNpcRadarRulesChangedV2()\n" +
            "    local rules = autoOutfitsData and autoOutfitsData.outfits\n" +
            "    if type(rules) ~= 'table' then return false end\n" +
            "    local changed = false\n" +
            "    for key, value in pairs(rules) do\n" +
            "        if __gcetNpcRadarRuleStateV2[key] ~= value then\n" +
            "            __gcetNpcRadarRuleStateV2[key] = value\n" +
            "            changed = true\n" +
            "        end\n" +
            "    end\n" +
            "    for key in pairs(__gcetNpcRadarRuleStateV2) do\n" +
            "        if rules[key] == nil then\n" +
            "            __gcetNpcRadarRuleStateV2[key] = nil\n" +
            "            changed = true\n" +
            "        end\n" +
            "    end\n" +
            "    return changed\n" +
            "end\n" +
            "local function ProcessRadarOutfits()",
            "scanner and state owner");
        source=Once(source,"    for entIdStr, npc in pairs(activeNPCs) do",
            "    if __gcetNpcRadarRulesChangedV2() then __gcetNpcRadarSweepV2 = 10.0 end\n" +
            "    local fullSweep = __gcetNpcRadarSweepV2 >= 10.0\n" +
            "    local scan = fullSweep and activeNPCs or __gcetNpcRadarQueueV2\n" +
            "    if fullSweep then __gcetNpcRadarSweepV2 = 0.0 end\n" +
            "    for entIdStr, npc in pairs(scan) do\n" +
            "        __gcetNpcRadarQueueV2[entIdStr] = nil",
            "scan ownership");
        source=Once(source,
            "                    activeNPCs[entId] = self\n                    __gcetRadarRecordCache[entId] = nil",
            "                    activeNPCs[entId] = self\n                    __gcetRadarRecordCache[entId] = nil\n" +
            "                    __gcetNpcRadarQueueV2[entId] = self",
            "attach wake");
        source=Once(source,
            "                    activeNPCs[entId] = nil\n                    __gcetRadarRecordCache[entId] = nil",
            "                    activeNPCs[entId] = nil\n                    __gcetRadarRecordCache[entId] = nil\n" +
            "                    __gcetNpcRadarQueueV2[entId] = nil",
            "detach cleanup");
        source=Once(source,
            "            activeNPCs[entIdStr] = nil\n            __gcetRadarRecordCache[entIdStr] = nil",
            "            activeNPCs[entIdStr] = nil\n            __gcetRadarRecordCache[entIdStr] = nil\n" +
            "            __gcetNpcRadarQueueV2[entIdStr] = nil",
            "invalid entity cleanup");
        const string target="            activeNPCs[entIdStr] = currentTarget";
        if(source.Contains(target,StringComparison.Ordinal))
            source=Once(source,target,
                target+"\n            __gcetNpcRadarQueueV2[entIdStr] = currentTarget",
                "look-at fallback");
        source=Once(source,"    radarTimer = radarTimer + delta",
            "    __gcetNpcRadarSweepV2 = __gcetNpcRadarSweepV2 + (delta or 0.0)\n" +
            "    radarTimer = radarTimer + delta",
            "full sweep clock");
        return source;
    }
}
