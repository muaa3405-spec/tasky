using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Tasky.Behaviors;
using Tasky.Dialogs;
using Tasky.Helpers;
using Tasky.Models;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky;

public partial class MainWindow : System.Windows.Window
{
    private NotifyIcon? _trayIcon;
    private bool _addPanelOpen;
    private string _completedGroupFilter = "All";
    private string _topTasksFilter = "overall";
    private bool _addNotePanelOpen;
    private int _statsWeekOffset = 0;
    private bool _weeklyChartShowTime = true;
    private string _meetingFilter = "week";
    private string _appUsageFilter = "today";

    // Pending attachments buffered while the Add Task form is open
    private readonly List<(byte[] Data, string Ext)> _pendingAttachBytes = new();
    private readonly List<string>                    _pendingAttachPaths  = new();
    private MainViewModel Vm => (MainViewModel)DataContext;

    // Navigation

    private enum AppPage { Main, Completed, Notes, Stats, Reminders, Settings, Trash }

    private void NavigateTo(AppPage page)
    {
        TaskListPage.Visibility  = page == AppPage.Main      ? Visibility.Visible : Visibility.Collapsed;
        CompletedPage.Visibility = page == AppPage.Completed ? Visibility.Visible : Visibility.Collapsed;
        NotesPage.Visibility     = page == AppPage.Notes     ? Visibility.Visible : Visibility.Collapsed;
        StatsPage.Visibility     = page == AppPage.Stats     ? Visibility.Visible : Visibility.Collapsed;
        RemindersPage.Visibility = page == AppPage.Reminders ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility  = page == AppPage.Settings  ? Visibility.Visible : Visibility.Collapsed;
        TrashPage.Visibility     = page == AppPage.Trash     ? Visibility.Visible : Visibility.Collapsed;
        AddPanelBorder.Visibility= page == AppPage.Main      ? Visibility.Visible : Visibility.Collapsed;
        MainHeader.Visibility    = page == AppPage.Main      ? Visibility.Visible : Visibility.Collapsed;
        SubHeader.Visibility     = page != AppPage.Main      ? Visibility.Visible : Visibility.Collapsed;

        EmptyBinBtn.Visibility   = page == AppPage.Trash ? Visibility.Visible : Visibility.Collapsed;
        NewNoteBtn.Visibility    = page == AppPage.Notes ? Visibility.Visible : Visibility.Collapsed;

        SubPageTitle.Text = page switch
        {
            AppPage.Completed => "☑  Completed Tasks",
            AppPage.Notes     => "\U0001F4DD  Notes",
            AppPage.Stats     => "Stats",
            AppPage.Reminders => "\U0001F514  Reminders",
            AppPage.Settings  => "\u2699  Settings",
            AppPage.Trash     => "\U0001F5D1  Recycle Bin",
            _                 => ""
        };

        if (page == AppPage.Trash)     RefreshTrashList();
        if (page == AppPage.Completed) RefreshCompletedList();
        if (page == AppPage.Notes)     { Vm.LoadNotes(); RefreshNotesEmpty(); }
        if (page == AppPage.Stats)     { Vm.RefreshStats(_statsWeekOffset); BuildStatsUI(); }
        if (page == AppPage.Reminders)
        {
            NotifPanel.Visibility    = Visibility.Visible;
            UpcomingPanel.Visibility = Visibility.Collapsed;
            NotifTabBtn.IsChecked    = true;
            UpcomingTabBtn.IsChecked = false;
            RefreshRemindersPage();
        }
        if (page == AppPage.Settings) LoadSettingsPage();

        AnimatePageIn(page);
    }

