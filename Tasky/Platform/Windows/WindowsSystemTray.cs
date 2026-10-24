using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Platform;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>
/// Notification-area icon built directly on Shell_NotifyIcon, so Windows keeps
/// the double-click restore and the balloon tips Tasky has always shown.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsSystemTray : ISystemTray
{
    private const uint IconId          = 1;
    private const uint CallbackMessage = WM_APP + 1;
    private const uint FirstMenuId     = 1000;
    private static readonly Uri IconUri = new("avares://Tasky/Assets/icon.ico");

    private readonly Win32MessageWindow _window;
    private readonly uint _taskbarCreatedMessage;
    private readonly System.Drawing.Icon? _icon;
    private IReadOnlyList<TrayMenuItem> _menu = [];
    private string _tooltip = string.Empty;
    private bool _added;

    public event Action? OpenRequested;

    public WindowsSystemTray(Win32MessageWindow window)
    {
        _window = window;
        _icon   = LoadSmallIcon();
        // Explorer restarts (e.g. after a crash) forget every tray icon; this message asks us to re-add ours.
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _window.MessageReceived += OnMessage;
    }

    public void Show(string tooltip, IReadOnlyList<TrayMenuItem> menu)
    {
        _tooltip = tooltip;
        _menu    = menu;
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added   = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    public void ShowBalloon(string title, string message, TimeSpan duration)
    {
        if (!_added) return;
        var data = NewData(NIF_INFO);
        data.szInfoTitle       = title;
        data.szInfo            = message;
        data.dwInfoFlags       = NIIF_INFO;
        data.uTimeoutOrVersion = (uint)duration.TotalMilliseconds;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private bool OnMessage(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == _taskbarCreatedMessage)
        {
            Show(_tooltip, _menu);
            return true;
        }
        if (msg != CallbackMessage) return false;

        switch ((uint)lParam.ToInt64() & 0xFFFF)
        {
            case WM_LBUTTONDBLCLK:
            case NIN_BALLOONUSERCLICK:
                OpenRequested?.Invoke();
                break;
            case WM_RBUTTONUP:
                ShowMenu();
                break;
        }
        return true;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            for (int i = 0; i < _menu.Count; i++)
            {
                var item = _menu[i];
                if (item.Text is null) AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                else AppendMenu(menu, MF_STRING, (UIntPtr)(FirstMenuId + i), item.Text);
            }

            GetCursorPos(out var pt);
            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_window.Handle);
            int chosen = TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _window.Handle, IntPtr.Zero);
            PostMessage(_window.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            int index = chosen - (int)FirstMenuId;
            if (chosen > 0 && index >= 0 && index < _menu.Count) _menu[index].Click?.Invoke();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize           = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd             = _window.Handle,
        uID              = IconId,
        uFlags           = flags,
        uCallbackMessage = CallbackMessage,
        hIcon            = _icon?.Handle ?? IntPtr.Zero,
        szTip            = _tooltip,
        szInfo           = string.Empty,
        szInfoTitle      = string.Empty
    };

    private static System.Drawing.Icon? LoadSmallIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(IconUri);
            return new System.Drawing.Icon(stream, GetSystemMetrics(SM_CXSMICON), GetSystemMetrics(SM_CYSMICON));
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Could not load the tray icon", ex);
            return null;
        }
    }

    public void Dispose()
    {
        _window.MessageReceived -= OnMessage;
        if (_added)
        {
            var data = NewData(0);
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        _icon?.Dispose();
    }
}
