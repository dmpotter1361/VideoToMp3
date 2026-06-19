# Video to MP3 — notes for Claude Code

Windows system-tray app: converts video → MP3 (keeps both), supports playlists,
fingerprint-based dedupe, optional login, and one-click external-tool setup.
C# / .NET 10 WinForms, Windows-only. Repo `dmpotter1361/VideoToMp3` (MSI v1.1.0).

## Run / build

```powershell
dotnet run                 # launch the tray app
dotnet build -c Release    # release build
./build.ps1                # packaging / release (MSI) script
```

No tests. Plain `dotnet` is enough.

## Layout

- **`TrayAppContext.cs`** — tray `ApplicationContext` + lifecycle.
- **`Converter.cs` / `ConverterForm.cs`** — conversion engine + UI.
- **`ToolLocator.cs`** — finds/sets up external tools (e.g. ffmpeg / yt-dlp);
  **`BootstrapForm.cs`** — first-run one-click tool setup.
- **`DuplicateFinder.cs` / `DuplicateForm.cs`** — audio-fingerprint dedupe.
- **`LoginForm.cs`** — optional login; **`StartupManager.cs`** — run-at-startup.
- **`AppSettings.cs` / `AppResources.cs`** — settings + embedded resources.

## Conventions

- Forms hand-written in code; **UI must survive font/display-scaling changes**
  (TableLayoutPanel/FlowLayoutPanel + AutoSize + font fallback).
- Wife is the primary user/tester — keep changes conservative; expect bug-fix turns.
