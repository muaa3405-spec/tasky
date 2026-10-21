using Tasky.Models;

namespace Tasky.Services;

/// <summary>
/// Background service that checks due reminders every 1 minute and fires
/// Windows toast notifications. Advances NextFireTime for repeating reminders.
/// </summary>
public class ReminderSchedulerService
{
    private readonly DatabaseService _db;
    private readonly NotificationService _notifications;
    private System.Threading.Timer? _timer;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public ReminderSchedulerService(DatabaseService db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public void Start()
    {
        _timer = new System.Threading.Timer(_ => CheckReminders(),
            null, TimeSpan.Zero, _checkInterval);
    }

    public void Stop() => _timer?.Dispose();

    private void CheckReminders()
    {
        try
        {
            var now = DateTime.Now;
            var reminders = _db.GetAllReminders();

            foreach (var reminder in reminders)
            {
                if (reminder.NextFireTime > now) continue;

                // Fire the notification + record as missed until user acknowledges
                _notifications.SendReminder(reminder.TaskTitle, reminder.TaskId);
                _db.InsertMissedNotification(reminder.TaskId, reminder.TaskTitle);

                // Immediately bump the badge count on the UI thread
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                    App.ViewModel?.RefreshMissedNotifCount());

                // Advance or delete
                if (reminder.Frequency == ReminderFrequency.Once)
                {
                    // One-time reminder — delete it after firing
                    _db.DeleteReminder(reminder.Id);
                }
                else
                {
                    // Skip every occurrence missed while Tasky was closed — fire once, not once per missed day.
                    var next = AdvancePastNow(reminder, now);
                    // Past end date — delete too
                    if (reminder.EndDate.HasValue && next.Date > reminder.EndDate.Value.Date)
                        _db.DeleteReminder(reminder.Id);
                    else
                    {
                        reminder.NextFireTime = next;
                        _db.UpdateReminder(reminder);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log but don't crash the timer thread
            System.Diagnostics.Debug.WriteLine($"[ReminderScheduler] Error: {ex.Message}");
        }
    }

    /// <summary>Next occurrence strictly after <paramref name="now"/>.</summary>
    private static DateTime AdvancePastNow(Models.ReminderItem r, DateTime now)
    {
        var next = r.NextFireTime;
        do
        {
            var advanced = AdvanceNextFireTime(r, next);
            if (advanced <= next) break;   // safety: a non-advancing schedule must not loop forever
            next = advanced;
        }
        while (next <= now);
        return next;
    }

    private static DateTime AdvanceNextFireTime(Models.ReminderItem r, DateTime current)
    {
        var tod     = current.TimeOfDay; // preserve time-of-day

        return r.Frequency switch
        {
            ReminderFrequency.Daily => current.AddDays(1),

            ReminderFrequency.Weekly when r.TargetDayOfWeek.HasValue =>
                NextWeekday((DayOfWeek)r.TargetDayOfWeek.Value, current.AddDays(1)).Date + tod,

            ReminderFrequency.Weekly => current.AddDays(7),

            ReminderFrequency.Monthly when r.DayOfMonth.HasValue =>
                NextMonthDay(r.DayOfMonth.Value, current.AddMonths(1)).Date + tod,

            ReminderFrequency.Monthly => current.AddMonths(1),

            _ => current
        };
    }

    /// <summary>First occurrence of <paramref name="weekday"/> on or after <paramref name="from"/>.</summary>
    private static DateTime NextWeekday(DayOfWeek weekday, DateTime from)
    {
        int diff = ((int)weekday - (int)from.DayOfWeek + 7) % 7;
        return from.AddDays(diff == 0 ? 7 : diff);
    }

    /// <summary>The <paramref name="day"/> of the month that <paramref name="from"/> falls in (clamped to last day).</summary>
    private static DateTime NextMonthDay(int day, DateTime from)
    {
        int clamped = Math.Min(day, DateTime.DaysInMonth(from.Year, from.Month));
        return new DateTime(from.Year, from.Month, clamped);
    }
}
