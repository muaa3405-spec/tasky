using System.Globalization;
using Tasky.Models;

namespace Tasky.Services.DataExchange;

/// <summary>
/// Reads tasks from a CSV file: Tasky's own export, a spreadsheet the user made, or another app's
/// export (Todoist, TickTick, Things and others). Columns are matched by common names, so the
/// only column that must exist is a title.
/// </summary>
public static class TaskCsvImporter
{
    public sealed record Result(IReadOnlyList<TaskExportRow> Tasks, int SkippedRows);

    /// <summary>Header names (lower case, letters only) accepted for each field.</summary>
    private static readonly string[] TitleNames       = ["title", "content", "task", "taskname", "name", "subject", "todo"];
    private static readonly string[] DescriptionNames = ["description", "notes", "note", "body", "details"];
    private static readonly string[] GroupNames       = ["group", "project", "list", "listname", "category", "folder"];
    private static readonly string[] DueNames         = ["duedate", "due", "date", "startdate", "deadline"];
    private static readonly string[] RepeatNames      = ["repeat", "recurrence", "repeats"];
    private static readonly string[] RepeatEndNames   = ["repeatends", "repeatenddate", "recurrenceenddate"];
    private static readonly string[] StatusNames      = ["status", "completed", "done", "iscompleted", "complete"];
    private static readonly string[] CompletedAtNames = ["completedat", "completeddate", "datecompleted", "completedtime"];
    private static readonly string[] MinutesNames     = ["minutesspent", "minutes", "timespent"];
    private static readonly string[] CreatedNames     = ["createdat", "created", "createddate", "datecreated"];

    /// <summary>Todoist exports sections and comments as rows too; only "task" rows are tasks.</summary>
    private static readonly string[] TypeNames = ["type"];
    private const string TaskType = "task";

    private static readonly HashSet<string> DoneValues = new(StringComparer.OrdinalIgnoreCase)
        { TaskCsvExporter.StatusDone, "completed", "complete", "true", "yes", "y", "1", "x", "✓" };

    /// <exception cref="FormatException">The file has no title column.</exception>
    public static Result Parse(string csvText)
    {
        var rows = CsvFormat.Read(csvText);
        if (rows.Count == 0) return new Result([], 0);

        var header = rows[0].Select(Normalize).ToList();
        int Col(string[] names) => header.FindIndex(names.Contains);

        int title = Col(TitleNames);
        if (title < 0)
            throw new FormatException("The file has no column for the task title (for example \"Title\" or \"Task\").");

        int description = Col(DescriptionNames), group = Col(GroupNames), due = Col(DueNames),
            repeat = Col(RepeatNames), repeatEnd = Col(RepeatEndNames), status = Col(StatusNames),
            completedAt = Col(CompletedAtNames), minutes = Col(MinutesNames), created = Col(CreatedNames),
            type = Col(TypeNames);

        var tasks = new List<TaskExportRow>();
        int skipped = 0;
        foreach (var row in rows.Skip(1))
        {
            string Field(int index) => index >= 0 && index < row.Count ? row[index].Trim() : string.Empty;

            if (type >= 0 && Field(type) is { Length: > 0 } kind && !kind.Equals(TaskType, StringComparison.OrdinalIgnoreCase)
                || Field(title).Length == 0)
            {
                skipped++;
                continue;
            }

            var completedTime = ParseDate(Field(completedAt));
            bool isDone = DoneValues.Contains(Field(status)) || completedTime.HasValue;
            tasks.Add(new TaskExportRow(
                Number:       0,
                Title:        Field(title),
                Description:  Field(description),
                Group:        Field(group),
                DueDate:      ParseDate(Field(due))?.Date,
                Repeat:       TaskExportRow.ParseRepeat(Field(repeat)),
                RepeatEnds:   ParseDate(Field(repeatEnd))?.Date,
                IsCompleted:  isDone,
                CompletedAt:  isDone ? completedTime ?? DateTime.Now : null,
                MinutesSpent: int.TryParse(Field(minutes), NumberStyles.Integer, CultureInfo.InvariantCulture, out int m) ? Math.Max(0, m) : 0,
                CreatedAt:    ParseDate(Field(created)) ?? DateTime.Now));
        }
        return new Result(tasks, skipped);
    }

    private static string Normalize(string header)
        => new(header.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());

    private static readonly string[] KnownDateFormats = [TaskCsvExporter.DateTimeFormat, TaskCsvExporter.DateFormat, "yyyy/MM/dd"];

    /// <summary>
    /// Tasky's own formats first, then the computer's date style (which also reads ISO 8601 such as
    /// 2026-10-27T09:00:00Z). Unreadable text, like Todoist's "every day", gives null.
    /// </summary>
    private static DateTime? ParseDate(string text)
    {
        if (text.Length == 0) return null;
        if (DateTime.TryParseExact(text, KnownDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var exact))
            return exact;
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local))
            return local;
        return null;
    }
}
