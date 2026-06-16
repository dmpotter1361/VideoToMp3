using System.Diagnostics;
using System.Text;

namespace VideoToMp3;

/// <summary>
/// Optional YouTube login via an exported cookies.txt file, so private
/// playlists / Watch Later / Liked videos can be downloaded.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _enableCheck = new();
    private readonly TextBox _fileBox = new();
    private readonly Button _browseButton = new();
    private readonly TextBox _labelBox = new();
    private readonly Button _testButton = new();
    private readonly Label _testStatus = new();
    private readonly Button _okButton = new();
    private readonly Button _cancelButton = new();

    public LoginForm(AppSettings settings)
    {
        _settings = settings;
        BuildUi();
        LoadFromSettings();
    }

    private void BuildUi()
    {
        Text = "YouTube Login";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(540, 430);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        const int pad = 14;

        var intro = new Label
        {
            Text = "Only needed for PRIVATE playlists, Watch Later, or Liked videos.\n" +
                   "Public videos work without this.",
            Location = new Point(pad, pad),
            AutoSize = true,
        };

        _enableCheck.Text = "Use my YouTube login when downloading";
        _enableCheck.AutoSize = true;
        _enableCheck.Location = new Point(pad, intro.Bottom + 10);

        var howTo = new Label
        {
            Text =
                "How to get the cookies file (one time):\n" +
                "  1. In Chrome, signed in to the right account, install the free\n" +
                "       extension “Get cookies.txt LOCALLY”.\n" +
                "  2. Open youtube.com, click the extension, choose Export, and\n" +
                "       save the .txt file somewhere (e.g. your Documents).\n" +
                "  3. Click Browse below and pick that file.\n" +
                "If private videos stop working later, just re-export the file.",
            Location = new Point(pad, _enableCheck.Bottom + 10),
            AutoSize = true,
        };

        var fileLabel = new Label { Text = "Cookies file:", Location = new Point(pad, howTo.Bottom + 12), AutoSize = true };

        _fileBox.Location = new Point(pad, fileLabel.Bottom + 4);
        _fileBox.Width = ClientSize.Width - pad * 2 - 100;
        _fileBox.ReadOnly = true;

        _browseButton.Text = "Browse…";
        _browseButton.Width = 88;
        _browseButton.Location = new Point(_fileBox.Right + 6, _fileBox.Top - 1);
        _browseButton.Click += (_, _) => Browse();

        var labelLabel = new Label
        {
            Text = "Account name (shown in the app, e.g. “Deborah”):",
            Location = new Point(pad, _fileBox.Bottom + 12),
            AutoSize = true,
        };
        _labelBox.Location = new Point(pad, labelLabel.Bottom + 4);
        _labelBox.Width = 260;

        _testButton.Text = "Test login";
        _testButton.Width = 100;
        _testButton.Location = new Point(pad, _labelBox.Bottom + 14);
        _testButton.Click += async (_, _) => await TestLogin();

        _testStatus.AutoSize = true;
        _testStatus.Location = new Point(_testButton.Right + 10, _testButton.Top + 4);
        _testStatus.Text = "";

        _okButton.Text = "Save";
        _okButton.Width = 90;
        _okButton.Location = new Point(ClientSize.Width - pad - 90, ClientSize.Height - 40);
        _okButton.Click += (_, _) => SaveAndClose();

        _cancelButton.Text = "Cancel";
        _cancelButton.Width = 90;
        _cancelButton.Location = new Point(_okButton.Left - 96, ClientSize.Height - 40);
        _cancelButton.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[]
        {
            intro, _enableCheck, howTo, fileLabel, _fileBox, _browseButton,
            labelLabel, _labelBox, _testButton, _testStatus, _okButton, _cancelButton,
        });

        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private void LoadFromSettings()
    {
        _enableCheck.Checked = _settings.UseLogin;
        _fileBox.Text = _settings.CookiesFilePath;
        _labelBox.Text = _settings.AccountLabel;
    }

    private void Browse()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select your exported cookies.txt",
            Filter = "Cookies file (*.txt)|*.txt|All files (*.*)|*.*",
        };
        if (Directory.Exists(Path.GetDirectoryName(_fileBox.Text)))
            dialog.InitialDirectory = Path.GetDirectoryName(_fileBox.Text);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _fileBox.Text = dialog.FileName;
            _enableCheck.Checked = true;
        }
    }

    private async Task TestLogin()
    {
        var file = _fileBox.Text.Trim();
        if (!File.Exists(file))
        {
            _testStatus.ForeColor = Color.DarkOrange;
            _testStatus.Text = "Pick a cookies file first.";
            return;
        }
        if (ToolLocator.YtDlpPath is not string ytdlp)
        {
            _testStatus.ForeColor = Color.Firebrick;
            _testStatus.Text = "yt-dlp not found.";
            return;
        }

        _testButton.Enabled = false;
        _testStatus.ForeColor = Color.Gray;
        _testStatus.Text = "Checking…";
        UseWaitCursor = true;

        try
        {
            // The Liked-videos list (LL) is only reachable when logged in, so a
            // clean exit means the cookies are valid and signed in.
            var (exit, _) = await RunYtDlp(ytdlp, new[]
            {
                "--cookies", file,
                "--flat-playlist", "--skip-download", "--no-warnings",
                "--playlist-items", "1",
                "-O", "%(title)s",
                "https://www.youtube.com/playlist?list=LL",
            });

            if (exit == 0)
            {
                _testStatus.ForeColor = Color.ForestGreen;
                _testStatus.Text = "Login works ✓";
            }
            else
            {
                _testStatus.ForeColor = Color.Firebrick;
                _testStatus.Text = "Not logged in — re-export the cookies file.";
            }
        }
        catch (Exception ex)
        {
            _testStatus.ForeColor = Color.Firebrick;
            _testStatus.Text = "Test failed: " + ex.Message;
        }
        finally
        {
            _testButton.Enabled = true;
            UseWaitCursor = false;
        }
    }

    private static async Task<(int exit, string output)> RunYtDlp(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync();
        _ = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return (proc.ExitCode, stdout);
    }

    private void SaveAndClose()
    {
        var file = _fileBox.Text.Trim();
        if (_enableCheck.Checked && !File.Exists(file))
        {
            MessageBox.Show(this,
                "Pick a valid cookies file, or untick “Use my YouTube login”.",
                "YouTube Login", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.UseLogin = _enableCheck.Checked;
        _settings.CookiesFilePath = file;
        _settings.AccountLabel = _labelBox.Text.Trim();
        _settings.Save();

        DialogResult = DialogResult.OK;
        Close();
    }
}
