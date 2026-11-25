local root, scenario = arg[1], arg[2]
package.path = root .. "/?.lua;" .. package.path
local cameras, forwards, pitch = 0, 0, 0
local fail = false
Game = {GetCameraSystem = function()
    cameras = cameras + 1
    if fail then error("camera unavailable") end
    return {GetActiveCameraForward = function()
        forwards = forwards + 1
        return {z = math.sin(math.rad(pitch))}
    end}
end}
local E = assert(loadfile(root .. "/init.lua"))()
local D = require("modules/DerivedState")
local function near(a,b) return math.abs(a-b) < 0.00001 end
local function tick(n) for i=1,n do E.Tick(1/60) end end
if scenario=="raw-root-before-update" then
    local _, rawState = pairs(E.GetState())
    pitch=20; tick(10)
    assert(cameras==10 and near(rawState.derived.lookPitch,20),"early raw root escape must preserve future sampling")
    print("Early raw root exposure PASS")
    return
end
pitch = 15
tick(10)
local state = E.GetState()
local retained = state.derived
assert(retained.district == "Watson")
if scenario:find("^old%-") then
    assert(cameras == 10 and near(retained.lookPitch,15), "mixed overlay must remain eager")
    pitch = 25; tick(2)
    assert(cameras == 12 and near(retained.lookPitch,25), "fallback freshness")
    print("Scalar mixed overlay PASS: " .. scenario)
    return
end
assert(cameras==0 and forwards==0, "unrequested camera producer ran")
if scenario=="invalid-epoch" then
    assert(near(retained.lookPitch,15) and cameras==1)
    pitch=20; tick(1) -- unobserved valid offer
    E.SetValid(false); tick(1)
    assert(near(retained.lookPitch,15) and cameras==1,"invalid frame must not invoke deferred native read")
    E.SetValid(true); tick(1)
    assert(near(retained.lookPitch,20) and cameras==2,"valid frame resumes sampling")
    pitch=30; tick(1)
    E.SetLoading(true)
    assert(near(retained.lookPitch,20) and cameras==2,"loading transition invalidates pending sample")
    E.SetLoading(false); tick(1)
    assert(near(retained.lookPitch,30) and cameras==3,"new loaded epoch")
elseif scenario=="scalar" then
    assert(near(retained.lookPitch,15) and cameras==1 and forwards==1, "first-use sample")
    assert(near(state.derived.lookPitch,15) and cameras==1, "shared scalar sample")
    pitch=30; tick(10)
    assert(cameras==1, "getter must not impose recurring reads")
    assert(near(retained.lookPitch,30) and cameras==2, "retained proxy freshness")
    E.Reset()
    assert(retained.lookPitch==0 and cameras==2, "reset must not resurrect old sample")
    pitch=40; tick(1)
    assert(near(retained.lookPitch,40) and cameras==3, "new epoch")
    fail=true; tick(1)
    assert(retained.lookPitch==0 and cameras==4, "original protected failure result")
    assert(retained.lookPitch==0 and cameras==4, "failed sample shared")
else
    local raw
    if scenario=="raw-module" then
        raw=D.Get()
    elseif scenario=="raw-root" then
        for key,value in pairs(state) do if key=="derived" then raw=value end end
    else
        local iter, tbl = pairs(retained)
        raw=tbl
        assert(type(iter)=="function")
    end
    assert(raw and near(raw.lookPitch,15) and cameras==1,"raw escape must sample immediately")
    pitch=35; tick(3)
    assert(near(raw.lookPitch,35) and cameras==4, "raw reference must retain original cadence")
    E.Reset()
    assert(raw.lookPitch==0 and cameras==4,"raw reset")
    pitch=45; tick(1)
    assert(near(raw.lookPitch,45) and cameras==5,"exposure survives reset")
end
print("Central derived scalar native lookup contract PASS: " .. scenario)
