local Lifecycle = {}
local lastPlayerHash = nil
local playerValid = false
local failFrames = 0
local failThreshold = 30
local warmupFrames = 0
local warmupThreshold = 18
local warmupPlayer = nil
local warmupHash = nil
local Events = nil
local State = nil
function Lifecycle.Init(events, state, logger)
    Events = events
    State = state
end
local function GetPlayerHash(player)
    if not player then return nil end
    local ok, id = pcall(player.GetEntityID, player)
    if not ok or not id then return nil end
    return id.hash
end
function Lifecycle.Update()
    local ok, player = pcall(Game.GetPlayer)
    if not ok then player = nil end
    if player then
        local ok2, id = pcall(player.GetEntityID, player)
        if not ok2 or not id then
            player = nil
        end
    end
    local hash = GetPlayerHash(player)
    if not hash then
        if State.inMenu then
            failFrames = 0
            return
        end
        if warmupFrames > 0 then
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
        end
        failFrames = failFrames + 1
        if playerValid and failFrames >= failThreshold then
            playerValid = false
            lastPlayerHash = nil
            failFrames = 0
            State.player:set(nil)
            Events.PlayerInvalidated:trigger()
        end
        return
    end
    failFrames = 0  -- valid frame, reset counter
    if not playerValid then
        if warmupFrames == 0 then
            warmupPlayer = player
            warmupHash = hash
        end
        if hash ~= warmupHash then
            warmupFrames = 0
            warmupPlayer = player
            warmupHash = hash
        end
        warmupFrames = warmupFrames + 1
        if warmupFrames >= warmupThreshold then
            playerValid = true
            lastPlayerHash = warmupHash
            State.player:set(warmupPlayer)
            warmupFrames = 0
            warmupPlayer = nil
            warmupHash = nil
            Events.PlayerReady:trigger(player)
        end
        return
    end
    if hash ~= lastPlayerHash then
        lastPlayerHash = hash
        State.player:set(player)
        Events.PlayerRecreated:trigger(player)
    end
end
function Lifecycle.IsPlayerValid()
    return playerValid
end
function Lifecycle.ForceInvalidate()
    warmupFrames = 0
    warmupPlayer = nil
    warmupHash = nil
    if not playerValid then return end
    playerValid = false
    lastPlayerHash = nil
    failFrames = 0
    State.player:set(nil)
    Events.PlayerInvalidated:trigger()
end
return Lifecycle
