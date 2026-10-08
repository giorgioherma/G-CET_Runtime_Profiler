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
    internal const string FixedVersion = "0.18.13-EXPANDED-SHARED-PROVIDERS";

    internal const string BaseInitSha256 =
        "c2113cabc10b7f270f7be5542cfa9a8fcc87734913c0f17510eddd1037bca46f";

    // The repository keeps the last proven fixed init compressed as the baseline.
    // The shared-system layer is injected deterministically at pass generation
    // time so existing PASS4 installs can be upgraded without replacing the
    // entire opaque payload by hand.
    internal const string LegacyFixedInitSha256 =
        "a0e6480c9404e968e30e573a36fd92b5a87310ba91bfac305a80aad04938e2ef";

    internal const string PreviousSharedInitSha256 =
        "8e746ff2e4959b17e1c7616a3e5fa05381913b8d8df0095f3ef6aca46a035c1d";

    private const string SharedSystemMarker =
        "-- G-CET shared providers v2";

    private const string ProfilerBridgeMarker =
        "CET_RUNTIME_PROFILER_ADAPTIVE_SCHEDULER_BEGIN v2";

    private const string HostCompatibilityMarker =
        "-- G-CET host compatibility bridge v1";

    private const string HostCompatibilityVersion =
        "HOST-COMPAT-v1";

    private const string HostActionRouterModule =
        "modules/G-CET/ActionRouter.lua";

    private static readonly IReadOnlyDictionary<string, string> FixedModuleHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modules/ActionRouter.lua"] =
                "4c5ad5f6925b9cb9b2f24ab359eaadc5d54fcf890d661682307ce8cbe09c955c",
            ["modules/Health.lua"] =
                "85b2c6402e54c64f83c1f55e35b5bfee0dc5ebabcdd079c3c8aebb06b010620f",
            ["modules/Scheduler.lua"] =
                "60529e3a1eb1db9e01f88ad44ff223ebe1d538bc55039ec8bb1f8dbed7f6e5b5"
        };

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
        var encodedInitPath = Path.Combine(runtimeRoot, "fixed-init.lua.gz.b64");
        if (!File.Exists(encodedInitPath))
        {
            throw new InvalidOperationException(
                $"Bundled fixed 0-Engine init payload is missing: {encodedInitPath}");
        }

        var fixedInit = DecodeGzipBase64(encodedInitPath, "init.lua");
        var fixedInitHash = Sha256(fixedInit);
        if (!fixedInitHash.Equals(LegacyFixedInitSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Bundled fixed 0-Engine init payload failed its SHA256 integrity check.");
        }

        var sharedInit = AddSharedSystemAccessors(fixedInit);
        var sharedInitHash = Sha256(sharedInit);
        var knownState = ClassifyCompatibleInit(sourceHash, sharedInitHash);

        if (knownState != "UNSUPPORTED")
        {
            var files = BuildKnownFixedRuntime(runtimeRoot, sharedInit);
            return new FixedZeroEngineBuild(
                files,
                statePrefix + knownState,
                liveHash,
                FixedVersion,
                sharedInitHash);
        }

        if (!TryBuildHostCompatibilityInit(
                sourceBytes,
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

        var actionRouter = LoadVerifiedFixedModule(
            runtimeRoot,
            "modules/ActionRouter.lua");

        EnsureHostModuleCollisionSafe(
            liveRoot,
            HostActionRouterModule,
            actionRouter);

        var hostFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = compatibleInit,
            ["0-Engine/" + HostActionRouterModule] = actionRouter
        };

        return new FixedZeroEngineBuild(
            hostFiles,
            statePrefix + "STRUCTURAL_COMPAT",
            liveHash,
            HostCompatibilityVersion,
            Sha256(compatibleInit));
    }


    private static string ClassifyCompatibleInit(
        string hash,
        string currentSharedHash)
    {
        if (hash.Equals(BaseInitSha256, StringComparison.OrdinalIgnoreCase))
            return "BASE_0.18.6";
        if (hash.Equals(LegacyFixedInitSha256, StringComparison.OrdinalIgnoreCase))
            return "LEGACY_FIXED";
        if (hash.Equals(PreviousSharedInitSha256, StringComparison.OrdinalIgnoreCase))
            return "PREVIOUS_SHARED_FIXED";
        if (hash.Equals(currentSharedHash, StringComparison.OrdinalIgnoreCase))
            return "ALREADY_FIXED";
        return "UNSUPPORTED";
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
        byte[] sharedInit)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = sharedInit
        };

        foreach (var pair in FixedModuleHashes)
        {
            files["0-Engine/" + pair.Key] =
                LoadVerifiedFixedModule(runtimeRoot, pair.Key);
        }

        return files;
    }

    private static byte[] LoadVerifiedFixedModule(
        string runtimeRoot,
        string modulePath)
    {
        if (!FixedModuleHashes.TryGetValue(modulePath, out var expectedHash))
        {
            throw new InvalidOperationException(
                $"No bundled hash is registered for 0-Engine runtime module: {modulePath}");
        }

        var encodedPath = Path.Combine(
            runtimeRoot,
            (modulePath + ".b64").Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(encodedPath))
        {
            throw new InvalidOperationException(
                $"Bundled fixed 0-Engine runtime payload is missing for {modulePath}: {encodedPath}");
        }

        var bytes = DecodeBase64(encodedPath, modulePath);
        var hash = Sha256(bytes);
        if (!hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Bundled fixed 0-Engine runtime failed SHA256 verification: {modulePath}. " +
                $"Expected {expectedHash}, got {hash}.");
        }

        return bytes;
    }

    private static void EnsureHostModuleCollisionSafe(
        string liveRoot,
        string relativeModule,
        byte[] intendedBytes)
    {
        var livePath = Path.Combine(
            liveRoot,
            relativeModule.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(livePath))
            return;

        var existingHash = Sha256(File.ReadAllBytes(livePath));
        var intendedHash = Sha256(intendedBytes);
        if (!existingHash.Equals(intendedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "G-CET host compatibility injection found a foreign file at its private support-module path and will not overwrite it. " +
                $"Path: {livePath}. Existing SHA256: {existingHash}. Expected G-CET SHA256: {intendedHash}.");
        }
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
                compatibleBytes = sourceBytes;
                return true;
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
            "        function __gcetApi.MakeEventRegistrar(modName, fallbackRegister)",
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
            "        if type(__gcetHost.MakeEventRegistrar) ~= \"function\" then",
            "            __gcetHost.MakeEventRegistrar = __gcetApi.MakeEventRegistrar",
            "        end",
            "",
            "        local __gcetActionRouter = nil",
            "        local __gcetActionObserverInstalled = false",
            "",
            "        local function __gcetEnsureActionRouter()",
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
            "            if not __gcetActionObserverInstalled then",
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
            "            if not __gcetEnsureActionRouter() then",
            "                error(\"G-CET ActionRouter could not initialize\")",
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
            lines.Add(
                $"            return __gcetGetSharedProvider(\"{provider.Provider}\")");
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

    private static byte[] AddSharedSystemAccessors(byte[] baseline)
    {
        var text = Encoding.UTF8.GetString(baseline);
        if (text.Contains(SharedSystemMarker, StringComparison.Ordinal))
            return baseline;

        var playerAccessor = Regex.Match(
            text,
            @"function\s+Engine\.GetPlayer\s*\(\s*\)\s*\r?\n\s*return\s+GetPlayer\s*\(\s*\)\s*\r?\nend",
            RegexOptions.CultureInvariant);

        if (!playerAccessor.Success)
        {
            throw new InvalidOperationException(
                "Bundled fixed 0-Engine init does not expose the expected Engine.GetPlayer accessor anchor.");
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
            lines.Add($"    return __gcetGetSharedProvider(\"{provider.Provider}\")");
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
