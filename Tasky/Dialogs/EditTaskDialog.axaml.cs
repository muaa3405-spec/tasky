using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Tasky.Helpers;
using Tasky.Models;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class EditTaskDialog : Window
{
    private const int ThumbnailDecodeWidth = 160;
    private static readonly Color FallbackGroupColor = Color.FromRgb(107, 114, 128);

    private readonly TaskItemViewModel _vm = null!;
    private readonly MainViewModel? _mainVm;

    /// <summary>One entry of the group picker: "(No group)" has no id and no dot.</summary>
    private sealed record GroupOption(int? Id, string Name, IBrush? DotBrush);

    [Obsolete("XAML previewer only")]
    public EditTaskDialog() => InitializeComponent();

    public EditTaskDialog(TaskItemViewModel vm, MainViewModel? mainVm = null)
    {
        _vm     = vm;
        _mainVm = mainVm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);

        TitleBox.Text              = vm.Title;
        DescBox.Text               = vm.Description;
        DueDatePicker.SelectedDate = vm.DueDate ?? DateTime.Today;

        PopulateGroupPicker(vm.GroupId);
        SelectRecurrence(vm.Recurrence);

        Opened += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };

        LoadAttachments();
    }

    // ─── Pickers ──────────────────────────────────────────────────────────

    private void PopulateGroupPicker(int? currentGroupId)
    {
        var options = new List<GroupOption> { new(null, "(No group)", null) };
        if (_mainVm is not null)
        {
            foreach (var g in _mainVm.Groups)
            {
                var dotColor = Color.TryParse(g.Color, out var c) ? c : FallbackGroupColor;
                options.Add(new GroupOption(g.Id, g.Name, new SolidColorBrush(dotColor)));
            }
        }

        // Coloured dot + group name (same as filter chips)
        GroupPicker.ItemTemplate = new FuncDataTemplate<GroupOption>((option, _) => BuildGroupContent(option));
        GroupPicker.ItemsSource  = options;

        int selected = options.FindIndex(o => o.Id == currentGroupId);
        GroupPicker.SelectedIndex = selected >= 0 ? selected : 0;
    }

    private static Control BuildGroupContent(GroupOption? option)
    {
        if (option is null) return new TextBlock();
        if (option.DotBrush is null) return new TextBlock { Text = option.Name };

        var dot = new Ellipse
        {
            Width  = 10, Height = 10,
            Fill   = option.DotBrush,
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = option.Name,
            VerticalAlignment = VerticalAlignment.Center
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children    = { dot, label }
        };
    }

    private void SelectRecurrence(RecurrenceType recurrence)
    {
        int recVal = (int)recurrence;
        foreach (var item in RecurrencePicker.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is string s && int.TryParse(s, out int v) && v == recVal)
            {
                RecurrencePicker.SelectedItem = item;
                break;
            }
        }
        if (RecurrencePicker.SelectedIndex < 0) RecurrencePicker.SelectedIndex = 0;
    }

    // ─── Attachments ──────────────────────────────────────────────────────

    private void LoadAttachments()
    {
        if (_mainVm is null)
        {
            // No view-model context — hide the whole section.
            AddAttachBtn.IsVisible  = false;
            NoAttachLabel.IsVisible = false;
            return;
        }

        AttachThumbs.Children.Clear();
        var list = _mainVm.GetAttachments(_vm.Id);
        NoAttachLabel.IsVisible = list.Count == 0;
        foreach (var att in list)
            AttachThumbs.Children.Add(BuildCard(att, list));
    }

    private Control BuildCard(TaskAttachment att, List<TaskAttachment> all)
    {
        bool isImg = ImageFiles.IsImage(att.FilePath);

        var border = new Border
        {
            Width = 96, Height = 110, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        border.WithResource(Border.BorderBrushProperty, "BorderColor");
        border.WithResource(Border.BackgroundProperty,  "CardBackground");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (isImg && File.Exists(att.FilePath))
        {
            stack.Children.Add(new Image
            {
                Width = 80, Height = 64, Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 6, 0, 4),
                Source = ImageFiles.Load(att.FilePath, ThumbnailDecodeWidth)
            });
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "📄", FontSize = 28,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 4)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = att.FileName, FontSize = 9, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 4), MaxHeight = 26,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        // Stock WPF button look with a dark translucent fill and red cross
        var del = new Button
        {
            Theme = UiResources.Theme("PlainButton"),
            Content = "✕", Width = 18, Height = 18, FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0), Cursor = new Cursor(StandardCursorType.Hand),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 24, 39)),
            Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
        };
        ToolTip.SetTip(del, "Remove attachment");
        del.Click += (_, _) =>
        {
            _mainVm!.DeleteAttachment(att);
            _mainVm.RefreshAttachmentCount(_vm);
            LoadAttachments();
        };

        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(del);
        border.Child = grid;

        border.PointerReleased += async (_, e) =>
        {
            if (e.InitialPressMouseButton != MouseButton.Left) return;
            if (!File.Exists(att.FilePath)) return;
            if (isImg)
            {
                var images = all.Where(a => ImageFiles.IsImage(a.FilePath)).ToList();
                int idx = images.FindIndex(a => a.Id == att.Id);
                await new Windows.ImageViewerWindow(images, idx >= 0 ? idx : 0).ShowDialog(this);
            }
            else
            {
                FileLauncher.Open(att.FilePath);
            }
        };

        return border;
    }

    private async void AddAttachment_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainVm is null) return;
        var files = await FileDialogs.PickFilesAsync(TopLevel.GetTopLevel(this),
            "Select image(s) to attach", [FileDialogs.Images, FileDialogs.AllFiles]);
        if (files.Count == 0) return;
        foreach (var f in files) _mainVm.SaveAttachment(_vm.Id, f);
        _mainVm.RefreshAttachmentCount(_vm);
        LoadAttachments();
        InAppNotificationService.Success($"{files.Count} file(s) attached.");
    }

    // ─── Save / cancel ────────────────────────────────────────────────────

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            InAppNotificationService.Warning("Title cannot be empty.");
            return;
        }
        _vm.Title       = TitleBox.Text.Trim();
        _vm.Description = (DescBox.Text ?? string.Empty).Trim();
        _vm.DueDate     = DueDatePicker.SelectedDate ?? DateTime.Today;

        // Group
        if (GroupPicker.SelectedItem is GroupOption group)
            _vm.GroupId = group.Id;

        // Recurrence
        if (RecurrencePicker.SelectedItem is ComboBoxItem rci
            && rci.Tag is string s && int.TryParse(s, out int rv))
            _vm.Recurrence = (RecurrenceType)rv;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
