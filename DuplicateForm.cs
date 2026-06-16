namespace VideoToMp3;

public sealed class DuplicateForm : Form
{
    private readonly AppSettings _settings;

    private readonly TextBox _folderBox = new();
    private readonly Button _changeFolderButton = new();
    private readonly Button _scanButton = new();
    private readonly TextBox _logBox = new();
    private readonly Label _statusLabel = new();

    private CancellationTokenSource? _cts;
    private bool _busy;

    public DuplicateForm(AppSettings settings)
    {
        _settings = settings;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Find Duplicate Songs";
        Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(600, 460);
        MinimumSize = new Size(500, 400);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Padding = new Padding(12),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddRow(root, MakeLabel(
            "Scans for the same song saved more than once (even under different\n" +
            "names or quality) and removes the extra copies, keeping the best one."));

        AddRow(root, MakeLabel("Folder to check:"));

        _folderBox.ReadOnly = true;
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Text = _settings.OutputFolder;
        _folderBox.Margin = new Padding(0);
        _changeFolderButton.Text = "Change…";
        _changeFolderButton.AutoSize = true;
        _changeFolderButton.Margin = new Padding(6, 0, 0, 0);
        _changeFolderButton.Click += (_, _) => ChangeFolder();
        var folderRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 2, 0, 8) };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(_folderBox, 0, 0);
        folderRow.Controls.Add(_changeFolderButton, 1, 0);
        AddRow(root, folderRow);

        _scanButton.Text = "Scan for duplicates";
        _scanButton.AutoSize = true;
        _scanButton.Padding = new Padding(10, 4, 10, 4);
        _scanButton.Anchor = AnchorStyles.Left;
        _scanButton.Margin = new Padding(0, 0, 0, 8);
        _scanButton.Click += async (_, _) => await OnScanClicked();
        AddRow(root, _scanButton);

        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(30, 30, 30);
        _logBox.ForeColor = Color.Gainsboro;
        _logBox.Font = new Font("Consolas", 9f);
        _logBox.Dock = DockStyle.Fill;
        _logBox.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_logBox, 0, root.RowCount);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowCount++;

        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Ready.";
        AddRow(root, _statusLabel);

        Controls.Add(root);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 2),
    };

    private static void AddRow(TableLayoutPanel root, Control control)
    {
        root.Controls.Add(control, 0, root.RowCount);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowCount++;
    }

    private void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to scan for duplicate songs",
            SelectedPath = Directory.Exists(_folderBox.Text) ? _folderBox.Text : _settings.OutputFolder,
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folderBox.Text = dialog.SelectedPath;
    }

    private async Task OnScanClicked()
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        var folder = _folderBox.Text.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(this, "That folder doesn't exist.", "Find Duplicates",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (ToolLocator.FpcalcPath is null)
        {
            using var bootstrap = new BootstrapForm();
            bootstrap.ShowDialog(this);
            if (ToolLocator.FpcalcPath is null)
                return;
        }

        SetBusy(true);
        _logBox.Clear();
        _statusLabel.Text = "Scanning…";
        _cts = new CancellationTokenSource();

        try
        {
            var finder = new DuplicateFinder(AppendLog);
            var groups = await finder.ScanAsync(folder, _cts.Token);

            var extras = groups.SelectMany(g => g.Extras).ToList();
            if (extras.Count == 0)
            {
                _statusLabel.Text = "No duplicates found.";
                AppendLog("\nNo duplicate songs found. Nothing to clean up.\n");
                return;
            }

            AppendLog($"\nFound {groups.Count} song(s) with duplicates:\n");
            foreach (var g in groups)
            {
                AppendLog($"\n  KEEP:   {g.Keeper.Name}  ({g.Keeper.Length / 1024} KB)\n");
                foreach (var e in g.Extras)
                    AppendLog($"  REMOVE: {e.Name}  ({e.Length / 1024} KB)\n");
            }

            var answer = MessageBox.Show(this,
                $"Found {extras.Count} extra copy/copies across {groups.Count} song(s).\n\n" +
                "Move the extras to the Recycle Bin? The best-quality copy of each is kept, " +
                "and anything removed can be restored from the Recycle Bin.",
                "Remove duplicates?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                _statusLabel.Text = "Left everything in place.";
                AppendLog("\nNo files were removed.\n");
                return;
            }

            int removed = DuplicateFinder.SendExtrasToRecycleBin(groups, AppendLog);
            _statusLabel.Text = $"Removed {removed} duplicate(s) to the Recycle Bin.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Cancelled.";
            AppendLog("\nCancelled.\n");
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Failed.";
            AppendLog($"\nError: {ex.Message}\n");
            MessageBox.Show(this, ex.Message, "Scan failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _scanButton.Text = busy ? "Cancel" : "Scan for duplicates";
        _changeFolderButton.Enabled = !busy;
        UseWaitCursor = busy;
    }

    private void AppendLog(string text)
    {
        if (_logBox.IsDisposed) return;
        if (_logBox.InvokeRequired)
        {
            _logBox.BeginInvoke(() => AppendLog(text));
            return;
        }
        _logBox.AppendText(text.Replace("\n", Environment.NewLine));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
