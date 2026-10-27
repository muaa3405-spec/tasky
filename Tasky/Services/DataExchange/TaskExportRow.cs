using Tasky.Models;

namespace Tasky.Services.DataExchange;

/// <summary>One task as it is written to an export or a printed list, with names instead of ids.</summary>
public sealed record TaskExportRow(
    int            Number,
    string         Title,
    string         Description,
    string         Group,
    DateTime?      DueDate,
    RecurrenceType Repeat,
    DateTime?      RepeatEnds,
    bool           IsCompleted,
    DateTime?      CompletedAt,
    int            MinutesSpent,
    DateTime       CreatedAt)
{
    public string NumberDisplay => $"#{Number:D3}";

    public string RepeatDisplay => Repeat switch
    {
        RecurrenceType.Daily   => "Daily",
        RecurrenceType.Weekly  => "Weekly",
        RecurrenceType.Monthly => "Monthly",
        _                      => string.Empty
    };

    public static RecurrenceType ParseRepeat(string text) => text.Trim().ToLowerInvariant() switch
    {
        "daily"   or "every day"   => RecurrenceType.Daily,
        "weekly"  or "every week"  => RecurrenceType.Weekly,
        "monthly" or "every month" => RecurrenceType.Monthly,
        _                          => RecurrenceType.None
    };

    public string TimeSpentDisplay => MinutesSpent <= 0 ? string.Empty
        : MinutesSpent < 60 ? $"{MinutesSpent}m"
        : $"{MinutesSpent / 60}h {MinutesSpent % 60:D2}m";
}
