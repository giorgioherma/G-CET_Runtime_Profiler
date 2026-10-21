using System.Reflection;
using System.Text;

const string OwnerQuest = "QuestRunner";
const string OwnerSit = "sitAnywhere";

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static int Stage(string root,
    Dictionary<string, byte[]> staged,
    List<object> manifest,
    List<object> skipped)
{
    var assembly = Assembly.Load("G.CETProfiler.Core");
    var stage = assembly.GetType(
        "GCETRuntimeProfiler.Core.Services.LiveStackSemanticCoordinatorSource",
        throwOnError: true)!.GetMethod("TryStage",
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
    Require(stage is not null, "Coordinator stage entrypoint missing");
    var changed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    var sourceHashes = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    return (int)stage!.Invoke(null, new object?[] {
        root, staged, sourceHashes, changed, manifest, skipped
    })!;
}

static void Write(string root, string owner, string source)
{
    var folder = Path.Combine(root, owner);
    Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, "init.lua"), source,
        new UTF8Encoding(false));
}

static string QuestFixture()
{
    return """
-- G-CET semantic:questrunner
local Manager={current=nil,update=function()end}
local Spawner={Update=function()end}
local __gcetQuestRunnerManagerElapsed = 0.0
local __gcetQuestRunnerSpawnerElapsed = 0.0

registerForEvent("onUpdate", function(dt)
    Cron.Update(dt)
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
    __gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed + (tonumber(dt) or 0)
    if __gcetQuestRunnerSpawnerElapsed >= 0.25 then
    	local __gcetElapsed = __gcetQuestRunnerSpawnerElapsed
    	__gcetQuestRunnerSpawnerElapsed = 0.0
    	Spawner.Update(__gcetElapsed)
    end
end)
""";
}

static string SitFixture()
{
    return """
-- G-CET semantic:sitanywhere
registerForEvent("onUpdate", function(dt)
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
end)
""";
}

static void Validate(string root, string owner, string source,
    string moduleMustContain, string bridgeMustContain)
{
    Write(root, owner, source);
    var staged = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    var manifest = new List<object>();
    var skipped = new List<object>();
    var applied = Stage(root, staged, manifest, skipped);
    Require(applied == 1, owner + " conversion not applied; reasons: " +
        System.Text.Json.JsonSerializer.Serialize(skipped));
    Require(staged.ContainsKey(owner + "/init.lua"), owner + " bridge not staged");
    Require(staged.ContainsKey(owner + "/G-CET/Coordinator.lua"),
        owner + " external module not staged");
    var lua = Encoding.UTF8.GetString(staged[owner + "/init.lua"]);
    var coordinator = Encoding.UTF8.GetString(staged[owner + "/G-CET/Coordinator.lua"]);
    Require(lua.Contains(bridgeMustContain, StringComparison.Ordinal),
        owner + " generated bridge missing");
    Require(coordinator.Contains(moduleMustContain, StringComparison.Ordinal),
        owner + " original scheduler code not relocated");
    Require(!lua.Contains(moduleMustContain, StringComparison.Ordinal),
        owner + " moved coordinator work remains inline");
    Require(lua.Contains("pcall(dofile", StringComparison.Ordinal),
        owner + " module load fallback absent");
    Require(manifest.Count == 1 && skipped.Count == 0,
        owner + " produced a partial or ambiguous manifest");

    File.WriteAllBytes(Path.Combine(root, owner, "init.lua"),
        staged[owner + "/init.lua"]);
    Directory.CreateDirectory(Path.Combine(root, owner, "G-CET"));
    File.WriteAllBytes(Path.Combine(root, owner, "G-CET", "Coordinator.lua"),
        staged[owner + "/G-CET/Coordinator.lua"]);
    staged.Clear(); manifest.Clear(); skipped.Clear();
    Require(Stage(root, staged, manifest, skipped) == 0 &&
        staged.Count == 0 && skipped.Count == 0,
        owner + " existing owned coordinator is not idempotent");
}

var root = Path.Combine(Path.GetTempPath(),
    "gcet-coordinator-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var empty = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    Require(Stage(root, empty, new(), new()) == 0 && empty.Count == 0,
        "Absent owners became required mod stack dependencies");

    Validate(root, OwnerQuest, QuestFixture(),
        "__gcetQuestRunnerSpawnerElapsed = __gcetQuestRunnerSpawnerElapsed +",
        "__gcetQuestManagerGate");
    Directory.Delete(Path.Combine(root, OwnerQuest), true);
    Validate(root, OwnerSit, SitFixture(),
        "local workspotRealtime = false",
        "__gcetSitAnywhereUpdate");

    // A current source drift must NEVER turn a semantic pass into an error
    // for a separate mod or silently replace unknown gameplay.
    Directory.Delete(Path.Combine(root, OwnerSit), true);
    var drift = QuestFixture().Replace(">= 0.20", ">= 0.30",
        StringComparison.Ordinal);
    Write(root, OwnerQuest, drift);
    var skippedDrift = new List<object>();
    var pending = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    Require(Stage(root, pending, new(), skippedDrift) == 0 &&
        pending.Count == 0 && skippedDrift.Count == 1 &&
        File.ReadAllText(Path.Combine(root, OwnerQuest, "init.lua")) == drift,
        "Semantic source drift was not independently refused without mutation");
    Console.WriteLine(
        "Live-stack coordinator contract PASS: absent owner, two converted owners, owned modules, idempotence and drift refusal.");
}
finally
{
    Directory.Delete(root, recursive: true);
}
