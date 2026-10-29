using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

// Mod-owned source transformations. Shared proof/replace helpers remain in SemanticInjectors.cs.
internal static partial class SemanticInjectors
{
    private static SemanticInjectionResult ApplySpecterRecoveryPrerequisites(
        SemanticPatchContext context)
    {
        var file = context.FindFile(
            "init.lua",
            "local function metricGet(ns,key,default)",
            "local seq=metricGet(STORY_NS,",
            "specter_story_done_specter_rainbow_market_dev",
            "specter_story_done_recovery_counter_isolation",
            "if seq==9 and rainbowDone==1 and recoveryDone==0 then",
            "autoStartClock=autoStartClock+(delta or 0)");
        var text = file.Text;
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string Native(string source) => source.Replace("\n", newline, StringComparison.Ordinal);
        var oldReads = Native(
            "    local seq=metricGet(STORY_NS,\"specter_story_sequence\",0)\n" +
            "    local rainbowDone=metricGet(STORY_NS,\"specter_story_done_specter_rainbow_market_dev\",0)\n" +
            "    local recoveryDone=metricGet(STORY_NS,\"specter_story_done_recovery_counter_isolation\",0)");
        var safeReads = Native(
            "    local seq=metricGet(STORY_NS,\"specter_story_sequence\",0)\n" +
            "    local rainbowDone=seq==9 and metricGet(STORY_NS,\"specter_story_done_specter_rainbow_market_dev\",0) or 0\n" +
            "    local recoveryDone=(seq==9 and rainbowDone==1) and metricGet(STORY_NS,\"specter_story_done_recovery_counter_isolation\",0) or 0");

        if (text.Contains(oldReads, StringComparison.Ordinal))
            text = ReplaceOnce(text, oldReads, safeReads,
                "SPECTER M9 prerequisite-gated persistent metric reads");
        else if (!text.Contains(safeReads, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Recovery metric trio no longer matches supported author or optimized source.");

        const string musicCall =
            "Music.update(delta, phoneBus(), active and {id=\"recovery_counter_isolation\"} or nil, stage, active and \"recovery\" or \"idle\", Game.GetPlayer() ~= nil, active)";
        const string optimizedCall =
            "Music.update(delta, phoneBus(), active and __gcetRecoveryMusicContract or nil, stage, active and \"recovery\" or \"idle\", Game.GetPlayer() ~= nil, active)";
        // Source is already generic-rewritten: both admission and output
        // must use the SAME proven shared player getter, not a pre-generic
        // version-specific Music.update literal.
        var currentMusicCall=PreserveGenericGetters(text,musicCall);
        var currentOptimizedCall=PreserveGenericGetters(text,optimizedCall);
        var currentBusGatedMusicCall=PreserveGenericGetters(text,
            optimizedCall.Replace("Music.update(delta, phoneBus(),",
                "Music.update(delta, (active or Music.owner ~= nil or next(Music.pending) ~= nil) and phoneBus() or nil,",
                StringComparison.Ordinal));
        if (text.Contains(currentMusicCall, StringComparison.Ordinal))
        {
            var music = context.FindFile(
                "specter_mission_music.lua",
                "function M.update(delta,bus,contract,stage,state,playerPresent,arrived)",
                "local id=contract and contract.id");
            if (music.Text.Replace("contract.id", "", StringComparison.Ordinal)
                    .Contains("contract.", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Recovery music consumer references additional contract fields.");
            if (!text.Contains("local Music = require(\"specter_mission_music\")", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Recovery music module initializer changed.");
            text = ReplaceOnce(text,
                "local Music = require(\"specter_mission_music\")",
                Native("local Music = require(\"specter_mission_music\")\n" +
                       "local __gcetRecoveryMusicContract = {id=\"recovery_counter_isolation\"}"),
                "SPECTER module-local immutable music contract");
            text = ReplaceOnce(text, currentMusicCall, currentOptimizedCall,
                "SPECTER per-frame music contract reuse");
        }
        else if (text.Contains("Music.update(", StringComparison.Ordinal) &&
                 !text.Contains(currentOptimizedCall, StringComparison.Ordinal) &&
                 !text.Contains(currentBusGatedMusicCall, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Recovery music update changed shape; refusing source rewrite.");

        // M11 HUD.Update returns without side effects while HUD.active is false.
        // Keep the author's Show/Hide/expiry clock and active presentation path:
        // the only new condition avoids a no-op protected call on dormant frames.
        // Separate author HUD source must prove the guard, not just a legacy marker.
        if (text.Contains("present(\"HUD update\",HUD.Update)", StringComparison.Ordinal))
        {
            context.FindFile("m11_hud.lua",
                "function HUD.Update()",
                "if HUD.active and Game.GetEngineTime():ToFloat()",
                "function HUD.IsActive()",
                "return HUD.active == true");
            text = GateSpecterHudWhenActive(text);
        }

        // This is the existing SPECTER source generator: no separate
        // corrective semantic pass and no new timer or worker.
        text = ReduceSpecterRecoveryColdAllocations(text, context);

        if (text == file.Text)
            return SemanticInjectionResult.Skip(
                "Recovery source already satisfies metric and music postconditions.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved same-frame story detection, 0.75-second activation timing, " +
            "restore, music timing and dialogue; skipped unneeded metric reads " +
            "and reused the immutable 1.5 music contract.");
    }

    // The recovery chapter must recognize M9->M10 within the SAME frame.
    // We leave that query cadence and the 0.75s arm untouched, but the
    // idle metric getter need not also fetch the unrelated InfiniteGigs
    // owner on every invocation: its only consumer explicitly calls refresh()
    // after the M9+M9-done+not-recovered prerequisite becomes true.
    private static string ReduceSpecterRecoveryColdAllocations(
        string source, SemanticPatchContext context)
    {
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string metricMarker = "__gcetSpecterMetricCoreOnlyV1";
        const string originalMetric =
            "local function metricGet(ns,key,default)\n" +
            "  refresh()\n";
        const string optimizedMetric =
            "local function metricGet(ns,key,default)\n" +
            "  -- " + metricMarker + ": resolve only GigCore on this story read.\n" +
            "  if not core then\n" +
            "    local ok, found = pcall(GetMod, \"GigCore\")\n" +
            "    if ok then core = found end\n" +
            "  end\n";
        const string oldState =
            "pcall(function() return core.State.get(ns,key,default) end)";
        const string newState = "pcall(core.State.get,ns,key,default)";
        const string oldBus =
            "pcall(function() return core.Bus.getMetric(ns,key,default) end)";
        const string newBus = "pcall(core.Bus.getMetric,ns,key,default)";
        var hasFullMetric = text.Contains(
            "local function metricGet(ns,key,default)\n", StringComparison.Ordinal);
        if (hasFullMetric)
        {
            if (text.Contains(metricMarker, StringComparison.Ordinal))
            {
                if (text.Split(optimizedMetric, StringSplitOptions.None).Length != 2 ||
                    text.Split(newState, StringSplitOptions.None).Length != 2 ||
                    text.Split(newBus, StringSplitOptions.None).Length != 2 ||
                    text.Contains(oldState, StringComparison.Ordinal) ||
                    text.Contains(oldBus, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "SPECTER metric core-only stage exists but its native error fallbacks drifted.");
            }
            else
            {
                var ownerRefresh = "local function refresh()\n" +
                    "  if not core then pcall(function() core=GetMod(\"GigCore\") end) end\n" +
                    "  pcall(function() owner=GetMod(\"InfiniteGigs\") end)\n" +
                    "end";
                if (text.Split(originalMetric, StringSplitOptions.None).Length != 2 ||
                    text.Split(ownerRefresh, StringSplitOptions.None).Length != 2 ||
                    text.Split(oldState, StringSplitOptions.None).Length != 2 ||
                    text.Split(oldBus, StringSplitOptions.None).Length != 2 ||
                    !text.Contains("if seq==9 and rainbowDone==1 and recoveryDone==0 then",
                        StringComparison.Ordinal) ||
                    !text.Contains("      refresh()\n      if autoStartClock>=0.75",
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "SPECTER same-frame metric/prerequisite/owner reactivation source drifted.");
                text = text.Replace(originalMetric, optimizedMetric,
                                   StringComparison.Ordinal)
                           .Replace(oldState, newState, StringComparison.Ordinal)
                           .Replace(oldBus, newBus, StringComparison.Ordinal);
            }
        }
        else if (text.Contains(metricMarker, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SPECTER core-only marker has no actual metric getter.");

        // The authored music module exports M.owner and M.pending, and
        // M.update consults its bus only while playing or flushing pending
        // stopped events. Do not fetch PhoneSystem on fully dormant frames.
        // NEVER gate the Music.update callback, active chapter, or pending
        // stop retries. Compact/older variants without a source-proven
        // module retain their existing music behavior unchanged.
        const string musicMarker = "__gcetSpecterIdleMusicBusV1";
        var ownedMusic = context.TryReadComposedModFile(
            context.Owner, "specter_mission_music.lua");
        var musicText = ownedMusic is null ? "" :
            ownedMusic.Replace("\r\n", "\n", StringComparison.Ordinal);
        var provenMusic = musicText.Contains("pending={}", StringComparison.Ordinal) &&
                          musicText.Contains("local function flush(bus)", StringComparison.Ordinal) &&
                          musicText.Contains("function M.update(delta,bus,contract,stage,state,playerPresent,arrived)",
                              StringComparison.Ordinal) &&
                          musicText.Contains("if M.owner then M.stop(bus) else flush(bus) end",
                              StringComparison.Ordinal);
        const string oldMusicPrefix = "Music.update(delta, phoneBus(),";
        const string newMusicPrefix =
            "Music.update(delta, (active or Music.owner ~= nil or next(Music.pending) ~= nil) and phoneBus() or nil,";
        if (text.Contains(musicMarker, StringComparison.Ordinal))
        {
            if (!provenMusic ||
                text.Split(newMusicPrefix, StringSplitOptions.None).Length != 2 ||
                text.Contains(oldMusicPrefix, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "SPECTER music bus was gated without an owned pending-stop source proof.");
        }
        else if (provenMusic && text.Contains(oldMusicPrefix, StringComparison.Ordinal))
        {
            if (text.Split(oldMusicPrefix, StringSplitOptions.None).Length != 2 ||
                !text.Contains("active and __gcetRecoveryMusicContract or nil",
                    StringComparison.Ordinal) ||
                !text.Contains("registerForEvent(\"onSessionStart\",function()",
                    StringComparison.Ordinal) ||
                !text.Contains("  Music.stop(phoneBus())",
                    StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "SPECTER music pending-stop/active story contract changed.");
            text = text.Replace(oldMusicPrefix, newMusicPrefix,
                StringComparison.Ordinal);
            text = text.Replace(
                "  " + newMusicPrefix,
                "  -- " + musicMarker + ": keep music stop retries, skip idle PhoneSystem fetch.\n" +
                "  " + newMusicPrefix,
                StringComparison.Ordinal);
        }
        else if (provenMusic && text.Contains("Music.update(", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "SPECTER proven music module exists but update call source drifted.");
        return nl == "\r\n" ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text;
    }

    // Catalog verifier: compact older editions have no internal metric
    // owner/native fallback and need only their authored prerequisite stage;
    // newer editions require all actually present source-owned producers.
    internal static bool IsSpecterRecoveryColdGcComplete(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "init.lua");
            var text = File.ReadAllText(path).Replace("\r\n", "\n",
                StringComparison.Ordinal);
            if (!text.Contains(
                    "local rainbowDone=seq==9 and metricGet(STORY_NS,",
                    StringComparison.Ordinal) ||
                !text.Contains(
                    "local recoveryDone=(seq==9 and rainbowDone==1) and metricGet(STORY_NS,",
                    StringComparison.Ordinal))
                return false;
            if (Regex.IsMatch(text,
                    @"(?m)^[ \t]*present\(""HUD update"",HUD\.Update\)",
                    RegexOptions.CultureInvariant) ||
                text.Contains("active and {id=\"recovery_counter_isolation\"} or nil",
                    StringComparison.Ordinal) ||
                (text.Contains("Music.update(", StringComparison.Ordinal) &&
                 !text.Contains("local __gcetRecoveryMusicContract = {id=\"recovery_counter_isolation\"}",
                     StringComparison.Ordinal)))
                return false;
            if (text.Contains("HUD update", StringComparison.Ordinal) &&
                Regex.Matches(text,
                    @"if HUD\.IsActive\(\) then present\(""HUD update"",HUD\.Update\) end",
                    RegexOptions.CultureInvariant).Count != 2)
                return false;
            if (text.Contains("local function metricGet(ns,key,default)\n",
                    StringComparison.Ordinal) &&
                (!text.Contains("__gcetSpecterMetricCoreOnlyV1",
                    StringComparison.Ordinal) ||
                 !text.Contains("pcall(core.State.get,ns,key,default)",
                    StringComparison.Ordinal) ||
                 !text.Contains("pcall(core.Bus.getMetric,ns,key,default)",
                    StringComparison.Ordinal)))
                return false;
            var musicPath = Path.Combine(folder, "specter_mission_music.lua");
            if (File.Exists(musicPath))
            {
                var music = File.ReadAllText(musicPath);
                if (music.Contains("pending={}", StringComparison.Ordinal) &&
                    music.Contains("local function flush(bus)", StringComparison.Ordinal) &&
                    music.Contains("if M.owner then M.stop(bus) else flush(bus) end",
                        StringComparison.Ordinal) &&
                    text.Contains("Music.update(", StringComparison.Ordinal) &&
                    (!text.Contains("__gcetSpecterIdleMusicBusV1",
                        StringComparison.Ordinal) ||
                     !text.Contains("next(Music.pending) ~= nil) and phoneBus() or nil",
                        StringComparison.Ordinal)))
                    return false;
            }
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    // Source-independent stage test is run on pristine and previously routed
    // variants. Only the two authored HUD update sites are eligible.
    private static string GateSpecterHudWhenActive(string text)
    {
        const string guarded = "if HUD.IsActive() then present(\"HUD update\",HUD.Update) end";
        var oldPattern = new Regex(
            @"(?m)^(?<indent>[ \t]*)present\(""HUD update"",HUD\.Update\)[ \t]*(?<cr>\r?)$",
            RegexOptions.CultureInvariant);
        var guardedPattern = new Regex(
            @"(?m)^[ \t]*if HUD\.IsActive\(\) then present\(""HUD update"",HUD\.Update\) end[ \t]*\r?$",
            RegexOptions.CultureInvariant);
        var oldCount = oldPattern.Matches(text).Count;
        var guardedCount = guardedPattern.Matches(text).Count;
        if (oldCount == 0 && guardedCount == 2) return text;
        if (oldCount != 2 || guardedCount != 0)
            throw new InvalidOperationException(
                "Specter HUD lifecycle source changed; refusing dormant HUD stage.");
        return oldPattern.Replace(text, m => m.Groups["indent"].Value + guarded + m.Groups["cr"].Value);
    }

}
