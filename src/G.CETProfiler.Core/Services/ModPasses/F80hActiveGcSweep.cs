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
        var activeGcComplete=text.Contains(marker,StringComparison.Ordinal);
        if(activeGcComplete &&
           (!text.Contains("data.since = data.since + __gcetSweepElapsed",StringComparison.Ordinal) ||
            !text.Contains("if isSoundEnabled() and audioCheckTimer + delta >= AUDIO_CHECK_INTERVAL then",StringComparison.Ordinal) ||
            !text.Contains("__gcetVarianceDispose",StringComparison.Ordinal)))
            throw new InvalidOperationException("Partial f80h active GC stage.");
        if(!activeGcComplete)
        {
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
        } // End of existing active-GC transformation; PASS8 composes in this SAME owner.

        // The legacy active-GC contract also supports independently
        // source-proven trimmed fixtures with no runtime state, Cron or
        // OnDetach owner at all. Preserve the existing GC-only behavior there.
        // On real authored files, ANY runtime evidence means PASS8 must prove
        // the complete lifecycle or refuse source drift (never partial emit).
        var pass8RuntimeOwner = text.Contains("local preActiveQueue =",StringComparison.Ordinal) ||
            text.Contains("  Cron.Update(delta)",StringComparison.Ordinal) ||
            text.Contains("ObserveAfter(\"NPCPuppet\", \"OnDetach\"",StringComparison.Ordinal) ||
            text.Contains("local idleCronElapsed =",StringComparison.Ordinal);
        if(!pass8RuntimeOwner)
        {
            if(activeGcComplete)
                return SemanticInjectionResult.Skip("Original active GC stage already complete; no PASS8 runtime owner.");
            if(file.Text.Contains("\r\n",StringComparison.Ordinal))
                text=text.Replace("\n","\r\n",StringComparison.Ordinal);
            context.Write(file,text);
            return SemanticInjectionResult.Success(
                "Original active-GC source-only stage; no runtime lifecycle exists to authorize PASS8.");
        }

        // Prior Phase 1+2 PASS8 code came from an already-optimized source.
        // Reproduce it directly from pristine current author source, after
        // generic/native and alongside the existing active-GC stage.
        // Do not require any previous PASS8 marker or AudioXL implementation.
        const string pass8Marker="-- G-CET f80h idle-runtime-v1";
        var idleComplete=text.Contains("local idleCronElapsed =",StringComparison.Ordinal);
        if(idleComplete)
        {
            if(!text.Contains("idleCronElapsed >= 0.50",StringComparison.Ordinal) ||
               !text.Contains("next(preActiveQueue) ~= nil",StringComparison.Ordinal))
                throw new InvalidOperationException("Partial pre-existing f80h PASS8 idle lane.");
        }
        else
        {
            if(text.Split("local preActiveQueue = {}",StringSplitOptions.None).Length!=2 ||
               text.Split("  Cron.Update(delta)\n",StringSplitOptions.None).Length!=2 ||
               !text.Contains("  -- ensure we seed as soon as game time becomes available",StringComparison.Ordinal))
                throw new InvalidOperationException("f80h pristine idle lifecycle not source-proven.");
            text=text.Replace("local preActiveQueue = {}",
                "local preActiveQueue = {}\n"+
                "local idleCronElapsed = 0.0 "+pass8Marker,
                StringComparison.Ordinal);
            const string reset="  pendingAppearance = {}\n  sweepTimer = 0";
            if(text.Split(reset,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("f80h runtime reset owner changed.");
            text=text.Replace(reset,
                "  pendingAppearance = {}\n  idleCronElapsed = 0.0\n  sweepTimer = 0",
                StringComparison.Ordinal);
            const string oldUpdate="  Cron.Update(delta)\n\n  -- ensure we seed as soon as game time becomes available";
            const string newUpdate= """
  -- G-CET f80h idle-runtime-v1: no active entity, pending registration or
  -- audio owner means only the original Cron maintenance needs periodic work.
  -- Preserve all author realtime timer behavior while runtime is active.
  if scriptActive and isEnabled() then
    local __gcetRuntimeBusy = next(tracked) ~= nil
      or next(pending) ~= nil or next(pendingAppearance) ~= nil
      or next(preActiveQueue) ~= nil or next(anchorAudio) ~= nil
    if not __gcetRuntimeBusy then
      if not rngSeeded then seedRngOnce() end
      idleCronElapsed = idleCronElapsed + (delta or 0)
      if idleCronElapsed >= 0.50 then
        Cron.Update(idleCronElapsed)
        idleCronElapsed = 0.0
      end
      return
    end
  end
  idleCronElapsed = 0.0
  Cron.Update(delta)

  -- ensure we seed as soon as game time becomes available
""";
            if(text.Split(oldUpdate,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException("f80h update owner changed.");
            text=text.Replace(oldUpdate,newUpdate.TrimEnd('\r','\n'),
                StringComparison.Ordinal);
        }

        // Deactivation of a streaming-out entity is a native call from
        // OnDetach; preserve the original live NPC cleanup elsewhere.
        const string detachOld =
            "    if entry and entry.entity then\n" +
            "      deactivateLookAt(entry.entity)\n" +
            "    end\n\n" +
            "    tracked[id.hash] = nil";
        if(text.Contains(detachOld,StringComparison.Ordinal))
        {
            if(text.Split(detachOld,StringSplitOptions.None).Length!=2 ||
               !text.Contains("ObserveAfter(\"NPCPuppet\", \"OnDetach\"",StringComparison.Ordinal))
                throw new InvalidOperationException("f80h OnDetach owner changed.");
            text=text.Replace(detachOld,
                "    -- G-CET f80h detach-safe-v1: drop streaming-out native ownership.\n" +
                "    -- Live look-at cleanup on active NPCs remains in other author paths.\n" +
                "    tracked[id.hash] = nil",
                StringComparison.Ordinal);
        }
        else if(!text.Contains("G-CET f80h detach-safe-v1",StringComparison.Ordinal) &&
                !text.Contains("PASS8: OnDetach already",StringComparison.Ordinal))
            throw new InvalidOperationException("f80h OnDetach cleanup no longer source-proven.");

        if(activeGcComplete && idleComplete &&
           (file.Text.Contains("G-CET f80h detach-safe-v1",StringComparison.Ordinal) ||
            file.Text.Contains("PASS8: OnDetach already",StringComparison.Ordinal)))
            return SemanticInjectionResult.Skip("f80h active GC and PASS8 idle/detach already complete.");

        if(file.Text.Contains("\r\n",StringComparison.Ordinal))
            text=text.Replace("\n","\r\n",StringComparison.Ordinal);
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "One-pass f80h active-GC, 0.50s no-runtime Cron, owned reset and streaming " +
            "detach-safe cleanup; tracked, pending, audio and live look-at callbacks retained.");
    }
}
