using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Tasky.Helpers;

/// <summary>Files dragged in from Explorer / Finder, as plain paths.</summary>
public static class DroppedFiles
{
    public static bool Any(DragEventArgs e) => e.DataTransfer.Contains(DataFormat.File);

    public static IReadOnlyList<string> From(DragEventArgs e)
        => e.DataTransfer.TryGetFiles() is { } items
            ? [.. items.Select(item => item.TryGetLocalPath()).OfType<string>()]
            : [];
}
