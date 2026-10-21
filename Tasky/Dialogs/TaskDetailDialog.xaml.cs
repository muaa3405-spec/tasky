using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Tasky.Helpers;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class TaskDetailDialog : Window
{
    private const int ThumbnailDecodeWidth = 200;

    private int _taskId;
    private string _taskTitle = string.Empty;

    public TaskDetailDialog(TaskItemViewModel vm, MainViewModel mainVm)
    {
        _taskId    = vm.Id;
        _taskTitle = vm.Title;
        InitializeComponent();
        ThemeHelper.Attach(this);
        DialogLayoutHelper.FitToWorkArea(this);

        // ID + title
        TaskNumberLabel.Text = vm.TaskNumberDisplay;
        TitleLabel.Text      = vm.Title;

        // Description
        DescLabel.Text = string.IsNullOrWhiteSpace(vm.Description)
            ? "(no description)" : vm.Description;

        // Status badge
        if (vm.IsCompleted)
        {
            StatusLabel.Text       = "✔ Completed";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(34, 197, 94));
        }
        else if (vm.IsActive)
        {
            StatusLabel.Text       = vm.IsPaused ? "⏸ Paused" : "▶ Active";
            StatusBadge.Background = new SolidColorBrush(
                vm.IsPaused ? Color.FromRgb(245, 158, 11) : Color.FromRgb(34, 197, 94));
        }
        else
        {
            StatusLabel.Text       = "○ Pending";
            StatusBadge.Background = new SolidColorBrush(Color.FromRgb(107, 114, 128));
        }

        // Time
        TimeLabel.Text = vm.TimeDisplay == "—"
            ? "Not tracked yet"
            : vm.TimeDisplay;

        // Hide reminder section for completed tasks
        if (vm.IsCompleted)
            QuickRemindCard.Visibility = Visibility.Collapsed;

        // Created + start date
        CreatedLabel.Text = $"Created: {vm.Model.CreatedAt:MMM d yyyy}";
        DueLabel.Text = vm.DueDate.HasValue
            ? vm.DueDateTag.Length > 0 ? $"{vm.DueDateTag} ({vm.DueDate.Value:MMM d yyyy})" : vm.DueDate.Value.ToString("MMM d yyyy")
            : "Not set";

        // Attachments
        var attachments = mainVm.GetAttachments(vm.Id);
        if (attachments.Count == 0)
        {
            AttachHeader.Visibility  = Visibility.Collapsed;
            AttachPanel.Visibility   = Visibility.Collapsed;
            NoAttachLabel.Visibility = Visibility.Visible;
        }
        else
        {
            NoAttachLabel.Visibility = Visibility.Collapsed;
            foreach (var att in attachments)
            {
                bool isImg = ImageFiles.IsImage(att.FilePath);

                var card = new Border
                {
                    Width = 120, Height = 130,
                    Margin = new Thickness(0, 0, 8, 8),
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                card.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
                card.SetResourceReference(Border.BackgroundProperty,  "CardBackground");

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

                if (isImg && File.Exists(att.FilePath))
                {
                    var img = new System.Windows.Controls.Image
                    {
                        Width = 100, Height = 80,
                        Stretch = Stretch.UniformToFill,
                        Margin = new Thickness(0, 6, 0, 4)
                    };
                    img.Source = ImageFiles.Load(att.FilePath, ThumbnailDecodeWidth);
                    stack.Children.Add(img);
                }
                else
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "📄", FontSize = 30,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        Margin = new Thickness(0, 14, 0, 4)
                    });
                }

                stack.Children.Add(new TextBlock
                {
                    Text = att.FileName,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 4)
                });

                card.Child = stack;
                card.MouseLeftButtonUp += (_, _) =>
                {
                    if (!File.Exists(att.FilePath)) return;
                    if (isImg)
                    {
                        var images = attachments.Where(a => ImageFiles.IsImage(a.FilePath)).ToList();
                        int idx = images.FindIndex(a => a.Id == att.Id);
                        new Windows.ImageViewerWindow(images, idx >= 0 ? idx : 0) { Owner = this }.ShowDialog();
                    }
                    else
                    {
                        FileLauncher.Open(att.FilePath);
                    }
                };

                AttachPanel.Children.Add(card);
            }
        }
    }

    private void Window_ContentRendered(object sender, EventArgs e)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(280);

        // Fade in
        var fade = new DoubleAnimation(0, 1, duration) { EasingFunction = ease };
        BeginAnimation(OpacityProperty, fade);

        // Slide up: Y 18 → 0
        var slide = new DoubleAnimation(18, 0, duration) { EasingFunction = ease };
        SlideTransform.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void QuickRemind_Click(object sender, RoutedEventArgs e)
    {
        int minutes = (QuickRemindBox.SelectedItem as ComboBoxItem)?.Tag is string t
                      && int.TryParse(t, out int m) ? m : 5;
        var fireAt = DateTime.Now.AddMinutes(minutes);
        App.Notifications?.ScheduleSnooze(_taskId, _taskTitle, fireAt);

        // Show green confirmation, then close after 1.2 s
        QuickRemindCard.Visibility = Visibility.Collapsed;
        RemindSetLabel.Text        = $"Reminder set for {fireAt:HH:mm}";
        RemindSetCard.Visibility   = Visibility.Visible;

        var timer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) => { timer.Stop(); Close(); };
        timer.Start();
    }
}
