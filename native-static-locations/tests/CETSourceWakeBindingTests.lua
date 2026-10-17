local Binding=dofile("native-static-locations/adapters/CETSourceWakeBinding.lua")
local function job(active)
  local j={active=active}
  j.Pause=function() j.active=false end
  j.Resume=function() j.active=true end
  return j
end
local far,near=job(true),job(false)
local providerCalls=0
local current=nil
local registered=0
local source={epoch="initial",fingerprint="sha256-source-1",relevant=true,
 sites=function() return {
  {id=501,x=10,y=20,z=30,radius=30,metric="xyz"},
  {id=502,x=40,y=50,z=60,radius=30,metric="xyz"},
 } end}
local detached=0
local host={closed=false,detachSource=function(self,owner)
 assert(owner==44);detached=detached+1;return true
end,attachSource=function(self,owner,f,n,alive,epoch,fp,sites,rel,restore)
 registered=registered+1
 assert(owner==44 and f==far and n[1]==near)
 assert(epoch=="initial" and fp=="sha256-source-1")
 assert(#sites==2 and rel and type(restore)=="function")
 return {onFallback=function() far.Resume() end,
  reconcileAuthorTransition=function() return true end}
end,setRelevant=function(self,owner,state) assert(owner==44);return true end,
replaceSourceEpoch=function(self,owner,epoch,fp,sites,rel)
 return epoch=="next" and fp=="sha256-source-2" and rel==false
end}
local src=Binding.new(function()
 providerCalls=providerCalls+1
 return current
end,44,far,{near},nil,function() end,source)
local ok,reason=src:attempt()
assert(not ok and reason=="NO_SHARED_HOST" and far.active)
current=host
assert(src:attempt() and registered==1)
assert(src:attempt() and registered==1)
assert(src:reconcile())
assert(src:setRelevant(false) and src.relevant==false)
assert(src:replaceEpoch("next","sha256-source-2",
 {{id=503,x=3,y=4,z=5,radius=20,metric="xyz"}},false))
assert(src.attachedEpoch=="next")
assert(src:shutdown() and far.active and detached==1)
assert(not src:attempt())
-- Missing sites never calls the host or alters the source job.
local broken=Binding.new(function() return host end,45,job(true),{},nil,nil,
 {epoch="e",fingerprint="long-hash",sites=function() error("bad data") end})
assert(not broken:attempt())
-- Caller supplies no new timer; the adapter can be invoked by the original
-- far/near scheduler or explicit source-session callbacks.
assert(providerCalls==2)
print("PASS generic source-adopted owner binding, late host, source epoch and fallback")
