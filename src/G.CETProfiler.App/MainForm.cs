using System.Diagnostics;
using GCETRuntimeProfiler.Core.Models;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

public sealed class MainForm : Form
{
    private readonly IProfilerService profiler = new ProfilerService();
    private readonly AppSettings appSettings = AppSettings.Load();

    private readonly Panel setupPage = new();
    private readonly Panel profilerPage = new();

    private readonly TextBox gameRoot = new();
    private readonly Button browseGame = new();
    private readonly Label setupGameStatus = new();

    private readonly CheckBox pairFrameTime = new();
    private readonly TextBox companionExe = new();
    private readonly TextBox companionResults = new();
    private readonly Button browseCompanionExe = new();
    private readonly Button browseCompanionResults = new();
    private readonly Label companionStatus = new();
    private readonly LinkLabel capFrameXLink = new();

    private readonly Label status = new();
    private readonly Label compatibilityText = new();
    private readonly CheckBox coreOnly = new();
    private readonly Label touchedFiles = new();
    private readonly GroupBox readyGroup = new();
    private readonly Label readyHeading = new();
    private readonly Label readyInstructions = new();

    private readonly Button install = new();
    private readonly Button collect = new();
    private readonly Button restore = new();
    private readonly Button openResults = new();
    private readonly Button startCompanion = new();
    private readonly Button startGame = new();
    private readonly Button refresh = new();
    private readonly Label restoreOutcome = new();

    private bool busy;
    private bool loadingSettings = true;
    private bool suppressActivationRefresh;
    private bool fallbackVisible;
    private ProfilerStatus? lastStatus;

    public MainForm()
    {
        Text = $"G-CET Runtime Profiler - v{profiler.PackageVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 690);
        MinimumSize = new Size(880, 730);
        Font = new Font("Segoe UI", 9F);

        BuildSetupPage();
        BuildProfilerPage();
        Controls.Add(profilerPage);
        Controls.Add(setupPage);

        gameRoot.Text = !string.IsNullOrWhiteSpace(appSettings.GameRoot)
            ? appSettings.GameRoot
            : FindInitialGameRoot();
        companionExe.Text = appSettings.ExternalProfilerExe;
        companionResults.Text = appSettings.ExternalResultsDirectory;
        pairFrameTime.Checked = appSettings.PairFrameTimeProfiler;
        loadingSettings = false;
        UpdateCompanionControls();
        RefreshCompanionStatus();
        ShowPage(0);

        Shown += async (_, _) =>
        {
            await RefreshStatusAsync(silent: true);
            RefreshCompanionStatus();
        };

        Activated += async (_, _) =>
        {
            if (busy || suppressActivationRefresh) return;
            await RefreshStatusAsync(silent: true);
            RefreshCompanionStatus();
        };

        FormClosing += (_, _) => SaveSettingsFromUi();
    }

    private void BuildSetupPage()
    {
        setupPage.Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "SETUP",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            Location = new Point(20, 18)
        };
        var subtitle = new Label
        {
            Text = "Select Cyberpunk 2077. Frame-time pairing is optional and never required by the CET profiler.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(22, 55)
        };

        var gameGroup = new GroupBox { Text = "Cyberpunk 2077" };
        gameGroup.SetBounds(20, 88, 820, 92);
        gameRoot.SetBounds(18, 30, 680, 26);
        gameRoot.TextChanged += async (_, _) =>
        {
            ClearRestoreOutcome();
            if (!loadingSettings)
                SaveSettingsFromUi();
            await RefreshStatusAsync(silent: true);
            RenderSetupGameStatus();
        };

