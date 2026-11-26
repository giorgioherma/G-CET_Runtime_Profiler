using System.Security.Cryptography;
using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// The independently built native player/location service is distributed in a
/// game-root optimization overlay. Never import it from the player's game.
/// </summary>
internal sealed record NativeRuntimeBundle(byte[] Bytes, string Sha256)
{
    // Historical pinned artifacts retain their original on-disk contract.
    // A renamed DLL must be compiled to read the renamed companion data.
    internal const string GameRootRelativePath =
        "red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll";
    internal const string CanonicalGameRootRelativePath =
        "red4ext/plugins/G-NativeRuntime/G-Engine.dll";
    internal const string CanonicalLocationPath =
        "red4ext/plugins/G-NativeRuntime/G-Native.locations";
    internal bool CanonicalNaming { get; init; }
    internal string DeploymentPath => CanonicalNaming ? CanonicalGameRootRelativePath : GameRootRelativePath;
    internal string LocationDeploymentPath => CanonicalNaming ? CanonicalLocationPath : CompiledNativeLocations.GameRootRelativePath;

    // For a real game-shaped source root (including MO2's effective view),
    // never emit the other runtime alongside an already enabled old/new
    // owner. Loose CI source fixtures do not represent installed plugins.
    internal void RequireNoConflictingInstalledRuntime(string modsRoot)
    {
        var current = new DirectoryInfo(Path.GetFullPath(modsRoot));
        foreach (var part in new[]{"mods","cyber_engine_tweaks","plugins","x64","bin"})
        {
            if (current is null || !current.Name.Equals(part,StringComparison.OrdinalIgnoreCase)) return;
            current = current.Parent;
        }
        if (current is null) return;
        var conflicting = CanonicalNaming ? GameRootRelativePath : CanonicalGameRootRelativePath;
        var plugins = Path.Combine(current.FullName,"red4ext","plugins");
        var opposite = Path.Combine(current.FullName,conflicting.Replace('/',Path.DirectorySeparatorChar));
        var swapped = CanonicalNaming
            ? Path.Combine(plugins,"G-NativeRuntime","GCETStaticLocations.dll")
            : Path.Combine(plugins,"G-CET-StaticLocations","G-Engine.dll");
        if (File.Exists(opposite) || File.Exists(swapped))
            throw new InvalidOperationException("Conflicting native runtime is enabled: "+conflicting+
                ". Disable the previous optimization overlay (or move its old DLL outside red4ext/plugins) before generating. No installed files were changed.");
    }

    // Only an intentionally packaged native artifact can admit this
    // optional backend. The current #88 package does not contain it.
    // Capability is not loaded-game parity or release authorization.
    internal bool HasTypedPoseRead =>
        Encoding.Latin1.GetString(Bytes).Contains("GCETReadPlayerPoseV1",StringComparison.Ordinal);

    internal bool HasTypedPositionEuler =>
        Encoding.Latin1.GetString(Bytes).Contains("GCETReadPlayerPositionEulerV1",StringComparison.Ordinal);

    internal bool HasTypedBlackboardRead
    {
        get
        {
            var content=Encoding.Latin1.GetString(Bytes);
            return content.Contains("GCETReadBlackboard4IntV1",StringComparison.Ordinal)&&
                   content.Contains("GCETReadBlackboard3IntBoolV1",StringComparison.Ordinal);
        }
    }

    internal bool HasSharedSpatialPose
    {
        get
        {
            var content=Encoding.Latin1.GetString(Bytes);
            return content.Contains("GCETReadSharedPlayerPoseV1",StringComparison.Ordinal)&&
                   content.Contains("GCETStaticArmCoarseSpatialV1",StringComparison.Ordinal);
        }
    }

    internal bool HasObserverDemand
    {
        get
        {
            var content=Encoding.Latin1.GetString(Bytes);
            return new[]{"GCETObserverPrepare","GCETObserverBind","GCETObserverSetDemand",
                "GCETObserverRelease","GCETObserverRetire"}
                .All(api=>content.Contains(api,StringComparison.Ordinal));
        }
    }

    internal static NativeRuntimeBundle? TryLoad(bool required)
    {
        return TryLoadFrom(Path.Combine(AppContext.BaseDirectory,"runtime","native"),required);
    }

    internal static NativeRuntimeBundle? TryLoadFrom(string directory,bool required)
    {
        var canonical = Path.Combine(directory,"G-Engine.dll");
        var legacy = Path.Combine(directory,"GCETStaticLocations.dll");
        bool canonicalExists=File.Exists(canonical), legacyExists=File.Exists(legacy);
        if(canonicalExists&&legacyExists)
            throw new InvalidOperationException("Resolver package contains both G-Engine.dll and GCETStaticLocations.dll; refusing ambiguous native ownership.");
        var file = canonicalExists?canonical:legacy;
        if (!File.Exists(file))
        {
            if (required)
                throw new InvalidOperationException(
                    "This pass needs the G-CET RED4ext native location/player " +
                    "service, but the Resolver package is missing runtime/native/" +
                    "G-Engine.dll (or the verified historical GCETStaticLocations.dll). No incomplete optimization ZIP was generated.");
            return null;
        }

        var bytes = File.ReadAllBytes(file);
        if (bytes.Length < 1024 || bytes[0] != (byte)'M' || bytes[1] != (byte)'Z')
            throw new InvalidOperationException(
                "The bundled G-CET native service is not a valid Windows DLL payload.");

        // A filename-only rename of a historical DLL would look for the
        // wrong data file. Verify the compiled UTF-16 manifest-name literal
        // before emitting either layout. Preserve the historical ABI.
        var hasCanonicalName=bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes("G-Native.locations"))>=0;
        if(hasCanonicalName!=canonicalExists)
            throw new InvalidOperationException("Bundled native DLL filename and compiled manifest lookup disagree; no optimization ZIP was generated.");

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
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()){CanonicalNaming=canonicalExists};
    }
}
