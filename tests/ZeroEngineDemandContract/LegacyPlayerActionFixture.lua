local EventEmitter = require('modules/EventEmitter')
local Scheduler = require('modules/G-CET/Scheduler')
local Engine = {Schedule = Scheduler}
local Events = {
    PlayerAction = EventEmitter.new(),
    PlayerReady = EventEmitter.new()
}
local isPlaying,State = true,{inMenu=false}
local Logger={Log=function()end}
local Lifecycle={Init=function()end}
local DerivedState={Init=function()end}
local BlackboardCache={Init=function()end}
function Engine.Subscribe(eventName, fn, source)
    local proxy = Events[eventName]
    if proxy then
        return proxy:subscribe(fn, source)
    else
        Logger.Log("0-Engine", "Unknown event: " .. tostring(eventName), "warn")
        return { unsubscribe = function() end }
    end
end
function Engine.GetEventNames()
    local names={}
    for name, _ in pairs(Events) do
        table.insert(names, name)
    end
    return names
end
function Engine.SetPlaying(value) isPlaying=value end
function Engine.SetMenu(value) State.inMenu=value end
function Engine.Debug()
    local names,totalSubs={},0
    for name, emitter in pairs(Events) do
        local count = emitter:getListenerCount()
        totalSubs = totalSubs + count
        table.insert(names, { name = name, count = count, emitter = emitter })
    end
    for _,entry in ipairs(names) do
        entry.emitter:getListenerSources()
    end
    return totalSubs
end
function Engine.Init()
    Lifecycle.Init(Events, State, Logger)
    DerivedState.Init(Events)
    BlackboardCache.Init(Events)
    Observe('PlayerPuppet', 'OnAction', function(_, action)
        if not isPlaying or State.inMenu then return end
        Events.PlayerAction:trigger(action)
    end)
end
return Engine
