namespace Tasky.Services.Migration;

/// <summary>What a migration file contains, shown to the user before export and import.</summary>
public sealed record MigrationCounts(
    int OpenTasks, int CompletedTasks, int Notes, int Groups, int Reminders, int Attachments)
{
    public string Summary =>
        $"{OpenTasks} open tasks, {CompletedTasks} completed, {Notes} notes, " +
        $"{Groups} groups, {Reminders} reminders, {Attachments} attachments";
}

/// <summary>The description stored inside every migration file.</summary>
public sealed record MigrationManifest(int Format, DateTime CreatedAt, string AppVersion, MigrationCounts Counts)
{
    public const int CurrentFormat = 1;
}
