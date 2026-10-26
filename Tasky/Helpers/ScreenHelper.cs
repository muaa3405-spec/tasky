using Avalonia;
using Avalonia.Controls;

namespace Tasky.Helpers;

/// <summary>Screen geometry in device-independent units, plus positioning of windows.</summary>
/// <remarks>
/// Screen areas and <see cref="Window.Position"/> share one unit, and <see cref="WindowBase.DesktopScaling"/>
/// converts it to device-independent units. That unit is physical pixels on Windows but points on macOS,
/// so dividing by the screen's render scaling (2 on a Retina Mac) would halve every position there.
/// </remarks>
public static class ScreenHelper
{
    /// <summary>The usable screen area (without the taskbar, or the menu bar and Dock) for <paramref name="window"/>.</summary>
    public static Rect WorkArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return new Rect(0, 0, 1280, 800);
        var area = screen.WorkingArea;
        double s = window.DesktopScaling;
        return new Rect(area.X / s, area.Y / s, area.Width / s, area.Height / s);
    }

    /// <summary>Moves the window so its top-left corner is at <paramref name="topLeft"/> (device-independent units).</summary>
    public static void MoveTo(Window window, Point topLeft)
    {
        double s = window.DesktopScaling;
        window.Position = new PixelPoint((int)(topLeft.X * s), (int)(topLeft.Y * s));
    }
}
