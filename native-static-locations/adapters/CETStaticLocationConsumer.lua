-- Development-only CET adapter. NOT a standalone CET mod, no onUpdate registration.
-- Run step() from the existing 0-Engine host. The Resolver must supply
-- source-proven owner callbacks and unique positive consumer/owner IDs.
local Hub={}
Hub.__index=Hub

function Hub.new(native, consumerId)
  assert(type(native)=="table" and type(consumerId)=="number" and consumerId>0)
  return setmetatable({
    native=native, consumerId=consumerId, owners={}, ownerCount=0,
    subscribed=false, maxEventsPerStep=512
  },Hub)
end

function Hub:addOwner(ownerId, onNear, onFar, onFallback)
  assert(type(ownerId)=="number" and ownerId>0)
  assert(type(onNear)=="function" and type(onFallback)=="function")
  assert(not self.owners[ownerId],"duplicate native anchor owner")
  self.owners[ownerId]={near=onNear,far=onFar,fallback=onFallback}
  self.ownerCount=self.ownerCount+1
end

function Hub:_fallback(reason)
  for _,handler in pairs(self.owners) do
    -- Fallback is the source-proven original discovery path, not a no-op.
    pcall(handler.fallback,reason)
  end
end

function Hub:_stop()
  if self.subscribed then pcall(self.native.GCETStaticUnsubscribe,self.consumerId) end
  self.subscribed=false
end

function Hub:shutdown()
  self:_stop()
end

function Hub:_notify(owner,id,state,initial)
  local handler=self.owners[owner]
  if not handler then return end
  if state==1 then
    handler.near(id,initial)
  elseif state==0 and handler.far then
    handler.far(id,initial)
  else
    handler.fallback("UNKNOWN_ANCHOR_STATE")
  end
end

function Hub:_snapshot()
  local native=self.native
  -- Single main thread: there must be no Update between the state reads
  -- and AckSnapshot. A failed owner callback prevents acknowledgment.
  for owner,_ in pairs(self.owners) do
    local count=native.GCETStaticOwnerCount(owner)
    assert(type(count)=="number" and count>=0)
    for i=0,count-1 do
      local id=native.GCETStaticOwnerAnchor(owner,i)
      if id and id>0 then
        self:_notify(owner,id,native.GCETStaticState(id),true)
      end
    end
  end
  assert(native.GCETStaticAckSnapshot(self.consumerId),
         "native snapshot ACK rejected")
end

function Hub:_step()
  if self.ownerCount==0 then return end
  local native=self.native
  if not native.GCETStaticReady() or native.GCETStaticCoordinatorState()~=2 then
    self:_fallback("NATIVE_UNAVAILABLE")
    return
  end
  if not self.subscribed then
    assert(native.GCETStaticSubscribe(self.consumerId,0),
           "native subscription rejected")
    self.subscribed=true
  end
  if native.GCETStaticNeedsSnapshot(self.consumerId) then
    self:_snapshot()
  end
  local maxEvents=self.maxEventsPerStep
  for _=1,maxEvents do
    local id=native.GCETStaticPoll(self.consumerId)
    if id==0 then return end
    assert(type(id)=="number" and id>0,"native stream requires resync")
    local owner=native.GCETStaticPolledOwner(self.consumerId)
    local state=native.GCETStaticPolledState(self.consumerId)
    self:_notify(owner,id,state,false)
  end
  -- A bounded number of events per host wake; backlog remains in native ring.
  -- On overrun, the next native Poll returns -1, never silently dropping data.
end

function Hub:step()
  local ok,err=pcall(self._step,self)
  if not ok then
    self:_stop()  -- new consumer must take full snapshot after any error
    self:_fallback("NATIVE_BRIDGE_ERROR: "..tostring(err))
    return false
  end
  return true
end

return Hub
