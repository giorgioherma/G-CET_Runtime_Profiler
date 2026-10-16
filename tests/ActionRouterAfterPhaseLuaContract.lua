-- Experimental after-phase ActionRouter: preserves CET Observe/ObserveAfter order.
local dir = assert(arg[1], "unpacked ActionRouter directory required")
package.path = dir .. "/?.lua;" .. package.path
local route = require("ActionRouter")
local afterHooks, trace, nameReads, typeReads = {}, {}, 0, 0
ObserveAfter = function(className, method, fn)
    assert(className == "PlayerPuppet" and method == "OnAction")
    afterHooks[#afterHooks+1] = fn
end
local action = {
    GetName = function() nameReads = nameReads + 1; return {value="popup_moveUp"} end,
    GetType = function() typeReads = typeReads + 1; return {value="REPEAT"} end,
    IsAction = function(_, name) return name == "popup_moveUp" end
}
local consumer={count=0,Consume=function(self) self.count=self.count+1 end}
local function emit()
    route.Dispatch(nil,action,consumer)
    for _,fn in ipairs(afterHooks) do fn(nil,action,consumer) end
end
-- The before-phase handler is installed on 0-Engine initialization and remains
-- independent of after-phase consumers.
local before=route.Subscribe({actions={"popup_moveUp"},decodeType=false},
    function(_,_, c) trace[#trace+1]="before";c:Consume() end,"BeforeClient")
assert(#afterHooks==0,"no eager after native hook")
local after=route.Subscribe({actions="*",phase="after",decodeType=false,active=false},
    function(_,_, c) trace[#trace+1]="after";c:Consume() end,"hotscenes")
assert(#afterHooks==1,"after hook must be registered exactly once, lazily")
emit()
assert(#trace==1 and trace[1]=="before" and consumer.count==1,
    "dormant after subscriber must not receive callbacks")
assert(typeReads==0,"decodeType=false must not add native type calls")
after.SetActive(true)
emit()
assert(table.concat(trace,",")=="before,before,after","before/after phase ordering")
assert(consumer.count==3,"original consumer must reach both phases")
local other=route.Subscribe({actions={"popup_moveUp"},phase="after",decodeType=false},
    function() trace[#trace+1]="otherAfter" end,"OtherAfterClient")
assert(#afterHooks==1,"subscribers must share a single after hook")
after.Pause()
local previous=#trace
emit()
assert(#trace==previous+2 and trace[#trace]=="otherAfter",
    "one subscriber pause must not silence other after clients")
other.Pause()
before.Pause()
local beforeNative=nameReads
emit()
assert(nameReads==beforeNative,"sleeping subscribers must not decode native action name")
assert(route.GetInfo().afterActive==0 and route.GetInfo().beforeActive==0,
    "active counters must reach zero without subscriber unregistration")
after.Resume()
emit()
assert(trace[#trace]=="after" and trace[#trace-1]~="before",
    "after-only wake must preserve phase isolation")
after.Cancel()
other.Cancel()
before.Cancel()
assert(route.GetInfo().afterHookInstalled and #afterHooks==1,
    "CET after observer remains physically attached; never claim native suspension")
local broken=route.Subscribe({phase="unknown"},function() error("must not run") end,"Invalid")
assert(not broken.IsActive(),"unknown phase must fail closed")
print("experimental ActionRouter after-phase contract PASSED")
