using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler.Resolver;

internal sealed class ResolverForm : Form
{
    private readonly TextBox _capture = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _game = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button _captureBrowse = new() { Text = "Browse..." };
    private readonly Button _gameBrowse = new() { Text = "Browse..." };
    private readonly Button _analyze = new() { Text = "ANALYZE", Height = 36 };
    private readonly Button _generate = new() { Text = "GENERATE PASS ZIP", Height = 36 };
    private readonly Label _status = new() { AutoSize = true, Text = "Select RESULTS (or a capture folder) and the Game Folder." };
    private readonly Label _families = new() { AutoSize = true, Text = "CALLBACK FAMILIES: -" };
    private readonly Label _generic = new() { AutoSize = true, Text = "AUTO PATCHABLE: -" };
    private readonly Label _semantic = new() { AutoSize = true, Text = "SEMANTIC READY: -   |   ALREADY SATISFIED: -" };
    private readonly Label _unresolved = new() { AutoSize = true, Text = "MATERIAL REMAINING: -   |   BELOW 3 ms/s: -" };
    private readonly TextBox _output = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
    };

    public ResolverForm()
    {
        Text = "G-CET Resolver — Dev";
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var captureLabel = new Label { Text = "RESULTS / capture", AutoSize = true, Anchor = AnchorStyles.Left };
        var gameLabel = new Label { Text = "Game Folder", AutoSize = true, Anchor = AnchorStyles.Left };

        _capture.Dock = DockStyle.Fill;
        _game.Dock = DockStyle.Fill;
        _captureBrowse.Dock = DockStyle.Fill;
        _gameBrowse.Dock = DockStyle.Fill;
        _analyze.Dock = DockStyle.Fill;
        _generate.Dock = DockStyle.Fill;
        _status.Anchor = AnchorStyles.Left;

        root.Controls.Add(captureLabel, 0, 0);
        root.Controls.Add(_capture, 1, 0);
        root.Controls.Add(_captureBrowse, 2, 0);
        root.Controls.Add(gameLabel, 0, 1);
        root.Controls.Add(_game, 1, 1);
        root.Controls.Add(_gameBrowse, 2, 1);
        root.Controls.Add(_analyze, 0, 2);
        root.SetColumnSpan(_analyze, 2);
        root.Controls.Add(_generate, 2, 2);
        root.Controls.Add(_status, 0, 3);
        root.SetColumnSpan(_status, 3);
        root.Controls.Add(_families, 0, 4);
        root.SetColumnSpan(_families, 3);
        root.Controls.Add(_generic, 0, 5);
        root.SetColumnSpan(_generic, 3);
        root.Controls.Add(_semantic, 0, 6);
        root.SetColumnSpan(_semantic, 3);
        root.Controls.Add(_unresolved, 0, 7);
        root.SetColumnSpan(_unresolved, 3);
        root.Controls.Add(_output, 0, 8);
        root.SetColumnSpan(_output, 3);
        _output.Dock = DockStyle.Fill;

        Controls.Add(root);

        _captureBrowse.Click += (_, _) => BrowseInto(_capture);
        _gameBrowse.Click += (_, _) => BrowseInto(_game);
        _analyze.Click += (_, _) => Analyze();
        _generate.Click += (_, _) => GeneratePass();
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

    private static string ResolveModsFolder(string gameFolder)
    {
        gameFolder = gameFolder.Trim();
        if (!Directory.Exists(gameFolder))
            throw new DirectoryNotFoundException("Select a valid Game Folder.");

        var mods = Path.Combine(
            Path.GetFullPath(gameFolder),
            "bin",
            "x64",
            "plugins",
            "cyber_engine_tweaks",
            "mods");

        if (!Directory.Exists(mods))
        {
            throw new DirectoryNotFoundException(
                "Could not find the live CET mods folder under the selected Game Folder. " +
                "Expected: bin\\x64\\plugins\\cyber_engine_tweaks\\mods");
        }

        return mods;
    }

    private void Analyze()
    {
        try
        {
            _analyze.Enabled = false;
            _generate.Enabled = false;
            _status.Text = "Analyzing callback families...";
            _output.Clear();
            Application.DoEvents();

            var capture = _capture.Text.Trim();
            var mods = ResolveModsFolder(_game.Text);

            if (!Directory.Exists(capture))
                throw new DirectoryNotFoundException("Select a valid G-CET RESULTS folder or collected capture folder.");

            var result = ResolverService.Resolve(capture, mods);

            _families.Text = $"CALLBACK FAMILIES: {result.FamilyCount}";
            _generic.Text =
                $"AUTO PATCHABLE: {result.GenericResolvedCount}   |   " +
                $"NON-FRAME-ONLY: {result.NonFrameOnlyAutoCount}   |   " +
                $"FRAME-ONLY: {result.FrameOnlyAutoCount}";
            _semantic.Text =
                $"SEMANTIC READY: {result.SemanticReadyRuleCount}   |   " +
                $"ALREADY SATISFIED: {result.AlreadySatisfiedCount}";
            _unresolved.Text =
                $"MATERIAL REMAINING: {result.MaterialRemainingCount}   |   " +
                $"BELOW 3 ms/s: {result.BelowThresholdCount}";

            _status.Text = $"Complete — {result.RankedCallbackCount} measured callback consumers inspected.";
            _output.Text =
                $"G-CET resolver output:\r\n{result.ResolverPath}\r\n\r\n" +
                (result.CadenceFinalPath is null
                    ? "Cadence subset: not available for this capture.\r\n\r\n"
                    : $"Cadence subset:\r\n{result.CadenceFinalPath}\r\n\r\n") +
                "Live CET sources were inspected read-only. No mod files were changed.";
        }
        catch (Exception ex)
        {
            _status.Text = "Analysis failed.";
            _output.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "G-CET Resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyze.Enabled = true;
            _generate.Enabled = true;
        }
    }

    private void GeneratePass()
    {
        try
        {
            _analyze.Enabled = false;
            _generate.Enabled = false;
            _status.Text = "Revalidating resolver decisions and generating overlay ZIP...";
            _output.Clear();
            Application.DoEvents();

            var capture = _capture.Text.Trim();
            var mods = ResolveModsFolder(_game.Text);

            if (!Directory.Exists(capture))
                throw new DirectoryNotFoundException("Select a valid G-CET RESULTS folder or collected capture folder.");

            // Always refresh the resolver against the current live stack before a
            // pass is generated. The generator then consumes only that resolver
            // output and refuses stale source hashes.
            var resolved = ResolverService.Resolve(capture, mods);
            var pass = ResolverService.GeneratePass(capture, mods);

            _families.Text = $"CALLBACK FAMILIES: {resolved.FamilyCount}";
            _generic.Text =
                $"AUTO PATCHABLE: {resolved.GenericResolvedCount}   |   " +
                $"NON-FRAME-ONLY: {resolved.NonFrameOnlyAutoCount}   |   " +
                $"FRAME-ONLY: {resolved.FrameOnlyAutoCount}";
            _semantic.Text =
                $"SEMANTIC READY: {resolved.SemanticReadyRuleCount}   |   " +
                $"ALREADY SATISFIED: {resolved.AlreadySatisfiedCount}";
            _unresolved.Text =
                $"MATERIAL REMAINING: {resolved.MaterialRemainingCount}   |   " +
                $"BELOW 3 ms/s: {resolved.BelowThresholdCount}";

            _status.Text = $"Pass ready — {pass.TransformCount} transforms across {pass.FileCount} files.";
            _output.Text =
                $"G-CET pass ZIP:\r\n{pass.ZipPath}\r\n\r\n" +
                $"Pass manifest:\r\n{pass.ManifestPath}\r\n\r\n" +
                $"Applied transforms: {pass.TransformCount}\r\n" +
                $"Changed files: {pass.FileCount}\r\n" +
                $"Skipped after source revalidation: {pass.SkippedCount}\r\n\r\n" +
                "The ZIP contains only deployable game files. The JSON manifest is kept beside it in RESULTS. " +
                "No live mod files were changed by the resolver.";
        }
        catch (Exception ex)
        {
            _status.Text = "Pass generation failed.";
            _output.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "G-CET Resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyze.Enabled = true;
            _generate.Enabled = true;
        }
    }
}
