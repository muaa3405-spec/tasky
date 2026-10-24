using System.Runtime.Versioning;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>Win+Shift+T through Win32 RegisterHotKey, delivered to the hidden message window.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsGlobalHotkey : IGlobalHotkey
{
    private const int  HotkeyId = 9001;
    private const uint KeyT     = 'T';

    private readonly Win32MessageWindow _window;
    private Action? _callback;
    private bool _registered;

    public WindowsGlobalHotkey(Win32MessageWindow window) => _window = window;

    public string DisplayName => "Win + Shift + T";

    public bool Register(Action callback)
    {
        _callback = callback;
        _window.MessageReceived += OnMessage;
        _registered = RegisterHotKey(_window.Handle, HotkeyId, MOD_WIN | MOD_SHIFT | MOD_NOREPEAT, KeyT);
        return _registered;
    }

    private bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WM_HOTKEY || wParam.ToInt32() != HotkeyId) return false;
        _callback?.Invoke();
        return true;
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        if (_registered) UnregisterHotKey(_window.Handle, HotkeyId);
        _registered = false;
    }
}
