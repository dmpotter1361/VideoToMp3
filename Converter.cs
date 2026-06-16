using System.Diagnostics;
using System.Text;

namespace VideoToMp3;

public sealed record ConversionResult(string? VideoPath, string? Mp3Path);

/// <summary>
/// Downloads with yt-dlp and (when MP3 is wanted) converts with ffmpeg. Videos
/// and MP3s are kept in separate subfolders, and a download archive prevents
/// re-downloading content that appears in multiple playlists.
/// </summary>
public sealed class Converter
{
    private readonly Action<string> _log;

    public Converter(Action<string> log) => _log = log;

    public async Task<IReadOnlyList<ConversionResult>> RunAsync(
        string url, string outputFolder, int bitrate, bool playlist,
        DownloadMode mode, string? cookiesFile, CancellationToken ct)
    {
        if (ToolLocator.YtDlpPath is not string ytdlp)
            throw new InvalidOperationException("yt-dlp.exe was not found.");

        var videoDir = Path.Combine(outputFolder, "Videos");
        var mp3Dir = Path.Combine(outputFolder, "MP3s");
        var wantsVideoFile = mode is DownloadMode.VideoOnly or DownloadMode.Both;
        var wantsMp3 = mode is DownloadMode.Mp3Only or DownloadMode.Both;
        if (wantsVideoFile) Directory.CreateDirectory(videoDir);
        if (wantsMp3) Directory.CreateDirectory(mp3Dir);

        // yt-dlp writes the final path of every NEW item here (one per line);
        // items already in the archive don't appear.
        var pathFile = Path.Combine(Path.GetTempPath(), $"v2mp3_{Guid.NewGuid():N}.txt");

        try
        {
            _log(playlist
                ? "Reading playlist and downloading every item…\n\n"
                : "Fetching info and downloading…\n\n");

            // For MP3-only we let yt-dlp extract the audio directly (no kept video);
            // otherwise we download the video and (for Both) convert afterwards.
            var downloadDir = mode == DownloadMode.Mp3Only ? mp3Dir : videoDir;

            var ytArgs = new List<string>
            {
                playlist ? "--yes-playlist" : "--no-playlist",
                "--windows-filenames",
                "--no-mtime",
                "--newline",
                "--download-archive", DownloadArchivePath,
                "--print-to-file", "after_move:filepath", pathFile,
                "-o", Path.Combine(downloadDir, "%(title)s.%(ext)s"),
            };
            if (mode == DownloadMode.Mp3Only)
            {
                ytArgs.AddRange(new[]
                {
                    "-f", "bestaudio/best",
                    "-x", "--audio-format", "mp3", "--audio-quality", $"{bitrate}K",
                });
            }
            else
            {
                ytArgs.AddRange(new[] { "-f", "bv*+ba/b", "--merge-output-format", "mp4" });
            }
            if (playlist)
                ytArgs.Add("--ignore-errors");
            if (!string.IsNullOrWhiteSpace(cookiesFile) && File.Exists(cookiesFile))
            {
                ytArgs.Add("--cookies");
                ytArgs.Add(cookiesFile);
            }
            ytArgs.Add(url);

            var ytExit = await RunProcessAsync(ytdlp, ytArgs, ct);

            var downloadedPaths = File.Exists(pathFile)
                ? File.ReadAllLines(pathFile, Encoding.UTF8)
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0 && File.Exists(l))
                    .Distinct()
                    .ToList()
                : new List<string>();

            if (downloadedPaths.Count == 0)
            {
                if (ytExit == 0)
                {
                    _log("\nNothing new to download — already in your library.\n");
                    return Array.Empty<ConversionResult>();
                }
                throw new InvalidOperationException(
                    $"Download failed (yt-dlp exit code {ytExit}). Check the URL and your connection.");
            }

            // MP3-only and Video-only are done after the download itself.
            if (mode == DownloadMode.Mp3Only)
            {
                _log($"\nDone. {downloadedPaths.Count} MP3(s) ready.\n");
                return downloadedPaths.Select(p => new ConversionResult(null, p)).ToList();
            }
            if (mode == DownloadMode.VideoOnly)
            {
                _log($"\nDone. {downloadedPaths.Count} video(s) ready.\n");
                return downloadedPaths.Select(p => new ConversionResult(p, null)).ToList();
            }

            // Both: convert each downloaded video to an MP3 alongside it.
            if (ToolLocator.FfmpegPath is not string ffmpeg)
                throw new InvalidOperationException("ffmpeg.exe was not found.");

            var results = new List<ConversionResult>(downloadedPaths.Count);
            for (int i = 0; i < downloadedPaths.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var videoPath = downloadedPaths[i];

                _log(downloadedPaths.Count > 1
                    ? $"\nConverting {i + 1} of {downloadedPaths.Count} to MP3 ({bitrate} kbps): {Path.GetFileName(videoPath)}\n"
                    : $"\nConverting to MP3 ({bitrate} kbps)…\n");

                var mp3Path = Path.Combine(mp3Dir, Path.GetFileNameWithoutExtension(videoPath) + ".mp3");
                var ffArgs = new[]
                {
                    "-y", "-i", videoPath, "-vn", "-c:a", "libmp3lame", "-b:a", $"{bitrate}k", mp3Path,
                };

                var ffExit = await RunProcessAsync(ffmpeg, ffArgs, ct);
                results.Add(ffExit == 0 && File.Exists(mp3Path)
                    ? new ConversionResult(videoPath, mp3Path)
                    : new ConversionResult(videoPath, null));
                if (ffExit != 0)
                    _log("  (kept the video, but MP3 conversion failed for this one)\n");
            }

            _log($"\nDone. {results.Count} item(s) ready.\n");
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
