namespace GCETRuntimeProfiler.Core.Services;

internal static partial class SemanticInjectors
{
    // The author already fetched the NPC's world position and checked it
    // for nil before scanning the anchor array. Re-reading the SAME native
    // position two more times for every matching anchor creates Lua/CET
    // objects without changing the decision. A cheap scalar radius prefilter
    // also avoids lowercasing/comparing location-specific appearance rules
    // when the NPC is nowhere near that anchor. Keep the original iteration
    // order, pending/near transition, active spawn and random roll intact.
    private static string OptimizeF80hAnchorRegistration(string source)
    {
        var crlf = source.Contains("\r\n", StringComparison.Ordinal);
        var normalized = crlf ? source.Replace("\r\n", "\n", StringComparison.Ordinal) : source;
        var output = OptimizeF80hAnchorRegistrationLf(normalized);
        return crlf ? output.Replace("\n", "\r\n", StringComparison.Ordinal) : output;
    }

    private static string OptimizeF80hAnchorRegistrationLf(string source)
    {
        const string marker = "__gcetF80hAnchorPositionV1";
        const string original =
            "    local rule = resolveAllowRule(app, anchor)\n" +
            "    if rule then\n" +
            "      local at   = isAtAnchor(self, anchor)\n" +
            "      local near = (not at) and isNearAnchor(self, anchor)\n";
        const string optimized =
            "    -- " + marker + ": one NPC position sample, per-anchor scalar test.\n" +
            "    local __gcetAnchorDistSquared = distSq(p, anchor)\n" +
            "    local __gcetOuterRadius = anchor.radius + OUTER_MARGIN\n" +
            "    local rule = __gcetAnchorDistSquared <= __gcetOuterRadius * __gcetOuterRadius\n" +
            "      and resolveAllowRule(app, anchor) or nil\n" +
            "    if rule then\n" +
            "      local at = __gcetAnchorDistSquared <= anchor.radius * anchor.radius\n" +
            "      local near = (not at) and __gcetAnchorDistSquared <= __gcetOuterRadius * __gcetOuterRadius\n";
        if (source.Contains(marker, StringComparison.Ordinal))
        {
            if (source.Split(optimized, StringSplitOptions.None).Length != 2 ||
                source.Contains(original, StringComparison.Ordinal) ||
                !source.Contains("local p = safeGetPos(self)\nif not p then", StringComparison.Ordinal))
                throw new InvalidOperationException("f80h anchor position reuse is partial or drifted.");
            return source;
        }
        // Some older minimal fixtures do not have this registration flow.
        // They retain the original active-GC and PASS8 transformations.
        if (!source.Contains("local function tryRegisterNPC(self)", StringComparison.Ordinal))
            return source;
        if (!source.Contains("local p = safeGetPos(self)\nif not p then", StringComparison.Ordinal) ||
            source.Split(original, StringSplitOptions.None).Length != 2 ||
            !source.Contains("local function distSq(p, a)", StringComparison.Ordinal) ||
            !source.Contains("pending[id.hash] = { npc = self, anchor = anchor, since = 0 }",
                StringComparison.Ordinal))
            throw new InvalidOperationException("f80h original single-NPC registration location flow drifted.");
        return source.Replace(original, optimized, StringComparison.Ordinal);
    }

    private static SemanticInjectionResult ApplyF80hActiveGcSweep(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "local SWEEP_INTERVAL   = 1.5",
            "local PENDING_TTL      = 12.0",
            "local womenByAnchor = {}",
            "CoupleVariance.Update(",
            "for hash, data in pairs(pendingAppearance) do");
        var text=file.Text.Replace("\r\n","\n",StringComparison.Ordinal);
        // Position/anchor filtering belongs to the SAME source-owned GC stage.
        // Run on final native/generic-composed source, never as a repair pass.
        text=OptimizeF80hAnchorRegistration(text);
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
            if(activeGcComplete && text == file.Text.Replace("\r\n","\n",StringComparison.Ordinal))
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
            // Capture the author's complete opening, not merely Cron.Update.
            // Phase 1+2 deliberately stopped ALL inactive/disabled runtime
            // maintenance, then seeded the RNG before the low-rate idle lane.
            const string oldUpdate=
                "  Cron.Update(delta)\n\n" +
                "  -- ensure we seed as soon as game time becomes available\n" +
                "  if scriptActive and not rngSeeded then\n" +
                "    seedRngOnce()\n" +
                "  end\n\n" +
                "  if not isEnabled() then return end\n" +
                "  if not scriptActive then return end\n";
            const string newUpdate= """
  -- G-CET f80h idle-runtime-v1: preserve the authoritative PASS8
  -- disabled/inactive short-circuit BEFORE Cron maintenance.
  if not isEnabled() then return end
  if not scriptActive then return end

  -- Original session-start RNG initialization precedes dormancy.
  if not rngSeeded then
    seedRngOnce()
  end

  local __gcetRuntimeBusy = next(tracked) ~= nil
    or next(pending) ~= nil or next(pendingAppearance) ~= nil
    or next(preActiveQueue) ~= nil or next(anchorAudio) ~= nil
  if not __gcetRuntimeBusy then
    idleCronElapsed = idleCronElapsed + delta
    if idleCronElapsed >= 0.50 then
      Cron.Update(idleCronElapsed)
      idleCronElapsed = 0.0
    end
    return
  end

  idleCronElapsed = 0.0
  Cron.Update(delta)
""";
            if(text.Split(oldUpdate,StringSplitOptions.None).Length!=2)
                throw new InvalidOperationException(
                    "f80h onUpdate active/inactive/Cron owner drifted.");
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
           text == file.Text.Replace("\r\n","\n",StringComparison.Ordinal) &&
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
