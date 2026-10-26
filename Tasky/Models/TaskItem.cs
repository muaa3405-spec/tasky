namespace Tasky.Models;

public enum RecurrenceType { None = 0, Daily = 1, Weekly = 2, Monthly = 3 }

public class TaskItem
{
    public int Id { get; set; }
    /// <summary>Human-visible sequential task number (#001, #002…).</summary>
    public int TaskNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public bool IsActive { get; set; }
    public bool IsPaused { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>UTC when the current active session started (null when paused/inactive).</summary>
    public DateTime? ActiveStartTime { get; set; }
    /// <summary>Total seconds accumulated across all sessions (excludes current running session).</summary>
    public int AccumulatedSeconds { get; set; }
    /// <summary>Manually entered time in minutes when no timer ran at completion.</summary>
    public int? ManualTimeMinutes { get; set; }
    /// <summary>The date this task is due / scheduled for. Defaults to today.</summary>
    public DateTime? DueDate { get; set; } = DateTime.Today;
    /// <summary>Timestamp when the task was marked as completed.</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>Optional group/project this task belongs to.</summary>
    public int? GroupId { get; set; }
    /// <summary>How often this task recurs. On completion a new instance is created automatically.</summary>
    public RecurrenceType Recurrence { get; set; } = RecurrenceType.None;
    /// <summary>Optional last day a repeat may fall on. Null = repeats forever.</summary>
    public DateTime? RecurrenceEndDate { get; set; }
    /// <summary>When the task was moved to the recycle bin. Null = not deleted (live task).</summary>
    public DateTime? DeletedAt { get; set; }
}
