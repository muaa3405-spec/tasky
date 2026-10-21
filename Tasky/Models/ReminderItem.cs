namespace Tasky.Models;

public enum ReminderFrequency
{
    Once = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public class ReminderItem
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    /// <summary>The time-of-day (HH:mm) combined with next fire date.</summary>
    public DateTime NextFireTime { get; set; }
    public ReminderFrequency Frequency { get; set; }
    /// <summary>Null means no end date (fires indefinitely for repeating).</summary>
    public DateTime? EndDate { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>Denormalized task title for notification display.</summary>
    public string TaskTitle { get; set; } = string.Empty;
    /// <summary>For Weekly: 0=Sun,1=Mon…6=Sat. Null = use NextFireTime's weekday.</summary>
    public int? TargetDayOfWeek { get; set; }
    /// <summary>For Monthly: 1-31. Null = use NextFireTime's day.</summary>
    public int? DayOfMonth { get; set; }
}
