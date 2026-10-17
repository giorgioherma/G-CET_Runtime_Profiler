-- Opt-in 0-Engine singleton bootstrap. Does NOT modify 0-Engine itself.
-- Install from a Resolver-generated shared host extension exactly once, then
-- source-proven mods can call GetMod("0-Engine").GCET.LookupCacheHost.
local M={}
local VERSION=1
function M.Install(engine,native,consumerId,components)
  if type(engine)~="table" or type(engine.Register)~="function" then
    return nil,"UNSUPPORTED_ENGINE"
  end
  -- 0-Engine builds in real installed stacks do not necessarily predeclare
  -- a GCET namespace. Do not mutate the engine unless host construction
  -- has succeeded, and never replace an existing foreign namespace.
  local api=engine.GCET
  if api~=nil and type(api)~="table" then
    return nil,"EXISTING_NAMESPACE_CONFLICT"
  end
  api=api or {}
  if api.LookupCacheHost~=nil then
    if api.LookupCacheHostVersion~=VERSION
      or api.LookupCacheConsumerId~=consumerId then
      return nil,"EXISTING_HOST_CONFLICT"
    end
    if api.LookupCacheHost.closed then
      return nil,"EXISTING_HOST_CLOSED"
    end
    return api.LookupCacheHost
  end
  if type(components)~="table"
    or type(components.Host)~="table" then
    return nil,"MISSING_COMPONENTS"
  end
  local ok,host=pcall(function()
    local client=engine.Register("G-LookupCache")
    return components.Host.new(native,client,consumerId,
      components.Hub,components.Set,components.Gate,components.Life)
  end)
  if not ok or type(host)~="table" then
    return nil,"HOST_NOT_READY"
  end
  -- All changes are isolated to this new opt-in namespace.
  if engine.GCET==nil then engine.GCET=api end
  api.LookupCacheHost=host
  api.LookupCacheHostVersion=VERSION
  api.LookupCacheConsumerId=consumerId
  return host
end
function M.Uninstall(engine)
  local api=type(engine)=="table" and engine.GCET or nil
  if type(api)~="table" or api.LookupCacheHostVersion~=VERSION
       or not api.LookupCacheHost then return false end
  api.LookupCacheHost:shutdown()
  api.LookupCacheHost=nil
  api.LookupCacheHostVersion=nil
  api.LookupCacheConsumerId=nil
  return true
end
return M
