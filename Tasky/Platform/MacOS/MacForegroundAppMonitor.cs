using System.Runtime.Versioning;
using Avalonia.Threading;

namespace Tasky.Platform.MacOS;

/// <summary>Polls NSWorkspace for the frontmost application once a second.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacForegroundAppMonitor : IForegroundAppMonitor
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    // System surfaces that are not "working in an app" (mirrors the Windows shell list)
    private static readonly HashSet<string> IgnoredBundles = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.apple.loginwindow", "com.apple.ScreenSaver.Engine", "com.apple.dock",
        "com.apple.notificationcenterui", "com.apple.controlcenter", "com.apple.Spotlight",
        "com.apple.systemuiserver", "com.apple.WindowManager",
    };

    private readonly DispatcherTimer _timer = new() { Interval = PollInterval };
    private int _lastPid = -1;

    public event Action<ForegroundApp?>? ForegroundChanged;

    public void Start()
    {
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
        Poll();
    }

    private void Poll()
    {
        try
        {
            var change = ObjC.WithPool(() =>
            {
                var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), "sharedWorkspace");
                var app       = ObjC.Send(workspace, "frontmostApplication");
                int pid       = app == IntPtr.Zero ? 0 : ObjC.SendInt(app, "processIdentifier");
                if (pid == _lastPid) return (Changed: false, App: (ForegroundApp?)null);
                _lastPid = pid;
                return (Changed: true, App: Describe(app, pid));
            });
            if (change.Changed) ForegroundChanged?.Invoke(change.App);
        }
        catch (Exception ex)
        {
            _timer.Stop();   // the API is unavailable; stop instead of failing every second
            Services.CrashLog.Write("App usage tracking stopped", ex);
        }
    }

    private static ForegroundApp? Describe(IntPtr app, int pid)
    {
        if (app == IntPtr.Zero || pid == Environment.ProcessId) return null;

        var bundleId = ObjC.ToManagedString(ObjC.Send(app, "bundleIdentifier"));
        if (bundleId is not null && IgnoredBundles.Contains(bundleId)) return null;

        var name = ObjC.ToManagedString(ObjC.Send(app, "localizedName"));
        if (string.IsNullOrWhiteSpace(name)) return null;

        var bundleUrl = ObjC.Send(app, "bundleURL");
        var path      = bundleUrl == IntPtr.Zero ? null : ObjC.ToManagedString(ObjC.Send(bundleUrl, "path"));
        return new ForegroundApp(name, path ?? string.Empty);
    }

    public void Dispose() => _timer.Stop();
}
