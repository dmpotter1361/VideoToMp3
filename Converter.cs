using System.Diagnostics;
using System.Text;

namespace VideoToMp3;

public sealed record ConversionResult(string VideoPath, string Mp3Path);

/// <summary>
/// Downloads the best video(s) with yt-dlp, then produces an MP3 with ffmpeg
/// for each. Videos and MP3s are kept in separate subfolders, and a download
/// archive prevents re-downloading content that appears in multiple playlists.
/// </summary>
public sealed class Converter
{
    private readonly Action<string> _log;

    public Converter(Action<string> log) => _log = log;

    public async Task<IReadOnlyList<ConversionResult>> RunAsync(
        string url, string outputFolder, int bitrate, bool playlist,
        string? cookiesFile, CancellationToken ct)
    {
        if (ToolLocator.YtDlpPath is not string ytdlp)
            throw new InvalidOperationException("yt-dlp.exe was not found.");
        if (ToolLocator.FfmpegPath is not string ffmpeg)
            throw new InvalidOperationException("ffmpeg.exe was not found.");

        // Keep videos and MP3s apart so the music folder stays clean.
        var videoDir = Path.Combine(outputFolder, "Videos");
        var mp3Dir = Path.Combine(outputFolder, "MP3s");
        Directory.CreateDirectory(videoDir);
        Directory.CreateDirectory(mp3Dir);

        // yt-dlp writes the final, post-merge path of every NEW item here
        // (one per line); items already in the archive don't appear.
        var pathFile = Path.Combine(Path.GetTempPath(), $"v2mp3_{Guid.NewGuid():N}.txt");

        try
        {
            _log(playlist
                ? "Reading playlist and downloading every video…\n\n"
                : "Fetching video info and downloading…\n\n");

            var outputTemplate = Path.Combine(videoDir, "%(title)s.%(ext)s");

            var ytArgs = new List<string>
            {
                playlist ? "--yes-playlist" : "--no-playlist",
                "--windows-filenames",
                "--no-mtime",
                "--newline",
                "--download-archive", DownloadArchivePath, // skip anything already grabbed
                "-f", "bv*+ba/b",
                "--merge-output-format", "mp4",
                "--print-to-file", "after_move:filepath", pathFile,
                "-o", outputTemplate,
            };
            if (playlist)
                ytArgs.Add("--ignore-errors"); // one bad video shouldn't sink the batch
            if (!string.IsNullOrWhiteSpace(cookiesFile) && File.Exists(cookiesFile))
            {
                ytArgs.Add("--cookies");
                ytArgs.Add(cookiesFile);
            }
            ytArgs.Add(url);

            var ytExit = await RunProcessAsync(ytdlp, ytArgs, ct);

            var videoPaths = File.Exists(pathFile)
                ? File.ReadAllLines(pathFile, Encoding.UTF8)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && File.Exists(l))
                    .Distinct()
                    .ToList()
                : new List<string>();

            if (videoPaths.Count == 0)
            {
                // Nothing new is normal when everything's already in the archive.
                if (ytExit == 0)
                {
                    _log("\nNothing new to download — already in your library.\n");
                    return Array.Empty<ConversionResult>();
                }
                throw new InvalidOperationException(
                    $"Download failed (yt-dlp exit code {ytExit}). Check the URL and your connection.");
            }

            var results = new List<ConversionResult>(videoPaths.Count);
            for (int i = 0; i < videoPaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var videoPath = videoPaths[i];

                _log(videoPaths.Count > 1
                    ? $"\nConverting {i + 1} of {videoPaths.Count} to MP3 ({bitrate} kbps): {Path.GetFileName(videoPath)}\n"
                    : $"\nConverting to MP3 ({bitrate} kbps)…\n");

                var mp3Path = Path.Combine(mp3Dir,
                    Path.GetFileNameWithoutExtension(videoPath) + ".mp3");
                var ffArgs = new[]
                {
                    "-y", "-i", videoPath, "-vn", "-c:a", "libmp3lame", "-b:a", $"{bitrate}k", mp3Path,
                };

                var ffExit = await RunProcessAsync(ffmpeg, ffArgs, ct);
                if (ffExit == 0 && File.Exists(mp3Path))
                    results.Add(new ConversionResult(videoPath, mp3Path));
                else
                    _log($"  (skipped — MP3 conversion failed for this one)\n");
            }

            _log($"\nDone. {results.Count} new file(s) ready.\n");
            return results;
        }
        finally
        {
            try { if (File.Exists(pathFile)) File.Delete(pathFile); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// Records which videos have already been downloaded (by their unique ID)
    /// so re-running overlapping playlists doesn't make duplicates. Kept in
    /// %APPDATA% so it survives even if the output folder is moved.
    /// </summary>
    private static string DownloadArchivePath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VideoToMp3");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "downloaded.txt");
        }
    }

    private async Task<int> RunProcessAsync(string exe, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) _log(e.Data + "\n"); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) _log(e.Data + "\n"); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using (ct.Register(() => { try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { } }))
        {
            await proc.WaitForExitAsync(ct);
        }

        return proc.ExitCode;
    }
}
