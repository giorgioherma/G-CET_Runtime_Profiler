using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Extracts previously source-proven A/B/C Lua work into owner-local coordinators.
/// Only installed owners are considered. Unknown source shapes remain untouched;
/// extraction must never reject or roll back a separately successful semantic pass.
/// </summary>
internal static class LiveStackSemanticCoordinatorSource
{
    private const string OwnedPath = "G-CET/Coordinator.lua";
    private const string Prefix = "-- G-CET semantic coordinator:";
    private const string Bridge = "-- G-CET semantic coordinator bridge:";

    internal static int TryStage(
        string modsRoot,
        Dictionary<string, byte[]> staged,
        Dictionary<string, string?> liveSourceHashes,
        Dictionary<string, int> appliedTransformsByFile,
        List<object> transformManifest,
        List<object> skipped)
    {
        var applied = 0;
        foreach (var (owner, rule) in new[]
        {
            ("QuestRunner", "questrunner"),
            ("sitAnywhere", "sitanywhere")
        })
        {
            // This is deliberately based on PRESENT mod folders, not on the
            // semantic catalog, a synthetic fixture or another user's stack.
            var folder = Directory.EnumerateDirectories(modsRoot)
                .FirstOrDefault(dir => Path.GetFileName(dir)
                    .Equals(owner, StringComparison.OrdinalIgnoreCase));
            if (folder is null || !File.Exists(Path.Combine(folder, "init.lua")))
                continue;

            var context = new SemanticPatchContext(modsRoot, owner, rule, staged);
            try
            {
                var file = context.FindFile("init.lua",
                    $"G-CET semantic:{rule}");
                if (file.Text.Contains(Bridge + rule, StringComparison.Ordinal))
                {
                    var installed = context.ReadOwnedModule(OwnedPath);
                    if (installed is null ||
                        !installed.StartsWith(Prefix + rule + "\n", StringComparison.Ordinal) &&
                        !installed.StartsWith(Prefix + rule + "\r\n", StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "Bridge exists but its owned coordinator is missing or changed.");
                    continue;
                }

                var result = rule == "questrunner"
                    ? QuestRunner(file.Text)
                    : SitAnywhere(file.Text);
                if (result is null)
                    continue; // Existing semantic stage is not present; not an error.

                context.Write(file, result.Value.UpdatedSource);
                context.WriteOwnedModule(OwnedPath, result.Value.Module);
                if (context.Changes.Count != 1 ||
                    context.ReadOwnedModule(OwnedPath) != result.Value.Module ||
                    !context.Changes.Single().RelativeFile.EndsWith("/init.lua",
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "One author bridge and one owned module were not proven.");

                var all = context.Changes.Concat(context.OwnedModules).ToArray();
                foreach (var entry in all)
                {
                    var budget = LuaLocalBudgetAnalyzer.Analyze(entry.Bytes);
                    if (budget.MaxActiveLocals > LuaLocalBudgetAnalyzer.Limit)
                        throw new InvalidOperationException(
                            "Coordinator or author bridge exceeds Lua active-local budget.");
                }

                foreach (var entry in all)
                {
                    var path = entry.RelativeFile.Replace('\\', '/');
                    var live = Path.Combine(Path.GetFullPath(modsRoot),
                        path.Replace('/', Path.DirectorySeparatorChar));
                    if (!liveSourceHashes.ContainsKey(path))
                        liveSourceHashes[path] = File.Exists(live)
                            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(live)))
                                .ToLowerInvariant()
                            : null;
                    // SHA is diagnostic provenance only, NEVER a Lua version gate.
                    staged[path] = entry.Bytes;
                    appliedTransformsByFile[path] =
                        appliedTransformsByFile.TryGetValue(path, out var count)
                        ? count + 1 : 1;
                }
                transformManifest.Add(new
                {
                    type = "SEMANTIC_COORDINATOR_DEPLOYMENT",
                    RuleId = rule,
                    Owner = owner,
                    files = all.Select(e => e.RelativeFile.Replace('\\', '/')).ToArray(),
                    sourceProof = true,
                    note = "Relocated verified existing Lua work. Single author callback, unchanged active scheduler and original author-work fallback."
                });
                applied++;
            }
            catch (Exception ex)
            {
                skipped.Add(new
                {
                    type = "SEMANTIC_COORDINATOR_DEPLOYMENT",
                    RuleId = rule,
                    Owner = owner,
                    reason = "Coordinator extraction refused; existing inline work retained: " +
                        ex.Message
                });
            }
        }
        return applied;
    }

