using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Tasky.Platform;
using Tasky.Services;
using Tasky.Services.Migration;
using Tasky.ViewModels;

namespace Tasky;

public partial class App : Application
{
    private static SingleInstanceService? _singleInstance;
    private static TrackingCheckpointService? _checkpoints;
    private static IUserActivity? _userActivity;

    public static App Instance => (App)Current!;

    public static MainViewModel? ViewModel { get; private set; }
    public static DatabaseService? Database { get; private set; }
    public static IGlobalHotkey? Hotkey { get; private set; }
    public static ReminderSchedulerService? ReminderScheduler { get; private set; }
    public static NotificationService? Notifications { get; private set; }
    public static IdleMonitorService? IdleMonitor { get; private set; }
    public static MeetingMonitorService? MeetingMonitor { get; private set; }
    public static AppUsageService? AppUsage { get; private set; }
    public static SettingsService  Settings  { get; } = new();
    public static MainWindow? MainAppWindow { get; private set; }

    private IClassicDesktopStyleApplicationLifetime Desktop
        => (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var args = Desktop.Args ?? [];

        // Errors are logged to the Tasky logs folder and Tasky keeps running.
        GlobalErrorHandler.Register(PauseRunningTimers);

        // After "Migrate to another computer" → Import, Tasky restarts itself; let the old copy exit first.
        MigrationService.WaitForPreviousInstance(args);

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            // Tasky is already running (possibly hidden in the tray) — bring it forward.
            _singleInstance.SignalPrimaryInstance();
            Desktop.Shutdown();
            return;
        }
        _singleInstance.ListenForActivation(() =>
            Dispatcher.UIThread.Post(() => MainAppWindow?.ShowFromTray()));

        var importResult = ApplyPendingImport();   // must run before anything opens the data files
        Settings.Load();

        Database = new DatabaseService();
        Database.Initialize();

        Notifications = new NotificationService(Database);

        ViewModel = new MainViewModel(Database);
        ViewModel.PurgeExpiredDeletedTasks();   // drop recycle-bin items older than 30 days
        ViewModel.DeleteOrphanAttachmentFolders();
        ViewModel.LoadTasks();
        ViewModel.LoadDeletedTasks();
        ViewModel.LoadGroups();
        ViewModel.LoadNotes();

        ReminderScheduler = new ReminderSchedulerService(Database, Notifications);
        ReminderScheduler.Start();

        // Idle / lock / sleep monitor — auto-pauses the running task
        _userActivity = PlatformServices.CreateUserActivity();
        _userActivity.SessionEnding += PauseRunningTimers;   // log-off / shutdown: stop the clock first
        IdleMonitor = new IdleMonitorService(_userActivity, ViewModel.PauseRunningTasks, ViewModel.ResumeActiveTask);
        _userActivity.Start();
        IdleMonitor.Start();

        // Saves running timers every minute so a crash or power cut can't inflate them
        _checkpoints = new TrackingCheckpointService(ViewModel.CheckpointRunningTasks);
        _checkpoints.Start();

        // Meeting monitor — detects mic/camera usage and records meeting sessions (Windows only)
        if (PlatformServices.SupportsMeetingDetection && OperatingSystem.IsWindows())
        {
            MeetingMonitor = new MeetingMonitorService(Database);
            MeetingMonitor.Start();
        }

        // App usage monitor — tracks foreground window time
        AppUsage = new AppUsageService(Database, PlatformServices.CreateForegroundAppMonitor(), _userActivity);
        AppUsage.Start();

        var mainWindow = new MainWindow { DataContext = ViewModel };
        MainAppWindow = mainWindow;
        Desktop.MainWindow = mainWindow;
        Desktop.ShutdownRequested += (_, _) => PauseRunningTimers();   // e.g. macOS log-off or Quit
        Desktop.Exit += (_, _) => OnExit();
        WatchDockReopen();

