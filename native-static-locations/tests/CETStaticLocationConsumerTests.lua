-- Pure Lua protocol test; no Cyberpunk runtime or author mod required.
local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local state={ [101]=1,[102]=0 }
local events={}
local subscribed=false
local snap=true
local available=true
local healthy=true
local last={}
local native={
 GCETStaticReady=function() return available end,
 GCETStaticCoordinatorState=function() return healthy and 2 or 1 end,
 GCETStaticSubscribe=function(id,owner) assert(id==1702001 and owner==0);subscribed=true;snap=true;return true end,
 GCETStaticUnsubscribe=function(id) assert(id==1702001);subscribed=false;return true end,
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
healthy=false
assert(hub:step() and #fallback==1 and fallback[1]=="NATIVE_UNAVAILABLE")
healthy=true
snap=true
state[101]=-1
assert(hub:step() and #fallback==2 and fallback[2]=="UNKNOWN_ANCHOR_STATE")
events[#events+1]={id=-1,owner=0,value=-1}
assert(not hub:step() and not subscribed and #fallback==3)
state[101]=1
assert(hub:step() and subscribed and #near==3)
hub:shutdown()
assert(not subscribed)
print("PASS native CET consumer snapshot/events/fail-open/recovery")
