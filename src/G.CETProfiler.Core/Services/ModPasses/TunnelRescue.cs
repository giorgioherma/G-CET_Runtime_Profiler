using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplyTunnelRescue(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local function active()return fact('visit')==1 end",
            "local function update(dt)",
            "if H.tick<.15 then return end",
            "JournalOffer.update",
            "Rescue.surface",
            "Life.frame",
            "Swimming.frame",
            "Street.frame",
            "H.hooks:QuietTunnel",
            "local function navigationStatus()",
            "GetQuestMappinPosition(hash)",
            "system:GetAllMappins()");

        var text = file.Text;

        // v2 is deliberately upgrade-safe over the earlier tunnel semantic.
        // If the quest-session outer split is already present, retain it and
        // add only the newly proven diagnostic sentinel.
        if (!text.Contains("__gcetTunnelOutsideAcc", StringComparison.Ordinal))
        {
            text = RegexReplaceOnce(
                text,
                @"(?ms)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onUpdate[""']\s*,\s*function\s*\(\s*dt\s*\)\s*)\r?\n(?<body>.*?)(?<close>^[ \t]*end\s*\)\s*$)",
                @"local __gcetTunnelOutsideAcc = 0.0
local __gcetTunnelWasInside = false
${opening}
 local __gcetTunnelActive = H.phase ~= 'outside'
 if not __gcetTunnelActive then
  __gcetTunnelOutsideAcc = __gcetTunnelOutsideAcc + (dt or 0)
  if __gcetTunnelOutsideAcc < .15 then return end
  dt = math.min(__gcetTunnelOutsideAcc, .5)
  __gcetTunnelOutsideAcc = 0.0
 else
  __gcetTunnelOutsideAcc = 0.0
 end

 local ok,e=pcall(update,dt)

 if ok and H.phase~='outside' then
  if not H.lifeError then
   local lifeOK,lifeError=pcall(Life.frame,dt,H,L)
   if not lifeOK then H.lifeError=tostring(lifeError);pcall(Life.clear,H.hooks);print('[Below the Surface] Life layer: '..H.lifeError)end
  end
  if ok and valid(H.hooks)then ok,e=pcall(Swimming.frame,dt,H.hooks,H.session and H.phase=='inside'and H.blocked==nil and not H.overlay)end
  if ok and not H.streetError then
   local streetOK,streetError=pcall(Street.frame,dt,H,L)
   if not streetOK then H.streetError=tostring(streetError);pcall(Street.clear);print('[Below the Surface] Street layer: '..H.streetError)end
  end
 elseif __gcetTunnelWasInside then
  pcall(Life.clear,H.hooks)
  pcall(Swimming.clear,H.hooks)
  pcall(Street.clear)
 end

 local __gcetTunnelQuiet = ok and H.session and not H.error and not H.overlay
  and H.blocked~='Close the game menu' and (H.phase=='arriving' or H.phase=='inside')
 if valid(H.hooks)then H.hooks:QuietTunnel(__gcetTunnelQuiet)end
 __gcetTunnelWasInside = H.phase~='outside'

 if not ok then
  H.error=tostring(e);if H.error~=H.lastError then print('[Below the Surface] '..H.error);H.lastError=H.error end
  H.auto=false;H.manual=false
  if H.session and H.phase~='outside'then pcall(leave,'Tunnel interrupted. Returning to the saved departure.')end
  pcall(status)
 end
${close}" + "\n",
                "Tunnel quest-session outer dormancy");

            // Capture groups expand in RegexReplaceOnce, never as a later string substitution.

            text = RegexReplaceOnce(
                text,
                @"(?m)^(?<opening>[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*[""']onDraw[""']\s*,\s*function\s*\(\s*\)\s*)if\s+H\.session\s+and\s+not\s+H\.overlay\s+and\s+not\s+H\.error\s+then\s+Timer\.draw\s*\(\s*\)\s*end\s+end\s*\)\s*$",
                "${opening}if H.session and H.phase~='outside' and not H.overlay and not H.error then Timer.draw() end end)\n",
                "Tunnel draw outside-phase gate");
            // onDraw opening already expanded inside RegexReplaceOnce.
        }

        if (!text.Contains("__gcetTunnelNavCache", StringComparison.Ordinal))
        {
            text = ReplaceOnce(
                text,
                "local function navigationStatus()\n",
                "local __gcetTunnelNavCache={key=nil,mappins={}}\n" +
                "local function navigationStatus()\n",
                "Tunnel diagnostic cache state");

            text = ReplaceOnce(
                text,
                "  local owned={}\n",
                "  local owned={}\n" +
                "  local __gcetTunnelScanKey={tostring(H.phase or ''),tostring(H.generation or ''),tostring(result.trackedHash or '')}\n" +
                "  local __gcetTunnelNeedsFullScan=false\n",
                "Tunnel targeted diagnostic sentinel");

            text = ReplaceOnce(
                text,
                "    row.targetFound=found;if found and pos then row.position=point(pos)end\n",
                "    row.targetFound=found;if found and pos then row.position=point(pos)end\n" +
                "    __gcetTunnelScanKey[#__gcetTunnelScanKey+1]=table.concat({stage,tostring(hash),tostring(found==true),tostring(row.tracked==true)},':')\n" +
                "    if not found then __gcetTunnelNeedsFullScan=true end\n",
                "Tunnel targeted mappin state signature");

            var fullScan =
                "  for _,pin in ipairs(system:GetAllMappins())do\n" +
                "   if pin:IsQuestMappin()then\n" +
                "    local hash=pin:GetJournalPathHash()\n" +
                "    if owned[hash]then\n" +
                "     result.mappins[#result.mappins+1]={stage=owned[hash],hash=hash,position=point(pin:GetWorldPosition()),\n" +
                "      active=pin:IsActive(),visible=pin:IsVisible(),playerTracked=pin:IsPlayerTracked(),questImportant=pin:IsQuestImportant(),variant=tostring(pin:GetVariant())}\n" +
                "    end\n" +
                "   end\n" +
                "  end\n";

            var gatedScan =
                "  local __gcetTunnelKey=table.concat(__gcetTunnelScanKey,'|')\n" +
                "  if __gcetTunnelNeedsFullScan or __gcetTunnelNavCache.key~=__gcetTunnelKey then\n" +
                "   local __gcetTunnelMappins={}\n" +
                "   for _,pin in ipairs(system:GetAllMappins())do\n" +
                "    if pin:IsQuestMappin()then\n" +
                "     local hash=pin:GetJournalPathHash()\n" +
                "     if owned[hash]then\n" +
                "      __gcetTunnelMappins[#__gcetTunnelMappins+1]={stage=owned[hash],hash=hash,position=point(pin:GetWorldPosition()),\n" +
                "       active=pin:IsActive(),visible=pin:IsVisible(),playerTracked=pin:IsPlayerTracked(),questImportant=pin:IsQuestImportant(),variant=tostring(pin:GetVariant())}\n" +
                "     end\n" +
                "    end\n" +
                "   end\n" +
                "   __gcetTunnelNavCache.key=__gcetTunnelKey\n" +
                "   __gcetTunnelNavCache.mappins=__gcetTunnelMappins\n" +
                "  end\n" +
                "  result.mappins=__gcetTunnelNavCache.mappins or {}\n";

            text = ReplaceOnce(
                text,
                fullScan,
                gatedScan,
                "Tunnel global diagnostic mappin scan");
        }

        if (!text.Contains("__gcetTunnelMissingActive", StringComparison.Ordinal))
        {
            text = ReplaceOnce(text,
                "local __gcetTunnelNavCache={key=nil,mappins={}}",
                "local __gcetTunnelNavCache={key=nil,mappins={},missingAge=0,lastClock=nil}",
                "Tunnel v3 diagnostic retry clock");
            text = ReplaceOnce(text,
                "  local __gcetTunnelNeedsFullScan=false",
                "  local __gcetTunnelMissingActive=false",
                "Tunnel v3 missing active objective only");
            text = ReplaceOnce(text,
                "    __gcetTunnelScanKey[#__gcetTunnelScanKey+1]=table.concat({stage,tostring(hash),tostring(found==true),tostring(row.tracked==true)},':')\n" +
                "    if not found then __gcetTunnelNeedsFullScan=true end",
                "    __gcetTunnelScanKey[#__gcetTunnelScanKey+1]=table.concat({stage,tostring(hash),tostring(row.state or ''),tostring(found==true),tostring(row.tracked==true)},':')\n" +
                "    if not found and row.state==tostring(gameJournalEntryState.Active) then __gcetTunnelMissingActive=true end",
                "Tunnel v3 objective transition signature");
            text = ReplaceOnce(text,
                "  local __gcetTunnelKey=table.concat(__gcetTunnelScanKey,'|')\n" +
                "  if __gcetTunnelNeedsFullScan or __gcetTunnelNavCache.key~=__gcetTunnelKey then",
                "  local __gcetTunnelKey=table.concat(__gcetTunnelScanKey,'|')\n" +
                "  local __gcetTunnelNow=H.clock or 0\n" +
                "  local __gcetTunnelSince=math.max(0,__gcetTunnelNow-(__gcetTunnelNavCache.lastClock or __gcetTunnelNow))\n" +
                "  __gcetTunnelNavCache.lastClock=__gcetTunnelNow\n" +
                "  if __gcetTunnelMissingActive then\n" +
                "   __gcetTunnelNavCache.missingAge=__gcetTunnelNavCache.missingAge+__gcetTunnelSince\n" +
                "  else __gcetTunnelNavCache.missingAge=0 end\n" +
                "  if __gcetTunnelNavCache.key~=__gcetTunnelKey or (__gcetTunnelMissingActive and __gcetTunnelNavCache.missingAge>=12) then",
                "Tunnel v3 bounded world scan");
            text = ReplaceOnce(text,
                "   __gcetTunnelNavCache.mappins=__gcetTunnelMappins\n",
                "   __gcetTunnelNavCache.mappins=__gcetTunnelMappins\n" +
                "   __gcetTunnelNavCache.missingAge=0\n",
                "Tunnel v3 retry clock reset");
        }

        // 0.5 s full original controller only in stable distant outside state.
        // Keep journal, weather, status, input and presentation paths intact.
        // Near (<150m), active, returning, loading or uncertain -> original path.
        if (!text.Contains("__gcetTunnelFarOutside", StringComparison.Ordinal))
        {
            var probe =
                "local function __gcetTunnelFarOutside()\n" +
                " if H.phase~='outside' or not H.session or H.recover or H.error or not valid(H.hooks) then return false end\n" +
                " if H.generation~=H.hooks.generation or active() or Rescue.reunionPending or Rescue.reunionCompleted then return false end\n" +
                " if fact('reunion_pending')==1 then return false end\n" +
                " local site=L.rescue and L.rescue.surface\n" +
                " local giver=L.rescue and L.rescue.brother\n" +
                " if type(site)~='table' or type(giver)~='table' then return false end\n" +
                " local player=Game.GetPlayer()\n" +
                " if not valid(player) or not player:IsAttached() then return false end\n" +
                " local p=player:GetWorldPosition()\n" +
                " if not p then return false end\n" +
                " local sx=p.x-site[1];local sy=p.y-site[2];local sz=p.z-site[3]\n" +
                " local gx=p.x-giver[1];local gy=p.y-giver[2];local gz=p.z-giver[3]\n" +
                " return sx*sx+sy*sy+sz*sz>150*150 and gx*gx+gy*gy+gz*gz>150*150\n" +
                "end\n";
            text = ReplaceOnce(text,
                "local __gcetTunnelOutsideAcc = 0.0\n",
                probe + "local __gcetTunnelOutsideAcc = 0.0\n",
                "Tunnel far proximity wake probe");
            text = ReplaceOnce(text,
                "  if __gcetTunnelOutsideAcc < .15 then return end\n" +
                "  dt = math.min(__gcetTunnelOutsideAcc, .5)",
                "  if __gcetTunnelOutsideAcc < .15 then return end\n" +
                "  local __gcetTunnelProbeOK,__gcetTunnelFar=pcall(__gcetTunnelFarOutside)\n" +
                "  if __gcetTunnelProbeOK and __gcetTunnelFar and __gcetTunnelOutsideAcc < .5 then return end\n" +
                "  dt = math.min(__gcetTunnelOutsideAcc, .5)",
                "Tunnel far stable discovery cadence");
        }

        // The preceding v3 diagnostics wrote status.json every two seconds
        // even while the quest is unarmed, outside and not recovering. This
        // invokes quest-mappin diagnostics and native queries on a hot timer.
        // Reduce ONLY passive diagnostics (not controller discovery/cadence).
        // Completed/active/uncertain states keep the author's two seconds.
        if (!text.Contains("G-CET semantic:tunnel-rescue-quiet-idle-status-v4",
                           StringComparison.Ordinal))
        {
            var statusWrites = Regex.Matches(text,
                @"if[ \t]+H[.]diag\s*>\s*2[ \t]+then[ \t]+H[.]diag\s*=\s*0\s*;\s*status\s*\(\s*\)\s*end",
                RegexOptions.CultureInvariant);
            // Some synthetic/older supported quest versions omit this
            // *diagnostic writer* entirely. The v3 gameplay optimization may
            // still apply; the optional v4 diagnostic stage is simply absent.
            // Real author builds expose the final update() writer. Never
            // invent or reintroduce a periodic status writer.
            if (statusWrites.Count > 4)
                throw new InvalidOperationException(
                    "Tunnel diagnostic writer shape ambiguous; refused passive-status optimization.");
            if (statusWrites.Count > 0)
            {
                var match = statusWrites[^1];
                var onInit = Regex.Match(text,
                    @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\([ \t]*['""]onInit['""]",
                    RegexOptions.CultureInvariant);
                if (!onInit.Success || match.Index > onInit.Index)
                    throw new InvalidOperationException(
                        "Tunnel final update status write no longer provable.");
                text = text[..match.Index] +
                    "if H.diag>(H.phase=='outside' and H.session and not H.questArmed and " +
                    "not H.recover and not H.auto and not H.manual and not H.error and " +
                    "not active() and 8 or 2) then H.diag=0;status()end " +
                    "-- G-CET semantic:tunnel-rescue-quiet-idle-status-v4" +
                    text[(match.Index+match.Length)..];
            }
        }


        // The v4 diagnostic guard was functionally inert for an offered quest:
        // tunnel-journal.lua actively sets H.questArmed=true during journal
        // discovery. That does NOT mean the player entered/started the quest.
        // The fourth/fifth captures show a stable 2.55s JSON/status() spike
        // rhythm, including after the v4 deployment. Only slow the
        // non-gameplay diagnostic snapshot (not JournalOffer.update or
        // quest/response wake) while outside with no active visit/error/input.
        if (!text.Contains("G-CET semantic:tunnel-passive-status-v5",
                           StringComparison.Ordinal))
        {
            var oldGuard = Regex.Matches(text,
                @"H[.]diag\s*>\s*\(\s*H[.]phase\s*==\s*'outside'\s+and\s+H[.]session\s+and\s+not\s+H[.]questArmed\s+and\s+not\s+H[.]recover\s+and\s+not\s+H[.]auto\s+and\s+not\s+H[.]manual\s+and\s+not\s+H[.]error\s+and\s+not\s+active\s*\(\s*\)\s+and\s+8\s+or\s+2\s*\)",
                RegexOptions.CultureInvariant);
            if (oldGuard.Count > 1)
                throw new InvalidOperationException(
                    "Tunnel passive diagnostic guard is ambiguous; refusing v5 upgrade.");
            if (oldGuard.Count == 1)
            {
                var m = oldGuard[0];
                text = text[..m.Index] +
                    "H.diag>(H.phase=='outside' and H.session and " +
                    "not H.recover and not H.auto and not H.manual and not H.error and " +
                    "not active() and 60 or 2)" + text[(m.Index+m.Length)..];
                text = "-- G-CET semantic:tunnel-passive-status-v5\n" + text;
            }
            else if (text.Contains("G-CET semantic:tunnel-rescue-quiet-idle-status-v4",
                                   StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Tunnel v4 marker present but diagnostic source changed; refusing v5.");
            }
            // Older original fixtures without a periodic status writer are
            // allowed; never synthesize a new costly diagnostic callback.
        }

        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved tunnel quest-session realtime behavior and the author's 0.15 s outside cadence. The 2 s diagnostics path keeps targeted quest-mappin probes, but globally enumerates mappins only when the cheap state signature changes or a target is missing; only plain Lua diagnostic rows are cached.");
    }


}
