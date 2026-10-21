using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Tasky.Services;

/// <summary>
/// Pauses the running task when the computer is not being used:
///   • idle (no keyboard/mouse) for 5 minutes — resumes when activity returns;
///   • session locked — resumes on unlock;
///   • sleep / hibernate — stays paused after wake-up (resume it yourself).
/// A task the user paused by hand is never resumed automatically.
/// </summary>
public sealed class IdleMonitorService : IDisposable
{
    // ─── Win32 idle-time API ──────────────────────────────────────────────
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static TimeSpan IdleTime()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        GetLastInputInfo(ref info);
        return TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
    }

    // ─── Configuration ────────────────────────────────────────────────────
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PollInterval  = TimeSpan.FromSeconds(30);

    // ─── State ────────────────────────────────────────────────────────────
    private readonly Func<DateTime, bool> _pauseRunning;   // returns true if something was paused
    private readonly Action _resumeActive;
    private readonly DispatcherTimer _timer;

    /// <summary>True only when this monitor paused a running task (so it may resume it).</summary>
    private bool _isAutoPaused;

    /// <param name="pauseRunning">Pauses running tasks, counting time up to the given moment.</param>
    /// <param name="resumeActive">Resumes the active task.</param>
    public IdleMonitorService(Func<DateTime, bool> pauseRunning, Action resumeActive)
    {
        _pauseRunning = pauseRunning;
        _resumeActive = resumeActive;
        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += OnTimerTick;
        SystemEvents.SessionSwitch    += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start() => _timer.Start();

    public void Stop()  => _timer.Stop();

    // ─── Idle check ───────────────────────────────────────────────────────

    private void OnTimerTick(object? sender, EventArgs e)
    {
        var idle = IdleTime();
        if (idle >= IdleThreshold)
        {
            // Count work only up to the last keyboard/mouse input.
            if (!_isAutoPaused) TryAutoPause(DateTime.Now - idle, "Computer idle for 5 minutes");
        }
        else if (_isAutoPaused)
        {
            AutoResume();
        }
    }

    // ─── Lock / unlock ────────────────────────────────────────────────────

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            if (!_isAutoPaused) TryAutoPause(DateTime.Now, "Computer locked");
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock
                           or SessionSwitchReason.ConsoleConnect)
        {
            if (_isAutoPaused) AutoResume();
        }
    }

    // ─── Sleep / hibernate ────────────────────────────────────────────────

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Suspend) return;
        OnUiThread(() =>
        {
            _pauseRunning(DateTime.Now);
            // After a sleep the user decides when work starts again.
            _isAutoPaused = false;
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void TryAutoPause(DateTime pausedAt, string reason)
    {
        OnUiThread(() =>
        {
            _isAutoPaused = _pauseRunning(pausedAt);
            if (_isAutoPaused)
                InAppNotificationService.Warning($"⏸  Timer paused — {reason}.");
        });
    }

    private void AutoResume()
    {
        _isAutoPaused = false;
        OnUiThread(() =>
        {
            _resumeActive();
            InAppNotificationService.Info("▶  Timer resumed — you're back!");
        });
    }

    private static void OnUiThread(Action action)
        => System.Windows.Application.Current?.Dispatcher.Invoke(action);

    // ─── Disposal ─────────────────────────────────────────────────────────

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.SessionSwitch    -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
