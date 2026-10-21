using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tasky.Helpers;
using Tasky.Models;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class EditTaskDialog : Window
{
    private const int ThumbnailDecodeWidth = 160;

    private readonly TaskItemViewModel _vm;
    private readonly MainViewModel? _mainVm;

    public EditTaskDialog(TaskItemViewModel vm, MainViewModel? mainVm = null)
    {
        _vm     = vm;
        _mainVm = mainVm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);
        Loaded += (_, _) => DatePickerHelper.Subscribe(DueDatePicker);

        TitleBox.Text              = vm.Title;
        DescBox.Text               = vm.Description;
        DueDatePicker.SelectedDate = vm.DueDate ?? DateTime.Today;

        // Populate group picker
        GroupPicker.Items.Add(new ComboBoxItem { Content = "(No group)", Tag = (int?)null });
        if (_mainVm is not null)
        {
            foreach (var g in _mainVm.Groups)
            {
                // Build content: coloured dot + group name (same as filter chips)
                System.Windows.Media.Color dotColor;
                try { dotColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(g.Color); }
                catch { dotColor = System.Windows.Media.Color.FromRgb(107, 114, 128); }

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width  = 10, Height = 10,
                    Fill   = new System.Windows.Media.SolidColorBrush(dotColor),
                    Margin = new System.Windows.Thickness(0, 0, 7, 0),
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                var label = new System.Windows.Controls.TextBlock
                {
                    Text = g.Name,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                var panel = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Children    = { dot, label }
                };

                GroupPicker.Items.Add(new ComboBoxItem
                {
                    Content = panel,
                    Tag     = (int?)g.Id
                });
            }
        }
        // Select current group
        for (int i = 0; i < GroupPicker.Items.Count; i++)
        {
            if (GroupPicker.Items[i] is ComboBoxItem ci && (int?)ci.Tag == vm.GroupId)
            {
                GroupPicker.SelectedIndex = i;
                break;
            }
        }
        if (GroupPicker.SelectedIndex < 0) GroupPicker.SelectedIndex = 0;

        // Select current recurrence
        int recVal = (int)vm.Recurrence;
        foreach (ComboBoxItem item in RecurrencePicker.Items)
        {
            if (item.Tag is string s && int.TryParse(s, out int v) && v == recVal)
            {
                RecurrencePicker.SelectedItem = item;
                break;
            }
        }
        if (RecurrencePicker.SelectedIndex < 0) RecurrencePicker.SelectedIndex = 0;

        Loaded += (_, _) =>
        {
            TitleBox.Focus();
            TitleBox.SelectAll();
        };

        LoadAttachments();
    }

    // ─── Attachments ──────────────────────────────────────────────────────

    private void LoadAttachments()
    {
        if (_mainVm is null)
        {
            // No view-model context — hide the whole section.
            AddAttachBtn.Visibility   = Visibility.Collapsed;
            NoAttachLabel.Visibility  = Visibility.Collapsed;
            return;
        }

        AttachThumbs.Children.Clear();
        var list = _mainVm.GetAttachments(_vm.Id);
        NoAttachLabel.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var att in list)
            AttachThumbs.Children.Add(BuildCard(att, list));
    }

    private FrameworkElement BuildCard(TaskAttachment att, System.Collections.Generic.List<TaskAttachment> all)
    {
        bool isImg = ImageFiles.IsImage(att.FilePath);

        var border = new Border
        {
            Width = 96, Height = 110, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        border.SetResourceReference(Border.BackgroundProperty,  "CardBackground");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (isImg && File.Exists(att.FilePath))
        {
            var img = new System.Windows.Controls.Image
            {
                Width = 80, Height = 64, Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 6, 0, 4)
            };
            img.Source = ImageFiles.Load(att.FilePath, ThumbnailDecodeWidth);
            stack.Children.Add(img);
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "📄", FontSize = 28,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 4)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = att.FileName, FontSize = 9, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 4), MaxHeight = 26,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var del = new System.Windows.Controls.Button
        {
            Content = "✕", Width = 18, Height = 18, FontSize = 10,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment   = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0), Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 24, 39)),
            Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            ToolTip = "Remove attachment"
        };
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

        border.MouseLeftButtonUp += (_, _) =>
        {
            if (!File.Exists(att.FilePath)) return;
            if (isImg)
            {
                var images = all.Where(a => ImageFiles.IsImage(a.FilePath)).ToList();
                int idx = images.FindIndex(a => a.Id == att.Id);
                new Windows.ImageViewerWindow(images, idx >= 0 ? idx : 0) { Owner = this }.ShowDialog();
            }
            else
            {
                FileLauncher.Open(att.FilePath);
            }
        };

        return border;
    }

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (_mainVm is null) return;
        var dlg = new OpenFileDialog
        {
            Title = "Select image(s) to attach",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) _mainVm.SaveAttachment(_vm.Id, f);
        _mainVm.RefreshAttachmentCount(_vm);
        LoadAttachments();
        InAppNotificationService.Success($"{dlg.FileNames.Length} file(s) attached.");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text))
        {
            InAppNotificationService.Warning("Title cannot be empty.");
            return;
        }
        _vm.Title       = TitleBox.Text.Trim();
        _vm.Description = DescBox.Text.Trim();
        _vm.DueDate     = DueDatePicker.SelectedDate ?? DateTime.Today;

        // Group
        if (GroupPicker.SelectedItem is ComboBoxItem gci)
            _vm.GroupId = gci.Tag as int?;

        // Recurrence
        if (RecurrencePicker.SelectedItem is ComboBoxItem rci
            && rci.Tag is string s && int.TryParse(s, out int rv))
            _vm.Recurrence = (RecurrenceType)rv;

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
