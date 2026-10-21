using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Tasky.Helpers;

public static class ThemeHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void Apply(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int val = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref val, sizeof(int));
    }

    /// <summary>
    /// Attaches to SourceInitialized:
    ///   1. Applies dark/light title bar chrome to match the app theme.
    ///   2. Sets a taller WindowChrome caption so the close (X) button is
    ///      large enough to be easily clicked on ToolWindow-style dialogs.
    /// </summary>
    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            Apply(window, ((App)Application.Current).IsDarkTheme);

            // Give ToolWindow dialogs a taller title bar so the X button
            // is comfortably sized (default ToolWindow caption is ~18 px).
            if (window.WindowStyle == WindowStyle.ToolWindow)
            {
                WindowChrome.SetWindowChrome(window, new WindowChrome
                {
                    CaptionHeight        = 36,
                    ResizeBorderThickness = new Thickness(4),
                    GlassFrameThickness  = new Thickness(0),
                    UseAeroCaptionButtons = true
                });
            }
        };
    }
}