        void OnFirstOpened(object? sender, EventArgs e)
        {
            mainWindow.Opened -= OnFirstOpened;   // Opened repeats each time the window is shown from the tray
            if (ViewModel!.Tasks.Any(t => t.WasInterrupted))
                InAppNotificationService.Info("⏸  Your timer was paused because Tasky closed while it was running.");
            importResult?.Notify();

            Hotkey = PlatformServices.CreateQuickAddHotkey();
            Hotkey.Register(ShowQuickAdd);
        }
        mainWindow.Opened += OnFirstOpened;
        mainWindow.Show();

        // Initialize auto theme AFTER window is created
        InitializeAutoTheme();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Ends the app (tray "Exit Tasky", restart after an import).</summary>
    public static void Shutdown() => Instance.Desktop.Shutdown();

    /// <summary>Outcome of a migration import applied at startup, reported once the window is up.</summary>
    private sealed record ImportOutcome(bool Succeeded)
    {
        public void Notify()
        {
            if (Succeeded)
                InAppNotificationService.Success("✔  Your Tasky data has been imported. Welcome back!");
            else
                InAppNotificationService.Error($"Import failed, so your existing data was kept. Details: {CrashLog.CurrentFile}");
        }
    }

    private static ImportOutcome? ApplyPendingImport()
    {
        try
        {
            return MigrationService.ApplyPendingImport() ? new ImportOutcome(true) : null;
        }
        catch
        {
            return new ImportOutcome(false);   // already logged; current data restored
        }
    }

    public static void ShowQuickAdd()
    {
        var qa = new Windows.QuickAddWindow(ViewModel!);
        qa.Show();
        qa.Activate();
    }

    /// <summary>macOS: clicking the Dock icon while the window is hidden brings it back.</summary>
    private void WatchDockReopen()
    {
        if (this.TryGetFeature<IActivatableLifetime>() is not { } activatable) return;
        activatable.Activated += (_, e) =>
        {
            if (e.Kind == ActivationKind.Reopen) MainAppWindow?.ShowFromTray();
        };
    }

    // ─── Theme ────────────────────────────────────────────────────────────

    private bool _isDark = true;
    private bool _userOverrodeTheme = false;

    public void ToggleTheme()
    {
        _userOverrodeTheme = true;
        _isDark = !_isDark;
        ApplyTheme(_isDark);
    }

    public void ApplyTheme(bool dark)
    {
        _isDark = dark;
        RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (ViewModel is not null) ViewModel.IsDarkTheme = dark;
    }

    public bool IsDarkTheme => _isDark;

    private void InitializeAutoTheme()
    {
        bool systemDark = ReadSystemIsDark();
        if (systemDark != _isDark)
            ApplyTheme(systemDark);

        if (PlatformSettings is { } settings) settings.ColorValuesChanged += OnSystemColorsChanged;
    }

    private void OnSystemColorsChanged(object? sender, PlatformColorValues e)
    {
        if (_userOverrodeTheme) return;

        bool systemDark = e.ThemeVariant == PlatformThemeVariant.Dark;
        if (systemDark == _isDark) return;
        Dispatcher.UIThread.Post(() =>
        {
            ApplyTheme(systemDark);
            MainAppWindow?.SyncThemeIcons(systemDark);
            MainAppWindow?.ApplyTitleBarThemePublic(systemDark);
        });
    }

    /// <summary>The OS app-mode setting (Windows "Choose your app mode", macOS Appearance).</summary>
    private bool ReadSystemIsDark()
        => PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark;

    // ─── Shutdown ─────────────────────────────────────────────────────────

    /// <summary>Pauses and saves every running timer (used on exit, log-off, shutdown and fatal errors).</summary>
    private static void PauseRunningTimers() => ViewModel?.PauseRunningTasks(DateTime.Now);

    private static void OnExit()
    {
        PauseRunningTimers();
        _checkpoints?.Dispose();
        AppUsage?.Dispose();
        if (OperatingSystem.IsWindows()) MeetingMonitor?.Dispose();
        IdleMonitor?.Dispose();
        _userActivity?.Dispose();
        Hotkey?.Dispose();
        ReminderScheduler?.Stop();
        MainAppWindow?.DisposeTray();
        _singleInstance?.Dispose();
    }
}
