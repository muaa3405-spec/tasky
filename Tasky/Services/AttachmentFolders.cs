using System.IO;

namespace Tasky.Services;

/// <summary>
/// Layout of the Attachments folder, shared by file storage and the database's path repair:
///   Attachments\&lt;task id&gt;\…        files attached to a task
///   Attachments\notes\&lt;note id&gt;\…  files attached to a note
/// </summary>
public static class AttachmentFolders
{
    public const string NotesFolderName = "notes";

    /// <summary>Folder for a task's files, relative to the Attachments root.</summary>
    public static string ForTask(int taskId) => taskId.ToString();

    /// <summary>Folder for a note's files, relative to the Attachments root.</summary>
    public static string ForNote(int noteId) => Path.Combine(NotesFolderName, noteId.ToString());
}
