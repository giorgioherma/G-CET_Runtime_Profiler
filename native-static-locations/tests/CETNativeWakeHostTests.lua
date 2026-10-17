local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local Set=dofile("native-static-locations/adapters/CETAnchorSetGate.lua")
local Host=dofile("native-static-locations/adapters/CETNativeWakeHost.lua")
local Life=dofile("native-static-locations/adapters/CETSourceAnchorLifecycle.lua")
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

-- Only lifecycle-owned registrations can be detached as sources;
-- unowned legacy attachments must not be silently removed or misreported.
local refused,why=host:detachSource(42)
assert(not refused and why=="UNKNOWN_OWNER" and host.gates[42]~=nil)
assert(host.gates[41]~=nil)
host:shutdown()
assert(scheduled.canceled and farA.active and farB.active)
-- Scheduler unavailable: never disable author discovery.
local failed={Schedule={EveryFrame=function() error("not ready") end}}
local farC=job(true)
local unavailable=Host.new(native,failed,1703002,Hub,Set,Gate)
unavailable:attach(41,farC,{job(false)})
assert(farC.active)
unavailable:shutdown()

-- A source-owned stage that is entirely inactive must be removed from
-- snapshots, not interpreted as FAR or UNKNOWN. Another owner stays active.
local nativeSites={}
local sourceNative={
 GCETStaticReady=function() return true end,
 GCETStaticCoordinatorState=function() return 2 end,
 GCETStaticAddAnchor=function(id,x,y,z,radius,xy,owner)
   nativeSites[id]={owner=owner,armed=false,state=owner==100 and 1 or 0}
   return true
 end,
 GCETStaticArm=function(id,armed)
   if not nativeSites[id] then return false end
   nativeSites[id].armed=armed
   return true
 end,
 GCETStaticRemoveAnchor=function(id)
   nativeSites[id]=nil;return true
 end,
 GCETStaticSubscribe=function() snapshot=true;return true end,
 GCETStaticUnsubscribe=function() return true end,
 GCETStaticNeedsSnapshot=function() return snapshot end,
 GCETStaticOwnerCount=function(owner)
   local n=0
   for _,v in pairs(nativeSites) do if v.owner==owner then n=n+1 end end
   return n
 end,
 GCETStaticOwnerAnchor=function(owner,i)
   local ids={}
   for id,v in pairs(nativeSites) do if v.owner==owner then ids[#ids+1]=id end end
   table.sort(ids);return ids[i+1]
 end,
 GCETStaticState=function(id)
   return nativeSites[id].armed and nativeSites[id].state or -1
 end,
 GCETStaticAckSnapshot=function() snapshot=false;return true end,
 GCETStaticPoll=function() return 0 end,
}
local sourceJobs={}
local sourceClient={Schedule={EveryFrame=function(_,fn)
 local entry={fn=fn,running=true}
 sourceJobs[#sourceJobs+1]=entry
 return {
   Pause=function() entry.running=false end,
   Resume=function() entry.running=true end,
   Cancel=function() entry.running=false end,
 }
end}}
local sourceHost=Host.new(sourceNative,sourceClient,1703003,Hub,Set,Gate,Life)
local aFar,aNear=job(true),job(false)
local bFar,bNear=job(false),job(false)
local defsA={{id=1001,x=1,y=2,z=3,radius=50,metric="xyz"}}
local defsB={{id=2001,x=10,y=20,z=30,radius=60,metric="xy"}}
local ownerA=sourceHost:attachSource(100,aFar,{aNear},nil,
 "epoch-A","fingerprint-owner-A",defsA,true)
local ownerB=sourceHost:attachSource(200,bFar,{bNear},nil,
 "epoch-B","fingerprint-owner-B",defsB,false)
assert(ownerA and ownerB and #sourceJobs==1)
assert(nativeSites[1001].armed and not nativeSites[2001].armed)
sourceJobs[1].fn()
assert(aNear.active and not aFar.active and not bFar.active)
-- Reactivate inactive owner by source lifecycle event; native stream
-- re-snapshots both owners without first treating B's disarmed ID as FAR.
assert(sourceHost:setRelevant(200,true))
sourceJobs[1].fn()
assert(not bFar.active and not bNear.active)
-- Completing A must not silence B nor restore A's old far scanner.
assert(sourceHost:setRelevant(100,false))
assert(not nativeSites[1001].armed and not aFar.active and not aNear.active)
sourceJobs[1].fn()
assert(not bFar.active)
-- New source data gets new IDs; old ones tombstoned.
local defsB2={{id=2002,x=11,y=22,z=33,radius=70,metric="xyz"}}
assert(sourceHost:replaceSourceEpoch(200,"epoch-B2","fingerprint-owner-B2",defsB2,true))
assert(nativeSites[2002] and not nativeSites[2001])
sourceJobs[1].fn()
assert(not bFar.active)
-- A completed source stays registered but disarmed. Detach B without
-- disturbing A or shutting down the shared service.
assert(sourceHost:detachSource(200))
assert(sourceHost.gates[200]==nil and sourceHost.sources[200]==nil)
assert(not nativeSites[2002] and nativeSites[1001])
assert(sourceHost.gates[100]~=nil)
sourceHost:shutdown()
assert(not nativeSites[1001] and not nativeSites[2002])
assert(not aFar.active) -- source A explicitly inactive even on shutdown
-- Last owner becoming inactive pauses the ONE shared host job entirely.
local singleHost=Host.new(sourceNative,sourceClient,1703004,Hub,Set,Gate,Life)
local dFar,dNear=job(true),job(false)
local defsD={{id=3001,x=10,y=20,z=30,radius=35,metric="xyz"}}
assert(singleHost:attachSource(300,dFar,{dNear},nil,
 "epoch-D","fingerprint-owner-D",defsD,true))
local index=#sourceJobs
assert(sourceJobs[index].running)
assert(singleHost:setRelevant(300,false))
assert(not sourceJobs[index].running and not nativeSites[3001].armed)
assert(singleHost:setRelevant(300,true))
assert(sourceJobs[index].running and nativeSites[3001].armed)
assert(#sourceJobs==index) -- resumed same shared job, no duplicate scheduler
singleHost:shutdown()
print("PASS shared native host, source-owned stage dormancy and reactivation")
