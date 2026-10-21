using System.Runtime.InteropServices;
using System.Threading;

namespace Tasky.Services;

/// <summary>
/// Ensures only one Tasky process runs. A second launch signals the running
/// instance (so it can bring its window forward) and then exits quietly.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexName       = "Tasky_SingleInstance_Mutex_v1";
    private const string ActivateEventName = "Tasky_SingleInstance_Activate_v1";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activateEvent;
    private readonly bool _ownsMutex;
    private RegisteredWaitHandle? _activateRegistration;

    public SingleInstanceService()
    {
        _mutex = new Mutex(true, MutexName, out _ownsMutex);
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
    }

    /// <summary>True when this process is the first (primary) instance.</summary>
    public bool IsPrimaryInstance => _ownsMutex;

    /// <summary>Asks the already-running instance to show its main window.</summary>
    public void SignalPrimaryInstance()
    {
        // This launch holds foreground rights (the user just started it);
        // hand them over so the primary instance may bring itself to front.
        AllowSetForegroundWindow(ASFW_ANY);
        _activateEvent.Set();
    }

    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    /// <summary>
    /// Invokes <paramref name="onActivateRequested"/> (on a thread-pool thread)
    /// every time a later launch calls <see cref="SignalPrimaryInstance"/>.
    /// </summary>
    public void ListenForActivation(Action onActivateRequested)
    {
        _activateRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent, (_, _) => onActivateRequested(), null,
            Timeout.Infinite, executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _activateRegistration?.Unregister(null);
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _activateEvent.Dispose();
    }
}
