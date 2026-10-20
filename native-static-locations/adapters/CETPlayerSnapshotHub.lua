-- Optional, source-proven bridge to the independent native player snapshot.
-- Call :Step() from ONE existing 0-Engine/CET host update: this registers NO hooks.
-- It MUST NOT pause the original author source unless Status()==2 AND
-- source equivalence has been independently established.
local Hub={}
Hub.__index=Hub
local function has(flags,bit)return math.floor(flags/bit)%2==1 end
local function invoke(fn,...)
    if not fn then return true end
    return pcall(fn,...)
end
-- One-time capability negotiation used by the existing shared 0-Engine host.
-- This does NOT register a CET callback or rewrite an author's source.
local autoMethods={
    "GCETPlayerSubscribe","GCETPlayerUnsubscribe","GCETPlayerPoll",
    "GCETPlayerStatus","GCETPlayerEpoch",
    "GCETPlayerX","GCETPlayerY","GCETPlayerZ"
}
local function nativeMethod(game,name)
    local ok,fn=pcall(function()return game[name]end)
    if not ok or type(fn)~="function" then return nil end
    return fn
end
function Hub.TryAuto(game,consumer,onFallback)
    if (type(game)~="table" and type(game)~="userdata") then
        return nil,"NATIVE_UNAVAILABLE"
    end
    local versionFn=nativeMethod(game,"GCETPlayerApiVersion")
    local capsFn=nativeMethod(game,"GCETPlayerCapabilities")
    if not versionFn or not capsFn then return nil,"NATIVE_UNAVAILABLE" end
    local vok,v=pcall(versionFn)
    local cok,c=pcall(capsFn)
    if not vok or v~=1 then return nil,"API_MISMATCH" end
    if not cok or type(c)~="number" then return nil,"CAPABILITY_MISMATCH" end
    -- Require manifest independence (bit 16) and the four field/event bits.
    -- Arithmetic masking is compatible with the stock CET LuaJIT runtime.
    local function has(bit)return math.floor(c/bit)%2==1 end
    if not has(1) or not has(2) or not has(4) or not has(8) or not has(16) then
        return nil,"CAPABILITY_MISMATCH"
    end
    local native={}
    for i=1,#autoMethods do
        local name=autoMethods[i]
        local fn=nativeMethod(game,name)
        if not fn then return nil,"INCOMPLETE_NATIVE_ABI" end
        native[name]=fn
    end
    -- No native sampling begins until someone Watch()es and the shared host
    -- invokes Step() on its already existing update lane.
    return Hub.new(native,consumer,onFallback)
end
function Hub.new(native,consumer,onFallback)
    assert(type(native)=="table" and type(consumer)=="number" and consumer>0)
    return setmetatable({
        native=native,consumer=consumer,fallback=onFallback,
        active=false,closed=false,handles={},listenerCount=0,
    },Hub)
end
function Hub:_fallback(reason)
    -- The owner retains its original source work. This is only a signal to
    -- the source-matched adapter, never an implicit pause/resume command.
    invoke(self.fallback,reason)
    return false,reason
end
function Hub:Watch(key,fn)
    assert(not self.closed)
    assert(key=="position" or key=="availability")
    assert(type(fn)=="function")
    local entry={key=key,fn=fn,live=true}
    self.handles[#self.handles+1]=entry
    self.listenerCount=self.listenerCount+1
    local hub=self
    local handle={}
    function handle.Cancel()
        if not entry.live then return end
        entry.live=false
        hub.listenerCount=hub.listenerCount-1
        if hub.listenerCount==0 and hub.active then
            invoke(hub.native.GCETPlayerUnsubscribe,hub.consumer)
            hub.active=false
        end
    end
    handle.unsubscribe=handle.Cancel
    return handle
end
function Hub:Step()
    if self.closed or self.listenerCount==0 then return false,"IDLE" end
    local native=self.native
    if not self.active then
        local ok,accepted=invoke(native.GCETPlayerSubscribe,self.consumer,3)
        if not ok or accepted~=true then return self:_fallback("NATIVE_UNAVAILABLE") end
        self.active=true
    end
    local ok,flags=invoke(native.GCETPlayerPoll,self.consumer)
    if not ok or type(flags)~="number" or flags<0 then
        -- Retire the abandoned subscription before retrying registration.
        invoke(native.GCETPlayerUnsubscribe,self.consumer)
        self.active=false
        return self:_fallback("NATIVE_POLL_FAILED")
    end
    if flags==0 then return true end
    local statusOk,status=invoke(native.GCETPlayerStatus)
    local epochOk,epoch=invoke(native.GCETPlayerEpoch)
    if not statusOk or not epochOk or type(epoch)~="number" then
        return self:_fallback("NATIVE_SNAPSHOT_FAILED")
    end
    if status~=2 then
        -- UNKNOWN is never FAR, nor permission to suspend authored behavior.
        return self:_fallback("PLAYER_UNKNOWN")
    end
    local x,y,z
    local needsPosition=has(flags,1) or has(flags,4) or has(flags,8) or has(flags,16)
    if needsPosition then
        local xOk,yOk,zOk
        xOk,x=invoke(native.GCETPlayerX)
        yOk,y=invoke(native.GCETPlayerY)
        zOk,z=invoke(native.GCETPlayerZ)
        if not xOk or not yOk or not zOk or type(x)~="number" or
           type(y)~="number" or type(z)~="number" then
            return self:_fallback("PLAYER_POSITION_FAILED")
        end
    end
    for i=1,#self.handles do
        local listener=self.handles[i]
        if listener.live then
            local triggered=listener.key=="position" and needsPosition or
                listener.key=="availability" and
                  (has(flags,2) or has(flags,4) or has(flags,8) or has(flags,16))
            if triggered then
                local sent
                if listener.key=="position" then
                    sent=invoke(listener.fn,x,y,z,epoch)
                else
                    sent=invoke(listener.fn,true,epoch)
                end
                if not sent then return self:_fallback("SUBSCRIBER_ERROR") end
            end
        end
    end
    return true
end
function Hub:Shutdown()
    if self.closed then return end
    self.closed=true
    if self.active then invoke(self.native.GCETPlayerUnsubscribe,self.consumer) end
    self.active=false
    for i=1,#self.handles do self.handles[i].live=false end
    self.listenerCount=0
end
return Hub
