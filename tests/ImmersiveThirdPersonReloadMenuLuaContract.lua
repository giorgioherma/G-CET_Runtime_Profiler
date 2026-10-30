-- Execute the existing ITP generator's Lua helper against retained save state.
local file = assert(io.open(assert(arg[1], "generator source required"), "rb"))
local source = assert(file:read("*a")); file:close()
local start = assert(source:find("private static string EnsureItppReloadMenuCacheReset", 1, true))
local finish = assert(source:find("private static string EnsureItppBoundedSessionRestore", start, true))
local producer = source:sub(start, finish)
local body = assert(producer:match('const string body = """(.-)""";'),
  "authoritative Lua reload reset missing")
-- Regression for the Windows runner: C# multiline raw strings can carry
-- CRLF even though the composed Resolver text was normalized to LF.
local normalizedEmitter =
  'body.Replace("\\r\\n", "\\n", StringComparison.Ordinal).Replace(' ..
  "'\\r', '\\n')"
assert(producer:find(normalizedEmitter, 1, true),
  "ITP source generator must normalize CRLF raw Lua body before emission")
local windowsBody = body:gsub("\n", "\r\n")
assert(windowsBody:find("\r\n", 1, true))
local normalizedBody = windowsBody:gsub("\r\n", "\n"):gsub("\r", "\n")
assert(normalizedBody == body,
  "generated reset body must preserve exact Lua content across Windows and Unix checkouts")
local chunk, err = load("return function(mod,state)\n" .. body .. "\nend", "@itp-reload-source")
assert(chunk,err)
local oldBb, oldHandler = {}, {}
local perspective = {userWantsTpp=true, override="none"}
local pending = {wantedTpp=true}
local state = {
  uiBb=oldBb, uiInMenuId=15, srHandler=oldHandler, inMenuSeq=27,
  inMenuCached=true, menuHeld=true, menuWasOpen=true, uiReadyAt=5,
  enabled=false, requestedMode="FPP", autoPerspective=perspective,
  pendingSessionTppRestore=pending, gameCameraHold="scene"
}
local mod = {}
chunk()(mod,state)
assert(type(mod.gcetItppForgetSessionUi)=="function")
mod.gcetItppForgetSessionUi()
assert(state.uiBb==nil and state.uiInMenuId==nil and state.srHandler==nil)
assert(state.inMenuSeq==nil and state.inMenuCached==nil and
  state.menuHeld==false and state.menuWasOpen==false and state.uiReadyAt==nil)
assert(state.enabled==false and state.requestedMode=="FPP"
  and state.autoPerspective==perspective and perspective.userWantsTpp==true
  and state.pendingSessionTppRestore==pending and state.gameCameraHold=="scene",
  "reload reset must preserve requested camera and scene safety")
state.uiBb = {newSave=true}
mod.gcetItppForgetSessionUi()
assert(state.uiBb==nil, "new save's UI reference survived later reload")
print("ITP generated reload cache: safe repeated FPP lifecycle PASS")
