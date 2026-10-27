using System.Globalization;

namespace Tasky.Services.DataExchange;

/// <summary>Writes tasks as a CSV file that opens in Excel, Numbers or Google Sheets, and that <see cref="TaskCsvImporter"/> reads back.</summary>
public static class TaskCsvExporter
{
    public const string DateFormat     = "yyyy-MM-dd";
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";

    public const string StatusOpen = "Open";
    public const string StatusDone = "Done";

    /// <summary>Column names, also recognised by the importer.</summary>
    public static readonly string[] Columns =
    [
        "Number", "Title", "Description", "Group", "Due date", "Repeat", "Repeat ends",
        "Status", "Completed at", "Minutes spent", "Created at"
    ];

    public static string ToCsv(IEnumerable<TaskExportRow> tasks)
        => CsvFormat.Write(new[] { (IReadOnlyList<string>)Columns }.Concat(tasks.Select(ToFields)));

    private static IReadOnlyList<string> ToFields(TaskExportRow t) =>
    [
        t.NumberDisplay,
        t.Title,
        t.Description,
        t.Group,
        Format(t.DueDate, DateFormat),
        t.RepeatDisplay,
        Format(t.RepeatEnds, DateFormat),
        t.IsCompleted ? StatusDone : StatusOpen,
        Format(t.CompletedAt, DateTimeFormat),
        t.MinutesSpent > 0 ? t.MinutesSpent.ToString(CultureInfo.InvariantCulture) : string.Empty,
        Format(t.CreatedAt, DateTimeFormat)
    ];

    private static string Format(DateTime? value, string format)
        => value?.ToString(format, CultureInfo.InvariantCulture) ?? string.Empty;
}
