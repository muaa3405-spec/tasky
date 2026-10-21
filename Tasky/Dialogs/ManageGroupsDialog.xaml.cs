using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class ManageGroupsDialog : Window
{
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
                Fill   = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = name,
                StrokeThickness = 0
            };

            // Highlight the initially-selected swatch
            if (hex == _selectedColor) SetSelected(ellipse);

            var capturedHex = hex;
            ellipse.MouseLeftButtonUp += (_, _) =>
            {
                _selectedColor = capturedHex;
                // Update all swatch borders
                foreach (Ellipse e in SwatchPanel.Children)
                    e.StrokeThickness = e == ellipse ? 2.5 : 0;
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
        EmptyLabel.Visibility  = _vm.Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = GroupNameBox.Text.Trim();
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

    private void GroupName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddGroup_Click(sender, new RoutedEventArgs());
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not int id) return;
        var group = _vm.Groups.FirstOrDefault(g => g.Id == id);
        if (group is null) return;

        var dlg = new ConfirmDialog("Delete Group",
            $"Delete \"{group.Name}\"?\n\nTasks will not be deleted — they will just lose their group.",
            "Delete", ConfirmType.Danger) { Owner = this };
        if (dlg.ShowDialog() == true) { _vm.DeleteGroup(id); RefreshList(); }
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();
}
