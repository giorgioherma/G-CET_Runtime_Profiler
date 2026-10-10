using System.Diagnostics;
using GCETRuntimeProfiler.Core.Services;

namespace GCETRuntimeProfiler.Resolver;

internal sealed class ResolverForm : Form
{
    private readonly TextBox _capture = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly TextBox _game = new() { Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
    private readonly Button _captureBrowse = new() { Text = "Browse..." };
    private readonly Button _gameBrowse = new() { Text = "Browse..." };
    private readonly Button _analyze = new() { Text = "ANALYZE", Height = 36 };
    private readonly Button _generate = new() { Text = "GENERATE PASS ZIP", Height = 36, Enabled = false };
    private readonly Button _openResults = new() { Text = "OPEN RESULTS FOLDER", Enabled = false };
    private readonly Button _updateFramework = new() { Text = "UPDATE 0-ENGINE ONLY", Height = 32 };
    private readonly Label _status = new() { AutoSize = true, Text = "Select RESULTS (or a capture folder) and the Game Folder." };
    private readonly Label _families = new() { AutoSize = true, Text = "CALLBACK FAMILIES: -" };
    private readonly Label _generic = new() { AutoSize = true, Text = "GENERIC READY: -" };
    private readonly Label _shared = new() { AutoSize = true, Text = "SHARED STATE READY: - families / - callbacks / - reads" };
    private readonly Label _semantic = new() { AutoSize = true, Text = "SEMANTIC READY: -   |   ALREADY SATISFIED: -" };
    private readonly Label _unresolved = new() { AutoSize = true, Text = "MATERIAL REMAINING: -   |   BELOW 3 ms/s: -" };
    private bool _passReady;
    private string? _resolvedResultsFolder;

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
            RowCount = 11
        };

        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
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
        _updateFramework.Dock = DockStyle.Fill;
        _openResults.Dock = DockStyle.Fill;
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
        root.Controls.Add(_updateFramework, 0, 3);
        root.SetColumnSpan(_updateFramework, 3);
        root.Controls.Add(_status, 0, 4);
        root.SetColumnSpan(_status, 2);
        root.Controls.Add(_openResults, 2, 4);
        root.Controls.Add(_families, 0, 5);
        root.SetColumnSpan(_families, 3);
        root.Controls.Add(_generic, 0, 6);
        root.SetColumnSpan(_generic, 3);
        root.Controls.Add(_shared, 0, 7);
        root.SetColumnSpan(_shared, 3);
        root.Controls.Add(_semantic, 0, 8);
        root.SetColumnSpan(_semantic, 3);
        root.Controls.Add(_unresolved, 0, 9);
        root.SetColumnSpan(_unresolved, 3);
        root.Controls.Add(_output, 0, 10);
        root.SetColumnSpan(_output, 3);
        _output.Dock = DockStyle.Fill;

        Controls.Add(root);

        _captureBrowse.Click += (_, _) => BrowseInto(_capture);
        _gameBrowse.Click += (_, _) => BrowseInto(_game);
        _capture.TextChanged += (_, _) =>
        {
            InvalidatePassReadiness();
            _resolvedResultsFolder = null;
            UpdateOpenResultsState();
        };
        _game.TextChanged += (_, _) => InvalidatePassReadiness();
        _analyze.Click += (_, _) => Analyze();
        _generate.Click += (_, _) => GeneratePass();
        _updateFramework.Click += (_, _) => UpdateFrameworkOnly();
        _openResults.Click += (_, _) => OpenResultsFolder();
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

    private void InvalidatePassReadiness()
    {
        _passReady = false;
        _generate.Enabled = false;
    }

    private void UpdateOpenResultsState()
    {
        var selected = _capture.Text.Trim();
        _openResults.Enabled =
            (!string.IsNullOrWhiteSpace(_resolvedResultsFolder) &&
             Directory.Exists(_resolvedResultsFolder)) ||
            Directory.Exists(selected);
    }

