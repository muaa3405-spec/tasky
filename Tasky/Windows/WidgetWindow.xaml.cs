using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Tasky.ViewModels;

namespace Tasky.Windows;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _vm;
    private TaskItemViewModel? _tracked;

    public WidgetWindow(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top  = workArea.Bottom - 80;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshActive();
        _vm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTask))
            Dispatcher.Invoke(RefreshActive);
    }

    private void RefreshActive()
    {
        if (_tracked != null) _tracked.PropertyChanged -= Tracked_PropertyChanged;

        _tracked = _vm.ActiveTask;

        if (_tracked is null)
        {
            NoActiveText.Visibility = Visibility.Visible;
            ActivePanel.Visibility  = Visibility.Collapsed;
        }
        else
        {
            NoActiveText.Visibility = Visibility.Collapsed;
            ActivePanel.Visibility  = Visibility.Visible;
            ActiveTitle.Text        = _tracked.Title;
            UpdateTimerDisplay();
            _tracked.PropertyChanged += Tracked_PropertyChanged;
        }
    }

    private void Tracked_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TaskItemViewModel.ElapsedDisplay)
                           or nameof(TaskItemViewModel.IsPaused))
            Dispatcher.Invoke(UpdateTimerDisplay);
    }

    private void UpdateTimerDisplay()
    {
        if (_tracked is null) return;
        ActiveTimer.Text  = _tracked.ElapsedDisplay;
        // Timer icon: ⏱ running (green) | ⏸ paused (amber)
        TimerIcon.Text      = _tracked.IsPaused ? "⏸" : "⏱";
        TimerIcon.Foreground = _tracked.IsPaused
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 197, 94));
        ActiveTimer.Foreground = TimerIcon.Foreground;
        // Pause button: ⏸ when running, ▶ when paused
        PauseIcon.Text = _tracked.IsPaused ? "▶" : "⏸";
    }

    private void PauseResume_Click(object sender, RoutedEventArgs e)
    {
        if (_tracked is null) return;
        _vm.TogglePause(_tracked);
    }

    private void Widget_Complete_Click(object sender, RoutedEventArgs e)
    {
        if (_tracked is null) return;
        _vm.CompleteTask(_tracked, out _);

        // Auto-close widget when no active task remains
        if (_vm.ActiveTask is null)
            Close();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (System.Windows.Application.Current.MainWindow is MainWindow mw)
            mw.ShowFromTray();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    protected override void OnClosing(CancelEventArgs e)
    {
        _vm.PropertyChanged -= Vm_PropertyChanged;
        if (_tracked != null) _tracked.PropertyChanged -= Tracked_PropertyChanged;
        base.OnClosing(e);
    }
}
