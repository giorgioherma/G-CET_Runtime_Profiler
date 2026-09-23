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

    private readonly Button install = new();
    private readonly Button collect = new();
    private readonly Button restore = new();
    private readonly Button openResults = new();
    private readonly Button startGame = new();
    private readonly Button emergencyRestore = new();
    private readonly Button refresh = new();

    private bool busy;
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

        gameRoot.Text = FindInitialGameRoot();
        pairFrameTime.Checked = appSettings.PairFrameTimeProfiler;
        companionExe.Text = appSettings.ExternalProfilerExe;
        companionResults.Text = appSettings.ExternalResultsDirectory;
        UpdateCompanionControls();
        ShowPage(0);

        Shown += async (_, _) =>
        {
            await RefreshStatusAsync(silent: true);
            RefreshCompanionStatus();
        };

        Activated += async (_, _) =>
        {
            if (busy) return;
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
            UpdateCompanionControls();
            RefreshCompanionStatus();
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
        companionExe.TextChanged += (_, _) => RefreshCompanionStatus();
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
        statusGroup.SetBounds(20, 66, 820, 214);
        status.SetBounds(18, 27, 775, 145);
        status.Font = new Font("Consolas", 9.5F);
        refresh.Text = "REFRESH";
        refresh.SetBounds(694, 174, 100, 28);
        refresh.Click += async (_, _) => await RefreshStatusAsync();
        statusGroup.Controls.AddRange([status, refresh]);

        var compatibility = new GroupBox { Text = "0-Engine / Scheduler" };
        compatibility.SetBounds(20, 290, 820, 112);
        compatibilityText.SetBounds(18, 25, 775, 42);
        compatibilityText.MaximumSize = new Size(775, 0);
        compatibilityText.AutoSize = true;

        coreOnly.Text = "Fallback: install CET core profiler only and leave 0-Engine completely untouched";
        coreOnly.SetBounds(18, 76, 600, 24);
        coreOnly.CheckedChanged += (_, _) => SetActionState(lastStatus);
        compatibility.Controls.AddRange([compatibilityText, coreOnly]);

        var filesGroup = new GroupBox { Text = "Files / safety" };
        filesGroup.SetBounds(20, 412, 820, 112);
        touchedFiles.SetBounds(18, 24, 775, 76);
        touchedFiles.MaximumSize = new Size(775, 0);
        touchedFiles.AutoSize = true;
        filesGroup.Controls.Add(touchedFiles);

        install.Text = "INSTALL PROFILER";
        install.SetBounds(20, 540, 230, 42);
        install.Click += async (_, _) => await InstallAsync();

        collect.Text = "COLLECT RESULTS / CLEAR LIVE";
        collect.SetBounds(265, 540, 300, 42);
        collect.Click += async (_, _) => await CollectAsync();

        restore.Text = "RESTORE ORIGINAL STATE";
        restore.SetBounds(580, 540, 260, 42);
        restore.Click += async (_, _) =>
        {
            var answer = MessageBox.Show(
                this,
                "Restore the CET profiler-managed game state?\r\n\r\n" +
                "Original CET / 0-Engine files and the previous CET binding state will be restored. " +
                "Any current live CET profiler output is archived first.",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
                await RunStrictRestoreAsync();
        };

        openResults.Text = "Open Results Folder";
        openResults.SetBounds(20, 596, 230, 36);
        openResults.Click += (_, _) => OpenResultsFolder();

        startGame.Text = "START CYBERPUNK";
        startGame.SetBounds(265, 596, 300, 36);
        startGame.Click += (_, _) => StartCyberpunk();

        emergencyRestore.Text = "EMERGENCY RESTORE";
        emergencyRestore.SetBounds(580, 596, 260, 36);
        emergencyRestore.Click += async (_, _) => await RunEmergencyRestoreAsync();

        var info = new Label
        {
            Text = "F11 #1 starts a fresh CET measurement. F11 #2 stops it and exports results. " +
                   "If a frame-time companion is configured, COLLECT copies its latest capture into the same result folder; the external source is never deleted.",
            MaximumSize = new Size(820, 0),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Location = new Point(20, 646)
        };

        profilerPage.Controls.AddRange([
            title, back, statusGroup, compatibility, filesGroup,
            install, collect, restore, openResults, startGame, emergencyRestore, info
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
                "Game:       NOT FOUND\r\n" +
                "CET:        -\r\n" +
                "0-Engine:   -\r\n" +
                "Scheduler:  -\r\n" +
                "CET F11:    -\r\n" +
                "Live files: -";
            RenderSetupGameStatus();
            RenderCompatibility(null);
            RenderTouchedFiles(null);
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
        var enabled = pairFrameTime.Checked;
        companionExe.Enabled = enabled;
        companionResults.Enabled = enabled;
        browseCompanionExe.Enabled = enabled && !busy;
        browseCompanionResults.Enabled = enabled && !busy;
    }

    private void RenderStatus(ProfilerStatus snapshot)
    {
        var zeroText = snapshot.ZeroEnginePresent ? snapshot.ZeroEngineInit : "NOT INSTALLED";
        var f11 = snapshot.F11Binding
            ? "F11 ✓"
            : snapshot.Managed
                ? "NOT F11 / NOT CONFIGURED"
                : "F11 WILL BE PRESET ON INSTALL";

        status.Text =
            $"Game:       FOUND\r\n" +
            $"CET:        {snapshot.CetState}\r\n" +
            $"0-Engine:   {zeroText}\r\n" +
            $"Scheduler:  {snapshot.Scheduler}\r\n" +
            $"CET F11:    {f11}\r\n" +
            $"Controls:   {(snapshot.ControlsPresent ? "PRESENT" : "NOT INSTALLED")}\r\n" +
            $"Live files: {snapshot.LiveResultCount}    Managed install: {(snapshot.Managed ? "YES" : "NO")}";
    }

    private void RenderCompatibility(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            compatibilityText.Text = "Select a valid Cyberpunk 2077 folder to inspect 0-Engine / Scheduler compatibility.";
            coreOnly.Visible = false;
            return;
        }

        var unsafeZero = snapshot.ZeroEnginePresent && snapshot.ZeroEngineInitKind == "unsafe";
        var showFallback = !snapshot.Managed && (unsafeZero || fallbackVisible);

        coreOnly.Visible = showFallback;
        if (!showFallback)
            coreOnly.Checked = false;

        if (!snapshot.ZeroEnginePresent)
        {
            compatibilityText.Text =
                "0-Engine is not installed. That is fine: CET core profiling does not require it. No 0-Engine files will be added or changed.";
        }
        else if (snapshot.Managed && snapshot.ManagedMode == "core-only")
        {
            compatibilityText.Text =
                "Managed install is running in CET core-only mode. The pre-existing 0-Engine installation was left untouched.";
        }
        else if (unsafeZero)
        {
            compatibilityText.Text =
                "This 0-Engine init/Scheduler layout is not recognized as safe for automatic integration. Full install is blocked before changes. " +
                "Enable the fallback below to install CET profiling while leaving 0-Engine byte-untouched.";
        }
        else if (fallbackVisible)
        {
            compatibilityText.Text =
                "Scheduler integration did not complete safely and was rolled back. You can retry using the CET core-only fallback; 0-Engine will be left untouched.";
        }
        else
        {
            compatibilityText.Text =
                "0-Engine was detected and its Scheduler integration path is recognized. The normal install will use the transactional integration path and verified backups.";
        }
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

    private void SetActionState(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            install.Enabled = false;
            collect.Enabled = false;
            restore.Enabled = false;
            emergencyRestore.Enabled = false;
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
        emergencyRestore.Enabled = !busy && snapshot.Managed;
        coreOnly.Enabled = !busy && coreOnly.Visible && !snapshot.Managed;

        var gameExe = GetGameExe(snapshot.GameRoot);
        var gameRunning = IsCyberpunkRunning();
        startGame.Enabled = !busy && File.Exists(gameExe) && !gameRunning;
        startGame.Text = gameRunning ? "CYBERPUNK RUNNING" : "START CYBERPUNK";
    }

    private async Task InstallAsync()
    {
        if (busy) return;

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
            if (!string.IsNullOrWhiteSpace(destination) && pairFrameTime.Checked)
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

            if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination))
                Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });

            var companionText = !pairFrameTime.Checked
                ? "Frame-time companion: disabled."
                : companionError is not null
                    ? "Frame-time companion: CET collection succeeded, but companion copy failed: " + companionError
                    : "Frame-time companion: " + (companion?.Message ?? "not collected.");

            MessageBox.Show(
                this,
                "CET results archived successfully and known live profiler output was cleared.\r\n\r\n" +
                "Archive folder:\r\n" + destination + "\r\n\r\n" +
                companionText,
                Text,
                MessageBoxButtons.OK,
                companionError is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
            SetBusy(true);
            var archived = await Task.Run(() => profiler.Restore(gameRoot.Text.Trim()));

            var message = string.IsNullOrWhiteSpace(archived)
                ? "Original CET / 0-Engine files and the previous CET binding state were restored."
                : "Original CET / 0-Engine files and the previous CET binding state were restored." +
                  Environment.NewLine + Environment.NewLine +
                  "Final live results were archived to:" + Environment.NewLine + archived;

            MessageBox.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Normal restore could not complete safely. The recovery state/backups were kept." +
                Environment.NewLine + Environment.NewLine +
                "Reason:" + Environment.NewLine + FriendlyMessage(ex) +
                Environment.NewLine + Environment.NewLine +
                "Use EMERGENCY RESTORE to recover each independent component that still passes its own safety checks. Anything uncertain stays untouched.",
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

    private async Task RunEmergencyRestoreAsync()
    {
        var answer = MessageBox.Show(
            this,
            "Emergency restore checks each profiler-managed component independently.\r\n\r\n" +
            "Safe components are restored. Anything changed, missing, or uncertain is LEFT UNTOUCHED. " +
            "If anything is skipped, recovery state/backups remain and the report lists what needs manual review.\r\n\r\nContinue?",
            Text,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes) return;

        try
        {
            SetBusy(true);
            var result = await Task.Run(() => profiler.EmergencyRestore(gameRoot.Text.Trim()));

            if (!result.Complete && !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath))
                Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true });

            var restored = result.Actions.Count(x => x.Status is "RESTORED" or "ARCHIVED");
            var skipped = result.Actions.Count(x => x.Status == "SKIPPED");

            MessageBox.Show(
                this,
                result.Complete
                    ? $"Emergency restore completed safely.\r\n\r\nRestored/archived components: {restored}\r\nRecovery state removed.\r\n\r\nReport:\r\n{result.ReportPath}"
                    : $"Emergency restore completed PARTIALLY.\r\n\r\nRestored/archived components: {restored}\r\nSkipped for safety: {skipped}\r\n\r\nNothing uncertain was overwritten or deleted. Recovery state/backups were preserved.\r\n\r\nReport:\r\n{result.ReportPath}",
                Text,
                MessageBoxButtons.OK,
                result.Complete ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
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
