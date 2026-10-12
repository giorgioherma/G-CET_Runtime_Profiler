using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Implements the existing streetgamespoker semantic rule, not another optimizer.
// PASS4 is emitted against original mod source; broken generated output is never repaired.
internal static class PokerPass4Source
{
    internal static SemanticInjectionResult Apply(SemanticPatchContext context)
    {
        var file=context.FindFile("init.lua",
            "StreetGamesPoker.ready = true", "StreetTables.registerMapPins()",
            "Cron.Update(dt)", "SeatController.update(dt)", "StreetTables.update(dt)",
            "InteractionMenu.update()", "CardRenderer.update(dt)", "TableTalk.update(dt)");

        var oldBody="    if not StreetGamesPoker.ready or inMenu or not inGame then return end\n    Cron.Update(dt)\n    SeatController.update(dt)\n    StreetTables.update(dt)\n    InteractionMenu.update()\n    CardRenderer.update(dt)\n    TableTalk.update(dt)\n";
        var text=ReplaceOnce(file.Text,
            "registerForEvent(\"onInit\", function()",
            "local __gcetPokerAttachPass4\\nregisterForEvent(\"onInit\", function()",
            "author onInit opening");
        text=ReplaceOnce(text,
            "    StreetGamesPoker.ready = true\\n",
            "    StreetGamesPoker.ready = true\\n" +
            "    if __gcetPokerAttachPass4 then __gcetPokerAttachPass4() end\\n",
            "post-ready attach");
        text=ReplaceOnce(text,oldBody,
            "    if pass4Attached then return end\\n" +
            "    if __gcetPokerAttachPass4 then __gcetPokerAttachPass4() end\\n" +
            "    if pass4Attached then return end\\n" + oldBody,
            "author update fallback");
        var matches=Regex.Matches(text,
            @"(?m)^[ \\t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\\d+)\\s*\\(\\s*['""]onUpdate['""]\\s*,\\s*function\\s*\\(\\s*dt\\s*\\)\\s*$",
            RegexOptions.CultureInvariant);
        if(matches.Count!=1)throw new InvalidOperationException(
            "Poker PASS4 onUpdate anchor absent or ambiguous.");
        var opening=matches[0].Value;
        var scheduler="local pass4Attached=false\nlocal pass4Attaching=false\nlocal POKER_WAKE_RADIUS=25.0\nlocal function pokerRuntimeReady()\n return StreetGamesPoker.ready and not inMenu and inGame\nend\nlocal function pokerIsActive()\n return SeatController.activeSpotId~=nil or SeatController.forcedCam==true or StreetTables.activeSpotId~=nil\nend\n__gcetPokerAttachPass4=function()\n if pass4Attached then return true end\n if pass4Attaching then return false end\n pass4Attaching=true\n local created={}\n local okAttach=pcall(function()\n  local okEngine,engine=pcall(GetMod,\"0-Engine\")\n  if not okEngine or type(engine)~=\"table\" or type(engine.Register)~=\"function\" then error(\"0-Engine Register API not ready\") end\n  local client=engine.Register(\"StreetGamesPoker\")\n  if type(client)~=\"table\" or type(client.Schedule)~=\"table\" or type(client.Schedule.EveryFrame)~=\"function\" or type(client.Schedule.Every)~=\"function\" then error(\"0-Engine Scheduler API not ready\") end\n  local nearMode=false\n  local farWorldJob,nearWorldJob,nearSeatJob=nil,nil,nil\n  local function setNearMode(value)\n   value=value==true\n   if value==nearMode then return end\n   nearMode=value\n   if value then\n    if farWorldJob then farWorldJob.Pause() end\n    if nearWorldJob then nearWorldJob.Resume() end\n    if nearSeatJob then nearSeatJob.Resume() end\n   else\n    if nearWorldJob then nearWorldJob.Pause() end\n    if nearSeatJob then nearSeatJob.Pause() end\n    if farWorldJob then farWorldJob.Resume() end\n   end\n  end\n  farWorldJob=client.Schedule.Every(1.0,{id=\"poker.world_far\",active=true,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if not pokerRuntimeReady() then return end\n   local active=pokerIsActive()\n   local near=StreetTables.update(ctx.elapsed,POKER_WAKE_RADIUS) or active or InteractionMenu.visible\n   if near then setNearMode(true) end\n  end)\n  table.insert(created,farWorldJob)\n  nearWorldJob=client.Schedule.Every(0.20,{id=\"poker.world_near\",active=false,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if not pokerRuntimeReady() then return end\n   local active=pokerIsActive()\n   local near=StreetTables.update(ctx.elapsed,POKER_WAKE_RADIUS) or active or InteractionMenu.visible\n   if not near then setNearMode(false) end\n  end)\n  table.insert(created,nearWorldJob)\n  nearSeatJob=client.Schedule.Every(0.10,{id=\"poker.seat_near\",active=false,pause=\"never\",clock=\"game\",catchUp=false},function(ctx)\n   if not pokerRuntimeReady() or not nearMode or pokerIsActive() then return end\n   SeatController.update(ctx.elapsed)\n  end)\n  table.insert(created,nearSeatJob)\n  local realtimeJob=client.Schedule.EveryFrame({id=\"poker.realtime\",pause=\"never\"},function(ctx)\n   if not pokerRuntimeReady() then return end\n   Cron.Update(ctx.dt)\n   local active=pokerIsActive()\n   if (active or InteractionMenu.visible) and not nearMode then setNearMode(true) end\n   if active then SeatController.update(ctx.dt) end\n   if InteractionMenu.visible then InteractionMenu.update() end\n   if next(CardRenderer.motions)~=nil or next(CardRenderer.flips)~=nil then CardRenderer.update(ctx.dt) end\n   if TableTalk.active or (TableTalk.clock or 0)>0 then TableTalk.update(ctx.dt) end\n  end)\n  table.insert(created,realtimeJob)\n  pass4Attached=true\n end)\n pass4Attaching=false\n if not okAttach then\n  for _,handle in ipairs(created) do if handle and type(handle.Cancel)==\"function\" then pcall(handle.Cancel) end end\n  return false\n end\n Logger.info(\"pass4_scheduler_attached\",{frameJobs=1,timedJobs=3})\n return true\nend";
        text=ReplaceOnce(text,opening,scheduler+"\\n"+opening,
            "four-lane Scheduler before author callback");
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Reproduced PASS4 scheduler from original source: four lanes, defined Logger, startup local in scope, map pins retained, and author's fallback if 0-Engine is not ready.");
    }

    private static string ReplaceOnce(string text,string oldValue,string replacement,string label)
    {
        var first=text.IndexOf(oldValue,StringComparison.Ordinal);
        if(first<0 || text.IndexOf(oldValue,first+oldValue.Length,StringComparison.Ordinal)>=0)
            throw new InvalidOperationException("Poker PASS4 source anchor missing/ambiguous: "+label);
        return text[..first]+replacement+text[(first+oldValue.Length)..];
    }
}
