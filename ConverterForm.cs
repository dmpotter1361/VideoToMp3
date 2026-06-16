using System.Diagnostics;

namespace VideoToMp3;

public sealed class ConverterForm : Form
{
    private readonly AppSettings _settings;

    private readonly TextBox _urlBox = new();
    private readonly TextBox _folderBox = new();
    private readonly ComboBox _bitrateBox = new();
    private readonly CheckBox _playlistCheck = new();
    private readonly CheckBox _dedupeCheck = new();
    private readonly Label _loginStatus = new();
    private readonly Button _loginButton = new();
    private readonly Button _convertButton = new();
    private readonly Button _changeFolderButton = new();
    private readonly Button _openFolderButton = new();
    private readonly TextBox _logBox = new();
    private readonly Label _statusLabel = new();

    private CancellationTokenSource? _cts;
    private bool _busy;

    public ConverterForm(AppSettings settings)
    {
        _settings = settings;
        BuildUi();
    }

    private void BuildUi()
    {
        Text = "Video to MP3";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(560, 440);
        MinimumSize = new Size(480, 380);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;

        var pad = 12;

        var urlLabel = new Label
        {
            Text = "Paste a video link (YouTube or other site):",
            Location = new Point(pad, pad),
            AutoSize = true,
        };

        _urlBox.Location = new Point(pad, urlLabel.Bottom + 4);
        _urlBox.Width = ClientSize.Width - pad * 2;
        _urlBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var folderLabel = new Label
        {
            Text = "Save to (MP3s and Videos go in subfolders here):",
            Location = new Point(pad, _urlBox.Bottom + 12),
            AutoSize = true,
        };

        _folderBox.Location = new Point(pad, folderLabel.Bottom + 4);
        _folderBox.Width = ClientSize.Width - pad * 2 - 180;
        _folderBox.ReadOnly = true;
        _folderBox.Text = _settings.OutputFolder;
        _folderBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        _changeFolderButton.Text = "Change…";
        _changeFolderButton.Width = 80;
        _changeFolderButton.Location = new Point(_folderBox.Right + 6, _folderBox.Top - 1);
        _changeFolderButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _changeFolderButton.Click += (_, _) => ChangeFolder();

        _openFolderButton.Text = "Open";
        _openFolderButton.Width = 80;
        _openFolderButton.Location = new Point(_changeFolderButton.Right + 6, _folderBox.Top - 1);
        _openFolderButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _openFolderButton.Click += (_, _) => OpenFolder();

        var qualityLabel = new Label
        {
            Text = "MP3 quality:",
            Location = new Point(pad, _folderBox.Bottom + 14),
            AutoSize = true,
        };

        _bitrateBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _bitrateBox.Items.AddRange(new object[] { "128 kbps", "192 kbps (recommended)", "320 kbps" });
        _bitrateBox.Width = 180;
        _bitrateBox.Location = new Point(qualityLabel.Right + 8, qualityLabel.Top - 3);
        _bitrateBox.SelectedIndex = _settings.Mp3Bitrate switch { 128 => 0, 320 => 2, _ => 1 };

        _convertButton.Text = "Convert";
        _convertButton.Width = 120;
        _convertButton.Height = 30;
        _convertButton.Location = new Point(ClientSize.Width - pad - _convertButton.Width, _bitrateBox.Top - 3);
        _convertButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _convertButton.Click += async (_, _) => await OnConvertClicked();

        _playlistCheck.Text = "Download the whole playlist (if the link is one)";
        _playlistCheck.AutoSize = true;
        _playlistCheck.Location = new Point(pad, _bitrateBox.Bottom + 12);

        _dedupeCheck.Text = "Clean up duplicate songs when done";
        _dedupeCheck.AutoSize = true;
        _dedupeCheck.Location = new Point(pad, _playlistCheck.Bottom + 6);
        _dedupeCheck.Checked = _settings.CleanDuplicatesAfter;

        _loginStatus.AutoSize = true;
        _loginStatus.Location = new Point(pad, _dedupeCheck.Bottom + 12);

        _loginButton.Text = "YouTube login…";
        _loginButton.Width = 130;
        _loginButton.Location = new Point(ClientSize.Width - pad - _loginButton.Width, _dedupeCheck.Bottom + 8);
        _loginButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _loginButton.Click += (_, _) => OpenLogin();

        _logBox.Location = new Point(pad, _loginButton.Bottom + 12);
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
            urlLabel, _urlBox, folderLabel, _folderBox, _changeFolderButton, _openFolderButton,
            qualityLabel, _bitrateBox, _playlistCheck, _dedupeCheck,
            _loginStatus, _loginButton, _convertButton, _logBox, _statusLabel,
        });

        AcceptButton = _convertButton;
        RefreshLoginStatus();
    }

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
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.OutputFolder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Couldn't open folder",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private int SelectedBitrate => _bitrateBox.SelectedIndex switch { 0 => 128, 2 => 320, _ => 192 };

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
                url, _settings.OutputFolder, SelectedBitrate, _playlistCheck.Checked, cookies, _cts.Token);

            if (results.Count == 0)
            {
                _statusLabel.Text = "Nothing new — already in your library.";
            }
            else if (results.Count == 1)
            {
                _statusLabel.Text = $"Saved: {Path.GetFileName(results[0].Mp3Path)}";
                AppendLog($"\nMP3:   {results[0].Mp3Path}\nVideo: {results[0].VideoPath}\n");
            }
            else
            {
                _statusLabel.Text = $"Saved {results.Count} new files (MP3s + videos).";
                AppendLog($"\nSaved {results.Count} new videos + MP3s under:\n{_settings.OutputFolder}\n");
            }

            // Optional unattended cleanup so a big batch comes back deduped.
            // Sends extras to the Recycle Bin (recoverable) with no prompt.
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
                AppendLog(removed > 0
                    ? $"\nCleanup complete — {removed} duplicate(s) sent to the Recycle Bin.\n"
                    : "\nCleanup complete — no duplicates found.\n");
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