    private void OpenResultsFolder()
    {
        var selected = _capture.Text.Trim();
        var folder =
            !string.IsNullOrWhiteSpace(_resolvedResultsFolder) &&
            Directory.Exists(_resolvedResultsFolder)
                ? _resolvedResultsFolder
                : selected;

        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            _openResults.Enabled = false;
            MessageBox.Show(
                this,
                "Select a valid RESULTS or capture folder first.",
                "G-CET Resolver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Path.GetFullPath(folder),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "G-CET Resolver",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool HasApplicablePass(ResolverBuildResult result)
    {
        return result.GenericResolvedCount > 0 ||
               result.SemanticReadyRuleCount > 0 ||
               result.SharedProviderReadyCallbackCount > 0;
    }

    private void UpdateSummary(ResolverBuildResult result)
    {
        _families.Text = $"CALLBACK FAMILIES: {result.FamilyCount}";
        _generic.Text =
            $"GENERIC READY: {result.GenericResolvedCount}   |   " +
            $"NON-FRAME: {result.NonFrameOnlyAutoCount}   |   " +
            $"FRAME: {result.FrameOnlyAutoCount}";
        var sharedFamilies = result.SharedProviderReadyFamilies.Length == 0
            ? "none"
            : string.Join(", ", result.SharedProviderReadyFamilies);
        _shared.Text =
            $"SHARED STATE READY: {result.SharedProviderReadyFamilies.Length} families / " +
            $"{result.SharedProviderReadyCallbackCount} callbacks / {result.SharedProviderReadyReadCount} reads";
        _shared.AccessibleDescription = sharedFamilies;
        _semantic.Text =
            $"SEMANTIC READY: {result.SemanticReadyRuleCount}   |   " +
            $"ALREADY SATISFIED: {result.AlreadySatisfiedCount}";
        _unresolved.Text =
            $"MATERIAL REMAINING: {result.MaterialRemainingCount}   |   " +
            $"BELOW 3 ms/s: {result.BelowThresholdCount}";
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
            _resolvedResultsFolder = Path.GetDirectoryName(result.ResolverPath);
            UpdateOpenResultsState();

            UpdateSummary(result);
            _passReady = HasApplicablePass(result);
            _generate.Enabled = _passReady;

            _status.Text = _passReady
                ? $"Complete — {result.RankedCallbackCount} measured callback consumers inspected. Pass ready."
                : $"Complete — {result.RankedCallbackCount} measured callback consumers inspected. No applicable pass changes.";
            _output.Text =
                $"G-CET resolver output:\r\n{result.ResolverPath}\r\n\r\n" +
                "Live CET sources were inspected read-only. No mod files were changed.";
        }
        catch (Exception ex)
        {
            _passReady = false;
            _generate.Enabled = false;
            _status.Text = "Analysis failed.";
            _output.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "G-CET Resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyze.Enabled = true;
            _generate.Enabled = _passReady;
        }
    }

    private void UpdateFrameworkOnly()
    {
        try
        {
            _updateFramework.Enabled = false;
            _status.Text = "Checking 0-Engine and preparing framework-only overlay...";
            Application.DoEvents();

            var mods = ResolveModsFolder(_game.Text);
            var result = ResolverService.GenerateFrameworkUpdate(mods);
            _resolvedResultsFolder = Path.GetDirectoryName(result.ZipPath);
            UpdateOpenResultsState();
            _status.Text = $"0-Engine-only update ready — {result.FileCount} changed framework files.";
            _output.Text =
                $"Framework-only ZIP:\r\n{result.ZipPath}\r\n\r\n" +
                $"Update manifest:\r\n{result.ManifestPath}\r\n\r\n" +
                "No capture or mod optimization candidates required. " +
                "Only changed 0-Engine runtime files are staged; no installed files were edited.";
        }
        catch (Exception ex)
        {
            _status.Text = "Framework update not generated.";
            _output.Text = ex.Message;
            if (!ex.Message.Contains("already up to date", StringComparison.OrdinalIgnoreCase))
                MessageBox.Show(this, ex.Message, "G-CET Resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { _updateFramework.Enabled = true; }
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
            UpdateSummary(resolved);
            _passReady = HasApplicablePass(resolved);

            if (!_passReady)
            {
                _status.Text = "No applicable pass changes remain after live-source revalidation.";
                _output.Text =
                    $"G-CET resolver output:\r\n{resolved.ResolverPath}\r\n\r\n" +
                    "No pass ZIP was generated because the current live stack has no applicable changes.";
                return;
            }

            var pass = ResolverService.GeneratePass(capture, mods);
            _resolvedResultsFolder = Path.GetDirectoryName(pass.ZipPath);
            UpdateOpenResultsState();

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
            _passReady = false;
            _generate.Enabled = false;
            _status.Text = "Pass generation failed.";
            _output.Text = ex.ToString();
            MessageBox.Show(this, ex.Message, "G-CET Resolver", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _analyze.Enabled = true;
            _generate.Enabled = _passReady;
        }
    }
}
