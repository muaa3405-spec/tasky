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
}

public class SettingsService
{
    private static string _path => AppPaths.SettingsFile;

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
