using System.Diagnostics;

namespace VideoToMp3;

/// <summary>
/// Finds yt-dlp.exe and ffmpeg.exe. Prefers PATH, then the WinGet Links
/// folder where both land when installed via winget.
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

        string? found = FromAppFolder(exe) ?? FromPath(exe) ?? FromKnownLocations(exe);
        _cache[exe] = found;
        return found;
    }

    /// <summary>Forget cached results so a fresh install is picked up.</summary>
    public static void ResetCache() => _cache.Clear();

    private static string? FromAppFolder(string exe)
    {
        // The folder the running .exe lives in (works for single-file too).
        var dir = Path.GetDirectoryName(Environment.ProcessPath);
        if (string.IsNullOrEmpty(dir))
            return null;
        var candidate = Path.Combine(dir, exe);
        return File.Exists(candidate) ? candidate : null;
    }

    private static string? FromPath(string exe)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), exe);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private static string? FromKnownLocations(string exe)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(localAppData, "Microsoft", "WinGet", "Links", exe),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>True if both required tools were found.</summary>
    public static bool ToolsAvailable => YtDlpPath is not null && FfmpegPath is not null;
}
