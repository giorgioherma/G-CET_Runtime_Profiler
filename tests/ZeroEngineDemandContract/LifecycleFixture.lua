local Lifecycle = {}
local State = nil
function Lifecycle.Init(events, state, logger)
    State = state
end
function Lifecycle.IsPlaying()
    return not State.inMenu
end
return Lifecycle
