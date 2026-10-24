using Avalonia;
using Avalonia.Input;

namespace Tasky.Helpers;

/// <summary>Keyboard shortcuts that differ by OS (Ctrl+V on Windows, Cmd+V on macOS).</summary>
public static class KeyGestures
{
    public static bool IsPaste(KeyEventArgs e)
        => Application.Current?.PlatformSettings?.HotkeyConfiguration.Paste.Any(g => g.Matches(e))
           ?? (e.Key == Key.V && e.KeyModifiers.HasFlag(KeyModifiers.Control));

    /// <summary>"Ctrl+V" or "⌘V", for hints shown to the user.</summary>
    public static string PasteHint => OperatingSystem.IsMacOS() ? "⌘V" : "Ctrl+V";
}
