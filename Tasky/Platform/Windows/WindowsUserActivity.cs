using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Tasky.Platform.Windows;

/// <summary>Idle time from GetLastInputInfo; lock, sleep and log-off from Windows session events.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsUserActivity : IUserActivity
{
    public event Action? SessionLocked;
    public event Action? SessionUnlocked;
    public event Action<DateTime>? SystemSlept;
    public event Action? SessionEnding;

    public TimeSpan IdleTime => TimeSpan.FromMilliseconds(NativeMethods.IdleMilliseconds());

    public void Start()
    {
        SystemEvents.SessionSwitch    += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.SessionEnding    += OnSessionEnding;
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
            SessionLocked?.Invoke();
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.ConsoleConnect)
            SessionUnlocked?.Invoke();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) SystemSlept?.Invoke(DateTime.Now);
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e) => SessionEnding?.Invoke();

    public void Dispose()
    {
        SystemEvents.SessionSwitch    -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionEnding    -= OnSessionEnding;
    }
}
