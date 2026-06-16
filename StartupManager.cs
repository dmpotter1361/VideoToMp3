using Microsoft.Win32;

namespace VideoToMp3;

/// <summary>
/// Handles the optional "start with Windows" toggle (per-user Run key) and
/// creating a desktop shortcut. Both are opt-in; nothing happens unless asked.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VideoToMp3";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch
        {
            return false;
        }
    }

    public static void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null) return;
        if (enabled)
            key.SetValue(ValueName, $"\"{ExePath}\"");
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Creates (or refreshes) a desktop shortcut to the app. Returns its path.</summary>
    public static string CreateDesktopShortcut()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var linkPath = Path.Combine(desktop, "Video to MP3.lnk");

        // Use the Windows Script Host COM object so we don't need an extra library.
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = ExePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(ExePath);
            shortcut.Description = "Download video and convert to MP3";
            shortcut.IconLocation = ExePath + ",0";
            shortcut.Save();
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
        return linkPath;
    }
}
