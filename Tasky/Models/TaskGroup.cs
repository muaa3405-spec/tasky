namespace Tasky.Models;

public class TaskGroup
{
    public int    Id    { get; set; }
    public string Name  { get; set; } = string.Empty;
    /// <summary>Emoji or short label used as colour/icon (e.g. "🔵","💼","🏠").</summary>
    public string Color { get; set; } = "#3B82F6";
}
