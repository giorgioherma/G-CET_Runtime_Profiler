using System.Diagnostics;
using System.Reflection;
using GCETRuntimeProfiler.Core.Models;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

public sealed class MainForm : Form
{
    private readonly IProfilerService profiler = new ProfilerService();
    private readonly AppSettings appSettings = AppSettings.Load();

    // Restrained G-CET visual language: dark diagnostic UI with cyan/magenta
    // accents. The workflow and information hierarchy stay intentionally plain.
    private static readonly Color ThemeBg = Color.FromArgb(8, 13, 18);
    private static readonly Color ThemePanel = Color.FromArgb(14, 23, 31);
    private static readonly Color ThemePanelAlt = Color.FromArgb(11, 18, 25);
    private static readonly Color ThemeBorder = Color.FromArgb(40, 71, 82);
    private static readonly Color ThemeText = Color.FromArgb(232, 243, 246);
    private static readonly Color ThemeMuted = Color.FromArgb(172, 188, 197);
    private static readonly Color ThemeCyan = Color.FromArgb(54, 244, 244);
    private static readonly Color ThemeMagenta = Color.FromArgb(255, 63, 215);
    private static readonly Color ThemeGreen = Color.FromArgb(94, 255, 130);
    private static readonly Color ThemeAmber = Color.FromArgb(255, 216, 64);
    private static readonly Color ThemeRed = Color.FromArgb(255, 82, 100);

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

    private readonly RichTextBox status = new();
    private readonly CheckBox coreOnly = new();
    private readonly GroupBox readyGroup = new();
    private readonly Label readyHeading = new();
    private readonly Label readyInstallInstruction = new();
    private readonly Label readyCaptureInstructions = new();
    private readonly Label readyNotice = new();

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
        var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (executableIcon is not null)
            Icon = executableIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(860, 690);
        MinimumSize = new Size(880, 730);
        Font = new Font("Segoe UI", 9F);
        BackColor = ThemeBg;
        ForeColor = ThemeText;

        BuildSetupPage();
        BuildProfilerPage();
        Controls.Add(profilerPage);
        Controls.Add(setupPage);
        ApplyTheme();

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
            ThemedDialog.ApplyDarkTitleBar(this);
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

        var logo = CreateHeaderLogo(new Point(20, 5));
        var title = new Label
        {
            Text = "SETUP",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            ForeColor = ThemeCyan,
            Location = new Point(84, 18)
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
                ThemedDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        setupPage.Controls.AddRange([logo, title, subtitle, gameGroup, companionGroup, next]);
        AddHeaderAccent(setupPage, 74);
    }

