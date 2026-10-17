local oldRegister,oldGetMod,oldGame=registerForEvent,GetMod,Game
local callbacks={}
registerForEvent=function(event,fn)
 assert(event=="onInit")
 callbacks[#callbacks+1]=fn
end
local available=false
local clients=0
local engine={Register=function(name)
 assert(name=="G-LookupCache")
 clients=clients+1
 return {Schedule={EveryFrame=function(_,fn)
  return {Cancel=function() end}
 end}}
end}
GetMod=function(name) assert(name=="0-Engine");return available and engine or nil end
Game={}
local Entry=dofile("native-static-locations/adapters/CETLookupCacheModEntry.lua")
assert(#callbacks==1 and clients==0)
assert(not Entry.TryAttach())
callbacks[1]()
assert(clients==0) -- Source must remain in original mode until engine ready.
available=true
local host=Entry.TryAttach()
assert(host and clients==1 and engine.GCET.LookupCacheHost==host)
local same=Entry.TryAttach()
assert(same==host and clients==1)
assert(engine.GCET.LookupCacheHostVersion==1)
local Boot=dofile("native-static-locations/adapters/CETNativeWakeBootstrap.lua")
assert(Boot.Uninstall(engine))
registerForEvent,GetMod,Game=oldRegister,oldGetMod,oldGame
print("PASS CET init-only host bootstrap, no recurring listener, late engine")
