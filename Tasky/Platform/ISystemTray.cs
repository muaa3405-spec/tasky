namespace Tasky.Platform;

/// <summary>The notification-area icon (Windows) or menu-bar icon (macOS) with its menu.</summary>
public interface ISystemTray : IDisposable
{
    /// <summary>Raised when the user double-clicks the icon (or clicks a balloon) to restore Tasky.</summary>
    event Action? OpenRequested;

    /// <summary>Shows the icon with its menu.</summary>
    void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu);

    /// <summary>Shows a short notification from the icon where the OS supports it.</summary>
    void ShowBalloon(string title, string message, TimeSpan duration);
}

/// <summary>One tray menu entry; a null <see cref="Text"/> is a separator.</summary>
public sealed record TrayMenuItem(string? Text, Action? Click = null)
{
    public static TrayMenuItem Separator { get; } = new(Text: null);
}
