local root=arg[1]
local loaded=true
local callback
local reads,named=0,0
local generation=1
local E
local reenter=false
TestPlayer={}
GameSession={
 IsLoaded=function()return loaded end,
 GetState=function()return {isLoaded=loaded} end,
 On=function(fn)callback=fn end
}
local function notify(v)
 loaded=v
 callback({isLoaded=v})
end
CName={new=function(k)return k end}
Game={
 GetCameraSystem=function()
  reads=reads+1
  local value={generation=generation}
  if reenter then
   reenter=false;generation=generation+1;notify(false);notify(true)
  end
  return value
 end,
 GetScriptableSystemsContainer=function()
  return {Get=function(_,key)
   named=named+1
   local value={generation=generation,key=key}
   if reenter then
    reenter=false;generation=generation+1;notify(false);notify(true)
   end
   return value
  end}
 end
}
package.loaded["modules/DerivedState"]={Reset=function()end}
E=dofile(root.."/provider-session.lua")
assert(reads==0 and named==0,"provider must stay on demand")
assert(E.GetCameraSystem().generation==1 and E.GetCameraSystem().generation==1 and reads==1)
assert(E.GetScriptableSystem("A").generation==1 and E.GetScriptableSystem("A").generation==1 and named==1)
for i=1,120 do E.Tick()end
assert(reads==1 and named==1,"idle frame must not produce values")
generation=2;notify(false);notify(true)
assert(reads==1 and named==1,"session events only invalidate")
assert(E.GetCameraSystem().generation==2 and reads==2)
assert(E.GetScriptableSystem("A").generation==2 and named==2,"same player, new session")
generation=3;loaded=false;E.Tick();loaded=true;E.Tick()
assert(E.GetCameraSystem().generation==3 and reads==3,"existing live session resync")
generation=4;E.Invalidate()
assert(E.GetCameraSystem().generation==4 and reads==4,"original invalidation callback")
E.Invalidate();reenter=true
assert(E.GetCameraSystem().generation==4 and reads==5,"do not replay in-flight original call")
assert(E.GetCameraSystem().generation==5 and reads==6,"old result must not fill new session")
E.Invalidate();reenter=true
assert(E.GetScriptableSystem("A").generation==5)
assert(E.GetScriptableSystem("A").generation==6 and named==4,"named result must not fill new session")
print("Shared provider lifetime: no recurring native reads; same-player reload, resync, invalidate, reentrant cache-fill and named provider PASS")
