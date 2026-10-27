using Tasky.ViewModels;

namespace Tasky.Helpers;

/// <summary>
/// What "Today", "This Week" and "All" mean for an open task. Used by the task list
/// and by the printed list, so both always agree.
/// </summary>
public static class TaskDateFilters
{
    public static bool Matches(TaskFilter filter, DateTime? dueDate, DateTime today)
    {
        var (weekStart, weekEnd) = WorkWeek(today);
        return filter switch
        {
            // Overdue and undated tasks stay in Today until they are done.
            TaskFilter.Today    => !dueDate.HasValue || dueDate.Value.Date <= today,
            TaskFilter.ThisWeek => dueDate.HasValue && dueDate.Value.Date >= weekStart && dueDate.Value.Date <= weekEnd,
            _                   => true
        };
    }

    /// <summary>Monday to Friday of the week containing <paramref name="today"/> (the week starts on Monday).</summary>
    public static (DateTime Start, DateTime End) WorkWeek(DateTime today)
    {
        int daysFromMonday = ((int)today.DayOfWeek + 6) % 7;   // Sun=6, Mon=0, ... Sat=5
        var monday = today.AddDays(-daysFromMonday);
        return (monday, monday.AddDays(4));
    }

    public static string Label(TaskFilter filter) => filter switch
    {
        TaskFilter.Today    => "Today",
        TaskFilter.ThisWeek => "This week",
        _                   => "All open tasks"
    };
}
