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
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(560, 420);
        MinimumSize = new Size(480, 360);
        StartPosition = FormStartPosition.CenterScreen;

        const int pad = 12;

        var info = new Label
        {
            Text = "Scans for the same song saved more than once (even under different\n" +
                   "names or quality) and removes the extra copies, keeping the best one.",
            Location = new Point(pad, pad),
            AutoSize = true,
        };

        var folderLabel = new Label
        {
            Text = "Folder to check:",
            Location = new Point(pad, info.Bottom + 10),
            AutoSize = true,
        };

        _folderBox.Location = new Point(pad, folderLabel.Bottom + 4);
        _folderBox.Width = ClientSize.Width - pad * 2 - 100;
        _folderBox.ReadOnly = true;
        _folderBox.Text = _settings.OutputFolder;
        _folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _changeFolderButton.Text = "Change…";
        _changeFolderButton.Width = 88;
        _changeFolderButton.Location = new Point(_folderBox.Right + 6, _folderBox.Top - 1);
        _changeFolderButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _changeFolderButton.Click += (_, _) => ChangeFolder();

        _scanButton.Text = "Scan for duplicates";
        _scanButton.Width = 160;
        _scanButton.Height = 30;
        _scanButton.Location = new Point(pad, _folderBox.Bottom + 12);
        _scanButton.Click += async (_, _) => await OnScanClicked();

        _logBox.Location = new Point(pad, _scanButton.Bottom + 12);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(30, 30, 30);
        _logBox.ForeColor = Color.Gainsboro;
        _logBox.Font = new Font("Consolas", 9f);
        _logBox.Width = ClientSize.Width - pad * 2;
        _logBox.Height = ClientSize.Height - _logBox.Top - 36;
        _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        _statusLabel.Location = new Point(pad, ClientSize.Height - 24);
        _statusLabel.AutoSize = true;
        _statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _statusLabel.Text = "Ready.";

        Controls.AddRange(new Control[]
        {
            info, folderLabel, _folderBox, _changeFolderButton, _scanButton, _logBox, _statusLabel,
        });
    }

    private void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to scan for duplicate songs",
            SelectedPath = Directory.Exists(_folderBox.Text)
                ? _folderBox.Text
                : _settings.OutputFolder,
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
            MessageBox.Show(this,
                "The fingerprint tool wasn't found. Install it with:\n\n" +
                "winget install AcoustID.Chromaprint",
                "Missing tool", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
