using System.Diagnostics;
using System.Text.Json;
using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Authoritative C# lifecycle engine for the standalone CET Runtime Profiler.
/// GUI and headless/TOTAL integration both call this service.
/// </summary>
public sealed class ProfilerService : IProfilerService
{
    private readonly string packageRoot;
    private readonly string payloadRoot;
    private readonly string resultsRoot;
    private readonly PackageManifest manifest;
    private readonly BindingService bindings;
    private readonly ZeroEngineService zeroEngine;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public ProfilerService(string? packageRoot = null)
    {
        this.packageRoot = Path.GetFullPath(packageRoot ?? AppContext.BaseDirectory);
        payloadRoot = Path.Combine(this.packageRoot, "payload");
        resultsRoot = Path.Combine(this.packageRoot, "RESULTS");

        var manifestPath = Path.Combine(this.packageRoot, "MANIFEST.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("MANIFEST.json is missing from the profiler package.", manifestPath);

        manifest = JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(manifestPath), JsonOptions)
            ?? throw new InvalidOperationException("MANIFEST.json could not be parsed.");

        if (string.IsNullOrWhiteSpace(manifest.PackageVersion) ||
            string.IsNullOrWhiteSpace(manifest.TargetCet.ProfilerSha256) ||
            manifest.LiveResultFiles.Count == 0)
            throw new InvalidOperationException("MANIFEST.json is incomplete.");

        bindings = new BindingService();
        zeroEngine = new ZeroEngineService(manifest);
    }

    public ProfilerStatus GetStatus(string gameRoot)
    {
        var paths = GetValidatedPaths(gameRoot);
        var officialHash = manifest.TargetCet.OfficialSha256.ToLowerInvariant();
        var profilerHash = manifest.TargetCet.ProfilerSha256.ToLowerInvariant();
        var liveHash = FileSystemService.Sha256(paths.LiveAsi);

        var cet = liveHash is null
            ? "MISSING"
            : HashEquals(liveHash, officialHash)
                ? "OFFICIAL"
                : HashEquals(liveHash, profilerHash)
                    ? "PROFILER_ACTIVE"
                    : "UNKNOWN";

        var state = ReadState(paths, allowMissing: true);
        var zeroPresent = File.Exists(paths.ZeroInit);
        var initState = zeroPresent
            ? zeroEngine.GetInitState(paths)
            : new ZeroFileState("absent", "0-ENGINE NOT FOUND");

        var schedulerText = "-";
        if (zeroPresent)
        {
            schedulerText = initState.Kind switch
            {
                "integrated" => zeroEngine.GetSchedulerState(paths).Text,
                "adaptive" or "profiler-bridge" => zeroEngine.GetAdaptiveSchedulerState(paths).Text,
                _ => "CHECK CORE PROFILER MODE"
            };
        }

        return new ProfilerStatus
        {
            PackageVersion = manifest.PackageVersion,
            TargetCetVersion = manifest.TargetCet.Version,
            GameRoot = paths.Root,
            CetState = cet,
            CetHash = liveHash,
            ZeroEnginePresent = zeroPresent,
            ZeroEngineInitKind = initState.Kind,
            ZeroEngineInit = initState.Text,
            Scheduler = schedulerText,
            Managed = state is not null,
            ManagedMode = state?.ZeroEngine.Mode ?? "",
            ControlsPresent = Directory.Exists(paths.Controls),
            F11Binding = bindings.IsF11Configured(paths),
            LiveResultCount = GetLiveResults(paths).Count,
            ResultsRoot = resultsRoot,
            State = state
        };
    }

    public ProfilerStatus Install(string gameRoot, bool coreProfilerOnly)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        ValidateCore(paths);

        if (Directory.Exists(paths.StateRoot))
            throw new InvalidOperationException("Profiler manager state already exists. Restore/clean the previous managed install first.");

        if (GetLiveResults(paths).Count > 0)
            throw new InvalidOperationException("Live profiler CSVs already exist in the CET folder. Use COLLECT RESULTS / CLEAR LIVE first.");

        var officialHash = manifest.TargetCet.OfficialSha256.ToLowerInvariant();
        var profilerHash = manifest.TargetCet.ProfilerSha256.ToLowerInvariant();
        var liveHash = FileSystemService.Sha256(paths.LiveAsi)!;

