-- Experimental source-proven scheduler-job gate for native location wake.
-- This does not discover locations or register an update hook.
-- The Resolver must prove the near jobs contain the original exact tests,
-- and the retired far job is ONLY the original location rediscovery lane.
local Gate={}
Gate.__index=Gate

local function validJob(job)
  return type(job)=="table"
    and type(job.Pause)=="function" and type(job.Resume)=="function"
end

function Gate.new(farJob,nearJobs,keepAlive,restoreOriginal,onFarHousekeeping)
  assert(validJob(farJob),"original far-discovery job must be pauseable")
  assert(type(nearJobs)=="table","near jobs required")
  for _,job in ipairs(nearJobs) do assert(validJob(job)) end
  if keepAlive~=nil then assert(type(keepAlive)=="function") end
  if restoreOriginal~=nil then assert(type(restoreOriginal)=="function") end
  if onFarHousekeeping~=nil then assert(type(onFarHousekeeping)=="function") end
  return setmetatable({
    far=farJob,near=nearJobs,keepAlive=keepAlive,
    restoreOriginal=restoreOriginal,onFarHousekeeping=onFarHousekeeping,
    mode="ORIGINAL", lastNativeState=nil, fallbackPending=false,
  },Gate)
end

function Gate:_mustKeepActive()
  if not self.keepAlive then return false end
  -- Failure to classify an independent live responsibility is not FAR.
  local ok,result=pcall(self.keepAlive)
  return not ok or result~=false
end

local function doJob(job,operation)
  -- 0-Engine Scheduler job API provides handle.Pause()/Resume().
  -- Never suppress a failure and pretend that the old discovery retired.
  local ok,result=pcall(job[operation])
  assert(ok and result~=false,operation.." rejected")
end

function Gate:_apply()
  local live=self:_mustKeepActive()
  if self.mode=="INACTIVE" then
    -- Author explicitly said this location responsibility is irrelevant.
    -- Independent non-location listeners are NOT among these jobs.
    doJob(self.far,"Pause")
    for _,job in ipairs(self.near) do
      doJob(job,live and "Resume" or "Pause")
    end
    return
  end
  if self.mode=="ORIGINAL" then
    -- Fail-open: restore the authored far discovery, regardless of distance.
    doJob(self.far,"Resume")
    for _,job in ipairs(self.near) do
      doJob(job,live and "Resume" or "Pause")
    end
    return
  end
  assert(self.mode=="NEAR" or self.mode=="FAR")
  -- Native is authoritative only for location discovery; independent
  -- active gameplay stays live in either proximity state.
  doJob(self.far,"Pause")
  for _,job in ipairs(self.near) do
    doJob(job,(self.mode=="NEAR" or live) and "Resume" or "Pause")
  end
end

function Gate:_transition(mode)
  if self.mode==mode and not (mode=="ORIGINAL" and self.fallbackPending) then return true end
  self.mode=mode
  -- Source adapters can supply their authored mode reset (e.g. a
  -- setNearMode(false) transition). Merely resuming the far job can be
  -- insufficient when author nearMode still says it is already NEAR.
  local restored=true
  if mode=="ORIGINAL" and self.restoreOriginal then
    local ok,result=pcall(self.restoreOriginal)
    restored=ok and result~=false
  end
  local ok=pcall(self._apply,self)
  if not ok or not restored then
    self.mode="ORIGINAL"
    self.fallbackPending=true
    -- Attempt original service even when a near-job operation failed.
    pcall(self.far.Resume)
    return false
  end
  self.fallbackPending=false
  return true
end

function Gate:onNear(_,initial)
  if self.mode=="INACTIVE" then return true end
  return self:_transition("NEAR")
end

function Gate:onFar(_,initial)
  if self.mode=="INACTIVE" then return true end
  if self.mode~="FAR" and self.onFarHousekeeping then
    -- Preserve once-on-exit author cleanup even when the periodic FAR
    -- discovery job has been retired. Failure resumes original discovery.
    local ok,result=pcall(self.onFarHousekeeping)
    if not ok or result==false then
      self:onFallback("AUTHOR_FAR_CLEANUP_FAILED")
      return false
    end
  end
  return self:_transition("FAR")
end

function Gate:onFallback(_)
  if self.mode=="INACTIVE" then return true end
  return self:_transition("ORIGINAL")
end

function Gate:deactivate()
  return self:_transition("INACTIVE")
end

function Gate:reactivate()
  if self.mode=="INACTIVE" then return self:_transition("ORIGINAL") end
  return true
end

function Gate:reconcileAuthorTransition()
  -- Call when the source's own setNearMode/active-state path changes job
  -- handles: preserve independent activity without resurrecting far scans.
  local ok=pcall(self._apply,self)
  if not ok then
    self.mode="ORIGINAL"
    self.fallbackPending=true
    pcall(self.far.Resume)
    return false
  end
  return true
end

function Gate:isNative()
  return self.mode~="ORIGINAL"
end

return Gate
