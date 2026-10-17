-- Generic Resolver-injected adoption point for an *already reviewed* author
-- scheduler lane. Called from existing mod callbacks/stage events, NEVER a
-- second per-mod polling listener. This does not discover or approve sites.
local Binding={}
Binding.__index=Binding

function Binding.new(hostProvider,ownerId,farJob,nearJobs,keepAlive,
                     restoreOriginal,source)
  assert(type(hostProvider)=="function")
  assert(type(ownerId)=="number" and ownerId>0 and ownerId%1==0)
  assert(type(farJob)=="table" and type(farJob.Pause)=="function"
      and type(farJob.Resume)=="function")
  assert(type(nearJobs)=="table")
  assert(type(source)=="table" and type(source.sites)=="function"
      and type(source.epoch)=="string" and source.epoch~=""
      and type(source.fingerprint)=="string"
      and #source.fingerprint>=8)
  return setmetatable({
    provider=hostProvider,owner=ownerId,far=farJob,near=nearJobs,
    keepAlive=keepAlive,restoreOriginal=restoreOriginal,source=source,
    host=nil,gate=nil,relevant=source.relevant~=false,
    attachedEpoch=nil,attachedFingerprint=nil,closed=false,
  },Binding)
end

function Binding:attempt()
  if self.closed then return false,"CLOSED" end
  if self.gate then return true end
  -- The author job and author callback remain running if bootstrap/natives
  -- are not ready. There is no new recurring discovery or fallback timer.
  local ok,host=pcall(self.provider)
  if not ok or type(host)~="table"
      or type(host.attachSource)~="function" or host.closed then
    return false,"NO_SHARED_HOST"
  end
  local sourceOk,sites=pcall(self.source.sites)
  if not sourceOk or type(sites)~="table" or #sites==0 then
    return false,"SOURCE_SITE_SET_UNAVAILABLE"
  end
  local attached,gate,why=pcall(host.attachSource,host,self.owner,
     self.far,self.near,self.keepAlive,self.source.epoch,
     self.source.fingerprint,sites,self.relevant,self.restoreOriginal)
  if not attached or type(gate)~="table" then
    return false,(attached and why) or "HOST_ADMISSION_FAILED"
  end
  self.host,self.gate=host,gate
  self.attachedEpoch=self.source.epoch
  self.attachedFingerprint=self.source.fingerprint
  return true
end

function Binding:setRelevant(relevant)
  if self.closed or type(relevant)~="boolean" then return false end
  self.relevant=relevant
  if not self.gate then return true end
  local ok,result=pcall(self.host.setRelevant,self.host,self.owner,relevant)
  if not ok or result~=true then
    self.gate:onFallback("STAGE_CHANGE_FAILED")
    return false
  end
  return true
end

function Binding:reconcile()
  if not self.gate then return true end
  local ok,result=pcall(self.gate.reconcileAuthorTransition,self.gate)
  return ok and result==true
end

function Binding:replaceEpoch(epoch,fingerprint,sites,relevant)
  if self.closed or type(epoch)~="string" or epoch==""
      or type(fingerprint)~="string" or #fingerprint<8
      or type(sites)~="table" or #sites==0
      or type(relevant)~="boolean" then return false,"BAD_EPOCH" end
  if self.gate then
    -- Failure must resume the original discovery, never silently continue
    -- on stale native anchors.
    self.gate:onFallback("SOURCE_EPOCH_UPDATE")
    local ok,result,reason=pcall(self.host.replaceSourceEpoch,self.host,
       self.owner,epoch,fingerprint,sites,relevant)
    if not ok or result~=true then
      return false,(ok and reason) or "EPOCH_UPDATE_FAILED"
    end
  end
  self.source.epoch=epoch
  self.source.fingerprint=fingerprint
  self.source.sites=function() return sites end
  self.relevant=relevant
  self.attachedEpoch=epoch
  self.attachedFingerprint=fingerprint
  return true
end

function Binding:shutdown()
  if self.closed then return true end
  self.closed=true
  if self.gate then
    self.gate:onFallback("AUTHOR_SHUTDOWN")
    -- Remove only this owner's armed sites; leave the shared dispatcher and
    -- other source owners alive. A native removal failure is fail-open.
    if type(self.host.detachSource)=="function" then
      local ok,retired=pcall(self.host.detachSource,self.host,self.owner)
      if not ok or not retired then
        pcall(self.host.setRelevant,self.host,self.owner,false)
      end
    else
      pcall(self.host.setRelevant,self.host,self.owner,false)
    end
  end
  return true
end

return Binding
