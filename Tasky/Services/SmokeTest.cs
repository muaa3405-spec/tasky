using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace Tasky.Services;

/// <summary>
/// Hooks for the CI smoke test only. With <see cref="ShowReminderVariable"/> set (and a sandboxed
/// <see cref="AppPaths.DataRootOverrideVariable"/>), a sample reminder is shown at start-up and its
/// position is written to the log, so the macOS screenshot shows where reminders appear.
/// </summary>
public static class SmokeTest
{
    public const string ShowReminderVariable = "TASKY_SMOKE_REMINDER";

    private static readonly TimeSpan LogDelay = TimeSpan.FromSeconds(3);

    private static bool IsRequested(string variable)
        => Environment.GetEnvironmentVariable(variable) is { Length: > 0 }
        && Environment.GetEnvironmentVariable(AppPaths.DataRootOverrideVariable) is { Length: > 0 };

    public static void ShowReminderIfRequested()
    {
        if (!IsRequested(ShowReminderVariable)) return;
        App.Notifications?.SendReminder("Smoke test reminder");

        var timer = new DispatcherTimer { Interval = LogDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            LogReminderPosition();
        };
        timer.Start();
    }

    private static void LogReminderPosition()
    {
        if (App.Instance.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;
        foreach (var toast in desktop.Windows.OfType<Dialogs.ReminderToastWindow>())
        {
            var screen = toast.Screens.ScreenFromWindow(toast) ?? toast.Screens.Primary;
            CrashLog.Write("Smoke test: reminder position",
                $"position={toast.Position} size={toast.Bounds.Size} desktopScaling={toast.DesktopScaling} " +
                $"renderScaling={toast.RenderScaling} screenScaling={screen?.Scaling} workingArea={screen?.WorkingArea}");
        }
    }
}
