using Microsoft.Win32;

namespace VideoToMp3;

/// <summary>
/// Finds yt-dlp.exe, ffmpeg.exe, and fpcalc.exe. Looks in the app's own folder,
/// the process PATH, the registry PATH (catches a just-installed entry the running
/// process hasn't picked up), both winget Links folders (user + machine scope),
/// and finally searches the winget Packages trees directly.
/// </summary>
public static class ToolLocator
{
    public static string? YtDlpPath => Resolve("yt-dlp.exe");
    public static string? FfmpegPath => Resolve("ffmpeg.exe");
    public static string? FpcalcPath => Resolve("fpcalc.exe");

    private static readonly Dictionary<string, string?> _cache = new();

    private static string? Resolve(string exe)
    {
        if (_cache.TryGetValue(exe, out var cached))
            return cached;

        // Cheap lookups first; the recursive Packages scan is the last resort.
        string? found = FromAppFolder(exe)
            ?? FromDirs(ProcessPathDirs(), exe)
            ?? FromDirs(LinksDirs(), exe)
            ?? FromDirs(RegistryPathDirs(), exe)
            ?? FromPackageTrees(exe);

        _cache[exe] = found;
        return found;
    }

    /// <summary>Forget cached results so a fresh install is picked up.</summary>
    public static void ResetCache() => _cache.Clear();

    private static string? FromAppFolder(string exe)
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(dir)) return null;
        var candidate = Path.Combine(dir, exe);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FromDirs(IEnumerable<string> dirs, string exe)
    {
        foreach (var dir in dirs)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate)) return candidate;
            }
            catch { /* ignore malformed entries */ }
        }
        return null;
    }

    private static IEnumerable<string> ProcessPathDirs() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    private static IEnumerable<string> LinksDirs()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            yield return Path.Combine(pf, "WinGet", "Links");
    }

    /// <summary>PATH as stored in the registry — picks up entries winget just added.</summary>
    private static IEnumerable<string> RegistryPathDirs()
    {
        (RegistryKey root, string sub)[] sources =
        {
            (Registry.LocalMachine, @"System\CurrentControlSet\Control\Session Manager\Environment"),
            (Registry.CurrentUser, "Environment"),
        };
        foreach (var (root, sub) in sources)
        {
            string? raw = null;
            try
            {
                using var key = root.OpenSubKey(sub);
                raw = key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            }
            catch { /* ignore */ }
            if (string.IsNullOrEmpty(raw)) continue;

            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(raw); }
            catch { continue; }
            foreach (var dir in expanded.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                yield return dir;
        }
    }

    /// <summary>Last resort: the actual binaries live under WinGet\Packages\&lt;id&gt;\...</summary>
    private static string? FromPackageTrees(string exe)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string?[] roots =
        {
            string.IsNullOrEmpty(local) ? null : Path.Combine(local, "Microsoft", "WinGet", "Packages"),
            string.IsNullOrEmpty(pf) ? null : Path.Combine(pf, "WinGet", "Packages"),
        };
        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
            try
            {
                var hit = Directory.EnumerateFiles(root, exe, SearchOption.AllDirectories).FirstOrDefault();
                if (hit is not null) return hit;
            }
            catch { /* ignore access errors */ }
        }
        return null;
    }

    /// <summary>True if both required tools were found.</summary>
    public static bool ToolsAvailable => YtDlpPath is not null && FfmpegPath is not null;
}
