# Video to MP3

<p align="center">
  <img src="appicon-preview.png" width="96" alt="Video to MP3 icon"><br>
  <a href="https://github.com/dmpotter1361/VideoToMp3/releases/latest"><img src="https://img.shields.io/github/v/release/dmpotter1361/VideoToMp3?label=download&sort=semver&cacheSeconds=300" alt="Latest release"></a>
</p>

A small Windows tray app that downloads a video from YouTube (or ~1,000 other
sites) and turns it into an MP3 — keeping both the audio and the original video.
Paste a link, click **Convert**, done. It also grabs whole playlists and can find
and clean up duplicate songs by *listening* to them, not just comparing filenames.

> Personal project. Not affiliated with, endorsed by, or sponsored by YouTube or
> any other site. Downloading content may be against a site's Terms of Service —
> use it for your own personal, offline listening.

### ⬇ Download

**[Get the latest version](https://github.com/dmpotter1361/VideoToMp3/releases/latest)** —
download `VideoToMp3-x.y.z-x64.msi` from the latest release and run it. It installs
like any normal program (shows in **Add/Remove Programs**, and a newer version
replaces the old one automatically). The first time you open it, it sets up its
free helper tools for you — **no command line, nothing manual**. (It's a personal
build and not code-signed, so Windows SmartScreen may warn — choose
**More info → Run anyway**.)

## Features

- **Tray app** — runs quietly next to the clock; double-click to open the converter.
- **Paste & convert** — auto-fills the link from your clipboard; one click makes the MP3.
- **MP3, video, or both** — choose what to keep; files are sorted into tidy
  `MP3s\` and `Videos\` subfolders so your music stays clean.
- **Quality choice** — 128 / 192 / 320 kbps (defaults to 192).
- **Playlists** — optionally download every video in a playlist, numbered and in order;
  a bad item is skipped instead of failing the batch.
- **No duplicate downloads** — remembers what it has already grabbed (by the video's
  unique ID), so the same song appearing in several playlists is only downloaded once.
- **Duplicate finder** — scans your library by **audio fingerprint**, so the *same song*
  is caught even under a different name, bitrate, or format. Keeps the best copy and
  sends the rest to the Recycle Bin (recoverable). Run it on demand, or tick
  "clean up when done" to dedupe automatically after a big download.
- **Optional login** — point it at an exported `cookies.txt` to download your own
  private playlists, Watch Later, or Liked videos; a clear indicator shows which
  account is in use. Public videos need no login.
- **Easy launching** — optional "Start with Windows" toggle and a one-click
  "Create desktop shortcut", both in the tray menu.

## Requirements

- **Windows 10/11 (x64).** The .NET runtime is bundled in the installer.

The app relies on three free helper tools — **[yt-dlp](https://github.com/yt-dlp/yt-dlp)**
and **[ffmpeg](https://ffmpeg.org/)** (download + convert) and
**[Chromaprint](https://acoustid.org/chromaprint)** (duplicate detection). You don't
need to install these yourself: the first time the app runs without them, it offers a
one-click setup that installs them via Windows' built-in `winget`. If you'd rather do
it manually, the equivalent commands are:

```powershell
winget install yt-dlp.yt-dlp
winget install AcoustID.Chromaprint
```

## Privacy

Everything stays on your PC. There's no account, no telemetry, and nothing is sent
anywhere except to the sites you download from. Optional login uses a `cookies.txt`
**you** export and select; it's read locally and never leaves the machine. App
settings and the download history live in `%APPDATA%\VideoToMp3\`.

## Build from source

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/) and
WiX v5 (`dotnet tool install --global wix --version 5.0.2`).

```powershell
# Run the app
dotnet run --project VideoToMp3.csproj

# Build the MSI installer (publishes self-contained, then builds the MSI)
pwsh ./build.ps1 -Version 1.1.0
```

## How it works

There's no public API for this, so the app drives proven command-line tools:
**yt-dlp** fetches the best video (and handles playlists, the download archive, and
optional login cookies), then **ffmpeg** extracts the MP3. The duplicate finder uses
**Chromaprint/fpcalc** to compute an audio fingerprint of each file and compares them
with a sliding bit-match (≈90% threshold, durations within a few seconds), comparing
audio-to-audio and video-to-video separately so an MP3 is never mistaken for its own
source video.

## Continuing development with Claude Code

This project was built with AI assistance and is set up so you can keep going the
same way. To pick up where it left off on your own machine:

1. **Get the code onto your PC**

   ```bash
   git clone https://github.com/dmpotter1361/VideoToMp3.git
   cd VideoToMp3
   ```

2. **Install [Claude Code](https://claude.com/claude-code)** (Anthropic's coding CLI)
   and start it in the project folder:

   ```bash
   npm install -g @anthropic-ai/claude-code
   claude
   ```

   (You can also use the Claude Code extension for VS Code / JetBrains, or
   [claude.ai/code](https://claude.ai/code).)

3. **Point Claude at the project and ask for what you want.** A good first prompt:

   > Read the README and `Converter.cs` / `DuplicateFinder.cs`, then build and run the
   > app so you understand it. I'd like to add &lt;your feature&gt;.

### Helpful map for a new contributor (human or AI)

- **`Converter.cs`** — downloads with yt-dlp and converts with ffmpeg; playlists,
  the download archive (dedupe by ID), and optional login cookies live here.
- **`DuplicateFinder.cs`** — audio-fingerprint duplicate detection and Recycle-Bin cleanup.
- **`ToolLocator.cs`** — finds `yt-dlp.exe`, `ffmpeg.exe`, and `fpcalc.exe`.
- **`BootstrapForm.cs`** — first-run one-click setup of the helper tools via winget.
- **`StartupManager.cs`** — "start with Windows" toggle and desktop-shortcut creation.
- **`installer/Package.wxs` + `build.ps1`** — the WiX MSI installer.
- **`TrayAppContext.cs`** — the tray icon and menu; opens the windows.
- **`ConverterForm.cs` / `DuplicateForm.cs` / `LoginForm.cs`** — the windows.
- **`AppSettings.cs`** — JSON settings in `%APPDATA%\VideoToMp3\`.
- **`AppResources.cs`** — shared app icon for the windows and tray.

## Acknowledgments

Video to MP3 was designed and built collaboratively with **Claude** (Anthropic's AI),
pair-programming with the author from the first idea through the downloader, the
playlist and duplicate-detection logic, the optional login, and this README. The
direction, decisions, and real-world testing are human; a lot of the implementation
was AI-assisted — and we're happy to say so. 🤖🤝

## License

[MIT](LICENSE)
