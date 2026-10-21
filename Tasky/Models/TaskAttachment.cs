namespace Tasky.Models;

public class TaskAttachment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public string FileName { get; set; } = string.Empty;
    /// <summary>Absolute path on disk (under %AppData%\Tasky\Attachments\{taskId}\).</summary>
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
