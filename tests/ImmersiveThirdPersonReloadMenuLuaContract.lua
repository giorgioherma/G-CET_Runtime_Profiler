-- Execute the existing ITP generator's Lua helper against retained save state.
local file = assert(io.open(assert(arg[1], "generator source required"), "rb"))
local source = assert(file:read("*a")); file:close()
local start = assert(source:find("private static string EnsureItppReloadMenuCacheReset", 1, true))
local finish = assert(source:find("private static string EnsureItppBoundedSessionRestore", start, true))
local body = assert(source:sub(start, finish):match('const string body = """(.-)""";'),
  "authoritative Lua reload reset missing")
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
