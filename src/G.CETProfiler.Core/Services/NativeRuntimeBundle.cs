using System.Security.Cryptography;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// The independently built native player/location service is distributed in a
/// game-root optimization overlay. Never import it from the player's game.
/// </summary>
internal sealed record NativeRuntimeBundle(byte[] Bytes, string Sha256)
{
    internal const string GameRootRelativePath =
        "red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll";

    internal static NativeRuntimeBundle? TryLoad(bool required)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "runtime",
            "native", "GCETStaticLocations.dll");
        if (!File.Exists(file))
        {
            if (required)
                throw new InvalidOperationException(
                    "This pass needs the G-CET RED4ext native location/player " +
                    "service, but the Resolver package is missing runtime/native/" +
                    "GCETStaticLocations.dll. No incomplete optimization ZIP was generated.");
            return null;
        }

        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new InvalidOperationException(
                "The bundled G-CET native service is not a valid Windows DLL payload.");

        // Check interface family, not mod-source version or binary SHA.
        var content = Encoding.Latin1.GetString(bytes);
        foreach (var api in new[]
        {
            "GCETStaticAddAnchor",
            "GCETStaticCoordinatorState",
            "GCETPlayerVector4Live",
            "GCETPlayerVector4Valid"
        })
        {
            if (!content.Contains(api, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Bundled native service does not expose required API: {api}");
        }
        return new NativeRuntimeBundle(bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}
