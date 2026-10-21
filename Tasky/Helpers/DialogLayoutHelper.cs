using System.Windows;

namespace Tasky.Helpers;

/// <summary>
/// Keeps content-sized dialogs inside the visible screen: the window is
/// centred on the screen and never taller than the work area, so long
/// content scrolls instead of running off the bottom edge.
/// </summary>
public static class DialogLayoutHelper
{
    /// <summary>Share of the work-area height a dialog may occupy.</summary>
    private const double MaxWorkAreaHeightRatio = 0.9;

    public static void FitToWorkArea(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.MaxHeight = SystemParameters.WorkArea.Height * MaxWorkAreaHeightRatio;
    }
}
