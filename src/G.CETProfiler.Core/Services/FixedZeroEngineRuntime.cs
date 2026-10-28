using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GCETRuntimeProfiler.Core.Services;

internal sealed record FixedZeroEngineBuild(
    IReadOnlyDictionary<string, byte[]> Files,
    string LiveState,
    string LiveInitSha256,
    string FixedVersion,
    string FixedInitSha256);

/// <summary>
/// The one intentional identity-specific runtime exception in V1.
///
/// Generic callback targets are always selected from profiler/resolver evidence.
/// 0-Engine is different: generated callback transforms require a small shared
/// ActionRouter/MakeEventRegistrar/provider API.
///
/// Known G-CET runtimes keep the exact fixed-runtime overlay. Unknown versions
/// are accepted only when their current source proves a simple exported-table
/// contract; those builds are preserved and receive an additive Engine.GCET
/// compatibility bridge plus uniquely named G-CET support modules.
/// </summary>
internal static class FixedZeroEngineRuntime
{
    internal const string BaseVersion = "0.18.6";
    internal const string FixedVersion = "0.18.14-ADDITIVE-WORKLOAD-SERVICES";

    private const string SharedSystemMarker =
        "-- G-CET shared providers v2";

    private const string ProfilerBridgeMarker =
        "CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2";

    private const string HostCompatibilityMarker =
        "-- G-CET host compatibility bridge v1";

    private const string HostCompatibilityVersion =
        "HOST-COMPAT-v1";

    private const string WorkloadMarker = "-- G-CET additive 0-Engine workload services v1";

    private static readonly string[] WorkloadModules =
    [
        "modules/GCETWorkQueue.lua",
        "modules/GCETPhasePlanner.lua",
        "modules/GCETFrameListeners.lua",
        "modules/GCETStateSignals.lua",
        "modules/GCETWorkloadProbe.lua",
        "modules/GCETNativeLocationHost.lua"
    ];

    // Live support modules are admitted by their source-visible API, not
    // version labels or immutable-file digests. A compatible installed module
    // remains authoritative and is staged unchanged (except for source-proven
    // additive instrumentation), even if locally customized.

    private const string HostActionRouterModule =
        "modules/G-CET/ActionRouter.lua";

    // G-CET Scheduler is canonical under modules/G-CET. Emit a root alias
    // pointing at the SAME Lua module instance, rather than an independent
    // root Scheduler implementation. The root alias is Resolver-owned and
    // persists after Profiler restore. ActionRouter stays namespaced.
    private static readonly string[] FixedModules =
    [
        "modules/ActionRouter.lua",
        "modules/Health.lua",
        "modules/Scheduler.lua"
    ];

    private static readonly string[] PackagedRootModules =
    [
        "modules/Health.lua"
    ];

    internal static FixedZeroEngineBuild Prepare(string modsRoot)
    {
        modsRoot = Path.GetFullPath(modsRoot);
        var liveRoot = Path.Combine(modsRoot, "0-Engine");
        var liveInit = Path.Combine(liveRoot, "init.lua");

        if (!File.Exists(liveInit))
        {
            throw new InvalidOperationException(
                "Generated G-CET passes require an installed 0-Engine that exports its runtime table. " +
                $"The expected live file was not found: {liveInit}");
        }

        var liveInitBytes = File.ReadAllBytes(liveInit);
        var liveHash = Sha256(liveInitBytes);

        // The profiler may have temporarily injected its adaptive scheduler
        // bridge into the live init. Generated passes must be based on the exact
        // pre-profiler source, because that is what RESTORE ORIGINAL STATE puts
        // back before the user applies the generated overlay.
        var sourceBytes = liveInitBytes;
        var sourceHash = liveHash;
        var statePrefix = "";

        if (Encoding.UTF8.GetString(liveInitBytes).Contains(
                ProfilerBridgeMarker,
                StringComparison.Ordinal))
        {
            var profilerBackup = ResolveProfilerBackupInit(modsRoot);
            if (profilerBackup is null || !File.Exists(profilerBackup))
            {
                throw new InvalidOperationException(
                    "The installed 0-Engine init.lua contains the G-CET profiler bridge, but the profiler-managed original init.lua backup could not be found. " +
                    "Restore the profiler's original state before generating a pass. " +
                    $"Installed SHA256: {liveHash}.");
            }

            sourceBytes = File.ReadAllBytes(profilerBackup);
            sourceHash = Sha256(sourceBytes);
            statePrefix = "PROFILER_MANAGED_";
        }

        var runtimeRoot = Path.Combine(AppContext.BaseDirectory, "runtime", "0-Engine");
        var ownedModuleSource = RouteLegacyGcetImportsToNamespace(sourceBytes);

        // Upgrade the CURRENT source by proven additive anchors instead
        // of recognizing an old install through an exact file hash and
        // replacing it with a bundled snapshot.
        try
        {
            var sharedCurrent = AddSharedSystemAccessors(ownedModuleSource);
            var extendedCurrent =
                AddNativeLocationBridge(AddWorkloadServices(sharedCurrent));
            var integratedCurrent = EnsureRuntimeApis(extendedCurrent);
            var liveFiles = BuildKnownFixedRuntime(
                runtimeRoot, liveRoot, integratedCurrent)
                .ToDictionary(x => x.Key, x => x.Value,
                    StringComparer.OrdinalIgnoreCase);
            AddNamespacedRuntimeDependencies(runtimeRoot, liveRoot, liveFiles,
                replaceRootScheduler: true);
            return new FixedZeroEngineBuild(
                liveFiles,
                statePrefix + "SOURCE_PROVEN_GCET_RUNTIME",
                liveHash,
                FixedVersion,
                Sha256(integratedCurrent));
        }
        catch (InvalidOperationException)
        {
            // Newer/forked author implementations can expose the same
            // exported runtime table without the legacy accessor anchor.
            // Preserve the author's complete init via the general adapter.
            // It must prove its export before any file is staged.
        }

        if (!TryBuildHostCompatibilityInit(
                ownedModuleSource,
                out var compatibleInit,
                out var compatibilityReason))
        {
            throw new InvalidOperationException(
                "The installed 0-Engine is not a recognized G-CET fixed runtime, and G-CET could not prove a safe host-preserving compatibility injection. " +
                $"Installed SHA256: {liveHash}. " +
                $"Source SHA256: {sourceHash}. " +
                $"Reason: {compatibilityReason}. " +
                "No unknown 0-Engine files will be replaced.");
        }

        compatibleInit = EnsureRuntimeApis(
            AddNativeLocationBridge(AddWorkloadServices(compatibleInit)));
        var hostFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = compatibleInit
        };
        AddWorkloadModules(runtimeRoot, liveRoot, hostFiles);
        AddNamespacedRuntimeDependencies(runtimeRoot, liveRoot, hostFiles,
            replaceRootScheduler: false);

