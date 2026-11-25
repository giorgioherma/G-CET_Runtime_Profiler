-- Model EventEmitter's private bucket and actual unsubscribe behavior.
-- The real generated host and real canonical Scheduler own the frame phase.
local root=assert(arg[1])
package.path=root.."/?.lua;"..package.path
local stockPairs=pairs
local function run(file,legacy)
    local scans,trace=0,{}
    local buckets=setmetatable({},{__mode="k"})
    package.loaded["modules/G-CET/Scheduler"]=nil
    if legacy then package.loaded["modules/G-CET/Scheduler"]=dofile(root.."/old-scheduler.lua") end
    local E={}
    function E.new()
        local listeners={}
        local e={}
        function e:subscribe(fn)
            if type(fn)~="function" then return {unsubscribe=function()end} end
            listeners[#listeners+1]=fn
            return {unsubscribe=function()
                for i,f in ipairs(listeners) do if f==fn then table.remove(listeners,i);break end end
            end}
        end
        function e:getListenerCount()return #listeners end
        function e:trigger(frame)
            for i=1,#listeners do
                local f=listeners[i]
                if f then f(frame) end
            end
        end
        return e
    end
    package.loaded["modules/EventEmitter"]=E
    local engine=dofile(root.."/"..file)
    -- Count only traversal of the original host registry, not any other
    -- Scheduler table. debug.getupvalue is a fixture-only probe.
    local registry
    for i=1,30 do
        local k,v=debug.getupvalue(engine.UpdateFrame,i)
        if not k then break end
        if k=="frameEmitters" then registry=v;break end
    end
    assert(registry,"original registry retained")
    pairs=function(t)
        if t==registry then scans=scans+1 end
        return stockPairs(t)
    end
    local frame=0
    local function tick(n)
        for _=1,n do frame=frame+1;engine.UpdateFrame(0) end
    end
    local function listen(interval,tag)
        return engine.OnFrame(interval,function(n)
            trace[#trace+1]=tag..":"..n
        end)
    end
    tick(10)
    assert(scans==(file=="generated.lua" and not legacy and 0 or 10),"idle registry scan")
    local a=listen(5,"a")
    local b=listen(7,"b")
    local c=listen(5,"c")
    tick(25)
    local countMixed=scans
    a.unsubscribe()
    tick(15)
    c.unsubscribe()
    tick(21)
    b.unsubscribe()
    local before=scans
    tick(20)
    assert(scans-before==(file=="generated.lua" and not legacy and 0 or 20),"last removal did not sleep")
    local d=listen(4,"d")
    tick(17)
    d.unsubscribe()
    d.unsubscribe()
    tick(5)
    pairs=stockPairs
    return table.concat(trace,"|"),scans,countMixed
end
local a,as=run("original.lua")
local b,bs=run("generated.lua")
local c,cs=run("generated.lua",true)
assert(a==b and a==c,"frame callbacks or order changed")
assert(bs<as and cs==as,"legacy fallback or deadline count")
print("Frame emitter demand: exact callback trace; registry scans "..as.." -> "..bs.."; legacy fallback PASS")
