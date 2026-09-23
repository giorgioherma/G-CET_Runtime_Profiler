using System.Diagnostics;
using GCETRuntimeProfiler.Core.Models;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler;

public sealed class MainForm : Form
{
    private readonly TextBox gameRoot = new();
    private readonly Label status = new();
    private readonly CheckBox coreOnly = new();
    private readonly Button install = new();
    private readonly Button collect = new();
    private readonly Button restore = new();
    private readonly Button openResults = new();
    private readonly Button startGame = new();
    private readonly Button emergencyRestore = new();
    private readonly Button refresh = new();
    private readonly Button browse = new();

    private readonly IProfilerService profiler = new ProfilerService();
    private bool busy;

    public MainForm()
    {
        Text = $"CET Runtime Profiler Manager - v{profiler.PackageVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 585);
        MinimumSize = new Size(820, 625);
        Font = new Font("Segoe UI", 9F);

        var pathLabel = new Label
        {
            Text = "Cyberpunk 2077 folder",
            AutoSize = true,
            Left = 20,
            Top = 18
        };

        gameRoot.SetBounds(20, 42, 665, 24);
        gameRoot.Text = FindInitialGameRoot();
        gameRoot.TextChanged += async (_, _) => await RefreshStatusAsync(silent: true);

        browse.Text = "Browse...";
        browse.SetBounds(695, 40, 85, 28);
        browse.Click += async (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the Cyberpunk 2077 game folder",
                SelectedPath = Directory.Exists(gameRoot.Text) ? gameRoot.Text : ""
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                gameRoot.Text = dialog.SelectedPath;
                await RefreshStatusAsync(silent: true);
            }
        };

        var statusGroup = new GroupBox { Text = "Status" };
        statusGroup.SetBounds(20, 82, 760, 230);

        status.SetBounds(18, 28, 720, 145);
        status.Font = new Font("Consolas", 10F);

        refresh.Text = "REFRESH STATUS";
        refresh.SetBounds(625, 184, 112, 28);
        refresh.Click += async (_, _) => await RefreshStatusAsync();

        statusGroup.Controls.Add(status);
        statusGroup.Controls.Add(refresh);

        var compatibility = new GroupBox { Text = "0-Engine / Scheduler compatibility" };
        compatibility.SetBounds(20, 322, 760, 72);

        coreOnly.Text = "Core profiler only — leave 0-Engine untouched";
        coreOnly.SetBounds(18, 22, 315, 24);

        var compatText = new Label
        {
            Text = "Use this only if Scheduler integration cannot safely patch the installed 0-Engine. Core profiling still works and 0-Engine remains exactly as installed."
        };
        compatText.SetBounds(335, 14, 400, 50);

        compatibility.Controls.Add(coreOnly);
        compatibility.Controls.Add(compatText);

        install.Text = "INSTALL PROFILER";
        install.SetBounds(20, 409, 230, 44);
        install.Click += async (_, _) => await RunOperationAsync(
            () => profiler.Install(gameRoot.Text.Trim(), coreOnly.Checked),
            result => "Profiler installed." + Environment.NewLine + Environment.NewLine +
                      "F11 #1 = START" + Environment.NewLine +
                      "F11 #2 = STOP + AUTO EXPORT" + Environment.NewLine + Environment.NewLine +
                      "0-Engine mode: " + result.ManagedMode);

        collect.Text = "COLLECT RESULTS / CLEAR LIVE";
        collect.SetBounds(265, 409, 275, 44);
        collect.Click += async (_, _) => await RunOperationAsync(
            () => profiler.Collect(gameRoot.Text.Trim()),
            destination =>
            {
                if (!string.IsNullOrWhiteSpace(destination) && Directory.Exists(destination))
                    Process.Start(new ProcessStartInfo(destination) { UseShellExecute = true });

                return "Results archived successfully." + Environment.NewLine + Environment.NewLine +
                       "Archive folder:" + Environment.NewLine + destination + Environment.NewLine + Environment.NewLine +
                       "Live CET profiler CSVs were cleared.";
            });

