using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Explicit source-proven adapters for the independently installed RED4ext
// G-LookupCache service. Every unknown source variant fails closed.
// No native DLL or full replacement author sources are shipped.
internal static class NativeLocationFourOwnersSource
{
    private static string Once(string text, string before, string after, string owner)
    {
        if (text.Split(before, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException(owner + ": native wake source anchor changed.");
        return text.Replace(before, after, StringComparison.Ordinal);
    }


    // Each upgrader applies to the old v1 integration and to the v1 scaffold
    // composed from pristine author source IN THE SAME generation. A completed
    // live v1 source is not falsely treated as already satisfying v2.
    private static string UpgradePokerV2(string text)
    {
        var useCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        if (useCrlf) text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Once(text,
            "G-CET native location:poker-v1",
            "G-CET native location:poker-v2",
            "Poker native revision");
        text = Once(text,
            "if lookup and type(lookup.attach) == 'function' and type(lookup.attach) == \"function\" then",
            "if lookup and type(lookup.attach) == 'function' then",
            "Poker unique native host admission");
        text = Once(text,
            "if farWorldJob then farWorldJob.Resume() end\n        return true",
            "if farWorldJob then\n            if nearMode then farWorldJob.Pause() else farWorldJob.Resume() end\n        end\n        return true",
            "Poker source scanner fallback mode");
        return useCrlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    private static string UpgradeTunnelV2(string text)
    {
        var useCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        if (useCrlf) text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Once(text,
            "G-CET native location:tunnel-v1",
            "G-CET native location:tunnel-v2",
            "Tunnel native revision");
        text = Once(text,
            "local __gcetTunnelNativeFar = false",
            "local __gcetTunnelNativeOutside = nil",
            "Tunnel native UNKNOWN bootstrap");
        text = Once(text,
            "if __gcetTunnelNativeFar then return true end",
            "if __gcetTunnelNativeOutside ~= nil then return __gcetTunnelNativeOutside end",
            "Tunnel authoritative near/far result");
        text = Once(text,
            "function() __gcetTunnelNativeFar=false end,",
            "function() __gcetTunnelNativeOutside=false end,",
            "Tunnel known near");
        text = Once(text,
            "function() __gcetTunnelNativeFar=true end,",
            "function() __gcetTunnelNativeOutside=true end,",
            "Tunnel known far");
        text = Once(text,
            "function() __gcetTunnelNativeFar=false;return true end)",
            "function() __gcetTunnelNativeOutside=nil;return true end)",
            "Tunnel unknown returns original distance probe");
        return useCrlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    private static string UpgradeIllegalV2(string text)
    {
        var useCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        if (useCrlf) text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Once(text,
            "G-CET native location:illegal-v1",
            "G-CET native location:illegal-v2",
            "Mechanic native revision");

        // Native FAR is an aggregate discovery hint (100m), not a replacement
        // for the author's exact 75m spawn/95m despawn decisions for live NPCs.
        // Fast travel can make a 10Hz Lua-delivered transition briefly stale.
        // Retain original exact player-distance checks for all live entities,
        // pending spawns and uncertain entity states. Only skip never-spawned,
        // idle matrix entries while a valid native FAR is already known.
        text = Once(text,
            "    if entry.spawnPending then\n      resolvePendingSpawn(entry)\n    else\n      local distance",
            "    if state.__gcetNativeFar and not entry.spawnPending and entry.entityID == nil and entry.entity == nil then\n" +
            "      -- Far inactive site: no entity to maintain or despawn.\n" +
            "    elseif entry.spawnPending then\n      resolvePendingSpawn(entry)\n    else\n      local distance",
            "Mechanic safe distant inactive-site replacement");
        return useCrlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    private static string UpgradeDedkaV2(string text)
    {
        var useCrlf = text.Contains("\r\n", StringComparison.Ordinal);
        if (useCrlf) text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = Once(text,
            "G-CET native location:dedka-v1",
            "G-CET native location:dedka-v2",
            "Dedka native revision");
        // Both 200m zones are already retired by the source-proven v1 handler
        // after an authoritative owner snapshot. Do not replace the authored
        // 5Hz fine-detail job or invent an unsafe external wake.
        return useCrlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    internal static SemanticInjectionResult ApplyPoker(SemanticPatchContext context)
    {
        var structural=NativeLocationStructuralSource.TryPoker(context);
        if(structural is not null) {
            if(structural.Applied) SemanticInjectors.ApplyPokerNativeWorker(context);
            return structural;
        }
        var file = context.FindFile("init.lua", "poker.world_far", "poker.world_near",
            "poker.seat_near", "StreetTables.locations");
        if (file.Text.Contains("G-CET native location:poker-v2", StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native poker transition stage v2 already installed.");
        if (file.Text.Contains("G-CET native location:poker-v1", StringComparison.Ordinal))
        {
            context.Write(file, UpgradePokerV2(file.Text));
            return SemanticInjectionResult.Success(
                "Upgraded old poker location stage in place: native owns far discovery; source-owned near table/seat and active hand lanes remain.");
        }
        var text = file.Text;
        text = Once(text,
            "        local nearMode = false\n        local farWorldJob = nil",
            "        local nearMode = false\n        local __gcetNativeNear = false\n        local __gcetNativeOwned = false\n        local farWorldJob = nil", "Poker mode");
        text = Once(text,
            "                if farWorldJob then farWorldJob.Resume() end\n            end\n        end\n\n        farWorldJob = client.Schedule.Every(1.0,",
            "                if farWorldJob then\n                    if __gcetNativeOwned then farWorldJob.Pause() else farWorldJob.Resume() end\n                end\n                if __gcetNativeOwned and __gcetNativeNear and nearWorldJob then nearWorldJob.Resume() end\n            end\n        end\n\n        farWorldJob = client.Schedule.Every(1.0,", "Poker far-job resume");
        // First native NEAR may arrive during a loading/menu frame. The
        // already-existing realtime lane must rearm discovery once the
        // author's runtime-ready predicate becomes true without demanding
        // another boundary crossing or adding a new frame subscriber.
        text = Once(text,
            "            local active = pokerIsActive()\n\n            if (active or InteractionMenu.visible) and not nearMode then",
            "            local active = pokerIsActive()\n\n" +
            "            if __gcetNativeOwned and __gcetNativeNear and not nearMode then\n" +
            "                setNearMode(true)\n" +
            "            end\n\n" +
            "            if (active or InteractionMenu.visible) and not nearMode then",
            "Poker first-wake/game-ready reconciliation");
        text = Once(text,
            "            if not near then setNearMode(false) end\n        end)\n        table.insert(created, nearWorldJob)",
            "            if near then setNearMode(true) else setNearMode(false) end\n        end)\n        table.insert(created, nearWorldJob)", "Poker native near reentry");
        const string install = """
        -- G-CET native location:poker-v1
        -- The existing far scanner is retired ONLY after a valid native snapshot.
        local lookup = engine.GCET and engine.GCET.NativeLocationHost
        if lookup and type(lookup.attach) == 'function' and type(lookup.attach) == "function" then
            local sites = {}
            for _, loc in ipairs(StreetTables.locations) do
                sites[#sites+1] = {x=loc.center.x,y=loc.center.y,z=loc.floorZ,
                    radius=POKER_WAKE_RADIUS,metric="xyz"}
            end
            local function nativePokerNear()
                __gcetNativeOwned = true
                __gcetNativeNear = true
                if pokerRuntimeReady() then setNearMode(true) end
                if farWorldJob then farWorldJob.Pause() end
            end
            local function nativePokerFar()
                __gcetNativeOwned = true
                __gcetNativeNear = false
                if pokerRuntimeReady() then StreetTables.update(0, POKER_WAKE_RADIUS) end
                setNearMode((pokerIsActive() or InteractionMenu.visible) == true)
                if farWorldJob then farWorldJob.Pause() end
            end
            local function nativePokerFallback()
                __gcetNativeOwned = false
                __gcetNativeNear = false
                if farWorldJob then farWorldJob.Resume() end
                return true
            end
            lookup:attach(10031,sites,nativePokerNear,nativePokerFar,nativePokerFallback)
        end

        """;
        text = Once(text, "        pokerClient = client\n        pass4FarWorldJob = farWorldJob",
            install + "        pokerClient = client\n        pass4FarWorldJob = farWorldJob", "Poker native owner registration");
        // Source coordinates are in the generated GCAS manifest, not sent
        // through Lua on game start. The original far scanner stays standby.
        var result=UpgradePokerV2(text);
        result=Once(result,
            "            local sites = {}\n" +
            "            for _, loc in ipairs(StreetTables.locations) do\n" +
            "                sites[#sites+1] = {x=loc.center.x,y=loc.center.y,z=loc.floorZ,\n" +
            "                    radius=POKER_WAKE_RADIUS,metric=\"xyz\"}\n" +
            "            end\n",
            "","Poker remove Lua-side coordinate table");
        result=Once(result,
            "lookup:attach(10031,sites,nativePokerNear,nativePokerFar,nativePokerFallback)",
            "lookup:attachCompiled(10031,#StreetTables.locations,nativePokerNear,nativePokerFar,nativePokerFallback)",
            "Poker compiled native owner binding");
        result=Once(result,
            "if lookup and type(lookup.attach) == 'function' then",
            "if lookup and type(lookup.attachCompiled) == 'function' then",
            "Poker compiled host shape");
        context.Write(file,result);
        SemanticInjectors.ApplyPokerNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Native location v2 replaces the previous 1 Hz distant poker wake scanner after the first valid snapshot; table/seat and active gameplay work remain, original scanner is standby fallback.");
    }

    internal static SemanticInjectionResult ApplyTunnel(SemanticPatchContext context)
    {
        var file = context.FindFile("init.lua","local function __gcetTunnelFarOutside()",
            "Rescue.surface", "H.phase", "JournalOffer.update");
        if (file.Text.Contains("G-CET native location:tunnel-v2",StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native tunnel transition stage v2 already installed.");
        if (file.Text.Contains("G-CET native location:tunnel-v1",StringComparison.Ordinal))
        {
            context.Write(file, UpgradeTunnelV2(file.Text));
            return SemanticInjectionResult.Success(
                "Upgraded native quest distance to tri-state: both proven NEAR and FAR avoid Lua player-position reads; UNKNOWN retains original distance getter.");
        }
        var text = Once(file.Text,
            "local function __gcetTunnelFarOutside()\n",
            "local __gcetTunnelNativeFar = false\nlocal function __gcetTunnelFarOutside()\n", "Tunnel discovery gate");
        text = Once(text,
            " if fact('reunion_pending')==1 then return false end\n local site=L.rescue and L.rescue.surface",
            " if fact('reunion_pending')==1 then return false end\n if __gcetTunnelNativeFar then return true end\n local site=L.rescue and L.rescue.surface",
            "Tunnel protected native far result");
        const string setup = """
        -- G-CET native location:tunnel-v1
        -- Native replaces only the far-distance question, not quest timers.
        local function __gcetTunnelAttachNative()
            local ok, engine = pcall(GetMod,"0-Engine")
            local lookup = ok and engine and engine.GCET and engine.GCET.NativeLocationHost
            if not lookup or type(lookup.attach) ~= 'function' then return end
            local site=L.rescue and L.rescue.surface
            local giver=L.rescue and L.rescue.brother
            if type(site)~="table" or type(giver)~="table" then return end
            lookup:attachCompiled(10032,2,function() __gcetTunnelNativeFar=false end,
              function() __gcetTunnelNativeFar=true end,
              function() __gcetTunnelNativeFar=false;return true end)
        end
        registerForEvent("onInit",__gcetTunnelAttachNative)

        """;
        text = Once(text, "local __gcetTunnelOutsideAcc = 0.0\n",
            setup + "local __gcetTunnelOutsideAcc = 0.0\n", "Tunnel native attach");
        context.Write(file,UpgradeTunnelV2(text));
        SemanticInjectors.ApplyTunnelNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Native location v2 replaces both near and far distance probes after valid snapshots, preserving 150ms/500ms quest cadence, journal, input, recovery and original unknown fallback.");
    }

    internal static SemanticInjectionResult ApplyIllegal(SemanticPatchContext context)
    {
        var file = context.FindFile("physical_npc.lua",
            "function PhysicalNpc.Update(delta)","state.mechanics = result",
            "if entry.spawnPending then","loadMatrix()");
        if (file.Text.Contains("G-CET native location:illegal-v2",StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native mechanic stage v2 already installed.");
        if (file.Text.Contains("G-CET native location:illegal-v1",StringComparison.Ordinal))
        {
            context.Write(file, UpgradeIllegalV2(file.Text));
            return SemanticInjectionResult.Success(
                "Native FAR skips unspawned inactive sites even when another mechanic is live; live/pending NPCs retain exact original distance and cleanup.");
        }
        var text = Once(file.Text,
            "  nextUpdateAt = 0.0\n}",
            "  nextUpdateAt = 0.0,\n  __gcetNearByIndex = nil\n}", "Mechanic per-site supervisor state");
        text=Once(text,
            "      result[#result + 1] = {\n        index = index,",
            "      result[#result + 1] = {\n        nativeIndex = #result + 1,\n        index = index,",
            "Mechanic per-site dense index identity");
        const string attach = """
          -- G-CET native location:illegal-v1
          local engineOk,engine=pcall(GetMod,"0-Engine")
          local lookup=engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
          if lookup and type(lookup.attach) == 'function' then
            local sites={}
            for _,entry in ipairs(result) do
              sites[#sites+1]={x=entry.x,y=entry.y,z=entry.z,radius=100,metric="xyz"}
            end
            lookup:attachPerSite(10033,sites,
              function(index,near)
                state.__gcetNearByIndex=state.__gcetNearByIndex or {}
                state.__gcetNearByIndex[index]=near
                state.nextUpdateAt=0
              end,
              function()
                state.__gcetNearByIndex=nil
                state.nextUpdateAt=0
                return true
              end)
          end
        """;
        text = Once(text, "  state.mechanics = result\n  log(\"matrix loaded mechanics=\" .. tostring(#result))",
            "  state.mechanics = result\n" + attach + "  log(\"matrix loaded mechanics=\" .. tostring(#result))",
            "Mechanic matrix binding");
        const string gate = """
          if state.__gcetNativeFar then
            local hasLiveOrPending=false
            for _,entry in ipairs(state.mechanics) do
              if entry.spawnPending or entry.entityID ~= nil or entry.entity ~= nil then
                hasLiveOrPending=true;break
              end
            end
            if not hasLiveOrPending then return end
          end
        """;
        text = Once(text,
            "  if #state.mechanics == 0 and not loadMatrix() then return end\n  local player = getPlayer()",
            "  if #state.mechanics == 0 and not loadMatrix() then return end\n" + gate + "  local player = getPlayer()",
            "Mechanic inactive far guard");
        // Source identity remains per mechanic, not per owner. Native FAR
        // is only an idle-site hint; original live/pending NPC service stays.
        text=Once(text,
            "          if state.__gcetNativeFar then\n" +
            "            local hasLiveOrPending=false\n" +
            "            for _,entry in ipairs(state.mechanics) do\n" +
            "              if entry.spawnPending or entry.entityID ~= nil or entry.entity ~= nil then\n" +
            "                hasLiveOrPending=true;break\n" +
            "              end\n" +
            "            end\n" +
            "            if not hasLiveOrPending then return end\n" +
            "          end\n", "", "Remove aggregate mechanic FAR guard");
        text=Once(text,
            "    if entry.spawnPending then\n      resolvePendingSpawn(entry)\n    else\n      local distance",
            "    if state.__gcetNearByIndex and state.__gcetNearByIndex[entry.nativeIndex] == false and " +
                "not entry.spawnPending and entry.entityID == nil and entry.entity == nil then\n" +
            "      -- This idle FAR mechanic alone sleeps; other listeners run.\n" +
            "    elseif entry.spawnPending then\n      resolvePendingSpawn(entry)\n    else\n      local distance",
            "Mechanic independent listener eligibility");
        context.Write(file,text);
        return SemanticInjectionResult.Success(
            "Per-mechanic static native NEAR/FAR wakes each original 0.5-second listener independently; exact 75/95m, live NPCs, pending resolution and UNKNOWN fallback stay.");
    }


    private static string RestoreLegacyIllegalAggregateScanner(string source)
    {
        var crlf=source.Contains("\r\n",StringComparison.Ordinal);
        var text=crlf?source.Replace("\r\n","\n",StringComparison.Ordinal):source;
        // PASS11 used a single owner (10033) across all mechanics. Its
        // aggregate FAR gate can incorrectly park another mechanic. Remove
        // ONLY the independently proven injected aggregate wrapper, keeping
        // the original author NPC lifetimes and unrelated optimizations.
        var attachment=new Regex(
            @"(?ms)^[ \t]*-- G-CET native location:illegal-v[12]\n[\s\S]*?^[ \t]*end[ \t]*(?=log\()",
            RegexOptions.CultureInvariant);
        var far=new Regex(
            @"(?ms)^[ \t]*if state\.__gcetNativeFar then\s*local hasLiveOrPending=false[\s\S]*?^[ \t]*end[ \t]*(?=local player\s*=\s*getPlayer\(\))",
            RegexOptions.CultureInvariant);
        var binding=attachment.Matches(text);
        var guards=far.Matches(text);
        if(binding.Count!=1 || guards.Count!=1 ||
           !binding[0].Value.Contains("lookup:attach(10033,sites,",StringComparison.Ordinal) ||
           !binding[0].Value.Contains("state.__gcetNativeFar=true",StringComparison.Ordinal) ||
           !binding[0].Value.Contains("radius=100,metric=\"xyz\"",StringComparison.Ordinal) ||
           !guards[0].Value.Contains("if not hasLiveOrPending then return end",StringComparison.Ordinal) ||
           !text.Contains("cfg.spawnDistance",StringComparison.Ordinal) ||
           !text.Contains("cfg.despawnDistance",StringComparison.Ordinal) ||
           !text.Contains("resolvePendingSpawn(entry)",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Legacy aggregate mechanic wake is not source-proven; keep old gameplay unchanged.");
        text=attachment.Replace(text,"  ",1);
        text=far.Replace(text,"  ",1);
        if(text.Contains("state.__gcetNativeFar",StringComparison.Ordinal) ||
           text.Contains("lookup:attach(10033,",StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Legacy aggregate mechanic state remains; refuse conflicting native owners.");
        return crlf?text.Replace("\n","\r\n",StringComparison.Ordinal):text;
    }

    // Author fixed positions live in the REDscript registry, not the CET
    // matrix getter. Resolver extracts the registry once into GCAS and creates
    // one independent native owner per mechanic. Never let proximity for one
    // mechanic wake the other sites or bypass live-NPC cleanup.
    internal static SemanticInjectionResult ApplyIllegalV3(SemanticPatchContext context)
    {
        var file=context.FindFile("physical_npc.lua",
            "function PhysicalNpc.Update(delta)", "state.mechanics = result",
            "if entry.spawnPending then", "GetMechanicCountForCET",
            "cfg.spawnDistance", "cfg.despawnDistance");
        if(file.Text.Contains("G-CET native location:illegal-v3",StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Independent mechanic native wake is already installed.");
        var legacy=file.Text.Contains("G-CET native location:illegal-v1",StringComparison.Ordinal) ||
            file.Text.Contains("G-CET native location:illegal-v2",StringComparison.Ordinal);
        var input=legacy?RestoreLegacyIllegalAggregateScanner(file.Text):file.Text;
        var crlf=input.Contains("\r\n",StringComparison.Ordinal);
        var text=crlf ? input.Replace("\r\n","\n",StringComparison.Ordinal) : input;
        var helper="""
  -- G-CET native location:illegal-v3
  -- One compiled REDscript-backed site per mechanic: 1003301+registry index.
  -- Do not register positions from Lua. If absent, leave each site's author
  -- supervision active; only an authoritative FAR parks a dormant site.
  local engineOk,engine=pcall(GetMod,"0-Engine")
  local host=engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
  if host and type(host.attachCompiled)=="function" then
    for _,entry in ipairs(result) do
      local index=tonumber(entry.index)
      if index and index>=0 and index<256 then
        host:attachCompiled(1003301+index,1,
          function() entry.__gcetNativeFar=false;state.nextUpdateAt=0 end,
          function() entry.__gcetNativeFar=true;state.nextUpdateAt=0 end,
          function() entry.__gcetNativeFar=false;state.nextUpdateAt=0;return true end)
      end
    end
  end
""".Replace("\r\n","\n",StringComparison.Ordinal);
        text=Once(text,"  state.mechanics = result\n",
                  "  state.mechanics = result\n"+helper,
                  "Mechanic per-registry-index compiled listener subscription");
        text=Once(text,
                  "  if #state.mechanics == 0 and not loadMatrix() then return end\n"+
                  "  local player = getPlayer()",
                  "  if #state.mechanics == 0 and not loadMatrix() then return end\n"+
                  "  -- When all native-owned mechanics are FAR with no live/pending\n"+
                  "  -- actors, avoid even the per-cycle player getter. UNKNOWN\n"+
                  "  -- and failed owner bindings remain original-author active.\n"+
                  "  local __gcetAnySiteActive=false\n"+
                  "  for _,entry in ipairs(state.mechanics) do\n"+
                  "    if entry.__gcetNativeFar ~= true or entry.spawnPending or\n"+
                  "       entry.entityID ~= nil or entry.entity ~= nil then\n"+
                  "      __gcetAnySiteActive=true;break\n"+
                  "    end\n"+
                  "  end\n"+
                  "  if not __gcetAnySiteActive then return end\n"+
                  "  local player = getPlayer()",
                  "Mechanic native FAR only suppresses idle player queries");
        text=Once(text,
                  "    if entry.spawnPending then\n      resolvePendingSpawn(entry)\n    else\n      local distance",
                  "    if entry.__gcetNativeFar == true and not entry.spawnPending and\n"+
                  "       entry.entityID == nil and entry.entity == nil then\n"+
                  "      -- Dormant only for THIS mechanic; other mechanics proceed.\n"+
                  "    elseif entry.spawnPending then\n"+
                  "      resolvePendingSpawn(entry)\n"+
                  "    else\n      local distance",
                  "Mechanic exact original near NPC supervision per owner");
        context.Write(file,crlf ? text.Replace("\n","\r\n",StringComparison.Ordinal):text);
        SemanticInjectors.ApplyIllegalMechanicNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Each fixed REDscript mechanic receives its own compiled native owner with 110m+ discovery-safe wake; exact author 75/95m NPC streaming, live/pending cleanup and Redscript discovery listeners are unchanged.");
    }

    internal static SemanticInjectionResult ApplyDedka(SemanticPatchContext context)
    {
        var structural=NativeLocationStructuralSource.TryDedka(context);
        if(structural is not null) {
            if(structural.Applied) SemanticInjectors.ApplyDedkaNativeWorker(context);
            return structural;
        }
        var file=context.FindFile("init.lua","dedka_entrance1","dedka_entrance2_cluster",
            "stopShopDetail()", "startShopDetail()", "entrancePos_2");
        if(file.Text.Contains("G-CET native location:dedka-v2",StringComparison.Ordinal))
            return SemanticInjectionResult.Skip("Native Dedka zone stage v2 already installed.");
        if(file.Text.Contains("G-CET native location:dedka-v1",StringComparison.Ordinal))
        {
            context.Write(file, UpgradeDedkaV2(file.Text));
            return SemanticInjectionResult.Success(
                "Upgraded source-proven native zone ownership and fallback without replacing the nearby shop detail/interaction lifecycle.");
        }
        var text=Once(file.Text,
            "  engine.RegisterZone({\n    id = \"dedka_entrance1\",",
            "  local __gcetDedkaZone1 = engine.RegisterZone({\n    id = \"dedka_entrance1\",",
            "Dedka first zone handle");
        text=Once(text,
            "  engine.RegisterZone({\n    id = \"dedka_entrance2_cluster\",",
            "  local __gcetDedkaZone2 = engine.RegisterZone({\n    id = \"dedka_entrance2_cluster\",",
            "Dedka second zone handle");
        const string integration = """
          -- G-CET native location:dedka-v1
          -- Original zones remain alive until a complete native owner snapshot.
          local __gcetZone1=__gcetDedkaZone1
          local __gcetZone2=__gcetDedkaZone2
          local function __gcetRetireZones()
            if __gcetZone1 then __gcetZone1.unregister();__gcetZone1=nil end
            if __gcetZone2 then __gcetZone2.unregister();__gcetZone2=nil end
          end
          local function __gcetRestoreZones()
            if __gcetZone1 and __gcetZone2 then return true end
            shopNearCount=0
            stopShopDetail()
            if not __gcetZone1 then
              __gcetZone1=engine.RegisterZone({
                id="dedka_entrance1",x=entrancePos.x,y=entrancePos.y,z=entrancePos.z,
                radius=200,throttle=10,
                onEnter=function() shopNearCount=shopNearCount+1;startShopDetail() end,
                onExit=function()
                  shopNearCount=shopNearCount-1
                  if shopNearCount<=0 then shopNearCount=0;stopShopDetail() end
                end
              })
            end
            if not __gcetZone2 then
              __gcetZone2=engine.RegisterZone({
                id="dedka_entrance2_cluster",x=entrancePos_2.x,y=entrancePos_2.y,z=entrancePos_2.z,
                radius=200,throttle=10,
                onEnter=function() shopNearCount=shopNearCount+1;startShopDetail() end,
                onExit=function()
                  shopNearCount=shopNearCount-1
                  if shopNearCount<=0 then shopNearCount=0;stopShopDetail() end
                end
              })
            end
            return __gcetZone1~=nil and __gcetZone2~=nil
          end
          local lookup=engine.GCET and engine.GCET.NativeLocationHost
          if lookup and type(lookup.attach)=='function' then
            lookup:attachCompiled(10034,2,function()
              __gcetRetireZones();shopNearCount=1;startShopDetail()
            end,function()
              __gcetRetireZones();shopNearCount=0;stopShopDetail()
            end,function() return __gcetRestoreZones() end)
          end

        """;
        text=Once(text,
            "  -- vehicle mount/unmount events (replaces per-frame GetMountedVehicle polling)",
            integration + "  -- vehicle mount/unmount events (replaces per-frame GetMountedVehicle polling)",
            "Dedka native aggregate binding");
        context.Write(file,UpgradeDedkaV2(text));
        SemanticInjectors.ApplyDedkaNativeWorker(context);
        return SemanticInjectionResult.Success(
            "Native v2 owns both distant shop zones following a valid snapshot; original 0-Engine zones remain standby fallback and near shop detail/interaction events are unchanged.");
    }
}
