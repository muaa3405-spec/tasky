using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class ManageGroupsDialog : Window
{
    /// <summary>Group colour hex ("#3B82F6") → brush, for the colour dot in the list.</summary>
    public static readonly IValueConverter HexToBrush =
        new FuncValueConverter<string?, IBrush?>(hex =>
            hex is not null && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : null);

    private readonly MainViewModel _vm;
    private string _selectedColor = "#3B82F6";  // default blue

    // Palette of background colours for groups (20 unique colours)
    private static readonly (string Hex, string Name)[] Palette =
    {
        ("#3B82F6", "Blue"),    ("#8B5CF6", "Violet"), ("#EC4899", "Pink"),
        ("#EF4444", "Red"),     ("#F97316", "Orange"), ("#EAB308", "Yellow"),
        ("#22C55E", "Green"),   ("#14B8A6", "Teal"),   ("#6B7280", "Gray"),
        ("#0EA5E9", "Sky"),     ("#A855F7", "Purple"), ("#84CC16", "Lime"),
        // 8 new unique colours
        ("#F43F5E", "Rose"),    ("#10B981", "Emerald"), ("#6366F1", "Indigo"),
        ("#F59E0B", "Amber"),   ("#06B6D4", "Cyan"),    ("#D946EF", "Fuchsia"),
        ("#64748B", "Slate"),   ("#78716C", "Stone"),
    };

    [Obsolete("XAML previewer only")]
    public ManageGroupsDialog()
    {
        _vm = null!;
        InitializeComponent();
    }

    public ManageGroupsDialog(MainViewModel vm)
    {
        _vm = vm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        BuildSwatches();
        RefreshList();
    }

    private void BuildSwatches()
    {
        foreach (var (hex, name) in Palette)
        {
            var ellipse = new Ellipse
            {
                Width  = 26, Height = 26,
                Margin = new Thickness(0, 0, 6, 6),
                Fill   = new SolidColorBrush(Color.Parse(hex)),
                Cursor = new Cursor(StandardCursorType.Hand),
                StrokeThickness = 0
            };
            ToolTip.SetTip(ellipse, name);

            // Highlight the initially-selected swatch
            if (hex == _selectedColor) SetSelected(ellipse);

            var capturedHex = hex;
            ellipse.PointerReleased += (_, e) =>
            {
                if (e.InitialPressMouseButton != MouseButton.Left) return;
                _selectedColor = capturedHex;
                // Update all swatch borders
                foreach (var swatch in SwatchPanel.Children.OfType<Ellipse>())
                    swatch.StrokeThickness = swatch == ellipse ? 2.5 : 0;
            };

            SwatchPanel.Children.Add(ellipse);
        }
    }

    private static void SetSelected(Ellipse e)
    {
        e.Stroke          = new SolidColorBrush(Colors.White);
        e.StrokeThickness = 2.5;
    }

    private void RefreshList()
    {
        GroupsList.ItemsSource = null;
        GroupsList.ItemsSource = _vm.Groups;
        EmptyLabel.IsVisible   = _vm.Groups.Count == 0;
    }

    private void AddGroup_Click(object? sender, RoutedEventArgs e)
    {
        var name = (GroupNameBox.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            InAppNotificationService.Warning("Please enter a group name.");
            GroupNameBox.Focus();
            return;
        }
        _vm.AddGroup(name, _selectedColor);
        GroupNameBox.Text = string.Empty;
        GroupNameBox.Focus();
        RefreshList();
    }

    private void GroupName_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddGroup_Click(sender, new RoutedEventArgs());
    }

    private async void DeleteGroup_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not int id) return;
        var group = _vm.Groups.FirstOrDefault(g => g.Id == id);
        if (group is null) return;

        var dlg = new ConfirmDialog("Delete Group",
            $"Delete \"{group.Name}\"?\n\nTasks will not be deleted — they will just lose their group.",
            "Delete", ConfirmType.Danger);
        if (await dlg.ShowDialog<bool?>(this) == true) { _vm.DeleteGroup(id); RefreshList(); }
    }

    private void Done_Click(object? sender, RoutedEventArgs e) => Close();
}
