-- G-CET native location host v1. Source-proven owners only. Never polls the
-- player's position from Lua. One optional 0-Engine timer drains RED4ext events.
-- On UNKNOWN, native failure, resync or callback failure, restore author logic.
local Host={version=1}
Host.__index=Host

local function finite(n)
  return type(n)=="number" and n==n and n~=math.huge and n~=-math.huge
end

local function native(name,...)
  local api=Game and Game[name]
  if type(api)~="function" then error("missing native API "..name) end
  return api(...)
end

function Host.new(engine)
  assert(engine and type(engine.Register)=="function")
  return setmetatable({
    engine=engine, owners={}, consumer=1702403, nextId=1700000,
    job=nil, subscribed=false, snapshotRequired=true
  },Host)
end

function Host:_stop()
  if self.subscribed then pcall(native,"GCETStaticUnsubscribe",self.consumer) end
  self.subscribed=false
  self.snapshotRequired=true
end

function Host:_fallback(reason)
  local allOk=true
  for _,owner in pairs(self.owners) do
    if owner.mode~="ORIGINAL" then
      local ok,value=pcall(owner.fallback,reason)
      if ok and value~=false then
        owner.mode="ORIGINAL"
      else
        allOk=false
      end
    end
  end
  return allOk
end

function Host:_set(owner,near)
  local mode=near and "NEAR" or "FAR"
  if owner.mode==mode then return end
  -- Mark prior to invoking owner callbacks so partial failures fail OPEN.
  owner.mode=mode
  local cb=near and owner.onNear or owner.onFar
  local ok,value=pcall(cb)
  if not ok or value==false then error("owner transition failed "..owner.id) end
end

function Host:_schedule()
  if self.job then return true end
  local ok,job=pcall(function()
    local client=self.engine.Register("G-CET-NativeLocationHost")
    assert(client and type(client.Schedule)=="table" and
      type(client.Schedule.Every)=="function")
    return client.Schedule.Every(0.10,{
      id="gcet.native.location.shared",active=true,clock="game",
      pause="never",catchUp=false
    },function() self:step() end)
  end)
  if not ok or type(job)~="table" or type(job.Cancel)~="function" then
    return false
  end
  self.job=job
  return true
end

