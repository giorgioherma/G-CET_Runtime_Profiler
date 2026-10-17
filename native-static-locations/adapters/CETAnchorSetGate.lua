-- Multi-location owner: any NEAR site wakes the source-proven near job.
-- One owner can have many anchors; changes at one must not silence another.
-- No coordinate scans, per-mod onUpdate, or guessed source admission.
local Set={}
Set.__index=Set

function Set.new(gate)
  assert(type(gate)=="table" and type(gate.onNear)=="function"
      and type(gate.onFar)=="function" and type(gate.onFallback)=="function")
  return setmetatable({gate=gate,sites={},count=0,near=0,valid=false},Set)
end

function Set:_unknown(reason)
  self.valid=false
  self.sites={}
  self.count=0
  self.near=0
  self.fallbackOk=(self.gate:onFallback(reason)==true)
  -- Propagate failure to the shared consumer: resubscribe + full snapshot.
  return false
end

function Set:snapshot(rows)
  if type(rows)~="table" or #rows==0 then
    return self:_unknown("EMPTY_OWNER_SNAPSHOT")
  end
  local sites,near,count={},0,0
  for _,row in ipairs(rows) do
    if type(row.id)~="number" or row.id<=0
        or (row.state~=0 and row.state~=1)
        or sites[row.id]~=nil then
      return self:_unknown("INVALID_OWNER_SNAPSHOT")
    end
    sites[row.id]=row.state
    count=count+1
    if row.state==1 then near=near+1 end
  end
  -- Apply only once after the entire set has been validated.
  self.sites=sites
  self.count=count
  self.near=near
  self.valid=true
  local ok=(near>0 and self.gate:onNear(0,true))
       or (near==0 and self.gate:onFar(0,true))
  if not ok then return self:_unknown("GATE_REJECTED_SNAPSHOT") end
  return true
end

function Set:update(id,state)
  if not self.valid or (state~=0 and state~=1) then
    return self:_unknown("UNKNOWN_OR_UNINITIALIZED_ANCHOR")
  end
  local previous=self.sites[id]
  if previous==nil then
    -- Source map changed. Must take a new owner snapshot, not invent membership.
    return self:_unknown("UNKNOWN_ANCHOR_ID")
  end
  if previous==state then return true end
  self.sites[id]=state
  if previous==1 then self.near=self.near-1 end
  if state==1 then self.near=self.near+1 end
  local ok=(self.near>0 and self.gate:onNear(id,false))
       or (self.near==0 and self.gate:onFar(id,false))
  if not ok then return self:_unknown("GATE_REJECTED_TRANSITION") end
  return true
end

function Set:fallback(reason)
  self:_unknown(reason or "NATIVE_UNAVAILABLE")
  return self.fallbackOk
end

function Set:bind(hub,ownerId)
  assert(type(hub)=="table" and type(hub.addOwner)=="function")
  hub:addOwner(ownerId,
    function(id) assert(self:update(id,1)) end,
    function(id) assert(self:update(id,0)) end,
    function(reason) return self:fallback(reason) end,
    function(rows) assert(self:snapshot(rows)) end)
end

return Set
