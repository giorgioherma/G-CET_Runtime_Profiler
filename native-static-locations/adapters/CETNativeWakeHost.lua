-- Experimental aggregate CET wake host. Install ONE shared job in 0-Engine,
-- not a per-mod native poller. Resolver must inject only source-proven owners.
local Host={}
Host.__index=Host

function Host.new(native,client,consumerId,hubFactory,setFactory,gateFactory,lifecycleFactory)
  assert(type(client)=="table" and type(client.Schedule)=="table")
  assert(type(client.Schedule.EveryFrame)=="function")
  assert(type(hubFactory)=="table" and type(hubFactory.new)=="function")
  assert(type(setFactory)=="table" and type(setFactory.new)=="function")
  assert(type(gateFactory)=="table" and type(gateFactory.new)=="function")
  return setmetatable({
    native=native,hub=hubFactory.new(native,consumerId),client=client,
    setFactory=setFactory,gateFactory=gateFactory,
    lifecycleFactory=lifecycleFactory,
    gates={},sources={},sharedJob=nil,closed=false,
  },Host)
end

function Host:_install()
  if self.sharedJob then return true end
  local ok,handle=pcall(self.client.Schedule.EveryFrame,{
    id="gcet.lookupcache.native.shared",
    active=true,pause="never",clock="game",
  },function() self.hub:step() end)
  if not ok or type(handle)~="table"
     or type(handle.Cancel)~="function" then
    -- No other dispatch mechanism is installed: keep original author jobs.
    for _,gate in pairs(self.gates) do gate:onFallback("HOST_NOT_READY") end
    return false
  end
  self.sharedJob=handle
  return true
end

function Host:attach(ownerId,farJob,nearJobs,keepAlive,restoreOriginal)
  assert(not self.closed and self.gates[ownerId]==nil)
  local gate=self.gateFactory.new(farJob,nearJobs,keepAlive,restoreOriginal)
  local owner=self.setFactory.new(gate)
  owner:bind(self.hub,ownerId)
  self.gates[ownerId]=gate
  -- Before the first healthy snapshot, every job stays in original mode.
  self:_install()
  return gate
end

function Host:attachSource(ownerId,farJob,nearJobs,keepAlive,epoch,fingerprint,sites,relevant,restoreOriginal)
  assert(not self.closed and self.gates[ownerId]==nil)
  assert(type(self.lifecycleFactory)=="table"
      and type(self.lifecycleFactory.new)=="function",
      "native source lifecycle module required")
  local lifecycle=self.lifecycleFactory.new(self.native,ownerId,
      function() self.hub:_stop() end)
  -- Fail closed for sources and fail open for gameplay: no Gate exists yet,
  -- so the original far-discovery job is still running.
  local ok,reason=lifecycle:register(epoch,fingerprint,sites,relevant)
  if not ok then return nil,reason end
  local gate=self:attach(ownerId,farJob,nearJobs,keepAlive,restoreOriginal)
  self.sources[ownerId]={lifecycle=lifecycle,gate=gate,sites=self.setFactory}
  if not relevant then
    gate:deactivate()
    self.hub:removeOwner(ownerId)
  end
  return gate
end

function Host:setRelevant(ownerId,relevant)
  local src=self.sources[ownerId]
  if not src or type(relevant)~="boolean" then return false end
  if not relevant then
    -- Gate cannot resume an author scanner for a fully irrelevant source.
    src.gate:deactivate()
    self.hub:removeOwner(ownerId)
    return src.lifecycle:setRelevant(false)
  end
  -- Original discovery resumes until native supplies a fresh known snapshot.
  if not src.gate:reactivate() then return false end
  local ok=src.lifecycle:setRelevant(true)
  if not ok then return false end
  if not self.hub.owners[ownerId] then
    self.setFactory.new(src.gate):bind(self.hub,ownerId)
  else
    self.hub:_stop()
  end
  return self:_install()
end

function Host:replaceSourceEpoch(ownerId,epoch,fingerprint,sites,relevant)
  local src=self.sources[ownerId]
  if not src then return false,"UNKNOWN_OWNER" end
  if relevant then src.gate:onFallback("SOURCE_EPOCH_UPDATE") end
  local ok,reason=src.lifecycle:register(epoch,fingerprint,sites,relevant)
  if not ok then return false,reason end
  -- Every changed native coordinate needs fresh snapshot, even stationary.
  if relevant then return self:setRelevant(ownerId,true) end
  return self:setRelevant(ownerId,false)
end

function Host:shutdown()
  if self.closed then return end
  self.closed=true
  if self.sharedJob then pcall(self.sharedJob.Cancel);self.sharedJob=nil end
  self.hub:shutdown()
  for _,src in pairs(self.sources) do src.lifecycle:retire() end
  for _,gate in pairs(self.gates) do gate:onFallback("HOST_SHUTDOWN") end
end

return Host
