using System.Diagnostics;
using System.Runtime.Versioning;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>
/// Follows foreground-window changes through a WinEventHook on its own
/// message-pump thread, and names the application behind each window.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsForegroundAppMonitor : IForegroundAppMonitor
{
    private Thread? _thread;
    private volatile bool _running;
    private WinEventDelegate? _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _pumpThreadId;

    public event Action<ForegroundApp?>? ForegroundChanged;

    // Browser process name → title suffix used for regular tabs.
    // If the window title does NOT end with this suffix the window is a PWA,
    // and we use the window title itself as the app name.
    private static readonly Dictionary<string, string> BrowserSuffixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["msedge"]  = "- Microsoft Edge",
            ["chrome"]  = "- Google Chrome",
            ["firefox"] = "- Mozilla Firefox",
            ["brave"]   = "- Brave",
            ["opera"]   = "- Opera",
            ["vivaldi"] = "- Vivaldi",
        };

    private static readonly HashSet<string> IgnoredProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "searchhost", "shellexperiencehost", "systemsettings",
            "lockapp", "logonui", "dwm", "taskmgr", "applicationframehost",
            "textinputhost", "startmenuexperiencehost", "winlogon", "fontdrvhost",
            "sihost", "ctfmon", "svchost", "csrss", "smss", "wininit",
        };

    public void Start()
    {
        _running = true;
        _thread  = new Thread(RunPump) { IsBackground = true, Name = "AppUsage" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    // ── Message-pump thread ───────────────────────────────────────────────────

    private void RunPump()
    {
        _pumpThreadId = GetCurrentThreadId();

        _hookProc   = OnWinEvent;
        _hookHandle = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
                                      IntPtr.Zero, _hookProc, 0, 0, WINEVENT_OUTOFCONTEXT);

        // Seed with whatever is currently in the foreground
        var initial = GetForegroundWindow();
        if (initial != IntPtr.Zero) ForegroundChanged?.Invoke(Describe(initial));

        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        ForegroundChanged?.Invoke(Describe(hwnd));
    }

    // ── Naming the application ────────────────────────────────────────────────

    /// <summary>The app behind a window, or null for Tasky itself and Windows shell windows.</summary>
    private static ForegroundApp? Describe(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == (uint)Environment.ProcessId) return null;

            using var proc = Process.GetProcessById((int)pid);
            if (IgnoredProcesses.Contains(proc.ProcessName)) return null;

            string friendly = proc.ProcessName;
            string exePath  = string.Empty;
            try
            {
                var module = proc.MainModule;
                exePath = module?.FileName ?? string.Empty;
                var fvi = module?.FileVersionInfo;
                if (!string.IsNullOrWhiteSpace(fvi?.ProductName))
                    friendly = fvi.ProductName.Trim();
            }
            catch { }

            // Detect PWAs installed from browsers: they run as msedge.exe / chrome.exe
            // but their window title is just the app name, not "Page - Browser Name".
            if (BrowserSuffixes.TryGetValue(proc.ProcessName, out var browserSuffix))
            {
                var title = GetWindowTitle(hwnd);
                if (!string.IsNullOrWhiteSpace(title) &&
                    !title.EndsWith(browserSuffix, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(title, friendly, StringComparison.OrdinalIgnoreCase))
                {
                    // Window title doesn't carry the browser suffix → it's a PWA.
                    // Strip page-specific suffix (e.g. "ChatGPT - Some Page" → "ChatGPT")
                    // so all sessions for the same PWA are grouped together.
                    var dashIdx = title.IndexOf(" - ", StringComparison.Ordinal);
                    friendly = dashIdx > 0 ? title[..dashIdx].Trim() : title;
                    exePath  = WindowsAppIconProvider.PwaPrefix + friendly;
                }
            }

            return new ForegroundApp(friendly, exePath);
        }
        catch
        {
            return null;
        }
    }

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    public void Dispose()
    {
        _running = false;
        if (_pumpThreadId != 0)
            PostThreadMessage(_pumpThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }
}