        restore.Text = "RESTORE ORIGINAL STATE";
        restore.SetBounds(555, 409, 225, 44);
        restore.Click += async (_, _) =>
        {
            var answer = MessageBox.Show(
                this,
                "Restore the CET profiler-managed game state?\r\n\r\n" +
                "The original CET / 0-Engine files and previous CET binding state will be restored. " +
                "Any current live CET profiler CSVs are archived first.",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            await RunStrictRestoreAsync();
        };

        openResults.Text = "Open Results Folder";
        openResults.SetBounds(20, 468, 230, 36);
        openResults.Click += (_, _) => OpenResultsFolder();

        startGame.Text = "START CYBERPUNK";
        startGame.SetBounds(265, 468, 275, 36);
        startGame.Click += (_, _) => StartCyberpunk();

        emergencyRestore.Text = "EMERGENCY RESTORE";
        emergencyRestore.SetBounds(555, 468, 225, 36);
        emergencyRestore.Click += async (_, _) =>
        {
            var answer = MessageBox.Show(
                this,
                "Emergency restore is for a managed state that normal RESTORE cannot finish.\r\n\r\n" +
                "It checks every profiler-managed component independently. Safe components are restored; anything changed, missing, or uncertain is LEFT UNTOUCHED.\r\n\r\n" +
                "If anything is skipped, the recovery state/backups stay in the game folder and a report lists exactly what needs manual review.\r\n\r\n" +
                "Continue?",
                Text,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer != DialogResult.Yes) return;

            await RunOperationAsync(
                () => profiler.EmergencyRestore(gameRoot.Text.Trim()),
                result =>
                {
                    if (!result.Complete && !string.IsNullOrWhiteSpace(result.ReportPath) && File.Exists(result.ReportPath))
                        Process.Start(new ProcessStartInfo(result.ReportPath) { UseShellExecute = true });

                    var restored = result.Actions.Count(x => x.Status is "RESTORED" or "ARCHIVED");
                    var skipped = result.Actions.Count(x => x.Status == "SKIPPED");

                    return result.Complete
                        ? $"Emergency restore completed safely.\r\n\r\nRestored/archived components: {restored}\r\nRecovery state removed.\r\n\r\nReport:\r\n{result.ReportPath}"
                        : $"Emergency restore completed PARTIALLY.\r\n\r\nRestored/archived components: {restored}\r\nSkipped for safety: {skipped}\r\n\r\nNothing uncertain was overwritten or deleted. The recovery state/backups were preserved.\r\n\r\nThe recovery report has been opened:\r\n{result.ReportPath}";
                });
        };

        var info = new Label
        {
            Text = "One capture key: F11 starts a fresh measurement; F11 again stops it and exports CSVs. " +
                   "Game-side install/restore state survives app restart and is the same state TOTAL Profiler consumes."
        };
        info.SetBounds(20, 520, 760, 62);

        Controls.AddRange([
            pathLabel, gameRoot, browse, statusGroup, compatibility,
            install, collect, restore, openResults, startGame, emergencyRestore, info
        ]);

        Shown += async (_, _) => await RefreshStatusAsync(silent: true);
        Activated += async (_, _) =>
        {
            if (!busy) await RefreshStatusAsync(silent: true);
        };
    }

