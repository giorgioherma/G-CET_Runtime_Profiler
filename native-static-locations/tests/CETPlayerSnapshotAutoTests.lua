package.path = "./native-static-locations/adapters/?.lua;"..package.path
local Hub=require("CETPlayerSnapshotHub")
local function no(result,reason)
    local hub,why=Hub.TryAuto(result,7001)
    assert(hub==nil and why==reason, tostring(why))
end
no(nil,"NATIVE_UNAVAILABLE")
no({},"NATIVE_UNAVAILABLE")
no({GCETPlayerApiVersion=function()return 0 end,
    GCETPlayerCapabilities=function()return 31 end},"API_MISMATCH")
no({GCETPlayerApiVersion=function()return 1 end,
    GCETPlayerCapabilities=function()return 15 end},"CAPABILITY_MISMATCH")
no({GCETPlayerApiVersion=function()return 1 end,
    GCETPlayerCapabilities=function()return 31 end},"INCOMPLETE_NATIVE_ABI")
local calls={subscribe=0,unsubscribe=0,poll=0,read=0}
local g={
 GCETPlayerApiVersion=function()return 1 end,
 GCETPlayerCapabilities=function()return 31 end,
 GCETPlayerSubscribe=function(id,fields)
   assert(id==7001 and fields==3)
   calls.subscribe=calls.subscribe+1;return true
 end,
 GCETPlayerUnsubscribe=function(id)
   assert(id==7001)
   calls.unsubscribe=calls.unsubscribe+1;return true
 end,
 GCETPlayerPoll=function(id)
   assert(id==7001)
   calls.poll=calls.poll+1
   if calls.poll==1 then return 4 end
   if calls.poll==2 then return 1 end
   return 0
 end,
 GCETPlayerStatus=function()return 2 end,
 GCETPlayerEpoch=function()return 6 end,
 GCETPlayerX=function()calls.read=calls.read+1;return 123 end,
 GCETPlayerY=function()calls.read=calls.read+1;return -43 end,
 GCETPlayerZ=function()calls.read=calls.read+1;return 6 end,
}
local hub,why=Hub.TryAuto(g,7001,function(reason)error("unexpected fallback "..reason)end)
assert(hub and why==nil)
assert(calls.subscribe==0 and calls.poll==0 and calls.read==0)
local ok,reason=hub:Step()
assert(ok==false and reason=="IDLE" and calls.subscribe==0)
local count=0
local listener=hub:Watch("position",function(x,y,z,epoch)
 assert(x==123 and y==-43 and z==6 and epoch==6)
 count=count+1
end)
assert(hub:Step() and count==1 and calls.subscribe==1 and calls.read==3)
assert(hub:Step() and count==2 and calls.subscribe==1 and calls.read==6)
assert(hub:Step() and count==2 and calls.read==6)
listener.Cancel()
assert(calls.unsubscribe==1 and hub.listenerCount==0)
hub:Shutdown()
assert(calls.unsubscribe==1)
-- Legacy scalar consumers remain compatible without optional bit 32.
local scalarOnly=Hub.TryAuto(g,7001)
assert(scalarOnly)
local absent,why=scalarOnly:TryReadVector4()
assert(absent==nil and why=="TYPED_VECTOR4_UNAVAILABLE")
scalarOnly:Shutdown()

-- One typed RTTI Vector4 read, not three scalar C calls or a Lua-rebuilt
-- plain table. The real native RTTI path owns the object and W component.
local reads=0
g.GCETPlayerCapabilities=function()return 63 end
g.GCETPlayerVector4=function()
    reads=reads+1
    return {x=11,y=22,z=33,w=0.75} -- test stand-in for CET Vector4 userdata
end
local typed=Hub.TryAuto(g,7001)
assert(typed)
local vec,reason=typed:TryReadVector4()
assert(vec and not reason and vec.x==11 and vec.w==0.75 and reads==1)
typed:Shutdown()
g.GCETPlayerStatus=function()return 1 end
local unknown=Hub.TryAuto(g,7001)
assert(unknown)
local unavailable,unknownReason=unknown:TryReadVector4()
assert(unavailable==nil and unknownReason=="PLAYER_UNKNOWN" and reads==1)
unknown:Shutdown()
g.GCETPlayerVector4Valid=function()return true end
g.GCETPlayerVector4Live=function()return {x=11,y=22,z=33,w=0.75} end
g.GCETPlayerCapabilities=function()return 127 end
local demand=Hub.TryAuto(g,7001)
assert(demand)
local result,err=demand:TryReadVector4(true)
assert(result and err==nil and result.w==0.75)
demand:Shutdown()
g.GCETPlayerVector4Valid=function()return false end
local missing=Hub.TryAuto(g,7001)
assert(missing)
local n,reason=missing:TryReadVector4()
assert(n==nil and reason=="PLAYER_UNKNOWN")
missing:Shutdown()
g.GCETPlayerVector4Valid=nil
local noDemand,missingReason=Hub.TryAuto(g,7001)
assert(noDemand==nil and missingReason=="INCOMPLETE_DEMAND_ABI")
g.GCETPlayerCapabilities=function()return 63 end
g.GCETPlayerVector4=nil
local bad,failed=Hub.TryAuto(g,7001)
assert(bad==nil and failed=="INCOMPLETE_VECTOR4_ABI")

print("CETPlayerSnapshotAutoTests PASS")
