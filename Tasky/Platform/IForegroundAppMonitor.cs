namespace Tasky.Platform;

/// <summary>Reports which application the user is working in.</summary>
public interface IForegroundAppMonitor : IDisposable
{
    /// <summary>
    /// Raised (on any thread) when another application comes to the front.
    /// Null means "nothing worth tracking" — Tasky itself, the desktop or a system screen.
    /// </summary>
    event Action<ForegroundApp?>? ForegroundChanged;

    void Start();
}

/// <param name="Name">Friendly application name shown in the App Usage panel.</param>
/// <param name="Location">Executable or app-bundle path used to find the icon (may be a "pwa://" marker).</param>
public sealed record ForegroundApp(string Name, string Location);
