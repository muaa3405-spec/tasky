using Tasky.Platform;

namespace Tasky.Services;

/// <summary>
/// Records net active time (gross time minus idle periods) per application,
/// as reported by the platform's foreground-app monitor.
///
/// Idle detection: a background timer polls every 30 s. When the computer has
/// been idle ≥ 5 min we record the idle start (backdated to the actual
/// last-input moment). When the user comes back we backdate the idle end the
/// same way, so the accumulated idle is accurate to within a few seconds
/// regardless of polling jitter.
/// </summary>
public sealed class AppUsageService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly IForegroundAppMonitor _monitor;
    private readonly IUserActivity _activity;
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

    private const int MinSeconds = 5;
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdlePoll      = TimeSpan.FromSeconds(30);

    public AppUsageService(DatabaseService db, IForegroundAppMonitor monitor, IUserActivity activity)
    {
        _db       = db;
        _monitor  = monitor;
        _activity = activity;
    }

    public void Start()
    {
        _idleTimer = new System.Threading.Timer(_ => CheckIdle(), null, IdlePoll, IdlePoll);
        _monitor.ForegroundChanged += OnForegroundChanged;
        _monitor.Start();
    }

    // ── Idle polling (thread-pool, every 30 s) ────────────────────────────────

    private void CheckIdle()
    {
        var  idle    = _activity.IdleTime;
        bool nowIdle = idle >= IdleThreshold;
        var  now     = DateTime.Now;

        lock (_lock)
        {
            if (nowIdle && !_isIdle)
            {
                // Just crossed into idle — backdate start to actual last input
                _isIdle        = true;
                _idleStartedAt = now - idle;
            }
            else if (!nowIdle && _isIdle)
            {
                // Just came back — backdate end to the actual last input moment
                var idleEnd = now - idle;
                if (_idleStartedAt.HasValue && idleEnd > _idleStartedAt.Value)
                    _idleSecsAccumulated += (idleEnd - _idleStartedAt.Value).TotalSeconds;
                _idleStartedAt = null;
                _isIdle        = false;
            }
        }
    }

    // ── Foreground changes (monitor thread) ───────────────────────────────────

    private void OnForegroundChanged(ForegroundApp? app)
    {
        SaveCurrentSession();
        if (app is not null) RecordFocus(app);
    }

    private void RecordFocus(ForegroundApp app)
    {
        var idle = _activity.IdleTime;
        lock (_lock)
        {
            _currentApp          = app.Name;
            _currentExe          = app.Location;
            _focusStart          = DateTime.Now;
            _idleSecsAccumulated = 0;
            // Inherit current idle state so we don't count AFK time
            // that was already happening when this window got focus
            _isIdle        = idle >= IdleThreshold;
            _idleStartedAt = _isIdle ? DateTime.Now - idle : null;
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
        _monitor.ForegroundChanged -= OnForegroundChanged;
        _monitor.Dispose();
        _idleTimer?.Dispose();
        SaveCurrentSession();
    }
}
