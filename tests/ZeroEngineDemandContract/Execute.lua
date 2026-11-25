local root = assert(arg[1])
package.path = root .. "/?.lua;" .. package.path
local counts, hits = {}, {}
function count(key) counts[key] = (counts[key] or 0) + 1 end
local tick = assert(loadfile(root .. "/init.lua"))()
local P = require("modules/Proximity")
local S = require("modules/SpatialHash")
local scheduler = require("modules/G-CET/Scheduler")
local frame = 0
local function advance(n)
    for _=1,n do frame=frame+1; tick(1/60) end
end
local function hit(key)
    return function(f) hits[key] = hits[key] or {}; table.insert(hits[key], f) end
end
if arg[2] ~= "complete" then
    local a = P.RegisterZone({throttle=5, callback=hit("fallback")})
    advance(10)
    assert(table.concat(hits.fallback,",")=="5,10", "partial overlay lost callbacks")
    assert(counts["proximity-pass"]==10, "partial overlay must keep stock cadence")
    a.unregister()
    print("Partial overlay fallback PASS: " .. arg[2])
    return
end
advance(10)
assert(next(counts)==nil, "zero registrations must execute no producer scans")
local a = P.RegisterZone({throttle=5, callback=hit("five")})
local b = P.RegisterZone({throttle=7, callback=hit("seven")})
local q = S.RegisterSpatialSet("query", {}, {pollThrottle=1})
local c = S.RegisterSpatialSet("poll", {}, {pollRadiusSq=100, pollThrottle=7, callback=hit("spatial")})
advance(25)
assert(table.concat(hits.five,",")=="15,20,25,30,35")
assert(table.concat(hits.seven,",")=="14,21,28,35")
assert(table.concat(hits.spatial,",")=="14,21,28,35")
assert(counts["proximity-pass"]==8 and counts["spatial-pass"]==4, "union of due frames; no double scan")
a.unregister(); a.unregister()
advance(14)
assert(counts["proximity-pass"]==10 and counts["spatial-pass"]==6, "slower remaining consumer cadence")
b.unregister(); c.unregister()
advance(30)
assert(counts["proximity-pass"]==10 and counts["spatial-pass"]==6, "last removal and query-only set dormant")
q.unregister()
local d=P.RegisterZone({throttle=3,callback=hit("three")})
advance(5)
assert(table.concat(hits.three,",")=="81,84", "registration after dormancy resumes original phase")
d.unregister()
-- Existing scheduler job timing is independent; demand checks do not advance clocks.
local calls=0
scheduler.Init(nil,nil)
scheduler.EveryFrames(2,{spread=false},function() calls=calls+1 end)
for i=1,6 do
    scheduler.Update({frame=i,dt=1/60,playing=true})
end
assert(calls==3,"existing scheduler frame jobs changed")
print("Generated scheduler demand: zero, 5/7 union, removal, reactivation, query-only and original scheduler timing PASS")
