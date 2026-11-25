local root, scenario=arg[1],arg[2]
package.path=root.."/?.lua;"..package.path
local transaction,slot,workspot,actor=0,0,0,0
local armed,inWorkspot,fail=false,false,false
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
            return armed and {} or nil
        end}
    end,
    GetWorkspotSystem=function()
        workspot=workspot+1
        return {IsActorInWorkspot=function(_,p)
            assert(p==TestPlayer);actor=actor+1;return inWorkspot
        end}
    end
}
if scenario=="old-module" or scenario=="stock" then package.loaded["modules/DerivedState"]=dofile(root.."/stock-module.lua") end
if scenario=="old-scheduler" then package.loaded["modules/G-CET/Scheduler"]=dofile(root.."/stock-scheduler.lua") end
local E=dofile(root..(scenario=="old-host" and "/stock-host.lua" or "/init.lua"))
local D=require("modules/DerivedState")
local function tick(n)for i=1,n do E.Tick(1/60)end end
if scenario:find("^old%-") then
    armed=true;inWorkspot=true;tick(12)
    assert(transaction==2 and workspot==2 and E.GetState().derived.hasWeapon and E.GetState().derived.isInWorkspot,
        "partial overlay must preserve original eager reads")
    print("Cadenced scalar mixed runtime fallback PASS: "..scenario)
    return
end
if scenario=="stock" then
    tick(120)
    assert(transaction==20 and slot==20 and workspot==20 and actor==20,"stock native read baseline")
    print("Stock pure scalar native calls: "..(transaction+slot+workspot+actor))
elseif scenario=="raw" then
    local _,raw=pairs(E.GetState())
    armed=true;inWorkspot=true;tick(6)
    assert(raw.derived.hasWeapon and raw.derived.isInWorkspot and transaction==1 and workspot==1)
    armed=false;inWorkspot=false;tick(5)
    assert(raw.derived.hasWeapon and raw.derived.isInWorkspot and transaction==1 and workspot==1)
    tick(1)
    assert(not raw.derived.hasWeapon and not raw.derived.isInWorkspot and transaction==2 and workspot==2)
    E.Reset();tick(6)
    assert(transaction==3 and workspot==3,"raw exposure survives reset")
elseif scenario=="no-demand" then
    tick(120)
    assert(transaction==0 and slot==0 and workspot==0 and actor==0,"unrequested fields executed native readers")
    print("Cadenced scalar: 0 native getter/system invocations in 120 unrequested frames")
elseif scenario=="mixed-fields" then
    armed=true;tick(6)
    local retained=E.GetState().derived
    assert(retained.hasWeapon and transaction==1 and slot==1 and workspot==0)
    armed=false;tick(5)
    assert(retained.hasWeapon and transaction==1,"original six-frame hold")
    tick(1)
    assert(not retained.hasWeapon and transaction==2 and workspot==0)
    -- Getter-only field can first be requested after its unused offer.
    inWorkspot=true;tick(3)
    assert(retained.isInWorkspot and workspot==1 and actor==1)
    tick(33)
    assert(transaction==2 and workspot==1,"getter-only reads must not activate recurring native production")
    fail=true;assert(not retained.hasWeapon and transaction==3 and slot==2,"protected native failure")
elseif scenario=="reset" then
    armed=true;inWorkspot=true;tick(6)
    local retained=E.GetState().derived
    assert(retained.hasWeapon and transaction==1)
    tick(6);E.SetValid(false);tick(1)
    assert(retained.hasWeapon and transaction==1,"invalid frame must not invoke deferred native sample")
    E.SetLoading(true);E.SetLoading(false);E.SetValid(true)
    tick(1)
    assert(retained.hasWeapon and transaction==1,"loading invalidates pending sample")
    E.Reset()
    assert(not retained.hasWeapon and transaction==1)
    tick(6)
    assert(retained.hasWeapon and transaction==2,"fresh valid offer after reset")
end
print("Cadenced scalar native demand PASS: "..scenario)