    private async Task RefreshStatusAsync(bool silent = false)
    {
        if (busy) return;

        var root = gameRoot.Text.Trim();
        if (!LooksLikeGameRoot(root))
        {
            status.Text =
                "Game:       NOT FOUND\r\n" +
                "CET:        -\r\n" +
                "0-Engine:   -\r\n" +
                "Init:       -\r\n" +
                "Scheduler:  -\r\n" +
                "Controls:   -\r\n" +
                "F11:        -\r\n" +
                "Live CSVs:  -";

            SetActionState(null);
            return;
        }

        try
        {
            SetBusy(true);
            var snapshot = await Task.Run(() => profiler.GetStatus(root));
            SetBusy(false);
            RenderStatus(snapshot);
            SetActionState(snapshot);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            status.Text = "STATUS ERROR:\r\n" + FriendlyMessage(ex);
            SetActionState(null);
            if (!silent)
                MessageBox.Show(this, FriendlyMessage(ex), Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenderStatus(ProfilerStatus snapshot)
    {
        var zeroText = snapshot.ZeroEnginePresent ? snapshot.ZeroEngineInit : "NOT FOUND - CORE PROFILER ONLY";
        var f11 = snapshot.F11Binding ? "F11 START / STOP + AUTO EXPORT" : "NOT CONFIGURED";

        status.Text =
            $"Game:       FOUND\r\n" +
            $"CET:        {snapshot.CetState}\r\n" +
            $"0-Engine:   {zeroText}\r\n" +
            $"Init:       {snapshot.ZeroEngineInit}\r\n" +
            $"Scheduler:  {snapshot.Scheduler}\r\n" +
            $"Controls:   {(snapshot.ControlsPresent ? "PRESENT" : "NOT INSTALLED")}\r\n" +
            $"F11:        {f11}\r\n" +
            $"Live CSVs:  {snapshot.LiveResultCount}    Managed install: {(snapshot.Managed ? "YES" : "NO")}";
    }

    private void SetActionState(ProfilerStatus? snapshot)
    {
        if (snapshot is null)
        {
            install.Enabled = false;
            collect.Enabled = false;
            restore.Enabled = false;
            coreOnly.Enabled = false;
            startGame.Enabled = false;
            startGame.Text = "START CYBERPUNK";
            emergencyRestore.Enabled = false;
            return;
        }

        var cetAllowed = snapshot.CetState is "OFFICIAL" or "PROFILER_ACTIVE";
        install.Enabled = !busy && cetAllowed && !snapshot.Managed && snapshot.LiveResultCount == 0;
        collect.Enabled = !busy && snapshot.LiveResultCount > 0;
        restore.Enabled = !busy && snapshot.Managed;
        coreOnly.Enabled = !busy && snapshot.ZeroEnginePresent && !snapshot.Managed;
        emergencyRestore.Enabled = !busy && snapshot.Managed;

        var gameExe = GetGameExe(snapshot.GameRoot);
        var gameRunning = IsCyberpunkRunning();
        startGame.Enabled = !busy && File.Exists(gameExe) && !gameRunning;
        startGame.Text = gameRunning ? "CYBERPUNK RUNNING" : "START CYBERPUNK";
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
                "Use EMERGENCY RESTORE to restore every independent component that still passes its own safety checks. Anything uncertain will be left untouched and listed for manual review.",
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

    private async Task RunOperationAsync<T>(Func<T> operation, Func<T, string> successMessage)
    {
        if (busy) return;

        try
        {
            SetBusy(true);
            var result = await Task.Run(operation);
            MessageBox.Show(this, successMessage(result), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private void SetBusy(bool value, bool preserveActionState = false)
    {
        busy = value;
        Cursor = value ? Cursors.WaitCursor : Cursors.Default;
        browse.Enabled = !value;
        refresh.Enabled = !value;
        gameRoot.Enabled = !value;
        openResults.Enabled = !value;
        if (value)
        {
            startGame.Enabled = false;
            emergencyRestore.Enabled = false;
        }

        if (value)
        {
            install.Enabled = false;
            collect.Enabled = false;
            restore.Enabled = false;
            coreOnly.Enabled = false;
        }
        else if (!preserveActionState)
        {
            // RefreshStatusAsync will immediately apply the exact state.
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
            var root = gameRoot.Text.Trim();
            var exe = GetGameExe(root);
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

    private static string GetGameExe(string root) =>
        Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe");

    private static bool IsCyberpunkRunning()
    {
        try
        {
            return Process.GetProcessesByName("Cyberpunk2077").Length > 0;
        }
        catch
        {
            return false;
        }
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
