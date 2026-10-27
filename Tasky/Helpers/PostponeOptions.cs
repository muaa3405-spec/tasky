namespace Tasky.Helpers;

/// <summary>The "postpone" choices offered on a task, and the dates they move it to.</summary>
public static class PostponeOptions
{
    public sealed record Option(string Label, Func<DateTime, DateTime> NewDueDate);

    public static readonly Option Tomorrow = new("Tomorrow", today => today.AddDays(1));

    /// <summary>Monday of next week (the week starts on Monday, as in the "This Week" filter).</summary>
    public static readonly Option NextWeek = new("Next week (Monday)", NextMonday);

    public static readonly IReadOnlyList<Option> All = [Tomorrow, NextWeek];

    private static DateTime NextMonday(DateTime today)
    {
        int daysUntilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        return today.AddDays(daysUntilMonday == 0 ? 7 : daysUntilMonday);
    }
}
