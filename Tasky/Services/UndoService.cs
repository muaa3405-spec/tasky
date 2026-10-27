namespace Tasky.Services;

/// <summary>
/// Remembers the most recent change that can be taken back (complete, delete, postpone),
/// so "Undo" on the notification or Ctrl+Z / ⌘Z reverses it. Only one step is kept:
/// the newest change replaces the previous one.
/// </summary>
public sealed class UndoService
{
    /// <summary>After this long an undo would surprise more than help, so it is dropped.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private sealed record Step(string Description, Action Undo, DateTime RecordedAt);

    private Step? _last;

    public void Record(string description, Action undo) => _last = new Step(description, undo, DateTime.Now);

    public void Clear() => _last = null;

    /// <summary>Reverses the last change. Returns what was undone, or null when there was nothing to undo.</summary>
    public string? UndoLast()
    {
        var step = _last;
        _last = null;
        if (step is null || DateTime.Now - step.RecordedAt > Lifetime) return null;
        step.Undo();
        return step.Description;
    }
}
