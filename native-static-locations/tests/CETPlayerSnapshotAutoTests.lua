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
print("CETPlayerSnapshotAutoTests PASS")
