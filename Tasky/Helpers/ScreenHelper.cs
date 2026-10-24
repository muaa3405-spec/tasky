using Avalonia;
using Avalonia.Controls;

namespace Tasky.Helpers;

/// <summary>Screen geometry in device-independent units, plus pixel positioning of windows.</summary>
public static class ScreenHelper
{
    /// <summary>The usable screen area (without the taskbar, or the menu bar and Dock) for <paramref name="window"/>.</summary>
    public static Rect WorkArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen is null) return new Rect(0, 0, 1280, 800);
        var area = screen.WorkingArea;
        double s = screen.Scaling;
        return new Rect(area.X / s, area.Y / s, area.Width / s, area.Height / s);
    }

    /// <summary>Moves the window so its top-left corner is at <paramref name="topLeft"/> (device-independent units).</summary>
    public static void MoveTo(Window window, Point topLeft)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        double s = screen?.Scaling ?? 1;
        window.Position = new PixelPoint((int)(topLeft.X * s), (int)(topLeft.Y * s));
    }
}
