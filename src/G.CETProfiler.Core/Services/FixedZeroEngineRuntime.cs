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
/// 0-Engine is different: generated callback transforms require the shared
/// ActionRouter/MakeEventRegistrar runtime, so the known fixed runtime overlay is
/// shipped with every generated CET pass.
///
/// The supplied baseline is 0-Engine 0.18.6. We accept only that exact init or
/// the exact fixed init already deployed; an unknown 0-Engine revision is never
/// silently overwritten.
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
                $"Generated G-CET passes require 0-Engine {BaseVersion} as the base dependency. " +
                $"The expected live file was not found: {liveInit}");
        }

        var liveInitBytes = File.ReadAllBytes(liveInit);
        var liveHash = Sha256(liveInitBytes);

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

        var liveState =
            liveHash.Equals(BaseInitSha256, StringComparison.OrdinalIgnoreCase)
                ? "BASE_0.18.6"
                : liveHash.Equals(LegacyFixedInitSha256, StringComparison.OrdinalIgnoreCase)
                    ? "LEGACY_FIXED"
                    : liveHash.Equals(PreviousSharedInitSha256, StringComparison.OrdinalIgnoreCase)
                        ? "PREVIOUS_SHARED_FIXED"
                        : liveHash.Equals(sharedInitHash, StringComparison.OrdinalIgnoreCase)
                            ? "ALREADY_FIXED"
                            : "UNSUPPORTED";

        if (liveState == "UNSUPPORTED")
        {
            throw new InvalidOperationException(
                "The installed 0-Engine init.lua is not the supported base or a recognized G-CET fixed runtime. " +
                $"Supported base: {BaseVersion} ({BaseInitSha256}). " +
                $"Prior fixed runtime: {LegacyFixedInitSha256}. " +
                $"Previous shared-state runtime: {PreviousSharedInitSha256}. " +
                $"Current fixed runtime: {FixedVersion} ({sharedInitHash}). " +
                $"Installed SHA256: {liveHash}. " +
                "G-CET will not overwrite an unknown 0-Engine revision.");
        }

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = sharedInit
        };

        foreach (var pair in FixedModuleHashes)
        {
            var encodedPath = Path.Combine(
                runtimeRoot,
                (pair.Key + ".b64").Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(encodedPath))
            {
                throw new InvalidOperationException(
                    $"Bundled fixed 0-Engine runtime payload is missing for {pair.Key}: {encodedPath}");
            }

            var bytes = DecodeBase64(encodedPath, pair.Key);
            var hash = Sha256(bytes);
            if (!hash.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Bundled fixed 0-Engine runtime failed SHA256 verification: {pair.Key}. " +
                    $"Expected {pair.Value}, got {hash}.");
            }

            files["0-Engine/" + pair.Key] = bytes;
        }

        return new FixedZeroEngineBuild(
            files,
            liveState,
            liveHash,
            FixedVersion,
            sharedInitHash);
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
