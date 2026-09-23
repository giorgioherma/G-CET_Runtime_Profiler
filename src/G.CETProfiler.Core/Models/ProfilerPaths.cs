namespace GCETRuntimeProfiler.Core.Models;

internal sealed class ProfilerPaths
{
    public required string Root { get; init; }
    public required string Plugins { get; init; }
    public required string CetRoot { get; init; }
    public required string Mods { get; init; }
    public required string LiveAsi { get; init; }
    public required string ZeroRoot { get; init; }
    public required string ZeroInit { get; init; }
    public required string ZeroScheduler { get; init; }
    public required string ZeroAdaptiveScheduler { get; init; }
    public required string Controls { get; init; }
    public required string Bindings { get; init; }
    public required string LegacyTotalBindingState { get; init; }
    public required string StateRoot { get; init; }
    public required string StateFile { get; init; }
    public required string BackupAsi { get; init; }
    public required string BackupZeroRoot { get; init; }
    public required string BackupZeroInit { get; init; }
    public required string BackupZeroScheduler { get; init; }
    public required string BackupZeroAdaptiveScheduler { get; init; }
    public required string BackupControlsRoot { get; init; }

    public static ProfilerPaths FromGameRoot(string root)
    {
        root = Path.GetFullPath(root);
        var plugins = Path.Combine(root, "bin", "x64", "plugins");
        var cetRoot = Path.Combine(plugins, "cyber_engine_tweaks");
        var mods = Path.Combine(cetRoot, "mods");
        var stateRoot = Path.Combine(plugins, ".cet_runtime_profiler");
        var zeroRoot = Path.Combine(mods, "0-Engine");

        return new ProfilerPaths
        {
            Root = root,
            Plugins = plugins,
            CetRoot = cetRoot,
            Mods = mods,
            LiveAsi = Path.Combine(plugins, "cyber_engine_tweaks.asi"),
            ZeroRoot = zeroRoot,
            ZeroInit = Path.Combine(zeroRoot, "init.lua"),
            ZeroScheduler = Path.Combine(zeroRoot, "modules", "Scheduler.lua"),
            ZeroAdaptiveScheduler = Path.Combine(zeroRoot, "modules", "CETProfilerScheduler.lua"),
            Controls = Path.Combine(mods, "CETProfilerControls"),
            Bindings = Path.Combine(cetRoot, "bindings.json"),
            LegacyTotalBindingState = Path.Combine(cetRoot, ".gctp_cet_profiler_binding_state.json"),
            StateRoot = stateRoot,
            StateFile = Path.Combine(stateRoot, "state.json"),
            BackupAsi = Path.Combine(stateRoot, "cyber_engine_tweaks.ORIGINAL.asi"),
            BackupZeroRoot = Path.Combine(stateRoot, "0-Engine.FULL.ORIGINAL"),
            BackupZeroInit = Path.Combine(stateRoot, "0-Engine.init.ORIGINAL.lua"),
            BackupZeroScheduler = Path.Combine(stateRoot, "0-Engine.Scheduler.ORIGINAL.lua"),
            BackupZeroAdaptiveScheduler = Path.Combine(stateRoot, "0-Engine.CETProfilerScheduler.ORIGINAL.lua"),
            BackupControlsRoot = Path.Combine(stateRoot, "CETProfilerControls.ORIGINAL")
        };
    }
}
