local root=arg[1]
package.path=root.."/?.lua;"..package.path
local function exercise(generated, mixed)
    package.loaded["external/Cron"]=nil
    package.loaded["modules/G-CET/Scheduler"]=nil
    local update
    registerForEvent=function(event,fn) assert(event=="onUpdate");update=fn end
    if not generated or mixed=="module" then
        package.loaded["external/Cron"]=dofile(root.."/stock-cron.lua")
    end
    if mixed=="scheduler" then
        package.loaded["modules/G-CET/Scheduler"]={}
    end
    local cron=dofile(root..((not generated or mixed=="host") and "/stock-host.lua" or "/init.lua"))
    local n=0
    local raw=pcall
    pcall=function(fn,...)
        if fn==cron.Update then n=n+1 end
        return raw(fn,...)
    end
    local trace={}
    local function record(s) trace[#trace+1]=s end
    local function tick(count,dt)for i=1,count do update(dt or 1)end end
    tick(60)
    local idle=n
    local periodic=cron.Every(2,function()record("periodic")end)
    cron.Pause(periodic);cron.Pause(periodic)
    tick(60)
    local paused=n-idle
    cron.Resume(periodic);cron.Resume(periodic)
    tick(1);assert(#trace==0)
    tick(1);assert(trace[1]=="periodic")
    cron.Pause(periodic);tick(4);cron.Resume(periodic)
    tick(2);assert(trace[2]=="periodic")
    cron.Halt(periodic);cron.Resume(periodic);tick(1)
    local afterPrune=n
    tick(30)
    local drained=n-afterPrune
    cron.NextTick(function(a)
        record("one")
        cron.NextTick(function()record("nested")end)
        a.Halt(a)
    end)
    tick(2)
    cron.NextTick(function()record("error");error("expected")end)
    tick(2)
    local reentrant
    reentrant=cron.Every(1,function(a)
        record("reentry")
        cron.Pause(a)
        cron.Update(1)
        cron.Halt(a)
    end)
    tick(2)
    cron.Resume(reentrant)
    tick(2)
    pcall=raw
    if generated and not mixed then
        assert(idle==0 and paused==0 and drained==0,"idle/paused/pruned dispatch")
    else
        assert(idle==60 and paused==60 and drained==30,"mixed original dispatch")
    end
    return table.concat(trace,","),n
end
local reference,stock=exercise(false)
local actual,optimized=exercise(true)
assert(reference==actual,"callback trace changed: "..reference.." vs "..actual)
assert(optimized<stock,"no recurring execution eliminated")
for _,mixed in ipairs({"module","host","scheduler"}) do
    local trace=exercise(true,mixed)
    assert(trace==reference,"mixed overlay changed timing: "..mixed)
end
print("Cron author cadence, pause/resume, prune, reentry, errors and mixed overlays PASS; host dispatch "..stock.." -> "..optimized)
