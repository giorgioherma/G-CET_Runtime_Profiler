namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyF80hActiveGcSweep(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local SWEEP_INTERVAL   = 1.5",
            "local PENDING_TTL      = 12.0",
            "local womenByAnchor = {}",
            "CoupleVariance.Update(",
            "for hash, data in pairs(pendingAppearance) do");
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        const string marker="__gcetF80hActiveGcSweepV1";
        if(text.Contains(marker,StringComparison.Ordinal))
        {
            if(!text.Contains("data.since = data.since + __gcetSweepElapsed",StringComparison.Ordinal) ||
               !text.Contains("if isSoundEnabled() and audioCheckTimer + delta >= AUDIO_CHECK_INTERVAL then",StringComparison.Ordinal) ||
               !text.Contains("__gcetVarianceDispose",StringComparison.Ordinal))
                throw new InvalidOperationException("Partial f80h active GC stage.");
            return SemanticInjectionResult.Skip("f80h active GC stage already complete.");
        }
        static string Once(string s,string old,string newer,string label)
        {
            if(s.Split(old,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("f80h current source changed: "+label);
            return s.Replace(old,newer,StringComparison.Ordinal);
        }
        const string varianceStart=
            "  -- Couple variance (time-based + random despawn)\n" +
            "  if CoupleVariance and CoupleVariance.Update then\n" +
            "    CoupleVariance.Update(\n" +
            "      delta,\n" +
            "      settings,\n" +
            "      ANCHORS,\n" +
            "      tracked,\n" +
            "      function(npc)\n" +
            "        if npc and npc.Dispose then\n" +
            "          pcall(function() npc:Dispose() end)\n" +
            "        end\n" +
            "      end,\n" +
            "      function(anchorName)\n" +
            "        setAnchorWoman(anchorName, nil)\n" +
            "      end\n" +
            "    )\n" +
            "  end\n";
        const string varianceNew=
            "  -- "+marker+": no frame-local closure pair; author callbacks unchanged.\n" +
            "  if CoupleVariance and CoupleVariance.Update then\n" +
            "    CoupleVariance.Update(delta, settings, ANCHORS, tracked,\n" +
            "      __gcetVarianceDispose, __gcetVarianceClear)\n" +
            "  end\n";
        const string helpers=
            "-- "+marker+": preserve the two original callback bodies, allocate only once.\n" +
            "local function __gcetVarianceDispose(npc)\n" +
            "  if npc and npc.Dispose then pcall(function() npc:Dispose() end) end\n" +
            "end\n" +
            "local function __gcetVarianceClear(anchorName)\n" +
            "  setAnchorWoman(anchorName, nil)\n" +
            "end\n\n";
        text=Once(text,"---------------------------------------------------------\n-- UPDATE LOOP\n---------------------------------------------------------\n",
            helpers+"---------------------------------------------------------\n-- UPDATE LOOP\n---------------------------------------------------------\n","stable variance callback insertion");
        text=Once(text,varianceStart,varianceNew,"per-frame temporary variance closures");
        const string audioHead="  local womenByAnchor = {}   -- anchorName -> NPCPuppet\n";
        const string audioTail=
            "  for _, anchor in ipairs(ANCHORS) do\n" +
            "    setAnchorWoman(anchor.name, womenByAnchor[anchor.name])\n" +
            "  end\n";
        var start=text.IndexOf(audioHead,StringComparison.Ordinal);
        var end=start>=0?text.IndexOf(audioTail,start,StringComparison.Ordinal):-1;
        if(start<0 || end<0 || text.IndexOf(audioHead,start+1,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("f80h audio owner block changed.");
        end+=audioTail.Length;
        var block=text[start..end];
        text=text[..start]+
            "  -- Audio owner selection is aligned with the author's one-second\n"+
            "  -- distance/audio tick, not rebuilt on every animation frame.\n"+
            "  if isSoundEnabled() and audioCheckTimer + delta >= AUDIO_CHECK_INTERVAL then\n"+
            string.Join("\n",block.TrimEnd('\n').Split('\n').Select(s=>"  "+s))+"\n"+
            "  end\n"+text[end..];
        const string sweep=
            "  if sweepTimer < SWEEP_INTERVAL then return end\n" +
            "  sweepTimer = 0\n";
        text=Once(text,sweep,
            "  if sweepTimer < SWEEP_INTERVAL then return end\n" +
            "  local __gcetSweepElapsed = sweepTimer\n" +
            "  sweepTimer = 0\n","pending sweep elapsed time");
        if(text.Split("      data.since = data.since + delta",StringSplitOptions.None).Length!=3)
            throw new InvalidOperationException("f80h pending age writers changed.");
        text=text.Replace("      data.since = data.since + delta",
            "      data.since = data.since + __gcetSweepElapsed",StringComparison.Ordinal);
        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            text=text.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Correct 12-second pending expiration using elapsed 1.5-second sweep time; " +
            "allocate CoupleVariance callbacks once; reconcile selected women on original " +
            "one-second audio tick rather than each rendered frame. Facial and LookAt " +
            "updates, observed NPCs and native FAR triggers remain at author cadence.");
    }
}