    private static (string UpdatedSource, string Module)? QuestRunner(string original)
    {
        if (!original.Contains("__gcetQuestRunnerManagerElapsed", StringComparison.Ordinal) ||
            !original.Contains("__gcetQuestRunnerSpawnerElapsed", StringComparison.Ordinal))
            return null;
        var crlf = original.Contains("\r\n", StringComparison.Ordinal);
        var text = original.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string state =
            "local __gcetQuestRunnerManagerElapsed = 0.0\n" +
            "local __gcetQuestRunnerSpawnerElapsed = 0.0\n\n";
        const string managerExpected = """
if Manager.current then
	__gcetQuestRunnerManagerElapsed = 0.0
	Manager:update(dt)
else
	__gcetQuestRunnerManagerElapsed = __gcetQuestRunnerManagerElapsed + (tonumber(dt) or 0)
	if __gcetQuestRunnerManagerElapsed >= 0.20 then
		local __gcetElapsed = __gcetQuestRunnerManagerElapsed
		__gcetQuestRunnerManagerElapsed = 0.0
		Manager:update(__gcetElapsed)
	end
end
""";
        const string spawnerExpected = """
__gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed + (tonumber(dt) or 0)
if __gcetQuestRunnerSpawnerElapsed >= 0.25 then
	local __gcetElapsed = __gcetQuestRunnerSpawnerElapsed
	__gcetQuestRunnerSpawnerElapsed = 0.0
	Spawner.Update(__gcetElapsed)
end
""";
        var manager = UniqueBlock(text,
            @"(?m)^(?<indent>[ \t]*)if Manager\.current then\n[\s\S]*?^\k<indent>end[ \t]*$",
            "QuestRunner Manager active/idle lane");
        var spawner = UniqueBlock(text,
            @"(?m)^(?<indent>[ \t]*)__gcetQuestRunnerSpawnerElapsed\s*=\s*__gcetQuestRunnerSpawnerElapsed\s*\+[\s\S]*?^\k<indent>end[ \t]*$",
            "QuestRunner Spawner lane");
        if (Dedent(manager) != managerExpected.TrimEnd('\n') ||
            Dedent(spawner) != spawnerExpected.TrimEnd('\n'))
            throw new InvalidOperationException("QuestRunner exact optimized lane bodies changed.");
        if (Regex.Matches(text, Regex.Escape(state)).Count != 1 ||
            manager.Index >= spawner.Index ||
            Regex.Matches(text, @"(?m)^\s*(?:registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]").Count != 1)
            throw new InvalidOperationException("QuestRunner state or update registration is ambiguous.");

        var module = Prefix + "questrunner\n" +
            "return function()\n" +
            " local __gcetQuestRunnerManagerElapsed=0.0\n" +
            " local __gcetQuestRunnerSpawnerElapsed=0.0\n" +
            " local function manager(dt,Manager)\n" + manager.Value + "\n end\n" +
            " local function spawner(dt,Spawner)\n" + spawner.Value + "\n end\n" +
            " return manager,spawner\nend\n";
        // The source's original gameplay cadence is always available when a
        // coordinator was deleted from an incomplete/manual installation.
        const string managerCall = """
if __gcetQuestManagerGate then
    __gcetQuestManagerGate(dt,Manager)
else
    Manager:update(dt)
end
""";
        const string spawnerCall = """
if __gcetQuestSpawnerGate then
    __gcetQuestSpawnerGate(dt,Spawner)
else
    Spawner.Update(dt)
end
""";
        // Source offsets are re-resolved after each edit: no guessed replacement positions.
        text = ReplaceOnce(text, manager.Value,
            IndentBlock(managerCall.TrimEnd('\n'), manager.Groups["indent"].Value),
            "QuestRunner Manager location");
        text = ReplaceOnce(text, spawner.Value,
            IndentBlock(spawnerCall.TrimEnd('\n'), spawner.Groups["indent"].Value),
            "QuestRunner Spawner location");
        const string loader = """
-- G-CET semantic coordinator bridge:questrunner
local __gcetQuestManagerGate, __gcetQuestSpawnerGate = nil, nil
do
 local ok,factory=pcall(dofile,"G-CET/Coordinator.lua")
 if ok and type(factory)=="function" then
  local ready,manager,spawner=pcall(factory)
  if ready and type(manager)=="function" and type(spawner)=="function" then
   __gcetQuestManagerGate,__gcetQuestSpawnerGate=manager,spawner
  end
 end
end

""";
        text = ReplaceOnce(text, state, loader, "QuestRunner coordinator initialization");
        return (crlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text,
            module);
    }

