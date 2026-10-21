using System.Diagnostics;
using System.IO;
using Tasky.Services;

namespace Tasky.Helpers;

/// <summary>Opens files in their default Windows app without ever crashing Tasky.</summary>
public static class FileLauncher
{
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
                $"Windows has no app set up to open \"{Path.GetFileName(path)}\".");
        }
    }
}
