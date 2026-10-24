namespace Tasky.Platform;

/// <summary>Whether someone is using the computer: input idle time, screen lock, sleep and log-off.</summary>
public interface IUserActivity : IDisposable
{
    /// <summary>Time since the last keyboard or mouse input anywhere on the computer.</summary>
    TimeSpan IdleTime { get; }

    event Action? SessionLocked;
    event Action? SessionUnlocked;

    /// <summary>The computer went to sleep; the argument is when it started sleeping.</summary>
    event Action<DateTime>? SystemSlept;

    /// <summary>The user is logging off or the computer is shutting down.</summary>
    event Action? SessionEnding;

    void Start();
}
