-- Existing canonical host integration shape, with counted context providers.
local Engine = {}
local currentFrame = 0
local isPlaying = true
local State = {time=0, inMenu=false}
function State.player() count("player"); return "player" end
function Engine.GetState() count("state"); return State end
function Engine.Tick(delta)
    currentFrame = currentFrame + 1
    State.time = State.time + delta
    if type(Engine.__gcetScheduleDriver) == "function" then
        local __gcetPlayer = State.player()
        Engine.__gcetScheduleDriver({
            frame = currentFrame, dt = delta, realDt = delta,
            now = State.time, generation = 0, playing = isPlaying,
            inMenu = State.inMenu, player = __gcetPlayer,
            state = Engine.GetState()
        })
    end
end
function Engine.SetPlaying(value) isPlaying = value end
do
    local __host = Engine
    local __scheduler = require("modules/G-CET/Scheduler")
    local __frame = 0
    local function activate()
            local __lastRealClock = os.clock()
            __host.__gcetScheduleDriver = function(ctx)
                local __realClock = os.clock()
                ctx.realDt = __realClock - __lastRealClock
                __lastRealClock = __realClock
                __frame = __frame + 1
                pcall(__scheduler.Update, ctx)
            end
    end
    __scheduler.Init(nil,nil)
    activate()
    Engine.Schedule = __scheduler
end
return Engine
