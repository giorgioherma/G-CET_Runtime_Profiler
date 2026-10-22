namespace GCETRuntimeProfiler.Core.Services;

// The native boundary only bypasses futile distant fallback actor lookups.
// Scriptable encounter status, death/leave respawn, global scheduler, cleanup,
// rewards and saved-game reconciliation remain source-owned and always active.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyCyberpsychosNativeLocations(
        SemanticPatchContext context)
    {
        var init=context.FindFile("init.lua",
            "BossWatchdog.init(Bridge, Diagnostics)",
            "local Sites = require(\"./modules/sites.lua\")",
            "TryAttachRepeatablePass4");
        var watchdog=context.FindFile("modules/boss_watchdog.lua",
            "local function syncSiteWithPosition(",
            "if status == 1 then",
            "local trackedID, trackedEntity = tracked(system, site.id)",
            "function Watchdog.clear()");
        const string marker="G-CET native location:cyberpsychos-fallback-v1";
        // Historical output marked only the watchdog, even though init.lua
        // already contained the completed coordinator bridge. Recognize the
        // actual paired postconditions, not the misplaced header marker.
        const string legacyBridge =
            "require(\"GCET_Semantic/cyberpsycho_native_wake\").attach(Sites, BossWatchdog)";
        var hasInitMarker=init.Text.Contains(marker,StringComparison.Ordinal);
        var hasBridge=init.Text.Split(legacyBridge,StringSplitOptions.None).Length==2;
        var hasDog=watchdog.Text.Contains(marker,StringComparison.Ordinal);
        if(hasDog && hasBridge)
        {
            if(!watchdog.Text.Contains("Watchdog.nativeFar and Watchdog.nativeFar[site.id]",
                    StringComparison.Ordinal) ||
               !watchdog.Text.Contains("state.fallbackID == nil",StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Cyberpsychos native watchdog completion proof changed.");
            return SemanticInjectionResult.Skip(
                "Cyberpsychos native fallback source and 0-Engine bridge already installed.");
        }
        if(hasInitMarker || hasDog || hasBridge)
            throw new InvalidOperationException(
                "Cyberpsychos native fallback stage incomplete: missing watchdog gate or init worker bridge.");
        var crlfInit=init.Text.Contains("\r\n",StringComparison.Ordinal);
        var crlfDog=watchdog.Text.Contains("\r\n",StringComparison.Ordinal);
        var i=crlfInit?init.Text.Replace("\r\n","\n",StringComparison.Ordinal):init.Text;
        var w=crlfDog?watchdog.Text.Replace("\r\n","\n",StringComparison.Ordinal):watchdog.Text;
        const string dogAnchor="    if status == 1 then\n        -- PASS4-R4:";
        if(w.Split(dogAnchor,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Cyberpsychos active watchdog boundary changed.");
        var gate="""
    if status == 1 then
        -- G-CET native location:cyberpsychos-fallback-v1
        -- FAR is only a coarse hint for missing fallback boss lookup.
        -- Existing actor, pending attach, cleanup and all status checks remain.
        local far = Watchdog.nativeFar and Watchdog.nativeFar[site.id]
        if far == true and triggerDistance <= 180.0
            and state.fallbackID == nil and state.registered == false
            and state.grace == 0.0 then
            return
        end
        -- PASS4-R4:
""";
        w=w.Replace(dogAnchor,gate,StringComparison.Ordinal);
        const string initAnchor="    BossWatchdog.init(Bridge, Diagnostics)\n";
        if(i.Split(initAnchor,StringSplitOptions.None).Length!=2)
            throw new InvalidOperationException("Cyberpsychos watchdog initialization drifted.");
        var attach="""
    -- G-CET native location:cyberpsychos-fallback-v1
    -- Native fallback vicinity is delegated to 0-Engine worker.
    require("GCET_Semantic/cyberpsycho_native_wake").attach(Sites, BossWatchdog)
""";
        context.WriteCoordinator("cyberpsycho_native_wake.lua", """
-- G-CET semantic coordinator:native-location-cyberpsychos
-- One native owner per source site; no clocks, combat or respawn decisions.
local M = {}
function M.attach(sites, watchdog)
  watchdog.nativeFar = {}
  if not sites or not sites.list or #sites.list < 1 or #sites.list > 512 then return false end
  local okEngine, engine = pcall(GetMod, "0-Engine")
  local host = okEngine and type(engine) == "table"
    and engine.GCET and engine.GCET.NativeLocationHost or nil
  if not host or type(host.attachCompiled) ~= "function" then return false end
  for index, site in ipairs(sites.list) do
    local siteID = site.id
    local owner = 1004100 + index
    local ok, attached = pcall(function()
      return host:attachCompiled(owner, 1,
        function() watchdog.nativeFar[siteID] = false end,
        function() watchdog.nativeFar[siteID] = true end,
        function() watchdog.nativeFar[siteID] = nil; return true end)
    end)
    if not ok or not attached then watchdog.nativeFar[siteID] = nil end
  end
  return true
end
return M
""");
        i=i.Replace(initAnchor,initAnchor+attach+"\n",StringComparison.Ordinal);
        context.Write(init,crlfInit?i.Replace("\n","\r\n",StringComparison.Ordinal):i);
        context.Write(watchdog,crlfDog?w.Replace("\n","\r\n",StringComparison.Ordinal):w);
        return SemanticInjectionResult.Success(
            "Source-count compiled vicinity owners bypass only distant unneeded fallback-boss resolution; death/departure, status, rewards, existing entities and UNKNOWN retain original Lua lifecycle.");
    }
}
