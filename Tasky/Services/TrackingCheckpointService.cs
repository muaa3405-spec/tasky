using Avalonia.Threading;

namespace Tasky.Services;

/// <summary>
/// Saves running timers once a minute. If Tasky stops without warning
/// (crash, power cut, battery dies) at most one minute of tracked time is lost,
/// and the task is found paused — not still counting — on the next start.
/// </summary>
public sealed class TrackingCheckpointService : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly DispatcherTimer _timer;

    public TrackingCheckpointService(Action checkpoint)
    {
        _timer = new DispatcherTimer { Interval = Interval };
        _timer.Tick += (_, _) => checkpoint();
    }

    public void Start() => _timer.Start();

    public void Dispose() => _timer.Stop();
}
