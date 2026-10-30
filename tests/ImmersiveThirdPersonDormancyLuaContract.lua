-- Runtime contract: exactly one pausable owner, no quiet polling, native-key fail-open.
local workerPath = assert(arg[1], "worker path required")
local M = assert(dofile(workerPath))
local state = {
 enabled=false, requestedMode="FPP", autoPerspective={userWantsTpp=false},
 modClock=1.0,
 turnWindowStartCleared=true, nativeSettingsDirty=false, depGuard={done=true},
 pickupRange={}, move360={}, armHeal={},
 spatialAudio={}, bodySliders={}, groundLevel={}, holocallEyes={}
}
local config = {camera={move360=false, spatialAudio=false},
 autoPerspective={}, apiAccess=false, nativeToggleKey=""}
local active, ended, called = true, false, 0
local subscriptions, scheduleCount = {}, 0
local frame = {}
function frame.IsActive() return active end
function frame.Pause() active=false end
function frame.Resume() active=true end
function frame.Cancel() ended=true; active=false end
local schedule = {
 EveryFrame = function(options, callback)
  scheduleCount=scheduleCount+1
  assert(options.id == "itp.frame" and options.active == true)
  frame.callback=callback
  return frame
 end
}
local client={Schedule=schedule,
 Subscribe=function(event, callback)
  subscriptions[event]=callback
  return {unsubscribe=function() subscriptions[event]=nil end}
 end}
local ctrl=assert(M.Install({},state,config,function(dt)
 assert(type(dt)=="number")
 state.modClock=state.modClock+dt
 called=called+1
end,client))
ctrl.Start()
assert(scheduleCount==1 and frame.IsActive())
local function ticks(n)
 for i=1,n do if frame.IsActive() then frame.callback({dt=0.016,frame=i}) end end
end
state.lastToggleAt=0.05
ticks(2)
assert(not frame.IsActive() and called==2, "idle never detached")
assert(state.lastToggleAt==0.05, "original toggle debounce timestamp lost")
ticks(150)
assert(called==2, "idle frame kept executing")
local quiet=ctrl.Diagnostics()
assert(quiet.mode=="dormant" and quiet.activeJob==false and quiet.pauses==1)
-- REDscript native key has no direct Lua signal. It MUST retain active mode.
config.nativeToggleKey="IK_Z"
ctrl.Signal("settings")
ticks(8)
assert(frame.IsActive() and called==10, "native key config lost polling fallback")
config.nativeToggleKey=""
ctrl.Signal("settings")
ticks(2)
assert(not frame.IsActive(), "unbound native key did not return to dormant")
ctrl.Signal("toggle")
state.enabled=true
ticks(20)
assert(frame.IsActive() and called==32, "active TPP cadence changed")
state.enabled=false
state.pendingFppCleanup={owed=true}
ctrl.Signal("restore")
ticks(10)
assert(frame.IsActive(), "paused before FPP cleanup")
state.pendingFppCleanup=nil
state.pendingTppDeactivate={delay=0.35}
ticks(2)
assert(frame.IsActive(), "pending TPP deactivation was suppressed")
state.pendingTppDeactivate=nil
state.lastToggleAt=state.modClock
ticks(10)
assert(frame.IsActive(), "original toggle debounce did not finish before sleep")
ticks(10)
assert(not frame.IsActive(), "cleanup and 0.25s toggle debounce settled but job stayed active")
assert(type(subscriptions.PlayerReady)=="function")
subscriptions.PlayerReady()
assert(frame.IsActive(), "lost session event wake")
ticks(2)
assert(not frame.IsActive(), "session wake never re-slept")
ticks(2)
assert(not frame.IsActive(), "spontaneous frame after resleep")
subscriptions.PlayerInvalidated()
assert(frame.IsActive(), "player lost event did not wake")
ticks(4)
assert(frame.IsActive(), "session 0.75s guard lost to two-frame sleep")
ticks(70)
assert(not frame.IsActive(), "session settle never slept")
ctrl.Stop()
assert(ended and not frame.IsActive() and next(subscriptions)==nil)
ctrl.Start()
assert(frame.IsActive(), "worker cannot restart after onShutdown/onInit lifecycle")
ticks(2)
assert(not frame.IsActive(), "restarted worker did not restore dormancy")
ctrl.Stop()
local noWake = assert(M.Install({},state,config,function() end,
  {Schedule={EveryFrame=function()
    local h={active=true}
    function h.IsActive() return h.active end
    function h.Pause() h.active=false end
    function h.Resume() h.active=true end
    function h.Cancel() h.active=false end
    noWakeFrame=h
    return h
  end}}))
noWake.Start()
assert(noWakeFrame.IsActive() and noWake.Diagnostics().sessionWakeProven==false,
  "missing independent session events must fail open, not strand ITP")
noWake.Stop()
print("ITP physically dormant job, native-key fail-open, active cadence, cleanup, session wake PASS")
