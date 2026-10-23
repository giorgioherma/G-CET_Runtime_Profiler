using System.Text;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Read-only projection of the runtime that the generated optimization ZIP
/// will contain. Author mods and profiler measurements ALWAYS come from the
/// live installation; these projected files are dependency capabilities, never
/// evidence that a mod was already optimized or a callback already executed.
/// </summary>
internal sealed class EffectiveRuntimeView
{
    internal FixedZeroEngineBuild? Framework { get; }
    internal NativeRuntimeBundle? Native { get; }
    internal string? FrameworkError { get; }
    internal bool HasFramework => Framework is not null;
    internal bool HasNative => Native is not null;

    private EffectiveRuntimeView(
        FixedZeroEngineBuild? framework,
        NativeRuntimeBundle? native,
        string? frameworkError)
    {
        Framework = framework;
        Native = native;
        FrameworkError = frameworkError;
    }

    internal static EffectiveRuntimeView Build(string modsRoot)
    {
        // No writes, extracts, or temporary changes to the user's game.
        // This is the SAME framework composition that generation uses.
        FixedZeroEngineBuild? framework = null;
        string? frameworkError = null;
        try
        {
            framework = FixedZeroEngineRuntime.Prepare(modsRoot);
            VerifyFramework(framework);
        }
        catch (InvalidOperationException ex)
        {
            frameworkError = ex.Message;
        }

        // Native runtime always comes from the independently compiled and
        // packaged artifact, never from the player's installed RED4ext tree.
        // Missing optional native payload is reported, not falsely assumed.
        var native = NativeRuntimeBundle.TryLoad(required: false);
        return new EffectiveRuntimeView(framework, native, frameworkError);
    }

    internal FixedZeroEngineBuild RequireFramework() =>
        Framework ?? throw new InvalidOperationException(
            "The effective 0-Engine runtime cannot be safely composed: " +
            (FrameworkError ?? "unknown runtime source or missing dependency") +
            ". No incomplete optimization ZIP was generated.");

    internal NativeRuntimeBundle? NativeForGeneration(bool required)
    {
        if (required && Native is null)
            return NativeRuntimeBundle.TryLoad(required: true);
        return Native;
    }

    internal object Describe() => new
    {
        mode = "READ_ONLY_PROJECTED_FRAMEWORK",
        liveModFilesAreAuthoritative = true,
        projectedFrameworkCountsAsAlreadyOptimized = false,
        profilerInstallOrRestoreModified = false,
        frameworkReady = HasFramework,
        frameworkMode = Framework?.LiveState,
        frameworkError = FrameworkError,
        projectedFrameworkFiles = Framework?.Files.Keys
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray()
            ?? Array.Empty<string>(),
        nativeReady = HasNative,
        nativeArtifactSource = "PACKAGED_CI_BINARY",
        nativeDeploymentPath = HasNative ? NativeRuntimeBundle.GameRootRelativePath : null,
        requiresExplicitOverlayDeployment = true
    };

    private static void VerifyFramework(FixedZeroEngineBuild framework)
    {
        if (!framework.Files.TryGetValue("0-Engine/init.lua", out var init))
            throw new InvalidOperationException("Projected framework has no init.lua.");
        var text = Encoding.UTF8.GetString(init);
        // Check active integration, not presence of unused support files.
        foreach (var api in new[] {
            "MakeEventRegistrar", "SubscribeAction", "Schedule",
            "GCETNativeLocationHost", "GCETWorkQueue"
        })
            if (!text.Contains(api, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Projected 0-Engine init.lua does not wire " + api + ".");

        if (!framework.Files.Keys.Any(path =>
                path.EndsWith("/ActionRouter.lua",
                    StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                "Projected 0-Engine has no ActionRouter implementation.");

        if (!framework.Files.ContainsKey(
                "0-Engine/modules/GCETNativeLocationHost.lua"))
            throw new InvalidOperationException(
                "Projected native location host dependency is absent.");

        if (!framework.Files.ContainsKey(
                "0-Engine/modules/G-CET/Scheduler.lua") &&
            !framework.Files.ContainsKey("0-Engine/modules/Scheduler.lua"))
            throw new InvalidOperationException(
                "Projected scheduler dependency is absent.");
    }
}
