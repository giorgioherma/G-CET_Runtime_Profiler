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
        return text;
    }

    private static string UpgradeTunnelV2(string text)
    {
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
        return text;
    }

    private static string UpgradeIllegalV2(string text)
    {
        text = Once(text,
            "G-CET native location:illegal-v1",
            "G-CET native location:illegal-v2",
            "Mechanic native revision");
        // 100m native FAR exceeds the original 95m despawn threshold.
        // If ANY source/streaming state is unknown, retain the original scan.
        // Pending spawns always continue their original resolution path.
        var oldBlock = new Regex(
            @"(?s)  if state\.__gcetNativeFar then\s+local hasLiveOrPending=false\s+" +
            @"for _,entry in ipairs\(state\.mechanics\) do\s+" +
            @"if entry\.spawnPending or entry\.entityID ~= nil or entry\.entity ~= nil then\s+" +
            @"hasLiveOrPending=true;break\s+end\s+end\s+" +
            @"if not hasLiveOrPending then return end\s+end\s+" +
            @"local player = getPlayer\(\)",
            RegexOptions.CultureInvariant);
        if (oldBlock.Matches(text).Count != 1)
            throw new InvalidOperationException(
                "Mechanic v1 FAR/live/pending ownership no longer uniquely proven.");
        const string replacement = """
          -- G-CET native FAR is strictly beyond the author's 95m despawn band.
          -- Resolve pending spawns as authored; live NPCs may now be retired
          -- without another Game.GetPlayer/GetWorldPosition/Vector4.Distance.
          -- Uncertain entity lifetime fails open to the exact original scan.
          if state.__gcetNativeFar then
            local needsOriginal=false
            for _,entry in ipairs(state.mechanics) do
              if entry.spawnPending then
                resolvePendingSpawn(entry)
              elseif entry.entityID ~= nil then
                deleteEntry(entry, "player left spawn radius (native FAR)")
              elseif entry.entity ~= nil then
                needsOriginal=true
              end
            end
            if not needsOriginal then return end
          end
          local player = getPlayer()
          """;
        return oldBlock.Replace(text,replacement,1);
    }

    private static string UpgradeDedkaV2(string text)
    {
        text = Once(text,
            "G-CET native location:dedka-v1",
            "G-CET native location:dedka-v2",
            "Dedka native revision");
        // Both 200m zones are already retired by the source-proven v1 handler
        // after an authoritative owner snapshot. Do not replace the authored
        // 5Hz fine-detail job or invent an unsafe external wake.
        return text;
    }

    internal static SemanticInjectionResult ApplyPoker(SemanticPatchContext context)
    {
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
        context.Write(file, UpgradePokerV2(text));
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
            lookup:attach(10032,{
                {x=site[1],y=site[2],z=site[3],radius=150,metric="xyz"},
                {x=giver[1],y=giver[2],z=giver[3],radius=150,metric="xyz"}
            },function() __gcetTunnelNativeFar=false end,
              function() __gcetTunnelNativeFar=true end,
              function() __gcetTunnelNativeFar=false;return true end)
        end
        registerForEvent("onInit",__gcetTunnelAttachNative)

        """;
        text = Once(text, "local __gcetTunnelOutsideAcc = 0.0\n",
            setup + "local __gcetTunnelOutsideAcc = 0.0\n", "Tunnel native attach");
        context.Write(file,UpgradeTunnelV2(text));
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
                "Native FAR now resolves pending mechanics and despawns established out-of-range NPCs without repeated position/vector scans; near and UNKNOWN retain exact source decisions.");
        }
        var text = Once(file.Text,
            "  nextUpdateAt = 0.0\n}",
            "  nextUpdateAt = 0.0,\n  __gcetNativeFar = false\n}", "Mechanic supervisor state");
        const string attach = """
          -- G-CET native location:illegal-v1
          local engineOk,engine=pcall(GetMod,"0-Engine")
          local lookup=engineOk and engine and engine.GCET and engine.GCET.NativeLocationHost
          if lookup and type(lookup.attach) == 'function' then
            local sites={}
            for _,entry in ipairs(result) do
              sites[#sites+1]={x=entry.x,y=entry.y,z=entry.z,radius=100,metric="xyz"}
            end
            lookup:attach(10033,sites,
              function() state.__gcetNativeFar=false;state.nextUpdateAt=0 end,
              function() state.__gcetNativeFar=true;state.nextUpdateAt=0 end,
              function() state.__gcetNativeFar=false;state.nextUpdateAt=0;return true end)
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
        context.Write(file,UpgradeIllegalV2(text));
        return SemanticInjectionResult.Success(
            "Native v2 FAR replaces matrix distance polling, including safe live-NPC despawn and pending resolution; exact 75/95m decisions, live entities and UNKNOWN fallback remain source-owned.");
    }

    internal static SemanticInjectionResult ApplyDedka(SemanticPatchContext context)
    {
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
            lookup:attach(10034,{
              {x=entrancePos.x,y=entrancePos.y,z=entrancePos.z,radius=200,metric="xyz"},
              {x=entrancePos_2.x,y=entrancePos_2.y,z=entrancePos_2.z,radius=200,metric="xyz"}
            },function()
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
        return SemanticInjectionResult.Success(
            "Native v2 owns both distant shop zones following a valid snapshot; original 0-Engine zones remain standby fallback and near shop detail/interaction events are unchanged.");
    }
}
