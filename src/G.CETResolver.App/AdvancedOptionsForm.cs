using System.Text.Json;

namespace GCETRuntimeProfiler.Resolver;

internal sealed class AdvancedOptionsForm : Form
{
    private static readonly string[] ClassificationChoices =
    {
        "",
        "CONTINUOUS",
        "HARD_DORMANT",
        "DISCOVERY_DORMANT",
        "BACKGROUND",
        "I_DONT_KNOW"
    };

    private readonly string _resolverPath;
    private readonly string _hintsPath;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect
    };
    private readonly Label _summary = new()
    {
        AutoSize = true,
        Dock = DockStyle.Fill
    };
    private readonly TextBox _instructions = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill
    };
    private readonly Button _save = new()
    {
        Text = "SAVE CLASSIFICATION HINTS",
        Height = 34,
        Dock = DockStyle.Right,
        AutoSize = true
    };

    public AdvancedOptionsForm(string resolverPath)
    {
        _resolverPath = Path.GetFullPath(resolverPath);
        _hintsPath = Path.Combine(
            Path.GetDirectoryName(_resolverPath)!,
            "G-CET_Advanced_UserHints.json");

        Text = "G-CET Resolver — Advanced Optimize";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 580);
        Size = new Size(1180, 700);

        BuildGrid();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            RowCount = 4,
            ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        var header = new Label
        {
            Text = "Only unresolved callbacks whose measured cost justifies the remaining difficulty are shown. " +
                   "Your classification is semantic evidence only; it never authorizes a patch.",
            AutoSize = false,
            Dock = DockStyle.Fill
        };

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        footer.Controls.Add(_save);
        footer.Controls.Add(_summary);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_instructions, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);

        _grid.SelectionChanged += (_, _) => ShowSelectedInstructions();
        _save.Click += (_, _) => SaveHints();

        LoadCandidates();
    }

    private void BuildGrid()
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Owner",
            HeaderText = "Mod",
            DataPropertyName = "Owner",
            Width = 180,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Target",
            HeaderText = "Callback",
            DataPropertyName = "Target",
            Width = 210,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Dormancy",
            HeaderText = "Resolver class",
            DataPropertyName = "Dormancy",
            Width = 145,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Cost",
            HeaderText = "CET ms/s",
            DataPropertyName = "Cost",
            Width = 85,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Difficulty",
            HeaderText = "Difficulty",
            DataPropertyName = "Difficulty",
            Width = 145,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "NextEvidence",
            HeaderText = "What resolver needs next",
            DataPropertyName = "NextEvidence",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            ReadOnly = true
        });

        var classification = new DataGridViewComboBoxColumn
        {
            Name = "Classification",
            HeaderText = "Your classification",
            Width = 175,
            FlatStyle = FlatStyle.Flat
        };
        classification.Items.AddRange(ClassificationChoices);
        _grid.Columns.Add(classification);
    }

    private void LoadCandidates()
    {
        var rows = new List<AdvancedRow>();
        using var doc = JsonDocument.Parse(File.ReadAllText(_resolverPath));

        if (doc.RootElement.TryGetProperty("callbackFamilies", out var families) &&
            families.ValueKind == JsonValueKind.Array)
        {
            foreach (var family in families.EnumerateArray())
            {
                if (!family.TryGetProperty("topConsumers", out var consumers) ||
                    consumers.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var consumer in consumers.EnumerateArray())
                {
                    if (!consumer.TryGetProperty("advanced", out var advanced) ||
                        advanced.ValueKind != JsonValueKind.Object)
                        continue;

                    var eligible = Bool(advanced, "Eligible");
                    var existingHint = Str(advanced, "UserHint");
                    if (!eligible && string.IsNullOrWhiteSpace(existingHint))
                        continue;

                    var runtime = consumer.TryGetProperty("runtime", out var runtimeNode)
                        ? runtimeNode
                        : default;
                    var dormancy = consumer.TryGetProperty("dormancy", out var dormancyNode)
                        ? dormancyNode
                        : default;

                    rows.Add(new AdvancedRow
                    {
                        Owner = Str(consumer, "owner"),
                        Kind = Str(consumer, "kind"),
                        Target = Str(consumer, "target"),
                        Dormancy = dormancy.ValueKind == JsonValueKind.Object
                            ? Str(dormancy, "Class")
                            : "UNKNOWN",
                        Cost = runtime.ValueKind == JsonValueKind.Object
                            ? Num(runtime, "exclusiveMsPerSecond")
                            : 0,
                        DifficultyNumber = (int)Num(advanced, "Difficulty"),
                        Difficulty = $"{(int)Num(advanced, "Difficulty")} — {Str(advanced, "DifficultyLabel")}",
                        NextEvidence = Str(advanced, "NextEvidence"),
                        Reason = Str(advanced, "Reason"),
                        UserClassificationUseful = Bool(advanced, "UserClassificationUseful"),
                        Classification = existingHint
                    });
                }
            }
        }

        rows = rows
            .OrderByDescending(x => x.Cost)
            .ThenBy(x => x.DifficultyNumber)
            .ToList();

        _grid.Rows.Clear();
        foreach (var row in rows)
        {
            var index = _grid.Rows.Add(
                row.Owner,
                row.Target,
                row.Dormancy,
                row.Cost.ToString("0.###"),
                row.Difficulty,
                FriendlyNextEvidence(row.NextEvidence),
                string.IsNullOrWhiteSpace(row.Classification) ? "" : row.Classification);

            var gridRow = _grid.Rows[index];
            gridRow.Tag = row;

            var classificationCell = gridRow.Cells["Classification"];
            classificationCell.ReadOnly = !row.UserClassificationUseful &&
                                          string.IsNullOrWhiteSpace(row.Classification);
            if (classificationCell.ReadOnly)
                classificationCell.ToolTipText = "Resolver already has a source-derived class; no user classification is needed.";
        }

        _summary.Text = rows.Count == 0
            ? "No Advanced candidates clear the current cost/difficulty threshold."
            : $"{rows.Count} Advanced candidate(s). Classify only rows where the resolver explicitly asks.";

        if (_grid.Rows.Count > 0)
            _grid.Rows[0].Selected = true;
        ShowSelectedInstructions();
    }

    private void ShowSelectedInstructions()
    {
        if (_grid.SelectedRows.Count == 0 ||
            _grid.SelectedRows[0].Tag is not AdvancedRow row)
        {
            _instructions.Text = "";
            return;
        }

        var instruction = row.NextEvidence switch
        {
            "USER_CLASSIFICATION" =>
                "Tell the resolver what kind of feature this is.\r\n\r\n" +
                "CONTINUOUS — camera, combat, realtime HUD/controller or anything that must react every frame.\r\n" +
                "HARD_DORMANT — meaningful work starts only after a keybind/tool/session is activated.\r\n" +
                "DISCOVERY_DORMANT — it must occasionally find something in the world, then becomes fully active.\r\n" +
                "BACKGROUND — queue/service/framework work that should sleep only when no work is pending.\r\n" +
                "I_DONT_KNOW — keep it unresolved and let a targeted profiler run gather more evidence.",
            "TARGETED_INACTIVE_ACTIVE_INACTIVE_CAPTURE" =>
                "Targeted profiler task:\r\n\r\nDON'T USE MOD → USE MOD → STOP USING MOD\r\n\r\n" +
                "Keep the feature inactive, activate/use it, then deactivate it. The resolver will compare runtime paths and state transitions.",
            "TARGETED_INACTIVE_NEAR_ACTIVE_LEAVE_CAPTURE" =>
                "Targeted profiler task:\r\n\r\nFAR AWAY → APPROACH → USE ACTIVITY → LEAVE\r\n\r\n" +
                "This separates dormant discovery work from the full active path.",
            "SOURCE_DEPENDENCY_PROOF_FOR_DISCOVERY_EXTRACTION" =>
                "No user action is needed yet. The resolver already proved the author's discovery cadence. " +
                "It now needs to prove that the discovery region can be extracted without changing captured state, pause behavior or timer phase.",
            "SOURCE_ACTIVE_STATE_WAKE_PROOF" =>
                "No extra profiler run yet. The resolver will search source for the active-state writer and a complete independent wake path.",
            "SOURCE_QUEUE_PENDING_RESOURCE_PROOF" =>
                "No extra profiler run yet. The resolver will search for queue-empty, no-pending-work or no-subscriber states before considering sleep.",
            _ => FriendlyNextEvidence(row.NextEvidence)
        };

        _instructions.Text =
            $"{row.Owner} — {row.Target}\r\n" +
            $"Measured CET cost: {row.Cost:0.###} ms/s\r\n" +
            $"Resolver class: {row.Dormancy}\r\n" +
            $"{row.Reason}\r\n\r\n" +
            instruction;
    }

    private void SaveHints()
    {
        var entries = new List<object>();

        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            if (gridRow.Tag is not AdvancedRow row)
                continue;

            var value = Convert.ToString(gridRow.Cells["Classification"].Value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(value))
                continue;

            entries.Add(new
            {
                owner = row.Owner,
                kind = row.Kind,
                target = row.Target,
                classification = value
            });
        }

        var document = new
        {
            schemaVersion = "0.1",
            generatedUtc = DateTime.UtcNow.ToString("O"),
            policy = new
            {
                semanticEvidenceOnly = true,
                canAuthorizeGeneration = false
            },
            entries
        };

        File.WriteAllText(
            _hintsPath,
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }) +
            Environment.NewLine);

        _summary.Text = entries.Count == 0
            ? "No classification hints were saved."
            : $"Saved {entries.Count} evidence-only classification hint(s). Re-run ANALYZE to apply them.";
    }

    private static string FriendlyNextEvidence(string value) => value switch
    {
        "USER_CLASSIFICATION" => "User classification",
        "TARGETED_INACTIVE_ACTIVE_INACTIVE_CAPTURE" => "Inactive → active → inactive profile",
        "TARGETED_INACTIVE_NEAR_ACTIVE_LEAVE_CAPTURE" => "Far → near → active → leave profile",
        "SOURCE_DEPENDENCY_PROOF_FOR_DISCOVERY_EXTRACTION" => "Source dependency proof",
        "SOURCE_ACTIVE_STATE_WAKE_PROOF" => "Source wake-path proof",
        "SOURCE_QUEUE_PENDING_RESOURCE_PROOF" => "Queue/pending-resource proof",
        "NONE_USER_MARKED_CONTINUOUS" => "No dormancy work — marked continuous",
        _ => value.Replace('_', ' ')
    };

    private static string Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static bool Bool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static double Num(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.TryGetDouble(out var number)
            ? number
            : 0;

    private sealed class AdvancedRow
    {
        public string Owner { get; init; } = "";
        public string Kind { get; init; } = "";
        public string Target { get; init; } = "";
        public string Dormancy { get; init; } = "";
        public double Cost { get; init; }
        public int DifficultyNumber { get; init; }
        public string Difficulty { get; init; } = "";
        public string NextEvidence { get; init; } = "";
        public string Reason { get; init; } = "";
        public bool UserClassificationUseful { get; init; }
        public string Classification { get; init; } = "";
    }
}
