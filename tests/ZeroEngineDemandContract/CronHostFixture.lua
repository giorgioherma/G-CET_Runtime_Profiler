local Cron = require('external/Cron')
local currentFrame = 0
local function UpdateFrame(delta)
    currentFrame = currentFrame + 1
end
registerForEvent("onUpdate", function(delta)
    pcall(Cron.Update, delta)
    pcall(UpdateFrame, delta)
end)
return Cron