    private static (string UpdatedSource, string Module)? SitAnywhere(string original)
    {
        if (!original.Contains("Cron.HasActiveTimers()", StringComparison.Ordinal) ||
            !original.Contains("world.hasVisibleState()", StringComparison.Ordinal))
            return null;
        var crlf = original.Contains("\r\n", StringComparison.Ordinal);
        var text = original.Replace("\r\n", "\n", StringComparison.Ordinal);
        var callbacks = Regex.Matches(text,
            @"(?ms)^(?<indent>[ \t]*)(?<reg>registerForEvent|registerRuntimeEvent|__gcetRegisterEvent_\d+)\s*\(\s*['""]onUpdate['""]\s*,\s*function\s*\(\s*dt\s*\)\s*\n(?<body>.*?)^\k<indent>end\s*\)[ \t]*$",
            RegexOptions.CultureInvariant).Cast<Match>()
            .Where(m => m.Groups["body"].Value.Contains("Cron.HasActiveTimers()", StringComparison.Ordinal))
            .ToArray();
        if (callbacks.Length != 1)
            throw new InvalidOperationException("sitAnywhere callback not uniquely bound.");
        var chosen = callbacks[0];
        var body = chosen.Groups["body"].Value;
        // Canonical source is emitted by the installed SitAnywhere A/B/C injector.
        // This checks the complete control-flow body, not a marker or mod SHA.
        const string proved = """
if self.runtimeData.inMenu or not self.runtimeData.inGame then return end
local hasTimers = Cron.HasActiveTimers()
local scanning = self.logic.isScanning == true or self.runtimeData.forceScan == true
local inWorkspot = self.logic:inWorkspot() == true
local inTransition = self.logic:inTransition() == true
local workspotRealtime = false
for _, spot in pairs(self.logic.sittables) do
local workspot = spot.workspot
if workspot.enableCamera or workspot.camTransition or workspot.slide then
workspotRealtime = true
break
end
end
if not scanning
and not inWorkspot
and not inTransition
and not workspotRealtime
and not hasTimers
and not interaction.hubShown
and self.logic.workspotsHidden
and not world.hasVisibleState() then
return
end
if hasTimers then Cron.Update(dt) end
if interaction.hubShown then interaction.update() end
world.update()
self.logic:onUpdate()
for _, spot in pairs(self.logic.sittables) do
local workspot = spot.workspot
if workspot.enableCamera or workspot.camTransition or workspot.slide then
workspot.yaw = self.yaw
workspot.pitch = self.pitch
spot:update(dt)
end
end
""";
        static string Compact(string input) =>
            Regex.Replace(input, @"\s+", "", RegexOptions.CultureInvariant);
        if (!string.Equals(Compact(body), Compact(proved), StringComparison.Ordinal))
            throw new InvalidOperationException(
                "sitAnywhere optimized callback control flow changed.");
        const string fallback = """
if self.runtimeData.inMenu or not self.runtimeData.inGame then return end
Cron.Update(dt)
interaction.update()
world.update()
self.logic:onUpdate()
for _, spot in pairs(self.logic.sittables) do
    local workspot = spot.workspot
    if workspot.enableCamera or workspot.camTransition or workspot.slide then
        workspot.yaw = self.yaw
        workspot.pitch = self.pitch
        spot:update(dt)
    end
end
""";
        var module = Prefix + "sitanywhere\n" +
            "return function(dt,self,Cron,interaction,world)\n" +
            body + "end\n";
        var indent = chosen.Groups["indent"].Value;
        var reg = chosen.Groups["reg"].Value;
        var replacement =
            indent + reg + "(\"onUpdate\", function(dt)\n" +
            indent + "    if __gcetSitAnywhereUpdate then\n" +
            indent + "        return __gcetSitAnywhereUpdate(dt,self,Cron,interaction,world)\n" +
            indent + "    end\n" +
            IndentBlock(fallback.TrimEnd('\n'), indent + "    ") + "\n" +
            indent + "end)";
        text = text[..chosen.Index] + replacement +
            text[(chosen.Index + chosen.Length)..];
        const string loader = """
-- G-CET semantic coordinator bridge:sitanywhere
local __gcetSitAnywhereUpdate=nil
do
 local ok,update=pcall(dofile,"G-CET/Coordinator.lua")
 if ok and type(update)=="function" then __gcetSitAnywhereUpdate=update end
end

""";
        var revisedCallback = text.IndexOf(replacement, StringComparison.Ordinal);
        if (revisedCallback < 0 || text.IndexOf(replacement,
            revisedCallback + replacement.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("sitAnywhere callback placement drift.");
        text = text.Insert(revisedCallback, loader);
        return (crlf ? text.Replace("\n", "\r\n", StringComparison.Ordinal) : text,
            module);
    }

    private static Match UniqueBlock(string source, string pattern, string label)
    {
        var matches = Regex.Matches(source, pattern,
            RegexOptions.CultureInvariant | RegexOptions.Multiline).Cast<Match>().ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(label + " is not uniquely source-proven.");
        return matches[0];
    }

    private static string Dedent(Match block)
    {
        var indent = block.Groups["indent"].Value;
        var lines = block.Value.Split('\n');
        if (lines.Any(line => line.Length != 0 && !line.StartsWith(indent, StringComparison.Ordinal)))
            throw new InvalidOperationException("Block indentation is inconsistent.");
        return string.Join("\n",
            lines.Select(line => line.StartsWith(indent, StringComparison.Ordinal)
                ? line[indent.Length..] : line));
    }

    private static string IndentBlock(string source, string indent) =>
        string.Join("\n", source.Split('\n').Select(line => indent + line));

    private static string ReplaceOnce(string text, string source, string replacement,
        string label)
    {
        var index = text.IndexOf(source, StringComparison.Ordinal);
        if (index < 0 ||
            text.IndexOf(source, index + source.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException(label + " occurs zero or multiple times.");
        return text[..index] + replacement + text[(index + source.Length)..];
    }
}
