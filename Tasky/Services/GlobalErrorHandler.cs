using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Tasky.Services;

/// <summary>
/// Last line of defence against unexpected errors. Errors on the UI thread are
/// logged and shown as a small in-app message, and Tasky keeps running.
/// Only errors the runtime cannot recover from end the process — and even
/// then running timers are saved first.
/// </summary>
public static class GlobalErrorHandler
{
    /// <summary>Repeating errors (e.g. from a timer) show at most one message per window.</summary>
    private static readonly TimeSpan NotifyCooldown = TimeSpan.FromSeconds(10);
    private static DateTime _lastNotified = DateTime.MinValue;

    private static readonly TimeSpan FatalSaveTimeout = TimeSpan.FromSeconds(3);

    /// <param name="saveWork">Called before a fatal error ends the process.</param>
    public static void Register(Application app, Action saveWork)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            CrashLog.Write("Unhandled UI error (recovered)", e.Exception);
            e.Handled = true;
            NotifyUser();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write("Unobserved background task error (recovered)", e.Exception);
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            CrashLog.Write("Fatal error", e.ExceptionObject);
            // Best effort, and never wait forever on a UI thread that may itself be stuck.
            try { app.Dispatcher.Invoke(saveWork, DispatcherPriority.Send, CancellationToken.None, FatalSaveTimeout); }
            catch { /* nothing more we can do */ }
        };
    }

    private static void NotifyUser()
    {
        if (DateTime.Now - _lastNotified < NotifyCooldown) return;
        _lastNotified = DateTime.Now;
        try
        {
            InAppNotificationService.Error($"Something went wrong, but your work is safe. Details: {CrashLog.CurrentFile}");
        }
        catch
        {
            // The notification itself failed — the log entry is enough.
        }
    }
}
