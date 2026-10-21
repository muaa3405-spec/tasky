using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tasky.Helpers;
using Tasky.Models;
using Tasky.Services;
using Tasky.ViewModels;

namespace Tasky.Dialogs;

public partial class AttachmentDialog : Window
{
    private const int ThumbnailDecodeWidth = 200;

    private readonly int _taskId;
    private readonly MainViewModel _vm;

    public AttachmentDialog(int taskId, string taskTitle, MainViewModel vm)
    {
        _taskId = taskId;
        _vm = vm;
        InitializeComponent();
        ThemeHelper.Attach(this);
        HeaderLabel.Text = $"Attachments — {taskTitle}";
        LoadAttachments();
    }

    // ─── Load saved attachments ───────────────────────────────────────────

    private void LoadAttachments()
    {
        AttachPanel.Children.Clear();
        var list = _vm.GetAttachments(_taskId);
        if (list.Count == 0)
        {
            SavedLabel.Visibility     = Visibility.Collapsed;
            NoAttachLabel.Visibility  = Visibility.Visible;
        }
        else
        {
            SavedLabel.Visibility     = Visibility.Visible;
            NoAttachLabel.Visibility  = Visibility.Collapsed;
            foreach (var att in list)
                AttachPanel.Children.Add(BuildCard(att));
        }
    }

    // ─── Attachment card ──────────────────────────────────────────────────

    private FrameworkElement BuildCard(TaskAttachment att)
    {
        var border = new Border
        {
            Width = 110, Height = 125, Margin = new Thickness(0, 0, 8, 8),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand
        };
        border.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        border.SetResourceReference(Border.BackgroundProperty,  "CardBackground");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var ext   = Path.GetExtension(att.FilePath).ToLowerInvariant();
        bool isImg = ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp";

        if (isImg && File.Exists(att.FilePath))
        {
            var img = new Image
            {
                Width = 94, Height = 76,
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
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 4)
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = att.FileName, FontSize = 10, TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 4)
        });

        // Delete × button
        var del = new System.Windows.Controls.Button
        {
            Content = "✕", FontSize = 10, Width = 20, Height = 20,
            VerticalAlignment   = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2, 2, 0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
        };
        del.Click += (_, _) => { _vm.DeleteAttachment(att); LoadAttachments(); };

        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(del);
        border.Child = grid;

        border.MouseLeftButtonUp += (_, _) => OpenInViewer(att);

        return border;
    }

    private void OpenInViewer(TaskAttachment clicked)
    {
        if (!File.Exists(clicked.FilePath)) return;

        if (ImageFiles.IsImage(clicked.FilePath))
        {
            // Images → native Tasky viewer (navigable, with zoom)
            // Pass only image attachments so prev/next skips non-image files
            var images = _vm.GetAttachments(_taskId)
                            .Where(a => ImageFiles.IsImage(a.FilePath))
                            .ToList();
            int index = images.FindIndex(a => a.Id == clicked.Id);
            var viewer = new Windows.ImageViewerWindow(images, index >= 0 ? index : 0) { Owner = this };
            viewer.ShowDialog();
        }
        else
        {
            // PDFs, Word docs, etc. → system default application
            FileLauncher.Open(clicked.FilePath);
        }
    }

    // ─── Drop zone click → open file picker ──────────────────────────────

    private void DropZone_Click(object sender, MouseButtonEventArgs e) => OpenFilePicker();

    private void AddFile_Click(object sender, RoutedEventArgs e) => OpenFilePicker();

    private void OpenFilePicker()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select image(s) to attach",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;
        foreach (var f in dlg.FileNames) _vm.SaveAttachment(_taskId, f);
        LoadAttachments();
        InAppNotificationService.Success($"{dlg.FileNames.Length} file(s) attached.");
    }

    // ─── Ctrl+V paste ─────────────────────────────────────────────────────

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            e.Handled = TryPasteFromClipboard();
    }

    private bool TryPasteFromClipboard()
    {
        if (SafeClipboard.ContainsImage())
        {
            var src = SafeClipboard.GetImage();
            if (src is null) return false;
            SaveBitmapSource(src);
            return true;
        }
        if (SafeClipboard.ContainsFileDropList() && SafeClipboard.GetFileDropList() is { } files)
        {
            int count = 0;
            foreach (string? f in files)
                if (f is not null && ImageFiles.IsImage(f)) { if (_vm.SaveAttachment(_taskId, f)) count++; }
            if (count > 0) { LoadAttachments(); InAppNotificationService.Success($"{count} image(s) pasted."); return true; }
        }
        InAppNotificationService.Info("No image in clipboard — copy a screenshot or image first.");
        return false;
    }

    // ─── Drag and drop ────────────────────────────────────────────────────

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        bool ok = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        // Highlight drop zone
        DropBorderBrush.Color = ok
            ? Color.FromRgb(59, 130, 246)   // PrimaryBlue
            : Color.FromRgb(55, 65,  81);
        DropBgBrush.Color = ok
            ? Color.FromRgb(30, 58, 95)
            : Color.FromRgb(31, 41, 55);
        DropIcon.Text  = ok ? "📂" : "📋";
        DropTitle.Text = ok ? "Release to attach" : "Click, paste (Ctrl+V) or drop images here";
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => ResetDropZone();

    private void Window_Drop(object sender, DragEventArgs e)
    {
        ResetDropZone();
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        int count = 0;
        foreach (var f in files.Where(ImageFiles.IsImage)) { if (_vm.SaveAttachment(_taskId, f)) count++; }
        if (count > 0) { LoadAttachments(); InAppNotificationService.Success($"{count} image(s) attached."); }
        else InAppNotificationService.Warning("No image files found — drop PNG, JPG, GIF, or WEBP files.");
    }

    private void ResetDropZone()
    {
        DropBorderBrush.Color = Color.FromRgb(55, 65, 81);
        DropBgBrush.Color     = Color.FromRgb(31, 41, 55);
        DropIcon.Text         = "📋";
        DropTitle.Text        = "Click, paste (Ctrl+V) or drop images here";
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private void SaveBitmapSource(BitmapSource src)
    {
        using var ms = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(src));
        enc.Save(ms);
        _vm.SaveAttachmentFromBytes(_taskId, ms.ToArray(), ".png");
        LoadAttachments();
        InAppNotificationService.Success("Image pasted and saved.");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