    private void BuildProfilerPage()
    {
        profilerPage.Dock = DockStyle.Fill;

        var logo = CreateHeaderLogo(new Point(20, 5));
        var title = new Label
        {
            Text = "INSTALL -> CAPTURE -> RESTORE",
            Font = new Font("Segoe UI Semibold", 18F),
            AutoSize = true,
            ForeColor = ThemeCyan,
            Location = new Point(84, 18)
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
        status.ReadOnly = true;
        status.BorderStyle = BorderStyle.None;
        status.ScrollBars = RichTextBoxScrollBars.None;
        status.DetectUrls = false;
        status.TabStop = false;
        status.BackColor = ThemePanelAlt;
        status.ForeColor = ThemeText;

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
        readyGroup.SetBounds(20, 382, 820, 154);

        readyHeading.SetBounds(18, 16, 775, 26);
        readyHeading.Font = new Font("Segoe UI Semibold", 11F);
        readyHeading.AutoSize = false;

        readyInstallInstruction.SetBounds(18, 44, 775, 20);
        readyInstallInstruction.Font = new Font("Segoe UI", 9.5F);
        readyInstallInstruction.AutoSize = false;
        readyInstallInstruction.Text = "1. Install G-CET Runtime Profiler.";

        readyCaptureInstructions.SetBounds(18, 64, 775, 70);
        readyCaptureInstructions.Font = new Font("Segoe UI", 9.5F);
        readyCaptureInstructions.AutoSize = false;
        readyCaptureInstructions.Text =
            "2. Run your Frame-time Capture Tool if you're using one and enter the game.\r\n" +
            "3. To start measurement press your shared keybind (F11). To stop capture and prep the results press the same key again (F11).\r\n" +
            "4. Return to installer and COLLECT RESULTS.\r\n" +
            "5. After usage RESTORE ORIGINAL STATE to finish.";

        readyNotice.SetBounds(18, 134, 775, 18);
        readyNotice.Font = new Font("Segoe UI Semibold", 8.5F);
        readyNotice.AutoSize = false;

        readyGroup.Controls.AddRange([readyHeading, readyInstallInstruction, readyCaptureInstructions, readyNotice]);

        install.Text = "INSTALL PROFILER";
        install.SetBounds(20, 550, 230, 42);
        install.Click += async (_, _) => await InstallAsync();

        collect.Text = "COLLECT RESULTS / CLEAR LIVE";
        collect.SetBounds(265, 550, 300, 42);
        collect.Click += async (_, _) => await CollectAsync();

        restore.Text = "RESTORE ORIGINAL STATE";
        restore.SetBounds(580, 550, 260, 42);
        restore.Click += async (_, _) =>
        {
            if (busy) return;

            DialogResult answer;
            suppressActivationRefresh = true;
            try
            {
                answer = ThemedDialog.Show(
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
        openResults.SetBounds(20, 604, 230, 36);
        openResults.Click += (_, _) => OpenResultsFolder();

        startCompanion.Text = "START FRAME-TIME TOOL";
        startCompanion.SetBounds(265, 604, 300, 36);
        startCompanion.Click += (_, _) => StartFrameTimeTool();

        startGame.Text = "START CYBERPUNK";
        startGame.SetBounds(580, 604, 260, 36);
        startGame.Click += (_, _) => StartCyberpunk();

        restoreOutcome.SetBounds(20, 648, 820, 28);
        restoreOutcome.Font = new Font("Segoe UI Semibold", 10F);
        restoreOutcome.TextAlign = ContentAlignment.MiddleLeft;
        restoreOutcome.Visible = false;

        profilerPage.Controls.AddRange([
            logo, title, back, statusGroup, readyGroup,
            install, collect, restore, openResults, startCompanion, startGame, restoreOutcome
        ]);
        AddHeaderAccent(profilerPage, 59);
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
            ColorizeStatusText();
            RenderSetupGameStatus();
            RenderCompatibility(null);
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
            ColorizeStatusText();
            RenderSetupGameStatus();
            RenderCompatibility(null);
            RenderReadyState(null);
            SetActionState(null);
            if (!silent)
                ThemedDialog.Show(this, FriendlyMessage(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderSetupGameStatus()
    {
        if (!LooksLikeGameRoot(gameRoot.Text.Trim()))
        {
            setupGameStatus.Text = "Game: NOT FOUND";
            setupGameStatus.ForeColor = ThemeRed;
            return;
        }

        setupGameStatus.Text = lastStatus is null
            ? "Game: FOUND"
            : $"Game: FOUND    CET: {lastStatus.CetState}";
        setupGameStatus.ForeColor = lastStatus?.CetState is "MISSING" or "UNKNOWN"
            ? ThemeAmber
            : ThemeGreen;
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
            ? snapshot.StartKeyIsF11 ? "F11 ✓" : snapshot.StartKey + "  (use F11 to match CET)"
            : "UNKNOWN — verify F11 manually";

        companionStatus.Text =
            $"CET capture key:        {(lastStatus?.F11Binding == true ? "F11 ✓" : "F11 preset on install")}\r\n" +
            $"Frame-time config key:  {keyText}\r\n" +
            $"Results folder:         {results}";
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
            ColorizeStatusText();
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

        var zeroLine = "0-Engine: Not installed · optional; extra 0-Engine features unavailable ⚠️";
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
            ? $"Frame-Time Profiler: {companion.DisplayName} configured ✅"
            : "Frame-Time Profiler: Not provided ⚠️";

        string syncLines;
        if (!companionConfigured)
        {
            syncLines =
                "Capture key match: Need frame capture tool ⚠️\r\n" +
                $"    - CET: {(snapshot.F11Binding ? "F11 ✅" : "F11 pending deployment ⚠️")}";
        }
        else if (companion.StartKeyKnown && companion.StartKeyIsF11 && snapshot.F11Binding)
        {
            syncLines =
                "Capture key match: YES ✅\r\n" +
                "    - CET: F11 ✅\r\n" +
                $"    - {companion.DisplayName} config: F11 ✅";
        }
        else
        {
            var externalKey = companion.StartKeyKnown
                ? companion.StartKey + " ⚠️"
                : "Unknown ⚠️";
            syncLines =
                "Capture key match: NO ⚠️\r\n" +
                $"    - CET: {(snapshot.F11Binding ? "F11 ✅" : "F11 pending deployment ⚠️")}\r\n" +
                $"    - {companion.DisplayName} config: {externalKey}";
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
        ColorizeStatusText();
    }

    private void ColorizeStatusText()
    {
        status.SuspendLayout();
        try
        {
            status.SelectAll();
            status.SelectionColor = ThemeText;

            for (var i = 0; i < status.Lines.Length; i++)
            {
                var line = status.Lines[i];
                var start = status.GetFirstCharIndexFromLine(i);
                if (start < 0)
                    continue;

                var color = line.Contains('❌') ||
                            line.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ||
                            line.Contains("BLOCKED", StringComparison.OrdinalIgnoreCase)
                    ? ThemeRed
                    : line.Contains('✅')
                        ? ThemeGreen
                        : line.Contains('⚠')
                            ? ThemeAmber
                            : line.Trim().Equals("optional:", StringComparison.OrdinalIgnoreCase)
                                ? ThemeMuted
                                : ThemeText;

                status.Select(start, line.Length);
                status.SelectionColor = color;
            }

            status.Select(0, 0);
            status.SelectionLength = 0;
        }
        finally
        {
            status.ResumeLayout();
        }
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
        (snapshot.Managed &&
         (snapshot.CetState != "PROFILER_ACTIVE" ||
          !snapshot.ControlsPresent ||
          !snapshot.F11Binding));

    private void RenderReadyState(ProfilerStatus? snapshot)
    {
        var ready = IsProfilerReady(snapshot);
        var blocked = HasCriticalProfilerError(snapshot);

        readyHeading.Text = ready
            ? "PROFILER IS READY!"
            : "PROFILER IS NOT READY!";

        readyHeading.ForeColor = ready
            ? ThemeGreen
            : blocked ? ThemeRed : ThemeAmber;

        // Before installation, step 1 is the only active instruction.
        // Once the managed profiler is fully ready, step 1 becomes completed/gray
        // and the capture/collect/restore steps become active.
        readyInstallInstruction.Enabled = true;
        readyCaptureInstructions.Enabled = true;
        readyInstallInstruction.ForeColor = snapshot?.Managed == true ? ThemeMuted : ThemeCyan;
        readyCaptureInstructions.ForeColor = ready ? ThemeText : ThemeMuted;

        readyNotice.Text = GetReadyNotice(snapshot);
        readyNotice.ForeColor = blocked
            ? ThemeRed
            : ready && (snapshot?.ZeroEnginePresent != true || snapshot.ManagedMode == "core-only")
                ? ThemeAmber
                : ThemeMuted;
    }

    private string GetReadyNotice(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
            return "Select a valid Cyberpunk 2077 folder in SETUP before installing.";

        if (snapshot.CetState == "MISSING")
            return "CET is required. Install/repair the supported Cyber Engine Tweaks version, verify the game folder, then REFRESH.";

        if (snapshot.CetState == "UNKNOWN")
            return $"Unsupported CET ASI detected. Install/repair supported CET {snapshot.TargetCetVersion}, then REFRESH.";

        if (snapshot.Managed && !IsProfilerReady(snapshot))
            return "Managed install is incomplete. RESTORE ORIGINAL STATE first, then install again; do not start a capture yet.";

        if (!snapshot.Managed && snapshot.LiveResultCount > 0)
            return "Existing live profiler output must be COLLECTed/cleared before installation can continue.";

        var unsafeZero = snapshot.ZeroEnginePresent && snapshot.ZeroEngineInitKind == "unsafe";
        if (!snapshot.Managed && unsafeZero && !coreOnly.Checked)
            return "0-Engine layout is not safely recognized. Enable the core-only fallback above to install without modifying 0-Engine.";

        if (IsProfilerReady(snapshot) && !snapshot.ZeroEnginePresent)
            return "0-Engine was not detected: core profiling is ready, but additional features are unavailable in no 0-Engine mode.";

        if (IsProfilerReady(snapshot) && snapshot.ManagedMode == "core-only")
            return "Core-only mode is ready: 0-Engine was left untouched, so additional 0-Engine integration features are unavailable.";

        return "";
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
            var installedSnapshot = await Task.Run(
                () => profiler.Install(gameRoot.Text.Trim(), coreOnly.Visible && coreOnly.Checked));

            lastStatus = installedSnapshot;
            fallbackVisible = false;

            var postInstallWarning = BuildPostInstallWarning(installedSnapshot);
            if (!string.IsNullOrWhiteSpace(postInstallWarning))
            {
                ThemedDialog.Show(
                    this,
                    postInstallWarning,
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            ProfilerStatus? afterFailure = null;
            try
            {
                afterFailure = await Task.Run(() => profiler.GetStatus(gameRoot.Text.Trim()));
                lastStatus = afterFailure;
            }
            catch
            {
                // Keep the original install error authoritative if status inspection
                // itself is impossible.
            }

            if (afterFailure?.ZeroEnginePresent == true || lastStatus?.ZeroEnginePresent == true)
                fallbackVisible = true;

            ThemedDialog.Show(
                this,
                BuildInstallFailureMessage(ex, afterFailure),
                Text,
                MessageBoxButtons.OK,
                afterFailure?.Managed == true ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            await RefreshStatusAsync(silent: true);
        }
    }

    private string BuildPostInstallWarning(ProfilerStatus snapshot)
    {
        if (!IsProfilerReady(snapshot))
        {
            var problems = new List<string>();
            if (snapshot.CetState != "PROFILER_ACTIVE")
                problems.Add("- CET profiler ASI is not active.");
            if (!snapshot.ControlsPresent)
                problems.Add("- CETProfilerControls is missing.");
            if (!snapshot.F11Binding)
                problems.Add("- The CET F11 capture binding is not configured.");

            return
                "INSTALL COMPLETED, BUT THE PROFILER IS NOT READY.\r\n\r\n" +
                (problems.Count > 0
                    ? string.Join("\r\n", problems) + "\r\n\r\n"
                    : "") +
                "Suggested fix:\r\n" +
                "1. Use RESTORE ORIGINAL STATE so the preserved recovery state can safely undo this install.\r\n" +
                "2. Press REFRESH and correct any CET / controls issue shown above.\r\n" +
                "3. Install again.\r\n\r\n" +
                "Do not start a capture until the status says PROFILER IS READY.";
        }

        if (!snapshot.ZeroEnginePresent)
        {
            return
                "G-CET Runtime Profiler installed successfully.\r\n\r\n" +
                "0-Engine was not detected. Core profiling is fully ready, but additional features will not be available in no 0-Engine mode.";
        }

        if (snapshot.ManagedMode == "core-only")
        {
            return
                "G-CET Runtime Profiler installed successfully in core-only mode.\r\n\r\n" +
                "0-Engine was intentionally left untouched. Core profiling is ready, but additional 0-Engine integration features will not be available.";
        }

        return "";
    }

    private string BuildInstallFailureMessage(Exception ex, ProfilerStatus? afterFailure)
    {
        var lines = new List<string>
        {
            "INSTALL FAILED.",
            "",
            "Reason:",
            FriendlyMessage(ex),
            ""
        };

        if (afterFailure?.Managed == true)
        {
            lines.Add("The installation did not reach a verified ready state and managed recovery state is still present.");
            lines.Add("Do not retry INSTALL over this state.");
            lines.Add("");
            lines.Add("Suggested fix:");
            lines.Add("- Use RESTORE ORIGINAL STATE first.");
            lines.Add("- Keep the .cet_runtime_profiler recovery folder/backups until restore succeeds.");
            lines.Add("- Press REFRESH, correct the reported issue, then install again.");
            return string.Join("\r\n", lines);
        }

        lines.Add("No managed profiler install remains; automatic rollback completed or no game files were changed.");
        lines.Add("");
        lines.Add("Suggested fixes:");

        if (afterFailure is null)
        {
            lines.Add("- Verify the selected Cyberpunk 2077 folder and press REFRESH.");
        }
        else
        {
            if (afterFailure.CetState == "MISSING")
                lines.Add("- Cyber Engine Tweaks (CET) is required. Install/repair the supported CET version, then press REFRESH.");
            else if (afterFailure.CetState == "UNKNOWN")
                lines.Add($"- The installed CET ASI is unsupported. Install/repair supported CET {afterFailure.TargetCetVersion}, then press REFRESH.");

            if (afterFailure.ZeroEnginePresent && afterFailure.ZeroEngineInitKind == "unsafe")
                lines.Add("- 0-Engine cannot be integrated safely in its current layout. Enable the core-only fallback and retry.");

            if (afterFailure.LiveResultCount > 0)
                lines.Add("- Existing live profiler output must be COLLECTed/cleared before installing.");
        }

        lines.Add("- Make sure Cyberpunk 2077 is closed and no program is locking CET profiler files.");
        lines.Add("- If Windows reports access denied, run with sufficient permissions.");
        lines.Add("- Press REFRESH after correcting the issue, then retry INSTALL.");

        return string.Join("\r\n", lines);
    }

    private async Task CollectAsync()
    {
        if (busy) return;

        try
        {
            SaveSettingsFromUi();
            SetBusy(true);
            var destination = await Task.Run(() => profiler.Collect(gameRoot.Text.Trim()));

            string? companionMessage = null;
            string? companionError = null;
            string? reportRefreshError = null;
            if (!string.IsNullOrWhiteSpace(destination) && HasConfiguredCompanion())
            {
                try
                {
                    companionMessage = await Task.Run(() => CompanionProfilerService.CollectLatest(appSettings, destination));
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
                    : "Frame-time companion: " + (companionMessage ?? "not collected.");

            if (reportRefreshError is not null)
                companionText += "\r\nReport refresh: CapFrameX copy is safe, but the post-copy report refresh failed: " + reportRefreshError;

            ThemedDialog.Show(
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
            ThemedDialog.Show(this, FriendlyMessage(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            ThemedDialog.Show(this, message, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            ShowRestoreOutcome(false, "RESTORE NOT COMPLETED — recovery state/backups preserved.");
            ThemedDialog.Show(
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
        restoreOutcome.ForeColor = ThemeText;
        restoreOutcome.Visible = true;
        restoreOutcome.BringToFront();
    }

    private void ShowRestoreOutcome(bool success, string message)
    {
        restoreOutcome.Text = (success ? "✓ " : "✗ ") + message;
        restoreOutcome.ForeColor = success ? ThemeGreen : ThemeRed;
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
            ThemedDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            ThemedDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            ThemedDialog.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void ApplyTheme()
    {
        ApplyThemeRecursive(this);

        setupPage.BackColor = ThemeBg;
        profilerPage.BackColor = ThemeBg;

        // Functional emphasis only: actions remain conventional buttons, with
        // accent color identifying the normal forward path and restore boundary.
        AccentButton(install, ThemeCyan);
        AccentButton(collect, ThemeCyan);
        AccentButton(startGame, ThemeCyan);
        AccentButton(startCompanion, ThemeCyan);
        AccentButton(restore, ThemeMagenta);

        readyGroup.BackColor = ThemePanelAlt;
        readyHeading.ForeColor = ThemeAmber;
        status.ForeColor = ThemeText;
        companionStatus.ForeColor = ThemeText;

        capFrameXLink.LinkColor = ThemeCyan;
        capFrameXLink.ActiveLinkColor = ThemeMagenta;
        capFrameXLink.VisitedLinkColor = ThemeCyan;
    }

    private static void ApplyThemeRecursive(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case Panel panel:
                    panel.BackColor = (panel.Tag as string) switch
                    {
                        "gcet-accent-cyan" => ThemeBorder,
                        "gcet-accent-magenta" => ThemeMagenta,
                        _ => ThemeBg
                    };
                    panel.ForeColor = ThemeText;
                    break;

                case GroupBox group:
                    group.BackColor = ThemePanelAlt;
                    group.ForeColor = ThemeCyan;
                    group.FlatStyle = FlatStyle.Flat;
                    break;

                case RichTextBox richTextBox:
                    richTextBox.BackColor = ThemePanelAlt;
                    richTextBox.ForeColor = ThemeText;
                    richTextBox.BorderStyle = BorderStyle.None;
                    break;

                case TextBox textBox:
                    textBox.BackColor = ThemePanel;
                    textBox.ForeColor = ThemeText;
                    textBox.BorderStyle = BorderStyle.FixedSingle;
                    break;

                case Button button:
                    button.UseVisualStyleBackColor = false;
                    button.BackColor = ThemePanel;
                    button.ForeColor = ThemeText;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = ThemeBorder;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(19, 35, 44);
                    button.FlatAppearance.MouseDownBackColor = Color.FromArgb(23, 43, 53);
                    break;

                case CheckBox checkBox:
                    checkBox.BackColor = Color.Transparent;
                    checkBox.ForeColor = ThemeText;
                    break;

                case LinkLabel link:
                    link.BackColor = Color.Transparent;
                    link.ForeColor = ThemeCyan;
                    link.LinkColor = ThemeCyan;
                    link.ActiveLinkColor = ThemeMagenta;
                    break;

                case Label label:
                    label.BackColor = Color.Transparent;
                    if (label.ForeColor == SystemColors.GrayText)
                        label.ForeColor = ThemeMuted;
                    else if (label.ForeColor == SystemColors.ControlText ||
                             label.ForeColor == SystemColors.WindowText ||
                             label.ForeColor == Color.Black)
                        label.ForeColor = label.Font.Size >= 16F ? ThemeCyan : ThemeText;
                    break;
            }

            if (control.HasChildren)
                ApplyThemeRecursive(control);
        }
    }

    private static void AccentButton(Button button, Color accent)
    {
        button.ForeColor = accent;
        button.FlatAppearance.BorderColor = accent;
        button.Paint += (_, e) =>
        {
            if (button.Enabled)
                return;

            e.Graphics.Clear(ThemePanel);
            using var border = new Pen(ThemeBorder);
            e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, button.Width - 1), Math.Max(0, button.Height - 1));
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                ThemeMuted,
                TextFormatFlags.HorizontalCenter |
                TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis);
        };
    }

    private static PictureBox CreateHeaderLogo(Point location)
    {
        var logo = new PictureBox
        {
            Location = location,
            Size = new Size(52, 52),
            BackColor = Color.Transparent,
            SizeMode = PictureBoxSizeMode.Zoom,
            TabStop = false
        };

        logo.Image = LoadBrandImage();
        return logo;
    }

    private static Image? LoadBrandImage()
    {
        try
        {
            using var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (executableIcon is not null)
                return executableIcon.ToBitmap();
        }
        catch
        {
            // Fall through to the embedded PNG.
        }

        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("GCETRuntimeProfiler.GCetIcon.png");
            if (stream is null)
                return null;

            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch
        {
            // Branding must never prevent the profiler UI from starting.
            return null;
        }
    }

    private static void AddHeaderAccent(Control page, int y)
    {
        var cyan = new Panel
        {
            Tag = "gcet-accent-cyan",
            BackColor = ThemeBorder,
            Location = new Point(20, y),
            Size = new Size(820, 1)
        };
        var magenta = new Panel
        {
            Tag = "gcet-accent-magenta",
            BackColor = ThemeMagenta,
            Location = new Point(20, y + 1),
            Size = new Size(92, 1)
        };
        page.Controls.Add(cyan);
        page.Controls.Add(magenta);
        cyan.SendToBack();
        magenta.SendToBack();
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
