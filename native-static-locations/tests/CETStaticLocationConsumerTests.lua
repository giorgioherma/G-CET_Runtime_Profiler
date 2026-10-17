-- Pure Lua fail-open/snapshot protocol regression; no Cyberpunk runtime required.
local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local state={[101]=1,[102]=0}
local events={}
local subscribed=false
local subscribes,unsubscribes=0,0
local snap=true
local available,healthy=true,true
local last={}
local native={
 GCETStaticReady=function() return available end,
 GCETStaticCoordinatorState=function() return healthy and 2 or 1 end,
 GCETStaticSubscribe=function(id,owner)
   assert(id==1702001 and owner==0)
   subscribes=subscribes+1;subscribed=true;snap=true;return true
 end,
 GCETStaticUnsubscribe=function(id)
   assert(id==1702001)
   unsubscribes=unsubscribes+1;subscribed=false;return true
 end,
 GCETStaticNeedsSnapshot=function() return snap end,
 GCETStaticOwnerCount=function(owner) assert(owner==41);return 2 end,
 GCETStaticOwnerAnchor=function(_,i) return i==0 and 101 or 102 end,
 GCETStaticState=function(id) return state[id] end,
 GCETStaticAckSnapshot=function() snap=false;return true end,
 GCETStaticPoll=function()
   if #events==0 then return 0 end
   last=table.remove(events,1);return last.id
 end,
 GCETStaticPolledOwner=function() return last.owner end,
 GCETStaticPolledState=function() return last.value end,
}
local near,far,fallback={},{},{}
local hub=Hub.new(native,1702001)
hub:addOwner(41,
 function(id,initial) near[#near+1]={id,initial} end,
 function(id,initial) far[#far+1]={id,initial} end,
 function(reason) fallback[#fallback+1]=reason end)
assert(hub:step() and subscribed and not snap)
assert(#near==1 and near[1][1]==101 and near[1][2])
assert(#far==1 and far[1][1]==102 and far[1][2])
events[#events+1]={id=101,owner=41,value=0}
events[#events+1]={id=102,owner=41,value=1}
assert(hub:step() and #near==2 and #far==2)
assert(not near[2][2] and near[2][1]==102)

-- Loss of position may last hundreds of host ticks: no repeated fallback.
healthy=false
for i=1,100 do assert(hub:step()) end
assert(#fallback==1 and fallback[1]=="NATIVE_UNAVAILABLE")
assert(unsubscribes==1 and not subscribed)

-- Even if native did not invalidate its snapshot, re-enter with new subscription.
healthy=true
state[101]=0
state[102]=1
local previous=subscribes
assert(hub:step() and subscribed and subscribes==previous+1)
assert(#far==3 and #near==3 and not snap)

-- An UNKNOWN initial state must not partially apply known states or ACK.
healthy=false
assert(hub:step() and #fallback==2)
healthy=true
state[101]=-1
local lastNear,lastFar=#near,#far
assert(not hub:step() and not subscribed)
assert(#near==lastNear and #far==lastFar)
assert(#fallback==2) -- outage fallback already active; not spammed
for i=1,3 do assert(not hub:step()) end
assert(#fallback==2 and not subscribed)

-- A healthy complete snapshot finally restores native delivery.
state[101]=1
assert(hub:step() and subscribed)
assert(#near==lastNear+2 and #far==lastFar)
events[#events+1]={id=-1,owner=0,value=-1}
assert(not hub:step() and not subscribed and #fallback==3)
state[101]=1
assert(hub:step() and subscribed)

hub:shutdown()
assert(not subscribed)
-- A source gate can reject restoring its original scheduler. That is not
-- successful fallback; retry on the next host tick rather than latching once.
local restores=0
local recoveryHub=Hub.new(native,1702002)
recoveryHub:addOwner(41,function() end,function() end,function()
 restores=restores+1
 return restores>=3
end)
local stableRestores=0
recoveryHub:addOwner(42,function() end,function() end,function()
 stableRestores=stableRestores+1
 return true
end)
healthy=false
for i=1,50 do assert(recoveryHub:step()) end
assert(restores==3,"failed original fallback must be retried, not spammed")
assert(stableRestores==1,"successfully restored owners must not re-fallback")
recoveryHub:shutdown()
print("PASS native CET consumer fail-open edge, retryable fallback, resnapshot, recovery")
