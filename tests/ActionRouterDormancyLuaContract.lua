-- Source-level runtime contract for the pinned 0-Engine ActionRouter.
-- Tests active=false subscriptions and per-subscriber native->Lua fanout
-- without introducing or claiming removal of the single shared native hook.
local dir = assert(arg[1], "unpacked ActionRouter module folder required")
package.path = dir .. "/?.lua;" .. package.path
local Router = require("ActionRouter")
local calls, secondCalls, decodedTypes = 0, 0, 0

local action = {
    GetName = function() return { value = "MoveX" } end,
    GetType = function()
        decodedTypes = decodedTypes + 1
        return { value = "BUTTON_PRESSED" }
    end
}
local function dispatch()
    Router.Dispatch(nil, action, nil)
end

local camera = Router.Subscribe({
    id = "itpp-dormancy-test",
    actions = { "MoveX" },
    decodeType = false
}, function()
    calls = calls + 1
end, "immersive_third_person")
-- Safe author/source opt-in: if a legacy router lacks SetActive, its
-- original active registration stays active rather than being stranded.
camera.SetActive(false)
assert(camera.IsActive() == false, "initial dormant subscriber must be paused")
dispatch()
assert(calls == 0, "initial FPP state must not invoke camera action handler")

-- Another mod still needs the shared action bridge while camera is dormant.
local other = Router.Subscribe({
    actions = { "MoveX" },
    decodeType = false
}, function()
    secondCalls = secondCalls + 1
end, "OtherMod")
dispatch()
assert(calls == 0 and secondCalls == 1,
    "pausing one mod must not disable another action subscriber")

camera.SetActive(true) -- source-proven TPP activation
dispatch()
assert(camera.IsActive() and calls == 1 and secondCalls == 2,
    "TPP activation must restore the exact camera action delivery")

camera.Pause() -- original FPP restore/session-gap source state
dispatch()
assert(not camera.IsActive() and calls == 1 and secondCalls == 3,
    "FPP restoration must stop only third-person downstream dispatch")

camera.Resume() -- reactivation through original mod-owned input
dispatch()
assert(calls == 2 and secondCalls == 4, "original wake must resume subscriber")

assert(decodedTypes == 0,
    "decodeType=false should avoid unnecessary native action-type decoding")
camera.Cancel()
camera.SetActive(true)
dispatch()
assert(calls == 2 and secondCalls == 5 and not camera.IsActive(),
    "cancelled subscription must not resurrect, affect other mods or decode type")

other.unsubscribe()
dispatch()
assert(secondCalls == 5, "cancelled second listener should not receive events")

print("Pinned 0-Engine ActionRouter native->Lua subscriber dormancy: PASS")
