using System.Diagnostics;

namespace VideoToMp3;

public sealed class ConverterForm : Form
{
    private readonly AppSettings _settings;

    private readonly TextBox _urlBox = new();
    private readonly TextBox _folderBox = new();
    private readonly ComboBox _bitrateBox = new();
    private readonly ComboBox _modeBox = new();
    private readonly CheckBox _playlistCheck = new();
    private readonly CheckBox _dedupeCheck = new();
    private readonly Label _loginStatus = new();
    private readonly Button _loginButton = new();
    private readonly Button _changeFolderButton = new();
    private readonly Button _openFolderButton = new();
    private readonly Button _convertButton = new();
    private readonly TextBox _logBox = new();
    private readonly Label _statusLabel = new();

    private CancellationTokenSource? _cts;
    private bool _busy;

    public ConverterForm(AppSettings settings)
    {
        _settings = settings;
        BuildUi();
        RefreshLoginStatus();
    }

    private void BuildUi()
    {
        Text = "Video to MP3";
        Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 500);
        MinimumSize = new Size(520, 460);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Padding = new Padding(12),
            AutoSize = false,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- URL ---
        AddFullRow(root, MakeLabel("Paste a video link (YouTube or other site):"));
        _urlBox.Dock = DockStyle.Fill;
        _urlBox.Margin = new Padding(0, 2, 0, 8);
        AddFullRow(root, _urlBox);

