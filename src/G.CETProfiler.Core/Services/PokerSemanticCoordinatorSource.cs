using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Deployment-only extraction of the source-proven Poker PASS4 controller.
/// Runs AFTER semantic and native-location adapters, so native rewrites apply
/// to the authoritative inline scheduler before it moves to an owned chunk.
/// Does not infer any scheduling or gameplay semantics.
/// </summary>
internal static class PokerSemanticCoordinatorSource
{
    internal const string Module = "G-CET/Coordinator.lua";
    internal const string Owner = "StreetGamesPoker";
    internal const string Rule = "streetgamespoker";
    internal const string Marker = "-- G-CET semantic coordinator:streetgamespoker";
    internal const string BridgeMarker = "-- G-CET semantic coordinator bridge:streetgamespoker";

    private static readonly Regex SchedulerSection = new(
        @"(?m)^-- Distance-aware PASS4: author gameplay preserved, only discovery/work throttled\.[\s\S]*?(?=^[ \t]*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\()",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // The temporary INLINE source is never shipped if extraction fails.
    // Only this already-reviewed PASS4 scheduler may defer its intermediate
    // local budget to the mandatory whole-ZIP validation after extraction.
    internal static bool MayDeferIntermediateLocalBudget(
        string ruleId, string relativeFile, byte[] generatedBytes)
    {
        if (ruleId != Rule ||
            !relativeFile.Equals(Owner + "/init.lua",
                StringComparison.OrdinalIgnoreCase))
            return false;
        var text = Encoding.UTF8.GetString(generatedBytes);
        var hits = SchedulerSection.Matches(text);
        if (hits.Count != 1 ||
            text.Contains(BridgeMarker, StringComparison.Ordinal))
            return false;
        var body = hits[0].Value;
        return body.Contains("local __gcetPokerAttached=false", StringComparison.Ordinal) &&
            body.Contains("__gcetPokerAttachPass4=function()", StringComparison.Ordinal) &&
            body.Contains("Logger.info(\"pass4_scheduler_attached\"", StringComparison.Ordinal) &&
            Regex.Matches(body, @"\bclient\.Schedule\.Every\s*\(").Count == 3 &&
            Regex.Matches(body, @"\bclient\.Schedule\.EveryFrame\s*\(").Count == 1 &&
            new[] { "poker.world_far", "poker.world_near",
                    "poker.seat_near", "poker.realtime" }
                .All(id => Regex.Matches(body,
                    "id=[\"']" + Regex.Escape(id) + "[\"']").Count == 1);
    }

    internal static int TryStage(
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        // Do not mutate a pre-existing completed installation without a
        // semantic/native stage already composing its current live source.
        var stagedOwner = staged.Keys.Any(k =>
            k.Equals(Owner + "/init.lua", StringComparison.OrdinalIgnoreCase));
        if (!stagedOwner) return 0;

        var context = new SemanticPatchContext(modsRoot, Owner, Rule, staged);
        try
        {
            var init = context.FindFile("init.lua",
                "StreetGamesPoker.ready", "StreetTables.update",
                "SeatController.update", "poker.world_far", "poker.world_near",
                "poker.seat_near", "poker.realtime");
            if (init.Text.Contains(BridgeMarker, StringComparison.Ordinal))
                return 0;

            var hits = SchedulerSection.Matches(init.Text);
            if (hits.Count != 1)
                throw new InvalidOperationException(
                    "Poker PASS4 inline scheduler boundary is not unique.");
            var hit = hits[0];
            var scheduler = hit.Value.TrimEnd('\r', '\n', ' ', '\t');
            if (!scheduler.EndsWith("\nend", StringComparison.Ordinal) ||
                !scheduler.Contains("local __gcetPokerAttached=false", StringComparison.Ordinal) ||
                !scheduler.Contains("__gcetPokerAttachPass4=function()", StringComparison.Ordinal) ||
                !scheduler.Contains("return StreetGamesPoker.ready and not inMenu and inGame", StringComparison.Ordinal) ||
                !scheduler.Contains("  __gcetPokerAttached=true", StringComparison.Ordinal) ||
                !scheduler.Contains("Logger.info(\"pass4_scheduler_attached\"", StringComparison.Ordinal) ||
                Regex.Matches(scheduler, @"\bclient\.Schedule\.Every\s*\(").Count != 3 ||
                Regex.Matches(scheduler, @"\bclient\.Schedule\.EveryFrame\s*\(").Count != 1)
                throw new InvalidOperationException(
                    "Poker PASS4 scheduler has unreviewed lexical or registration drift.");

            var required = new[] {
                "poker.world_far", "poker.world_near", "poker.seat_near", "poker.realtime"
            };
            if (required.Any(id => Regex.Matches(scheduler,
                    "id=[\"']" + Regex.Escape(id) + "[\"']").Count != 1))
                throw new InvalidOperationException(
                    "Poker PASS4 job identity/count changed; refusing extraction.");

            // The coordinator deliberately keeps the exact four job bodies,
            // their startup transaction and native-location hooks. Only two
            // lexical references are bridged: mutable menu/game predicates and
            // the main-file attached guard.
            var relocated = scheduler
                .Replace("return StreetGamesPoker.ready and not inMenu and inGame",
                    "return StreetGamesPoker.ready and not __gcetBindings.inMenu() and __gcetBindings.inGame()",
                    StringComparison.Ordinal)
                .Replace("  __gcetPokerAttached=true",
                    "  __gcetPokerAttached=true\n  __gcetBindings.onAttached()",
                    StringComparison.Ordinal);
            var coordinator = Marker + "\n" +
                "return function(__gcetBindings)\n" +
                " local StreetGamesPoker=__gcetBindings.StreetGamesPoker\n" +
                " local StreetTables=__gcetBindings.StreetTables\n" +
                " local SeatController=__gcetBindings.SeatController\n" +
                " local InteractionMenu=__gcetBindings.InteractionMenu\n" +
                " local CardRenderer=__gcetBindings.CardRenderer\n" +
                " local TableTalk=__gcetBindings.TableTalk\n" +
                " local Cron=__gcetBindings.Cron\n" +
                " local Logger=__gcetBindings.Logger\n" +
                relocated + "\n" +
                " return __gcetPokerAttachPass4\n" +
                "end\n";

            // Preserve the one original onInit and onUpdate registrations.
            // Loading failures leave the original author update path active.
            // CET resolves dofile paths inside the calling mod's sandbox.
            // Never depend on debug.getinfo or a global require cache.
            const string bridge = """
-- G-CET semantic coordinator bridge:streetgamespoker
local __gcetPokerAttached=false
__gcetPokerAttachPass4=function()
 local ok,factory=pcall(dofile,"G-CET/Coordinator.lua")
 if not ok or type(factory)~="function" then return false end
 local bound,attach=pcall(factory,{
  StreetGamesPoker=StreetGamesPoker,StreetTables=StreetTables,
  SeatController=SeatController,InteractionMenu=InteractionMenu,
  CardRenderer=CardRenderer,TableTalk=TableTalk,Cron=Cron,Logger=Logger,
  inMenu=function() return inMenu end,
  inGame=function() return inGame end,
  onAttached=function() __gcetPokerAttached=true end
 })
 if not bound or type(attach)~="function" then return false end
 __gcetPokerAttachPass4=attach
 return __gcetPokerAttachPass4()
end

""";
            var updated = init.Text[..hit.Index] + bridge +
                init.Text[(hit.Index + hit.Length)..];
            if (SchedulerSection.IsMatch(updated) ||
                !updated.Contains("if __gcetPokerAttached then return end", StringComparison.Ordinal) ||
                !updated.Contains("if __gcetPokerAttachPass4 then __gcetPokerAttachPass4() end", StringComparison.Ordinal) ||
                Regex.Matches(updated, @"(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]").Count != 1)
                throw new InvalidOperationException(
                    "Poker bridge could not prove single callback and author fallback.");

            var initBudget = LuaLocalBudgetAnalyzer.Analyze(updated);
            var moduleBudget = LuaLocalBudgetAnalyzer.Analyze(coordinator);
            if (initBudget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit ||
                moduleBudget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit)
                throw new InvalidOperationException(
                    "External Poker coordinator failed Lua local-variable budget.");

            context.Write(init, updated);
            context.WriteOwnedModule(Module, coordinator);
            if (context.Changes.Count != 1 ||
                context.OwnedModules.Count > 1 ||
                context.ReadOwnedModule(Module) != coordinator)
                throw new InvalidOperationException(
                    "Poker bridge requires an identical installed or newly staged owned coordinator.");
            foreach (var change in context.Changes.Concat(context.OwnedModules))
            {
                var relative = change.RelativeFile.Replace('\\', '/');
                var livePath = Path.Combine(Path.GetFullPath(modsRoot),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (!liveSourceHashes.ContainsKey(relative))
                    liveSourceHashes[relative] = File.Exists(livePath)
                        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(livePath))).ToLowerInvariant()
                        : null;
                staged[relative] = change.Bytes;
                appliedTransformsByFile[relative] =
                    appliedTransformsByFile.TryGetValue(relative, out var count)
                        ? count + 1 : 1;
            }
            transformManifest.Add(new {
                type = "SEMANTIC_COORDINATOR_DEPLOYMENT",
                RuleId = Rule, Owner,
                files = context.Changes.Concat(context.OwnedModules)
                    .Select(c => c.RelativeFile.Replace('\\', '/')).ToArray(),
                sourceProof = true,
                note = "Relocated reviewed PASS4 jobs after native hooks; author fallback and existing callback registrations preserved."
            });
            return 1;
        }
        catch (Exception ex)
        {
            skipped.Add(new {
                type = "SEMANTIC_COORDINATOR_DEPLOYMENT",
                RuleId = Rule, Owner,
                reason = "Poker coordinator deployment refused; prior inline stage retained: " + ex.Message
            });
            return 0;
        }
    }
}
