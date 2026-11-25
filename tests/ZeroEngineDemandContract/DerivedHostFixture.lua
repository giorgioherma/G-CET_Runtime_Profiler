local DerivedState = require("modules/DerivedState")
local Engine = {}
local State = {derived = {}}
local proxyCache = {}
local function DeepReadOnly(tbl, path)
    if proxyCache[tbl] then return proxyCache[tbl] end
    local proxy = setmetatable({}, {
        __index = function(_, k)
            local v = tbl[k]
            if type(v) == "table" then
                return DeepReadOnly(v, (path or "state") .. "." .. tostring(k))
            end
            return v
        end,
        __newindex = function() error("read only") end,
        __len = function() return #tbl end,
        __pairs = function()
            return next, tbl, nil
        end
    })
    proxyCache[tbl] = proxy
    return proxy
end
local stateProxy = nil
local function UpdateFrame(delta)
    local player = {}
    local stateOk = pcall(DerivedState.Update, player)
    if not stateOk then return end
    State.derived = DerivedState.Get()
end
function Engine.GetState()
    if not stateProxy then
        stateProxy = DeepReadOnly(State, "state")
    end
    return stateProxy
end
Engine.Tick = UpdateFrame
Engine.Reset = DerivedState.Reset
return Engine