        // --- Save-to folder + buttons ---
        AddFullRow(root, MakeLabel("Save to (MP3s and Videos go in subfolders here):"));
        _folderBox.ReadOnly = true;
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.Text = _settings.OutputFolder;
        _changeFolderButton.Text = "Change…";
        _changeFolderButton.AutoSize = true;
        _changeFolderButton.Margin = new Padding(6, 0, 0, 0);
        _changeFolderButton.Click += (_, _) => ChangeFolder();
        _openFolderButton.Text = "Open";
        _openFolderButton.AutoSize = true;
        _openFolderButton.Margin = new Padding(6, 0, 0, 0);
        _openFolderButton.Click += (_, _) => OpenFolder();
        var folderRow = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 2, 0, 8) };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _folderBox.Margin = new Padding(0);
        folderRow.Controls.Add(_folderBox, 0, 0);
        folderRow.Controls.Add(_changeFolderButton, 1, 0);
        folderRow.Controls.Add(_openFolderButton, 2, 0);
        AddFullRow(root, folderRow);

        // --- Quality ---
        _bitrateBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _bitrateBox.Items.AddRange(new object[] { "128 kbps", "192 kbps (recommended)", "320 kbps" });
        _bitrateBox.SelectedIndex = _settings.Mp3Bitrate switch { 128 => 0, 320 => 2, _ => 1 };
        _bitrateBox.Margin = new Padding(0, 2, 0, 4);
        SizeComboToContent(_bitrateBox);
        AddLabelledRow(root, "MP3 quality:", _bitrateBox);

        // --- Download mode ---
        _modeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _modeBox.Items.AddRange(new object[] { "MP3 only", "Video only", "MP3 + Video (both)" });
        _modeBox.SelectedIndex = _settings.Mode switch
        {
            DownloadMode.Mp3Only => 0,
            DownloadMode.VideoOnly => 1,
            _ => 2,
        };
        _modeBox.Margin = new Padding(0, 2, 0, 8);
        SizeComboToContent(_modeBox);
        AddLabelledRow(root, "Download:", _modeBox);

        // --- Checkboxes ---
        _playlistCheck.Text = "Download the whole playlist (if the link is one)";
        _playlistCheck.AutoSize = true;
        AddFullRow(root, _playlistCheck);

        _dedupeCheck.Text = "Clean up duplicate songs when done";
        _dedupeCheck.AutoSize = true;
        _dedupeCheck.Checked = _settings.CleanDuplicatesAfter;
        _dedupeCheck.Margin = new Padding(3, 3, 3, 8);
        AddFullRow(root, _dedupeCheck);

        // --- Login status + button ---
        _loginStatus.AutoSize = true;
        _loginStatus.Anchor = AnchorStyles.Left;
        _loginButton.Text = "YouTube login…";
        _loginButton.AutoSize = true;
        _loginButton.Anchor = AnchorStyles.Right;
        _loginButton.Click += (_, _) => OpenLogin();
        root.Controls.Add(_loginStatus, 0, root.RowCount);
        root.Controls.Add(_loginButton, 1, root.RowCount);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowCount++;

        // --- Convert button (right-aligned, own row) ---
        _convertButton.Text = "Convert";
        _convertButton.AutoSize = true;
        _convertButton.Padding = new Padding(14, 4, 14, 4);
        _convertButton.Anchor = AnchorStyles.Right;
        _convertButton.Margin = new Padding(0, 8, 0, 8);
        _convertButton.Click += async (_, _) => await OnConvertClicked();
        AddFullRow(root, _convertButton, anchorRight: true);

        // --- Log (takes remaining space) ---
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(30, 30, 30);
        _logBox.ForeColor = Color.Gainsboro;
        _logBox.Font = new Font("Consolas", 9f);
        _logBox.Dock = DockStyle.Fill;
        _logBox.Margin = new Padding(0, 0, 0, 6);
        root.Controls.Add(_logBox, 0, root.RowCount);
        root.SetColumnSpan(_logBox, 2);
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowCount++;

        // --- Status ---
        _statusLabel.AutoSize = true;
        _statusLabel.Text = "Ready.";
        AddFullRow(root, _statusLabel);

        Controls.Add(root);
        AcceptButton = _convertButton;
    }

    /// <summary>Widen a dropdown so its longest item shows fully at the current font.</summary>
    private static void SizeComboToContent(ComboBox combo)
    {
        int widest = 0;
        foreach (var item in combo.Items)
            widest = Math.Max(widest, TextRenderer.MeasureText(item?.ToString() ?? "", combo.Font).Width);
        combo.Width = widest + 40; // room for the dropdown arrow + padding
        combo.DropDownWidth = combo.Width;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 4, 3, 0),
    };

    private static void AddFullRow(TableLayoutPanel root, Control control, bool anchorRight = false)
    {
        if (anchorRight)
            control.Anchor = AnchorStyles.Right;
        root.Controls.Add(control, 0, root.RowCount);
        root.SetColumnSpan(control, 2);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowCount++;
    }

    private static void AddLabelledRow(TableLayoutPanel root, string label, Control control)
    {
        var lbl = MakeLabel(label);
        lbl.Anchor = AnchorStyles.Left;
        lbl.Margin = new Padding(3, 6, 8, 4);
        root.Controls.Add(lbl, 0, root.RowCount);
        control.Anchor = AnchorStyles.Left;
        root.Controls.Add(control, 1, root.RowCount);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowCount++;
    }

    /// <summary>Called by the tray when the window is brought up; grabs a URL from the clipboard.</summary>
    public void PrefillFromClipboard()
    {
        if (_busy) return;
        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText().Trim();
                if (LooksLikeUrl(text))
                {
                    _urlBox.Text = text;
                    _urlBox.SelectAll();
                }
            }
        }
        catch { /* clipboard can throw transiently; ignore */ }
    }

    private static bool LooksLikeUrl(string text) =>
        (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
        !text.Contains(' ') && !text.Contains('\n');

    private void OpenLogin()
    {
        using var login = new LoginForm(_settings);
        login.ShowDialog(this);
        RefreshLoginStatus();
    }

    private void RefreshLoginStatus()
    {
        if (_settings.LoginActive)
        {
            var who = string.IsNullOrWhiteSpace(_settings.AccountLabel) ? "configured" : _settings.AccountLabel;
            _loginStatus.ForeColor = Color.ForestGreen;
            _loginStatus.Text = $"🔓 Logged in as: {who}";
        }
        else
        {
            _loginStatus.ForeColor = Color.Gray;
            _loginStatus.Text = "🔒 Login off — public videos only";
        }
    }

    private void ChangeFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to save videos and MP3s",
            SelectedPath = Directory.Exists(_settings.OutputFolder)
                ? _settings.OutputFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.OutputFolder = dialog.SelectedPath;
            _folderBox.Text = dialog.SelectedPath;
            _settings.Save();
        }
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            Process.Start(new ProcessStartInfo { FileName = _settings.OutputFolder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Couldn't open folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private int SelectedBitrate => _bitrateBox.SelectedIndex switch { 0 => 128, 2 => 320, _ => 192 };

    private DownloadMode SelectedMode => _modeBox.SelectedIndex switch
    {
        0 => DownloadMode.Mp3Only,
        1 => DownloadMode.VideoOnly,
        _ => DownloadMode.Both,
    };

    private async Task OnConvertClicked()
    {
        if (_busy)
        {
            _cts?.Cancel();
            return;
        }

        var url = _urlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show(this, "Paste a video link first.", "Video to MP3",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ToolLocator.ToolsAvailable)
        {
            using var bootstrap = new BootstrapForm();
            bootstrap.ShowDialog(this);
            if (!ToolLocator.ToolsAvailable)
                return;
        }

        _settings.Mp3Bitrate = SelectedBitrate;
        _settings.Mode = SelectedMode;
        _settings.CleanDuplicatesAfter = _dedupeCheck.Checked;
        _settings.Save();

        SetBusy(true);
        _logBox.Clear();
        _statusLabel.Text = "Working…";
        _cts = new CancellationTokenSource();

        try
        {
            var converter = new Converter(AppendLog);
            var cookies = _settings.LoginActive ? _settings.CookiesFilePath : null;
            var results = await converter.RunAsync(
                url, _settings.OutputFolder, SelectedBitrate, _playlistCheck.Checked,
                SelectedMode, cookies, _cts.Token);

            if (results.Count == 0)
            {
                _statusLabel.Text = "Nothing new — already in your library.";
            }
            else if (results.Count == 1)
            {
                var r = results[0];
                var saved = r.Mp3Path ?? r.VideoPath;
                _statusLabel.Text = $"Saved: {Path.GetFileName(saved)}";
            }
            else
            {
                _statusLabel.Text = $"Saved {results.Count} new files.";
                AppendLog($"\nSaved {results.Count} new file(s) under:\n{_settings.OutputFolder}\n");
            }

            // Optional unattended cleanup so a big batch comes back deduped.
            if (_dedupeCheck.Checked)
            {
                _statusLabel.Text = "Checking for duplicate songs…";
                AppendLog("\n--- Cleaning up duplicate songs ---\n");
                var finder = new DuplicateFinder(AppendLog);
                var groups = await finder.ScanAsync(_settings.OutputFolder, _cts.Token);
                int removed = DuplicateFinder.SendExtrasToRecycleBin(groups, AppendLog);
                _statusLabel.Text = removed > 0
                    ? $"Done. Removed {removed} duplicate(s) to the Recycle Bin."
                    : "Done. No duplicates found.";
            }
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
            MessageBox.Show(this, ex.Message, "Conversion failed",
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
        _convertButton.Text = busy ? "Cancel" : "Convert";
        _urlBox.Enabled = !busy;
        _changeFolderButton.Enabled = !busy;
        _bitrateBox.Enabled = !busy;
        _modeBox.Enabled = !busy;
        _playlistCheck.Enabled = !busy;
        _dedupeCheck.Enabled = !busy;
        _loginButton.Enabled = !busy;
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
        // X just hides the window so the tray app keeps running; real exit
        // comes from the tray menu.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