    private void AnimatePageIn(AppPage page)
    {
        UIElement? target = page switch
        {
            AppPage.Completed => CompletedPage,
            AppPage.Notes     => NotesPage,
            AppPage.Stats     => StatsPage,
            AppPage.Reminders => RemindersPage,
            AppPage.Settings  => SettingsPage,
            AppPage.Trash     => TrashPage,
            AppPage.Main      => TaskListPage,
            _                 => null
        };
        if (target is null || target.Visibility != Visibility.Visible) return;

        var tt   = new TranslateTransform(40, 0);
        target.RenderTransform = tt;
        target.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var sb   = new Storyboard();

        var slide = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease };
        Storyboard.SetTarget(slide, target);
        Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.(TranslateTransform.X)"));

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease };
        Storyboard.SetTarget(fade, target);
        Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));

        sb.Children.Add(slide);
        sb.Children.Add(fade);
        sb.Begin();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        Vm.CompletedSearch = string.Empty;
        Vm.NoteSearch      = string.Empty;
        Vm.TaskSearch      = string.Empty;
        _addNotePanelOpen  = false;
        if (AddNotePanel != null) AddNotePanel.Visibility = Visibility.Collapsed;
        NavigateTo(AppPage.Main);
    }

    // DWM dark title bar

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_CAPTION_COLOR           = 35;  // Windows 11+

    // Dark theme header: #1F2937  Light theme header: #F3F4F6
    // COLORREF = 0x00BBGGRR (BGR order)
    private static int HexToColorRef(string hex)
    {
        hex = hex.TrimStart('#');
        int r = Convert.ToInt32(hex.Substring(0, 2), 16);
        int g = Convert.ToInt32(hex.Substring(2, 2), 16);
        int b = Convert.ToInt32(hex.Substring(4, 2), 16);
        return (b << 16) | (g << 8) | r;
    }

    private void ApplyTitleBarTheme(bool dark)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        // Dark mode flag
        int darkVal = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

        // Caption (title bar) background — matches app header colour
        int captionColor = HexToColorRef(dark ? "#111827" : "#E5E7EB");
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));
    }

    public void ApplyTitleBarThemePublic(bool dark) => ApplyTitleBarTheme(dark);

    public void SyncThemeIcons(bool dark)
    {
        if (SubThemeIcon != null) SubThemeIcon.Text = dark ? "☀️" : "\U0001F319";
    }

    public MainWindow()
    {
        InitializeComponent();
        InitTrayIcon();
        SourceInitialized += (_, _) =>
        {
            bool dark = ((App)System.Windows.Application.Current).IsDarkTheme;
            ApplyTitleBarTheme(dark);
            SyncThemeIcons(dark);
        };
        Loaded += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.CompletedSearch) &&
                        CompletedPage.Visibility == Visibility.Visible)
                        RefreshCompletedList();
                    if (e.PropertyName == nameof(MainViewModel.NoteSearch) &&
                        NotesPage.Visibility == Visibility.Visible)
                        RefreshNotesEmpty();
                };
                vm.LoadGroups();
                BuildGroupChips();
                PopulateNewTaskGroupPicker();
                vm.RefreshMissedNotifCount();
            }

            // Auto-refresh every 10 minutes so due-date labels (Today/Tomorrow/Yesterday)
            // and sort order stay accurate without needing to restart the app.
            var autoRefresh = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMinutes(10) };
            autoRefresh.Tick += (_, _) => AutoRefresh();
            autoRefresh.Start();

            // Apply calendar colour fixes to all DatePickers in this window
            Tasky.Helpers.DatePickerHelper.Subscribe(CompletedDatePicker);
        };
    }

    // ─── Auto-refresh (every 10 min) ─────────────────────────────────────────
    // Keeps due-date labels (Today/Tomorrow/Yesterday) and the date filters
    // current. Tasks are refreshed in place — never reloaded — so running
    // timers, the widget and any open dialog keep working on the same objects.
    private void AutoRefresh()
    {
        if (DataContext is not MainViewModel vm) return;

        vm.PurgeExpiredDeletedTasks();   // honour the 30-day recycle-bin retention
        vm.RefreshTimeSensitiveLabels();
        vm.RefreshMissedNotifCount();

        // Keep the new-task date picker current across midnight
        vm.NewDueDate = DateTime.Today;

        // If a page that shows time-sensitive data is open, refresh it too
        if (TrashPage.Visibility      == Visibility.Visible) { vm.LoadDeletedTasks(); RefreshTrashList(); }
        if (CompletedPage.Visibility  == Visibility.Visible) RefreshCompletedList();
        if (StatsPage.Visibility      == Visibility.Visible) { vm.RefreshStats(_statsWeekOffset); BuildStatsUI(); }
        if (RemindersPage.Visibility  == Visibility.Visible) RefreshRemindersPage();
    }

    // Group chips

    private void BuildGroupChips()
    {
        GroupChipsPanel.Children.Clear();

        var allChip = new RadioButton
        {
            Content   = "All",
            Style     = (Style)FindResource("FilterChip"),
            GroupName = "GF",
            IsChecked = Vm.SelectedGroupFilter == null,
            Margin    = new Thickness(0, 0, 5, 0)
        };
        allChip.Click += (_, _) => Vm.SelectedGroupFilter = null;
        GroupChipsPanel.Children.Add(allChip);

        foreach (var g in Vm.Groups)
        {
            // Build chip content: coloured 8px dot + group name
            System.Windows.Media.Color dotColor;
            try { dotColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(g.Color); }
            catch { dotColor = System.Windows.Media.Color.FromRgb(107, 114, 128); }

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width  = 8, Height = 8,
                Fill   = new System.Windows.Media.SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new System.Windows.Controls.TextBlock
            {
                Text = g.Name,
                VerticalAlignment = VerticalAlignment.Center
            };
            var content = new System.Windows.Controls.StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { dot, label }
            };

            var chip = new RadioButton
            {
                Content   = content,
                Style     = (Style)FindResource("FilterChip"),
                GroupName = "GF",
                IsChecked = Vm.SelectedGroupFilter == g.Id,
                Margin    = new Thickness(0, 0, 5, 0),
                Tag       = g.Id
            };
            int gid = g.Id;
            chip.Click += (_, _) => Vm.SelectedGroupFilter = gid;
            GroupChipsPanel.Children.Add(chip);
        }

        GroupFilterScroll.Visibility = Vm.Groups.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PopulateNewTaskGroupPicker()
    {
        if (NewTaskGroupPicker is null) return;
        NewTaskGroupPicker.Items.Clear();

        int selectIndex = 0;
        int i = 0;
        foreach (var g in Vm.Groups)
        {
            System.Windows.Media.Color dotColor;
            try { dotColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(g.Color); }
            catch { dotColor = System.Windows.Media.Color.FromRgb(107, 114, 128); }

            var dot = new System.Windows.Shapes.Ellipse
            {
                Width  = 8, Height = 8,
                Fill   = new System.Windows.Media.SolidColorBrush(dotColor),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock { Text = g.Name, VerticalAlignment = VerticalAlignment.Center };
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(dot);
            panel.Children.Add(label);

            NewTaskGroupPicker.Items.Add(new ComboBoxItem { Content = panel, Tag = (int?)g.Id });

            // Pre-select whichever group matches the VM default (set by LoadGroups)
            if ((int?)g.Id == Vm.NewTaskGroupId) selectIndex = i;
            i++;
        }
        NewTaskGroupPicker.SelectedIndex = selectIndex;
    }

    private void NewTaskGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem ci)
            Vm.NewTaskGroupId = ci.Tag as int?;
    }

    private void NewTaskRecurrence_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel) return;
        if (sender is ComboBox cb && cb.SelectedItem is ComboBoxItem ci
            && ci.Tag is string s && int.TryParse(s, out int v))
            Vm.NewTaskRecurrence = (RecurrenceType)v;
    }

    // System Tray

    private void InitTrayIcon()
    {
        System.Drawing.Icon icon;
        try
        {
            var uri    = new Uri("pack://application:,,,/Assets/icon.ico");
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            icon = stream is not null ? new System.Drawing.Icon(stream) : SystemIcon();
        }
        catch { icon = SystemIcon(); }

        _trayIcon = new NotifyIcon { Icon = icon, Text = "Tasky - Task Manager", Visible = true };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Tasky", null, (_, _) => ShowFromTray());
        menu.Items.Add("Quick Add",  null, (_, _) => OpenQuickAdd());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit Tasky", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip  = menu;
        _trayIcon.DoubleClick       += (_, _) => ShowFromTray();
        _trayIcon.BalloonTipClicked += (_, _) => ShowFromTray();
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    public void ShowBalloonReminder(string title, string message)
        => _trayIcon?.ShowBalloonTip(8000, title, message, ToolTipIcon.Info);

    // ─── Dim + blur overlay for modal dialogs ─────────────────────────────

    /// <summary>
    /// Blurs the window content and darkens it. Call before ShowDialog(),
    /// then HideDimOverlay() in the finally block.
    /// </summary>
    private static readonly System.Windows.Media.Effects.BlurEffect _blur =
        new() { Radius = 6 };

    private void ShowDimOverlay()
    {
        // Blur only the content panels — NOT RootGrid, so InAppNotifBorder stays crisp on top
        MainHeader.Effect      = _blur;
        AddPanelBorder.Effect  = _blur;
        PageContainer.Effect   = _blur;
        DimOverlay.Visibility  = Visibility.Visible;
    }

    private void HideDimOverlay()
    {
        DimOverlay.Visibility  = Visibility.Collapsed;
        MainHeader.Effect      = null;
        AddPanelBorder.Effect  = null;
        PageContainer.Effect   = null;
    }

    /// <summary>Wraps ShowDialog with blur/dim so every dialog gets the same backdrop.</summary>
    private bool? ShowModalDialog(System.Windows.Window dlg)
    {
        ShowDimOverlay();
        try   { return dlg.ShowDialog(); }
        finally { HideDimOverlay(); }
    }

    // ─── In-app notification overlay ─────────────────────────────────────

    private System.Windows.Threading.DispatcherTimer? _notifTimer;

    public void ShowInAppNotification(string message, string accentHex, string icon)
    {
        // Set content
        NotifIcon.Text    = icon;
        NotifMessage.Text = message;

        // Set accent colour + tinted background
        try
        {
            var col = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(accentHex);
            var accentBrush = new System.Windows.Media.SolidColorBrush(col);
            NotifAccent.Background = accentBrush;

            // Dark-tinted background based on accent (green/amber/blue/red)
            var bgHex = accentHex.ToUpperInvariant() switch
            {
                "#22C55E" or "#34D399" => "#052E16",  // success → dark green
                "#F59E0B" or "#FCD34D" => "#451A03",  // warning → dark amber
                "#EF4444" or "#F87171" => "#3B0A0A",  // error   → dark red
                _                      => "#1E3A5F",  // default → dark blue
            };
            var bgCol = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(bgHex);
            NotifMainBorder.Background = new System.Windows.Media.SolidColorBrush(bgCol);
        }
        catch { NotifAccent.Background = System.Windows.Media.Brushes.Gray; }

        // Fade in
        InAppNotifBorder.Visibility = System.Windows.Visibility.Visible;
        InAppNotifBorder.Opacity    = 0;
        var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
            new Duration(TimeSpan.FromMilliseconds(200)));
        InAppNotifBorder.BeginAnimation(OpacityProperty, fadeIn);

        // Auto-dismiss after 3 s
        _notifTimer?.Stop();
        _notifTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromSeconds(3) };
        _notifTimer.Tick += (_, _) =>
        {
            _notifTimer.Stop();
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                new Duration(TimeSpan.FromMilliseconds(250)));
            fadeOut.Completed += (_, _) =>
                InAppNotifBorder.Visibility = System.Windows.Visibility.Collapsed;
            InAppNotifBorder.BeginAnimation(OpacityProperty, fadeOut);
        };
        _notifTimer.Start();
    }

    private static System.Drawing.Icon SystemIcon()
    {
        var bmp = new System.Drawing.Bitmap(32, 32);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.FromArgb(13, 17, 23));
        g.DrawString("T", new System.Drawing.Font("Arial", 18, System.Drawing.FontStyle.Bold),
            System.Drawing.Brushes.White, 2f, 1f);
        return System.Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void OpenQuickAdd()
    {
        var qa = new Windows.QuickAddWindow(Vm);
        qa.Show(); qa.Activate();
    }

    private void ExitApp()
    {
        _trayIcon?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    // Window events

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
        _trayIcon?.ShowBalloonTip(2000, "Tasky", "Running in background. Double-click to restore.", ToolTipIcon.Info);
    }

    // Header buttons

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var app  = (App)System.Windows.Application.Current;
        app.ToggleTheme();
        bool dark = app.IsDarkTheme;
        SyncThemeIcons(dark);
        ApplyTitleBarTheme(dark);
    }

    private void Completed_Click(object sender, RoutedEventArgs e)  => NavigateTo(AppPage.Completed);
    private void Notes_Click(object sender, RoutedEventArgs e)      => NavigateTo(AppPage.Notes);
    private void Stats_Click(object sender, RoutedEventArgs e)      { Vm.RefreshStats(_statsWeekOffset); NavigateTo(AppPage.Stats); }
    private void Reminders_Click(object sender, RoutedEventArgs e)  => NavigateTo(AppPage.Reminders);

    // ─── Reminders page ───────────────────────────────────────────────────

    /// <summary>Flat view-model used by the reminders ItemsControl.</summary>
    private sealed class ReminderRow
    {
        public int    Id               { get; init; }
        public string TaskTitle        { get; init; } = string.Empty;
        public string TaskIdDisplay    { get; init; } = string.Empty;
        public string FireDisplay      { get; init; } = string.Empty;
        public string FrequencyDisplay { get; init; } = string.Empty;
    }

    private sealed class MissedNotifRow
    {
        public int    Id             { get; init; }
        public int    TaskId         { get; init; }
        public string TaskTitle      { get; init; } = string.Empty;
        public string TaskIdDisplay  { get; init; } = string.Empty;
        public string FiredAtDisplay { get; init; } = string.Empty;
        public bool   IsRead         { get; init; }
    }

    private void RefreshRemindersPage()
    {
        // All notifications (unread first, then read/faded)
        var all = App.Database!.GetMissedNotifications()
            .Select(m => new MissedNotifRow
            {
                Id             = m.Id,
                TaskId         = m.TaskId,
                TaskTitle      = m.TaskTitle,
                TaskIdDisplay  = $"#{m.TaskId:D3}",
                FiredAtDisplay = m.FiredAt.Date == DateTime.Today
                    ? $"Today {m.FiredAt:HH:mm}"
                    : $"{m.FiredAt:MMM d, HH:mm}",
                IsRead         = m.IsRead
            })
            .ToList();

        var unread = all.Where(r => !r.IsRead).ToList();
        var read   = all.Where(r =>  r.IsRead).ToList();

        MissedList.ItemsSource       = unread;
        ReadList.ItemsSource         = read;
        MissedSection.Visibility     = unread.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ReadSection.Visibility       = read.Count   > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifEmptyLabel.Visibility   = unread.Count == 0 && read.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        Vm.MissedNotifCount          = unread.Count;

        // Upcoming reminders — nearest first
        var reminders = App.Database!.GetAllReminders()
            .Where(r => r.NextFireTime > DateTime.Now)
            .OrderBy(r => r.NextFireTime)
            .Select(r => new ReminderRow
            {
                Id               = r.Id,
                TaskTitle        = r.TaskTitle,
                TaskIdDisplay    = $"#{r.TaskId:D3}",
                FireDisplay      = r.NextFireTime.ToString("MMM d, yyyy  HH:mm"),
                FrequencyDisplay = BuildFreqLabel(r)
            })
            .ToList();

        RemindersList.ItemsSource      = reminders;
        RemindersEmptyLabel.Visibility = reminders.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifTab_Click(object sender, RoutedEventArgs e)
    {
        NotifPanel.Visibility    = Visibility.Visible;
        UpcomingPanel.Visibility = Visibility.Collapsed;
    }

    private void UpcomingTab_Click(object sender, RoutedEventArgs e)
    {
        NotifPanel.Visibility    = Visibility.Collapsed;
        UpcomingPanel.Visibility = Visibility.Visible;
    }

    private void MissedNotif_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not MissedNotifRow row) return;
        // Mark as read (moves to faded "Read" section) instead of deleting
        App.Database!.MarkMissedNotificationRead(row.Id);
        RefreshRemindersPage();
        // Open task detail
        var vm = Vm.Tasks.FirstOrDefault(t => t.Id == row.TaskId)
              ?? Vm.CompletedTasks.FirstOrDefault(t => t.Id == row.TaskId);
        if (vm is not null) OpenTaskDetail(vm);
    }

    private void ReadNotif_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not MissedNotifRow row) return;
        var vm = Vm.Tasks.FirstOrDefault(t => t.Id == row.TaskId)
              ?? Vm.CompletedTasks.FirstOrDefault(t => t.Id == row.TaskId);
        if (vm is not null) OpenTaskDetail(vm);
    }

    private void DismissMissedNotif_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not int id) return;
        App.Database!.DeleteMissedNotification(id);
        RefreshRemindersPage();
    }

    private static string BuildFreqLabel(Models.ReminderItem r)
    {
        if (r.Frequency == Models.ReminderFrequency.Weekly && r.TargetDayOfWeek.HasValue)
        {
            var day = System.Globalization.CultureInfo.CurrentCulture
                .DateTimeFormat.GetDayName((DayOfWeek)r.TargetDayOfWeek.Value);
            return $"Every {day}";
        }
        if (r.Frequency == Models.ReminderFrequency.Monthly && r.DayOfMonth.HasValue)
        {
            string suffix = (r.DayOfMonth.Value % 100) switch
            {
                11 or 12 or 13 => "th",
                _ => (r.DayOfMonth.Value % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" }
            };
            return $"{r.DayOfMonth}{suffix} of month";
        }
        return r.Frequency switch
        {
            Models.ReminderFrequency.Daily   => "Daily",
            Models.ReminderFrequency.Weekly  => "Weekly",
            Models.ReminderFrequency.Monthly => "Monthly",
            _                                => "Once"
        };
    }

    private void DeleteReminder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not int id) return;
        var dlg = new ConfirmDialog("Delete Reminder",
            "Delete this reminder? The task will not be deleted.",
            "Delete", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true)
        {
            App.Database!.DeleteReminder(id);
            RefreshRemindersPage();
            InAppNotificationService.Success("Reminder deleted.");
        }
    }

    private void Groups_Click(object sender, RoutedEventArgs e)
    {
        ShowModalDialog(new ManageGroupsDialog(Vm) { Owner = this });
        BuildGroupChips();
        PopulateNewTaskGroupPicker();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => NavigateTo(AppPage.Settings);

    // ── Settings page ────────────────────────────────────────────────────────
    private void LoadSettingsPage()
    {
        var s = App.Settings.Current;
        if (SettingUserName != null) SettingUserName.Text = s.UserName;

        // Theme
        if (ThemeDark  != null) ThemeDark.IsChecked  = s.Theme == "dark";
        if (ThemeLight != null) ThemeLight.IsChecked = s.Theme == "light";
        if (ThemeAuto  != null) ThemeAuto.IsChecked  = s.Theme == "auto";

        // Tune
        if (SettingTune != null)
            foreach (ComboBoxItem item in SettingTune.Items)
                if (item.Tag?.ToString() == s.NotifTune) { item.IsSelected = true; break; }

        // Accent color
        if (AccentBlue   != null) AccentBlue.IsChecked   = s.NotifAccentColor == "#60A5FA";
        if (AccentGreen  != null) AccentGreen.IsChecked  = s.NotifAccentColor == "#22C55E";
        if (AccentOrange != null) AccentOrange.IsChecked = s.NotifAccentColor == "#FB923C";
        if (AccentPurple != null) AccentPurple.IsChecked = s.NotifAccentColor == "#A78BFA";

        // Dismiss
        if (DismissPerm != null) DismissPerm.IsChecked = s.NotifAutoDismissSecs == 0;
        if (Dismiss15   != null) Dismiss15.IsChecked   = s.NotifAutoDismissSecs == 15;
        if (Dismiss30   != null) Dismiss30.IsChecked   = s.NotifAutoDismissSecs == 30;
        if (Dismiss60   != null) Dismiss60.IsChecked   = s.NotifAutoDismissSecs == 60;

        // Task completion
        if (CompletionPopupToggle != null) CompletionPopupToggle.IsChecked = s.ShowCompletionPopup;
        if (SaveScreenshotToggle  != null) SaveScreenshotToggle.IsChecked  = s.SaveScreenshotToDesktop;

    }

    private void SettingUserName_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        App.Settings.Current.UserName = SettingUserName?.Text ?? string.Empty;
        App.Settings.Save();
    }

    private void ThemeSetting_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "dark";
        App.Settings.Current.Theme = tag;
        App.Settings.Save();
        var app = (App)Application.Current;
        bool dark = tag == "dark" || (tag == "auto" && app.IsDarkTheme);
        if (tag == "light") dark = false;
        app.ApplyTheme(dark);
        ApplyTitleBarTheme(dark);
        SyncThemeIcons(dark);
    }

    private void SettingTune_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var tag = (SettingTune?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "notify";
        App.Settings.Current.NotifTune = tag;
        App.Settings.Save();
    }

    private void PreviewTune_Click(object sender, RoutedEventArgs e)
    {
        var tag = (SettingTune?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "notify";
        Tasky.Services.SoundPlayer.Play(tag);
    }

    private void TestNotification_Click(object sender, RoutedEventArgs e)
    {
        // Fire a real reminder toast so the user can verify their notification
        // settings (sound, accent color, auto-dismiss) are working end-to-end.
        if (App.Notifications is { } notifications)
            notifications.SendReminder("This is a test reminder — your notifications are working!");
        else
            InAppNotificationService.Error("Notification service is not available.");
    }

    private void AccentColor_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "#60A5FA";
        App.Settings.Current.NotifAccentColor = tag;
        App.Settings.Save();
    }

    private void DismissTime_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() is string t
            && int.TryParse(t, out int secs))
        {
            App.Settings.Current.NotifAutoDismissSecs = secs;
            App.Settings.Save();
        }
    }

    private void CompletionPopupToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.ShowCompletionPopup = CompletionPopupToggle?.IsChecked == true;
        App.Settings.Save();
    }

    private void SaveScreenshotToggle_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.Current.SaveScreenshotToDesktop = SaveScreenshotToggle?.IsChecked == true;
        App.Settings.Save();
    }


    private void WidgetToggle_Click(object sender, RoutedEventArgs e)
    {
        // Widget removed
    }

    // Add panel

    private void AddToggle_Click(object sender, RoutedEventArgs e)
    {
        _addPanelOpen = !_addPanelOpen;
        AddFormPanel.Visibility = _addPanelOpen ? Visibility.Visible : Visibility.Collapsed;
        AddToggleBtn.Content    = _addPanelOpen ? "✕  Cancel" : "➕  New Task";
        if (_addPanelOpen)
        {
            Vm.NewDueDate = DateTime.Today;  // always default to today when form opens
            TitleInputBox.Focus();
        }
        else
        {
            // Cancelled — discard pending attachments
            _pendingAttachBytes.Clear();
            _pendingAttachPaths.Clear();
            UpdatePendingBadge();
        }
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Vm.NewTitle)) return;

        Vm.AddTask();   // creates and sorts the task

        // The newest task IS the one just added — find it by CreatedAt (most recent)
        var newTask = Vm.Tasks.OrderByDescending(t => t.Model.CreatedAt).FirstOrDefault();
        int pendingCount = _pendingAttachBytes.Count + _pendingAttachPaths.Count;
        if (newTask != null && pendingCount > 0)
        {
            FlushPendingAttachments(newTask.Id);
            InAppNotificationService.Success($"Task created with {pendingCount} attachment(s).");
        }

        _addPanelOpen           = false;
        AddFormPanel.Visibility = Visibility.Collapsed;
        AddToggleBtn.Content    = "➕  New Task";
    }

    private void TitleBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTask_Click(this, new RoutedEventArgs());
    }

    // Filter chips

    private void Filter_All_Click(object sender, RoutedEventArgs e)   => Vm.ActiveFilter = TaskFilter.All;
    private void Filter_Today_Click(object sender, RoutedEventArgs e) => Vm.ActiveFilter = TaskFilter.Today;
    private void Filter_ThisWeek_Click(object sender, RoutedEventArgs e) => Vm.ActiveFilter = TaskFilter.ThisWeek;

    // Image paste / drop

    private void Desc_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (SafeClipboard.ContainsImage() && SafeClipboard.GetImage() is { } img)
            {
                e.Handled = true;
                SaveClipboardImage(img);
            }
        }
    }

    private void Desc_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        foreach (var f in files.Where(ImageFiles.IsImage)) SaveImageFile(f);
    }

    private void SaveClipboardImage(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        // If the Add Task panel is open, buffer for the upcoming new task
        if (_addPanelOpen && AddFormPanel.Visibility == Visibility.Visible)
        {
            using var ms = new MemoryStream();
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            enc.Save(ms);
            _pendingAttachBytes.Add((ms.ToArray(), ".png"));
            RebuildPendingAttachThumbs();
            return;
        }
        // Otherwise attach to active/last task as before
        var target = ResolveExistingTarget();
        if (target is null) return;
        using var ms2 = new MemoryStream();
        var enc2 = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc2.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        enc2.Save(ms2);
        Vm.SaveAttachmentFromBytes(target.Id, ms2.ToArray(), ".png");
        Vm.RefreshAttachmentCount(target);
        ShowAttachmentToast(target.Title);
    }

    private void SaveImageFile(string path)
    {
        if (_addPanelOpen && AddFormPanel.Visibility == Visibility.Visible)
        {
            _pendingAttachPaths.Add(path);
            RebuildPendingAttachThumbs();
            return;
        }
        var target = ResolveExistingTarget();
        if (target is null) return;
        Vm.SaveAttachment(target.Id, path);
        Vm.RefreshAttachmentCount(target);
        ShowAttachmentToast(target.Title);
    }

    private void AttachImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select image(s) to attach",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) _pendingAttachPaths.Add(f);
        RebuildPendingAttachThumbs();
    }

    private void UpdatePendingBadge() => RebuildPendingAttachThumbs();

    private void RebuildPendingAttachThumbs()
    {
        int total = _pendingAttachBytes.Count + _pendingAttachPaths.Count;
        if (PendingAttachPanel != null)
            PendingAttachPanel.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PendingAttachThumbs is null) return;
        PendingAttachThumbs.Children.Clear();

        // Pasted images (raw bytes)
        for (int i = 0; i < _pendingAttachBytes.Count; i++)
        {
            int idx = i;
            System.Windows.Media.Imaging.BitmapImage? src = null;
            try
            {
                src = new System.Windows.Media.Imaging.BitmapImage();
                src.BeginInit();
                src.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                src.StreamSource = new MemoryStream(_pendingAttachBytes[idx].Data);
                src.EndInit();
            }
            catch { src = null; }
            PendingAttachThumbs.Children.Add(BuildPendingThumb(src, () =>
            {
                if (idx < _pendingAttachBytes.Count) _pendingAttachBytes.RemoveAt(idx);
                RebuildPendingAttachThumbs();
            }));
        }

        // Picked / dropped files (paths)
        for (int i = 0; i < _pendingAttachPaths.Count; i++)
        {
            int idx = i;
            string path = _pendingAttachPaths[idx];
            System.Windows.Media.Imaging.BitmapImage? src = null;
            try
            {
                src = new System.Windows.Media.Imaging.BitmapImage();
                src.BeginInit();
                src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                src.UriSource   = new Uri(path);
                src.EndInit();
            }
            catch { src = null; }
            PendingAttachThumbs.Children.Add(BuildPendingThumb(src, () =>
            {
                if (idx < _pendingAttachPaths.Count) _pendingAttachPaths.RemoveAt(idx);
                RebuildPendingAttachThumbs();
            }));
        }
    }

    private FrameworkElement BuildPendingThumb(System.Windows.Media.Imaging.BitmapSource? src, Action onRemove)
    {
        var border = new Border
        {
            Width = 72, Height = 72, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1)
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        border.SetResourceReference(Border.BackgroundProperty,  "CardBackground");

        var grid = new Grid();
        if (src != null)
            grid.Children.Add(new Image { Source = src, Stretch = System.Windows.Media.Stretch.UniformToFill });
        else
            grid.Children.Add(new TextBlock
            {
                Text = "📄", FontSize = 26,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            });

        var del = new Button
        {
            Content = "✕", Width = 18, Height = 18, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0), Cursor = Cursors.Hand,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 24, 39)),
            Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            ToolTip = "Remove"
        };
        del.Click += (_, _) => onRemove();

        grid.Children.Add(del);
        border.Child = grid;
        return border;
    }

    private void FlushPendingAttachments(int taskId)
    {
        foreach (var (data, ext) in _pendingAttachBytes)
            Vm.SaveAttachmentFromBytes(taskId, data, ext);
        foreach (var path in _pendingAttachPaths)
            Vm.SaveAttachment(taskId, path);
        _pendingAttachBytes.Clear();
        _pendingAttachPaths.Clear();
        UpdatePendingBadge();
        var vm = Vm.Tasks.FirstOrDefault(t => t.Id == taskId);
        if (vm != null) Vm.RefreshAttachmentCount(vm);
    }

    private TaskItemViewModel? ResolveExistingTarget()
    {
        if (Vm.Tasks.Count == 0)
        {
            InAppNotificationService.Warning("No tasks available to attach to.");
            return null;
        }
        return Vm.ActiveTask ?? Vm.Tasks.First();
    }

    private static void ShowAttachmentToast(string title)
        => InAppNotificationService.Success("Image attached to \"" + title + "\".");

    // Task event handlers

    private void Checkbox_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (GetTaskVm(sender) is not { } vm) return;

        // The checkbox binding is one-way: clicking only ticks the box on screen and
        // the task itself is untouched until the user confirms. On cancel, untick it.
        var confirm = new ConfirmDialog(
            "Mark as Done",
            $"Mark \"{vm.Title}\" as complete?",
            "Mark Done", ConfirmType.Info) { Owner = this };
        if (ShowModalDialog(confirm) != true)
        {
            if (sender is System.Windows.Controls.CheckBox box)
                box.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, vm.IsCompleted);
            return;
        }

        // 1. Mark as completed first (task moves to CompletedTasks)
        bool ok = Vm.CompleteTask(vm, out bool needsManualTime);
        if (!ok) return;

        // 2. Handle manual time entry if needed
        if (needsManualTime)
        {
            var dlg = new ManualTimeDialog { Owner = this };
            if (ShowModalDialog(dlg) == true && dlg.ResultMinutes.HasValue)
            { vm.ManualTimeMinutes = dlg.ResultMinutes; Vm.SaveTask(vm); }
        }

        // 3. Refresh stats if visible
        if (StatsPage.Visibility == Visibility.Visible) { Vm.RefreshStats(_statsWeekOffset); BuildStatsUI(); }

        // 4. Show celebration popup with confetti (task is already saved)
        if (!App.Settings.Current.ShowCompletionPopup) return;
        ShowDimOverlay();
        var celebration = new Dialogs.TaskCompletedDialog(vm);
        celebration.Closed += (_, _) => HideDimOverlay();
        celebration.Show();
        celebration.Activate();
    }

    private void Completed_Uncheck(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (GetTaskVm(sender) is not { } vm) return;
        var confirm = new ConfirmDialog(
            "Resume Task",
            $"Move \"{vm.Title}\" back to active tasks?",
            "Resume", ConfirmType.Warning) { Owner = this };
        if (ShowModalDialog(confirm) != true) return;
        Vm.UncompleteTask(vm);
        RefreshCompletedList();
    }

    private void ShowMore_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is { } vm) vm.IsDescriptionExpanded = !vm.IsDescriptionExpanded;
    }

    private void ActiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var before = DragDropBehavior.CapturePositions(TaskListControl);
        Vm.ToggleActive(vm);
        TaskListControl.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            () => DragDropBehavior.AnimateFlip(TaskListControl, before));
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is { } vm) Vm.TogglePause(vm);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var dlg = new EditTaskDialog(vm, Vm) { Owner = this };
        if (ShowModalDialog(dlg) == true)
        {
            Vm.SaveTask(vm);
            // Preserve the user's custom sort order — just refresh the display
            Vm.RefreshVisibleTasks();
        }
    }

    private void Reminder_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var d = new ReminderDialog(vm.Id, vm.Title, App.Database!) { Owner = this };
        ShowModalDialog(d);
        Vm.RefreshReminderCount(vm);  // update badge after dialog closes
    }

    private void Attachment_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        { var d = new AttachmentDialog(vm.Id, vm.Title, Vm) { Owner = this }; ShowModalDialog(d); }
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var dlg = new ConfirmDialog("Delete Task",
            "Delete \"" + vm.Title + "\"?\n\nThis cannot be undone.", "Delete", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true) Vm.DeleteTask(vm);
    }

    private void TaskContent_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount < 1) return;
        if (GetTaskVm(sender) is not { } vm) return;
        ShowDimmedDetail(vm);
    }

    private void ShowDimmedDetail(TaskItemViewModel vm)
    {
        ShowDimOverlay();
        var dlg = new TaskDetailDialog(vm, Vm) { Owner = this };
        dlg.Closed += (_, _) => HideDimOverlay();
        dlg.Show();
    }

    // Completed page

    private void CGroup_Click(object sender, RoutedEventArgs e)
    {
        _completedGroupFilter = (sender as RadioButton)?.Tag?.ToString() ?? "All";
        // Clear date picker when a chip filter is selected
        if (CompletedDatePicker != null) CompletedDatePicker.SelectedDate = null;
        RefreshCompletedList();
    }

    private void CompletedDate_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CompletedDatePicker?.SelectedDate == null) return;
        // Deselect all chip filters when a specific date is chosen
        if (CChipAll != null) CChipAll.IsChecked = false;
        _completedGroupFilter = "Date";
        RefreshCompletedList();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel) RefreshCompletedList();
    }

    private void RefreshCompletedList()
    {
        if (DataContext is not MainViewModel) return;
        var search = (Vm.CompletedSearch ?? string.Empty).Trim();
        IEnumerable<TaskItemViewModel> filtered = Vm.CompletedTasks;
        if (!string.IsNullOrEmpty(search))
            filtered = filtered.Where(v =>
                v.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                v.TaskNumberDisplay.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (_completedGroupFilter == "Today")
            filtered = filtered.Where(v => v.CompletedGroup == "Today");
        else if (_completedGroupFilter == "This Week")
            filtered = filtered.Where(v => v.CompletedGroup is "Today" or "This Week");
        else if (_completedGroupFilter == "This Month")
            filtered = filtered.Where(v => v.CompletedAt.HasValue &&
                v.CompletedAt.Value.Year == DateTime.Today.Year &&
                v.CompletedAt.Value.Month == DateTime.Today.Month);
        else if (_completedGroupFilter == "Date" && CompletedDatePicker?.SelectedDate is DateTime picked)
            filtered = filtered.Where(v => v.CompletedAt.HasValue &&
                v.CompletedAt.Value.Date == picked.Date);

        var sortTag = (SortBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "CompletedDesc";
        static int TimeKey(TaskItemViewModel v)
            => v.TotalSeconds > 0 ? v.TotalSeconds : (v.ManualTimeMinutes ?? 0) * 60;

        // Only TaskItemViewModels in the source — no mixed string headers
        var items = new System.Collections.Generic.List<TaskItemViewModel>();
        foreach (var g in filtered.GroupBy(v => v.CompletedGroup)
                                  .OrderBy(g => g.Key == "Today" ? 0 : g.Key == "This Week" ? 1 : 2))
        {
            IEnumerable<TaskItemViewModel> sorted = sortTag switch
            {
                "oldest"    => g.OrderBy(v => v.CompletedAt ?? DateTime.MinValue),
                "time_desc" => g.OrderByDescending(TimeKey),
                "time_asc"  => g.OrderBy(TimeKey),
                _              => g.OrderByDescending(v => v.CompletedAt ?? DateTime.MinValue)
            };
            foreach (var v in sorted) items.Add(v);
        }
        CompletedList.ItemsSource = items;
        if (CompletedEmptyState != null)
            CompletedEmptyState.Visibility = items.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CompletedTask_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (GetTaskVm(sender) is { } vm) OpenTaskDetail(vm);
    }

    private void CompletedDetail_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is { } vm) OpenTaskDetail(vm);
    }

    private void CompletedUpdateTime_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var dlg = new ManualTimeDialog { Owner = this };

        // Pre-fill with whatever time is currently showing:
        // timer-tracked seconds take precedence over ManualTimeMinutes
        int currentMinutes = vm.TotalSeconds > 0
            ? Math.Max(1, vm.TotalSeconds / 60)
            : vm.ManualTimeMinutes ?? 0;
        if (currentMinutes > 0)
            dlg.PresetMinutes(currentMinutes);

        if (ShowModalDialog(dlg) == true && dlg.ResultMinutes.HasValue)
        {
            // Store as accumulated seconds so if the task is restored, the timer
            // continues from this value rather than restarting from zero.
            vm.Model.AccumulatedSeconds = dlg.ResultMinutes.Value * 60;
            vm.ManualTimeMinutes = null;
            Vm.SaveTask(vm);
            RefreshCompletedList();
            InAppNotificationService.Success($"Time updated for \"{vm.Title}\".");
        }
    }

    private void CompletedDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var dlg = new ConfirmDialog("Delete Completed Task",
            "Permanently delete \"" + vm.Title + "\"?\n\nThis cannot be undone.",
            "Delete", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true) { Vm.DeleteTask(vm); RefreshCompletedList(); }
    }

    // Recycle bin

    private void OpenTrash_Click(object sender, RoutedEventArgs e) => NavigateTo(AppPage.Trash);

    private void RefreshTrashList()
    {
        if (DataContext is not MainViewModel) return;
        TrashEmptyState.Visibility = Vm.DeletedTasks.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        Vm.RestoreTask(vm);
        RefreshTrashList();
        InAppNotificationService.Success($"Restored \"{vm.Title}\".");
    }

    private void PermanentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (GetTaskVm(sender) is not { } vm) return;
        var dlg = new ConfirmDialog("Delete Permanently",
            "Permanently delete \"" + vm.Title + "\"?\n\nThis cannot be undone.",
            "Delete", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true) { Vm.PermanentlyDeleteTask(vm); RefreshTrashList(); }
    }

    private void Migrate_Click(object sender, RoutedEventArgs e)
    {
        if (App.Database is null) return;
        var dlg = new MigrateDialog(new Services.Migration.MigrationService(App.Database)) { Owner = this };
        ShowModalDialog(dlg);
    }

    private void ClearMeetingHistory_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new ConfirmDialog("Clear Meeting History",
            "Delete all auto-detected meeting sessions?\n\nThis cannot be undone.",
            "Clear", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) != true) return;
        App.Database!.ClearMeetingSessions();
        if (StatsPage.Visibility == Visibility.Visible) { Vm.RefreshStats(_statsWeekOffset); BuildStatsUI(); }
        InAppNotificationService.Success("Meeting history cleared.");
    }

    private void EmptyBin_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.DeletedTasks.Count == 0)
        {
            InAppNotificationService.Info("The recycle bin is already empty.");
            return;
        }
        var dlg = new ConfirmDialog("Empty Recycle Bin",
            $"Permanently delete all {Vm.DeletedTasks.Count} item(s) in the bin?\n\nThis cannot be undone.",
            "Empty Bin", ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true) { Vm.EmptyTrash(); RefreshTrashList(); }
    }

    // Notes page

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        _addNotePanelOpen = !_addNotePanelOpen;
        AddNotePanel.Visibility = _addNotePanelOpen ? Visibility.Visible : Visibility.Collapsed;
        if (_addNotePanelOpen)
        {
            NoteTitleBox.Text = string.Empty;
            NoteDescBox.Text  = string.Empty;
            NoteTagsBox.Text  = string.Empty;
            NoteTitleBox.Focus();
        }
    }

    private void CancelNote_Click(object sender, RoutedEventArgs e)
    {
        _addNotePanelOpen = false;
        AddNotePanel.Visibility = Visibility.Collapsed;
    }

    private void SaveNote_Click(object sender, RoutedEventArgs e)
    {
        var title = NoteTitleBox?.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            InAppNotificationService.Warning("Note title cannot be empty.");
            return;
        }
        Vm.AddNote(title, NoteDescBox?.Text?.Trim() ?? string.Empty, NoteTagsBox?.Text?.Trim() ?? string.Empty);
        _addNotePanelOpen = false;
        AddNotePanel.Visibility = Visibility.Collapsed;
        Vm.LoadNotes();
        RefreshNotesEmpty();
        InAppNotificationService.Success("Note saved.");
    }

    private void Note_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement el && el.DataContext is Models.Note note)
        {
            var dlg = new Dialogs.NoteDetailDialog(note, Vm) { Owner = this };
            ShowModalDialog(dlg);
        }
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.DataContext is not Models.Note note) return;
        var dlg = new Dialogs.ConfirmDialog("Delete Note",
            $"Delete \"{note.Title}\"? This cannot be undone.",
            "Delete", Dialogs.ConfirmType.Danger) { Owner = this };
        if (ShowModalDialog(dlg) == true) { Vm.DeleteNote(note); RefreshNotesEmpty(); }
    }

    private void RefreshNotesEmpty()
    {
        if (NotesEmptyLabel is null) return;
        NotesEmptyLabel.Visibility = Vm.FilteredNotes.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Helper: extract TaskItemViewModel from any sender ─────────────────
    private static TaskItemViewModel? GetTaskVm(object sender)
        => (sender as FrameworkElement)?.DataContext as TaskItemViewModel;

    // ── Open task detail dialog ───────────────────────────────────────────
    public void OpenTaskDetail(TaskItemViewModel vm)
    {
        var dlg = new TaskDetailDialog(vm, Vm) { Owner = this };
        ShowModalDialog(dlg);
    }

    // ── Stats UI ─────────────────────────────────────────────────────────
    private void BuildStatsUI()
    {
        if (StatTodayCount    != null) StatTodayCount.Text    = Vm.StatsCompletedToday.ToString();
        if (StatWeekCount     != null) StatWeekCount.Text     = Vm.StatsCompletedWeek.ToString();
        if (StatAllTime       != null) StatAllTime.Text       = Vm.StatsCompletedAllTime.ToString();
        if (StatTimeToday     != null) StatTimeToday.Text     = Vm.StatsTimeToday;
        UpdateMeetingDisplay();

        // Update week navigation label and Next button state
        if (WeekLabel   != null) WeekLabel.Text = Vm.Last7DaysWeekLabel;
        if (NextWeekBtn != null) NextWeekBtn.IsEnabled = _statsWeekOffset < 0;

        if (BarChartPanel != null)
        {
            BarChartPanel.Children.Clear();
            foreach (var (day, count) in Vm.Last7DaysData)
            {
                int    max    = Math.Max(Vm.Last7DaysMax, 1);
                double height = Math.Max(4, 80.0 * count / max);
                var col = new System.Windows.Controls.StackPanel
                    { VerticalAlignment = VerticalAlignment.Bottom, Width = 30, Margin = new Thickness(3, 0, 3, 0) };
                var bar = new System.Windows.Controls.Border
                    { Height = height, CornerRadius = new CornerRadius(3, 3, 0, 0) };
                bar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "PrimaryBlue");
                var lbl = new System.Windows.Controls.TextBlock
                    { Text = day, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
                lbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
                var cnt = new System.Windows.Controls.TextBlock
                    { Text = count > 0 ? count.ToString() : string.Empty, FontSize = 10,
                      HorizontalAlignment = HorizontalAlignment.Center };
                cnt.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimary");
                col.Children.Add(cnt);
                col.Children.Add(bar);
                col.Children.Add(lbl);
                BarChartPanel.Children.Add(col);
            }
        }

        BuildTimeChartPanel();

        if (TopTasksPanel != null)
        {
            TopTasksPanel.Children.Clear();
            static int TimeKey(TaskItemViewModel v)  // local function — avoids closure allocation
                => v.TotalSeconds > 0 ? v.TotalSeconds : (v.ManualTimeMinutes ?? 0) * 60;
            var today = DateTime.Today;
            IEnumerable<TaskItemViewModel> pool = _topTasksFilter switch
            {
                "today" => Vm.CompletedTasks.Where(t => t.CompletedAt?.Date == today),
                "week"  => Vm.CompletedTasks.Where(t => t.CompletedAt?.Date >= today.AddDays(-6)),
                "month" => Vm.CompletedTasks.Where(t => t.CompletedAt.HasValue &&
                               t.CompletedAt.Value.Year == today.Year &&
                               t.CompletedAt.Value.Month == today.Month),
                _       => Vm.CompletedTasks
            };
            var topTasks = pool.Where(t => TimeKey(t) > 0).OrderByDescending(TimeKey).Take(5).ToList();
            if (TopTasksEmpty != null)
                TopTasksEmpty.Visibility = topTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var t in topTasks)
            {
                var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var title = new System.Windows.Controls.TextBlock
                    { Text = t.Title, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
                title.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimary");
                System.Windows.Controls.Grid.SetColumn(title, 0);
                var time = new System.Windows.Controls.TextBlock
                    { Text = t.TimeDisplay, FontSize = 12,
                      FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                      Margin = new Thickness(8, 0, 0, 0) };
                time.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentGreen");
                System.Windows.Controls.Grid.SetColumn(time, 1);
                row.Children.Add(title);
                row.Children.Add(time);
                TopTasksPanel.Children.Add(row);
            }
        }

        BuildBusiestHourChart();
        BuildWeeklyTimeChart();
        RefreshAndBuildAppUsage();
    }

    private void TopFilter_Click(object sender, RoutedEventArgs e)
    {
        _topTasksFilter = (sender as RadioButton)?.Tag?.ToString() ?? "overall";
        BuildStatsUI();
    }

    // ── Week navigation (Stats page) ─────────────────────────────────────────

    private void PrevWeek_Click(object sender, RoutedEventArgs e)
    {
        _statsWeekOffset--;
        if (NextWeekBtn != null) NextWeekBtn.IsEnabled = _statsWeekOffset < 0;
        Vm.RefreshStats(_statsWeekOffset);
        BuildStatsUI();
    }

    private void NextWeek_Click(object sender, RoutedEventArgs e)
    {
        if (_statsWeekOffset >= 0) return;
        _statsWeekOffset++;
        if (NextWeekBtn != null) NextWeekBtn.IsEnabled = _statsWeekOffset < 0;
        Vm.RefreshStats(_statsWeekOffset);
        BuildStatsUI();
    }

    private void WeeklyToggle_Click(object sender, RoutedEventArgs e)
    {
        _weeklyChartShowTime = WeeklyTimeToggle?.IsChecked == true;
        BuildWeeklyTimeChart();
    }

    private void MeetingFilter_Click(object sender, RoutedEventArgs e)
    {
        _meetingFilter = (sender as System.Windows.Controls.RadioButton)?.Tag?.ToString() ?? "week";
        // Re-query the DB for fresh data each time the chip changes
        Vm.RefreshStats(_statsWeekOffset);
        UpdateMeetingDisplay();
    }

    private void UpdateMeetingDisplay()
    {
        if (StatMeetingDisplay == null) return;
        StatMeetingDisplay.Text = _meetingFilter switch
        {
            "today" => Vm.StatMeetingHoursToday,
            "month" => Vm.StatMeetingHoursMonth,
            _       => Vm.StatMeetingHoursWeek
        };
    }

    private void BuildTimeChartPanel()
    {
        if (TimeChartPanel == null) return;
        TimeChartPanel.Children.Clear();
        var data = Vm.Last7DaysTimeData;
        int max  = Math.Max(Vm.Last7DaysTimeMax, 1);

        foreach (var (day, secs) in data)
        {
            double height = Math.Max(4, 80.0 * secs / max);
            var col = new System.Windows.Controls.StackPanel
                { VerticalAlignment = VerticalAlignment.Bottom, Width = 30, Margin = new Thickness(3, 0, 3, 0) };
            var bar = new System.Windows.Controls.Border
                { Height = height, CornerRadius = new CornerRadius(3, 3, 0, 0) };
            bar.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "AccentGreen");
            var lbl = new System.Windows.Controls.TextBlock
                { Text = day, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center };
            lbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
            string cnt = secs > 0 ? FormatSecsShort(secs) : string.Empty;
            var cntTb = new System.Windows.Controls.TextBlock
                { Text = cnt, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };
            cntTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextPrimary");
            col.Children.Add(cntTb);
            col.Children.Add(bar);
            col.Children.Add(lbl);
            TimeChartPanel.Children.Add(col);
        }
    }

    private static string FormatSecsShort(int secs)
    {
        if (secs <= 0) return string.Empty;
        var ts = TimeSpan.FromSeconds(secs);
        return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h{ts.Minutes:D2}m" : $"{ts.Minutes}m";
    }

    // Full-precision format used in App Usage panel: "1h 20m 19s" or "30m 20s" or "45s"
    private static string FormatSecsFull(int secs)
    {
        if (secs <= 0) return "0s";
        var ts = TimeSpan.FromSeconds(secs);
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes}m {ts.Seconds}s";
        if (ts.Minutes > 0)
            return $"{ts.Minutes}m {ts.Seconds}s";
        return $"{ts.Seconds}s";
    }




    private void BuildBusiestHourChart()
    {
        if (BusiestHourPanel == null) return;
        BusiestHourPanel.Children.Clear();
        var data     = Vm.BusiestHourData;   // 15 slots: 7 AM–9 PM, avg/day last 30 days
        double maxVal = data.Max();

        if (BusiestHourEmpty != null)
            BusiestHourEmpty.Visibility = maxVal == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (maxVal == 0) return;

        for (int i = 0; i < 15; i++)
        {
            int    hour   = i + 7;
            double avg    = data[i];
            double height = avg == 0 ? 4 : Math.Max(6, 64.0 * avg / maxVal);
            byte   alpha  = avg == 0 ? (byte)35 : (byte)(80 + (int)(175.0 * avg / maxVal));

            string label24 = $"{hour:D2}:00";
            string shortLbl = $"{hour:D2}";

            var bar = new System.Windows.Controls.Border
            {
                Height       = height,
                Width        = 26,
                CornerRadius = new CornerRadius(2, 2, 0, 0),
                Background   = new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromArgb(alpha, 96, 165, 250)),
                ToolTip      = $"{label24}: avg {avg:F2} completions/active day (last 30 days)"
            };

            var lbl = new System.Windows.Controls.TextBlock
            {
                Text                = shortLbl,
                FontSize            = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            lbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");

            var col = new System.Windows.Controls.StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Width             = 30,
                Margin            = new Thickness(1, 0, 1, 0)
            };
            col.Children.Add(bar);
            col.Children.Add(lbl);
            BusiestHourPanel.Children.Add(col);
        }
    }

    // ── App Usage panel ───────────────────────────────────────────────────────

    private void AppUsageFilter_Click(object sender, RoutedEventArgs e)
    {
        _appUsageFilter = (sender as RadioButton)?.Tag?.ToString() ?? "today";
        RefreshAndBuildAppUsage();
    }

    private void AppUsageRefresh_Click(object sender, RoutedEventArgs e)
        => RefreshAndBuildAppUsage();

    // Forward mouse-wheel from inner horizontal ScrollViewers to the outer stats scroll
    private void InnerScroll_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (e.Handled) return;
        e.Handled = true;
        StatsScrollViewer?.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(
            e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source      = sender
        });
    }

    private void RefreshAndBuildAppUsage()
    {
        var today = DateTime.Today;
        DateTime from, to;
        switch (_appUsageFilter)
        {
            case "week":
                from = today.AddDays(-6);
                to   = today.AddDays(1);
                break;
            case "month":
                from = new DateTime(today.Year, today.Month, 1);
                to   = today.AddDays(1);
                break;
            default:
                from = today;
                to   = today.AddDays(1);
                break;
        }
        Vm.RefreshAppUsage(from, to);
        BuildAppUsagePanel();
    }

    // ── Shell icon extraction (for PWA Start-Menu shortcuts) ─────────────────

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int    iIcon;
        public uint   dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst =  80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttr, ref SHFILEINFO psfi, uint cbSFI, uint uFlags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> _pwaIconCache = new();

    private static System.Windows.Media.ImageSource? GetPwaIcon(string appName)
    {
        if (_pwaIconCache.TryGetValue(appName, out var cached)) return cached;

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Edge and Chrome store the PWA icon as "<AppName>.ico" inside a _crx_... subdirectory
        string[] webAppDirs =
        [
            System.IO.Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Web Applications"),
            System.IO.Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Web Applications"),
        ];

        foreach (var dir in webAppDirs)
        {
            if (!System.IO.Directory.Exists(dir)) continue;
            try
            {
                foreach (var ico in System.IO.Directory.EnumerateFiles(dir, "*.ico", System.IO.SearchOption.AllDirectories))
                {
                    if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(ico), appName,
                            StringComparison.OrdinalIgnoreCase)) continue;

                    var img = LoadIcoFile(ico);
                    if (img != null) { _pwaIconCache[appName] = img; return img; }
                }
            }
            catch { }
        }

        // Fallback: read icon from the Start Menu shortcut via shell
        string[] startMenuDirs =
        [
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
        ];

        foreach (var dir in startMenuDirs)
        {
            if (!System.IO.Directory.Exists(dir)) continue;
            try
            {
                foreach (var lnk in System.IO.Directory.EnumerateFiles(dir, "*.lnk", System.IO.SearchOption.AllDirectories))
                {
                    var lnkName = System.IO.Path.GetFileNameWithoutExtension(lnk);
                    if (!string.Equals(lnkName, appName, StringComparison.OrdinalIgnoreCase)) continue;

                    var img = ExtractShellIcon(lnk);
                    _pwaIconCache[appName] = img;
                    return img;
                }
            }
            catch { }
        }

        _pwaIconCache[appName] = null;
        return null;
    }

    private static System.Windows.Media.ImageSource? LoadIcoFile(string icoPath)
    {
        try
        {
            using var icon = new System.Drawing.Icon(icoPath);
            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    private static System.Windows.Media.ImageSource? ExtractShellIcon(string path)
    {
        var shfi = new SHFILEINFO();
        var ret  = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), 0x100); // SHGFI_ICON
        if (ret == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
        try
        {
            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon, System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            return bmp;
        }
        finally { DestroyIcon(shfi.hIcon); }
    }

    // Icon cache keyed by the raw exePath string from the DB (may be full path or process name)
    private static readonly Dictionary<string, System.Windows.Media.ImageSource?> _appIconCache = new();

    private static System.Windows.Media.ImageSource? GetAppIcon(string exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;

        // PWA marker — search Start Menu shortcuts for the app's custom icon
        if (exePath.StartsWith("pwa://", StringComparison.OrdinalIgnoreCase))
            return GetPwaIcon(exePath.Substring(6));

        if (_appIconCache.TryGetValue(exePath, out var cached)) return cached;

        // Resolve to a real file path
        string resolved = exePath;
        if (!System.IO.File.Exists(resolved))
        {
            // Old DB rows store only the process name — look up the running process
            try
            {
                string procName = System.IO.Path.GetFileNameWithoutExtension(exePath);
                var procs = System.Diagnostics.Process.GetProcessesByName(procName);
                try
                {
                    if (procs.Length > 0)
                        resolved = procs[0].MainModule?.FileName ?? string.Empty;
                }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch { resolved = string.Empty; }

            if (string.IsNullOrEmpty(resolved) || !System.IO.File.Exists(resolved))
            {
                _appIconCache[exePath] = null;
                return null;
            }
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(resolved);
            if (icon == null) { _appIconCache[exePath] = null; return null; }
            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            _appIconCache[exePath] = bmp;
            return bmp;
        }
        catch { _appIconCache[exePath] = null; return null; }
    }

    private static readonly System.Windows.Media.Color[] _avatarPalette =
    [
        System.Windows.Media.Color.FromRgb( 99, 102, 241),
        System.Windows.Media.Color.FromRgb( 16, 185, 129),
        System.Windows.Media.Color.FromRgb(245, 158,  11),
        System.Windows.Media.Color.FromRgb(239,  68,  68),
        System.Windows.Media.Color.FromRgb( 59, 130, 246),
        System.Windows.Media.Color.FromRgb(168,  85, 247),
        System.Windows.Media.Color.FromRgb(236,  72, 153),
        System.Windows.Media.Color.FromRgb( 20, 184, 166),
    ];

    private static System.Windows.Media.Color AvatarColor(string name)
        => _avatarPalette[Math.Abs(name.GetHashCode()) % _avatarPalette.Length];

    private void BuildAppUsagePanel()
    {
        if (AppUsagePanel == null) return;
        AppUsagePanel.Children.Clear();

        var data = Vm.AppUsageData;
        if (AppUsageEmpty != null)
            AppUsageEmpty.Visibility = data.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (data.Count == 0) return;

        int maxSecs = Math.Max(1, data[0].Seconds);

        foreach (var (appName, secs, exePath) in data)
        {
            var row = new System.Windows.Controls.Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Icon or letter-avatar (tooltip shows the full app name)
            var appIcon = GetAppIcon(exePath);
            System.Windows.UIElement iconCell;
            if (appIcon != null)
            {
                var img = new System.Windows.Controls.Image
                {
                    Source              = appIcon,
                    Width               = 24, Height = 24,
                    VerticalAlignment   = VerticalAlignment.Center,
                    SnapsToDevicePixels = true,
                    ToolTip             = appName
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(
                    img, System.Windows.Media.BitmapScalingMode.HighQuality);
                iconCell = img;
            }
            else
            {
                var avatar = new System.Windows.Controls.Border
                {
                    Width           = 24, Height = 24,
                    CornerRadius    = new CornerRadius(6),
                    Background      = new System.Windows.Media.SolidColorBrush(AvatarColor(appName)),
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip         = appName
                };
                avatar.Child = new System.Windows.Controls.TextBlock
                {
                    Text                = appName.Length > 0 ? appName[0].ToString().ToUpperInvariant() : "?",
                    FontSize            = 12,
                    FontWeight          = FontWeights.Bold,
                    Foreground          = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center
                };
                iconCell = avatar;
            }
            System.Windows.Controls.Grid.SetColumn(iconCell, 0);

            // Progress bar
            var barGrid = new System.Windows.Controls.Grid
            {
                Margin            = new Thickness(8, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Height            = 6
            };
            var track = new System.Windows.Controls.Border { Height = 6, CornerRadius = new CornerRadius(3) };
            track.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "InputBackground");
            double ratio = (double)secs / maxSecs;
            var fill = new System.Windows.Controls.Border
            {
                Height              = 6,
                CornerRadius        = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background          = new System.Windows.Media.SolidColorBrush(
                                          System.Windows.Media.Color.FromRgb(52, 211, 153))
            };
            fill.Tag = ratio;
            barGrid.SizeChanged += (_, a) =>
            {
                if (fill.Tag is double r) fill.Width = Math.Max(4, a.NewSize.Width * r);
            };
            barGrid.Children.Add(track);
            barGrid.Children.Add(fill);
            System.Windows.Controls.Grid.SetColumn(barGrid, 1);

            var timeTb = new System.Windows.Controls.TextBlock
            {
                Text              = FormatSecsFull(secs),
                FontSize          = 11,
                FontFamily        = new System.Windows.Media.FontFamily("Consolas"),
                VerticalAlignment = VerticalAlignment.Center,
                MinWidth          = 80,
                TextAlignment     = System.Windows.TextAlignment.Right
            };
            timeTb.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "AccentGreen");
            System.Windows.Controls.Grid.SetColumn(timeTb, 2);

            row.Children.Add(iconCell);
            row.Children.Add(barGrid);
            row.Children.Add(timeTb);
            AppUsagePanel.Children.Add(row);
        }
    }

    private void BuildWeeklyTimeChart()
    {
        if (WeeklyChartPanel == null) return;
        WeeklyChartPanel.Children.Clear();
        string[] weekDayRanges = { "Days 1–7", "Days 8–14", "Days 15–21", "Days 22–end" };

        if (_weeklyChartShowTime)
        {
            var data     = Vm.WeeklyTimeData;
            int maxSecs  = Math.Max(1, data.Max());
            bool anyData = data.Any(s => s > 0);
            if (WeeklyChartEmpty != null)
            { WeeklyChartEmpty.Text = "No tracked time yet."; WeeklyChartEmpty.Visibility = anyData ? Visibility.Collapsed : Visibility.Visible; }
            if (!anyData) return;

            for (int w = 0; w < 4; w++)
            {
                int    secs   = data[w];
                double height = secs == 0 ? 4 : Math.Max(8, 64.0 * secs / maxSecs);
                byte   alpha  = secs == 0 ? (byte)30 : (byte)(70 + (int)(185.0 * secs / maxSecs));
                string tip    = $"Week {w + 1} ({weekDayRanges[w]})\nAvg per month: {(secs > 0 ? TaskItemViewModel.FormatSeconds(secs) : "no data")}";

                var bar = new System.Windows.Controls.Border
                {
                    Height = height, Width = 54, CornerRadius = new CornerRadius(4, 4, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(alpha, 34, 197, 94)), ToolTip = tip
                };
                var lbl = new System.Windows.Controls.TextBlock
                    { Text = $"Week {w + 1}", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                lbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
                var sub = new System.Windows.Controls.TextBlock
                    { Text = secs > 0 ? TaskItemViewModel.FormatSeconds(secs) : "—", FontSize = 9,
                      HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Consolas"), Margin = new Thickness(0, 2, 0, 0) };
                sub.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, secs > 0 ? "AccentGreen" : "TextSecondary");
                var col = new System.Windows.Controls.StackPanel
                    { VerticalAlignment = VerticalAlignment.Bottom, Width = 66, Margin = new Thickness(6, 0, 6, 0) };
                col.Children.Add(bar); col.Children.Add(lbl); col.Children.Add(sub);
                WeeklyChartPanel.Children.Add(col);
            }
        }
        else
        {
            var data      = Vm.WeeklyTaskData;
            int maxTasks  = Math.Max(1, data.Max());
            bool anyData  = data.Any(t => t > 0);
            if (WeeklyChartEmpty != null)
            { WeeklyChartEmpty.Text = "No task data yet."; WeeklyChartEmpty.Visibility = anyData ? Visibility.Collapsed : Visibility.Visible; }
            if (!anyData) return;

            for (int w = 0; w < 4; w++)
            {
                int    tasks  = data[w];
                double height = tasks == 0 ? 4 : Math.Max(8, 64.0 * tasks / maxTasks);
                byte   alpha  = tasks == 0 ? (byte)30 : (byte)(70 + (int)(185.0 * tasks / maxTasks));
                string tip    = $"Week {w + 1} ({weekDayRanges[w]})\nAvg tasks/month: {(tasks > 0 ? $"{tasks}" : "no data")}";

                var bar = new System.Windows.Controls.Border
                {
                    Height = height, Width = 54, CornerRadius = new CornerRadius(4, 4, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(alpha, 96, 165, 250)), ToolTip = tip
                };
                var lbl = new System.Windows.Controls.TextBlock
                    { Text = $"Week {w + 1}", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                lbl.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextSecondary");
                var sub = new System.Windows.Controls.TextBlock
                    { Text = tasks > 0 ? $"{tasks} avg" : "—", FontSize = 9,
                      HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
                sub.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, tasks > 0 ? "PrimaryBlue" : "TextSecondary");
                var col = new System.Windows.Controls.StackPanel
                    { VerticalAlignment = VerticalAlignment.Bottom, Width = 66, Margin = new Thickness(6, 0, 6, 0) };
                col.Children.Add(bar); col.Children.Add(lbl); col.Children.Add(sub);
                WeeklyChartPanel.Children.Add(col);
            }
        }
    }
}
