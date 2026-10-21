using System.IO;

namespace Tasky.Services;

/// <summary>Single source of truth for where Tasky keeps its files on this computer.</summary>
public static class AppPaths
{
    /// <summary>
    /// Developer option: set this environment variable to a folder to run Tasky
    /// against sandbox data instead of your real data (used for testing).
    /// </summary>
    public const string DataRootOverrideVariable = "TASKY_DATA_ROOT";

    private static readonly string? OverrideRoot =
        Environment.GetEnvironmentVariable(DataRootOverrideVariable) is { Length: > 0 } root ? root : null;

    /// <summary>User data: database, settings, attachments (%AppData%\Tasky).</summary>
    public static string DataFolder { get; } = OverrideRoot is not null
        ? Path.Combine(OverrideRoot, "Roaming", "Tasky")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tasky");

    public static string DatabaseFile      => Path.Combine(DataFolder, DatabaseFileName);
    public static string SettingsFile      => Path.Combine(DataFolder, SettingsFileName);
    public static string AttachmentsFolder => Path.Combine(DataFolder, AttachmentsFolderName);

    public const string DatabaseFileName      = "tasky.db";
    public const string SettingsFileName      = "settings.json";
    public const string AttachmentsFolderName = "Attachments";

    /// <summary>Machine-local working files: logs, import staging, safety copies (%LocalAppData%\Tasky).</summary>
    public static string LocalFolder { get; } = OverrideRoot is not null
        ? Path.Combine(OverrideRoot, "Local", "Tasky")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tasky");

    public static string LogsFolder          => Path.Combine(LocalFolder, "logs");
    public static string PendingImportFolder => Path.Combine(LocalFolder, "pending-import");
    public static string ImportBackupsFolder => Path.Combine(LocalFolder, "before-import");
    public static string TempFolder          => Path.Combine(LocalFolder, "temp");
}
