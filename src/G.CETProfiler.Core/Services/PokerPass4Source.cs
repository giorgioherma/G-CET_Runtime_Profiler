using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Source-proven version-tolerant implementation of the existing streetgamespoker
// semantic. Edits the two AUTHOR files that together enable PASS4/PASS4C:
//  - init.lua: scheduler lanes and author update fallback
//  - StreetTables.lua: 25m wake signal and squared-distance world sweep.
// Unrecognized source shape fails closed; no post-generation repair.
internal static class PokerPass4Source
{
    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        var init=context.FindFile("init.lua",
            "StreetGamesPoker", "StreetTables.registerMapPins()",
            "Cron.Update", "SeatController.update", "StreetTables.update",
            "InteractionMenu.update", "CardRenderer.update", "TableTalk.update");
        var tables=context.FindFile("StreetTables.lua",
            "StreetTables.locations", "function StreetTables.update",
            "CHAIR_SPAWN_RANGE", "CHAIR_DESPAWN_RANGE", "TRACE_RANGE",
            "traceApproach", "spawnChair", "despawnChair");
        if (init.Text.Contains("__gcetPokerAttachPass4",StringComparison.Ordinal) ||
            tables.Text.Contains("__gcetPokerWakeSq",StringComparison.Ordinal))
            throw new InvalidOperationException("Poker source is partially transformed; restore the pristine mod before generating PASS4.");

