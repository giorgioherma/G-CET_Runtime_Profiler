-- Mock/contract verification for source-matched R4 Lua compatibility bridge.
local module=dofile("native-static-locations/adapters/LegacyR4NativeCacheBridge.lua")
local observed={near=0,far=0,unavailable=0,legacyUpdates=0}
local current={}
local old={}
function old.Register(owner,source,fingerprint,anchors,hooks)
  local key=owner..":"..source
  current[key]={near=nil}
  local item=current[key]
  return {IsNear=function() return item.near end,
    GetState=function() return "UNAVAILABLE" end,
    Unregister=function() current[key]=nil end}
end
function old.Invalidate(owner,source) current[owner..":"..source]=nil end
function old.ResetSession() for _,v in pairs(current)do v.near=nil end end
function old.Update(pos,frame) observed.legacyUpdates=observed.legacyUpdates+1 end
function old.GetAnchor()return nil end
function old.QueryNearby()return {} end
function old.GetInfo() return {ready=false} end

local state,events,metadata={}, {},{}
local subscribed=false
local last=nil
local valid=true
local native={
  GCETStaticReady=function()return true end,
  GCETStaticCoordinatorState=function()return valid and 2 or 1 end,
  GCETStaticAddAnchor=function(id,x,y,z,r,xy,owner)
    assert(not metadata[id]);metadata[id]={owner=owner,x=x};state[id]=-1;return true
  end,
  GCETStaticArm=function(id,on) if not metadata[id]then return false end
    state[id]=metadata[id].x==0 and 1 or 0;return true end,
  GCETStaticRemoveAnchor=function(id) state[id]=nil;metadata[id]=nil;return true end,
  GCETStaticSubscribe=function(id,owner)assert(id==1702401 and owner==0);subscribed=true;return true end,
  GCETStaticUnsubscribe=function(id) subscribed=false;return true end,
  GCETStaticNeedsSnapshot=function(id)return true end,
  GCETStaticState=function(id)return state[id] end,
  GCETStaticAckSnapshot=function(id)return true end,
  GCETStaticPoll=function(id)
    if #events==0 then return 0 end
    last=table.remove(events,1);return last.id
  end,
  GCETStaticPolledState=function(id)return last.value end
}
local w=module.Wrap(old,native,1702401)
local h=w.Register("Poker","tables",nil,{{id="A",x=0,y=0,z=0,radius=25}}, {
 onNear=function()observed.near=observed.near+1 end,
 onFar=function()observed.far=observed.far+1 end,
 onUnavailable=function()observed.unavailable=observed.unavailable+1 end
})
assert(h and h.IsNear()==nil)
w.Update({x=0,y=0,z=0},1)
assert(subscribed and h.IsNear()==true and observed.near==1 and observed.legacyUpdates==0)
local id=next(metadata);state[id]=0;events[#events+1]={id=id,value=0}
w.Update({x=0,y=0,z=0},2)
assert(h.IsNear()==false and observed.far==1)
valid=false
w.Update({x=0,y=0,z=0},3)
assert(observed.legacyUpdates==1 and not subscribed)
valid=true
w.Update({x=0,y=0,z=0},4)
assert(subscribed and h.IsNear()==false)
local replacement=w.Register("Poker","tables","reload",
   {{id="B",x=0,y=0,z=0,radius=35}}, {})
assert(replacement and not subscribed and h.IsNear()==nil)
w.Update({x=0,y=0,z=0},5)
assert(replacement.IsNear()==true)
replacement.Unregister()
assert(next(metadata)==nil and not subscribed)
print("PASS native R4 compatibility: register, snapshot, wake, fallback, reload, removal")
