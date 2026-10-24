using Avalonia.Threading;

namespace Tasky.Services;

/// <summary>
/// Shows reminder notifications as Tasky's own themed toast window
/// (bottom-right of the screen), with a tray balloon as fallback.
/// </summary>
public class NotificationService
{
    private readonly DatabaseService _db;

    public NotificationService(DatabaseService db) => _db = db;

    public void SendReminder(string taskTitle, int taskId = 0, string? body = null)
    {
        var displayTitle = taskId > 0 ? $"#{taskId:D3}  {taskTitle}" : taskTitle;

        // Show our own themed blue toast window — same approach as Teams/Slack
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var toast = new Dialogs.ReminderToastWindow(displayTitle, taskTitle, taskId);
                toast.Show();
            }
            catch (Exception ex)
            {
                CrashLog.Write("Custom reminder toast failed", ex);
                // Fallback: tray balloon
                App.MainAppWindow?.ShowBalloonReminder(displayTitle, body ?? "Time to work on this task.");
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
}
