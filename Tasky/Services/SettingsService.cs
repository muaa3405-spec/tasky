using System.IO;
using System.Text.Json;

namespace Tasky.Services;

public class AppSettings
{
    public string  UserName              { get; set; } = string.Empty;
    public string  Theme                 { get; set; } = "dark";
    public string  NotifTune             { get; set; } = "proximity";
    public string  NotifAccentColor      { get; set; } = "#60A5FA";
    public int     NotifAutoDismissSecs  { get; set; } = 30;
    public bool    ShowCompletionPopup      { get; set; } = true;
    public bool    SaveScreenshotToDesktop { get; set; } = true;
    public string  MsClientId             { get; set; } = string.Empty;
}

/// <summary>Plays a notification sound by tag using Windows Media WAV files.</summary>
public static class SoundPlayer
{
    private static readonly string MediaDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");

    private static readonly Dictionary<string, string> WavFiles = new()
    {
        ["notify"]    = "Windows Notify.wav",
        ["nudge"]     = "Windows Message Nudge.wav",
        ["calendar"]  = "Windows Notify Calendar.wav",
        ["email"]     = "Windows Notify Email.wav",
        ["proximity"] = "Windows Proximity Notification.wav",
        ["logon"]     = "Windows Logon.wav",
    };

    public static void Play(string tag)
    {
        if (tag == "none") return;
        try
        {
            if (WavFiles.TryGetValue(tag, out var file))
            {
                var fullPath = Path.Combine(MediaDir, file);
                if (File.Exists(fullPath))
                {
                    using var player = new System.Media.SoundPlayer(fullPath);
                    player.Play();
                    return;
                }
            }
            // Fallback to system exclamation if WAV not found
            System.Media.SystemSounds.Exclamation.Play();
        }
        catch { }
    }
}

public class SettingsService
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tasky", "settings.json");

    private AppSettings _settings = new();
    public AppSettings Current => _settings;

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
                _settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path))
                            ?? new AppSettings();
        }
        catch { _settings = new AppSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path,
                JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