function Host:attach(id,sites,onNear,onFar,onFallback)
  if type(id)~="number" or id<=0 or self.owners[id] or
     type(sites)~="table" or #sites==0 or
     type(onNear)~="function" or type(onFar)~="function" or
     type(onFallback)~="function" then return false end
  -- An absent native plugin must add zero periodic Lua work.
  local available,nativeReady=pcall(native,"GCETStaticReady")
  if not available or nativeReady~=true then return false end
  if not self:_schedule() then return false end
  local added={}
  local ok=pcall(function()
    assert(native("GCETStaticReady")==true)
    for _,site in ipairs(sites) do
      assert(finite(site.x) and finite(site.y) and finite(site.z) and
        finite(site.radius) and site.radius>0)
      assert(site.metric=="xyz" or site.metric=="xy")
      self.nextId=self.nextId+1
      local key=self.nextId
      assert(native("GCETStaticAddAnchor",key,site.x,site.y,site.z,
        site.radius,site.metric=="xy",id)==true)
      added[#added+1]=key
      assert(native("GCETStaticArm",key,true)==true)
    end
  end)
  if not ok then
    for _,key in ipairs(added) do pcall(native,"GCETStaticRemoveAnchor",key) end
    return false
  end
  self.owners[id]={id=id,ids=added,onNear=onNear,onFar=onFar,
    fallback=onFallback,mode="ORIGINAL",known={},nearCount=0}
  self:_stop() -- membership changed: next step MUST resnapshot all owners
  return true
end

-- Bind an owner whose immutable positions are already in G-CET.locations.
-- No Lua->RTTI anchor writes, no Lua site table and no duplicate shared timer.
-- A source mismatch or missing/invalid native data leaves author fallback alive.
function Host:attachCompiled(id,expected,onNear,onFar,onFallback)
  if type(id)~="number" or id<=0 or self.owners[id] or
     type(expected)~="number" or expected<=0 or expected>100000 or
     type(onNear)~="function" or type(onFar)~="function" or
     type(onFallback)~="function" then return false end
  local ok,ready=pcall(native,"GCETStaticReady")
  if not ok or ready~=true then return false end
  local countOk,count=pcall(native,"GCETStaticOwnerCount",id)
  if not countOk or count~=expected then return false end
  if not self:_schedule() then return false end
  local ids,armed={},{}
  local success=pcall(function()
    local seen={}
    for n=0,count-1 do
      local key=native("GCETStaticOwnerAnchor",id,n)
      assert(type(key)=="number" and key>0 and not seen[key])
      seen[key]=true;ids[#ids+1]=key
    end
    for _,key in ipairs(ids) do
      assert(native("GCETStaticArm",key,true)==true)
      armed[#armed+1]=key
    end
  end)
  if not success then
    for _,key in ipairs(armed) do pcall(native,"GCETStaticArm",key,false) end
    return false
  end
  self.owners[id]={id=id,ids=ids,onNear=onNear,onFar=onFar,
    fallback=onFallback,mode="ORIGINAL",known={},nearCount=0}
  self:_stop() -- joining an owner demands a complete authoritative snapshot
  return true
end

function Host:_snapshot()
  local rows={}
  for _,owner in pairs(self.owners) do
    local n=native("GCETStaticOwnerCount",owner.id)
    assert(type(n)=="number" and n==#owner.ids)
    local allowed,states,count={}, {},0
    for _,id in ipairs(owner.ids) do allowed[id]=true end
    for i=0,n-1 do
      local id=native("GCETStaticOwnerAnchor",owner.id,i)
      assert(allowed[id] and states[id]==nil)
      local state=native("GCETStaticState",id)
      assert(state==0 or state==1)
      states[id]=state
      if state==1 then count=count+1 end
    end
    rows[#rows+1]={owner=owner,states=states,count=count}
  end
  for _,row in ipairs(rows) do
    row.owner.known=row.states
    row.owner.nearCount=row.count
    self:_set(row.owner,row.count>0)
  end
  assert(native("GCETStaticAckSnapshot",self.consumer)==true)
  self.snapshotRequired=false
end

function Host:_tick()
  if next(self.owners)==nil then return end
  if native("GCETStaticReady")~=true or
      native("GCETStaticCoordinatorState")~=2 then
    self:_stop()
    self:_fallback("UNKNOWN_NATIVE_POSITION")
    return
  end
  if not self.subscribed then
    assert(native("GCETStaticSubscribe",self.consumer,0)==true)
    self.subscribed=true
    self.snapshotRequired=true
  end
  if self.snapshotRequired or native("GCETStaticNeedsSnapshot",self.consumer) then
    self:_snapshot()
  end
  for _=1,512 do
    local id=native("GCETStaticPoll",self.consumer)
    if id==0 then return end
    assert(type(id)=="number" and id>0,"NATIVE_STREAM_RESYNC")
    local ownerId=native("GCETStaticPolledOwner",self.consumer)
    local state=native("GCETStaticPolledState",self.consumer)
    assert(state==0 or state==1,"UNKNOWN_NATIVE_EVENT")
    local owner=self.owners[ownerId]
    if owner then
      local prev=owner.known[id]
      assert(prev~=nil,"UNRECOGNIZED_NATIVE_EPOCH")
      if prev~=state then
        owner.known[id]=state
        owner.nearCount=owner.nearCount+(state==1 and 1 or -1)
        assert(owner.nearCount>=0 and owner.nearCount<=#owner.ids)
        self:_set(owner,owner.nearCount>0)
      end
    end
  end
end

function Host:step()
  local ok=pcall(self._tick,self)
  if not ok then self:_stop();self:_fallback("NATIVE_BRIDGE_FAILURE") end
end

function Host:shutdown()
  self:_stop()
  self:_fallback("NATIVE_HOST_SHUTDOWN")
  for _,owner in pairs(self.owners) do
    for _,id in ipairs(owner.ids) do pcall(native,"GCETStaticRemoveAnchor",id) end
  end
  self.owners={}
  if self.job then pcall(self.job.Cancel);self.job=nil end
end

return Host
