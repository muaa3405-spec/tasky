namespace Tasky.Models;

public class Note
{
    public int      Id          { get; set; }
    public string   Title       { get; set; } = string.Empty;
    public string   Description { get; set; } = string.Empty;
    public string   Tags        { get; set; } = string.Empty;
    public DateTime CreatedAt   { get; set; } = DateTime.Now;
    public DateTime UpdatedAt   { get; set; } = DateTime.Now;

    /// <summary>Number of attached files (loaded with the note, not stored on it).</summary>
    public int AttachmentCount { get; set; }
    public bool HasAttachments => AttachmentCount > 0;
    public string AttachmentCountDisplay => AttachmentCount == 1 ? "1 attachment" : $"{AttachmentCount} attachments";

    // Splits Tags into trimmed, non-empty entries for display as chips
    public List<string> TagList =>
        string.IsNullOrWhiteSpace(Tags)
            ? []
            : [.. Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(t => !string.IsNullOrWhiteSpace(t))];
}
