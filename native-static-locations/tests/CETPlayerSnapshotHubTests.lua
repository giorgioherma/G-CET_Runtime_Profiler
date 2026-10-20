package.path = "./native-static-locations/adapters/?.lua;"..package.path
local Hub=require("CETPlayerSnapshotHub")
local n={state=1,epoch=1,flags=4,subscribeCount=0,unsubCount=0}
function n.GCETPlayerSubscribe(id,mask)
 assert(id==4001 and mask==3)
 n.subscribeCount=n.subscribeCount+1
 return true
end
function n.GCETPlayerUnsubscribe(id)
 assert(id==4001)
 n.unsubCount=n.unsubCount+1
 return true
end
function n.GCETPlayerPoll(id)
 assert(id==4001)
 local v=n.flags;n.flags=0
 return v
end
function n.GCETPlayerStatus()return n.state end
function n.GCETPlayerEpoch()return n.epoch end
function n.GCETPlayerX()return 22 end
function n.GCETPlayerY()return -10 end
function n.GCETPlayerZ()return 45 end
local fallback={}
local positions={}
local avail={}
local h=Hub.new(n,4001,function(reason)fallback[#fallback+1]=reason end)
local p=h:Watch("position",function(x,y,z,epoch)positions[#positions+1]={x,y,z,epoch} end)
local a=h:Watch("availability",function(yes,epoch)avail[#avail+1]={yes,epoch} end)
local ok,reason=h:Step()
assert(not ok and reason=="PLAYER_UNKNOWN" and #fallback==1 and #positions==0)
n.state=2;n.flags=4
assert(h:Step() and #positions==1 and positions[1][1]==22)
assert(#avail==1 and avail[1][1]==true)
assert(h:Step() and #positions==1)
n.flags=1;assert(h:Step() and #positions==2 and #avail==1)
n.flags=2;assert(h:Step() and #positions==2 and #avail==2)
p.Cancel();a.Cancel()
assert(n.unsubCount==1)
n.flags=4;assert(h:Step()==false and n.subscribeCount==1)
local again=h:Watch("availability",function() end)
n.flags=4;assert(h:Step() and n.subscribeCount==2)
n.flags=-1;ok,reason=h:Step()
assert(not ok and reason=="NATIVE_POLL_FAILED" and n.unsubCount==2)
again.Cancel()
h:Shutdown();h:Shutdown()
print("CETPlayerSnapshotHub tests PASS")
