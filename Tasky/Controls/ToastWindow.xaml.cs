using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Tasky.Controls;

public enum ToastType { Success, Info, Warning, Error }

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };

    public ToastWindow(string title, string message, ToastType type = ToastType.Info)
    {
        InitializeComponent();
        TitleLabel.Text   = title;
        MessageLabel.Text = message;
        ApplyType(type);

        Loaded += (_, _) =>
        {
            PositionWindow();
            BeginStoryboard((Storyboard)Resources["FadeIn"]);
            _timer.Tick += (_, _) => FadeOut();
            _timer.Start();
        };
    }

    private void ApplyType(ToastType type)
    {
        var (icon, color) = type switch
        {
            ToastType.Success => ("✔", Color.FromRgb(34, 197, 94)),
            ToastType.Warning => ("⚠", Color.FromRgb(245, 158, 11)),
            ToastType.Error   => ("✕", Color.FromRgb(239, 68, 68)),
            _                 => ("ℹ", Color.FromRgb(59, 130, 246)),
        };
        IconLabel.Text              = icon;
        AccentBar.Background        = new SolidColorBrush(color);
        IconLabel.Foreground        = new SolidColorBrush(color);

        // Adapt background to app theme
        bool dark = !(Application.Current is App app) || app.IsDarkTheme;
        RootBg.Color = dark
            ? Color.FromRgb(30,  41,  59)   // slate-800
            : Color.FromRgb(255, 255, 255);  // white
        if (!dark)
        {
            TitleLabel.Foreground   = new SolidColorBrush(Color.FromRgb(17,  24,  39));
            MessageLabel.Foreground = new SolidColorBrush(Color.FromRgb(107, 114, 128));
        }
    }

    private void PositionWindow()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right  - ActualWidth  - 20;
        Top  = wa.Bottom - ActualHeight - 20;
    }

    private void FadeOut()
    {
        _timer.Stop();
        var sb = (Storyboard)Resources["FadeOut"];
        sb.Completed += (_, _) => Close();
        BeginStoryboard(sb);
    }
}
