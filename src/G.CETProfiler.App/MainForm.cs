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

    private readonly IProfilerService profiler = new ProfilerService();

    public MainForm()
    {
        Text = "CET Runtime Profiler Manager — C# port";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(800, 585);
        MinimumSize = new Size(820, 625);
        Font = new Font("Segoe UI", 9F);

        var pathLabel = new Label { Text = "Cyberpunk 2077 folder", AutoSize = true, Left = 20, Top = 18 };
        gameRoot.SetBounds(20, 42, 665, 24);
        var browse = new Button { Text = "Browse..." };
        browse.SetBounds(695, 40, 85, 28);
        browse.Click += (_, _) => Browse();

        var statusGroup = new GroupBox { Text = "Status" };
        statusGroup.SetBounds(20, 82, 760, 230);
        status.SetBounds(18, 28, 720, 145);
        status.Font = new Font("Consolas", 10F);
        status.Text = "C# port scaffold ready. Lifecycle implementation will be ported 1:1 from the alpha6c reference.";
        var refresh = new Button { Text = "REFRESH STATUS" };
        refresh.SetBounds(625, 184, 112, 28);
        refresh.Click += (_, _) => RefreshStatus();
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
        collect.Text = "COLLECT RESULTS / CLEAR LIVE";
        collect.SetBounds(265, 409, 275, 44);
        restore.Text = "RESTORE ORIGINAL STATE";
        restore.SetBounds(555, 409, 225, 44);
        openResults.Text = "Open Results Folder";
        openResults.SetBounds(20, 468, 230, 36);

        install.Click += (_, _) => ShowPortPending();
        collect.Click += (_, _) => ShowPortPending();
        restore.Click += (_, _) => ShowPortPending();
        openResults.Click += (_, _) => Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "RESULTS"));

        var info = new Label
        {
            Text = "One capture key: F11 starts a fresh measurement; F11 again stops it and exports CSVs. Game-side install/restore state survives app restart and is the same state TOTAL Profiler consumes."
        };
        info.SetBounds(20, 520, 760, 62);

        Controls.AddRange([pathLabel, gameRoot, browse, statusGroup, compatibility, install, collect, restore, openResults, info]);
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select the Cyberpunk 2077 game folder" };
        if (dialog.ShowDialog(this) == DialogResult.OK) gameRoot.Text = dialog.SelectedPath;
    }

    private void RefreshStatus()
    {
        try { status.Text = profiler.GetStatus(gameRoot.Text).ToString(); }
        catch (NotImplementedException) { ShowPortPending(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void ShowPortPending() => MessageBox.Show(this,
        "This repository seed preserves the final UI/CLI contract, but the lifecycle engine has not been ported from PowerShell to C# yet.",
        Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
}
