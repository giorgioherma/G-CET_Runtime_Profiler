local root, scenario=arg[1],arg[2]
package.path=root.."/?.lua;"..package.path
local transaction,slot,workspot,actor=0,0,0,0
local armed,inWorkspot,fail=false,false,false
local E,checkOrder,reenter
local orderChecks,orderBad=0,0
TestPlayer={}
TweakDBID={new=function(k)return k end}
Game={
    GetCameraSystem=function()return nil end,
    GetTransactionSystem=function()
        transaction=transaction+1
        if fail then error("system unavailable")end
        return {GetItemInSlot=function(_,p,k)
            assert(p==TestPlayer and k=="AttachmentSlots.WeaponRight")
            slot=slot+1
            if reenter then
                reenter=false
                E.SetValid(false);E.Tick(1/60);E.SetValid(true)
            end
            return armed and {} or nil
        end}
    end,
    GetWorkspotSystem=function()
        workspot=workspot+1
        if checkOrder then
            orderChecks=orderChecks+1
            if E.GetState().derived.hasWeapon~=armed then orderBad=orderBad+1 end
        end
        return {IsActorInWorkspot=function(_,p)
            assert(p==TestPlayer);actor=actor+1;return inWorkspot
        end}
    end
}
if scenario=="old-module" or scenario=="stock" or scenario=="stock-reentrant" then package.loaded["modules/DerivedState"]=dofile(root.."/stock-module.lua") end
if scenario=="old-scheduler" then package.loaded["modules/G-CET/Scheduler"]=dofile(root.."/stock-scheduler.lua") end
if scenario=="legacy-module" then package.loaded["modules/DerivedState"]=dofile(root.."/legacy-module.lua") end
if scenario=="legacy-scheduler" then package.loaded["modules/G-CET/Scheduler"]=dofile(root.."/legacy-scheduler.lua") end
E=dofile(root..(scenario=="old-host" and "/stock-host.lua" or "/init.lua"))
local D=require("modules/DerivedState")
local function tick(n)for i=1,n do E.Tick(1/60)end end
if scenario=="stock" or scenario=="no-demand" then
    tick(120)
    assert(transaction==20 and slot==20 and workspot==20 and actor==20,"historical producer must retain original count")
    print("Historical scalar native calls: "..(transaction+slot+workspot+actor))
elseif scenario=="reentrant" or scenario=="stock-reentrant" then
    armed=true;inWorkspot=true;reenter=true;tick(6)
    assert(transaction==1 and slot==1 and workspot==1 and actor==1,
        "reentrant invalid frame must not suppress later original-phase getter")
    armed=false;inWorkspot=false
    local retained=E.GetState().derived
    assert(retained.hasWeapon and retained.isInWorkspot and transaction==1 and workspot==1,
        "reentrant phase sample must remain historical")
elseif scenario=="raw" then
    armed=true;inWorkspot=true;tick(6)
    armed=false;inWorkspot=false;tick(2)
    local _,raw=pairs(E.GetState())
    assert(raw.derived.hasWeapon and raw.derived.isInWorkspot and transaction==1 and workspot==1,
        "late raw exposure must preserve original snapshot")
    tick(4)
    assert(not raw.derived.hasWeapon and not raw.derived.isInWorkspot and transaction==2 and workspot==2)
    E.Reset();tick(6)
    assert(transaction==3 and workspot==3,"raw exposure survives reset")
elseif scenario=="reset" then
    armed=true;inWorkspot=true;tick(6)
    local retained=E.GetState().derived
    assert(retained.hasWeapon and transaction==1)
    tick(6);E.SetValid(false);tick(1)
    armed=false
    assert(retained.hasWeapon and transaction==2,"invalid frame must retain historical sample")
    E.SetLoading(true);E.SetLoading(false);E.SetValid(true)
    tick(1)
    assert(retained.hasWeapon and transaction==2,"loading must not replay reads")
    E.Reset()
    assert(not retained.hasWeapon and transaction==2)
    armed=true;tick(6)
    assert(retained.hasWeapon and transaction==3,"fresh valid offer after reset")
else
    armed=true;inWorkspot=true;tick(6)
    armed=false;inWorkspot=false;tick(2)
    local retained=E.GetState().derived
    assert(retained.hasWeapon and retained.isInWorkspot and transaction==1 and workspot==1,
        "frame 8 first read must return frame 6 values")
    assert(D.Get().hasWeapon and transaction==1,"late raw access cannot replay getter")
    checkOrder=true;tick(4)
    assert(not retained.hasWeapon and not retained.isInWorkspot and transaction==2 and workspot==2,
        "original six-frame phase and publication order")
    assert(orderChecks==1 and orderBad==0,"weapon publication precedes workspot lookup")
    armed=true;fail=true;checkOrder=false;tick(6)
    fail=false
    assert(not retained.hasWeapon and transaction==3 and slot==2,"scheduled failure must not be replayed")
    tick(6)
    assert(retained.hasWeapon and transaction==4 and slot==3)
end
print("Cadenced historical scalar PASS: "..scenario)
