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
        RepeatEndPicker.SelectedDate = vm.RecurrenceEndDate;

        Opened += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };

        SetUpAttachments();
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

    private RecurrenceType SelectedRecurrence
        => RecurrencePicker.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int value)
            ? (RecurrenceType)value
            : RecurrenceType.None;

    private void RecurrencePicker_Changed(object? sender, SelectionChangedEventArgs e)
    {
        // Fires during InitializeComponent too, before the end-date section exists.
        if (RepeatEndSection is null) return;
        RepeatEndSection.IsVisible = SelectedRecurrence != RecurrenceType.None;
    }

    // ─── Attachments ──────────────────────────────────────────────────────

    private void SetUpAttachments()
    {
        if (_mainVm is null)
        {
            // No view-model context — hide the whole section.
            AttachmentsLabel.IsVisible = false;
            Attachments.IsVisible      = false;
            return;
        }

        Attachments.FilesAdded      += AttachFiles;
        Attachments.ImagePasted     += AttachPastedImage;
        Attachments.DeleteRequested += RemoveAttachment;
        Attachments.AcceptAttachmentsAnywhereIn(this);
        ReloadAttachments();
    }

    private void ReloadAttachments()
    {
        Attachments.SetAttachments(_mainVm!.GetAttachments(_vm.Id));
        _mainVm.RefreshAttachmentCount(_vm);
    }

    private void AttachFiles(IReadOnlyList<string> paths)
    {
        int saved = paths.Count(path => _mainVm!.SaveAttachment(_vm.Id, path));
        ReloadAttachments();
        if (saved > 0) InAppNotificationService.Success(saved == 1 ? "File attached." : $"{saved} files attached.");
    }

    private void AttachPastedImage(byte[] png)
    {
        if (_mainVm!.SaveAttachmentFromBytes(_vm.Id, png, ".png"))
            InAppNotificationService.Success("Image pasted and attached.");
        ReloadAttachments();
    }

    private void RemoveAttachment(IAttachment attachment)
    {
        if (attachment is not TaskAttachment taskAttachment) return;
        _mainVm!.DeleteAttachment(taskAttachment);
        ReloadAttachments();
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

        _vm.Recurrence        = SelectedRecurrence;
        _vm.RecurrenceEndDate = SelectedRecurrence == RecurrenceType.None ? null : RepeatEndPicker.SelectedDate;

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
