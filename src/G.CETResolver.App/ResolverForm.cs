using System.Text.Json;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler.Resolver;

internal sealed class ResolverForm : Form
{
    private readonly TextBox _capture = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _mods = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button _captureBrowse = new() { Text = "Browse..." };
    private readonly Button _modsBrowse = new() { Text = "Browse..." };
    private readonly Button _analyze = new() { Text = "Analyze", Height = 36 };
    private readonly Label _status = new() { AutoSize = true, Text = "Select a profiler capture and the live CET mods folder." };
    private readonly Label _leave = new() { AutoSize = true, Text = "LEAVE_ALONE: -" };
    private readonly Label _exact = new() { AutoSize = true, Text = "EXACT_CADENCE: -" };
    private readonly Label _mixed = new() { AutoSize = true, Text = "MIXED_SPLIT: -" };
    private readonly Label _active = new() { AutoSize = true, Text = "ACTIVE_DORMANT: -" };
    private readonly TextBox _output = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
    };

    public ResolverForm()
    {
        Text = "G-CET Cadence Resolver — Dev";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(880, 620);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 3,
            RowCount = 9
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var captureLabel = new Label
        {
            Text = "Capture folder",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };
        var modsLabel = new Label
        {
            Text = "Live CET mods",
            AutoSize = true,
            Anchor = AnchorStyles.Left
        };

        _capture.Dock = DockStyle.Fill;
        _mods.Dock = DockStyle.Fill;
        _captureBrowse.Dock = DockStyle.Fill;
        _modsBrowse.Dock = DockStyle.Fill;
        _analyze.Dock = DockStyle.Fill;
        _status.Anchor = AnchorStyles.Left;

        root.Controls.Add(captureLabel, 0, 0);
        root.Controls.Add(_capture, 1, 0);
        root.Controls.Add(_captureBrowse, 2, 0);

        root.Controls.Add(modsLabel, 0, 1);
        root.Controls.Add(_mods, 1, 1);
        root.Controls.Add(_modsBrowse, 2, 1);

        root.Controls.Add(_analyze, 0, 2);
        root.SetColumnSpan(_analyze, 3);

        root.Controls.Add(_status, 0, 3);
        root.SetColumnSpan(_status, 3);

        root.Controls.Add(_leave, 0, 4);
        root.SetColumnSpan(_leave, 3);
        root.Controls.Add(_exact, 0, 5);
        root.SetColumnSpan(_exact, 3);
        root.Controls.Add(_mixed, 0, 6);
        root.SetColumnSpan(_mixed, 3);
        root.Controls.Add(_active, 0, 7);
        root.SetColumnSpan(_active, 3);

        root.Controls.Add(_output, 0, 8);
        root.SetColumnSpan(_output, 3);
        _output.Dock = DockStyle.Fill;

        Controls.Add(root);

        _captureBrowse.Click += (_, _) => BrowseInto(_capture);
        _modsBrowse.Click += (_, _) => BrowseInto(_mods);
        _analyze.Click += (_, _) => Analyze();
    }

    private static void BrowseInto(TextBox target)
    {
        using var dialog = new FolderBrowserDialog
        {
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(target.Text) ? target.Text : ""
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            target.Text = dialog.SelectedPath;
    }

    private void Analyze()
    {
        try
        {
            _analyze.Enabled = false;
            _status.Text = "Analyzing...";
            _output.Clear();
            Application.DoEvents();

            var capture = _capture.Text.Trim();
            var mods = _mods.Text.Trim();

            if (!Directory.Exists(capture))
                throw new DirectoryNotFoundException("Select a valid collected profiler capture folder.");
            if (!Directory.Exists(mods))
                throw new DirectoryNotFoundException("Select the live cyber_engine_tweaks\\mods folder.");

            var result = CadenceResolverService.Resolve(capture, mods);
            var finalJson = File.ReadAllText(result.FinalResolutionPath);

            using var doc = JsonDocument.Parse(finalJson);
            var root = doc.RootElement;
            var summary = root.GetProperty("summary");

            _leave.Text = $"LEAVE_ALONE: {GetInt(summary, "leaveAlone")}";
            _exact.Text = $"EXACT_CADENCE: {GetInt(summary, "exactCadence")}";
            _mixed.Text = $"MIXED_SPLIT: {GetInt(summary, "mixedSplit")}";
            _active.Text = $"ACTIVE_DORMANT: {GetInt(summary, "activeDormant")}";

            _status.Text = $"Complete — {result.CallbackCount} onUpdate callbacks analyzed.";
            _output.Text =
                $"Runtime resolution:\r\n{result.RuntimeResolutionPath}\r\n\r\n" +
                $"Final source-confirmed resolution:\r\n{result.FinalResolutionPath}\r\n\r\n" +
                "Live CET sources were inspected read-only. No mod files were changed.";
        }
        catch (Exception ex)
        {
            _status.Text = "Analysis failed.";
            _output.Text = ex.ToString();
            MessageBox.Show(
                this,
                ex.Message,
                "G-CET Cadence Resolver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _analyze.Enabled = true;
        }
    }

    private static int GetInt(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
    }
}