        return new FixedZeroEngineBuild(
            hostFiles,
            statePrefix + "STRUCTURAL_COMPAT",
            liveHash,
            HostCompatibilityVersion,
            Sha256(compatibleInit));
    }



    // Build against a complete 0-Engine API, not only its Lua filenames.
    // Source-proven inputs are preserved: missing methods are attached in one
    // G-CET-owned compatibility block, never by rewriting author functions.
    private static byte[] EnsureRuntimeApis(byte[] source)
    {
        var text = Encoding.UTF8.GetString(source);
        if (!text.Contains("MakeEventRegistrar", StringComparison.Ordinal) ||
            !text.Contains("SubscribeAction", StringComparison.Ordinal))
        {
            if (!TryBuildHostCompatibilityInit(
                    source, out var adapted, out var reason))
                throw new InvalidOperationException(
                    "Cannot attach required action/frame APIs: " + reason);
            source = adapted;
        }

        return EnsureNamespacedSchedule(source);
    }

    // The stock 0-Engine may contain a Scheduler.lua file without ever
    // importing or ticking it. Add a separate, verified scheduler runtime,
    // with a SINGLE native event bridge installed only on first use.
    // Do not replace modules/Scheduler.lua or alter original Register methods.
    private static byte[] EnsureNamespacedSchedule(byte[] source)
    {
        var hasBom = source.Length >= 3 && source[0] == 0xEF &&
            source[1] == 0xBB && source[2] == 0xBF;
        var text = hasBom ? Encoding.UTF8.GetString(source, 3, source.Length - 3)
                          : Encoding.UTF8.GetString(source);

        const string marker = "-- G-CET namespaced scheduler compatibility v1";
        if (text.Contains(marker, StringComparison.Ordinal))
            return source;

        // Existing complete integration is authoritative. A source name,
        // an imported but unused Scheduler module, or version is not proof.
        if (Regex.IsMatch(text,
                @"\b(?:Engine|[A-Za-z_][A-Za-z0-9_]*)\.Schedule\s*=\s*\{",
                RegexOptions.CultureInvariant) &&
            text.Contains("Schedule.Every", StringComparison.Ordinal) &&
            text.Contains("Scheduler.Update", StringComparison.Ordinal) &&
            text.Contains("Scheduler.Init", StringComparison.Ordinal))
            return source;

        var exports = Regex.Matches(text,
            @"(?m)^[ \t]*return[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.CultureInvariant);
        if (exports.Count == 0)
            throw new InvalidOperationException(
                "Cannot attach Scheduler: no final 0-Engine export.");
        var final = exports[^1];
        if (!Regex.IsMatch(text[(final.Index + final.Length)..],
                @"\A(?:\s|--[^\r\n]*)*\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "Cannot attach Scheduler after executable statements.");

        // The known 0.18.6 author runtime already owns a single CET onUpdate
        // dispatcher. Adopt it instead of registering another CET callback.
        // Only source-proven core state/export shapes are admitted.
        var normalizedHost = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string authorUpdate =
            "    pcall(Cron.Update, delta)\n" +
            "    pcall(UpdateFrame, delta)\n" +
            "end)";
        var canAdoptCentralUpdate =
            normalizedHost.Split(authorUpdate, StringSplitOptions.None).Length == 2 &&
            normalizedHost.Contains("registerForEvent(\"onUpdate\", function(delta)", StringComparison.Ordinal) &&
            normalizedHost.Contains("local currentFrame", StringComparison.Ordinal) &&
            normalizedHost.Contains("local isPlaying", StringComparison.Ordinal) &&
            normalizedHost.Contains("State.inMenu", StringComparison.Ordinal) &&
            normalizedHost.Contains("State.player()", StringComparison.Ordinal) &&
            normalizedHost.Contains("function Engine.GetState(", StringComparison.Ordinal);
        var block = """
-- G-CET namespaced scheduler compatibility v1
do
    local __host = __GCET_HOST__
    if type(__host) == "table" and type(__host.Register) == "function" then
        local __ok, __scheduler = pcall(require, "modules/G-CET/Scheduler")
        if not __ok or type(__scheduler) ~= "table" or
           type(__scheduler.Init) ~= "function" or
           type(__scheduler.Update) ~= "function" or
           type(__scheduler.Every) ~= "function" then
            error("G-CET namespaced scheduler dependency missing or incomplete")
        end
        local __started, __frame = false, 0
        local function __activate()
            if __started then return end
            __scheduler.Init(nil, nil)
            registerForEvent("onUpdate", function(dt)
                __frame = __frame + 1
                -- CET onUpdate delta is an elapsed-frame interval.
                -- os.clock() measures process CPU time, not wall time.
                local __realDt = dt or 0
                local __player = nil
                if Game ~= nil and type(Game.GetPlayer) == "function" then
                    local ok, player = pcall(Game.GetPlayer)
                    if ok then __player = player end
                end
                pcall(__scheduler.Update, {
                    frame = __frame, dt = dt, realDt = __realDt,
                    now = nil, generation = 0, playing = __player ~= nil,
                    inMenu = false, player = __player, state = nil
                })
            end)
            __started = true
        end
        local __names = {
            "EveryFrame", "EveryFrames", "Every", "After", "NextTick"
        }
        local __schedule = type(__host.Schedule) == "table"
            and __host.Schedule or {}
        for _, name in ipairs(__names) do
            if type(__schedule[name]) ~= "function" then
                local method = name
                __schedule[method] = function(...)
                    __activate()
                    return __scheduler[method](...)
                end
            end
        end
        if type(__schedule.GetInfo) ~= "function" then
            __schedule.GetInfo = __scheduler.GetInfo
        end
        __host.Schedule = __schedule
        local __priorRegister = __host.Register
        __host.Register = function(...)
            local mod = __priorRegister(...)
            if type(mod) == "table" then
                local schedule = type(mod.Schedule) == "table" and mod.Schedule or {}
                local owner = mod.name or (select(1, ...)) or "unscoped"
                for _, name in ipairs(__names) do
                    local method = name
                    if type(schedule[method]) ~= "function" then
                        schedule[method] = function(...)
                            local args = {...}
                            local optIdx = (method == "EveryFrames" or
                                method == "Every" or method == "After") and 2 or 1
                            local options = args[optIdx]
                            if type(options) == "function" then
                                args[optIdx + 1] = options
                                options = {}
                            end
                            if type(options) ~= "table" then options = {} end
                            local copy = {}
                            for key, value in pairs(options) do copy[key] = value end
                            copy.owner = owner
                            args[optIdx] = copy
                            return __schedule[method]((table.unpack or unpack)(args))
                        end
                    end
                end
                mod.Schedule = schedule
            end
            return mod
        end
    end
end

""".Replace("__GCET_HOST__", final.Groups["name"].Value);
        if (canAdoptCentralUpdate)
        {
            // This closure is installed by the namespaced bridge before any
            // gameplay update. The author callback supplies the real lifecycle
            // context; no second native CET onUpdate is registered.
            const string existingSchedulerEvent =
                "            registerForEvent(\"onUpdate\", function(dt)";
            const string startedLine = "            __started = true";
            var eventStart = block.IndexOf(existingSchedulerEvent, StringComparison.Ordinal);
            var eventEnd = eventStart < 0 ? -1 :
                block.IndexOf(startedLine, eventStart, StringComparison.Ordinal);
            if (eventStart < 0 || eventEnd < 0)
                throw new InvalidOperationException(
                    "Namespaced scheduler bridge cannot adopt central 0-Engine tick.");
            var driver = """
            local __lastRealClock = os.clock()
            __host.__gcetScheduleDriver = function(ctx)
                -- Match Phase 1+2 clock=real semantics while preserving the
                -- original CET delta for simulation/game-time jobs.
                local __realClock = os.clock()
                ctx.realDt = __realClock - __lastRealClock
                __lastRealClock = __realClock
                __frame = __frame + 1
                pcall(__scheduler.Update, ctx)
            end
""";
            block = block[..eventStart] + driver + block[eventEnd..];
            var authorReplacement = """
    pcall(Cron.Update, delta)
    pcall(UpdateFrame, delta)
    if type(Engine.__gcetScheduleDriver) == "function" then
        local __gcetPlayer = State.player()
        Engine.__gcetScheduleDriver({
            frame = currentFrame, dt = delta, realDt = delta,
            now = State.time, generation = 0, playing = isPlaying,
            inMenu = State.inMenu, player = __gcetPlayer,
            state = Engine.GetState()
        })
    end
end)
""";
            normalizedHost = normalizedHost.Replace(
                authorUpdate, authorReplacement, StringComparison.Ordinal);
            text = text.Contains("\r\n", StringComparison.Ordinal)
                ? normalizedHost.Replace("\n", "\r\n", StringComparison.Ordinal)
                : normalizedHost;
            // Insertion index changed after author-update injection.
            exports = Regex.Matches(text,
                @"(?m)^[ \t]*return[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
                RegexOptions.CultureInvariant);
            final = exports[^1];
        }
        var encoded = Encoding.UTF8.GetBytes(text.Insert(final.Index, block));
        if (!hasBom) return encoded;
        var result = new byte[encoded.Length + 3];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        Buffer.BlockCopy(encoded, 0, result, 3, encoded.Length);
        return result;
    }

    private static void AddNamespacedRuntimeDependencies(
        string runtimeRoot, string liveRoot,
        Dictionary<string, byte[]> files,
        bool replaceRootScheduler)
    {
        // The G-CET services are owned and shipped by Resolver regardless of
        // the installed upstream module inventory. Bundled root-named files
        // are templates, never deployable root-level Scheduler/ActionRouter.
        var router = ReconcileHostModule(liveRoot,
            HostActionRouterModule,
            LoadBundledFixedModule(runtimeRoot, "modules/ActionRouter.lua"));
        const string schedulePath = "modules/G-CET/Scheduler.lua";
        var scheduler = ReconcileHostModule(liveRoot, schedulePath,
            LoadBundledFixedModule(runtimeRoot, "modules/Scheduler.lua"));
        var source = Encoding.UTF8.GetString(scheduler);
        foreach (var method in new[] { "Init", "Update", "Every", "EveryFrames" })
            if (!source.Contains("function Scheduler." + method, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Existing namespaced Scheduler is missing " + method);
        files["0-Engine/" + HostActionRouterModule] = router;
        files["0-Engine/" + schedulePath] = scheduler;

        // Only the source-proven G-CET fixed-runtime owner may replace the
        // root module with its permanent alias. A foreign/structural host can
        // have an independently meaningful author-owned modules/Scheduler.lua;
        // preserve that file rather than redirect an unproven API.
        if (replaceRootScheduler)
        {
            // Lua require() caches modules/G-CET/Scheduler and both import
            // paths therefore return the SAME scheduler instance.
            const string rootSchedulerAlias =
                "-- G-CET canonical scheduler alias v1\n" +
                "return require('modules/G-CET/Scheduler')\n";
            files["0-Engine/modules/Scheduler.lua"] =
                Encoding.UTF8.GetBytes(rootSchedulerAlias);
        }
    }

    // Version-fluid migration of G-CET's legacy root imports. Require both
    // Scheduler and ActionRouter integration to be source-proven before moving
    // their original G-CET-owned require paths. A future official 0-Engine
    // Scheduler or ActionRouter import is never rewritten speculatively.
    private static byte[] RouteLegacyGcetImportsToNamespace(byte[] source)
    {
        var hasBom = source.Length >= 3 &&
            source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF;
        var text = hasBom ? Encoding.UTF8.GetString(source, 3, source.Length - 3)
                          : Encoding.UTF8.GetString(source);
        var scheduler = new Regex(
            @"(?<head>\brequire\s*\(\s*['""])modules/Scheduler(?<tail>['""]\s*\))",
            RegexOptions.CultureInvariant);
        var router = new Regex(
            @"(?<head>\brequire\s*\(\s*['""])modules/ActionRouter(?<tail>['""]\s*\))",
            RegexOptions.CultureInvariant);
        var schedulerHits = scheduler.Matches(text);
        var routerHits = router.Matches(text);
        if (schedulerHits.Count == 0 && routerHits.Count == 0)
            return source;

        if (schedulerHits.Count != 1 || routerHits.Count != 1 ||
            !text.Contains("Scheduler.Init(Logger, Health)", StringComparison.Ordinal) ||
            !text.Contains("ActionRouter.Init(Logger, Health", StringComparison.Ordinal) ||
            !text.Contains("Scheduler.Update", StringComparison.Ordinal) ||
            !text.Contains("ActionRouter.Dispatch", StringComparison.Ordinal) ||
            !text.Contains("Engine.Schedule", StringComparison.Ordinal) ||
            !text.Contains("Engine.SubscribeAction", StringComparison.Ordinal))
            return source;

        text = scheduler.Replace(text, match =>
            match.Groups["head"].Value + "modules/G-CET/Scheduler" +
            match.Groups["tail"].Value, 1);
        text = router.Replace(text, match =>
            match.Groups["head"].Value + "modules/G-CET/ActionRouter" +
            match.Groups["tail"].Value, 1);
        var body = Encoding.UTF8.GetBytes(text);
        if (!hasBom) return body;
        var result = new byte[3 + body.Length];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        Buffer.BlockCopy(body, 0, result, 3, body.Length);
        return result;
    }

    private static string? ResolveProfilerBackupInit(string modsRoot)
    {
        var cetRoot = Directory.GetParent(modsRoot)?.FullName;
        var pluginsRoot = cetRoot is null
            ? null
            : Directory.GetParent(cetRoot)?.FullName;

        if (string.IsNullOrWhiteSpace(pluginsRoot))
            return null;

        return Path.Combine(
            pluginsRoot,
            ".cet_runtime_profiler",
            "0-Engine.init.ORIGINAL.lua");
    }

    private static IReadOnlyDictionary<string, byte[]> BuildKnownFixedRuntime(
        string runtimeRoot,
        string liveRoot,
        byte[] sharedInit)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = sharedInit
        };

        foreach (var module in PackagedRootModules)
        {
            files["0-Engine/" + module] =
                ReconcileHostModule(liveRoot, module,
                    LoadBundledFixedModule(runtimeRoot, module));
        }

        AddWorkloadModules(runtimeRoot, liveRoot, files);
        return files;
    }

    private static byte[] LoadBundledFixedModule(
        string runtimeRoot, string modulePath)
    {
        if (!FixedModules.Contains(modulePath, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "Unexpected bundled runtime module path: " + modulePath);
        var path = Path.Combine(runtimeRoot,
            (modulePath + ".b64").Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path))
            throw new InvalidOperationException(
                "Bundled support module missing: " + path);

        // Version-fluid source adaptation does not use exact release hashes.
        // A packaged baseline is trusted as part of the signed build artifact,
        // but the installed source always takes precedence when present.
        var bytes = DecodeBase64(path, modulePath);
        if (bytes.Length < 100)
            throw new InvalidOperationException(
                "Bundled support module is empty or truncated: " + modulePath);
        return bytes;
    }

    // Additive, namespaced 0-Engine services. No stock runtime module is
    // replaced. No frame listener starts until a client opts into a service.
    private static byte[] AddWorkloadServices(byte[] source)
    {
        var hasBom = source.Length >= 3 &&
            source[0] == 0xEF && source[1] == 0xBB && source[2] == 0xBF;
        var text = hasBom
            ? Encoding.UTF8.GetString(source, 3, source.Length - 3)
            : Encoding.UTF8.GetString(source);

        if (text.Contains(WorkloadMarker, StringComparison.Ordinal))
        {
            foreach (var name in new[] {
                "GCETWorkQueue", "GCETPhasePlanner", "GCETFrameListeners",
                "GCETStateSignals" })
            {
                if (!text.Contains("modules/" + name, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "0-Engine contains a partial or foreign workload-service bridge.");
            }
            return source;
        }

        var exports = Regex.Matches(text,
            @"(?m)^[ \t]*return[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.CultureInvariant);
        if (exports.Count == 0)
            throw new InvalidOperationException(
                "Cannot add 0-Engine workload services: no final runtime export.");
        var last = exports[^1];
        var id = last.Groups["name"].Value;
        if (!Regex.IsMatch(text[(last.Index + last.Length)..],
                @"\A(?:\s|--[^\r\n]*)*\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "Cannot add 0-Engine workload services after executable statements.");
        var nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var injection = string.Join(nl, new[] {
            "",
            WorkloadMarker,
            "do",
            $"    local runtime = {id}",
            "    if type(runtime) == 'table' then",
            "        local function installIfAvailable(field, moduleName)",
            "            if runtime[field] ~= nil then return end",
            "            local ok, module = pcall(require, moduleName)",
            "            if not ok or type(module) ~= 'table' or type(module.New) ~= 'function' then return end",
            "            local built, service = pcall(module.New, runtime)",
            "            if built and type(service) == 'table' then runtime[field] = service end",
            "        end",
            "        installIfAvailable('WorkQueue', 'modules/GCETWorkQueue')",
            "        installIfAvailable('PhasePlanner', 'modules/GCETPhasePlanner')",
            "        installIfAvailable('FrameListeners', 'modules/GCETFrameListeners')",
            "        installIfAvailable('StateSignals', 'modules/GCETStateSignals')",
            "    end",
            "end",
            ""
        });
        var output = Encoding.UTF8.GetBytes(text.Insert(last.Index, injection));
        if (!hasBom) return output;
        var withBom = new byte[output.Length + 3];
        withBom[0] = 0xEF; withBom[1] = 0xBB; withBom[2] = 0xBF;
        Buffer.BlockCopy(output, 0, withBom, 3, output.Length);
        return withBom;
    }


    // Additive opt-in native location host. The prior profiler/0-Engine native
    // payload is unchanged; the RED4ext plugin remains independently installed.
    // An absent native API must not disable any author discovery callback.
    private static byte[] AddNativeLocationBridge(byte[] source)
    {
        const string marker = "-- G-CET native static location host v1";
        var hasBom = source.Length >= 3 && source[0] == 0xEF &&
            source[1] == 0xBB && source[2] == 0xBF;
        var text = hasBom
            ? Encoding.UTF8.GetString(source, 3, source.Length - 3)
            : Encoding.UTF8.GetString(source);
        if (text.Contains(marker, StringComparison.Ordinal))
        {
            if (!text.Contains("GCETNativeLocationHost", StringComparison.Ordinal) ||
                !text.Contains("NativeLocationHost:shutdown()", StringComparison.Ordinal))
                throw new InvalidOperationException("Partial or foreign native location host bridge.");
            return source;
        }
        var exports = Regex.Matches(text,
            @"(?m)^[ \t]*return[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.CultureInvariant);
        if (exports.Count == 0)
            throw new InvalidOperationException("Native location host requires a final 0-Engine export.");
        var last = exports[^1];
        if (!Regex.IsMatch(text[(last.Index + last.Length)..],
                @"\A(?:\s|--[^\r\n]*)*\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("0-Engine export cannot safely host native location bridge.");
        var name = last.Groups["name"].Value;
        var block = $$"""
            {{marker}}
            do
                local runtime={{name}}
                if type(runtime)=="table" and type(runtime.Register)=="function" then
                    local ok,source=pcall(require,"modules/GCETNativeLocationHost")
                    if ok and type(source)=="table" and type(source.new)=="function" then
                        local api=runtime.GCET
                        if api==nil or type(api)=="table" then
                            api=api or {}
                            if api.NativeLocationHost==nil then
                                local okHost,host=pcall(source.new,runtime)
                                if okHost and type(host)=="table" then
                                    api.NativeLocationHost=host
                                    runtime.GCET=api
                                end
                            end
                        end
                    end
                end
            end
            registerForEvent("onShutdown",function()
                if type({{name}})=="table" and type({{name}}.GCET)=="table" and
                    type({{name}}.GCET.NativeLocationHost)=="table" then
                    {{name}}.GCET.NativeLocationHost:shutdown()
                end
            end)

            """;
        var inserted = text.Insert(last.Index, block + "\n");
        var body=Encoding.UTF8.GetBytes(inserted);
        if (!hasBom) return body;
        var output=new byte[body.Length+3];
        output[0]=0xEF;output[1]=0xBB;output[2]=0xBF;
        Buffer.BlockCopy(body,0,output,3,body.Length);
        return output;
    }

    private static void AddWorkloadModules(
        string runtimeRoot, string liveRoot, Dictionary<string, byte[]> files)
    {
        foreach (var relative in WorkloadModules)
        {
            var path = Path.Combine(runtimeRoot,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
                throw new InvalidOperationException(
                    "Bundled 0-Engine workload module missing: " + relative);
            var data = File.ReadAllBytes(path);
            var moduleText = Encoding.UTF8.GetString(data);
            var requiredFactory = relative.EndsWith("GCETWorkloadProbe.lua", StringComparison.Ordinal)
                ? "function M.Begin("
                : relative.EndsWith("GCETNativeLocationHost.lua", StringComparison.Ordinal)
                    ? "function Host.new("
                    : "function M.New(";
            if (data.Length < 100 ||
                !moduleText.Contains(requiredFactory, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Invalid bundled workload module: " + relative);
            files["0-Engine/" + relative] =
                ReconcileHostModule(liveRoot, relative, data);
        }
    }

    private static byte[] ReconcileHostModule(
        string liveRoot,
        string relativeModule,
        byte[] bundled)
    {
        var livePath = Path.Combine(liveRoot,
            relativeModule.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(livePath))
            return bundled;

        // Existing author/user source is authoritative regardless of release,
        // naming conventions, file hash, or local changes. Bundled support files
        // fill gaps only. A missing API is handled at the opt-in call site,
        // never by replacing or refusing the whole pass.
        var bytes = File.ReadAllBytes(livePath);
        if (relativeModule.Equals(HostActionRouterModule, StringComparison.OrdinalIgnoreCase))
        {
            var oldRouter = Encoding.UTF8.GetString(bytes);
            // The user's known legacy G-CET router can implement Subscribe and
            // Dispatch yet silently ignore phase="after". For our private
            // G-CET namespace use the packaged phase-aware implementation,
            // but never replace an unrecognized custom router.
            if (oldRouter.Contains("function ActionRouter.DispatchAfter(", StringComparison.Ordinal) &&
                oldRouter.Contains("phase == \"after\"", StringComparison.Ordinal))
                return bytes;
            if (oldRouter.Contains("ActionRouter.lua - one indexed PlayerPuppet::OnAction fan-out.", StringComparison.Ordinal) &&
                oldRouter.Contains("function ActionRouter.Subscribe(config, fn, owner)", StringComparison.Ordinal) &&
                oldRouter.Contains("function ActionRouter.Dispatch(this, action, consumer)", StringComparison.Ordinal) &&
                !oldRouter.Contains("DispatchAfter", StringComparison.Ordinal))
                return bundled;
            // Unknown user-owned namespace remains unchanged. Its missing
            // after-phase capability is checked at SubscribeAction, which
            // must return nil so the author retains CET ObserveAfter fallback.
            return bytes;
        }
        if (relativeModule.Equals("modules/GCETNativeLocationHost.lua",
                StringComparison.OrdinalIgnoreCase))
        {
            return UpgradeNativeLocationHost(bytes,bundled);
        }
        if (relativeModule.Equals("modules/GCETWorkQueue.lua",
                StringComparison.OrdinalIgnoreCase))
        {
            var original = Encoding.UTF8.GetString(bytes);
            var upgraded = AddOptionalWorkQueueProbe(original);
            if (upgraded != original)
                return EncodeLikeOriginal(bytes, upgraded);
        }
        return bytes;
    }


    // The player's 0-Engine is authoritative, including custom Scheduler,
    // routing and module changes. Native-location APIs are an independent
    // *capability* requirement: reusing an old G-CET host with no
    // attachCompiled makes an otherwise valid generated GCAS manifest inert.
    // Inject only source-proven missing capabilities, never replace the host
    // module wholesale and never identify compatibility by release or hash.
    private static byte[] UpgradeNativeLocationHost(byte[] live,byte[] bundled)
    {
        var source=Encoding.UTF8.GetString(live);
        var current=Encoding.UTF8.GetString(bundled);
        var hasAttach=source.Contains("function Host:attachCompiled(",StringComparison.Ordinal);
        var hasShutdownPreservation=source.Contains("if owner.compiled",StringComparison.Ordinal) &&
            source.Contains("GCETStaticArm",StringComparison.Ordinal);
        if(hasAttach && hasShutdownPreservation)
            return live;

        var required=new[]{
            "local function native(", "function Host:attach(", "function Host:_stop(",
            "function Host:_schedule(", "function Host:_fallback(",
            "function Host:shutdown(", "self.owners"
        };
        if(required.Any(token=>!source.Contains(token,StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "The installed 0-Engine native location host lacks required " +
                "source-proven owner/session APIs; refusing to overwrite custom code.");

        var anchors=Regex.Matches(source,
            @"(?m)^[ \t]*return[ \t]+Host[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.CultureInvariant);
        if(anchors.Count!=1 ||
           !Regex.IsMatch(source[(anchors[0].Index+anchors[0].Length)..],
               @"\A(?:\s|--[^\r\n]*)*\z",RegexOptions.CultureInvariant))
            throw new InvalidOperationException(
                "Cannot prove the native host's final exported module table.");

        var patch=new StringBuilder();
        if(!hasAttach)
        {
            var start=current.IndexOf("function Host:attachCompiled(",StringComparison.Ordinal);
            var end=start<0?-1:current.IndexOf(
                "\n-- Site-granular native wake",start,StringComparison.Ordinal);
            if(end<0)
                throw new InvalidOperationException(
                    "Bundled compiled-owner API source is missing its reviewed boundary.");
            var compiled=current[start..end];
            if(!compiled.Contains("GCETStaticOwnerCount",StringComparison.Ordinal) ||
               !compiled.Contains("GCETStaticArm",StringComparison.Ordinal) ||
               !compiled.Contains("compiled=true",StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Bundled compiled-owner API fails required behavioral contract.");
            patch.AppendLine("-- G-CET source-structural native capability bridge");
            patch.AppendLine(compiled);

            // Existing manually optimized mods may still use :attach(id,sites).
            // Do not create duplicate anchors where the generated GCAS file
            // already owns that ID; preserve the old dynamic path otherwise.
            patch.AppendLine("""
                local __gcetPriorAttach=Host.attach
                function Host:attach(id,sites,onNear,onFar,onFallback)
                  local ok,count=pcall(native,"GCETStaticOwnerCount",id)
                  if ok and type(count)=="number" and count>0 then
                    if count~=#sites and not (id==10035 and count==100 and #sites<=100) then
                      return false
                    end
                    return self:attachCompiled(id,count,onNear,onFar,onFallback)
                  end
                  return __gcetPriorAttach(self,id,sites,onNear,onFar,onFallback)
                end
                """);
        }
        if(!hasShutdownPreservation)
        {
            // Reuse the author's own shutdown routine. Hide immutable anchor
            // IDs from its legacy RemoveAnchor loop after first disarming
            // them; preserve every other custom shutdown side effect.
            patch.AppendLine("""
                local __gcetPriorNativeShutdown=Host.shutdown
                function Host:shutdown()
                  if self.owners then
                    for _,owner in pairs(self.owners) do
                      if owner.compiled and type(owner.ids)=="table" then
                        for _,id in ipairs(owner.ids) do
                          pcall(native,"GCETStaticArm",id,false)
                        end
                        owner.ids={}
                      end
                    end
                  end
                  return __gcetPriorNativeShutdown(self)
                end
                """);
        }
        var insertion=patch.ToString();
        if(insertion.Length==0)return live;
        var amended=source.Insert(anchors[0].Index,insertion+"\n");
        return EncodeLikeOriginal(live,amended);
    }

    private static byte[] EncodeLikeOriginal(byte[] original, string text)
    {
        // Avoid BOM / newline churn when adding a proven stage.
        var hasBom = original.Length >= 3 && original[0] == 0xEF &&
            original[1] == 0xBB && original[2] == 0xBF;
        var source = hasBom
            ? Encoding.UTF8.GetString(original, 3, original.Length - 3)
            : Encoding.UTF8.GetString(original);
        var output = source.Contains("\r\n", StringComparison.Ordinal)
            ? text.Replace("\r\n", "\n", StringComparison.Ordinal)
                  .Replace("\n", "\r\n", StringComparison.Ordinal)
            : text;
        var payload = Encoding.UTF8.GetBytes(output);
        if (!hasBom) return payload;
        var result = new byte[payload.Length + 3];
        result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
        Buffer.BlockCopy(payload, 0, result, 3, payload.Length);
        return result;
    }

    private static string AddOptionalWorkQueueProbe(string source)
    {
        if (source.Contains("Probe.Begin(", StringComparison.Ordinal) ||
            source.Contains("Probe.End(", StringComparison.Ordinal))
            return source;
        const string call = "local ok, finished = pcall(job.step, job.context)";
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var matches = Regex.Matches(lines,
            @"(?m)^(?<indent>[ \t]*)local ok, finished = pcall\(job\.step, job\.context\)[ \t]*$",
            RegexOptions.CultureInvariant);
        if (matches.Count != 1 ||
            !Regex.IsMatch(lines, @"(?m)^local\s+M\s*=\s*\{",
                RegexOptions.CultureInvariant))
            return source;
        var match = matches[0];
        var ind = match.Groups["indent"].Value;
        var before = ind + call;
        var after = ind + "local trace = Probe.Begin(job.owner, \"gcet-work\", tostring(job.label), frame)\n" +
            before + "\n" + ind + "Probe.End(trace)";
        var withProbe = lines[..match.Index] + after +
            lines[(match.Index + match.Length)..];

        // Insert beside the module table so the original author requires,
        // closures, jobs and cadence remain exactly where they were.
        var module = Regex.Match(withProbe,
            @"(?m)^(?<line>local\s+M\s*=\s*\{[^\r\n]*\})[ \t]*$",
            RegexOptions.CultureInvariant);
        if (!module.Success ||
            !withProbe.Contains("return M", StringComparison.Ordinal))
            return source;
        withProbe = withProbe.Insert(module.Index + module.Length,
            "\nlocal Probe = require(\"modules/GCETWorkloadProbe\")");
        return withProbe;
    }

    private static bool TryBuildHostCompatibilityInit(
        byte[] sourceBytes,
        out byte[] compatibleBytes,
        out string reason)
    {
        compatibleBytes = Array.Empty<byte>();
        reason = "";

        var hasBom =
            sourceBytes.Length >= 3 &&
            sourceBytes[0] == 0xEF &&
            sourceBytes[1] == 0xBB &&
            sourceBytes[2] == 0xBF;

        var text = hasBom
            ? Encoding.UTF8.GetString(sourceBytes, 3, sourceBytes.Length - 3)
            : Encoding.UTF8.GetString(sourceBytes);

        var exportMatches = Regex.Matches(
            text,
            @"(?m)^[ \t]*return[ \t]+(?<name>[A-Za-z_][A-Za-z0-9_]*)[ \t]*;?[ \t]*(?:--[^\r\n]*)?\r?$",
            RegexOptions.CultureInvariant);

        if (exportMatches.Count == 0)
        {
            reason = "no simple runtime-table export was found";
            return false;
        }

        // Functions inside 0-Engine commonly return identifiers of their own.
        // The host contract concerns only the final exported mod table, so use
        // the last simple return and prove below that nothing executable follows it.
        var export = exportMatches[^1];
        var exportName = export.Groups["name"].Value;

        var tail = text[(export.Index + export.Length)..];
        if (!Regex.IsMatch(
                tail,
                @"\A(?:\s|--[^\r\n]*)*\z",
                RegexOptions.CultureInvariant))
        {
            reason = "the runtime export is not the final executable statement";
            return false;
        }

        var beforeExport = text[..export.Index];
        if (!Regex.IsMatch(
                beforeExport,
                @"(?m)^[ \t]*(?:local[ \t]+)?" +
                Regex.Escape(exportName) +
                @"[ \t]*=",
                RegexOptions.CultureInvariant))
        {
            reason =
                $"the exported symbol '{exportName}' has no source-visible assignment";
            return false;
        }

        if (text.Contains(HostCompatibilityMarker, StringComparison.Ordinal))
        {
            var required = new[]
            {
                "__gcetHost.GCET",
                "MakeEventRegistrar",
                "SubscribeAction",
                "G-CET host compatibility bridge v1"
            };

            if (required.All(token =>
                    text.Contains(token, StringComparison.Ordinal)))
            {
                try
                {
                    var updated = EnsureVolatileProviderAccessor(
                        text, "__gcetApi", required: true);
                    var updatedBody = Encoding.UTF8.GetBytes(updated);
                    compatibleBytes = hasBom
                        ? [0xef, 0xbb, 0xbf, ..updatedBody]
                        : updatedBody;
                    return true;
                }
                catch (InvalidOperationException ex)
                {
                    reason = ex.Message;
                    return false;
                }
            }

            reason = "a partial or foreign G-CET compatibility marker is present";
            return false;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : "\n";

        var block = BuildHostCompatibilityBlock(exportName, newline);
        var patched =
            text[..export.Index] +
            block +
            text[export.Index..];

        var body = Encoding.UTF8.GetBytes(patched);
        if (!hasBom)
        {
            compatibleBytes = body;
            return true;
        }

        compatibleBytes = new byte[body.Length + 3];
        compatibleBytes[0] = 0xEF;
        compatibleBytes[1] = 0xBB;
        compatibleBytes[2] = 0xBF;
        Buffer.BlockCopy(body, 0, compatibleBytes, 3, body.Length);
        return true;
    }

    private static string BuildHostCompatibilityBlock(
        string exportName,
        string newline)
    {
        var lines = new List<string>
        {
            "",
            HostCompatibilityMarker,
            "do",
            $"    local __gcetHost = {exportName}",
            "    if type(__gcetHost) == \"table\" then",
            "        local __gcetApi = type(__gcetHost.GCET) == \"table\" and __gcetHost.GCET or {}",
            "        __gcetHost.GCET = __gcetApi",
            $"        __gcetApi.version = \"{HostCompatibilityVersion}\"",
            "",
            "        local __gcetFrame = 0",
            "        local __gcetUpdateSubs = {}",
            "        local __gcetDrawSubs = {}",
            "        local __gcetUpdateInstalled = false",
            "        local __gcetDrawInstalled = false",
            "",
            "        local function __gcetHandle(entry)",
            "            local handle = {}",
            "            function handle.unsubscribe() entry.active = false end",
            "            handle.Cancel = handle.unsubscribe",
            "            function handle.SetActive(value) entry.active = value == true end",
            "            function handle.Pause() handle.SetActive(false) end",
            "            function handle.Resume() handle.SetActive(true) end",
            "            function handle.IsActive() return entry.active == true end",
            "            return handle",
            "        end",
            "",
            "        local function __gcetSubscribe(bucket, fn, owner)",
            "            if type(fn) ~= \"function\" then return nil end",
            "            local entry = { fn = fn, owner = owner or \"unscoped\", active = true }",
            "            bucket[#bucket + 1] = entry",
            "            return __gcetHandle(entry)",
            "        end",
            "",
            "        local function __gcetDispatch(bucket, ...)",
            "            local count = #bucket",
            "            for i = 1, count do",
            "                local entry = bucket[i]",
            "                if entry and entry.active then",
            "                    pcall(entry.fn, ...)",
            "                end",
            "            end",
            "        end",
            "",
            "        local function __gcetEnsureUpdate()",
            "            if __gcetUpdateInstalled then return true end",
            "            local ok = pcall(function()",
            "                registerForEvent(\"onUpdate\", function(delta)",
            "                    __gcetFrame = __gcetFrame + 1",
            "                    __gcetDispatch(__gcetUpdateSubs, delta)",
            "                end)",
            "            end)",
            "            if ok then __gcetUpdateInstalled = true end",
            "            return ok",
            "        end",
            "",
            "        local function __gcetEnsureDraw()",
            "            if __gcetDrawInstalled then return true end",
            "            local ok = pcall(function()",
            "                registerForEvent(\"onDraw\", function()",
            "                    __gcetDispatch(__gcetDrawSubs)",
            "                end)",
            "            end)",
            "            if ok then __gcetDrawInstalled = true end",
            "            return ok",
            "        end",
            "",
            "        local __gcetHostMakeEventRegistrar = __gcetHost.MakeEventRegistrar",
            "",
            "        local function __gcetLocalMakeEventRegistrar(modName, fallbackRegister)",
            "            local owner = (type(modName) == \"string\" and modName ~= \"\") and modName or \"unscoped\"",
            "            return function(eventName, callback)",
            "                if eventName == \"onUpdate\" and type(callback) == \"function\" then",
            "                    if __gcetEnsureUpdate() then",
            "                        return __gcetSubscribe(__gcetUpdateSubs, callback, owner)",
            "                    end",
            "                elseif eventName == \"onDraw\" and type(callback) == \"function\" then",
            "                    if __gcetEnsureDraw() then",
            "                        return __gcetSubscribe(__gcetDrawSubs, callback, owner)",
            "                    end",
            "                end",
            "",
            "                if type(fallbackRegister) == \"function\" then",
            "                    return fallbackRegister(eventName, callback)",
            "                end",
            "                return nil",
            "            end",
            "        end",
            "",
            "        function __gcetApi.MakeEventRegistrar(modName, fallbackRegister)",
            "            if type(__gcetHostMakeEventRegistrar) == \"function\" then",
            "                local ok, registrar = pcall(__gcetHostMakeEventRegistrar, modName, fallbackRegister)",
            "                if ok and type(registrar) == \"function\" then return registrar end",
            "                ok, registrar = pcall(__gcetHostMakeEventRegistrar, __gcetHost, modName, fallbackRegister)",
            "                if ok and type(registrar) == \"function\" then return registrar end",
            "            end",
            "            return __gcetLocalMakeEventRegistrar(modName, fallbackRegister)",
            "        end",
            "",
            "        if type(__gcetHost.MakeEventRegistrar) ~= \"function\" then",
            "            __gcetHost.MakeEventRegistrar = __gcetApi.MakeEventRegistrar",
            "        end",
            "",
            "        local __gcetHostSubscribeAction = __gcetHost.SubscribeAction",
            "        local __gcetActionRouter = nil",
            "        local __gcetActionObserverInstalled = false",
            "",
            "        local function __gcetEnsureActionRouter(phase)",
            "            if __gcetActionRouter == nil then",
            "                local ok, router = pcall(require, \"modules/G-CET/ActionRouter\")",
            "                if not ok or type(router) ~= \"table\" or type(router.Subscribe) ~= \"function\" or type(router.Dispatch) ~= \"function\" then",
            "                    return false",
            "                end",
            "                __gcetActionRouter = router",
            "                if type(router.Init) == \"function\" then",
            "                    pcall(router.Init, nil, nil, function() return __gcetFrame end)",
            "                end",
            "            end",
            "",
            "            if phase ~= \"after\" and not __gcetActionObserverInstalled then",
            "                local ok = pcall(function()",
            "                    Observe(\"PlayerPuppet\", \"OnAction\", function(player, action, consumer)",
            "                        __gcetActionRouter.Dispatch(player, action, consumer)",
            "                    end)",
            "                end)",
            "                if not ok then return false end",
            "                __gcetActionObserverInstalled = true",
            "            end",
            "            return true",
            "        end",
            "",
            "        function __gcetApi.SubscribeAction(config, fn, source)",
            "            local phase = type(config) == \"table\" and config.phase or \"before\"",
            "            -- Legacy host methods may silently ignore phase=after.",
            "            -- All after clients use the G-CET-owned phase-aware router.",
            "            if phase ~= \"after\" and type(__gcetHostSubscribeAction) == \"function\" then",
            "                local ok, handle = pcall(__gcetHostSubscribeAction, config, fn, source)",
            "                if ok and handle ~= nil then return handle end",
            "                ok, handle = pcall(__gcetHostSubscribeAction, __gcetHost, config, fn, source)",
            "                if ok and handle ~= nil then return handle end",
            "            end",
            "            if not __gcetEnsureActionRouter(phase) then",
            "                error(\"G-CET ActionRouter could not initialize\")",
            "            end",
            "            if phase == \"after\" and type(__gcetActionRouter.DispatchAfter) ~= \"function\" then",
            "                return nil",
            "            end",
            "            return __gcetActionRouter.Subscribe(config, fn, source or \"unscoped\")",
            "        end",
            "",
            "        if type(__gcetHost.SubscribeAction) ~= \"function\" then",
            "            __gcetHost.SubscribeAction = __gcetApi.SubscribeAction",
            "        end",
            "",
            "        local __gcetHostGetPlayer = __gcetHost.GetPlayer",
            "        local function __gcetCallProvider(hostGetter, fallbackGetter)",
            "            if type(hostGetter) == \"function\" then",
            "                local ok, value = pcall(hostGetter)",
            "                if ok and value ~= nil then return value end",
            "                ok, value = pcall(hostGetter, __gcetHost)",
            "                if ok and value ~= nil then return value end",
            "            end",
            "            if type(fallbackGetter) == \"function\" then",
            "                local ok, value = pcall(fallbackGetter)",
            "                if ok then return value end",
            "            end",
            "            return nil",
            "        end",
            "",
            "        function __gcetApi.GetPlayer()",
            "            return __gcetCallProvider(__gcetHostGetPlayer, function() return Game.GetPlayer() end)",
            "        end",
            "",
            "        if type(__gcetHost.GetPlayer) ~= \"function\" then",
            "            __gcetHost.GetPlayer = __gcetApi.GetPlayer",
            "        end",
            ""
        };

        foreach (var provider in SharedProviderCatalog.RuntimeProviders)
        {
            lines.Add(
                $"        local __gcetHost{provider.Getter} = __gcetHost.{provider.Getter}");
        }

        lines.AddRange(
        [
            "",
            "        local __gcetSharedProviderValues = {}",
            "        local __gcetScriptableSystemValues = {}",
            "        local __gcetSharedProviderEpoch = nil",
            "        local __gcetSharedProviderGetters = {"
        ]);

        foreach (var provider in SharedProviderCatalog.RuntimeProviders)
        {
            lines.Add(
                $"            {provider.Provider} = function() return __gcetCallProvider(__gcetHost{provider.Getter}, function() return Game.{provider.Getter}() end) end,");
        }

        lines.AddRange(
        [
            "        }",
            "",
            "        local function __gcetSyncSharedProviderEpoch()",
            "            local player = __gcetApi.GetPlayer()",
            "            if __gcetSharedProviderEpoch ~= player then",
            "                __gcetSharedProviderEpoch = player",
            "                __gcetSharedProviderValues = {}",
            "                __gcetScriptableSystemValues = {}",
            "            end",
            "        end",
            "",
            "        local function __gcetGetSharedProvider(key)",
            "            __gcetSyncSharedProviderEpoch()",
            "            local value = __gcetSharedProviderValues[key]",
            "            if value == nil then",
            "                local getter = __gcetSharedProviderGetters[key]",
            "                if getter ~= nil then",
            "                    value = getter()",
            "                    if value ~= nil then",
            "                        __gcetSharedProviderValues[key] = value",
            "                    end",
            "                end",
            "            end",
            "            return value",
            "        end",
            ""
        ]);

        foreach (var provider in SharedProviderCatalog.RuntimeProviders)
        {
            lines.Add($"        function __gcetApi.{provider.Getter}()");
            lines.Add(SharedProviderCatalog.MustReadLive(provider)
                ? $"            return Game.{provider.Getter}()"
                : $"            return __gcetGetSharedProvider(\"{provider.Provider}\")");
            lines.Add("        end");
            lines.Add(
                $"        if type(__gcetHost.{provider.Getter}) ~= \"function\" then");
            lines.Add(
                $"            __gcetHost.{provider.Getter} = __gcetApi.{provider.Getter}");
            lines.Add("        end");
            lines.Add("");
        }

        lines.AddRange(
        [
            "        function __gcetApi.GetScriptableSystem(name)",
            "            __gcetSyncSharedProviderEpoch()",
            "            local key = tostring(name or \"\")",
            "            if key == \"\" then return nil end",
            "            local value = __gcetScriptableSystemValues[key]",
            "            if value ~= nil then return value end",
            "            local container = __gcetApi.GetScriptableSystemsContainer()",
            "            if container == nil then return nil end",
            "            local ok, resolved = pcall(function() return container:Get(CName.new(key)) end)",
            "            if ok and resolved ~= nil then",
            "                __gcetScriptableSystemValues[key] = resolved",
            "                return resolved",
            "            end",
            "            return nil",
            "        end",
            "        if type(__gcetHost.GetScriptableSystem) ~= \"function\" then",
            "            __gcetHost.GetScriptableSystem = __gcetApi.GetScriptableSystem",
            "        end",
            ""
        ]);

        lines.Add("    end");
        lines.Add("end");
        lines.Add("");

        return string.Join(newline, lines);
    }

    // Repair the SAME authoritative runtime producer, including installed
    // earlier G-CET provider stages. A marker alone never establishes safe
    // lifetime semantics for the live game/menu request handler.
    private static string EnsureVolatileProviderAccessor(
        string source, string runtimeTable, bool required)
    {
        const string getter = "GetSystemRequestsHandler";
        var methodPattern = @"(?m)^[ \t]*function[ \t]+" +
            Regex.Escape(runtimeTable) + @"\." + getter +
            @"[ \t]*\([ \t]*\)[ \t]*\r?\n" +
            @"(?<body>[ \t]*return[^\r\n]*)\r?\n" +
            @"[ \t]*end[ \t]*\r?$";
        var matches = Regex.Matches(source, methodPattern, RegexOptions.CultureInvariant);
        if (matches.Count == 0 && !required)
            return source;
        if (matches.Count != 1)
            throw new InvalidOperationException(
                "0-Engine volatile system-requests accessor missing or duplicated for " +
                runtimeTable);
        var match = matches[0];
        var body = match.Groups["body"].Value.Trim();
        const string oldBody = "return __gcetGetSharedProvider(\"SYSTEM_REQUESTS_HANDLER\")";
        const string newBody = "return Game.GetSystemRequestsHandler()";
        if (body == newBody)
            return source;
        if (body != oldBody)
            throw new InvalidOperationException(
                "0-Engine system-requests accessor behavior drift; cannot prove safe upgrade.");
        return source[..match.Groups["body"].Index] +
            match.Groups["body"].Value.Replace(oldBody, newBody, StringComparison.Ordinal) +
            source[(match.Groups["body"].Index + match.Groups["body"].Length)..];
    }

    private static byte[] AddSharedSystemAccessors(byte[] baseline)
    {
        var text = Encoding.UTF8.GetString(baseline);
        if (text.Contains(SharedSystemMarker, StringComparison.Ordinal))
        {
            var upgraded = EnsureVolatileProviderAccessor(
                text, "Engine", required: true);
            // Real installed runtimes can contain BOTH the known Engine
            // accessors and the earlier G-CET host compatibility adapter.
            // Generated Lua prefers Engine.GCET when its getter exists. The
            // previous repair fixed Engine but left that selected adapter
            // returning a stale native context. Upgrade both from the same
            // authoritative runtime source, never via a post-generation pass.
            if (upgraded.Contains(HostCompatibilityMarker, StringComparison.Ordinal))
                upgraded = EnsureVolatileProviderAccessor(
                    upgraded, "__gcetApi", required: true);
            return Encoding.UTF8.GetBytes(upgraded);
        }

        var playerAccessor = Regex.Match(
            text,
            @"function\s+Engine\.GetPlayer\s*\(\s*\)\s*\r?\n\s*return\s+GetPlayer\s*\(\s*\)\s*\r?\nend",
            RegexOptions.CultureInvariant);

        if (!playerAccessor.Success)
        {
            throw new InvalidOperationException(
                "Installed 0-Engine source does not expose the legacy Engine.GetPlayer accessor anchor.");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = new List<string>
        {
            "",
            SharedSystemMarker,
            "local __gcetSharedProviderValues = {}",
            "local __gcetScriptableSystemValues = {}",
            "local __gcetSharedProviderEpoch = nil",
            "local __gcetSharedProviderGetters = {"
        };

        foreach (var provider in SharedProviderCatalog.RuntimeProviders)
        {
            lines.Add(
                $"    {provider.Provider} = function() return Game.{provider.Getter}() end,");
        }

        lines.AddRange(
        [
            "}",
            "",
            "local function __gcetSyncSharedProviderEpoch()",
            "    local player = GetPlayer()",
            "    if __gcetSharedProviderEpoch ~= player then",
            "        __gcetSharedProviderEpoch = player",
            "        __gcetSharedProviderValues = {}",
            "        __gcetScriptableSystemValues = {}",
            "    end",
            "end",
            "",
            "local function __gcetGetSharedProvider(key)",
            "    __gcetSyncSharedProviderEpoch()",
            "    local value = __gcetSharedProviderValues[key]",
            "    if value == nil then",
            "        local getter = __gcetSharedProviderGetters[key]",
            "        if getter ~= nil then",
            "            value = getter()",
            "            if value ~= nil then",
            "                __gcetSharedProviderValues[key] = value",
            "            end",
            "        end",
            "    end",
            "    return value",
            "end",
            ""
        ]);

        foreach (var provider in SharedProviderCatalog.RuntimeProviders)
        {
            lines.Add($"function Engine.{provider.Getter}()");
            lines.Add(SharedProviderCatalog.MustReadLive(provider)
                ? $"    return Game.{provider.Getter}()"
                : $"    return __gcetGetSharedProvider(\"{provider.Provider}\")");
            lines.Add("end");
            lines.Add("");
        }

        lines.AddRange(
        [
            "function Engine.GetScriptableSystem(name)",
            "    __gcetSyncSharedProviderEpoch()",
            "    local key = tostring(name or \"\")",
            "    if key == \"\" then return nil end",
            "    local value = __gcetScriptableSystemValues[key]",
            "    if value ~= nil then return value end",
            "    local container = Engine.GetScriptableSystemsContainer()",
            "    if container == nil then return nil end",
            "    local ok, resolved = pcall(function() return container:Get(CName.new(key)) end)",
            "    if ok and resolved ~= nil then",
            "        __gcetScriptableSystemValues[key] = resolved",
            "        return resolved",
            "    end",
            "    return nil",
            "end",
            ""
        ]);

        var block = string.Join(newline, lines);
        var insertAt = playerAccessor.Index + playerAccessor.Length;
        var augmented = text.Insert(insertAt, block);
        if (augmented.Contains(HostCompatibilityMarker, StringComparison.Ordinal))
            augmented = EnsureVolatileProviderAccessor(
                augmented, "__gcetApi", required: true);
        return Encoding.UTF8.GetBytes(augmented);
    }

    private static byte[] DecodeBase64(string path, string logicalName)
    {
        try
        {
            var encoded = File.ReadAllText(path, Encoding.ASCII).Trim();
            return Convert.FromBase64String(encoded);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Bundled fixed 0-Engine payload could not be decoded: {logicalName} ({path}). {ex.Message}",
                ex);
        }
    }

    private static byte[] DecodeGzipBase64(string path, string logicalName)
    {
        try
        {
            var encoded = File.ReadAllText(path, Encoding.ASCII).Trim();
            var compressed = Convert.FromBase64String(encoded);

            using var input = new MemoryStream(compressed, writable: false);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Bundled fixed 0-Engine payload could not be decoded: {logicalName} ({path}). {ex.Message}",
                ex);
        }
    }

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
