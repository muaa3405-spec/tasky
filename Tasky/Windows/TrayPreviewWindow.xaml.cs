using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Tasky.ViewModels;

namespace Tasky.Windows;

public partial class TrayPreviewWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DispatcherTimer _timerUpdate;
    private DispatcherTimer? _hideTimer;
    private DispatcherTimer? _mouseWatcher;

    public TrayPreviewWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();

        _timerUpdate = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timerUpdate.Tick += (_, _) => UpdateContent();
    }

    public void ShowNear(System.Drawing.Point cursorPos)
    {
        CancelHide();
        UpdateContent();

        // Position above the tray area
        var wa = SystemParameters.WorkArea;
        double left = cursorPos.X - ActualWidth / 2;
        double top  = wa.Bottom - ActualHeight - 60;

        if (left + ActualWidth > wa.Right) left = wa.Right - ActualWidth - 4;
        if (left < wa.Left)               left = wa.Left + 4;

        Left = left;
        Top  = top;

        if (!IsVisible)
        {
            Show();
            var sb = (Storyboard)CardBorder.Resources["FadeIn"];
            CardBorder.BeginStoryboard(sb);
            _timerUpdate.Start();
        }

        // Start polling cursor so we hide when mouse drifts away
        // (NotifyIcon has no MouseLeave event)
        _mouseWatcher?.Stop();
        _mouseWatcher = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _mouseWatcher.Tick += WatchCursor;
        _mouseWatcher.Start();
    }

    private void WatchCursor(object? sender, EventArgs e)
    {
        if (!IsVisible) { _mouseWatcher?.Stop(); return; }

        var cursor = System.Windows.Forms.Cursor.Position;

        // Is cursor inside this window?
        var winRect = new System.Drawing.Rectangle(
            (int)Left, (int)Top, (int)ActualWidth, (int)ActualHeight);
        if (winRect.Contains(cursor)) return;

        // Is cursor in the taskbar/tray strip at the very bottom?
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
        if (cursor.Y >= screen.Bottom - 48) return;

        // Cursor is elsewhere — hide
        _mouseWatcher?.Stop();
        ScheduleHide();
    }

    public void ScheduleHide()
    {
        _hideTimer?.Stop();
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            DoHide();
        };
        _hideTimer.Start();
    }

    public void CancelHide()
    {
        _hideTimer?.Stop();
        _mouseWatcher?.Stop();
    }

    private void DoHide()
    {
        _mouseWatcher?.Stop();
        _timerUpdate.Stop();
        var sb = (Storyboard)CardBorder.Resources["FadeOut"];
        sb.Completed += (_, _) => Hide();
        CardBorder.BeginStoryboard(sb);
    }

    private void UpdateContent()
    {
        var active = _vm.ActiveTask;
        if (active is not null)
        {
            ActiveTaskTitle.Text       = active.Title.Length > 32
                ? active.Title[..32] + "…"
                : active.Title;
            ActiveTaskTitle.Foreground = System.Windows.Media.Brushes.White;
            ActiveTaskTimer.Text       = active.ElapsedDisplay;
            ActiveTaskTimer.Visibility = Visibility.Visible;
        }
        else
        {
            ActiveTaskTitle.Text       = "No active task";
            ActiveTaskTitle.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(156, 163, 175));
            ActiveTaskTimer.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenBtn_Click(object sender, RoutedEventArgs e)
    {
        DoHide();
        if (Application.Current.MainWindow is MainWindow mw)
            mw.ShowFromTray();
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => ScheduleHide();
}
