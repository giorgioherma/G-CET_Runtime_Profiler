-- Experimental only: native ActionRouter interest mirror, not a CET hook.
-- Requires an exhaustive source snapshot; missing DLL/snapshot => allow all.
local M = { ABI = 1 }
local ffiOk, ffi = pcall(require, "ffi")
local dll, gate = nil, nil

if ffiOk then
    pcall(ffi.cdef, [[
        typedef struct gcet_action_gate gcet_action_gate;
        typedef struct gcet_action_gate_stats {
            uint32_t struct_size; uint32_t abi_version;
            uint64_t revision, lookups, admitted, rejected, fail_open;
            uint32_t exact_actions, armed, wildcard, reserved;
        } gcet_action_gate_stats;
        uint32_t gcet_gate_abi_version(void);
        gcet_action_gate* gcet_gate_create(void);
        void gcet_gate_destroy(gcet_action_gate*);
        int gcet_gate_begin(gcet_action_gate*);
        int gcet_gate_add(gcet_action_gate*, const char*);
        int gcet_gate_seal(gcet_action_gate*, int);
        void gcet_gate_disarm(gcet_action_gate*);
        int gcet_gate_should_deliver(gcet_action_gate*, const char*);
        int gcet_gate_get_stats(gcet_action_gate*, gcet_action_gate_stats*);
    ]])
end

function M.Load(explicitDllPath)
    if not ffiOk or type(explicitDllPath) ~= "string" or explicitDllPath == "" then return false end
    if gate ~= nil then return true end
    local ok, native = pcall(ffi.load, explicitDllPath)
    if not ok then return false end
    ok = pcall(function()
        if tonumber(native.gcet_gate_abi_version()) ~= M.ABI then error("ABI mismatch") end
        local candidate = native.gcet_gate_create()
        if candidate == nil or candidate == ffi.NULL then error("native gate unavailable") end
        gate = ffi.gc(candidate, native.gcet_gate_destroy)
        dll = native
    end)
    if not ok then gate, dll = nil, nil end
    return ok
end

function M.Disarm()
    if dll and gate then pcall(dll.gcet_gate_disarm, gate) end
end

-- The caller must include ALL active subscribers for this router, not merely
-- a sample or a single mod. A no/wrong snapshot explicitly fails open.
function M.Refresh(router)
    M.Disarm()
    if not dll or not gate or type(router) ~= "table" or
       type(router.GetInterestSnapshot) ~= "function" then return false end
    local ok, snapshot = pcall(router.GetInterestSnapshot)
    if not ok or type(snapshot) ~= "table" or snapshot.complete ~= true or
       type(snapshot.actions) ~= "table" or type(snapshot.wildcard) ~= "boolean" then
        return false
    end
    local good = pcall(function()
        if dll.gcet_gate_begin(gate) ~= 1 then error("rebuild unavailable") end
        if snapshot.wildcard and dll.gcet_gate_add(gate, "*") ~= 1 then error("wildcard") end
        for _, action in ipairs(snapshot.actions) do
            if type(action) ~= "string" or action == "" or
               dll.gcet_gate_add(gate, action) ~= 1 then error("invalid action") end
        end
        if dll.gcet_gate_seal(gate, 1) ~= 1 then error("cannot seal") end
    end)
    if not good then M.Disarm() end
    return good
end

-- Decision for this router only; safe even when FFI/native is unavailable.
-- Do not call on every event from stock Lua ActionRouter: that still pays the
-- native -> Lua crossing and typically duplicates GetName work.
function M.ShouldDeliver(actionName)
    if not dll or not gate or type(actionName) ~= "string" then return true end
    local ok, result = pcall(dll.gcet_gate_should_deliver, gate, actionName)
    return not ok or result ~= 0
end

function M.GetValues()
    local result = { available = false, purpose = "native-interest-only", preDispatchHook = false }
    if not dll or not gate then return result end
    local s = ffi.new("gcet_action_gate_stats")
    s.struct_size = ffi.sizeof(s)
    local ok, read = pcall(dll.gcet_gate_get_stats, gate, s)
    if not ok or read ~= 1 or tonumber(s.abi_version) ~= M.ABI then return result end
    result.available = true
    result.armed = s.armed ~= 0
    result.wildcard = s.wildcard ~= 0
    result.activeExactActionNames = tonumber(s.exact_actions)
    result.snapshotRevision = tonumber(s.revision)
    result.decisionCalls = tonumber(s.lookups)
    result.allowed = tonumber(s.admitted)
    result.suppressed = tonumber(s.rejected)
    result.failOpen = tonumber(s.fail_open)
    -- NOT GC bytes, native marshalling saved or game-frame savings.
    return result
end

return M
