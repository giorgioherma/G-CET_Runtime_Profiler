local Bootstrap=dofile("native-static-locations/adapters/CETNativeWakeBootstrap.lua")
local Host=dofile("native-static-locations/adapters/CETNativeWakeHost.lua")
local Hub=dofile("native-static-locations/adapters/CETStaticLocationConsumer.lua")
local Set=dofile("native-static-locations/adapters/CETAnchorSetGate.lua")
local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local Life=dofile("native-static-locations/adapters/CETSourceAnchorLifecycle.lua")
local created=0
local engine={GCET={OtherService="must_survive"}}
engine.Register=function(name)
 assert(name=="G-LookupCache")
 created=created+1
 return {Schedule={EveryFrame=function(_,fn)
   return {Cancel=function() end,Pause=function() end,Resume=function() end}
 end}}
end
local components={Host=Host,Hub=Hub,Set=Set,Gate=Gate,Life=Life}
local native={}
local first,err=Bootstrap.Install(engine,native,1703001,components)
assert(first and err==nil and created==1)
assert(engine.GCET.OtherService=="must_survive")
assert(engine.GCET.LookupCacheHost==first)
local second=Bootstrap.Install(engine,native,1703001,components)
assert(second==first and created==1)
local conflict,reason=Bootstrap.Install(engine,native,1703002,components)
assert(conflict==nil and reason=="EXISTING_HOST_CONFLICT")
local alien={GCET={LookupCacheHost={}}}
alien.Register=engine.Register
assert(not Bootstrap.Install(alien,native,1703001,components))
local unavailable={GCET={}}
unavailable.Register=function() error("scheduler not loaded") end
local bad,why=Bootstrap.Install(unavailable,native,1703001,components)
assert(bad==nil and why=="HOST_NOT_READY")
assert(unavailable.GCET.LookupCacheHost==nil)
assert(Bootstrap.Uninstall(engine))
assert(first.closed and engine.GCET.LookupCacheHost==nil)
assert(engine.GCET.OtherService=="must_survive")
assert(not Bootstrap.Uninstall(engine))
local third=Bootstrap.Install(engine,native,1703001,components)
assert(third and third~=first and created==2)
assert(Bootstrap.Uninstall(engine))
print("PASS singleton 0-Engine native wake bootstrap, collisions and cleanup")
