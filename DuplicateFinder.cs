using System.Diagnostics;
using System.Numerics;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace VideoToMp3;

/// <summary>One set of files that are the same recording. Keeper is kept; Extras are removable.</summary>
public sealed record DuplicateGroup(FileInfo Keeper, IReadOnlyList<FileInfo> Extras);

/// <summary>
/// Finds duplicate recordings by audio fingerprint (Chromaprint/fpcalc), so the
/// same song is detected across different titles, bitrates, or formats. Audio
/// files and video files are compared only within their own kind.
/// </summary>
public sealed class DuplicateFinder
{
    private readonly Action<string> _log;

    // How close two fingerprints must be (fraction of matching bits) to count
    // as the same recording. Conservative to avoid false positives.
    private const double MatchThreshold = 0.90;
    private const int MinOverlapFrames = 50;
    private const int MaxOffset = 20;

    private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma" };
    private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v", ".flv" };

    private sealed record Print(FileInfo File, int Duration, uint[] Fingerprint);

    public DuplicateFinder(Action<string> log) => _log = log;

    /// <summary>
    /// Sends every extra copy to the Recycle Bin (recoverable), keeping each
    /// group's best file. Returns how many were removed. Shared by the manual
    /// scanner and the optional auto-cleanup after a download.
    /// </summary>
    public static int SendExtrasToRecycleBin(IEnumerable<DuplicateGroup> groups, Action<string> log)
    {
        int removed = 0;
        foreach (var extra in groups.SelectMany(g => g.Extras))
        {
            try
            {
                FileSystem.DeleteFile(extra.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                log($"Removed duplicate: {extra.Name}\n");
                removed++;
            }
            catch (Exception ex)
            {
                log($"Couldn't remove {extra.Name}: {ex.Message}\n");
            }
        }
        return removed;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> ScanAsync(string folder, CancellationToken ct)
    {
        if (ToolLocator.FpcalcPath is not string fpcalc)
            throw new InvalidOperationException("fpcalc.exe (Chromaprint) was not found.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Folder not found: {folder}");

        var files = Directory.EnumerateFiles(folder, "*", System.IO.SearchOption.AllDirectories)
            .Select(p => new FileInfo(p))
            .Where(f => AudioExt.Contains(f.Extension) || VideoExt.Contains(f.Extension))
            .ToList();

        _log($"Scanning {files.Count} media file(s) under:\n{folder}\n\n");

        var groups = new List<DuplicateGroup>();
        // Audio and video are fingerprinted the same way (fpcalc reads the audio
        // track of videos too) but compared in separate buckets.
        groups.AddRange(await FindInBucketAsync(
            files.Where(f => AudioExt.Contains(f.Extension)), fpcalc, "audio", ct));
        groups.AddRange(await FindInBucketAsync(
            files.Where(f => VideoExt.Contains(f.Extension)), fpcalc, "video", ct));

        return groups;
    }

    private async Task<IReadOnlyList<DuplicateGroup>> FindInBucketAsync(
        IEnumerable<FileInfo> files, string fpcalc, string label, CancellationToken ct)
    {
        var prints = new List<Print>();
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var print = await FingerprintAsync(fpcalc, file, ct);
            if (print is not null)
                prints.Add(print);
            else
                _log($"  (couldn't fingerprint {file.Name} — skipped)\n");
        }

        if (prints.Count < 2)
            return Array.Empty<DuplicateGroup>();

        _log($"Comparing {prints.Count} {label} file(s)…\n");

        // Union-find so chains of matches collapse into one group.
        var parent = Enumerable.Range(0, prints.Count).ToArray();
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        void Union(int a, int b) { parent[Find(a)] = Find(b); }

        for (int i = 0; i < prints.Count; i++)
        {
            for (int j = i + 1; j < prints.Count; j++)
            {
                ct.ThrowIfCancellationRequested();
                if (Math.Abs(prints[i].Duration - prints[j].Duration) > 7)
                    continue; // can't be the same recording if lengths differ much
                if (Similarity(prints[i].Fingerprint, prints[j].Fingerprint) >= MatchThreshold)
                    Union(i, j);
            }
        }

        return prints
            .Select((p, idx) => (p, root: Find(idx)))
            .GroupBy(x => x.root)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                // Keep the largest file (best quality proxy); the rest are extras.
                var ordered = g.Select(x => x.p.File).OrderByDescending(f => f.Length).ToList();
                return new DuplicateGroup(ordered[0], ordered.Skip(1).ToList());
            })
            .ToList();
    }

    private async Task<Print?> FingerprintAsync(string fpcalc, FileInfo file, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fpcalc,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-raw");
        psi.ArgumentList.Add("-length");
        psi.ArgumentList.Add("120");
        psi.ArgumentList.Add(file.FullName);

        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            return null;

        int duration = 0;
        uint[]? fingerprint = null;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("DURATION=", StringComparison.Ordinal))
                int.TryParse(trimmed.AsSpan("DURATION=".Length), out duration);
            else if (trimmed.StartsWith("FINGERPRINT=", StringComparison.Ordinal))
            {
                fingerprint = trimmed["FINGERPRINT=".Length..]
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => uint.TryParse(s, out var v) ? v : 0u)
                    .ToArray();
            }
        }

        return fingerprint is { Length: > 0 } ? new Print(file, duration, fingerprint) : null;
    }

    /// <summary>Best fraction of matching bits between two fingerprints, sliding over small offsets.</summary>
    private static double Similarity(uint[] a, uint[] b)
    {
        double best = 0;
        for (int offset = -MaxOffset; offset <= MaxOffset; offset++)
        {
            int start = Math.Max(0, offset);
            int end = Math.Min(a.Length, b.Length + offset);
            int overlap = end - start;
            if (overlap < MinOverlapFrames)
                continue;

            int matchingBits = 0;
            for (int i = start; i < end; i++)
                matchingBits += 32 - BitOperations.PopCount(a[i] ^ b[i - offset]);

            double frac = (double)matchingBits / (overlap * 32);
            if (frac > best)
                best = frac;
        }
        return best;
    }
}
