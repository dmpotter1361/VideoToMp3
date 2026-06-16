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
        Font = SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        // Grow to fit the content at whatever font size the user runs.
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddRow(root, MakeLabel(
            "Only needed for PRIVATE playlists, Watch Later, or Liked videos.\n" +
            "Public videos work without this."));

        _enableCheck.Text = "Use my YouTube login when downloading";
        _enableCheck.AutoSize = true;
        _enableCheck.Margin = new Padding(3, 8, 3, 8);
        AddRow(root, _enableCheck);

        AddRow(root, MakeLabel(
            "How to get the cookies file (one time):\n" +
            "  1. In Chrome, signed in to the right account, install the free\n" +
            "       extension “Get cookies.txt LOCALLY”.\n" +
            "  2. Open youtube.com, click the extension, choose Export, and\n" +
            "       save the .txt file somewhere (e.g. your Documents).\n" +
            "  3. Click Browse below and pick that file.\n" +
            "If private videos stop working later, just re-export the file."));

        AddRow(root, MakeLabel("Cookies file:"));
        _fileBox.ReadOnly = true;
        _fileBox.Width = 380;
        _fileBox.Margin = new Padding(0);
        _browseButton.Text = "Browse…";
        _browseButton.AutoSize = true;
        _browseButton.Margin = new Padding(6, 0, 0, 0);
        _browseButton.Click += (_, _) => Browse();
        var fileRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 2, 0, 8) };
        fileRow.Controls.Add(_fileBox, 0, 0);
        fileRow.Controls.Add(_browseButton, 1, 0);
        AddRow(root, fileRow);

        AddRow(root, MakeLabel("Account name (shown in the app, e.g. “Deborah”):"));
        _labelBox.Width = 260;
        _labelBox.Margin = new Padding(0, 2, 0, 8);
        AddRow(root, _labelBox);

        _testButton.Text = "Test login";
        _testButton.AutoSize = true;
        _testButton.Margin = new Padding(0);
        _testStatus.AutoSize = true;
        _testStatus.Anchor = AnchorStyles.Left;
        _testStatus.Margin = new Padding(10, 0, 0, 0);
        _testButton.Click += async (_, _) => await TestLogin();
        var testRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        testRow.Controls.Add(_testButton, 0, 0);
        testRow.Controls.Add(_testStatus, 1, 0);
        testRow.CellBorderStyle = TableLayoutPanelCellBorderStyle.None;
        AddRow(root, testRow);

        _okButton.Text = "Save";
        _okButton.AutoSize = true;
        _okButton.Margin = new Padding(6, 0, 0, 0);
        _okButton.Click += (_, _) => SaveAndClose();
        _cancelButton.Text = "Cancel";
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;
        var buttonRow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 6, 0, 0),
        };
        buttonRow.Controls.Add(_okButton);
        buttonRow.Controls.Add(_cancelButton);
        AddRow(root, buttonRow);

        Controls.Add(root);
        AcceptButton = _okButton;
        CancelButton = _cancelButton;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 2, 3, 2),
    };

    private static void AddRow(TableLayoutPanel root, Control control)
    {
        root.Controls.Add(control, 0, root.RowCount);
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowCount++;
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
            var exit = await RunYtDlp(ytdlp, new[]
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

    private static async Task<int> RunYtDlp(string exe, IReadOnlyList<string> args)
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
        _ = await proc.StandardOutput.ReadToEndAsync();
        _ = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        return proc.ExitCode;
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
