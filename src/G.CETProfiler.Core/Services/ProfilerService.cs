using System.Diagnostics;
using System.Text.Json;
using GCETRuntimeProfiler.Core.Models;

namespace GCETRuntimeProfiler.Core.Services;

/// <summary>
/// Primary C# lifecycle engine for the standalone CET Runtime Profiler.
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

    public string PackageVersion => manifest.PackageVersion;
    public string ResultsRoot => resultsRoot;

    public ProfilerService(string? packageRoot = null)
    {
        this.packageRoot = Path.GetFullPath(packageRoot ?? PackageRootLocator.Resolve());
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
        var schedulerPresent = zeroPresent && File.Exists(paths.ZeroScheduler);
        var schedulerIntegrated = zeroPresent && zeroEngine.IsSchedulerIntegrated(paths.ZeroInit);
        var schedulerState = schedulerPresent
            ? zeroEngine.GetSchedulerState(paths)
            : new ZeroFileState("absent", "ABSENT");
        var schedulerProfilerAware = schedulerState.Kind == "aware";
        var adaptiveProfilerSchedulerPresent =
            zeroPresent &&
            File.Exists(paths.ZeroAdaptiveScheduler) &&
            zeroEngine.GetAdaptiveSchedulerState(paths).Kind == "aware";

        if (zeroPresent)
        {
            schedulerText = initState.Kind switch
            {
                "integrated" => schedulerState.Text,
                "adaptive" or "profiler-bridge" => zeroEngine.GetAdaptiveSchedulerState(paths).Text,
                _ => "CHECK CORE PROFILER MODE"
            };
        }

        var captureBinding = bindings.InspectCaptureBinding(paths);
        var liveResults = GetLiveResults(paths);
        var captureReadyForCollection = HasCompletedCapture(paths);

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
            SchedulerPresent = schedulerPresent,
            SchedulerIntegrated = schedulerIntegrated,
            SchedulerProfilerAware = schedulerProfilerAware,
            AdaptiveProfilerSchedulerPresent = adaptiveProfilerSchedulerPresent,
            Managed = state is not null,
            ManagedMode = state?.ZeroEngine.Mode ?? "",
            ControlsPresent = Directory.Exists(paths.Controls),
            CaptureTitlePresent = File.Exists(paths.CaptureTitle),
            CaptureTitle = File.Exists(paths.CaptureTitle)
                ? ReadCaptureTitle(paths.CaptureTitle)
                : "WORLD",
            CaptureKey = captureBinding.Key,
            CaptureKeyIsDefaultF11 = captureBinding.IsDefaultF11,
            CaptureKeyCode = captureBinding.Code,
            LiveResultCount = liveResults.Count,
            CaptureReadyForCollection = captureReadyForCollection,
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
            throw new InvalidOperationException("Live profiler output already exists in the CET folder. Use COLLECT RESULTS / CLEAR LIVE first.");

        var officialHash = manifest.TargetCet.OfficialSha256.ToLowerInvariant();
        var profilerHash = manifest.TargetCet.ProfilerSha256.ToLowerInvariant();
        var liveHash = FileSystemService.Sha256(paths.LiveAsi)!;

        if (!HashEquals(liveHash, officialHash) && !HashEquals(liveHash, profilerHash))
            throw new InvalidOperationException(
                $"Installed CET ASI is neither the exact supported official CET {manifest.TargetCet.Version} binary nor this profiler binary. No files were changed.");

        var profilerSource = Path.Combine(payloadRoot, "cyber_engine_tweaks.PROFILER.dll");
        var schedulerSource = Path.Combine(payloadRoot, "0-Engine", "modules", "Scheduler.lua");
        var adaptiveSchedulerSource = Path.Combine(payloadRoot, "0-Engine", "modules", "CETProfilerScheduler.lua");
        var controlsSource = Path.Combine(payloadRoot, "CETProfilerControls");

        VerifyPayload(profilerSource, profilerHash, "Bundled native profiler payload failed its manifest hash check.");
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
            // CET owns the user's keybind. G-CET no longer snapshots or restores
            // binding state; it only seeds F11 later if this input has no binding.
            Binding = null
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
            if (!File.Exists(Path.Combine(paths.Controls, "init.lua")) ||
                !File.Exists(paths.CaptureTitle))
                throw new InvalidOperationException("CETProfilerControls deployment verification failed.");

            state.Controls.InstalledFingerprint = FileSystemService.DirectoryFingerprint(paths.Controls);
            SaveState(paths, state);

            bindings.EnsureDefaultF11IfMissing(paths);

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

    public string SaveCaptureTitle(string gameRoot, string captureTitle)
    {
        var paths = GetValidatedPaths(gameRoot);
        var state = ReadState(paths, allowMissing: false)
            ?? throw new InvalidOperationException("No managed profiler installation state was found.");

        if (!Directory.Exists(paths.Controls) || !File.Exists(paths.CaptureTitle))
            throw new InvalidOperationException("CaptureTitle.txt is missing from the installed CETProfilerControls folder.");

        var clean = SafeCaptureTitle(captureTitle);
        var previousText = File.ReadAllText(paths.CaptureTitle);
        var temp = paths.CaptureTitle + ".tmp";

        try
        {
            File.WriteAllText(temp, clean + Environment.NewLine);
            File.Move(temp, paths.CaptureTitle, true);

            state.Controls.InstalledFingerprint = FileSystemService.DirectoryFingerprint(paths.Controls);
            SaveState(paths, state);
            return clean;
        }
        catch
        {
            FileSystemService.DeleteFileIfExists(temp);
            File.WriteAllText(paths.CaptureTitle, previousText);
            throw;
        }
    }

    public string? Collect(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);

        if (!HasCompletedCapture(paths))
            throw new InvalidOperationException(
                "No completed CET capture is ready. Start the profiler in game, then stop/export it before collecting results.");

        return CollectResultsInternal(paths, allowEmpty: false);
    }

    public string? ResetLive(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        return ArchiveCompletedCaptureOrClearTemplates(paths);
    }

    public string? Restore(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        var state = ReadState(paths, allowMissing: false)
            ?? throw new InvalidOperationException("No managed profiler installation state was found.");

        ValidateRestore(paths, state);

        // A real completed capture is archived. Header/template files created merely
        // by loading the profiler are profiler-owned scratch output and are cleared.
        var archived = ArchiveCompletedCaptureOrClearTemplates(paths);

        RestoreAsi(paths, state);
        RestoreZeroEngine(paths, state);
        RestoreControls(paths, state);

        // Leave bindings.json exactly as the user currently configured it.
        // A custom capture key selected while profiling remains their choice.
        Directory.Delete(paths.StateRoot, true);
        return archived;
    }

    public EmergencyRestoreResult EmergencyRestore(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        var state = ReadState(paths, allowMissing: false)
            ?? throw new InvalidOperationException("No managed profiler installation state was found.");

        var actions = new List<EmergencyRestoreAction>();
        var manual = new List<string>();
        string? archived = null;
        var failed = false;

        void NoChange(string component, string path, string message) =>
            actions.Add(new EmergencyRestoreAction
            {
                Component = component,
                Status = "NO CHANGE",
                Path = path,
                Message = message
            });

        void TryStep(string component, string path, string? backupPath, Action action)
        {
            try
            {
                action();
                actions.Add(new EmergencyRestoreAction
                {
                    Component = component,
                    Status = "RESTORED",
                    Path = path,
                    Message = "Independent safety checks passed."
                });
            }
            catch (Exception ex)
            {
                failed = true;
                actions.Add(new EmergencyRestoreAction
                {
                    Component = component,
                    Status = "SKIPPED",
                    Path = path,
                    Message = ex.Message
                });

                var backup = !string.IsNullOrWhiteSpace(backupPath) && (File.Exists(backupPath) || Directory.Exists(backupPath))
                    ? $" Backup preserved at: {backupPath}"
                    : "";
                manual.Add($"{component}: {ex.Message} Live path: {path}.{backup}");
            }
        }

        try
        {
            archived = ArchiveCompletedCaptureOrClearTemplates(paths);
            actions.Add(new EmergencyRestoreAction
            {
                Component = "Live CET results",
                Status = string.IsNullOrWhiteSpace(archived) ? "NO CHANGE" : "ARCHIVED",
                Path = paths.CetRoot,
                Message = string.IsNullOrWhiteSpace(archived)
                    ? "No live profiler CSVs were present."
                    : $"Archived safely to {archived}."
            });
        }
        catch (Exception ex)
        {
            failed = true;
            actions.Add(new EmergencyRestoreAction
            {
                Component = "Live CET results",
                Status = "SKIPPED",
                Path = paths.CetRoot,
                Message = ex.Message
            });
            manual.Add($"Live CET results: {ex.Message} Live CSVs were left untouched in {paths.CetRoot}.");
        }

        if (state.Binding is not null || File.Exists(paths.LegacyTotalBindingState))
        {
            NoChange(
                "CET bindings",
                paths.Bindings,
                "User-controlled binding state is intentionally preserved.");
        }
        else
        {
            NoChange("CET bindings", paths.Bindings, "No recorded binding transaction exists; left untouched.");
        }

        if (state.Controls.Mode is "replaced" or "added" or "profiler-owned")
        {
            TryStep(
                "CETProfilerControls",
                paths.Controls,
                state.Controls.Mode == "replaced" ? paths.BackupControlsRoot : null,
                () =>
                {
                    ValidateControlsRestore(paths, state.Controls);
                    RestoreControls(paths, state);
                });
        }
        else
        {
            NoChange("CETProfilerControls", paths.Controls, $"State mode '{state.Controls.Mode}' is not a profiler-owned mutation; left untouched.");
        }

        if (state.ZeroEngine.Mode == "bypassed")
        {
            TryStep(
                "0-Engine folder",
                paths.ZeroRoot,
                paths.BackupZeroRoot,
                () =>
                {
                    ValidateBypassedZeroRestore(paths, state);
                    RestoreBypassedZeroEngine(paths, state);
                });
        }
        else
        {
            EmergencyRestoreFile(
                "0-Engine init.lua",
                paths.ZeroInit,
                paths.BackupZeroInit,
                state.ZeroEngine.Init,
                actions,
                manual,
                ref failed);

            EmergencyRestoreFile(
                "0-Engine Scheduler.lua",
                paths.ZeroScheduler,
                paths.BackupZeroScheduler,
                state.ZeroEngine.Scheduler,
                actions,
                manual,
                ref failed);

            EmergencyRestoreFile(
                "0-Engine CETProfilerScheduler.lua",
                paths.ZeroAdaptiveScheduler,
                paths.BackupZeroAdaptiveScheduler,
                state.ZeroEngine.AdaptiveScheduler,
                actions,
                manual,
                ref failed);
        }

        if (state.Asi.Mode == "replaced")
        {
            TryStep(
                "CET ASI",
                paths.LiveAsi,
                paths.BackupAsi,
                () =>
                {
                    ValidateAsiRestore(paths, state);
                    RestoreAsi(paths, state);
                });
        }
        else
        {
            NoChange("CET ASI", paths.LiveAsi, $"State mode '{state.Asi.Mode}' means this manager did not replace the user's ASI; left untouched.");
        }

        var complete = !failed;
        if (complete && Directory.Exists(paths.StateRoot))
            Directory.Delete(paths.StateRoot, true);

        var reportPath = WriteEmergencyRestoreReport(paths, actions, manual, archived, complete);

        return new EmergencyRestoreResult
        {
            Ok = true,
            Complete = complete,
            Archived = archived,
            StatePreserved = !complete && Directory.Exists(paths.StateRoot),
            ReportPath = reportPath,
            Actions = actions,
            ManualReview = manual
        };
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
        ValidateAsiRestore(paths, state);

        if (state.ZeroEngine.Mode == "bypassed")
            ValidateBypassedZeroRestore(paths, state);

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
        // Restore uses the saved original for files G-CET manages. The live file may
        // legitimately change while the profiler is active; that must never trap
        // the user in a managed state. Only the integrity of the saved original
        // backup is a restore gate.
        if (transaction.Mode is "replaced" or "patched-adaptive")
        {
            RequireFile(backupPath, missingBackupMessage);
            RequireHash(backupPath, transaction.OriginalHash, badBackupMessage);
        }
    }

    private static void ValidateControlsRestore(ProfilerPaths paths, DirectoryTransactionState controls)
    {
        if (controls.Mode != "replaced")
            return;

        if (!Directory.Exists(paths.BackupControlsRoot))
            throw new InvalidOperationException("Original CETProfilerControls backup is missing. Restore cannot reconstruct the original folder.");

        if (!string.IsNullOrWhiteSpace(controls.OriginalFingerprint) &&
            !string.Equals(
                FileSystemService.DirectoryFingerprint(paths.BackupControlsRoot),
                controls.OriginalFingerprint,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Original CETProfilerControls backup fingerprint is wrong. Restore cannot trust the saved original folder.");
    }

    private static void RestoreAsi(ProfilerPaths paths, ProfilerState state)
    {
        if (state.Asi.Mode != "replaced") return;

        FileSystemService.CopyFileVerified(paths.BackupAsi, paths.LiveAsi, state.Asi.OriginalHash);
        RequireHash(paths.LiveAsi, state.Asi.OriginalHash, "CET ASI restoration failed verification.");
    }

    private static void RestoreZeroEngine(ProfilerPaths paths, ProfilerState state)
    {
        if (state.ZeroEngine.Mode == "bypassed")
        {
            var expected = state.ZeroEngine.Bypass.OriginalFingerprint;
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
        else if (state.Controls.Mode is "added" or "profiler-owned")
        {
            FileSystemService.DeleteDirectoryIfExists(paths.Controls);
        }
    }

    private static void ValidateAsiRestore(ProfilerPaths paths, ProfilerState state)
    {
        if (state.Asi.Mode != "replaced") return;

        RequireFile(paths.BackupAsi, "Original CET ASI backup is missing.");
        RequireHash(paths.BackupAsi, state.Asi.OriginalHash, "Original CET ASI backup hash is wrong.");
    }

    private static void ValidateBypassedZeroRestore(ProfilerPaths paths, ProfilerState state)
    {
        if (state.ZeroEngine.Mode != "bypassed") return;

        if (!Directory.Exists(paths.BackupZeroRoot))
            throw new InvalidOperationException("Full 0-Engine backup is missing.");

        var expected = state.ZeroEngine.Bypass.OriginalFingerprint;
        if (!string.Equals(FileSystemService.DirectoryFingerprint(paths.BackupZeroRoot), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Full 0-Engine backup fingerprint is wrong.");
    }

    private static void RestoreBypassedZeroEngine(ProfilerPaths paths, ProfilerState state)
    {
        var expected = state.ZeroEngine.Bypass.OriginalFingerprint;
        FileSystemService.CopyDirectoryExact(paths.BackupZeroRoot, paths.ZeroRoot);

        if (!string.Equals(FileSystemService.DirectoryFingerprint(paths.ZeroRoot), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("0-Engine full-folder restoration failed verification.");
    }

    private static void EmergencyRestoreFile(
        string component,
        string livePath,
        string backupPath,
        FileTransactionState transaction,
        List<EmergencyRestoreAction> actions,
        List<string> manual,
        ref bool failed)
    {
        if (transaction.Mode is not ("replaced" or "patched-adaptive" or "added"))
        {
            actions.Add(new EmergencyRestoreAction
            {
                Component = component,
                Status = "NO CHANGE",
                Path = livePath,
                Message = $"State mode '{transaction.Mode}' is not a profiler-owned mutation; left untouched."
            });
            return;
        }

        try
        {
            ValidateRestoreFile(
                livePath,
                backupPath,
                transaction,
                $"Original backup is missing for {component}.",
                $"Original backup hash is wrong for {component}.",
                $"{component} changed after profiler installation; it was left untouched.");

            RestoreFile(livePath, backupPath, transaction, $"{component} restoration failed verification.");

            actions.Add(new EmergencyRestoreAction
            {
                Component = component,
                Status = "RESTORED",
                Path = livePath,
                Message = "Independent safety checks passed."
            });
        }
        catch (Exception ex)
        {
            failed = true;
            actions.Add(new EmergencyRestoreAction
            {
                Component = component,
                Status = "SKIPPED",
                Path = livePath,
                Message = ex.Message
            });

            var backup = File.Exists(backupPath) ? $" Backup preserved at: {backupPath}" : "";
            manual.Add($"{component}: {ex.Message} Live path: {livePath}.{backup}");
        }
    }

    private string WriteEmergencyRestoreReport(
        ProfilerPaths paths,
        IReadOnlyList<EmergencyRestoreAction> actions,
        IReadOnlyList<string> manual,
        string? archived,
        bool complete)
    {
        try
        {
            var reportRoot = Path.Combine(resultsRoot, "RecoveryReports");
            Directory.CreateDirectory(reportRoot);

            var reportPath = Path.Combine(
                reportRoot,
                $"EmergencyRestore-{DateTime.Now:yyyyMMdd-HHmmss}.txt");

            var lines = new List<string>
            {
                $"G-CET Runtime Profiler {manifest.PackageVersion}",
                "EMERGENCY RESTORE REPORT",
                $"Created: {DateTimeOffset.Now:O}",
                $"Game: {paths.Root}",
                $"Complete: {(complete ? "YES" : "NO")}",
                $"Managed recovery state preserved: {(!complete && Directory.Exists(paths.StateRoot) ? "YES" : "NO")}",
                $"Managed state/backups: {paths.StateRoot}",
                $"Archived live results: {archived ?? "(none)"}",
                "",
                "Actions:"
            };

            foreach (var action in actions)
                lines.Add($"[{action.Status}] {action.Component} | {action.Path} | {action.Message}");

            if (manual.Count > 0)
            {
                lines.Add("");
                lines.Add("MANUAL REVIEW REQUIRED:");
                lines.AddRange(manual.Select(x => "- " + x));
                lines.Add("");
                lines.Add("Do not delete the .cet_runtime_profiler recovery folder until the skipped items are resolved.");
            }

            File.WriteAllLines(reportPath, lines);
            return reportPath;
        }
        catch
        {
            return "";
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

        // Bindings are user-controlled and are never part of rollback/restore.
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
            Directory.Exists(paths.BackupZeroRoot))
            FileSystemService.CopyDirectoryExact(paths.BackupZeroRoot, paths.ZeroRoot);
    }

    private string? ArchiveCompletedCaptureOrClearTemplates(ProfilerPaths paths)
    {
        var found = GetLiveResults(paths);
        if (found.Count == 0)
            return null;

        if (HasCompletedCapture(paths))
            return CollectResultsInternal(paths, allowEmpty: false);

        // The native profiler creates its CSV shells when it loads. They are not a
        // capture and must never produce a result archive or enable COLLECT.
        foreach (var source in found)
            FileSystemService.DeleteFileIfExists(source);

        return null;
    }

    private static DateTime? GetCaptureStartLocalTime(ProfilerPaths paths)
    {
        var markers = Path.Combine(paths.CetRoot, "CET_Runtime_Profile_Markers.csv");
        if (!File.Exists(markers))
            return null;

        try
        {
            foreach (var line in File.ReadLines(markers))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = line.Split(',');
                if (fields.Length < 4)
                    continue;

                var label = fields[3].Trim().Trim('"');
                if (!label.Equals("START", StringComparison.OrdinalIgnoreCase))
                    continue;

                var rawEpoch = fields[2].Trim().Trim('"');
                if (!long.TryParse(rawEpoch, out var epochMs) || epochMs <= 0)
                    return null;

                return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime;
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static bool HasCompletedCapture(ProfilerPaths paths)
    {
        var markers = Path.Combine(paths.CetRoot, "CET_Runtime_Profile_Markers.csv");
        if (!File.Exists(markers))
            return false;

        var sawStart = false;
        var sawStop = false;

        try
        {
            foreach (var line in File.ReadLines(markers))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                foreach (var rawField in line.Split(','))
                {
                    var field = rawField.Trim().Trim('"');
                    if (field.Equals("START", StringComparison.OrdinalIgnoreCase))
                        sawStart = true;
                    else if (field.Equals("STOP", StringComparison.OrdinalIgnoreCase))
                        sawStop = true;
                }

                if (sawStart && sawStop)
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private string? CollectResultsInternal(ProfilerPaths paths, bool allowEmpty)
    {
        var found = GetLiveResults(paths);
        if (found.Count == 0)
        {
            if (allowEmpty) return null;
            throw new InvalidOperationException("No live profiler output files were found in the CET folder.");
        }

        Directory.CreateDirectory(resultsRoot);

        var captureTime = GetCaptureStartLocalTime(paths) ?? DateTime.Now;
        var stamp = captureTime.ToString("yyyyMMdd-HHmmss");
        var captureTitle = File.Exists(paths.CaptureTitle)
            ? ReadCaptureTitle(paths.CaptureTitle)
            : "WORLD";
        if (captureTitle == "UNREADABLE")
            captureTitle = "WORLD";

        var baseName = $"CET-{stamp}_{captureTitle}";
        var destination = Path.Combine(resultsRoot, baseName);
        var suffix = 1;
        while (Directory.Exists(destination))
            destination = Path.Combine(resultsRoot, $"{baseName}-{suffix++}");

        Directory.CreateDirectory(destination);

        try
        {
            foreach (var source in found)
            {
                var relative = ResultReportService.GetArchiveRelativePath(Path.GetFileName(source));
                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
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

        // Keep the normalized capture title with the archived result so standalone
        // review and TOTAL handoff do not have to infer it from the folder name.
        File.WriteAllText(
            Path.Combine(destination, "CaptureTitle.txt"),
            captureTitle + Environment.NewLine);

        // Human presentation is deliberately downstream of verified raw collection.
        // A report failure must never discard a valid native capture or leave live
        // profiler output behind merely because presentation could not be built.
        try
        {
            ResultReportService.Generate(destination);
        }
        catch (Exception ex)
        {
            File.WriteAllText(
                Path.Combine(destination, "CET_Report_Error.txt"),
                "The native CET profiler data was archived successfully, but the human-readable report could not be generated."
                + Environment.NewLine + Environment.NewLine
                + ex);
        }

        foreach (var source in found)
            File.Delete(source);

        return destination;
    }

    private List<string> GetLiveResults(ProfilerPaths paths)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Exact names remain the primary contract.
        foreach (var name in manifest.LiveResultFiles)
        {
            var path = Path.Combine(paths.CetRoot, name);
            if (File.Exists(path)) found.Add(path);
        }

        // Scripted profiler-owned output patterns cover small metadata/status/temp
        // companions without ever sweeping arbitrary files from the CET directory.
        // A valid game root can exist before CET creates its own subdirectory.
        if (Directory.Exists(paths.CetRoot))
        {
            foreach (var pattern in manifest.LiveResultPatterns)
            {
                if (string.IsNullOrWhiteSpace(pattern) ||
                    pattern.Contains(Path.DirectorySeparatorChar) ||
                    pattern.Contains(Path.AltDirectorySeparatorChar))
                    continue;

                foreach (var path in Directory.EnumerateFiles(paths.CetRoot, pattern, SearchOption.TopDirectoryOnly))
                    found.Add(path);
            }
        }

        return found
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private static string SafeCaptureTitle(string value)
    {
        var chars = value.Trim().ToUpperInvariant()
            .Take(48)
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_')
            .ToArray();

        var clean = new string(chars).Trim('.', '_');
        if (string.IsNullOrWhiteSpace(clean))
            throw new ArgumentException("Capture title is empty.");

        return clean;
    }

    private static string ReadCaptureTitle(string path)
    {
        try
        {
            var line = File.ReadLines(path)
                .Select(x => x.Trim())
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#'));

            return string.IsNullOrWhiteSpace(line)
                ? "WORLD"
                : SafeCaptureTitle(line);
        }
        catch
        {
            return "UNREADABLE";
        }
    }

    private static bool HashEquals(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
