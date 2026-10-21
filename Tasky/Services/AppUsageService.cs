using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tasky.Services;

/// <summary>
/// Tracks foreground window changes via WinEventHook and records net active time
/// (gross time minus idle periods) per application.
///
/// Idle detection: a background timer polls every 30 s using GetLastInputInfo.
/// When the system has been idle ≥ 5 min we record the idle start (backdated to
/// the actual last-input moment). When the user comes back we backdate the idle
/// end the same way, so the accumulated idle is accurate to within a few seconds
/// regardless of polling jitter.
/// </summary>
public sealed class AppUsageService : IDisposable
{
    private readonly DatabaseService _db;
    private Thread? _thread;
    private volatile bool _running;
    private WinEventDelegate? _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _pumpThreadId;
    private System.Threading.Timer? _idleTimer;

    // All mutable state is protected by _lock.
    // _lock must never be held while calling into the DB.
    private readonly object _lock = new();
    private string? _currentApp;
    private string? _currentExe;
    private DateTime _focusStart;

    // Idle accounting for the current session
    private bool     _isIdle;
    private DateTime? _idleStartedAt;
    private double   _idleSecsAccumulated;

    private const int  MinSeconds      = 5;
    private const uint IdleThresholdMs = 5 * 60 * 1000; // 5 minutes

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

    private static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString().Trim();
    }

    private static readonly HashSet<string> IgnoredProcesses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "explorer", "searchhost", "shellexperiencehost", "systemsettings",
            "lockapp", "logonui", "dwm", "taskmgr", "applicationframehost",
            "textinputhost", "startmenuexperiencehost", "winlogon", "fontdrvhost",
            "sihost", "ctfmon", "svchost", "csrss", "smss", "wininit",
        };

    // ── P/Invoke ──────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static uint IdleMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        GetLastInputInfo(ref info);
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int count);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint id, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG { public IntPtr hwnd, wParam, lParam; public uint message, time; public int x, y; }

    private delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd,
        int obj, int child, uint thread, uint time);

    // ── Public API ────────────────────────────────────────────────────────────

    public AppUsageService(DatabaseService db) => _db = db;

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

        // Poll idle state every 30 s from the thread-pool
        _idleTimer = new System.Threading.Timer(_ => CheckIdle(), null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        _hookProc = OnWinEvent;
        _hookHandle = SetWinEventHook(
            0x0003, 0x0003,   // EVENT_SYSTEM_FOREGROUND only
            IntPtr.Zero, _hookProc,
            0, 0,
            0x0000);          // WINEVENT_OUTOFCONTEXT

        // Seed with whatever is currently in the foreground
        var initial = GetForegroundWindow();
        if (initial != IntPtr.Zero) RecordFocus(initial);

        MSG msg;
        while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
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

    // ── Idle polling (thread-pool, every 30 s) ────────────────────────────────

    private void CheckIdle()
    {
        uint idleMs   = IdleMs();
        bool nowIdle  = idleMs >= IdleThresholdMs;
        var  now      = DateTime.Now;

        lock (_lock)
        {
            if (nowIdle && !_isIdle)
            {
                // Just crossed into idle — backdate start to actual last input
                _isIdle       = true;
                _idleStartedAt = now.AddMilliseconds(-(long)idleMs);
            }
            else if (!nowIdle && _isIdle)
            {
                // Just came back — backdate end to actual last input moment
                // (GetLastInputInfo tells us the last input was idleMs ago)
                var idleEnd = now.AddMilliseconds(-(long)idleMs);
                if (_idleStartedAt.HasValue && idleEnd > _idleStartedAt.Value)
                    _idleSecsAccumulated += (idleEnd - _idleStartedAt.Value).TotalSeconds;
                _idleStartedAt = null;
                _isIdle        = false;
            }
        }
    }

    // ── WinEvent hook callback (STA pump thread) ──────────────────────────────

    private void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd,
        int obj, int child, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero) return;
        SaveCurrentSession();

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            lock (_lock) { _currentApp = null; }
            return;
        }

        RecordFocus(hwnd);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RecordFocus(IntPtr hwnd)
    {
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            var proc = Process.GetProcessById((int)pid);
            if (IgnoredProcesses.Contains(proc.ProcessName))
            {
                lock (_lock) { _currentApp = null; }
                return;
            }

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
                    friendly = dashIdx > 0 ? title.Substring(0, dashIdx).Trim() : title;
                    exePath  = "pwa://" + friendly;
                }
            }

            uint idleMs = IdleMs();
            lock (_lock)
            {
                _currentApp          = friendly;
                _currentExe          = exePath;
                _focusStart          = DateTime.Now;
                _idleSecsAccumulated = 0;
                // Inherit current idle state so we don't count AFK time
                // that was already happening when this window got focus
                _isIdle       = idleMs >= IdleThresholdMs;
                _idleStartedAt = _isIdle
                    ? DateTime.Now.AddMilliseconds(-(long)idleMs)
                    : null;
            }
        }
        catch
        {
            lock (_lock) { _currentApp = null; }
        }
    }

    /// <summary>
    /// Saves the current session with idle time deducted.
    /// Net time = gross window time − accumulated idle periods.
    /// </summary>
    private void SaveCurrentSession()
    {
        string? app;
        string  exe;
        DateTime start;
        int netSeconds;

        lock (_lock)
        {
            app = _currentApp;
            if (app is null) return;
            _currentApp = null;

            var end = DateTime.Now;

            // Count any ongoing idle period that hasn't been closed yet
            double idleSecs = _idleSecsAccumulated;
            if (_isIdle && _idleStartedAt.HasValue)
                idleSecs += (end - _idleStartedAt.Value).TotalSeconds;

            start      = _focusStart;
            exe        = _currentExe ?? string.Empty;
            netSeconds = (int)Math.Max(0, (end - start).TotalSeconds - idleSecs);

            // Reset idle state for next session
            _idleSecsAccumulated = 0;
            _idleStartedAt       = null;
            _isIdle              = false;
        }

        if (netSeconds < MinSeconds) return;

        var effectiveEnd = start.AddSeconds(netSeconds);
        try { _db.InsertAppUsageSession(app, exe, start, effectiveEnd); }
        catch { }
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _running = false;
        _idleTimer?.Dispose();
        SaveCurrentSession();
        if (_pumpThreadId != 0)
            PostThreadMessage(_pumpThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
    }
}