        if (!HashEquals(liveHash, officialHash) && !HashEquals(liveHash, profilerHash))
            throw new InvalidOperationException(
                $"Installed CET ASI is neither the exact supported official CET {manifest.TargetCet.Version} binary nor this profiler binary. No files were changed.");

        var profilerSource = Path.Combine(payloadRoot, "cyber_engine_tweaks.PROFILER.asi");
        var schedulerSource = Path.Combine(payloadRoot, "0-Engine", "modules", "Scheduler.lua");
        var adaptiveSchedulerSource = Path.Combine(payloadRoot, "0-Engine", "modules", "CETProfilerScheduler.lua");
        var controlsSource = Path.Combine(payloadRoot, "CETProfilerControls");

        VerifyPayload(profilerSource, profilerHash, "Bundled profiler ASI failed its manifest hash check.");
        VerifyPayload(schedulerSource, manifest.ZeroEngine.ProfilerSchedulerSha256,
            "Bundled profiler-aware Scheduler.lua failed its manifest hash check.");
        VerifyPayload(adaptiveSchedulerSource, manifest.ZeroEngine.ProfilerAdaptiveSchedulerSha256,
            "Bundled CETProfilerScheduler.lua failed its manifest hash check.");
        if (!Directory.Exists(controlsSource))
            throw new InvalidOperationException("Bundled CETProfilerControls is missing.");

        var zeroPresentBefore = File.Exists(paths.ZeroInit);
        if (zeroPresentBefore && !coreProfilerOnly && zeroEngine.GetInitState(paths).Kind == "unsafe")
            throw new InvalidOperationException(
                "0-Engine was found, but its init.lua structure is not recognized as safe for adaptive Scheduler injection. " +
                "Check 'Core profiler only - leave 0-Engine untouched' and install again.");

        Directory.CreateDirectory(paths.StateRoot);

        var controlsPresentBefore = Directory.Exists(paths.Controls);
        var state = new ProfilerState
        {
            PackageVersion = manifest.PackageVersion,
            InstalledUtc = DateTime.UtcNow.ToString("O"),
            CoreProfilerOnly = coreProfilerOnly,
            Asi = new FileTransactionState
            {
                Mode = "",
                OriginalHash = liveHash,
                InstalledHash = profilerHash
            },
            ZeroEngine = new ZeroEngineTransactionState
            {
                Mode = zeroPresentBefore ? "pending" : "absent",
                PresentBefore = zeroPresentBefore
            },
            Controls = new DirectoryTransactionState
            {
                Mode = controlsPresentBefore ? "replaced" : "added"
            },
            Binding = bindings.Snapshot(paths)
        };

