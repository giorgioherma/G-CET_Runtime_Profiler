-- Development-only CET adapter. NOT a standalone CET mod, no onUpdate registration.
-- Run step() from the existing 0-Engine host. The Resolver must supply
-- source-proven owner callbacks and unique positive consumer/owner IDs.
local Hub={}
Hub.__index=Hub

function Hub.new(native, consumerId)
  assert(type(native)=="table" and type(consumerId)=="number" and consumerId>0)
  return setmetatable({
    native=native, consumerId=consumerId, owners={}, ownerCount=0,
    subscribed=false, fallbackActive=false, maxEventsPerStep=512
  },Hub)
end

function Hub:addOwner(ownerId, onNear, onFar, onFallback, onSnapshot)
  assert(type(ownerId)=="number" and ownerId>0)
  assert(type(onNear)=="function" and type(onFallback)=="function")
  assert(onSnapshot==nil or type(onSnapshot)=="function")
  assert(not self.owners[ownerId],"duplicate native anchor owner")
  -- A newly attached owner cannot wait for another owner to generate an
  -- event. Force a complete all-owner snapshot on the next shared host step.
  self:_stop()
  self.owners[ownerId]={near=onNear,far=onFar,fallback=onFallback,snapshot=onSnapshot}
  self.ownerCount=self.ownerCount+1
end

function Hub:removeOwner(ownerId)
  if not self.owners[ownerId] then return false end
  self:_stop()
  self.owners[ownerId]=nil
  self.ownerCount=self.ownerCount-1
  return true
end

function Hub:_fallback(reason)
  -- Fallback re-arms the *original* author scanner. Trigger once per
  -- outage, not every 0-Engine frame. Retry if an owner handler failed.
  if self.fallbackActive then return true end
  local allOk=true
  for _,handler in pairs(self.owners) do
    local ok=pcall(handler.fallback,reason)
    if not ok then allOk=false end
  end
  self.fallbackActive=allOk
  return allOk
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
    error("UNKNOWN_ANCHOR_STATE")
  end
end

function Hub:_snapshot()
  local native=self.native
  -- Preflight the entire snapshot before driving any native-mode author
  -- callbacks. Unknown states must not leave a partially switched stack.
  -- Same-thread state-read -> ACK ordering remains required.
  local states={}
  for owner,_ in pairs(self.owners) do
    local count=native.GCETStaticOwnerCount(owner)
    assert(type(count)=="number" and count>=0)
    for i=0,count-1 do
      local id=native.GCETStaticOwnerAnchor(owner,i)
      assert(type(id)=="number" and id>0,"invalid native anchor ID")
      local state=native.GCETStaticState(id)
      assert(state==0 or state==1,"UNKNOWN_ANCHOR_STATE")
      states[#states+1]={owner=owner,id=id,state=state}
    end
  end
  -- Owners with multiple sites need an atomic aggregate initial state.
  -- A FAR for one anchor must not suppress NEAR at another.
  local groups={}
  for _,row in ipairs(states) do
    local group=groups[row.owner]
    if not group then group={};groups[row.owner]=group end
    group[#group+1]={id=row.id,state=row.state}
  end
  for owner,handler in pairs(self.owners) do
    if handler.snapshot then
      handler.snapshot(groups[owner] or {})
    else
      for _,row in ipairs(groups[owner] or {}) do
        self:_notify(owner,row.id,row.state,true)
      end
    end
  end
  assert(native.GCETStaticAckSnapshot(self.consumerId),
         "native snapshot ACK rejected")
  self.fallbackActive=false
end

function Hub:_step()
  if self.ownerCount==0 then return end
  local native=self.native
  if not native.GCETStaticReady() or native.GCETStaticCoordinatorState()~=2 then
    -- Unsubscribe on loss: recovery MUST start with a fresh snapshot even if
    -- the ring had not yet signalled an epoch invalidation.
    self:_stop()
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
