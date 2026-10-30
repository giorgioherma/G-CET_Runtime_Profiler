-- Execute the exact Lua branch emitted by the authoritative ITP generator.
-- The source-owned timeout releases the startup latch; it never forces TPP.
local path = assert(arg[1], "ITP generator path required")
local file = assert(io.open(path, "rb"))
local text = assert(file:read("*a"))
file:close()
local start = assert(text:find("private static string EnsureItppBoundedSessionRestore", 1, true))
local finish = assert(text:find("private static string ApplyImmersiveThirdPersonDetachedDormancy", start, true))
local rule = text:sub(start, finish - 1)
local body = assert(rule:match('var body = """(.-)"""%.Replace'), "source-owned Lua handoff missing")
assert(body:find("if hardCapped and not ready then", 1, true))
assert(body:find("state.pendingSessionTppRestore = nil", 1, true))
assert(not body:find("applyTppMode", 1, true), "unsafe direct camera activation")
assert(not body:find("state.enabled =", 1, true), "forced camera state")
local factory, err = load("return function(state, config, hardCapped, ready)\n" ..
  body .. "\nreturn 'normal-author-guards'\nend", "@itp-author-source-handoff")
assert(factory, err)
local step = factory()
local function run(hardCapped, ready, existing)
  local pending = { wantedTpp = true, cleanDone = true }
  local state = { modClock = 32, uiReadyAt = existing, pendingSessionTppRestore = pending,
    enabled = false, requestedMode = "FPP" }
  local result = step(state, {sessionRestoreDelay = 1.25}, hardCapped, ready)
  assert(state.enabled == false and state.requestedMode == "FPP", "forced TPP")
  return state, pending, result
end
local notExpired, original, status = run(false, false, nil)
assert(notExpired.pendingSessionTppRestore == original and
  notExpired.uiReadyAt == nil and status == "normal-author-guards",
  "unexpired session must preserve original gating")
local ready, p, status2 = run(true, true, nil)
assert(ready.pendingSessionTppRestore == p and status2 == "normal-author-guards",
  "safe-ready path must remain in author's existing handoff")
local expired, _, status3 = run(true, false, nil)
assert(expired.pendingSessionTppRestore == nil and expired.uiReadyAt == 30.75 and
  status3 == nil, "expired startup must resume normal camera ownership")
local existing, _, status4 = run(true, false, 4.5)
assert(existing.pendingSessionTppRestore == nil and existing.uiReadyAt == 4.5 and
  status4 == nil, "earlier valid UI readiness must be preserved")
print("ITP source-owned bounded session handoff: guarded 12-second release, no forced TPP PASS")
