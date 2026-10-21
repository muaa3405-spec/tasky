namespace Tasky.Models;

public class NoteAttachment : IAttachment
{
    public int Id { get; set; }
    public int NoteId { get; set; }
    public string FileName { get; set; } = string.Empty;
    /// <summary>Absolute path on disk (under %AppData%\Tasky\Attachments\notes\{noteId}\).</summary>
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
