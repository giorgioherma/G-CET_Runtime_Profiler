local root, generated = arg[1], arg[2]
local function simulate(schedulerPath, hostPath)
    local counts, trace = {}, {}
    local wall = 0
    local oldClock = os.clock
    os.clock = function() return wall end
    function count(name) counts[name]=(counts[name] or 0)+1 end
    CETProfilerIsRunning = function() return true end
    CETProfilerSchedulerRegisterJob = function() count("register"); return counts.register end
    CETProfilerSchedulerFrameBegin = function(frame) count("begin") end
    CETProfilerSchedulerFrameEnd = function(frame) count("end") end
    CETProfilerSchedulerJobBegin = function(handle) count("jobBegin"); return handle end
    CETProfilerSchedulerJobEnd = function() count("jobEnd") end
    local S = assert(loadfile(schedulerPath))()
    package.loaded["modules/G-CET/Scheduler"] = S
    local E = assert(loadfile(hostPath))()
    local function tick(n)
        for _=1,n do wall=wall+0.125; E.Tick(0.125) end
    end
    local function callback(label)
        return function(ctx)
            assert(ctx.player=="player" and type(ctx.state)=="table", "active job context changed")
            trace[#trace+1]=string.format("%s/%d/%.6f/%.6f/%.6f",label,ctx.frame,ctx.elapsed,ctx.now,ctx.realDt)
        end
    end
    tick(8)
    local noJobContexts=counts.player or 0
    local a=S.EveryFrames(3,{spread=false},callback("frame"))
    local b=S.Every(0.5,{clock="real",owner="test",id="phase"},callback("real"))
    local c=S.After(0.25,{},callback("once"))
    tick(7)
    a.Pause(); a.Pause(); b.SetActive(false); c.Cancel()
    tick(2) -- complete any actual pending cleanup
    local before=counts.player or 0
    tick(9)
    local pausedContexts=(counts.player or 0)-before
    a.Resume(); a.Resume(); b.SetActive(true)
    tick(11)
    a.Cancel(); b.Cancel(); a.Cancel()
    tick(2)
    before=counts.player or 0
    tick(8)
    local cancelledContexts=(counts.player or 0)-before
    local d=S.EveryFrames(2,{spread=false,generationScoped=true},callback("generation"))
    S.SetGeneration(2)
    tick(2)
    assert(not d.IsActive())
    local e
    e=S.NextTick(function(ctx) callback("self-cancel")(ctx); e.Cancel() end)
    tick(3)
    local beforeFinal=counts.player or 0
    tick(5)
    local finalContexts=(counts.player or 0)-beforeFinal
    local f=S.EveryFrames(2,{spread=false,active=false},callback("resume-new"))
    tick(3)
    f.Resume()
    tick(4)
    f.Cancel()
    tick(2)
    assert(counts.begin==counts["end"],"unbalanced profiler scopes")
    assert(counts.jobBegin==counts.jobEnd,"unbalanced job scopes")
    assert(S.GetInfo().activeJobs==0,"active count wrong")
    os.clock=oldClock
    return table.concat(trace,"\n"),counts,{noJobContexts,pausedContexts,cancelledContexts,finalContexts}
end
local old,oldCounts,oldIdle=simulate(root.."/idle-original.lua",root.."/idle-host-original.lua")
local new,newCounts,newIdle=simulate(generated,root.."/idle-host-generated.lua")
assert(old==new,"job timing/elapsed/phase/context changed:\n"..old.."\n---\n"..new)
assert(oldCounts.begin==newCounts.begin and oldCounts.jobBegin==newCounts.jobBegin and oldCounts.register==newCounts.register,
    "measurement telemetry lost")
assert(newCounts.player==newCounts.state,"context acquisition mismatch")
for i,v in ipairs(newIdle) do assert(v==0,"idle context was acquired in scenario "..i..": "..v) end
for i,v in ipairs(oldIdle) do assert(v>0,"stock context count missing") end
-- Old controller under new host, and new controller under old host.
local mixed,mixedCounts=simulate(root.."/idle-original.lua",root.."/idle-host-generated.lua")
assert(mixed==old and mixedCounts.player==oldCounts.player,"older controller fallback changed")
local mixed2,mixed2Counts=simulate(generated,root.."/idle-host-original.lua")
assert(mixed2==old and mixed2Counts.player==oldCounts.player,"older host fallback changed")
print("Canonical scheduler idle context PASS; original contexts "..oldCounts.player..", generated "..newCounts.player.."; matched all timing and profiler scopes")
