-- Actual generated host, router and scheduler. Model only the native ABI
-- here; executable guard/exception/lock proof belongs to the native CI.
local root,scenario=assert(arg[1]),assert(arg[2])
package.path=root.."/?.lua;"..package.path
local before,after
function Observe(klass,method,fn)
    assert(klass=="PlayerPuppet" and method=="OnAction" and not before)
    before=fn
end
function ObserveAfter(klass,method,fn) after=fn end
CName={new=function(v)return v end}
local count={prepare=0,bind=0,set=0,release=0,retire=0}
local nativeDormant=false
Game={}
if scenario~="missing-native" then
    Game.GCETObserverPrepare=function(klass,method)
        assert(klass=="PlayerPuppet" and method=="OnAction")
        count.prepare=count.prepare+1
        return scenario~="prepare-refused"
    end
    Game.GCETObserverBind=function()
        count.bind=count.bind+1
        return scenario=="bind-refused" and 0 or 1
    end
    Game.GCETObserverSetDemand=function(handle,active)
        assert(handle==1)
        count.set=count.set+1
        nativeDormant=not active
        if active and scenario=="wake-refused" then
            nativeDormant=false -- native false return invalidates old epoch
            return false
        end
        return true
    end
    Game.GCETObserverRelease=function(handle)
        assert(handle==1);count.release=count.release+1;nativeDormant=false;return true
    end
    Game.GCETObserverRetire=function()count.retire=count.retire+1;nativeDormant=false end
end
local router=assert(dofile(root.."/init.lua"))
local scheduler=require("modules/G-CET/Scheduler")
local frame=0
local function tick(n)
    for _=1,n or 1 do
        frame=frame+1
        scheduler.Update({frame=frame,now=frame/60,dt=1/60,realDt=1/60,
            playing=true,generation=0,inMenu=false})
    end
end
local calls=0
local action={GetName=function()return {value="MoveX"} end}
local function fire()
    if not nativeDormant then before(nil,action,nil) end
end
tick(20)
assert(count.prepare==0,"no subscriptions may not request attachment")
local a=router.Subscribe({actions={"MoveX"},decodeType=false},function()calls=calls+1 end)
tick(20);fire()
assert(calls==1 and count.prepare==0,"active subscription must not initiate attachment")
a.Pause()
if scenario=="cancel-before-prepare" then
    a.Resume();tick(20);fire()
    assert(calls==2 and count.prepare==0,"wake before preparation cancels one-shot")
    return
end
tick()
fire()
if scenario=="complete" or scenario=="wake-refused" then
    assert(count.prepare==1 and count.bind==1 and count.set==1 and nativeDormant,
        "first dormant callback binds and sleeps")
    tick(20);fire()
    assert(count.prepare==1 and count.bind==1 and count.set==1 and calls==1,"dormant path polls")
    a.Resume();fire()
    assert(calls==2 and not nativeDormant,"wake must deliver immediately")
    if scenario=="wake-refused" then
        tick(20);assert(count.release==1 and count.retire==1,"failed wake retirement")
        a.Pause();a.Resume();fire()
        assert(calls==3 and count.prepare==1,"failed native transition keeps stock")
    else
        local b=router.Subscribe({actions={"MoveX"},decodeType=false},function()calls=calls+10 end)
        a.Pause();fire();assert(calls==12 and count.set==2,"second listener must stay awake")
        b.Cancel();fire();assert(calls==12 and nativeDormant and count.set==3,"last listener sleeps")
        a.Resume();fire();assert(calls==13 and count.set==4,"reactivated listener")
    end
else
    tick(20);a.Resume();fire()
    assert(calls==2 and not nativeDormant,"native refusal must retain stock")
    if scenario=="bind-refused" then assert(count.bind==1 and count.retire==1,"mixed native chain cleanup")
    elseif scenario=="prepare-refused" then assert(count.prepare==1 and count.bind==0,"prepare refusal")
    else assert(count.prepare==0,"missing API") end
end
print("Generated observer demand "..scenario.." PASS")
