using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Tasky.Behaviors;

/// <summary>
/// Makes the mouse wheel scroll a ScrollViewer when the pointer is over its Padding strip.
/// The ScrollViewer theme applies Padding as a Margin around the content presenter (so content
/// is sized and clipped correctly), and Avalonia only scrolls on the presenter's wheel handler,
/// so without this the wheel would do nothing along the page edges.
/// </summary>
public static class ScrollPaddingWheel
{
    private const double WheelScrollStep = 50;
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        InputElement.PointerWheelChangedEvent.AddClassHandler<ScrollViewer>(OnWheel);
    }

    private static void OnWheel(ScrollViewer viewer, PointerWheelEventArgs e)
    {
        if (e.Handled || !IsOverPaddingStrip(viewer, e)) return;

        double maxY = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        if (maxY <= 0 || e.Delta.Y == 0) return;

        double y = Math.Clamp(viewer.Offset.Y - e.Delta.Y * WheelScrollStep, 0, maxY);
        viewer.Offset = viewer.Offset.WithY(y);
        e.Handled = true;
    }

    /// <summary>The strip is the template's root Grid background, outside the presenter and scroll bars.</summary>
    private static bool IsOverPaddingStrip(ScrollViewer viewer, PointerWheelEventArgs e)
        => e.Source is Grid grid && grid.TemplatedParent == viewer;
}
