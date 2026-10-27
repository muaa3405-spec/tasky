using System.Diagnostics;
using System.IO;
using Tasky.Services;

namespace Tasky.Helpers;

/// <summary>Opens files in their default app (Windows or macOS) without ever crashing Tasky.</summary>
public static class FileLauncher
{
    private static string OsName => OperatingSystem.IsMacOS() ? "macOS" : "Windows";

    /// <summary>Shows a folder in Explorer or Finder, creating it first if needed.</summary>
    public static void OpenFolder(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Could not open folder '{folder}'", ex);
            InAppNotificationService.Warning($"Couldn't open the folder. It is at {folder}");
        }
    }

    /// <summary>Opens a web page in the default browser.</summary>
    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write($"Could not open '{url}'", ex);
            InAppNotificationService.Warning($"Couldn't open your browser. The address is {url}");
        }
    }

    public static void Open(string path)
    {
        if (!File.Exists(path))
        {
            InAppNotificationService.Warning("That file no longer exists.");
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Typically "no application is associated" or the file is blocked.
            CrashLog.Write("Could not open attachment", ex);
            InAppNotificationService.Warning(
                $"{OsName} has no app set up to open \"{Path.GetFileName(path)}\".");
        }
    }
}
