using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Tasky.Platform.MacOS;

/// <summary>
/// Idle time from the HID event source; screen lock from the login session; sleep is
/// noticed as a gap in a short polling timer (the timer cannot tick while the Mac sleeps).
/// Log-off is handled by the app's own shutdown, so <see cref="SessionEnding"/> never fires here.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacUserActivity : IUserActivity
{
    private const string CoreGraphics   = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int  HidSystemState = 1;            // kCGEventSourceStateHIDSystemState
    private const uint AnyInputEvent  = 0xFFFFFFFF;   // kCGAnyInputEventType
    private const uint Utf8Encoding   = 0x08000100;   // kCFStringEncodingUTF8

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    /// <summary>A tick this much later than planned means the Mac was asleep in between.</summary>
    private static readonly TimeSpan SleepGap = TimeSpan.FromSeconds(60);

    [DllImport(CoreGraphics)]   private static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
    [DllImport(CoreGraphics)]   private static extern IntPtr CGSessionCopyCurrentDictionary();
    [DllImport(CoreFoundation)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);
    [DllImport(CoreFoundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [DllImport(CoreFoundation)] private static extern byte   CFBooleanGetValue(IntPtr boolean);
    [DllImport(CoreFoundation)] private static extern void   CFRelease(IntPtr value);

    private static readonly IntPtr ScreenLockedKey =
        CFStringCreateWithCString(IntPtr.Zero, "CGSSessionScreenIsLocked", Utf8Encoding);

    /// <summary>NSActivityUserInitiatedAllowingIdleSystemSleep: keeps timers on time while the window is hidden.</summary>
    private const nuint UserInitiatedActivity = 0x00EFFFFF;

    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };
    // A monotonic clock: daylight-saving or manual clock changes must not look like sleep.
    private long _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
    private IntPtr _appNapOptOut;
    private bool _locked;

    public event Action? SessionLocked;
    public event Action? SessionUnlocked;
    public event Action<DateTime>? SystemSlept;
    public event Action? SessionEnding { add { } remove { } }

    public TimeSpan IdleTime
        => TimeSpan.FromSeconds(Math.Max(0, CGEventSourceSecondsSinceLastEventType(HidSystemState, AnyInputEvent)));

    public void Start()
    {
        OptOutOfAppNap();
        _lastTick = System.Diagnostics.Stopwatch.GetTimestamp();
        _timer.Tick += OnTick;
        _timer.Start();
    }

    /// <summary>
    /// macOS slows the timers of apps whose window is hidden ("App Nap"). Tasky tracks time from the
    /// menu bar, so it opts out once; the token stays alive for the life of the process.
    /// </summary>
    private void OptOutOfAppNap()
    {
        try
        {
            var processInfo = ObjC.Send(ObjC.Class("NSProcessInfo"), "processInfo");
            var activity = ObjC.Send(processInfo, "beginActivityWithOptions:reason:",
                                     UserInitiatedActivity, ObjC.NSString("Tracking task time"));
            _appNapOptOut = ObjC.Send(activity, "retain");
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Could not opt out of App Nap", ex);
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_lastTick, now);
        if (elapsed > PollInterval + SleepGap) SystemSlept?.Invoke(DateTime.Now - elapsed);
        _lastTick = now;

        bool locked = IsScreenLocked();
        if (locked == _locked) return;
        _locked = locked;
        if (locked) SessionLocked?.Invoke(); else SessionUnlocked?.Invoke();
    }

    private static bool IsScreenLocked()
    {
        var session = CGSessionCopyCurrentDictionary();
        if (session == IntPtr.Zero) return false;
        try
        {
            var value = CFDictionaryGetValue(session, ScreenLockedKey);
            return value != IntPtr.Zero && CFBooleanGetValue(value) != 0;
        }
        finally { CFRelease(session); }
    }

    public void Dispose()
    {
        _timer.Stop();
        if (_appNapOptOut == IntPtr.Zero) return;
        ObjC.Send(ObjC.Send(ObjC.Class("NSProcessInfo"), "processInfo"), "endActivity:", _appNapOptOut);
        ObjC.Send(_appNapOptOut, "release");
        _appNapOptOut = IntPtr.Zero;
    }
}
