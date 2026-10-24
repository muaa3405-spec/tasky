namespace Tasky.Platform;

/// <summary>
/// The system-wide Quick Add shortcut: Win+Shift+T on Windows, Cmd+Shift+T on macOS.
/// The callback runs on the UI thread.
/// </summary>
public interface IGlobalHotkey : IDisposable
{
    /// <summary>Text shown to the user for the shortcut.</summary>
    string DisplayName { get; }

    bool Register(Action callback);
}
