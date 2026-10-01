using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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
    internal const string FixedVersion = "0.18.11-PASS4.2.1-PHASE-CADENCE-FIX";

    internal const string BaseInitSha256 =
        "c2113cabc10b7f270f7be5542cfa9a8fcc87734913c0f17510eddd1037bca46f";

    internal const string FixedInitSha256 =
        "a0e6480c9404e968e30e573a36fd92b5a87310ba91bfac305a80aad04938e2ef";

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
        var liveState =
            liveHash.Equals(BaseInitSha256, StringComparison.OrdinalIgnoreCase)
                ? "BASE_0.18.6"
                : liveHash.Equals(FixedInitSha256, StringComparison.OrdinalIgnoreCase)
                    ? "ALREADY_FIXED"
                    : "UNSUPPORTED";

        if (liveState == "UNSUPPORTED")
        {
            throw new InvalidOperationException(
                "The installed 0-Engine init.lua is not the exact supported base or fixed runtime. " +
                $"Supported base: {BaseVersion} ({BaseInitSha256}). " +
                $"Supported fixed runtime: {FixedVersion} ({FixedInitSha256}). " +
                $"Installed SHA256: {liveHash}. " +
                "G-CET will not overwrite an unknown 0-Engine revision.");
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
        if (!fixedInitHash.Equals(FixedInitSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Bundled fixed 0-Engine init payload failed its SHA256 integrity check.");
        }

        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["0-Engine/init.lua"] = fixedInit
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
            FixedInitSha256);
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
