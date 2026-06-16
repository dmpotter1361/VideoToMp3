using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace VideoToMp3;

public sealed class TrayAppContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _trayIcon;
    private ConverterForm? _form;
    private DuplicateForm? _dupeForm;

    public TrayAppContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Convert a video…", null, (_, _) => ShowConverter());
        menu.Items.Add("Find duplicate songs…", null, (_, _) => ShowDuplicates());
        menu.Items.Add("YouTube login…", null, (_, _) => ShowLogin());
        menu.Items.Add("Open output folder", null, (_, _) => OpenOutputFolder());
        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsStartupEnabled(),
        };
        startupItem.Click += (_, _) => ToggleStartup(startupItem);
        menu.Items.Add(startupItem);
        menu.Items.Add("Create desktop shortcut", null, (_, _) => CreateShortcut());

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _trayIcon = new NotifyIcon
        {
            Icon = BuildIcon(),
            Text = "Video to MP3",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowConverter();

        if (!ToolLocator.ToolsAvailable)
        {
            // Guide the user through one-click setup instead of a dead-end warning.
            using var bootstrap = new BootstrapForm();
            bootstrap.ShowDialog();
        }

        if (ToolLocator.ToolsAvailable)
        {
            _trayIcon.BalloonTipTitle = "Video to MP3 is running";
            _trayIcon.BalloonTipText = "Double-click the tray icon to convert a video.";
            _trayIcon.ShowBalloonTip(4000);
        }
    }

    private void ShowConverter()
    {
        if (_form is null || _form.IsDisposed)
            _form = new ConverterForm(_settings);

        _form.Show();
        _form.PrefillFromClipboard();
        if (_form.WindowState == FormWindowState.Minimized)
            _form.WindowState = FormWindowState.Normal;
        _form.Activate();
        _form.BringToFront();
    }

    private void ShowDuplicates()
    {
        if (_dupeForm is null || _dupeForm.IsDisposed)
            _dupeForm = new DuplicateForm(_settings);

        _dupeForm.Show();
        if (_dupeForm.WindowState == FormWindowState.Minimized)
            _dupeForm.WindowState = FormWindowState.Normal;
        _dupeForm.Activate();
        _dupeForm.BringToFront();
    }

    private void ToggleStartup(ToolStripMenuItem item)
    {
        try
        {
            StartupManager.SetStartup(item.Checked);
        }
        catch (Exception ex)
        {
            item.Checked = !item.Checked; // revert on failure
            MessageBox.Show(ex.Message, "Start with Windows",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CreateShortcut()
    {
        try
        {
            StartupManager.CreateDesktopShortcut();
            _trayIcon.BalloonTipTitle = "Video to MP3";
            _trayIcon.BalloonTipText = "Desktop shortcut created.";
            _trayIcon.ShowBalloonTip(3000);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Create desktop shortcut",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowLogin()
    {
        using var login = new LoginForm(_settings);
        login.ShowDialog();
    }

    private void OpenOutputFolder()
    {
        try
        {
            Directory.CreateDirectory(_settings.OutputFolder);
            Process.Start(new ProcessStartInfo { FileName = _settings.OutputFolder, UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private void ExitApp()
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _form?.Dispose();
        _dupeForm?.Dispose();
        ExitThread();
    }

    /// <summary>Uses the app's own icon for the tray; falls back to a drawn one.</summary>
    private static Icon BuildIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var appIcon = Icon.ExtractAssociatedIcon(exe);
                if (appIcon is not null)
                    return appIcon;
            }
        }
        catch { /* fall back to the drawn icon below */ }

        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(220, 38, 38)); // red disc
            g.FillEllipse(bg, 1, 1, 30, 30);
            using var font = new Font("Segoe UI Symbol", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var fg = new SolidBrush(Color.White);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("♫", font, fg, new RectangleF(0, 0, 32, 32), sf);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
