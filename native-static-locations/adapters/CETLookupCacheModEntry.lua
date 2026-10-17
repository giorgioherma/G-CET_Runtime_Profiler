-- Install as bin/x64/plugins/cyber_engine_tweaks/mods/G-LookupCache/init.lua
-- with the core adapters in this mod's "modules/" directory. It registers
-- ONCE on CET onInit and NO update/Cron/listener for proximity tracking.
local M={}
local source=debug.getinfo(1,"S").source
local home=type(source)=="string" and source:gsub("^@",""):match("^(.*[/\\])") or nil
local cachedComponents=nil
local CONSUMER_ID=1703001

local function components()
  if cachedComponents then return cachedComponents end
  if not home then return nil end
  local files={
    Host="CETNativeWakeHost.lua",
    Hub="CETStaticLocationConsumer.lua",
    Set="CETAnchorSetGate.lua",
    Gate="CETNativeJobGate.lua",
    Life="CETSourceAnchorLifecycle.lua",
  }
  local loaded={}
  for key,name in pairs(files) do
    local ok,value=pcall(dofile,home.."modules/"..name)
    if not ok or type(value)~="table" then return nil end
    loaded[key]=value
  end
  cachedComponents=loaded
  return loaded
end

function M.TryAttach()
  local ok,engine=pcall(GetMod,"0-Engine")
  if not ok or type(engine)~="table"
     or type(engine.Register)~="function" then
    return nil,"ENGINE_NOT_READY"
  end
  local loaded=components()
  if not loaded then return nil,"MISSING_SHARED_MODULES" end
  local okBoot,bootstrap=pcall(dofile,home.."modules/CETNativeWakeBootstrap.lua")
  if not okBoot or type(bootstrap)~="table"
     or type(bootstrap.Install)~="function" then
    return nil,"MISSING_BOOTSTRAP"
  end
  if type(Game)~="table" then return nil,"NATIVE_PROXY_UNAVAILABLE" end
  return bootstrap.Install(engine,Game,CONSUMER_ID,loaded)
end

registerForEvent("onInit",function()
  -- Other mod load ordering is not assumed. A source-owned original
  -- scheduler can retry M.TryAttach() until 0-Engine is present.
  M.TryAttach()
end)

return M
