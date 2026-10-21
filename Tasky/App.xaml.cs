using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;
using Tasky.Services;
using Tasky.Services.Migration;
using Tasky.ViewModels;

namespace Tasky;

public partial class App : Application
{
    private const string AppUserModelId = "Nordfin.Tasky.App";

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(
        [MarshalAs(UnmanagedType.LPWStr)] string appID);

    private static SingleInstanceService? _singleInstance;
    private static TrackingCheckpointService? _checkpoints;

    public static MainViewModel? ViewModel { get; private set; }
    public static DatabaseService? Database { get; private set; }
    public static HotkeyService? Hotkey { get; private set; }
    public static ReminderSchedulerService? ReminderScheduler { get; private set; }
    public static NotificationService? Notifications { get; private set; }
    public static IdleMonitorService? IdleMonitor { get; private set; }
    public static MeetingMonitorService? MeetingMonitor { get; private set; }
    public static AppUsageService? AppUsage { get; private set; }
    public static SettingsService  Settings  { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // Errors are logged to %LocalAppData%\Tasky\logs and Tasky keeps running.
        GlobalErrorHandler.Register(this, PauseRunningTimers);

        SetCurrentProcessExplicitAppUserModelID(AppUserModelId);

        // After "Migrate to another computer" → Import, Tasky restarts itself; let the old copy exit first.
        MigrationService.WaitForPreviousInstance(e.Args);

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.IsPrimaryInstance)
        {
            // Tasky is already running (possibly hidden in the tray) — bring it forward.
            _singleInstance.SignalPrimaryInstance();
            Shutdown();
            return;
        }
        _singleInstance.ListenForActivation(() =>
            Dispatcher.BeginInvoke(() => (MainWindow as MainWindow)?.ShowFromTray()));

        base.OnStartup(e);
        var importResult = ApplyPendingImport();   // must run before anything opens the data files
        Settings.Load();

        Database = new DatabaseService();
        Database.Initialize();

        Notifications = new NotificationService(Database);
        Notifications.Initialize();

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
        IdleMonitor = new IdleMonitorService(ViewModel.PauseRunningTasks, ViewModel.ResumeActiveTask);
        IdleMonitor.Start();

        // Saves running timers every minute so a crash or power cut can't inflate them
        _checkpoints = new TrackingCheckpointService(ViewModel.CheckpointRunningTasks);
        _checkpoints.Start();

        // Meeting monitor — detects mic/camera usage and records meeting sessions
        MeetingMonitor = new MeetingMonitorService(Database);
        MeetingMonitor.Start();

        // App usage monitor — tracks foreground window time
        AppUsage = new AppUsageService(Database);
        AppUsage.Start();

        var mainWindow = new MainWindow { DataContext = ViewModel };
        MainWindow = mainWindow;
        mainWindow.Show();

        // Initialize auto theme AFTER window is created
        InitializeAutoTheme();

        mainWindow.Loaded += (_, _) =>
        {
            if (ViewModel.Tasks.Any(t => t.WasInterrupted))
                InAppNotificationService.Info("⏸  Your timer was paused because Tasky closed while it was running.");
            importResult?.Notify();

            Hotkey = new HotkeyService(mainWindow);
            Hotkey.Register(HotkeyService.MOD_WIN | HotkeyService.MOD_SHIFT, (uint)'T',
                () => Dispatcher.Invoke(ShowQuickAdd));
        };
    }

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

    private void ShowQuickAdd()
    {
        var qa = new Windows.QuickAddWindow(ViewModel!);
        qa.Show();
        qa.Activate();
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
        var themeSrc = dark ? "Resources/DarkTheme.xaml" : "Resources/LightTheme.xaml";
        var dicts = Resources.MergedDictionaries;
        dicts[0] = new ResourceDictionary { Source = new Uri(themeSrc, UriKind.Relative) };
        if (ViewModel is not null) ViewModel.IsDarkTheme = dark;
    }

    public bool IsDarkTheme => _isDark;

    private void InitializeAutoTheme()
    {
        bool windowsDark = ReadWindowsIsDark();
        if (windowsDark != _isDark)
            ApplyTheme(windowsDark);

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_userOverrodeTheme) return;

        bool windowsDark = ReadWindowsIsDark();
        if (windowsDark != _isDark)
        {
            Dispatcher.Invoke(() =>
            {
                ApplyTheme(windowsDark);
                if (MainWindow is MainWindow mw)
                {
                    mw.SyncThemeIcons(windowsDark);
                    mw.ApplyTitleBarThemePublic(windowsDark);
                }
            });
        }
    }

    private static bool ReadWindowsIsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            return val is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Pauses and saves every running timer (used on exit, log-off, shutdown and fatal errors).</summary>
    private static void PauseRunningTimers() => ViewModel?.PauseRunningTasks(DateTime.Now);

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        // Windows is logging off or shutting down — stop the clock before the process is killed.
        PauseRunningTimers();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        PauseRunningTimers();
        _checkpoints?.Dispose();
        AppUsage?.Dispose();
        MeetingMonitor?.Dispose();
        IdleMonitor?.Dispose();
        ReminderScheduler?.Stop();
        Notifications?.Cleanup();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
