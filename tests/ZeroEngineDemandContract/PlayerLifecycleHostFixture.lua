local Lifecycle = CurrentLifecycle
local BlackboardCache = CurrentBlackboard
local function UpdateFrame(delta)
    Lifecycle.Update()
    -- Blackboard update remains in its original author phase.
    local pcOk, pc = pcall(Game.GetPlayer)
    if pcOk and pc then
        pcall(BlackboardCache.Update, pc)
    end
end
return UpdateFrame
