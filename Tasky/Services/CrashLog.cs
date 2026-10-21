using System.IO;

namespace Tasky.Services;

/// <summary>
/// Appends errors to a daily log under %LocalAppData%\Tasky\logs.
/// Writing the log can never throw — a failing logger must not take the app down.
/// </summary>
public static class CrashLog
{
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tasky", "logs");

    public static string CurrentFile => Path.Combine(Folder, $"tasky-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string context, object? error)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(CurrentFile,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{error}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Nowhere left to report to — swallow so the caller keeps running.
        }
    }
}