        browseGame.Text = "Browse...";
        browseGame.SetBounds(708, 28, 92, 30);
        browseGame.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the Cyberpunk 2077 game folder",
                SelectedPath = Directory.Exists(gameRoot.Text) ? gameRoot.Text : ""
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            gameRoot.Text = dialog.SelectedPath;
            await RefreshStatusAsync(silent: true);
            RenderSetupGameStatus();
        };

        setupGameStatus.SetBounds(18, 62, 780, 20);
        gameGroup.Controls.AddRange([gameRoot, browseGame, setupGameStatus]);

        var companionGroup = new GroupBox { Text = "Optional frame-time capture companion" };
        companionGroup.SetBounds(20, 192, 820, 382);

        pairFrameTime.Text = "Run with a frame-time capture tool";
        pairFrameTime.Font = new Font("Segoe UI Semibold", 10F);
        pairFrameTime.SetBounds(18, 28, 300, 24);
        pairFrameTime.CheckedChanged += (_, _) =>
        {
            if (loadingSettings) return;
            UpdateCompanionControls();
            RefreshCompanionStatus();
            SaveSettingsFromUi();
            if (lastStatus is not null)
                RenderStatus(lastStatus);
            RenderReadyState(lastStatus);
        };

        var explanation = new Label
        {
            Text = "CET Runtime Profiler works standalone. Pairing it with a frame-time capture lets you compare CET/Lua activity with actual frame-time behavior. This build was designed and tested alongside CapFrameX 1.9.1.2 Beta, but you can use a profiler you already have.",
            MaximumSize = new Size(770, 0),
            AutoSize = true,
            Location = new Point(18, 62)
        };

        capFrameXLink.Text = "CapFrameX releases";
        capFrameXLink.AutoSize = true;
        capFrameXLink.Location = new Point(18, 118);
        capFrameXLink.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(CompanionProfilerService.RecommendedProfilerWebsite) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        var syncHint = new Label
        {
            Text = "For synchronized captures, use the same START key. G-CET presets its CET binding to F11 during install.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(150, 118)
        };

        var exeLabel = new Label { Text = "Profiler executable", AutoSize = true, Location = new Point(18, 158) };
        companionExe.SetBounds(18, 180, 680, 26);
        companionExe.TextChanged += (_, _) =>
        {
            if (loadingSettings) return;
            RefreshCompanionStatus();
            SetActionState(lastStatus);
            SaveSettingsFromUi();
            if (lastStatus is not null)
                RenderStatus(lastStatus);
            RenderReadyState(lastStatus);
        };
        browseCompanionExe.Text = "Browse...";
        browseCompanionExe.SetBounds(708, 178, 92, 30);
        browseCompanionExe.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select your frame-time profiler executable",
                Filter = "Executable (*.exe)|*.exe|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            companionExe.Text = dialog.FileName;
            var suggested = CompanionProfilerService.SuggestResultsDirectory(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(suggested) &&
                (string.IsNullOrWhiteSpace(companionResults.Text) || !Directory.Exists(companionResults.Text)))
                companionResults.Text = suggested;

            RefreshCompanionStatus();
        };

        var resultLabel = new Label { Text = "Capture / results folder", AutoSize = true, Location = new Point(18, 218) };
        companionResults.SetBounds(18, 240, 680, 26);
        companionResults.TextChanged += (_, _) =>
        {
            if (loadingSettings) return;
            RefreshCompanionStatus();
            SaveSettingsFromUi();
            SetActionState(lastStatus);
            if (lastStatus is not null)
                RenderStatus(lastStatus);
            RenderReadyState(lastStatus);
        };
        browseCompanionResults.Text = "Browse...";
        browseCompanionResults.SetBounds(708, 238, 92, 30);
        browseCompanionResults.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the frame-time profiler capture/results folder",
                SelectedPath = Directory.Exists(companionResults.Text) ? companionResults.Text : ""
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            companionResults.Text = dialog.SelectedPath;
            RefreshCompanionStatus();
            SetActionState(lastStatus);
            SaveSettingsFromUi();
        };

        companionStatus.SetBounds(18, 282, 780, 62);
        companionStatus.Font = new Font("Consolas", 9.5F);

        companionGroup.Controls.AddRange([
            pairFrameTime, explanation, capFrameXLink, syncHint,
            exeLabel, companionExe, browseCompanionExe,
            resultLabel, companionResults, browseCompanionResults,
            companionStatus
        ]);

        var next = new Button { Text = "CONTINUE →", Width = 140, Height = 38, Left = 700, Top = 602 };
        next.Click += async (_, _) =>
        {
            SaveSettingsFromUi();
            await RefreshStatusAsync();
            if (LooksLikeGameRoot(gameRoot.Text.Trim()))
                ShowPage(1);
        };

        setupPage.Controls.AddRange([title, subtitle, gameGroup, companionGroup, next]);
    }

    private void BuildProfilerPage()
    {
        profilerPage.Dock = DockStyle.Fill;

        var title = new Label
        {
            Text = "INSTALL, CAPTURE & RECOVERY",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            Location = new Point(20, 18)
        };

        var back = new Button { Text = "← SETUP", Width = 100, Height = 30, Left = 740, Top = 18 };
        back.Click += (_, _) =>
        {
            SaveSettingsFromUi();
            ShowPage(0);
        };

        var statusGroup = new GroupBox { Text = "Profiler status" };
        statusGroup.SetBounds(20, 66, 820, 306);
        status.SetBounds(18, 27, 775, 222);
        status.Font = new Font("Segoe UI", 9.5F);

        coreOnly.Text = "Fallback: install CET core profiler only and leave 0-Engine completely untouched";
        coreOnly.SetBounds(18, 252, 610, 24);
        coreOnly.CheckedChanged += (_, _) =>
        {
            SetActionState(lastStatus);
            RenderReadyState(lastStatus);
        };

        refresh.Text = "REFRESH";
        refresh.SetBounds(694, 264, 100, 28);
        refresh.Click += async (_, _) => await RefreshStatusAsync();
        statusGroup.Controls.AddRange([status, coreOnly, refresh]);

        readyGroup.Text = "";
        readyGroup.SetBounds(20, 382, 820, 148);

        readyHeading.SetBounds(18, 18, 775, 28);
        readyHeading.Font = new Font("Segoe UI Semibold", 11F);
        readyHeading.AutoSize = false;

        readyInstructions.SetBounds(18, 48, 775, 90);
        readyInstructions.Font = new Font("Segoe UI", 9.5F);
        readyInstructions.AutoSize = false;
        readyInstructions.Text =
            "1. Run your Frame-time Capture Tool if you're using one and enter the game.\r\n" +
            "2. To start measurement press your shared keybind (F11). To stop capture and prep the results press the same key again (F11).\r\n" +
            "3. Return to installer and COLLECT RESULTS.\r\n" +
            "4. After usage RESTORE ORIGINAL STATE to finish.";

        readyGroup.Controls.AddRange([readyHeading, readyInstructions]);

        install.Text = "INSTALL PROFILER";
        install.SetBounds(20, 544, 230, 42);
        install.Click += async (_, _) => await InstallAsync();

        collect.Text = "COLLECT RESULTS / CLEAR LIVE";
        collect.SetBounds(265, 544, 300, 42);
        collect.Click += async (_, _) => await CollectAsync();

        restore.Text = "RESTORE ORIGINAL STATE";
        restore.SetBounds(580, 544, 260, 42);
        restore.Click += async (_, _) =>
        {
            if (busy) return;

            DialogResult answer;
            suppressActivationRefresh = true;
            try
            {
                answer = MessageBox.Show(
                    this,
                    "Restore the CET profiler-managed game state?\r\n\r\n" +
                    "Original CET / 0-Engine files and the previous CET binding state will be restored. " +
                    "Any current live CET profiler output is archived first.",
                    Text,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
            }
            finally
            {
                suppressActivationRefresh = false;
            }

            if (answer == DialogResult.Yes)
                await RunStrictRestoreAsync();
        };

        openResults.Text = "Open Results Folder";
        openResults.SetBounds(20, 598, 230, 36);
        openResults.Click += (_, _) => OpenResultsFolder();

        startCompanion.Text = "START FRAME-TIME TOOL";
        startCompanion.SetBounds(265, 598, 300, 36);
        startCompanion.Click += (_, _) => StartFrameTimeTool();

        startGame.Text = "START CYBERPUNK";
        startGame.SetBounds(580, 598, 260, 36);
        startGame.Click += (_, _) => StartCyberpunk();

        restoreOutcome.SetBounds(20, 642, 820, 28);
        restoreOutcome.Font = new Font("Segoe UI Semibold", 10F);
        restoreOutcome.TextAlign = ContentAlignment.MiddleLeft;
        restoreOutcome.Visible = false;

        profilerPage.Controls.AddRange([
            title, back, statusGroup, readyGroup,
            install, collect, restore, openResults, startCompanion, startGame, restoreOutcome
        ]);
    }

    private void ShowPage(int page)
    {
        setupPage.Visible = page == 0;
        profilerPage.Visible = page == 1;
        if (page == 0)
        {
            setupPage.BringToFront();
            RenderSetupGameStatus();
            RefreshCompanionStatus();
        }
        else
        {
            profilerPage.BringToFront();
            RenderStatus(lastStatus);
            RenderReadyState(lastStatus);
        }
    }

    private async Task RefreshStatusAsync(bool silent = false)
    {
        if (busy) return;

        var root = gameRoot.Text.Trim();
        if (!LooksLikeGameRoot(root))
        {
            lastStatus = null;
            status.Text =
                "Game: NOT FOUND ❌\r\n" +
                "CET Profiler: unavailable ❌\r\n" +
                "CET Controls: unavailable ❌\r\n" +
                "Live Files: -";
            RenderSetupGameStatus();
            RenderCompatibility(null);
            RenderTouchedFiles(null);
            RenderReadyState(null);
            SetActionState(null);
            return;
        }

        try
        {
            SetBusy(true);
            var snapshot = await Task.Run(() => profiler.GetStatus(root));
            lastStatus = snapshot;
            RenderStatus(snapshot);
            RenderCompatibility(snapshot);
            RenderTouchedFiles(snapshot);
            RenderReadyState(snapshot);
            RenderSetupGameStatus();
            SetBusy(false);
            SetActionState(snapshot);
        }
        catch (Exception ex)
        {
            lastStatus = null;
            SetBusy(false);
            status.Text = "STATUS ERROR:\r\n" + FriendlyMessage(ex);
            RenderSetupGameStatus();
            RenderCompatibility(null);
            RenderTouchedFiles(null);
            RenderReadyState(null);
            SetActionState(null);
            if (!silent)
                MessageBox.Show(this, FriendlyMessage(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderSetupGameStatus()
    {
        if (!LooksLikeGameRoot(gameRoot.Text.Trim()))
        {
            setupGameStatus.Text = "Game: NOT FOUND";
            return;
        }

        setupGameStatus.Text = lastStatus is null
            ? "Game: FOUND"
            : $"Game: FOUND    CET: {lastStatus.CetState}";
    }

    private void RefreshCompanionStatus()
    {
        if (!loadingSettings)
            SyncSettingsFromUi();
        var snapshot = CompanionProfilerService.Inspect(appSettings);

        if (!snapshot.Enabled)
        {
            companionStatus.Text =
                "CET START:      F11 preset on install\r\n" +
                "Frame-time:     DISABLED";
            return;
        }

        var results = string.IsNullOrWhiteSpace(companionResults.Text)
            ? "NOT SET"
            : Directory.Exists(companionResults.Text) ? "FOUND" : "NOT FOUND";

        var keyText = snapshot.StartKeyKnown
            ? snapshot.StartKeyIsF11 ? "F11 ✓" : snapshot.StartKey + "  (use F11 to sync)"
            : "UNKNOWN — verify F11 manually";

        companionStatus.Text =
            $"CET START:      {(lastStatus?.F11Binding == true ? "F11 ✓" : "F11 preset on install")}\r\n" +
            $"Frame-time:     {snapshot.DisplayName} · START {keyText}\r\n" +
            $"Results folder: {results}";
    }

    private void UpdateCompanionControls()
    {
        // The checkbox controls whether the companion participates in this run.
        // Keep the remembered paths visible/editable even when pairing is unchecked.
        companionExe.Enabled = !busy;
        companionResults.Enabled = !busy;
        browseCompanionExe.Enabled = !busy;
        browseCompanionResults.Enabled = !busy;
    }

    private void RenderStatus(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            status.Text =
                "Game: NOT FOUND ❌\r\n" +
                "CET Profiler: unavailable ❌\r\n" +
                "CET Controls: unavailable ❌\r\n" +
                "Live Files: -";
            SetActionState(null);
            return;
        }

        var state = snapshot.State;

        var cetProfiler = snapshot.CetState switch
        {
            "PROFILER_ACTIVE" when snapshot.Managed && state?.Asi.Mode == "replaced"
                => "ACTIVE · original ASI backed up ✅",
            "PROFILER_ACTIVE" when snapshot.Managed && state?.Asi.Mode == "preexisting-profiler"
                => "ACTIVE · profiler ASI already present ✅",
            "PROFILER_ACTIVE" when snapshot.Managed
                => "ACTIVE ✅",
            "OFFICIAL" => "Ready to deploy profiler ASI ⚠️",
            "MISSING" => "CET ASI missing ❌",
            "UNKNOWN" => "Unsupported / unknown CET ASI ❌",
            _ => snapshot.CetState + " ⚠️"
        };

        var controls = snapshot.Managed && snapshot.ControlsPresent
            ? "DEPLOYED ✅"
            : snapshot.ControlsPresent
                ? "Present but not managed by this install ⚠️"
                : snapshot.Managed ? "MISSING AFTER INSTALL ❌" : "Ready to deploy ⚠️";

        var zeroLine = "0-Engine: Not installed · optional ⚠️";
        var schedulerLine = "Scheduler: Not applicable · optional ⚠️";

        if (snapshot.ZeroEnginePresent)
        {
            if (snapshot.Managed && snapshot.ManagedMode == "core-only")
            {
                zeroLine = "0-Engine: Present · intentionally left untouched ✅";
                schedulerLine = "Scheduler: Skipped in core-only mode ✅";
            }
            else if (snapshot.Managed && state is not null)
            {
                zeroLine = state.ZeroEngine.Init.Mode switch
                {
                    "patched-adaptive" => "0-Engine: init.lua backed up and adjusted ✅",
                    "preexisting-compatible" => "0-Engine: compatible integrated init.lua preserved ✅",
                    "preexisting-profiler-bridge" => "0-Engine: existing profiler bridge reused ✅",
                    "left-untouched" => "0-Engine: left untouched ✅",
                    _ => "0-Engine: Present ✅"
                };

                var schedulerState = state.ZeroEngine.Mode == "adaptive"
                    ? state.ZeroEngine.AdaptiveScheduler
                    : state.ZeroEngine.Scheduler;

                schedulerLine = schedulerState.Mode switch
                {
                    "replaced" => "Scheduler: original backed up · profiler-aware scheduler deployed ✅",
                    "added" => "Scheduler: profiler-aware scheduler added ✅",
                    "preexisting-profiler" => "Scheduler: existing profiler-aware scheduler reused ✅",
                    "left-untouched" => "Scheduler: left untouched ✅",
                    _ when snapshot.SchedulerProfilerAware || snapshot.AdaptiveProfilerSchedulerPresent
                        => "Scheduler: profiler-aware ✅",
                    _ => "Scheduler: integration pending ⚠️"
                };
            }
            else
            {
                zeroLine = snapshot.ZeroEngineInitKind == "unsafe"
                    ? "0-Engine: Present · structure needs core-only fallback ⚠️"
                    : "0-Engine: Present · optional integration pending ⚠️";

                schedulerLine = snapshot.SchedulerProfilerAware
                    ? "Scheduler: profiler-aware already present ✅"
                    : "Scheduler: profiler-aware pass not installed yet ⚠️";
            }
        }

        SyncSettingsFromUi();
        var companion = CompanionProfilerService.Inspect(appSettings);
        var companionConfigured =
            pairFrameTime.Checked &&
            companion.ExeFound &&
            Directory.Exists(companionResults.Text.Trim());

        var frameLine = companionConfigured
            ? $"Frame-Time Profiler: {companion.DisplayName} found ✅"
            : "Frame-Time Profiler: Not provided ⚠️";

        string syncLines;
        if (!companionConfigured)
        {
            syncLines =
                "Synced keybind: Need frame capture tool ⚠️\r\n" +
                $"    - CET: {(snapshot.F11Binding ? "F11 ✅" : "F11 pending deployment ⚠️")}";
        }
        else if (companion.StartKeyKnown && companion.StartKeyIsF11 && snapshot.F11Binding)
        {
            syncLines =
                "Synced keybind: YES ✅\r\n" +
                "    - CET: F11 ✅\r\n" +
                "    - Frame-Time Profiler: F11 ✅";
        }
        else
        {
            var externalKey = companion.StartKeyKnown
                ? companion.StartKey + " ⚠️"
                : "Unknown ⚠️";
            syncLines =
                "Synced keybind: NO ⚠️\r\n" +
                $"    - CET: {(snapshot.F11Binding ? "F11 ✅" : "F11 pending deployment ⚠️")}\r\n" +
                $"    - Frame-Time Profiler: {externalKey}";
        }

        var installed = IsProfilerReady(snapshot);
        var blocked = HasCriticalProfilerError(snapshot);
        var installState = installed
            ? "INSTALLED. ✅"
            : blocked ? "BLOCKED. ❌" : "NOT INSTALLED. ⚠️";

        status.Text =
            "Game: Found ✅\r\n" +
            $"CET Profiler: {cetProfiler}\r\n" +
            $"CET Controls: {controls}\r\n" +
            "optional:\r\n" +
            zeroLine + "\r\n" +
            schedulerLine + "\r\n" +
            frameLine + "\r\n" +
            syncLines + "\r\n\r\n" +
            $"G-CET PROFILER IS {installState}\r\n" +
            $"Live Files: {snapshot.LiveResultCount}";
    }

    private bool IsProfilerReady(ProfilerStatus? snapshot) =>
        snapshot is not null &&
        snapshot.Managed &&
        snapshot.CetState == "PROFILER_ACTIVE" &&
        snapshot.ControlsPresent &&
        snapshot.F11Binding;

    private static bool HasCriticalProfilerError(ProfilerStatus? snapshot) =>
        snapshot is null ||
        snapshot.CetState is "MISSING" or "UNKNOWN" ||
        (snapshot.Managed && (!snapshot.ControlsPresent || !snapshot.F11Binding));

    private void RenderReadyState(ProfilerStatus? snapshot)
    {
        var ready = IsProfilerReady(snapshot);
        var blocked = HasCriticalProfilerError(snapshot);

        readyHeading.Text = ready
            ? "PROFILER IS READY!"
            : "PROFILER IS NOT READY!";

        readyHeading.ForeColor = ready
            ? Color.ForestGreen
            : blocked ? Color.Firebrick : Color.DarkGoldenrod;

        readyInstructions.Enabled = ready;
    }

    private void RenderCompatibility(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            coreOnly.Visible = false;
            coreOnly.Checked = false;
            return;
        }

        var unsafeZero = snapshot.ZeroEnginePresent && snapshot.ZeroEngineInitKind == "unsafe";
        var showFallback = !snapshot.Managed && (unsafeZero || fallbackVisible);

        coreOnly.Visible = showFallback;
        if (!showFallback)
            coreOnly.Checked = false;
    }

    private void RenderTouchedFiles(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            touchedFiles.Text = "No files will be changed until a valid game/CET installation is detected.";
            return;
        }

        var zero = snapshot.ZeroEnginePresent && snapshot.ZeroEngineInitKind != "unsafe" && !coreOnly.Checked
            ? " · recognized 0-Engine init/Scheduler integration"
            : "";

        touchedFiles.Text =
            "Managed scope: CET ASI · CET bindings.json · CETProfilerControls" + zero + ".\r\n" +
            "Rule #1: every pre-existing user file/directory we change is copied and verified first. " +
            "Unique files should still have your own backup if this is their only copy. Unknown/unowned files are never deleted.";
    }

    private bool HasConfiguredCompanion() =>
        pairFrameTime.Checked &&
        File.Exists(companionExe.Text.Trim()) &&
        Directory.Exists(companionResults.Text.Trim());

    private void SetActionState(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            install.Enabled = false;
            collect.Enabled = false;
            restore.Enabled = false;
            startCompanion.Enabled = false;
            startGame.Enabled = false;
            coreOnly.Enabled = false;
            return;
        }

        var cetAllowed = snapshot.CetState is "OFFICIAL" or "PROFILER_ACTIVE";
        var unsafeZero = snapshot.ZeroEnginePresent && snapshot.ZeroEngineInitKind == "unsafe";
        var fallbackSatisfied = !unsafeZero || coreOnly.Checked;

        install.Enabled = !busy && cetAllowed && !snapshot.Managed && snapshot.LiveResultCount == 0 && fallbackSatisfied;
        collect.Enabled = !busy && snapshot.LiveResultCount > 0;
        restore.Enabled = !busy && snapshot.Managed;
        coreOnly.Enabled = !busy && coreOnly.Visible && !snapshot.Managed;

        startCompanion.Enabled = !busy && HasConfiguredCompanion();

        var gameExe = GetGameExe(snapshot.GameRoot);
        var gameRunning = IsCyberpunkRunning();
        startGame.Enabled = !busy && File.Exists(gameExe) && !gameRunning;
        startGame.Text = gameRunning ? "CYBERPUNK RUNNING" : "START CYBERPUNK";
    }

    private async Task InstallAsync()
    {
        if (busy) return;

        ClearRestoreOutcome();

        try
        {
            SetBusy(true);
            var result = await Task.Run(() => profiler.Install(gameRoot.Text.Trim(), coreOnly.Visible && coreOnly.Checked));
            fallbackVisible = false;
            MessageBox.Show(
                this,
                "Profiler installed.\r\n\r\n" +
                "CET capture key: F11\r\n" +
                "F11 #1 = START\r\n" +
                "F11 #2 = STOP + AUTO EXPORT\r\n\r\n" +
                "0-Engine mode: " + result.ManagedMode,
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            if (lastStatus?.ZeroEnginePresent == true)
                fallbackVisible = true;

            MessageBox.Show(
                this,
                FriendlyMessage(ex) +
                (lastStatus?.ZeroEnginePresent == true
                    ? "\r\n\r\nIf Scheduler integration is the problem, the CET core-only fallback is now available. It leaves 0-Engine untouched."
                    : ""),
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(silent: true);
        }
    }

    private async Task CollectAsync()
    {
        if (busy) return;

        try
        {
            SaveSettingsFromUi();
            SetBusy(true);
            var destination = await Task.Run(() => profiler.Collect(gameRoot.Text.Trim()));

            CompanionCollectResult? companion = null;
            string? companionError = null;
            string? reportRefreshError = null;
            if (!string.IsNullOrWhiteSpace(destination) && HasConfiguredCompanion())
            {
                try
                {
                    companion = await Task.Run(() => CompanionProfilerService.CollectLatest(appSettings, destination));
                }
                catch (Exception ex)
                {
                    companionError = ex.Message;
                }
            }

            // The core collection creates a CET-only report immediately after raw
            // verification. Once the optional companion copy is complete, rebuild
            // the same standalone report so CapFrameX can become a synchronized
            // evidence layer without changing the native CET capture.
            if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination))
            {
                try
                {
                    await Task.Run(() => ResultReportService.Generate(destination));
                }
                catch (Exception ex)
                {
                    reportRefreshError = ex.Message;
                }

                var report = Path.Combine(destination, ResultReportService.ReportFileName);
                Process.Start(new ProcessStartInfo(File.Exists(report) ? report : destination) { UseShellExecute = true });
            }

            var companionText = !HasConfiguredCompanion()
                ? "Frame-time companion: not configured; CET results were collected normally."
                : companionError is not null
                    ? "Frame-time companion: CET collection succeeded, but companion copy failed: " + companionError
                    : "Frame-time companion: " + (companion?.Message ?? "not collected.");

            if (reportRefreshError is not null)
                companionText += "\r\nReport refresh: CapFrameX copy is safe, but the post-copy report refresh failed: " + reportRefreshError;

            MessageBox.Show(
                this,
                "CET results archived successfully and known live profiler output was cleared.\r\n\r\n" +
                "CET_Report.html is the human-readable starting point. Full native data remains under Data\\.\r\n\r\n" +
                "Archive folder:\r\n" + destination + "\r\n\r\n" +
                companionText,
                Text,
                MessageBoxButtons.OK,
                companionError is null && reportRefreshError is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, FriendlyMessage(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(silent: true);
        }
    }

    private async Task RunStrictRestoreAsync()
    {
        if (busy) return;

        try
        {
            ShowRestoreProgress("RESTORING ORIGINAL STATE...");
            SetBusy(true);
            restoreOutcome.Refresh();

            var archived = await Task.Run(() => profiler.Restore(gameRoot.Text.Trim()));

            var message = string.IsNullOrWhiteSpace(archived)
                ? "Original CET / 0-Engine files and the previous CET binding state were restored."
                : "Original CET / 0-Engine files and the previous CET binding state were restored." +
                  Environment.NewLine + Environment.NewLine +
                  "Final live results were archived to:" + Environment.NewLine + archived;

            var verified = await Task.Run(() => profiler.GetStatus(gameRoot.Text.Trim()));
            if (verified.Managed)
                throw new InvalidOperationException("Restore returned, but managed profiler state is still present.");

            lastStatus = verified;
            ShowRestoreOutcome(true, "RESTORE SUCCESSFUL — profiler removed and original state restored.");
            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowRestoreOutcome(false, "RESTORE NOT COMPLETED — recovery state/backups preserved.");
            MessageBox.Show(
                this,
                "Normal restore could not complete safely. The recovery state/backups were kept." +
                Environment.NewLine + Environment.NewLine +
                "Reason:" + Environment.NewLine + FriendlyMessage(ex) +
                Environment.NewLine + Environment.NewLine +
                "No uncertain file was overwritten. The managed recovery state/backups were left in place for diagnosis or advanced recovery.",
                Text,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(silent: true);
        }
    }

    private void ShowRestoreProgress(string message)
    {
        restoreOutcome.Text = "… " + message;
        restoreOutcome.ForeColor = SystemColors.ControlText;
        restoreOutcome.Visible = true;
        restoreOutcome.BringToFront();
    }

    private void ShowRestoreOutcome(bool success, string message)
    {
        restoreOutcome.Text = (success ? "✓ " : "✗ ") + message;
        restoreOutcome.ForeColor = success ? Color.ForestGreen : Color.Firebrick;
        restoreOutcome.Visible = true;
        restoreOutcome.BringToFront();
    }

    private void ClearRestoreOutcome()
    {
        restoreOutcome.Text = "";
        restoreOutcome.Visible = false;
    }

    private void SetBusy(bool value)
    {
        busy = value;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;

        setupPage.Enabled = !value;
        profilerPage.Enabled = !value;

        if (!value)
        {
            UpdateCompanionControls();
            SetActionState(lastStatus);
        }
    }

    private void OpenResultsFolder()
    {
        try
        {
            Directory.CreateDirectory(profiler.ResultsRoot);
            Process.Start(new ProcessStartInfo(profiler.ResultsRoot) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartFrameTimeTool()
    {
        try
        {
            var exe = companionExe.Text.Trim();
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
                throw new FileNotFoundException("No frame-time profiler executable is configured.", exe);

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartCyberpunk()
    {
        try
        {
            var exe = GetGameExe(gameRoot.Text.Trim());
            if (!File.Exists(exe))
                throw new FileNotFoundException("Cyberpunk2077.exe was not found in the selected game folder.", exe);

            if (IsCyberpunkRunning())
            {
                startGame.Enabled = false;
                startGame.Text = "CYBERPUNK RUNNING";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe)!,
                UseShellExecute = true
            });

            startGame.Enabled = false;
            startGame.Text = "CYBERPUNK RUNNING";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SyncSettingsFromUi()
    {
        appSettings.GameRoot = gameRoot.Text.Trim();
        appSettings.PairFrameTimeProfiler = pairFrameTime.Checked;
        appSettings.ExternalProfilerExe = companionExe.Text.Trim();
        appSettings.ExternalResultsDirectory = companionResults.Text.Trim();
    }

    private void SaveSettingsFromUi()
    {
        SyncSettingsFromUi();
        appSettings.Save();
    }

    private static string GetGameExe(string root) =>
        Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe");

    private static bool IsCyberpunkRunning()
    {
        try { return Process.GetProcessesByName("Cyberpunk2077").Length > 0; }
        catch { return false; }
    }

    private static bool LooksLikeGameRoot(string root) =>
        !string.IsNullOrWhiteSpace(root) &&
        Directory.Exists(Path.Combine(root, "bin", "x64", "plugins"));

    private static string FindInitialGameRoot()
    {
        var candidates = new[]
        {
            @"D:\Games\PC\Cyberpunk 2077",
            @"C:\Program Files (x86)\Steam\steamapps\common\Cyberpunk 2077",
            @"C:\Program Files\Steam\steamapps\common\Cyberpunk 2077",
            @"C:\GOG Games\Cyberpunk 2077"
        };
        return candidates.FirstOrDefault(LooksLikeGameRoot) ?? "";
    }

    private static string FriendlyMessage(Exception ex)
    {
        if (ex is AggregateException aggregate)
            return string.Join(Environment.NewLine, aggregate.Flatten().InnerExceptions.Select(x => x.Message));
        return ex.Message;
    }
}
