-- CET 2.5 standalone Scheduler probe against the actual packaged Scheduler source.
-- Run: lua5.4 tests/CET25SchedulerProbeContract.lua payload/0-Engine/modules/G-CET/Scheduler.lua
local schedulerPath = arg[1]
assert(schedulerPath, 'pass Scheduler.lua path')
local events = {}
local capturing = false
local marks, registered, began, ended = {}, {}, {}, {}
local currentContext
function registerForEvent(name, callback)
  assert(events[name] == nil, 'duplicate CET mod event: ' .. name)
  events[name] = callback
end
function CETProfilerIsRunning() return capturing end
function CETProfilerMark(label) marks[#marks+1] = label return true end
function CETProfilerSchedulerRegisterJob(owner, kind, label, interval, unit)
  local index = #registered + 1
  registered[index] = {owner=owner,kind=kind,label=label,interval=interval,unit=unit}
  return index
end
function CETProfilerSchedulerFrameBegin(frame) end
function CETProfilerSchedulerFrameEnd(frame) end
function CETProfilerSchedulerJobBegin(handle)
  began[handle] = (began[handle] or 0) + 1
  return handle
end
function CETProfilerSchedulerJobEnd(handle, token, frame)
  assert(handle == token)
  ended[handle] = (ended[handle] or 0) + 1
end
local Scheduler = dofile(schedulerPath)
Scheduler.Init(nil,nil)
local Engine = {}
function Engine.Register(owner)
  local mod = {Schedule = {}}
  for _, method in ipairs({'EveryFrames','Every','After'}) do
    local fn = Scheduler[method]
    mod.Schedule[method] = function(...)
      local args = {...}
      args[2].owner = args[2].owner or owner
      return fn(table.unpack(args))
    end
  end
  return mod
end
function GetMod(id)
  if id == '0-Engine' then return Engine end
end
local source = assert(loadfile('validation/CET-1.38.1/GCET_25_SchedulerProbe/init.lua'))
source()
events.onInit()
assert(#registered == 3)
local function update(n)
  for _=1,n do
    local frame = (currentContext and currentContext.frame or 0)+1
    currentContext = {frame=frame,dt=0.02,realDt=0.02,now=frame*0.02,
      playing=true,inMenu=false,generation=0}
    Scheduler.Update(currentContext)
    events.onUpdate(0.02)
  end
end
update(250) -- don't profile initial startup; one-shot isn't registered yet
local before = #marks
capturing = true
update(650)
assert(#marks > before, 'native markers missing')
assert(#registered == 5, 'expected 5 distinct jobs')
assert(#began > 0)
for id,v in pairs(began) do assert(ended[id] == v, 'unpaired profiler job timing') end
for i,j in ipairs(registered) do
  assert(j.owner == 'GCET25SchedulerProbe', 'wrong owner: '..j.owner)
  assert(began[i] and began[i] > 0, 'job never executed: '..j.label)
end
local checkpoints = {}
for _,m in ipairs(marks) do
  local f,t,o,c,p,e=m:match('GCET25_SCHED_V1_F10_(%d+)_T1_(%d+)_O3_(%d+)_C5_(%d+)_P4_(%d+)_E_(%d+)')
  if f then checkpoints[#checkpoints+1] = {f=tonumber(f),t=tonumber(t),o=tonumber(o),c=tonumber(c),p=tonumber(p),e=tonumber(e)} end
end
assert(#checkpoints >= 3, 'insufficient checkpoints')
local last=checkpoints[#checkpoints]
assert(last.o == 1, 'one-shot not fired once')
assert(last.c == 8, 'cancel job not capped at 8')
assert(last.p == 6, 'pause job not capped at 6')
assert(last.f > 40 and last.t > 10, 'repeating jobs did not execute')
assert(last.e == 0, 'fixture errors')
capturing = false
update(20)
local markBeforeSecond = #marks
capturing = true
update(420)
assert(#marks > markBeforeSecond, 'second capture markers missing')
local second = marks[#marks]
local f,t,o,c,p,e=second:match('GCET25_SCHED_V1_F10_(%d+)_T1_(%d+)_O3_(%d+)_C5_(%d+)_P4_(%d+)_E_(%d+)')
assert(tonumber(o)==2, 'second capture one-shot not fired')
assert(tonumber(c)==16, 'second capture cancel job not restarted')
assert(tonumber(p)==12, 'second capture paused job not resumed')
events.onShutdown()
local beforeShutdown={}
for k,v in pairs(began) do beforeShutdown[k]=v end
update(100)
for k,v in pairs(began) do assert(beforeShutdown[k]==v,'job executing after shutdown') end
-- Missing 0-Engine must fail closed without registering a substitute scheduler.
local priorRegistrations = #registered
events = {}
GetMod = function() return nil end
source()
events.onInit()
assert(#registered == priorRegistrations, 'missing 0-Engine still registered jobs')
capturing = true
events.onUpdate(0.02)
assert(marks[#marks]:match('_E_1$'), 'missing 0-Engine diagnostic marker absent')
events.onShutdown()
print('PASS: real Scheduler bridge registration, native timing, independent markers, pause/cancel/restart/shutdown and missing-host fail-closed')
