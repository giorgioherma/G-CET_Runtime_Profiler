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

function Gate.new(farJob,nearJobs,keepAlive)
  assert(validJob(farJob),"original far-discovery job must be pauseable")
  assert(type(nearJobs)=="table","near jobs required")
  for _,job in ipairs(nearJobs) do assert(validJob(job)) end
  if keepAlive~=nil then assert(type(keepAlive)=="function") end
  return setmetatable({
    far=farJob,near=nearJobs,keepAlive=keepAlive,
    mode="ORIGINAL", lastNativeState=nil,
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
  if self.mode==mode then return true end
  self.mode=mode
  local ok=pcall(self._apply,self)
  if not ok then
    self.mode="ORIGINAL"
    -- Attempt original service even when a near-job operation failed.
    pcall(self.far.Resume)
    return false
  end
  return true
end

function Gate:onNear(_,initial)
  return self:_transition("NEAR")
end

function Gate:onFar(_,initial)
  return self:_transition("FAR")
end

function Gate:onFallback(_)
  return self:_transition("ORIGINAL")
end

function Gate:reconcileAuthorTransition()
  -- Call when the source's own setNearMode/active-state path changes job
  -- handles: preserve independent activity without resurrecting far scans.
  local ok=pcall(self._apply,self)
  if not ok then
    self.mode="ORIGINAL"
    pcall(self.far.Resume)
    return false
  end
  return true
end

function Gate:isNative()
  return self.mode~="ORIGINAL"
end

return Gate
