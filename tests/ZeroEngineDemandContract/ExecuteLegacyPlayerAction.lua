local root,scenario=arg[1],arg[2]
package.path=root.."/?.lua;"..package.path
package.loaded["modules/Logger"]={Log=function()end}
local callback,foreign
function Observe(_,_,fn)
    if callback then foreign=fn else callback=fn end
end
CName={new=function(s)return s end}
local count={prepare=0,bind=0,set=0,release=0,retire=0,marshal=0}
local sleeping=false
Game={}
if scenario~="missing-native" then
    Game.GCETObserverPrepare=function()count.prepare=count.prepare+1;return true end
    Game.GCETObserverBind=function()count.bind=count.bind+1;return foreign and 0 or 1 end
    Game.GCETObserverSetDemand=function(_,active)
        count.set=count.set+1;sleeping=not active
        if active and scenario=="wake-refused" then sleeping=false;return false end
        return true
    end
    Game.GCETObserverRelease=function()count.release=count.release+1;sleeping=false;return true end
    Game.GCETObserverRetire=function()count.retire=count.retire+1;sleeping=false end
end
local E=dofile(root.."/init.lua")
local S=require("modules/G-CET/Scheduler")
local frame=0
local function tick(n)
    for i=1,n or 1 do
        frame=frame+1;S.Update({frame=frame,now=frame/60,dt=1/60,realDt=1/60,
            playing=true,generation=0,inMenu=false})
    end
end
local trace={}
local function fire(v)
    if not sleeping or foreign then
        count.marshal=count.marshal+1
        callback(nil,v)
        if foreign then foreign(nil,v) end
    end
end
local first
if scenario=="pre-init" then first=E.Subscribe("PlayerAction",function(v)trace[#trace+1]=v end) end
E.Init()
if scenario=="mixed" then Observe("PlayerPuppet","OnAction",function(_,v)trace[#trace+1]="foreign:"..v end) end
tick()
fire("bind")
if scenario=="pre-init" then
    assert(count.prepare==0 and trace[1]=="bind","pre-init listener must stay active")
    first.unsubscribe();tick();fire("late-bind")
    assert(sleeping and count.bind==1,"last pre-init unsubscribe must arm original observer")
elseif scenario=="mixed" then
    assert(not sleeping and count.bind==1 and trace[1]=="foreign:bind","mixed chain retained")
    tick(2);assert(count.retire==1)
elseif scenario=="missing-native" then
    assert(not sleeping and count.prepare==0)
else
    assert(sleeping and count.prepare==1 and count.bind==1,"initial zero demand bind")
    local before=count.marshal
    tick(30)
    for i=1,120 do fire("idle") end
    assert(count.marshal==before and count.set==1,"dormant invocation must not marshal")
end
local h1,h2
h1=E.Subscribe("PlayerAction",function(v)
    trace[#trace+1]="a:"..v
    if v=="remove" then h1.unsubscribe();h2.unsubscribe() end
end)
h2=E.Subscribe("PlayerAction",function(v)trace[#trace+1]="b:"..v end)
fire("wake")
assert(trace[#trace-(foreign and 2 or 1)]=="a:wake" and trace[#trace-(foreign and 1 or 0)]=="b:wake","ordered immediate wake")
E.SetMenu(true);local n=#trace;fire("menu");assert(#trace==n+(foreign and 1 or 0))
E.SetMenu(false);E.SetPlaying(false);n=#trace;fire("paused");assert(#trace==n+(foreign and 1 or 0))
E.SetPlaying(true);n=#trace;fire("remove");assert(#trace==n+1+(foreign and 1 or 0),"reentrant unsubscribe preserves dispatch")
assert(E.Debug()==0,"stock listener count")
if scenario~="mixed" and scenario~="missing-native" and scenario~="wake-refused" then
    assert(sleeping,"last listener sleeps")
    local before=count.marshal;for i=1,120 do fire("idle") end
    assert(count.marshal==before,"no recurrent marshalling in mock native idle")
end
local h=E.Subscribe("PlayerAction",function(v)trace[#trace+1]="new:"..v end)
fire("again");assert(trace[#trace-(foreign and 1 or 0)]=="new:again")
h.unsubscribe()
print("Legacy PlayerAction generated demand "..scenario.." PASS; modeled marshal calls "..count.marshal)
