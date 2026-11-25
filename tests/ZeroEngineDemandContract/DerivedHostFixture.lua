local DerivedState = require("modules/DerivedState")
local Lifecycle = require("modules/Lifecycle")
local Events, Logger = {}, {}
local Engine = {}
local State = {derived = {}}
Lifecycle.Init(Events, State, Logger)
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
local currentFrame = 0
local valid = true
local function UpdateFrame(delta)
    currentFrame = currentFrame + 1
    if not valid then return end
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
Engine.SetLoading = DerivedState.SetLoading
function Engine.SetValid(value) valid = value end
return Engine
