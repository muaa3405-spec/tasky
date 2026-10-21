using System.IO;

namespace Tasky.Services;

/// <summary>
/// Owns the attachment files on disk: one folder per task or note under the data
/// folder (see <see cref="AttachmentFolders"/>). Every new file gets a unique name,
/// so pasting or attaching several files at once never overwrites an earlier one.
/// Deletes are limited to this folder.
/// </summary>
public sealed class AttachmentStorage
{
    private readonly string _root;

    public AttachmentStorage(string root)
    {
        _root = Path.GetFullPath(root);
        Directory.CreateDirectory(_root);
    }

    /// <summary>Copies a user file into the owner's folder, keeping its name where possible.</summary>
    /// <param name="ownerFolder">From <see cref="AttachmentFolders"/>.</param>
    public string SaveCopy(string ownerFolder, string sourcePath)
    {
        var dest = UniquePath(EnsureFolder(ownerFolder),
            Path.GetFileNameWithoutExtension(sourcePath), Path.GetExtension(sourcePath));
        File.Copy(sourcePath, dest, overwrite: false);
        return dest;
    }

    /// <summary>Writes pasted image bytes to a new, uniquely named file.</summary>
    /// <param name="ownerFolder">From <see cref="AttachmentFolders"/>.</param>
    public string SaveBytes(string ownerFolder, byte[] data, string extension)
    {
        var dest = UniquePath(EnsureFolder(ownerFolder), $"img_{DateTime.Now:yyyyMMdd_HHmmss}", extension);
        using (var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write))
            fs.Write(data);
        return dest;
    }

    public void DeleteFile(string path)
    {
        if (!IsInsideRoot(path)) return;
        TryDelete(() => File.Delete(path));
    }

    /// <param name="ownerFolder">From <see cref="AttachmentFolders"/>.</param>
    public void DeleteFolder(string ownerFolder)
    {
        var folder = Path.Combine(_root, ownerFolder);
        if (!IsInsideRoot(folder)) return;
        TryDelete(() => { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); });
    }

    /// <summary>
    /// Removes task and note folders whose owner no longer exists (left over from older
    /// versions). An empty id set more likely means a missing or reset database than
    /// "everything was deleted", so that kind of folder is then left alone.
    /// </summary>
    public void DeleteOrphanFolders(ISet<int> existingTaskIds, ISet<int> existingNoteIds)
    {
        if (existingTaskIds.Count > 0)
            DeleteOrphansIn(_root, existingTaskIds, AttachmentFolders.ForTask);

        var notesRoot = Path.Combine(_root, AttachmentFolders.NotesFolderName);
        if (existingNoteIds.Count > 0 && Directory.Exists(notesRoot))
            DeleteOrphansIn(notesRoot, existingNoteIds, AttachmentFolders.ForNote);
    }

    private void DeleteOrphansIn(string parent, ISet<int> existingIds, Func<int, string> ownerFolder)
    {
        foreach (var dir in Directory.EnumerateDirectories(parent))
        {
            if (int.TryParse(Path.GetFileName(dir), out int id) && !existingIds.Contains(id))
                DeleteFolder(ownerFolder(id));
        }
    }

    private string EnsureFolder(string ownerFolder)
    {
        var folder = Path.Combine(_root, ownerFolder);
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
