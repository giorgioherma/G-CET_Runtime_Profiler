-- Contract for the Resolver-owned optional native location host.
-- No RED4ext native dependency: simulate the exact exported game RTTI surface.
local source=assert(arg[1],"pass GCETNativeLocationHost.lua")
local Host=dofile(source)
local ready,known=true,true
local roster,positions,states,stream={}, {}, {}, {}
local currentEvent,cursor,needsSnapshot=nil,1,true
local callbacks,scheduled={},0
Game={}
function Game.GCETStaticReady() return ready end
function Game.GCETStaticCoordinatorState() return known and 2 or 1 end
function Game.GCETStaticAddAnchor(id,x,y,z,radius,xy,owner)
  if positions[id] then return false end
  positions[id]={x=x,y=y,z=z,r=radius,owner=owner}
  roster[owner]=roster[owner] or {}
  table.insert(roster[owner],id)
  return true
end
function Game.GCETStaticArm(id,armed) return positions[id]~=nil end
function Game.GCETStaticOwnerCount(owner) return #(roster[owner] or {}) end
function Game.GCETStaticOwnerAnchor(owner,index) return roster[owner][index+1] end
function Game.GCETStaticState(id) return states[id] end
function Game.GCETStaticSubscribe() needsSnapshot=true;return true end
function Game.GCETStaticUnsubscribe() return true end
function Game.GCETStaticNeedsSnapshot() return needsSnapshot end
function Game.GCETStaticAckSnapshot() needsSnapshot=false;return true end
function Game.GCETStaticPoll()
  local e=stream[cursor]
  if not e then return 0 end
  if e.overrun then return -1 end
  cursor=cursor+1
  currentEvent=e
  return e.id
end
function Game.GCETStaticPolledOwner() return currentEvent.owner end
function Game.GCETStaticPolledState() return currentEvent.state end
function Game.GCETStaticRemoveAnchor(id) positions[id]=nil;return true end
local engine={Register=function(id)
  assert(id=="G-CET-NativeLocationHost")
  return {Schedule={Every=function(period,opts,callback)
    assert(period==0.10 and opts.id=="gcet.native.location.shared")
    scheduled=scheduled+1
    callbacks[1]=callback
    return {Cancel=function()end}
  end}}
end}
local host=Host.new(engine)
local effects={}
for _,owner in ipairs({10031,10032,10033,10034,10035,10036,10037,10038}) do
  local sites=owner==10035 and 100 or owner==10036 and 3 or owner==10037 and 1 or owner==10038 and 1 or
    owner==10031 and 2 or owner==10032 and 2 or owner==10033 and 1 or 2
  local list={}
  for n=1,sites do
    list[n]={x=owner+n,y=0,z=0,radius=100,metric="xyz"}
  end
  effects[owner]={near=0,far=0,fallback=0}
  assert(host:attach(owner,list,
    function()effects[owner].near=effects[owner].near+1 end,
    function()effects[owner].far=effects[owner].far+1 end,
    function()effects[owner].fallback=effects[owner].fallback+1;return true end))
end
assert(scheduled==1,"one 0-Engine consumer must serve all eight owners")
for id in pairs(positions) do states[id]=0 end
host:step()
for _,v in pairs(effects) do
  assert(v.far==1 and v.near==0 and v.fallback==0,"initial all-FAR snapshot")
end
local owner=10031
local ids=roster[owner]
local function event(id,state)
  states[id]=state
  stream[#stream+1]={id=id,owner=owner,state=state}
  host:step()
end
event(ids[1],1)
assert(effects[owner].near==1)
event(ids[2],1)
assert(effects[owner].near==1,"second near site must not duplicate wake")
event(ids[1],0)
assert(effects[owner].far==1,"one far site cannot silence another near site")
event(ids[2],0)
assert(effects[owner].far==2)
known=false
host:step()
for _,v in pairs(effects) do assert(v.fallback==1) end
host:step()
for _,v in pairs(effects) do assert(v.fallback==1) end
known=true
host:step()
for _,v in pairs(effects) do assert(v.far>=2) end -- Poker also left FAR once before outage
stream[#stream+1]={overrun=true}
host:step()
for _,v in pairs(effects) do assert(v.fallback==2) end
host:shutdown()
print("Native eight-owner contract passed: one 10Hz host, 100-card snapshot, aggregate near/far, unknown and stream overrun.")