        var tableText=TransformWorldSweep(tables.Text);
        var initText=TransformController(init.Text);
        context.Write(init,initText);
        context.Write(tables,tableText);
        return SemanticInjectionResult.Success(
            "Generated both PASS4 files from original mod: far world 1 Hz, near world 5 Hz, " +
            "near seat 10 Hz, realtime gameplay every frame; 25 m squared-distance wake probe, " +
            "map pins untouched, initialized logger, and exact author update fallback.");
    }

    private static string TransformController(string text)
    {
        text=ReplaceUnique(text,
            @"(?m)^(?<open>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onInit['""]\s*,\s*function\s*\(\s*\)\s*)$",
            m=>"local __gcetPokerAttachPass4\n"+m.Value,
            "onInit registration");
        text=ReplaceUnique(text,
            @"(?m)^(?<indent>[ \t]*)StreetGamesPoker\s*\.\s*ready\s*=\s*true[ \t]*$",
            m=>m.Value+"\n"+m.Groups["indent"].Value+
                "if __gcetPokerAttachPass4 then __gcetPokerAttachPass4() end",
            "ready after author initialization");
        var scheduler="-- Distance-aware PASS4: author gameplay preserved, only discovery/work throttled.\nlocal __gcetPokerAttached=false\nlocal __gcetPokerAttaching=false\nlocal __gcetPokerRadius=25.0\nlocal function __gcetPokerReady()\n return StreetGamesPoker.ready and not inMenu and inGame\nend\nlocal function __gcetPokerActive()\n return SeatController.activeSpotId~=nil or SeatController.forcedCam==true or StreetTables.activeSpotId~=nil\nend\n__gcetPokerAttachPass4=function()\n if __gcetPokerAttached then return true end\n if __gcetPokerAttaching then return false end\n __gcetPokerAttaching=true\n local created={}\n local ok=pcall(function()\n  local okEngine,engine=pcall(GetMod,\"0-Engine\")\n  if not okEngine or type(engine)~=\"table\" or type(engine.Register)~=\"function\" then error(\"0-Engine not ready\") end\n  local client=engine.Register(\"StreetGamesPoker\")\n  if type(client)~=\"table\" or type(client.Schedule)~=\"table\"\n   or type(client.Schedule.Every)~=\"function\" or type(client.Schedule.EveryFrame)~=\"function\" then error(\"Scheduler not ready\") end\n  local nearMode=false\n  local farWorld,nearWorld,nearSeat=nil,nil,nil\n  local function setNear(state)\n   state=state==true\n   if state==nearMode then return end\n   nearMode=state\n   if state then\n    if farWorld then farWorld.Pause() end\n    if nearWorld then nearWorld.Resume() end\n    if nearSeat then nearSeat.Resume() end\n   else\n    if nearWorld then nearWorld.Pause() end\n    if nearSeat then nearSeat.Pause() end\n    if farWorld then farWorld.Resume() end\n   end\n  end\n  farWorld=client.Schedule.Every(1.0,{id=\"poker.world_far\",active=true,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if not __gcetPokerReady() then return end\n   local near=StreetTables.update(ctx.elapsed,__gcetPokerRadius)\n    or __gcetPokerActive() or InteractionMenu.visible\n   if near then setNear(true) end\n  end)\n  created[#created+1]=farWorld\n  nearWorld=client.Schedule.Every(0.20,{id=\"poker.world_near\",active=false,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if not __gcetPokerReady() then return end\n   local near=StreetTables.update(ctx.elapsed,__gcetPokerRadius)\n    or __gcetPokerActive() or InteractionMenu.visible\n   if not near then setNear(false) end\n  end)\n  created[#created+1]=nearWorld\n  nearSeat=client.Schedule.Every(0.10,{id=\"poker.seat_near\",active=false,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if __gcetPokerReady() and nearMode and not __gcetPokerActive() then SeatController.update(ctx.elapsed) end\n  end)\n  created[#created+1]=nearSeat\n  local realtime=client.Schedule.EveryFrame({id=\"poker.realtime\",pause=\"never\"},function(ctx)\n   if not __gcetPokerReady() then return end\n   Cron.Update(ctx.dt)\n   local active=__gcetPokerActive()\n   if (active or InteractionMenu.visible) and not nearMode then setNear(true) end\n   if active then SeatController.update(ctx.dt) end\n   if InteractionMenu.visible then InteractionMenu.update() end\n   if CardRenderer.shuffleActive==true or next(CardRenderer.motions)~=nil or next(CardRenderer.flips)~=nil then CardRenderer.update(ctx.dt) end\n   if TableTalk.active or (TableTalk.clock or 0)>0 then TableTalk.update(ctx.dt) end\n  end)\n  created[#created+1]=realtime\n  __gcetPokerAttached=true\n end)\n __gcetPokerAttaching=false\n if not ok then\n  for _,handle in ipairs(created) do if handle and type(handle.Cancel)==\"function\" then pcall(handle.Cancel) end end\n  return false\n end\n Logger.info(\"pass4_scheduler_attached\",{frameJobs=1,timedJobs=3})\n return true\nend";
        text=ReplaceUnique(text,
            @"(?m)^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*(?:dt|deltaTime)\s*\)\s*$",
            m=>scheduler+"\n"+m.Value+
               "\n    if __gcetPokerAttached then return end"+
               "\n    if __gcetPokerAttachPass4 then __gcetPokerAttachPass4() end"+
               "\n    if __gcetPokerAttached then return end",
            "author onUpdate registration");
        return text;
    }

    private static string TransformWorldSweep(string text)
    {
        // Only transform the terminal world-update function, not the numerous
        // other table gameplay helpers in the author source.
        var start=Regex.Match(text,
            @"(?m)^[ \t]*function\s+StreetTables\s*\.\s*update\s*\(\s*dt\s*\)\s*$",
            RegexOptions.CultureInvariant);
        if (!start.Success || start.NextMatch().Success)
            throw new InvalidOperationException("Poker world update signature changed or is ambiguous.");
        var finish=Regex.Match(text[start.Index..],
            @"(?m)^[ \t]*return\s+StreetTables\s*$",
            RegexOptions.CultureInvariant);
        if (!finish.Success || finish.NextMatch().Success)
            throw new InvalidOperationException("Poker terminal module return missing.");
        var ending=start.Index+finish.Index+finish.Length;
        var body=text[start.Index..ending];
        var prefix=text[..start.Index];
        var suffix=text[ending..];

        var distanceUses=Regex.Matches(body,@"\bd\b",RegexOptions.CultureInvariant).Count;
        if (distanceUses!=4)
            throw new InvalidOperationException(
                "Poker distance scalar has new consumers; squared-distance rewrite not proven.");

        body=ReplaceUnique(body,
            @"(?m)^[ \t]*function\s+StreetTables\s*\.\s*update\s*\(\s*dt\s*\)",
            _=>"function StreetTables.update(dt, wakeRadius)",
            "world update signature");

        body=ReplaceUnique(body,
            @"(?m)^(?<indent>[ \t]*)if\s+not\s+player\s+then\s+return\s+end[ \t]*$",
            m=>m.Groups["indent"].Value+"if not player then return false end",
            "no-player early exit");

        body=ReplaceUnique(body,
            @"(?m)^(?<indent>[ \t]*)local\s+px\s*,\s*py\s*,\s*pz\s*=\s*pos\.x\s*,\s*pos\.y\s*,\s*pos\.z[ \t]*$",
            m=>m.Value+"\n"+m.Groups["indent"].Value+
                "local __gcetPokerWakeRadius=wakeRadius or 25.0\n"+
                m.Groups["indent"].Value+
                "local __gcetPokerWakeSq=__gcetPokerWakeRadius*__gcetPokerWakeRadius\n"+
                m.Groups["indent"].Value+
                "local __gcetPokerSpawnSq=CHAIR_SPAWN_RANGE*CHAIR_SPAWN_RANGE\n"+
                m.Groups["indent"].Value+
                "local __gcetPokerDespawnSq=CHAIR_DESPAWN_RANGE*CHAIR_DESPAWN_RANGE\n"+
                m.Groups["indent"].Value+
                "local __gcetPokerTraceSq=TRACE_RANGE*TRACE_RANGE\n"+
                m.Groups["indent"].Value+
                "local __gcetPokerNearAny=StreetTables.activeSpotId~=nil or SpotManager.activeSpotId~=nil",
            "world sweep scalar coordinates");

        body=ReplaceUnique(body,
            @"(?m)^(?<indent>[ \t]*)local\s+d\s*=\s*math\s*\.\s*sqrt\s*\(\s*dx\s*\*\s*dx\s*\+\s*dy\s*\*\s*dy\s*\+\s*dz\s*\*\s*dz\s*\)[ \t]*$",
            m=>m.Groups["indent"].Value+"local __gcetPokerDistanceSq=dx*dx+dy*dy+dz*dz\n"+
                m.Groups["indent"].Value+
                "if __gcetPokerDistanceSq<=__gcetPokerWakeSq then __gcetPokerNearAny=true end",
            "per-location distance");
        body=ReplaceUnique(body,@"\bd\s*<\s*CHAIR_SPAWN_RANGE\b",
            _=>"__gcetPokerDistanceSq < __gcetPokerSpawnSq","chair spawn distance");
        body=ReplaceUnique(body,@"\bd\s*>\s*CHAIR_DESPAWN_RANGE\b",
            _=>"__gcetPokerDistanceSq > __gcetPokerDespawnSq","chair despawn distance");
        body=ReplaceUnique(body,@"\bd\s*<\s*TRACE_RANGE\b",
            _=>"__gcetPokerDistanceSq < __gcetPokerTraceSq","approach trace distance");
        body=ReplaceUnique(body,
            @"(?m)(?<loop>^[ \t]*end[ \t]*\n)(?<function>^[ \t]*end[ \t]*\n)(?<module>\s*return\s+StreetTables\s*$)",
            m=>m.Groups["loop"].Value+"    return __gcetPokerNearAny\n"+
                m.Groups["function"].Value+m.Groups["module"].Value,
            "world-update return");
        return prefix+body+suffix;
    }

    private static string ReplaceUnique(string text,string regex,Func<Match,string> convert,string proof)
    {
        var matches=Regex.Matches(text,regex,RegexOptions.CultureInvariant);
        if(matches.Count!=1)
            throw new InvalidOperationException(
                "Poker semantic source proof failed ("+proof+"): "+matches.Count+" structural matches.");
        var m=matches[0];
        return text[..m.Index]+convert(m)+text[(m.Index+m.Length)..];
    }
}
