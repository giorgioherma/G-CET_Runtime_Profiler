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
    assert(cameras==10 and near(rawState.derived.lookPitch,20),"early raw root snapshot")
    return
end
pitch=15; tick(10)
local state=E.GetState()
local retained=state.derived
assert(retained.district=="Watson" and cameras==10 and forwards==10)
-- Change the engine AFTER the producer phase, before the first observation.
pitch=35
if scenario=="invalid-epoch" then
    assert(near(retained.lookPitch,15) and cameras==10,"historical first observation, not a live query")
    E.SetValid(false); tick(1)
    assert(near(retained.lookPitch,15) and cameras==10,"invalid frame retains snapshot")
    E.SetValid(true); tick(1)
    assert(near(retained.lookPitch,35) and cameras==11)
    pitch=30; E.SetLoading(true)
    assert(near(retained.lookPitch,35) and cameras==11,"loading cannot synthesize another sample")
    E.SetLoading(false); tick(1)
    assert(near(retained.lookPitch,30) and cameras==12)
elseif scenario=="scalar" or scenario:find("^old%-") then
    assert(near(retained.lookPitch,15) and cameras==10,"historical first observation, not a live query")
    assert(near(state.derived.lookPitch,15) and cameras==10,"shared cached scalar")
    tick(10)
    assert(cameras==20 and near(retained.lookPitch,35))
    E.Reset()
    assert(retained.lookPitch==0 and cameras==20)
    pitch=40; tick(1)
    assert(near(retained.lookPitch,40) and cameras==21)
    fail=true; tick(1); fail=false; pitch=50
    assert(retained.lookPitch==0 and cameras==22,"scheduled failure must not be replayed")
    tick(1)
    assert(near(retained.lookPitch,50) and cameras==23)
else
    local raw
    if scenario=="raw-module" then raw=D.Get()
    elseif scenario=="raw-root" then
        for key,value in pairs(state) do if key=="derived" then raw=value end end
    else local _,tbl=pairs(retained); raw=tbl end
    assert(raw and near(raw.lookPitch,15) and cameras==10,"late raw exposure preserves historical value")
    tick(3)
    assert(near(raw.lookPitch,35) and cameras==13)
    E.Reset()
    assert(raw.lookPitch==0 and cameras==13)
    pitch=45; tick(1)
    assert(near(raw.lookPitch,45) and cameras==14)
end
print("Central derived scalar historical snapshot PASS: " .. scenario)
