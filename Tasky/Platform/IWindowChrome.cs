using Avalonia.Controls;

namespace Tasky.Platform;

/// <summary>Colours the native title bar to match the app theme where the OS allows it.</summary>
public interface IWindowChrome
{
    /// <summary>Dark or light title bar for a dialog.</summary>
    void ApplyTheme(Window window, bool dark);

    /// <summary>Dark or light title bar whose caption colour matches the main window header.</summary>
    void ApplyThemeWithCaption(Window window, bool dark);
}
