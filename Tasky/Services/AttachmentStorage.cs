using System.IO;

namespace Tasky.Services;

/// <summary>
/// Owns the attachment files on disk (one folder per task under the data folder).
/// Every new file gets a unique name, so pasting or attaching several files at
/// once never overwrites an earlier one. Deletes are limited to this folder.
/// </summary>
public sealed class AttachmentStorage
{
    private readonly string _root;

    public AttachmentStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Copies a user file into the task's folder, keeping its name where possible.</summary>
    public string SaveCopy(int taskId, string sourcePath)
    {
        var dest = UniquePath(TaskFolder(taskId),
            Path.GetFileNameWithoutExtension(sourcePath), Path.GetExtension(sourcePath));
        File.Copy(sourcePath, dest, overwrite: false);
        return dest;
    }

    /// <summary>Writes pasted image bytes to a new, uniquely named file.</summary>
    public string SaveBytes(int taskId, byte[] data, string extension)
    {
        var dest = UniquePath(TaskFolder(taskId), $"img_{DateTime.Now:yyyyMMdd_HHmmss}", extension);
        using (var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            fs.Write(data);
        return dest;
    }

    public void DeleteFile(string path)
    {
        if (!IsInsideRoot(path)) return;
        TryDelete(() => File.Delete(path));
    }

    public void DeleteTaskFolder(int taskId)
    {
        var folder = Path.Combine(_root, taskId.ToString());
        TryDelete(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); });
    }

    /// <summary>Removes task folders whose task no longer exists (left over from older versions).</summary>
    public void DeleteOrphanFolders(ISet<int> existingTaskIds)
    {
        // An empty task table more likely means a missing/reset database than
        // "everything was deleted" — leave the files alone in that case.
        if (existingTaskIds.Count == 0) return;

        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            if (int.TryParse(Path.GetFileName(dir), out int id) && !existingTaskIds.Contains(id))
                DeleteTaskFolder(id);
        }
    }

    private string TaskFolder(int taskId)
    {
        var folder = Path.Combine(_root, taskId.ToString());
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>"name.ext", or "name_2.ext", "name_3.ext"… when that name is taken.</summary>
    private static string UniquePath(string folder, string baseName, string extension)
    {
        var path = Path.Combine(folder, baseName + extension);
        for (int n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"{baseName}_{n}{extension}");
        return path;
    }

    private bool IsInsideRoot(string path)
        => Path.GetFullPath(path).StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(Action delete)
    {
        try { delete(); }
        catch (Exception ex) { CrashLog.Write("Could not delete attachment file", ex); }
    }
}
