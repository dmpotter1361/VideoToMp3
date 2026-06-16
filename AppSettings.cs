using System.Text.Json;
using System.Text.Json.Serialization;

namespace VideoToMp3;

/// <summary>
/// Tiny JSON-backed settings stored in %APPDATA%\VideoToMp3\settings.json.
/// </summary>
public sealed class AppSettings
{
    public string OutputFolder { get; set; } = DefaultOutputFolder;
    public int Mp3Bitrate { get; set; } = 192;
    public bool CleanDuplicatesAfter { get; set; } = false;
    public bool UseLogin { get; set; } = false;
    public string CookiesFilePath { get; set; } = "";
    public string AccountLabel { get; set; } = "";

    /// <summary>True when login is enabled and the cookies file still exists.</summary>
    [JsonIgnore]
    public bool LoginActive =>
        UseLogin && !string.IsNullOrWhiteSpace(CookiesFilePath) && File.Exists(CookiesFilePath);

    private static string DefaultOutputFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Video to MP3");

    private static string SettingsDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "VideoToMp3");

    private static string SettingsPath => Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded is not null)
                    return loaded;
            }
        }
        catch
        {
            // Corrupt/unreadable settings fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Best-effort; not worth interrupting the user over.
        }
    }
}
