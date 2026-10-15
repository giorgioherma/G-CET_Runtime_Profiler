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
        if (text.Contains(musicCall, StringComparison.Ordinal))
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
            text = ReplaceOnce(text, musicCall, optimizedCall,
                "SPECTER per-frame music contract reuse");
        }
        else if (text.Contains("Music.update(", StringComparison.Ordinal) &&
                 !text.Contains(optimizedCall, StringComparison.Ordinal))
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

        if (text == file.Text)
            return SemanticInjectionResult.Skip(
                "Recovery source already satisfies metric and music postconditions.");
        context.Write(file, text);
        return SemanticInjectionResult.Success(
            "Preserved same-frame story detection, 0.75-second activation timing, " +
            "restore, music timing and dialogue; skipped unneeded metric reads " +
            "and reused the immutable 1.5 music contract.");
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
