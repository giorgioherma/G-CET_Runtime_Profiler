-- EXPERIMENTAL R4 compatibility bridge. Load only where the source-matched
-- GCETStaticAnchors module and original author wake fallbacks are present.
-- Keeps original author callbacks and only replaces shared Lua geometry work.
-- No registerForEvent/onUpdate, C++ owns the player position.
local M={}
function M.Wrap(old, native, consumerId)
  assert(type(old)=="table" and type(old.Register)=="function"
    and type(old.Update)=="function" and type(native)=="table")
  consumerId=consumerId or 1702401
  local ownerIds, ownerNext={},1000
  local anchorNext=10000
  local records={}
  local byId={}
  local nativeMode=false
  local subscribed=false
  local counters={events=0,snapshots=0,failovers=0}

  local function method(name,...)
    local fn=native[name]
    if type(fn)~="function" then error("native RTTI missing: "..name) end
    return fn(...)
  end
  local function unSub()
    if subscribed then pcall(method,"GCETStaticUnsubscribe",consumerId) end
    subscribed=false
    nativeMode=false
  end
  local function fallbackCall(record)
    if record and type(record.hooks.onUnavailable)=="function" then
      pcall(record.hooks.onUnavailable)
    end
  end
  local function dropRecord(rec)
    if not rec then return end
    for _,entry in ipairs(rec.entries) do
      if entry.native then
        pcall(method,"GCETStaticRemoveAnchor",entry.num)
        byId[entry.num]=nil
      end
    end
    rec.native=false
    rec.near=nil
    rec.nearCount=0
  end
  local function teardownNative()
    unSub()
    for _,rec in pairs(records) do dropRecord(rec) end
  end
  local function sourceKey(owner,source) return owner.."\31"..source end
  local function notify(rec)
    local newNear=rec.nearCount>0
    if rec.near==newNear then return end
    rec.near=newNear
    local fn=newNear and rec.hooks.onNear or rec.hooks.onFar
    if type(fn)=="function" and not pcall(fn) then
      rec.near=nil
      fallbackCall(rec)
      error("source wake callback failed")
    end
  end
  local function apply(rec,entry,newState)
    if newState~=0 and newState~=1 then
      rec.near=nil
      fallbackCall(rec)
      error("native UNKNOWN anchor: original wake required")
    end
    local near=(newState==1)
    if entry.near~=near then
      rec.nearCount=rec.nearCount+(near and 1 or -1)
      entry.near=near
    end
  end
  local function hasOwners()
    local n=0
    for _,rec in pairs(records) do
      n=n+1
      if not rec.native then return false end
    end
    return n>0
  end
  local function startStream()
    if not subscribed then
      assert(method("GCETStaticSubscribe",consumerId,0),"native subscribe rejected")
      subscribed=true
    end
    if method("GCETStaticNeedsSnapshot",consumerId) then
      for _,rec in pairs(records) do
        rec.near=nil
        rec.nearCount=0
        for _,entry in ipairs(rec.entries) do
          local state=method("GCETStaticState",entry.num)
          entry.near=false
          apply(rec,entry,state)
        end
      end
      for _,rec in pairs(records) do notify(rec) end
      assert(method("GCETStaticAckSnapshot",consumerId),"snapshot ACK rejected")
      counters.snapshots=counters.snapshots+1
    end
    nativeMode=true
  end
  local function drain()
    for _=1,512 do
      local id=method("GCETStaticPoll",consumerId)
      if id==0 then return end
      assert(type(id)=="number" and id>0, "native event overrun")
      local entry=byId[id]
      if entry then
        local state=method("GCETStaticPolledState",consumerId)
        local rec=entry.record
        apply(rec,entry,state)
        notify(rec)
        counters.events=counters.events+1
      end
    end
  end
  local function registerNative(rec)
    -- All allocation and indexing happen at registration, not at frame tick.
    local nOwner=ownerIds[rec.owner]
    if not nOwner then
      ownerNext=ownerNext+1
      nOwner=ownerNext
      ownerIds[rec.owner]=nOwner
    end
    local ok=pcall(function()
      assert(method("GCETStaticReady"),"native not ready")
      for _,entry in ipairs(rec.entries) do
        anchorNext=anchorNext+1
        entry.num=anchorNext
        assert(method("GCETStaticAddAnchor",entry.num,entry.x,entry.y,entry.z,
          entry.radius,entry.metric=="xy",nOwner),"native registration rejected")
        entry.native=true
        byId[entry.num]=entry
        assert(method("GCETStaticArm",entry.num,true),"arm rejected")
      end
    end)
    if not ok then dropRecord(rec);return false end
    rec.native=true
    return true
  end
  local wrapper={}
  function wrapper.Register(owner,source,fingerprint,anchors,hooks)
    local legacyHandle=old.Register(owner,source,fingerprint,anchors,hooks)
    if not legacyHandle then return nil end
    local key=sourceKey(owner,source)
    local prev=records[key]
    if prev then dropRecord(prev) end
    unSub() -- a changed source needs a new all-owner snapshot
    local entries={}
    for i,e in ipairs(anchors) do
      entries[i]={id=e.id,x=e.x,y=e.y,z=e.z,
        radius=e.radius,metric=e.metric or "xyz",near=false}
    end
    local rec={owner=owner,source=source,hooks=hooks or {},
      entries=entries,near=nil,nearCount=0,legacy=legacyHandle,native=false}
    for _,entry in ipairs(entries) do entry.record=rec end
    records[key]=rec
    if not registerNative(rec) then teardownNative() end
    local handle={}
    function handle.IsNear()
      if nativeMode and rec.native then return rec.near end
      return legacyHandle.IsNear()
    end
    function handle.GetState()
      if nativeMode and rec.native then
        if rec.near==nil then return "UNAVAILABLE" end
        return rec.near and "NEAR_ARMED" or "FAR_DORMANT"
      end
      return legacyHandle.GetState()
    end
    function handle.Unregister() wrapper.Invalidate(owner,source) end
    return handle
  end
  function wrapper.Invalidate(owner,source)
    local key=sourceKey(owner,source)
    local rec=records[key]
    if rec then dropRecord(rec);records[key]=nil end
    unSub()
    return old.Invalidate(owner,source)
  end
  function wrapper.ResetSession()
    unSub()
    for _,rec in pairs(records) do rec.near=nil;rec.nearCount=0 end
    return old.ResetSession()
  end
  function wrapper.Update(position,frame)
    if not hasOwners() then return old.Update(position,frame) end
    local ok,healthy=pcall(function()
      return method("GCETStaticReady")
        and method("GCETStaticCoordinatorState")==2
    end)
    if not ok or not healthy then
      if nativeMode then
        unSub()
        old.ResetSession()
      end
      counters.failovers=counters.failovers+1
      return old.Update(position,frame)
    end
    local success=pcall(function()
      startStream()
      drain()
    end)
    if not success then
      unSub()
      old.ResetSession()
      counters.failovers=counters.failovers+1
      return old.Update(position,frame)
    end
  end
  function wrapper.GetAnchor(...) return old.GetAnchor(...) end
  function wrapper.QueryNearby(owner,px,py,pz)
    if not nativeMode then return old.QueryNearby(owner,px,py,pz) end
    local function finite(v)
      return type(v)=="number" and v==v and v~=math.huge and v~=-math.huge
    end
    if not finite(px) or not finite(py) or not finite(pz) then return nil end
    local result={}
    -- On-demand API only; no allocation in the frame worker.
    for _,rec in pairs(records) do
      if rec.owner==owner then
        for _,entry in ipairs(rec.entries) do
          local dx,dy=px-entry.x,py-entry.y
          local dz=entry.metric=="xy" and 0 or (pz-entry.z)
          if dx*dx+dy*dy+dz*dz<=entry.radius*entry.radius then
            result[#result+1]={id=entry.id,x=entry.x,y=entry.y,
              z=entry.z,radius=entry.radius}
          end
        end
      end
    end
    return result
  end
  function wrapper.GetInfo()
    local info=old.GetInfo()
    info.nativeBacked=nativeMode
    info.nativeEvents=counters.events
    info.nativeSnapshots=counters.snapshots
    info.nativeFallbacks=counters.failovers
    return info
  end
  -- All other safe stable interfaces stay those of the legacy module.
  return setmetatable(wrapper,{__index=old})
end
return M
