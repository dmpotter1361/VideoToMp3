using System.Diagnostics;
using System.Text;

namespace VideoToMp3;

/// <summary>
/// First-run setup. If the helper tools (yt-dlp/ffmpeg/Chromaprint) are missing,
/// this offers to install them with one click via winget — no command line needed.
/// </summary>
public sealed class BootstrapForm : Form
{
    private readonly Label _message = new();
    private readonly Button _installButton = new();
    private readonly Button _laterButton = new();
    private readonly TextBox _logBox = new();
    private bool _busy;

    public BootstrapForm()
    {
        Text = "Video to MP3 — First-time setup";
        Font = new Font("Segoe UI", 9f);
        ClientSize = new Size(520, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        const int pad = 16;

        _message.Text =
            "Almost ready!\n\n" +
            "To download and convert videos, Video to MP3 needs a few small, free\n" +
            "helper tools. This is a one-time setup and takes about a minute.\n\n" +
            "Click Install to set everything up automatically.";
        _message.Location = new Point(pad, pad);
        _message.AutoSize = true;

        _installButton.Text = "Install (recommended)";
        _installButton.Width = 180;
        _installButton.Height = 32;
        _installButton.Location = new Point(pad, 150);
        _installButton.Click += async (_, _) => await InstallAsync();

        _laterButton.Text = "Not now";
        _laterButton.Width = 100;
        _laterButton.Height = 32;
        _laterButton.Location = new Point(_installButton.Right + 10, 150);
        _laterButton.Click += (_, _) => Close();

        _logBox.Location = new Point(pad, 196);
        _logBox.Size = new Size(ClientSize.Width - pad * 2, ClientSize.Height - 196 - pad);
        _logBox.Multiline = true;
        _logBox.ReadOnly = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.BackColor = Color.FromArgb(30, 30, 30);
        _logBox.ForeColor = Color.Gainsboro;
        _logBox.Font = new Font("Consolas", 8.5f);
        _logBox.Visible = false;

        Controls.AddRange(new Control[] { _message, _installButton, _laterButton, _logBox });
        AcceptButton = _installButton;
    }

    private async Task InstallAsync()
    {
        if (_busy) return;

        var winget = FindWinget();
        if (winget is null)
        {
            MessageBox.Show(this,
                "This setup needs Windows' “App Installer” (winget), which seems to be missing.\n\n" +
                "Please install “App Installer” from the Microsoft Store, then reopen Video to MP3.",
                "Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            try { Process.Start(new ProcessStartInfo { FileName = "ms-windows-store://pdp/?productid=9NBLGGH4NNS1", UseShellExecute = true }); } catch { }
            return;
        }

        _busy = true;
        _installButton.Enabled = false;
        _laterButton.Enabled = false;
        _logBox.Visible = true;
        UseWaitCursor = true;
        _message.Text = "Installing helper tools… please wait.\nThis can take a minute or two.";

        try
        {
            // yt-dlp brings ffmpeg with it; Chromaprint powers duplicate detection.
            await RunWinget(winget, "yt-dlp.yt-dlp");
            await RunWinget(winget, "AcoustID.Chromaprint");

            ToolLocator.ResetCache();

            if (ToolLocator.ToolsAvailable)
            {
                _message.Text = "All set! ✓  You can close this window and start converting videos.";
                AppendLog("\nSetup complete. Everything is ready.\n");
                _laterButton.Text = "Close";
                _laterButton.Enabled = true;
                _installButton.Visible = false;
            }
            else
            {
                _message.Text = "Setup didn't finish. You can try again, or close and retry later.";
                AppendLog("\nThe tools still weren't detected. Please try again.\n");
                _installButton.Enabled = true;
                _laterButton.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _message.Text = "Something went wrong during setup.";
            AppendLog($"\nError: {ex.Message}\n");
            _installButton.Enabled = true;
            _laterButton.Enabled = true;
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
        }
    }

    private async Task RunWinget(string winget, string id)
    {
        AppendLog($"Installing {id}…\n");
        var psi = new ProcessStartInfo
        {
            FileName = winget,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in new[]
        {
            "install", "--id", id, "-e",
            "--accept-package-agreements", "--accept-source-agreements",
            "--disable-interactivity",
        })
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data + "\n"); };
        proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) AppendLog(e.Data + "\n"); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        await proc.WaitForExitAsync();
        AppendLog($"Finished {id}.\n");
    }

    private static string? FindWinget()
    {
        var direct = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        if (File.Exists(direct)) return direct;

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var c = Path.Combine(dir.Trim(), "winget.exe");
                if (File.Exists(c)) return c;
            }
            catch { }
        }
        return null;
    }

    private void AppendLog(string text)
    {
        if (_logBox.IsDisposed) return;
        if (_logBox.InvokeRequired) { _logBox.BeginInvoke(() => AppendLog(text)); return; }
        _logBox.AppendText(text.Replace("\n", Environment.NewLine));
    }
}
