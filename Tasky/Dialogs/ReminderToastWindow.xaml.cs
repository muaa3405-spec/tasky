using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Tasky.Dialogs;

public partial class ReminderToastWindow : Window
{
    private readonly int _taskId;
    private readonly string _taskTitle;
    private readonly DispatcherTimer _autoDismiss;

    private static readonly CubicEase EaseOut = new() { EasingMode = EasingMode.EaseOut };
    private static readonly CubicEase EaseIn  = new() { EasingMode = EasingMode.EaseIn  };

    /// <param name="displayTitle">Title shown on the toast (with the task number).</param>
    /// <param name="taskTitle">Plain task title, reused when the toast is snoozed.</param>
    public ReminderToastWindow(string displayTitle, string taskTitle, int taskId)
    {
        _taskId    = taskId;
        _taskTitle = taskTitle;
        InitializeComponent();
        TitleText.Text = displayTitle;

        // Position at bottom-right (above taskbar) — do final Y after layout
        var wa = SystemParameters.WorkArea;
        Left    = wa.Right - Width - 16;
        Top     = wa.Bottom - 300;
        Opacity = 0;

        Loaded += (_, _) =>
        {
            Top = wa.Bottom - ActualHeight - 16;

            // Apply accent color from settings to the top strip + header text
            try
            {
                var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter
                    .ConvertFromString(App.Settings.Current.NotifAccentColor);
                var brush = new System.Windows.Media.SolidColorBrush(col);
                AccentStrip.Background = brush;
            }
            catch { }

            // Slide in: X 360→0 + fade in
            var slide = new DoubleAnimation(360, 0, TimeSpan.FromMilliseconds(380))
                { EasingFunction = EaseOut };
            var fade  = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
                { EasingFunction = EaseOut };

            SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
            BeginAnimation(OpacityProperty, fade);
        };

        // Auto-dismiss — use setting (0 = permanent, else N seconds)
        int secs = App.Settings.Current.NotifAutoDismissSecs;
        _autoDismiss = new DispatcherTimer
            { Interval = TimeSpan.FromSeconds(secs > 0 ? secs : int.MaxValue / 2) };
        _autoDismiss.Tick += (_, _) => SlideClose();
        if (secs > 0) _autoDismiss.Start();

        Tasky.Services.SoundPlayer.Play(App.Settings.Current.NotifTune);
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        _autoDismiss.Stop();
        int minutes = (SnoozeBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag is string t
                      && int.TryParse(t, out int m) ? m : 5;
        App.Notifications?.ScheduleSnooze(_taskId, _taskTitle, DateTime.Now.AddMinutes(minutes));
        SlideClose();
    }

    private void OpenTask_Click(object sender, RoutedEventArgs e)
    {
        _autoDismiss.Stop();
        if (Application.Current.MainWindow is MainWindow mw)
        {
            var vm = App.ViewModel?.Tasks.FirstOrDefault(t => t.Id == _taskId)
                  ?? App.ViewModel?.CompletedTasks.FirstOrDefault(t => t.Id == _taskId);
            mw.ShowFromTray();
            mw.Activate();
            if (vm is not null) mw.OpenTaskDetail(vm);
        }
        SlideClose();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _autoDismiss.Stop();
        SlideClose();
    }

    private void SlideClose()
    {
        _autoDismiss.Stop();

        // Slide out: X 0→360 (exits to the right) + fade out
        var slide = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(280))
            { EasingFunction = EaseIn };
        var fade  = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280))
            { EasingFunction = EaseIn };
        fade.Completed += (_, _) => Close();

        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, slide);
        BeginAnimation(OpacityProperty, fade);
    }
}
