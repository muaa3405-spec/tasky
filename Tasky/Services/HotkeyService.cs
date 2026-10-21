using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Tasky.Services;

/// <summary>
/// Registers a system-wide hotkey using Win32 RegisterHotKey.
/// Fires the callback on the UI thread via the window's message loop.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    // Modifier flags
    public const uint MOD_ALT     = 0x0001;
    public const uint MOD_CTRL    = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const uint MOD_WIN     = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId  = 9001;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _window;
    private HwndSource? _source;
    private Action? _callback;
    private bool _registered;

    public HotkeyService(Window window) => _window = window;

    public bool Register(uint modifiers, uint virtualKey, Action callback)
    {
        _callback = callback;
        var helper = new WindowInteropHelper(_window);
        _source = HwndSource.FromHwnd(helper.Handle);
        _source.AddHook(WndProc);

        _registered = RegisterHotKey(helper.Handle, HotkeyId, modifiers | MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _callback?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_registered)
        {
            var helper = new WindowInteropHelper(_window);
            UnregisterHotKey(helper.Handle, HotkeyId);
            _registered = false;
        }
        _source?.RemoveHook(WndProc);
    }
}
