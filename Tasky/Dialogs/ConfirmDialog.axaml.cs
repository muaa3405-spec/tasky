using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tasky.Helpers;

namespace Tasky.Dialogs;

public enum ConfirmType { Danger, Warning, Info }

public partial class ConfirmDialog : Window
{
    public bool Confirmed { get; private set; }

    /// <summary>For the XAML loader / previewer.</summary>
    public ConfirmDialog() : this(string.Empty, string.Empty) { }

    public ConfirmDialog(
        string title,
        string message,
        string confirmLabel = "Confirm",
        ConfirmType type    = ConfirmType.Danger)
    {
        InitializeComponent();
        ThemeHelper.Attach(this);

        TitleLabel.Text    = title;
        MessageLabel.Text  = message;
        ConfirmBtn.Content = confirmLabel;

        var (icon, color) = type switch
        {
            ConfirmType.Warning => ("⚠️",  Color.FromRgb(245, 158, 11)),
            ConfirmType.Info    => ("ℹ️",   Color.FromRgb(59,  130, 246)),
            _                   => ("🗑️",  Color.FromRgb(239, 68,  68))   // Danger / Delete
        };

        IconLabel.Text = icon;

        // Danger confirm button gets a red background
        if (type == ConfirmType.Danger)
            ConfirmBtn.Background = new SolidColorBrush(Color.FromRgb(239, 68, 68));
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Confirmed = false;
        Close(false);
    }

    // Allow dragging the borderless window
    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }
}
