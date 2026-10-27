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

    /// <summary>Where the OS keeps per-user application data.</summary>
    private static string UserDataRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>
    /// User data: database, settings, attachments
    /// (%AppData%\Tasky on Windows, ~/Library/Application Support/Tasky on macOS).
    /// </summary>
    public static string DataFolder { get; } = OverrideRoot is not null
        ? Path.Combine(OverrideRoot, "Roaming", "Tasky")
        : Path.Combine(UserDataRoot, "Tasky");

    public static string DatabaseFile      => Path.Combine(DataFolder, DatabaseFileName);
    public static string SettingsFile      => Path.Combine(DataFolder, SettingsFileName);
    public static string AttachmentsFolder => Path.Combine(DataFolder, AttachmentsFolderName);

    public const string DatabaseFileName      = "tasky.db";
    public const string SettingsFileName      = "settings.json";
    public const string AttachmentsFolderName = "Attachments";

    /// <summary>
    /// Machine-local working files: logs, import staging, safety copies
    /// (%LocalAppData%\Tasky on Windows, ~/Library/Application Support/Tasky Local on macOS).
    /// Never inside <see cref="DataFolder"/>, because an import swaps that whole folder.
    /// </summary>
    public static string LocalFolder { get; } = OverrideRoot is not null
        ? Path.Combine(OverrideRoot, "Local", "Tasky")
        : OperatingSystem.IsMacOS()
            ? Path.Combine(UserDataRoot, "Tasky Local")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tasky");

    public static string LogsFolder          => Path.Combine(LocalFolder, "logs");
    public static string PendingImportFolder => Path.Combine(LocalFolder, "pending-import");
    public static string ImportBackupsFolder => Path.Combine(LocalFolder, "before-import");
    public static string TempFolder          => Path.Combine(LocalFolder, "temp");
    public static string BackupsFolder       => Path.Combine(LocalFolder, "backups");
    public static string PendingRestoreFile  => Path.Combine(LocalFolder, "pending-restore.txt");
}
