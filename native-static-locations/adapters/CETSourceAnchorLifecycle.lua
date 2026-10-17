-- Source-owned, one-shot native anchor lifecycle. Not a runtime discovery scan.
-- A Resolver-proven source epoch supplies every possible location for one
-- separable function. UNKNOWN/native failure restores author-side discovery.
local Life={}
Life.__index=Life
-- Native diagnostic probes reserve IDs beginning at 2,100,000,000.
local MAX_ID=2099999999
local function validNum(n)
 return type(n)=="number" and n==n and n~=math.huge and n~=-math.huge
end
local function validId(id)
 return validNum(id) and id%1==0 and id>0 and id<=MAX_ID
end
local function nativeCall(native,name,...)
 local fn=native[name]
 if type(fn)~="function" then return false end
 local ok,value=pcall(fn,...)
 return ok and value==true
end
local function signature(sites)
 if type(sites)~="table" or #sites==0 then return nil end
 local seen,parts={},{}
 for _,s in ipairs(sites) do
  if type(s)~="table" or not validId(s.id) or seen[s.id]
      or not validNum(s.x) or not validNum(s.y) or not validNum(s.z)
      or not validNum(s.radius) or s.radius<=0
      or (s.metric~="xy" and s.metric~="xyz") then
   return nil
  end
  seen[s.id]=true
  parts[#parts+1]=table.concat({
   tostring(s.id),tostring(s.x),tostring(s.y),
   tostring(s.z),tostring(s.radius),s.metric
  },":")
 end
 table.sort(parts)
 return table.concat(parts,"|")
end
function Life.new(native,ownerId,onChange)
 assert(type(native)=="table" and validId(ownerId))
 assert(onChange==nil or type(onChange)=="function")
 return setmetatable({
  native=native,ownerId=ownerId,onChange=onChange,
  seen={},active={},epoch=nil,fingerprint=nil,signature=nil,
  relevant=false,valid=true,
 },Life)
end
function Life:_notify()
 if self.onChange then pcall(self.onChange) end
end
function Life:_remove(sites)
 local all=true
 for _,s in ipairs(sites) do
  if not nativeCall(self.native,"GCETStaticArm",s.id,false) then all=false end
  if not nativeCall(self.native,"GCETStaticRemoveAnchor",s.id) then all=false end
 end
 return all
end
function Life:register(epoch,fingerprint,sites,relevant)
 local sig=signature(sites)
 if type(epoch)~="string" or epoch=="" or
   type(fingerprint)~="string" or #fingerprint<8 or not sig
   or type(relevant)~="boolean" then return false,"INVALID_SOURCE_EPOCH" end
 if self.epoch==epoch then
  if self.fingerprint~=fingerprint or self.signature~=sig then
   return false,"SAME_EPOCH_SOURCE_DRIFT"
  end
  return self:setRelevant(relevant)
 end
 for _,s in ipairs(sites) do
  if self.seen[s.id] then return false,"TOMBSTONED_ID" end
 end
 local appended={}
 for _,s in ipairs(sites) do
  -- An attempted ID is never reused, even if native partially failed.
  self.seen[s.id]=true
  local ok=nativeCall(self.native,"GCETStaticAddAnchor",
   s.id,s.x,s.y,s.z,s.radius,s.metric=="xy",self.ownerId)
  if not ok then
   self:_remove(appended)
   self:_notify()
   return false,"NATIVE_ADD_FAILED"
  end
  appended[#appended+1]=s
  if relevant and not nativeCall(self.native,"GCETStaticArm",s.id,true) then
   self:_remove(appended)
   self:_notify()
   return false,"NATIVE_ARM_FAILED"
  end
 end
 -- New set is registered before old set is retired. This is one game-thread
 -- transaction; missing/UNKNOWN snapshots must fail open until reevaluated.
 local prior=self.active
 self.active=appended
 self.epoch=epoch
 self.fingerprint=fingerprint
 self.signature=sig
 self.relevant=relevant
 if not self:_remove(prior) then
  self:_remove(appended)
  self.active={}
  self.valid=false
  self:_notify()
  return false,"PREVIOUS_EPOCH_REMOVAL_FAILED"
 end
 self.valid=true
 self:_notify()
 return true
end
function Life:setRelevant(relevant)
 if type(relevant)~="boolean" or not self.valid or not self.epoch then
  return false,"NO_VALID_SOURCE_EPOCH"
 end
 if self.relevant==relevant then return true end
 for _,s in ipairs(self.active) do
  if not nativeCall(self.native,"GCETStaticArm",s.id,relevant) then
   self.valid=false
   for _,other in ipairs(self.active) do
    nativeCall(self.native,"GCETStaticArm",other.id,false)
   end
   self:_notify()
   return false,"NATIVE_STAGE_ARM_FAILED"
  end
 end
 self.relevant=relevant
 self:_notify()
 return true
end
function Life:retire()
 local wasValid=self:_remove(self.active)
 self.active={}
 self.epoch=nil
 self.fingerprint=nil
 self.signature=nil
 self.relevant=false
 self.valid=wasValid
 self:_notify()
 return wasValid
end
return Life
