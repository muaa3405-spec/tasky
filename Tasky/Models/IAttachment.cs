namespace Tasky.Models;

/// <summary>A file attached to a task or a note.</summary>
public interface IAttachment
{
    int Id { get; }
    string FileName { get; }
    /// <summary>Absolute path on disk, under %AppData%\Tasky\Attachments\.</summary>
    string FilePath { get; }
}
