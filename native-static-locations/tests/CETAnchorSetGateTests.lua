local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local Set=dofile("native-static-locations/adapters/CETAnchorSetGate.lua")
local values={[11]=0,[12]=1,[13]=0}
local names={11,12,13}
local queue,last={},nil
local subscribed,snap=false,true
local healthy=true
local native={
 GCETStaticReady=function() return true end,
 GCETStaticCoordinatorState=function() return healthy and 2 or 1 end,
 GCETStaticSubscribe=function() subscribed=true;snap=true;return true end,
 GCETStaticUnsubscribe=function() subscribed=false;return true end,
 GCETStaticNeedsSnapshot=function() return snap end,
 GCETStaticOwnerCount=function(owner) assert(owner==41);return #names end,
 GCETStaticOwnerAnchor=function(_,i) return names[i+1] end,
 GCETStaticState=function(id) return values[id] end,
 GCETStaticAckSnapshot=function() snap=false;return true end,
 GCETStaticPoll=function()
   if #queue==0 then return 0 end
   last=table.remove(queue,1)
   return last.id
 end,
 GCETStaticPolledOwner=function() return 41 end,
 GCETStaticPolledState=function() return last.state end,
}
local function job(active)
 local j={active=active,pauses=0,resumes=0}
 j.Pause=function() j.active=false;j.pauses=j.pauses+1 end
 j.Resume=function() j.active=true;j.resumes=j.resumes+1 end
 return j
end
local far,near=job(true),job(false)
local gate=Gate.new(far,{near})
local set=Set.new(gate)
local hub=Hub.new(native,1702001)
set:bind(hub,41)
-- Snapshot three sites: one near, two far. Only one gate transition.
assert(hub:step() and set.near==1 and set.count==3)
assert(not far.active and near.active)
assert(near.resumes==1)
local function event(id,state)
 values[id]=state
 queue[#queue+1]={id=id,state=state}
 assert(hub:step())
end
event(11,1)
assert(set.near==2 and near.active)
event(12,0)
assert(set.near==1 and near.active)
event(11,0)
assert(set.near==0 and not near.active and not far.active)
event(13,1)
assert(set.near==1 and near.active)
-- Outage -> original far scanner resumes, once.
healthy=false
for i=1,4 do assert(hub:step()) end
assert(gate.mode=="ORIGINAL" and far.active and not set.valid)
-- Native recovery -> complete owner resnapshot rather than stale accumulation.
healthy=true
assert(hub:step() and set.valid and set.near==1 and near.active)
-- Unknown site means source epoch drift: fail open, never infer FAR.
queue[#queue+1]={id=777,state=1}
assert(not hub:step())
assert(gate.mode=="ORIGINAL" and far.active and not set.valid)
-- A new complete snapshot can adopt the changed source epoch.
names={11,12,13,777};values[777]=0
assert(hub:step() and set.valid and set.count==4)
-- Invalid snapshot does not partially gate active owners.
healthy=false
assert(hub:step())
healthy=true
values[777]=-1
assert(not hub:step() and not set.valid and far.active)
values[777]=0
assert(hub:step() and set.valid)
hub:shutdown()
print("PASS multi-anchor OR-near aggregate, atomic snapshot, fail-open/rebind")
