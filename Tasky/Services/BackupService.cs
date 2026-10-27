using System.Globalization;
using System.IO;

namespace Tasky.Services;

/// <summary>
/// Automatic daily safety copies of the database and settings, kept in
/// %LocalAppData%\Tasky\backups (macOS: ~/Library/Application Support/Tasky Local/backups).
/// Attachment files are not copied: they are never changed in place, only added or deleted.
///
/// Restoring works like a migration import: the choice is staged, Tasky restarts, and
/// <see cref="ApplyPendingRestore"/> swaps the files in before anything opens the database.
/// </summary>
public sealed class BackupService
{
    /// <summary>How many backups are kept; older ones are deleted.</summary>
    public const int KeepCount = 14;

    /// <summary>A new backup is made when the newest one is at least this old.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    private const string FolderPrefix    = "Tasky-backup-";
    private const string TimestampFormat = "yyyyMMdd-HHmmss";
    private const string PartialSuffix   = ".partial";

    private static readonly object Gate = new();

    private readonly DatabaseService _db;

    public BackupService(DatabaseService db) => _db = db;

    public sealed record BackupInfo(string Folder, DateTime CreatedAt)
    {
        public string Label => CreatedAt.Date == DateTime.Today
            ? $"Today {CreatedAt:HH:mm}"
            : CreatedAt.ToString("ddd d MMM yyyy, HH:mm");
    }

    /// <summary>All finished backups, newest first.</summary>
    public static IReadOnlyList<BackupInfo> List()
    {
        if (!Directory.Exists(AppPaths.BackupsFolder)) return [];
        return [.. Directory.EnumerateDirectories(AppPaths.BackupsFolder, FolderPrefix + "*")
            .Select(TryRead)
            .OfType<BackupInfo>()
            .OrderByDescending(b => b.CreatedAt)];
    }

    public static BackupInfo? Latest => List().FirstOrDefault();

    /// <summary>Makes a backup when none was made in the last <see cref="Interval"/>.</summary>
    public BackupInfo? BackUpIfDue()
        => Latest is { } latest && DateTime.Now - latest.CreatedAt < Interval ? null : BackUpNow();

    public BackupInfo BackUpNow()
    {
        lock (Gate)
        {
            var created = DateTime.Now;
            var folder  = Path.Combine(AppPaths.BackupsFolder, FolderPrefix + created.ToString(TimestampFormat, CultureInfo.InvariantCulture));
            var partial = folder + PartialSuffix;
            TryDeleteFolder(partial);
            Directory.CreateDirectory(partial);

            _db.SnapshotTo(Path.Combine(partial, AppPaths.DatabaseFileName));
            if (File.Exists(AppPaths.SettingsFile))
                File.Copy(AppPaths.SettingsFile, Path.Combine(partial, AppPaths.SettingsFileName), overwrite: true);

            // Only a complete backup gets its final name, so a crash mid-copy never looks like a backup.
            TryDeleteFolder(folder);
            Directory.Move(partial, folder);
            DeleteOldBackups();
            return new BackupInfo(folder, created);
        }
    }

    /// <summary>Runs <see cref="BackUpIfDue"/> off the UI thread; a failure is only logged.</summary>
    public void BackUpIfDueInBackground()
        => Task.Run(() =>
        {
            try { BackUpIfDue(); }
            catch (Exception ex) { CrashLog.Write("Automatic backup failed", ex); }
        });

    // ── Restore ───────────────────────────────────────────────────────────

    /// <summary>Checks the backup opens, then marks it to be restored at the next start.</summary>
    public static void StageRestore(BackupInfo backup)
    {
        DatabaseService.CountRecords(DatabaseIn(backup.Folder));   // throws if the copy is unusable
        Directory.CreateDirectory(AppPaths.LocalFolder);
        File.WriteAllText(AppPaths.PendingRestoreFile, backup.Folder);
    }

    /// <summary>
    /// Call at startup, before the database is opened. Returns true when a staged restore was applied.
    /// The data it replaces is kept in the before-import safety folder. On failure the current data
    /// is put back and the error rethrown.
    /// </summary>
    public static bool ApplyPendingRestore()
    {
        if (!File.Exists(AppPaths.PendingRestoreFile)) return false;
        var source = File.ReadAllText(AppPaths.PendingRestoreFile).Trim();
        File.Delete(AppPaths.PendingRestoreFile);

        var safetyCopy = Path.Combine(AppPaths.ImportBackupsFolder, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}");
        try
        {
            if (!File.Exists(DatabaseIn(source))) throw new FileNotFoundException("The backup to restore is missing", DatabaseIn(source));
            Directory.CreateDirectory(safetyCopy);
            CopyIfExists(AppPaths.DatabaseFile, Path.Combine(safetyCopy, AppPaths.DatabaseFileName));
            CopyIfExists(AppPaths.SettingsFile, Path.Combine(safetyCopy, AppPaths.SettingsFileName));

            // The write-ahead journal belongs to the database being replaced.
            DeleteIfExists(AppPaths.DatabaseFile + "-wal");
            DeleteIfExists(AppPaths.DatabaseFile + "-shm");
            Directory.CreateDirectory(AppPaths.DataFolder);
            File.Copy(DatabaseIn(source), AppPaths.DatabaseFile, overwrite: true);
            CopyIfExists(Path.Combine(source, AppPaths.SettingsFileName), AppPaths.SettingsFile);
            return true;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Restoring a backup failed; putting the current data back", ex);
            CopyIfExists(Path.Combine(safetyCopy, AppPaths.DatabaseFileName), AppPaths.DatabaseFile);
            CopyIfExists(Path.Combine(safetyCopy, AppPaths.SettingsFileName), AppPaths.SettingsFile);
            throw;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string DatabaseIn(string backupFolder) => Path.Combine(backupFolder, AppPaths.DatabaseFileName);

    private static BackupInfo? TryRead(string folder)
    {
        var stamp = Path.GetFileName(folder)[FolderPrefix.Length..];
        return DateTime.TryParseExact(stamp, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var created)
               && File.Exists(DatabaseIn(folder))
            ? new BackupInfo(folder, created)
            : null;
    }

    private static void DeleteOldBackups()
    {
        foreach (var old in List().Skip(KeepCount)) TryDeleteFolder(old.Folder);
    }

    private static void CopyIfExists(string from, string to)
    {
        if (File.Exists(from)) File.Copy(from, to, overwrite: true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception ex) { CrashLog.Write($"Could not delete '{folder}'", ex); }
    }
}
