using System.IO;
using Microsoft.Toolkit.Uwp.Notifications;

namespace Tasky.Services;

/// <summary>
/// Wraps Windows Toast Notifications via the Community Toolkit.
/// The toolkit handles AUMID registration for unpackaged Win32 apps automatically.
/// </summary>
public class NotificationService
{
    private readonly DatabaseService _db;

    public NotificationService(DatabaseService db) => _db = db;

    public void Initialize()
    {
        // Registers the COM activator and AUMID for unpackaged apps.
        // Must be called once before sending any toast.
        ToastNotificationManagerCompat.OnActivated += OnToastActivated;
    }

    public void SendReminder(string taskTitle, int taskId = 0, string? body = null)
    {
        var displayTitle = taskId > 0 ? $"#{taskId:D3}  {taskTitle}" : taskTitle;

        // Show our own themed blue toast window — same approach as Teams/Slack
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var toast = new Tasky.Dialogs.ReminderToastWindow(displayTitle, taskTitle, taskId);
                toast.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Tasky] Custom toast failed: {ex.Message}");
                // Fallback: tray balloon
                if (System.Windows.Application.Current.MainWindow is MainWindow mw)
                    mw.ShowBalloonReminder(displayTitle, body ?? "Time to work on this task.");
            }
        });
    }

    /// <summary>
    /// Called by the snooze / "remind in" buttons. Stored as a one-time reminder,
    /// so it still fires after Tasky is closed and reopened (the scheduler checks every minute).
    /// </summary>
    /// <param name="taskTitle">The plain task title, without the "#012" prefix.</param>
    public void ScheduleSnooze(int taskId, string taskTitle, DateTime fireAt)
    {
        if (fireAt <= DateTime.Now) fireAt = DateTime.Now.AddMinutes(1);
        _db.InsertReminder(new Models.ReminderItem
        {
            TaskId       = taskId,
            TaskTitle    = taskTitle,
            NextFireTime = fireAt,
            Frequency    = Models.ReminderFrequency.Once,
            IsEnabled    = true
        });
    }

    private void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var arg = e.Argument ?? string.Empty;
        if (arg.Contains("action=dismiss")) return;

        // Parse optional taskId from "action=open&taskId=42"
        int taskId = 0;
        foreach (var part in arg.Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0] == "taskId")
                int.TryParse(kv[1], out taskId);
        }

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (System.Windows.Application.Current.MainWindow is not MainWindow mw) return;

            if (taskId > 0)
            {
                // Find the task VM and open its detail dialog
                var vm = App.ViewModel?.Tasks.FirstOrDefault(t => t.Id == taskId)
                      ?? App.ViewModel?.CompletedTasks.FirstOrDefault(t => t.Id == taskId);
                if (vm is not null) { mw.OpenTaskDetail(vm); return; }
            }

            mw.ShowFromTray();
            mw.Activate();
        });
    }

    public void Cleanup()
    {
        // Unregister COM activator on exit
        ToastNotificationManagerCompat.Uninstall();
    }
}
