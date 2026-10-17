-- Model the author scheduler at the natural dispatch unit: a real
-- pause removes callbacks, not merely distance instructions inside them.
local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local Set=dofile("native-static-locations/adapters/CETAnchorSetGate.lua")
local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local Host=dofile("native-static-locations/adapters/CETNativeWakeHost.lua")
local ready=true
local state=0
local changes,last={},nil
local subscribed=false
local snapshot=true
local native={
 GCETStaticReady=function() return true end,
 GCETStaticCoordinatorState=function() return ready and 2 or 1 end,
 GCETStaticSubscribe=function() subscribed=true;snapshot=true;return true end,
 GCETStaticUnsubscribe=function() subscribed=false;return true end,
 GCETStaticNeedsSnapshot=function() return snapshot end,
 GCETStaticOwnerCount=function() return 1 end,
 GCETStaticOwnerAnchor=function() return 51 end,
 GCETStaticState=function() return state end,
 GCETStaticAckSnapshot=function() snapshot=false;return true end,
 GCETStaticPoll=function()
  if #changes==0 then return 0 end
  last=table.remove(changes,1);return last.id
 end,
 GCETStaticPolledOwner=function() return 7 end,
 GCETStaticPolledState=function() return last.state end,
}
local ticks={}
local function scheduler()
 local engine={Schedule={}}
 function engine.Schedule.EveryFrame(options,callback)
  local task={running=true,fn=callback,calls=0,id=options.id}
  function task.Pause() task.running=false end
  function task.Resume() task.running=true end
  function task.Cancel() task.running=false end
  ticks[#ticks+1]=task
  return task
 end
 function engine.Schedule.Every(interval,options,callback)
  local task={running=options.active~=false,fn=callback,calls=0,id=options.id}
  function task.Pause() task.running=false end
  function task.Resume() task.running=true end
  function task.Cancel() task.running=false end
  ticks[#ticks+1]=task
  return task
 end
 return engine
end
local client=scheduler()
local counts={far=0,near=0,independent=0}
local far=client.Schedule.Every(1,{id="source.world_far",active=true},function()
 counts.far=counts.far+1
end)
local near=client.Schedule.Every(0.2,{id="source.world_near",active=false},function()
 counts.near=counts.near+1
end)
local independent=client.Schedule.EveryFrame({id="source.active_gameplay"},function()
 counts.independent=counts.independent+1
end)
local host=Host.new(native,client,1703001,Hub,Set,Gate)
host:attach(7,far,{near})
assert(#ticks==4) -- 3 original lanes, ONE shared native host lane
local function frames(n)
 for i=1,n do
  for _,job in ipairs(ticks) do
   if job.running then job.calls=job.calls+1;job.fn() end
  end
 end
end
-- The single host snapshots on the first shared scheduler tick, before
-- the authored 1-second far task reaches its first due time.
ticks[4].fn()
frames(100)
assert(counts.far==0 and counts.near==0)
assert(counts.independent==100)
assert(ticks[1].calls==0) -- actual callback dispatch suppressed
changes[#changes+1]={id=51,state=1}
state=1
frames(20)
assert(counts.far==0 and counts.near==19)
assert(counts.independent==120)
changes[#changes+1]={id=51,state=0}
state=0
frames(40)
assert(counts.far==0 and counts.near==20) -- event handled after first near tick
ready=false
frames(40)
assert(counts.far==39) -- original scanner wakes next dispatch after fallback
assert(counts.independent==200)
ready=true
frames(40)
assert(counts.far==40) -- one restoration tick before the snapshot
assert(not far.running and not near.running)
-- No handle, e.g. cancelless private Cron.Every, cannot enter this gate.
local ok=pcall(Gate.new,{}, {near})
assert(not ok)
host:shutdown()
assert(far.running)
print("PASS actual far callback retirement, independent activity, fallback resume")
