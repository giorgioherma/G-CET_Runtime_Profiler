-- Experimental aggregate CET wake host. Install ONE shared job in 0-Engine,
-- not a per-mod native poller. Resolver must inject only source-proven owners.
local Host={}
Host.__index=Host

function Host.new(native,client,consumerId,hubFactory,setFactory,gateFactory)
  assert(type(client)=="table" and type(client.Schedule)=="table")
  assert(type(client.Schedule.EveryFrame)=="function")
  assert(type(hubFactory)=="table" and type(hubFactory.new)=="function")
  assert(type(setFactory)=="table" and type(setFactory.new)=="function")
  assert(type(gateFactory)=="table" and type(gateFactory.new)=="function")
  return setmetatable({
    hub=hubFactory.new(native,consumerId),client=client,
    setFactory=setFactory,gateFactory=gateFactory,
    gates={},sharedJob=nil,closed=false,
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

function Host:attach(ownerId,farJob,nearJobs,keepAlive)
  assert(not self.closed and self.gates[ownerId]==nil)
  local gate=self.gateFactory.new(farJob,nearJobs,keepAlive)
  local owner=self.setFactory.new(gate)
  owner:bind(self.hub,ownerId)
  self.gates[ownerId]=gate
  -- Before the first healthy snapshot, every job stays in original mode.
  self:_install()
  return gate
end

function Host:shutdown()
  if self.closed then return end
  self.closed=true
  if self.sharedJob then pcall(self.sharedJob.Cancel);self.sharedJob=nil end
  self.hub:shutdown()
  for _,gate in pairs(self.gates) do gate:onFallback("HOST_SHUTDOWN") end
end

return Host
