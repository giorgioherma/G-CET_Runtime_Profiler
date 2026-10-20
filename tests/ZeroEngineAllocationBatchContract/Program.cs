using System.Reflection;
using System.Text;

static void Check(bool yes, string why)
{
    if (!yes) throw new InvalidOperationException(why);
}
var cls = typeof(GCETRuntimeProfiler.Core.Services.PassGeneratorService).Assembly
    .GetType("GCETRuntimeProfiler.Core.Services.ZeroEngineAllocationBatchSource", true)!;
var spatial = cls.GetMethod("TryTransformSpatialCore",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var lifecycle = cls.GetMethod("TryTransformLifecycleCore",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var pinnedSpatial = cls.GetMethod("TryTransformSpatial",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var pinnedLifecycle = cls.GetMethod("TryTransformLifecycle",
    BindingFlags.Static | BindingFlags.NonPublic)!;
var evidence = cls.GetMethod("HasCallsiteEvidence",
    BindingFlags.Static | BindingFlags.NonPublic)!;

var loops = """
    for dx = -cellRadius, cellRadius do
        for dy = -cellRadius, cellRadius do
            local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)
            local cell = grid[key]
        end
    end
""";
var spatialFixture = """
local SpatialHash = {}
local defaultGridSize = 100
function SpatialHash.RegisterSpatialSet(name, entries, config, source)
    config = config or {}
    entries = entries or {}
    local setData = {
        config = {
            gridSize = config.gridSize or defaultGridSize,
            pollRadius = config.pollRadius,
            pollRadiusSq = config.pollRadius and (config.pollRadius * config.pollRadius) or nil,
        }
    }
end
function SpatialHash.QueryNearest(setName, radiusSq)
    for dx = -cellRadius, cellRadius do
        for dy = -cellRadius, cellRadius do
            local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)
            local cell = grid[key]
        end
    end
end
function SpatialHash.QueryWithin(setName, radiusSq)
    for dx = -cellRadius, cellRadius do
        for dy = -cellRadius, cellRadius do
            local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)
            local cell = grid[key]
        end
    end
end
function SpatialHash.Update(frame)
            local cellRadius = math.ceil(cfg.pollRadius / gridSize)
            if cellRadius < 1 then cellRadius = 1 end
    for dx = -cellRadius, cellRadius do
        for dy = -cellRadius, cellRadius do
            local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)
            local cell = grid[key]
        end
    end
end
return SpatialHash
""";
var spatialInput = Encoding.UTF8.GetBytes(spatialFixture);
object?[] p = [spatialInput, null, null];
Check((bool)spatial.Invoke(null, p)!, "Spatial structural fixture refused");
var spatialOutput = Encoding.UTF8.GetString((byte[])p[1]!);
Check((string)p[2]! == "APPLIED", "Spatial success reason incorrect");
Check(spatialOutput.Split("G-CET SPATIAL_GRID_X_ROW_REUSE_STAGE_B_V1").Length - 1 == 3,
    "Missing three loop rewrites");
Check(spatialOutput.Contains("G-CET SPATIAL_POLL_RADIUS_SNAPSHOT_STAGE_A_V1"),
    "Missing radius stage");
Check(spatialOutput.Contains("local cellRadius = cfg.pollCellRadius"), "Missing registration radius");
Check(spatialOutput.Contains("local key = (gx + 10000) * 20000 + (gy + 10000)"),
    "Grid hash formula missing");
Check(!spatialOutput.Contains("local key = getGridKey(px + dx * gridSize"),
    "Old repetitive grid helper still present");
object?[] pAgain = [p[1], null, null];
Check(!(bool)spatial.Invoke(null, pAgain)!, "Spatial double application");
Check((string)pAgain[2]! == "ALREADY_SATISFIED", "Spatial idempotence wrong reason");
object?[] pPinned = [Encoding.UTF8.GetBytes(spatialFixture + "\n-- custom SpatialHash release metadata"), null, null];
Check((bool)pinnedSpatial.Invoke(null, pPinned)!, "Version-fluid Spatial source incorrectly rejected");
var missingLoop = spatialFixture.Replace(
    "function SpatialHash.QueryWithin(setName, radiusSq)",
    "function SpatialHash.QueryWithin(setName, radiusSq)")
    .Replace("local key = getGridKey(px + dx * gridSize, py + dy * gridSize, gridSize)",
       "local key = unexpectedGrid()", StringComparison.Ordinal);
object?[] pDrift = [Encoding.UTF8.GetBytes(missingLoop), null, null];
Check(!(bool)spatial.Invoke(null, pDrift)!, "Drifted spatial loops accepted");

var lifecycleFixture = """
local Lifecycle = {}
local function GetPlayerHash(player)
    if not player then return nil end

    local ok, id = pcall(player.GetEntityID, player)
    if not ok or not id then return nil end

    return id.hash
end
function Lifecycle.Update()
    local ok, player = pcall(Game.GetPlayer)
    if not ok then player = nil end
    -- Defensive engine transition protection
    if player then
        local ok2, id = pcall(player.GetEntityID, player)
        if not ok2 or not id then
            player = nil
        end
    end

    local hash = GetPlayerHash(player)
    if hash then
        Events.PlayerReady:trigger(player)
        Events.PlayerRecreated:trigger(player)
        Events.PlayerInvalidated:trigger()
    end
end
function Lifecycle.ForceInvalidate()
    -- Session teardown is a separate invalidation event path.
    Events.PlayerInvalidated:trigger()
end
return Lifecycle
""";
var lifeInput = Encoding.UTF8.GetBytes(lifecycleFixture);
object?[] l = [lifeInput, null, null];
Check((bool)lifecycle.Invoke(null, l)!, "Lifecycle structural fixture refused");
var lifeOutput = Encoding.UTF8.GetString((byte[])l[1]!);
Check((string)l[2]! == "APPLIED", "Lifecycle success reason incorrect");
Check(lifeOutput.Split("pcall(player.GetEntityID, player)").Length - 1 == 1,
    "Lifecycle duplicate getter remains");
Check(lifeOutput.Contains("local hash = GetPlayerHash(checkedId)"),
    "Lifecycle failed to use validated local ID");
Check(lifeOutput.Contains("Events.PlayerRecreated:trigger(player)"),
    "Lifecycle original event lost");
Check(lifeOutput.Split("Events.PlayerInvalidated:trigger()").Length - 1 == 2,
    "Lifecycle must retain both update-time and ForceInvalidate event paths");
object?[] lAgain = [l[1], null, null];
Check(!(bool)lifecycle.Invoke(null, lAgain)!, "Lifecycle double application");
Check((string)lAgain[2]! == "ALREADY_SATISFIED", "Lifecycle idempotence wrong reason");
object?[] lPinned = [Encoding.UTF8.GetBytes(lifecycleFixture + "\n-- author Lifecycle upgrade"), null, null];
Check((bool)pinnedLifecycle.Invoke(null, lPinned)!, "Version-fluid Lifecycle source incorrectly rejected");
var lifeDrift = lifecycleFixture.Replace("local hash = GetPlayerHash(player)",
    "local hash = GetPlayerHash(nil)", StringComparison.Ordinal);
object?[] lInvalid = [Encoding.UTF8.GetBytes(lifeDrift), null, null];
Check(!(bool)lifecycle.Invoke(null, lInvalid)!, "Lifecycle source drift admitted");
// Locate the second event directly: fixture source line endings may be CRLF
// on Windows, making newline-delimited Replace() an unreliable drift test.
const string invalidation = "Events.PlayerInvalidated:trigger()";
var teardownEvent = lifecycleFixture.LastIndexOf(invalidation, StringComparison.Ordinal);
Check(teardownEvent > lifecycleFixture.IndexOf(invalidation, StringComparison.Ordinal),
    "Fixture must contain a separate ForceInvalidate event");
var lifeInvalidationDrift = lifecycleFixture.Remove(teardownEvent, invalidation.Length);
object?[] lInvalidation = [Encoding.UTF8.GetBytes(lifeInvalidationDrift), null, null];
Check(!(bool)lifecycle.Invoke(null, lInvalidation)!,
    "Missing ForceInvalidate event path was accepted");

var root = Path.Combine(Path.GetTempPath(), "gcet-0engine-batch-" + Guid.NewGuid().ToString("N"));
try
{
    var rt = Path.Combine(root, "Data", "Runtime");
    Directory.CreateDirectory(rt);
    var csv = Path.Combine(rt, "CET_Runtime_Profile_LuaNative_Callsites.csv");
    var header = "RegistrationId,ProfileEpoch,SampleSequence,Frame,CallerSourceFile,CallerLine,ParentFunctionKey,ChildFunctionKey,SampledCCalls,SampledCOwnPositiveBytes,SampledCOwnPositiveOps,Attribution,Interpretation";
    var spatialRow = "8,1,1,92,D:/Games/mods/0-Engine/modules/SpatialHash.lua,272,Parent,|ceil|C,8,320,8,SAMPLED,NOTE";
    var lifeRow = "8,1,2,92,D:/Games/mods/0-Engine/modules/Lifecycle.lua,46,Parent,|__index|C,8,380,8,SAMPLED,NOTE";
    File.WriteAllLines(csv, [header, spatialRow, lifeRow]);
    Check((bool)evidence.Invoke(null, [root, "SpatialHash.lua", 272, "ceil"])!,
        "Spatial evidence rejected");
    Check((bool)evidence.Invoke(null, [root, "Lifecycle.lua", 46, "__index"])!,
        "Lifecycle evidence rejected");
    File.WriteAllLines(csv, [header, spatialRow.Replace(",320,8", ",0,8"), lifeRow]);
    Check(!(bool)evidence.Invoke(null, [root, "SpatialHash.lua", 272, "ceil"])!,
        "Zero-byte evidence admitted");
    File.WriteAllLines(csv, [header, spatialRow]);
    Check(!(bool)evidence.Invoke(null, [root, "Lifecycle.lua", 46, "__index"])!,
        "Absent lifecycle evidence admitted");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}
Console.WriteLine("ZeroEngineAllocationBatch: Spatial radius+row reuse, Lifecycle ID, idempotence, drift, and callsite evidence PASS");
