local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local Set=dofile("native-static-locations/adapters/CETAnchorSetGate.lua")
local Host=dofile("native-static-locations/adapters/CETNativeWakeHost.lua")
local sites={
 [41]={[401]=0,[402]=1},
 [42]={[420]=0},
}
local ids={[41]={401,402},[42]={420}}
local queue,last={},nil
local subscribed,snapshot=false,true
local healthy=true
local native={
 GCETStaticReady=function() return true end,
 GCETStaticCoordinatorState=function() return healthy and 2 or 1 end,
 GCETStaticSubscribe=function() subscribed=true;snapshot=true;return true end,
 GCETStaticUnsubscribe=function() subscribed=false;return true end,
 GCETStaticNeedsSnapshot=function() return snapshot end,
 GCETStaticOwnerCount=function(owner) return #(ids[owner] or {}) end,
 GCETStaticOwnerAnchor=function(owner,i) return ids[owner][i+1] end,
 GCETStaticState=function(id)
  for _,t in pairs(sites) do if t[id]~=nil then return t[id] end end
  return -1
 end,
 GCETStaticAckSnapshot=function() snapshot=false;return true end,
 GCETStaticPoll=function()
  if #queue==0 then return 0 end
  last=table.remove(queue,1);return last.id
 end,
 GCETStaticPolledOwner=function() return last.owner end,
 GCETStaticPolledState=function() return last.state end,
}
local jobs,scheduled=0,{}
local client={Schedule={EveryFrame=function(options,fn)
  jobs=jobs+1
  scheduled={opts=options,step=fn,canceled=false}
  return {Cancel=function() scheduled.canceled=true end}
end}}
local function job(active)
  local j={active=active}
  j.Pause=function() j.active=false end
  j.Resume=function() j.active=true end
  return j
end
local farA,nearA=job(true),job(false)
local farB,nearB=job(true),job(false)
local host=Host.new(native,client,1703001,Hub,Set,Gate)
host:attach(41,farA,{nearA})
assert(jobs==1 and farA.active) -- no native state yet: author fallback alive
scheduled.step()
assert(not farA.active and nearA.active)
host:attach(42,farB,{nearB})
assert(jobs==1 and farB.active) -- late attach is safe
scheduled.step()
assert(not farB.active and not nearB.active and nearA.active)
-- Owner 42 enters, without polling its mod or disabling owner 41.
sites[42][420]=1
queue[#queue+1]={id=420,owner=42,state=1}
scheduled.step()
assert(nearB.active and nearA.active and not farB.active)
healthy=false
for i=1,20 do scheduled.step() end
assert(farA.active and farB.active)
healthy=true
scheduled.step()
assert(not farA.active and not farB.active and nearA.active and nearB.active)
host:shutdown()
assert(scheduled.canceled and farA.active and farB.active)
-- Scheduler unavailable: never disable author discovery.
local failed={Schedule={EveryFrame=function() error("not ready") end}}
local farC=job(true)
local unavailable=Host.new(native,failed,1703002,Hub,Set,Gate)
unavailable:attach(41,farC,{job(false)})
assert(farC.active)
unavailable:shutdown()
print("PASS shared single CET native host, late owner, fallback and shutdown")