        try
        {
            if (controlsPresentBefore)
            {
                state.Controls.OriginalFingerprint = FileSystemService.DirectoryFingerprint(paths.Controls);
                FileSystemService.CopyDirectoryExact(paths.Controls, paths.BackupControlsRoot);
                if (!string.Equals(
                        FileSystemService.DirectoryFingerprint(paths.BackupControlsRoot),
                        state.Controls.OriginalFingerprint,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Existing CETProfilerControls backup verification failed.");
            }

            SaveState(paths, state);

            InstallAsi(paths, state, profilerSource, liveHash, profilerHash);
            InstallZeroEngine(paths, state, coreProfilerOnly, schedulerSource, adaptiveSchedulerSource);

            FileSystemService.CopyDirectoryExact(controlsSource, paths.Controls);
            if (!File.Exists(Path.Combine(paths.Controls, "init.lua")))
                throw new InvalidOperationException("CETProfilerControls deployment verification failed.");

            state.Controls.InstalledFingerprint = FileSystemService.DirectoryFingerprint(paths.Controls);
            SaveState(paths, state);

            bindings.SetDefaultF11(paths);
            if (!bindings.IsF11Configured(paths))
                throw new InvalidOperationException("CETProfilerControls F11 binding verification failed.");

            SaveState(paths, state);
            return GetStatus(paths.Root);
        }
        catch (Exception installError)
        {
            try
            {
                RollbackFailedInstall(paths, state);
            }
            catch (Exception rollbackError)
            {
                throw new AggregateException(
                    "Profiler installation failed and automatic rollback was incomplete. Managed state was preserved for recovery.",
                    installError, rollbackError);
            }

            throw;
        }
    }

    public string? Collect(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        return CollectResultsInternal(paths, allowEmpty: false);
    }

    public string? ResetLive(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        return CollectResultsInternal(paths, allowEmpty: true);
    }

    public string? Restore(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        var state = ReadState(paths, allowMissing: false)
            ?? throw new InvalidOperationException("No managed profiler installation state was found.");

        ValidateRestore(paths, state);

        // Preserve any final live result before touching the managed game files.
        var archived = CollectResultsInternal(paths, allowEmpty: true);

        RestoreAsi(paths, state);
        RestoreZeroEngine(paths, state);
        RestoreControls(paths, state);
        bindings.Restore(paths, state.Binding);

        Directory.Delete(paths.StateRoot, true);
        return archived;
    }

    private void InstallAsi(
        ProfilerPaths paths,
        ProfilerState state,
        string profilerSource,
        string liveHash,
        string profilerHash)
    {
        if (HashEquals(liveHash, profilerHash))
        {
            state.Asi.Mode = "preexisting-profiler";
            SaveState(paths, state);
            return;
        }

        FileSystemService.CopyFileVerified(paths.LiveAsi, paths.BackupAsi, liveHash);

        state.Asi.Mode = "replaced";
        SaveState(paths, state);

        FileSystemService.CopyFileVerified(profilerSource, paths.LiveAsi, profilerHash);

        if (!HashEquals(FileSystemService.Sha256(paths.LiveAsi), profilerHash))
            throw new InvalidOperationException("Profiler ASI deployment verification failed.");

        SaveState(paths, state);
    }

    private void InstallZeroEngine(
        ProfilerPaths paths,
        ProfilerState state,
        bool coreProfilerOnly,
        string schedulerSource,
        string adaptiveSchedulerSource)
    {
        if (!state.ZeroEngine.PresentBefore)
        {
            state.ZeroEngine.Mode = "absent";
            SaveState(paths, state);
            return;
        }

        if (coreProfilerOnly)
        {
            state.ZeroEngine.Mode = "core-only";
            state.ZeroEngine.Init = new FileTransactionState
            {
                Mode = "left-untouched",
                OriginalHash = FileSystemService.Sha256(paths.ZeroInit),
                InstalledHash = FileSystemService.Sha256(paths.ZeroInit)
            };
            state.ZeroEngine.Scheduler.Mode = "left-untouched";
            if (File.Exists(paths.ZeroScheduler))
            {
                state.ZeroEngine.Scheduler.OriginalHash = FileSystemService.Sha256(paths.ZeroScheduler);
                state.ZeroEngine.Scheduler.InstalledHash = state.ZeroEngine.Scheduler.OriginalHash;
            }

            SaveState(paths, state);
            return;
        }

        var initState = zeroEngine.GetInitState(paths);
        state.ZeroEngine.Init.OriginalHash = FileSystemService.Sha256(paths.ZeroInit);

        if (initState.Kind == "integrated")
        {
            state.ZeroEngine.Mode = "integrated";
            state.ZeroEngine.Init.Mode = "preexisting-compatible";
            state.ZeroEngine.Init.InstalledHash = FileSystemService.Sha256(paths.ZeroInit);
            SaveState(paths, state);

            var schedulerState = zeroEngine.GetSchedulerState(paths);
            InstallSchedulerFile(
                paths.ZeroScheduler,
                paths.BackupZeroScheduler,
                schedulerSource,
                manifest.ZeroEngine.ProfilerSchedulerSha256,
                state.ZeroEngine.Scheduler,
                schedulerState,
                paths,
                state);
            return;
        }

        if (initState.Kind is "adaptive" or "profiler-bridge")
        {
            state.ZeroEngine.Mode = "adaptive";

            if (initState.Kind == "profiler-bridge")
            {
                state.ZeroEngine.Init.Mode = "preexisting-profiler-bridge";
                state.ZeroEngine.Init.InstalledHash = FileSystemService.Sha256(paths.ZeroInit);
                SaveState(paths, state);
            }
            else
            {
                FileSystemService.CopyFileVerified(
                    paths.ZeroInit,
                    paths.BackupZeroInit,
                    state.ZeroEngine.Init.OriginalHash);

                state.ZeroEngine.Init.Mode = "patched-adaptive";
                SaveState(paths, state);

                zeroEngine.AddAdaptiveProfilerSchedulerBridge(paths.ZeroInit);
                state.ZeroEngine.Init.InstalledHash = FileSystemService.Sha256(paths.ZeroInit);
                SaveState(paths, state);
            }

            var adaptiveState = zeroEngine.GetAdaptiveSchedulerState(paths);
            InstallSchedulerFile(
                paths.ZeroAdaptiveScheduler,
                paths.BackupZeroAdaptiveScheduler,
                adaptiveSchedulerSource,
                manifest.ZeroEngine.ProfilerAdaptiveSchedulerSha256,
                state.ZeroEngine.AdaptiveScheduler,
                adaptiveState,
                paths,
                state);
            return;
        }

        throw new InvalidOperationException(
            "0-Engine init.lua structure is not recognized. Use Core profiler mode to leave 0-Engine untouched and skip Scheduler integration.");
    }

    private static void InstallSchedulerFile(
        string livePath,
        string backupPath,
        string sourcePath,
        string expectedProfilerHash,
        FileTransactionState transaction,
        ZeroFileState current,
        ProfilerPaths paths,
        ProfilerState state)
    {
        if (current.Kind == "aware")
        {
            transaction.Mode = "preexisting-profiler";
            transaction.OriginalHash = current.Hash;
            transaction.InstalledHash = expectedProfilerHash;
            SaveState(paths, state);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(livePath)!);

        if (current.Kind == "other")
        {
            FileSystemService.CopyFileVerified(livePath, backupPath, current.Hash);
            transaction.Mode = "replaced";
            transaction.OriginalHash = current.Hash;
            SaveState(paths, state);
        }
        else
        {
            transaction.Mode = "added";
            SaveState(paths, state);
        }

        FileSystemService.CopyFileVerified(sourcePath, livePath, expectedProfilerHash);
        transaction.InstalledHash = expectedProfilerHash.ToLowerInvariant();
        SaveState(paths, state);
    }

    private void ValidateRestore(ProfilerPaths paths, ProfilerState state)
    {
        var profilerHash = state.Asi.InstalledHash?.ToLowerInvariant();

        if (state.Asi.Mode == "replaced")
        {
            RequireFile(paths.BackupAsi, "Original CET ASI backup is missing. Restore aborted before changing anything.");
            RequireHash(paths.BackupAsi, state.Asi.OriginalHash, "Original CET ASI backup hash is wrong. Restore aborted before changing anything.");

            var current = FileSystemService.Sha256(paths.LiveAsi);
            if (!HashEquals(current, profilerHash) && !HashEquals(current, state.Asi.OriginalHash))
                throw new InvalidOperationException("Live CET ASI changed after profiler installation. Restore aborted to avoid overwriting user changes.");
        }

        if (state.ZeroEngine.Mode == "bypassed")
        {
            if (!Directory.Exists(paths.BackupZeroRoot))
                throw new InvalidOperationException("Full 0-Engine backup is missing. Restore aborted before changing anything.");

            var expected = state.ZeroEngine.Bypass.OriginalFingerprint;
            if (!string.Equals(FileSystemService.DirectoryFingerprint(paths.BackupZeroRoot), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Full 0-Engine backup fingerprint is wrong. Restore aborted before changing anything.");

            if (Directory.Exists(paths.ZeroRoot) &&
                !string.Equals(FileSystemService.DirectoryFingerprint(paths.ZeroRoot), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("0-Engine reappeared or changed while compatibility mode was active. Restore aborted to avoid overwriting user files.");
        }

        ValidateRestoreFile(
            paths.ZeroInit,
            paths.BackupZeroInit,
            state.ZeroEngine.Init,
            "Original 0-Engine init.lua backup is missing. Restore aborted before changing anything.",
            "Original 0-Engine init.lua backup hash is wrong. Restore aborted before changing anything.",
            "0-Engine init.lua changed after profiler installation. Restore aborted to avoid overwriting user changes.");

        ValidateRestoreFile(
            paths.ZeroScheduler,
            paths.BackupZeroScheduler,
            state.ZeroEngine.Scheduler,
            "Original Scheduler.lua backup is missing. Restore aborted before changing anything.",
            "Original Scheduler.lua backup hash is wrong. Restore aborted before changing anything.",
            "0-Engine Scheduler.lua changed after profiler installation. Restore aborted to avoid overwriting user changes.");

        ValidateRestoreFile(
            paths.ZeroAdaptiveScheduler,
            paths.BackupZeroAdaptiveScheduler,
            state.ZeroEngine.AdaptiveScheduler,
            "Original CETProfilerScheduler.lua backup is missing. Restore aborted before changing anything.",
            "Original CETProfilerScheduler.lua backup hash is wrong. Restore aborted before changing anything.",
            "CETProfilerScheduler.lua changed after profiler installation. Restore aborted to avoid overwriting user changes.");

        ValidateControlsRestore(paths, state.Controls);
    }

    private static void ValidateRestoreFile(
        string livePath,
        string backupPath,
        FileTransactionState transaction,
        string missingBackupMessage,
        string badBackupMessage,
        string changedLiveMessage)
    {
        if (transaction.Mode is "replaced" or "patched-adaptive")
        {
            RequireFile(backupPath, missingBackupMessage);
            RequireHash(backupPath, transaction.OriginalHash, badBackupMessage);

            var current = FileSystemService.Sha256(livePath);
            if (!HashEquals(current, transaction.InstalledHash) && !HashEquals(current, transaction.OriginalHash))
                throw new InvalidOperationException(changedLiveMessage);
        }
        else if (transaction.Mode == "added" && File.Exists(livePath))
        {
            if (!HashEquals(FileSystemService.Sha256(livePath), transaction.InstalledHash))
                throw new InvalidOperationException(
                    $"Profiler-added {Path.GetFileName(livePath)} changed after installation. Restore aborted to avoid deleting user changes.");
        }
    }

    private static void ValidateControlsRestore(ProfilerPaths paths, DirectoryTransactionState controls)
    {
        if (controls.Mode == "replaced")
        {
            if (!Directory.Exists(paths.BackupControlsRoot))
                throw new InvalidOperationException("Original CETProfilerControls backup is missing. Restore aborted before changing anything.");

            if (!string.IsNullOrWhiteSpace(controls.OriginalFingerprint) &&
                !string.Equals(
                    FileSystemService.DirectoryFingerprint(paths.BackupControlsRoot),
                    controls.OriginalFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Original CETProfilerControls backup fingerprint is wrong. Restore aborted.");

            if (Directory.Exists(paths.Controls) &&
                !string.IsNullOrWhiteSpace(controls.InstalledFingerprint))
            {
                var live = FileSystemService.DirectoryFingerprint(paths.Controls);
                var knownInstalled = string.Equals(live, controls.InstalledFingerprint, StringComparison.OrdinalIgnoreCase);
                var alreadyOriginal = string.Equals(live, controls.OriginalFingerprint, StringComparison.OrdinalIgnoreCase);
                if (!knownInstalled && !alreadyOriginal)
                    throw new InvalidOperationException("CETProfilerControls changed after profiler installation. Restore aborted to avoid overwriting user changes.");
            }
        }
        else if ((controls.Mode == "added" || controls.Mode == "profiler-owned") &&
                 Directory.Exists(paths.Controls) &&
                 !string.IsNullOrWhiteSpace(controls.InstalledFingerprint) &&
                 !string.Equals(
                     FileSystemService.DirectoryFingerprint(paths.Controls),
                     controls.InstalledFingerprint,
                     StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Profiler-owned CETProfilerControls changed after installation. Restore aborted to avoid deleting user changes.");
        }
    }

    private static void RestoreAsi(ProfilerPaths paths, ProfilerState state)
    {
        if (state.Asi.Mode != "replaced") return;

        if (HashEquals(FileSystemService.Sha256(paths.LiveAsi), state.Asi.InstalledHash))
            FileSystemService.CopyFileVerified(paths.BackupAsi, paths.LiveAsi, state.Asi.OriginalHash);

        RequireHash(paths.LiveAsi, state.Asi.OriginalHash, "CET ASI restoration failed verification.");
    }

    private static void RestoreZeroEngine(ProfilerPaths paths, ProfilerState state)
    {
        if (state.ZeroEngine.Mode == "bypassed")
        {
            var expected = state.ZeroEngine.Bypass.OriginalFingerprint;
            if (!Directory.Exists(paths.ZeroRoot))
                FileSystemService.CopyDirectoryExact(paths.BackupZeroRoot, paths.ZeroRoot);

            if (!string.Equals(FileSystemService.DirectoryFingerprint(paths.ZeroRoot), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("0-Engine full-folder restoration failed verification.");
        }

        RestoreFile(paths.ZeroInit, paths.BackupZeroInit, state.ZeroEngine.Init, "0-Engine init.lua restoration failed verification.");
        RestoreFile(paths.ZeroScheduler, paths.BackupZeroScheduler, state.ZeroEngine.Scheduler, "Scheduler.lua restoration failed verification.");
        RestoreFile(paths.ZeroAdaptiveScheduler, paths.BackupZeroAdaptiveScheduler, state.ZeroEngine.AdaptiveScheduler,
            "CETProfilerScheduler.lua restoration failed verification.");
    }

    private static void RestoreFile(string livePath, string backupPath, FileTransactionState transaction, string verifyMessage)
    {
        if (transaction.Mode == "replaced" || transaction.Mode == "patched-adaptive")
        {
            if (HashEquals(FileSystemService.Sha256(livePath), transaction.InstalledHash))
                FileSystemService.CopyFileVerified(backupPath, livePath, transaction.OriginalHash);

            RequireHash(livePath, transaction.OriginalHash, verifyMessage);
        }
        else if (transaction.Mode == "added")
        {
            FileSystemService.DeleteFileIfExists(livePath);
        }
    }

    private static void RestoreControls(ProfilerPaths paths, ProfilerState state)
    {
        if (state.Controls.Mode == "replaced")
        {
            FileSystemService.CopyDirectoryExact(paths.BackupControlsRoot, paths.Controls);
            if (!string.IsNullOrWhiteSpace(state.Controls.OriginalFingerprint) &&
                !string.Equals(
                    FileSystemService.DirectoryFingerprint(paths.Controls),
                    state.Controls.OriginalFingerprint,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CETProfilerControls restoration failed verification.");
        }
        else
        {
            FileSystemService.DeleteDirectoryIfExists(paths.Controls);
        }
    }

    private void RollbackFailedInstall(ProfilerPaths paths, ProfilerState state)
    {
        // Reverse in the same ownership order as a normal restore, but do not archive
        // results: install never starts the game and therefore cannot create a capture.
        RestoreZeroEngineBestEffort(paths, state);

        if (state.Asi.Mode == "replaced" && File.Exists(paths.BackupAsi))
            FileSystemService.CopyFileVerified(paths.BackupAsi, paths.LiveAsi, state.Asi.OriginalHash);

        if (state.Controls.Mode == "replaced" && Directory.Exists(paths.BackupControlsRoot))
            FileSystemService.CopyDirectoryExact(paths.BackupControlsRoot, paths.Controls);
        else
            FileSystemService.DeleteDirectoryIfExists(paths.Controls);

        bindings.Restore(paths, state.Binding);
        Directory.Delete(paths.StateRoot, true);
    }

    private static void RestoreZeroEngineBestEffort(ProfilerPaths paths, ProfilerState state)
    {
        if (state.ZeroEngine.AdaptiveScheduler.Mode == "replaced" && File.Exists(paths.BackupZeroAdaptiveScheduler))
            File.Copy(paths.BackupZeroAdaptiveScheduler, paths.ZeroAdaptiveScheduler, true);
        else if (state.ZeroEngine.AdaptiveScheduler.Mode == "added")
            FileSystemService.DeleteFileIfExists(paths.ZeroAdaptiveScheduler);

        if (state.ZeroEngine.Scheduler.Mode == "replaced" && File.Exists(paths.BackupZeroScheduler))
            File.Copy(paths.BackupZeroScheduler, paths.ZeroScheduler, true);
        else if (state.ZeroEngine.Scheduler.Mode == "added")
            FileSystemService.DeleteFileIfExists(paths.ZeroScheduler);

        if (state.ZeroEngine.Init.Mode == "patched-adaptive" && File.Exists(paths.BackupZeroInit))
            File.Copy(paths.BackupZeroInit, paths.ZeroInit, true);

        if (state.ZeroEngine.Mode == "bypassed" &&
            Directory.Exists(paths.BackupZeroRoot) &&
            !Directory.Exists(paths.ZeroRoot))
            FileSystemService.CopyDirectoryExact(paths.BackupZeroRoot, paths.ZeroRoot);
    }

    private string? CollectResultsInternal(ProfilerPaths paths, bool allowEmpty)
    {
        var found = GetLiveResults(paths);
        if (found.Count == 0)
        {
            if (allowEmpty) return null;
            throw new InvalidOperationException("No live profiler CSV files were found in the CET folder.");
        }

        Directory.CreateDirectory(resultsRoot);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var destination = Path.Combine(resultsRoot, stamp);
        var suffix = 1;
        while (Directory.Exists(destination))
            destination = Path.Combine(resultsRoot, $"{stamp}-{suffix++}");

        Directory.CreateDirectory(destination);

        try
        {
            foreach (var source in found)
            {
                var target = Path.Combine(destination, Path.GetFileName(source));
                FileSystemService.CopyFileVerified(source, target);
            }
        }
        catch
        {
            // A failed verification must never clear live files. The incomplete
            // destination is removed so it cannot be mistaken for a valid capture.
            FileSystemService.DeleteDirectoryIfExists(destination);
            throw;
        }

        foreach (var source in found)
            File.Delete(source);

        return destination;
    }

    private List<string> GetLiveResults(ProfilerPaths paths)
    {
        var found = new List<string>();
        foreach (var name in manifest.LiveResultFiles)
        {
            var path = Path.Combine(paths.CetRoot, name);
            if (File.Exists(path)) found.Add(path);
        }

        return found;
    }

    private ProfilerState? ReadState(ProfilerPaths paths, bool allowMissing)
    {
        if (!File.Exists(paths.StateFile))
        {
            if (allowMissing) return null;
            throw new InvalidOperationException("No managed profiler installation state was found.");
        }

        try
        {
            return JsonSerializer.Deserialize<ProfilerState>(File.ReadAllText(paths.StateFile), JsonOptions)
                ?? throw new InvalidOperationException("Managed profiler state is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Managed profiler state.json is invalid. Restore was not attempted.", ex);
        }
    }

    private static void SaveState(ProfilerPaths paths, ProfilerState state)
    {
        Directory.CreateDirectory(paths.StateRoot);
        var temp = paths.StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions) + Environment.NewLine);
        File.Move(temp, paths.StateFile, true);
    }

    private ProfilerPaths GetValidatedPaths(string gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            throw new InvalidOperationException("Cyberpunk 2077 folder is empty.");

        var paths = ProfilerPaths.FromGameRoot(gameRoot);
        if (!Directory.Exists(paths.Plugins))
            throw new InvalidOperationException($"Cyberpunk 2077 folder is invalid or CET plugins folder was not found: {paths.Root}");

        return paths;
    }

    private static void ValidateCore(ProfilerPaths paths)
    {
        if (!File.Exists(paths.LiveAsi))
            throw new InvalidOperationException($"CET ASI not found: {paths.LiveAsi}");
    }

    private static void AssertGameClosed()
    {
        try
        {
            if (Process.GetProcessesByName("Cyberpunk2077").Length > 0)
                throw new InvalidOperationException("Cyberpunk 2077 is running. Close the game before installing, collecting, or restoring.");
        }
        catch (PlatformNotSupportedException)
        {
            // CI/test hosts can still exercise the pure filesystem transaction.
        }
    }

    private static void VerifyPayload(string path, string expectedHash, string message)
    {
        if (!File.Exists(path) || !HashEquals(FileSystemService.Sha256(path), expectedHash))
            throw new InvalidOperationException(message);
    }

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path)) throw new InvalidOperationException(message);
    }

    private static void RequireHash(string path, string? expectedHash, string message)
    {
        if (string.IsNullOrWhiteSpace(expectedHash) ||
            !HashEquals(FileSystemService.Sha256(path), expectedHash))
            throw new InvalidOperationException(message);
    }

    private static bool HashEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
