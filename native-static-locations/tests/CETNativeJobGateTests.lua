local Gate=dofile("native-static-locations/adapters/CETNativeJobGate.lua")
local function job(initial)
  local j={active=initial,pauses=0,resumes=0}
  j.Pause=function() j.active=false;j.pauses=j.pauses+1;return true end
  j.Resume=function() j.active=true;j.resumes=j.resumes+1;return true end
  return j
end
local far,near,active=job(true),job(false),false
local gate=Gate.new(far,{near},function() return active end)
assert(gate:isNative()==false and far.active)
-- Native FAR retires the source-owned far-world discovery. No near job.
assert(gate:onFar(17,true) and not far.active and not near.active)
local count=far.pauses
assert(gate:onFar(17,false) and far.pauses==count)
-- A native enter wakes the ORIGINAL near-exact author check, not a quest start.
assert(gate:onNear(17,false) and not far.active and near.active)
-- Author Scheduler methods are permitted to return nil on success.
local nilJob=job(true)
nilJob.Pause=function() nilJob.active=false end
nilJob.Resume=function() nilJob.active=true end
local nilGate=Gate.new(nilJob,{job(false)})
assert(nilGate:onFar(1,true) and not nilJob.active)
assert(nilGate:onFallback("lost") and nilJob.active)
-- If author logic changed both handles, one lifecycle reconciliation
-- must not resurrect old far discovery.
far.Resume()
near.Pause()
assert(gate:reconcileAuthorTransition())
assert(not far.active and near.active)
-- Active gameplay is independent: native FAR may not silence it.
active=true
assert(gate:onFar(17,false) and not far.active and near.active)
active=false
assert(gate:reconcileAuthorTransition() and not near.active)
-- Missing native service restores the original source discovery.
assert(gate:onFallback("NATIVE_UNAVAILABLE"))
assert(far.active and not near.active and not gate:isNative())
-- No additional host update, no automatic source admission.
assert(gate:onNear(17,true) and near.active)
-- Native job API failure: original job is resumed fail-open.
local broken=job(true)
local brokenNear=job(false)
brokenNear.Resume=function() error("engine unavailable") end
local bad=Gate.new(broken,{brokenNear})
assert(not bad:onNear(99,true))
assert(broken.active and not bad:isNative())
-- If author active predicate errors, keep the independent lane alive.
local predicate=Gate.new(job(true),{job(false)},function() error("unavailable") end)
assert(predicate:onFar(22,true) and predicate.near[1].active)
-- Author nearMode can remain true after native leaves or becomes UNKNOWN.
-- Source-owned transition must reset it before original far timer resumes.
local authoredNear=true
local original=job(true)
local restoreCount=0
local restoredGate=Gate.new(original,{job(false)},nil,function()
 authoredNear=false
 restoreCount=restoreCount+1
end)
assert(restoredGate:onNear(12,true))
authoredNear=true
assert(restoredGate:onFallback("COORDINATOR_LOST"))
assert(not authoredNear and original.active and restoreCount==1)
assert(restoredGate:onFallback("COORDINATOR_LOST") and restoreCount==1)
-- An author restorer may error. Do not leave original scan suspended.
local originalFail=job(true)
local failing=Gate.new(originalFail,{job(false)},nil,
 function() error("author reset failed") end)
assert(failing:onNear(13,true))
assert(not failing:onFallback("NATIVE_UNAVAILABLE"))
assert(originalFail.active and not failing:isNative())
print("PASS native job gate proximity, author-state restoration, fail-open")
