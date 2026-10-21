using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tasky.Helpers;
using Tasky.Models;

namespace Tasky.Controls;

/// <summary>
/// Shows attachments as thumbnails and lets the user open, remove and add them
/// (file picker, Ctrl+V paste via <see cref="TryPaste"/>, drag and drop).
/// The panel never touches storage itself: the host handles the events and then
/// calls <see cref="SetAttachments"/> with the refreshed list.
/// </summary>
public partial class AttachmentsPanel : System.Windows.Controls.UserControl
{
    private const int CardWidth = 96, CardHeight = 110, ThumbnailDecodeWidth = 160;

    private IReadOnlyList<IAttachment> _items = Array.Empty<IAttachment>();

    /// <summary>The user picked or dropped files to attach.</summary>
    public event Action<IReadOnlyList<string>>? FilesAdded;

    /// <summary>The user pasted an image (e.g. a screenshot) to attach.</summary>
    public event Action<BitmapSource>? ImagePasted;

    /// <summary>The user removed an attachment.</summary>
    public event Action<IAttachment>? DeleteRequested;

    public AttachmentsPanel() => InitializeComponent();

    public void SetAttachments(IReadOnlyList<IAttachment> items)
    {
        _items = items;
        Thumbs.Children.Clear();
        foreach (var item in items) Thumbs.Children.Add(BuildCard(item));
        EmptyLabel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Attaches whatever image or files are on the clipboard. Returns false when the
    /// clipboard holds neither, so the host can let a normal text paste go ahead.
    /// </summary>
    public bool TryPaste()
    {
        if (SafeClipboard.ContainsImage() && SafeClipboard.GetImage() is { } image)
        {
            ImagePasted?.Invoke(image);
            return true;
        }
        if (SafeClipboard.ContainsFileDropList() && SafeClipboard.GetFileDropList() is { Count: > 0 } files)
        {
            FilesAdded?.Invoke(files.Cast<string>().ToList());
            return true;
        }
        return false;
    }

    // ── Adding ────────────────────────────────────────────────────────────

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title       = "Choose files to attach",
            Filter      = "All files|*.*|Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            Multiselect = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) FilesAdded?.Invoke(dialog.FileNames);
    }

    private void Panel_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Panel_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            FilesAdded?.Invoke(files.Where(System.IO.File.Exists).ToList());
    }

    // ── Thumbnails ────────────────────────────────────────────────────────

    private FrameworkElement BuildCard(IAttachment item)
    {
        var card = new Border
        {
            Width = CardWidth, Height = CardHeight, Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand, ToolTip = item.FileName
        };
        card.SetResourceReference(Border.BorderBrushProperty, "BorderColor");
        card.SetResourceReference(Border.BackgroundProperty,  "CardBackground");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var thumbnail = ImageFiles.IsImage(item.FilePath) ? ImageFiles.Load(item.FilePath, ThumbnailDecodeWidth) : null;
        if (thumbnail is not null)
        {
            stack.Children.Add(new Image
            {
                Source = thumbnail, Width = 80, Height = 64, Stretch = Stretch.UniformToFill,
                Margin = new Thickness(0, 6, 0, 4)
            });
        }
        else
        {
            stack.Children.Add(new TextBlock
            {
                Text = "📄", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 4)
            });
        }
        var name = new TextBlock
        {
            Text = item.FileName, FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(4, 0, 4, 4)
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
        stack.Children.Add(name);

        var remove = new Button
        {
            Content = "✕", Width = 18, Height = 18, FontSize = 10, ToolTip = "Remove",
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 2, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 24, 39)),
            Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68))
        };
        remove.Click += (_, e) => { e.Handled = true; DeleteRequested?.Invoke(item); };

        var grid = new Grid();
        grid.Children.Add(stack);
        grid.Children.Add(remove);
        card.Child = grid;
        card.MouseLeftButtonUp += (_, _) => Open(item);
        return card;
    }

    private void Open(IAttachment item)
    {
        if (!ImageFiles.IsImage(item.FilePath))
        {
            FileLauncher.Open(item.FilePath);
            return;
        }
        var images = _items.Where(a => ImageFiles.IsImage(a.FilePath)).ToList();
        int index = Math.Max(0, images.FindIndex(a => a.Id == item.Id));
        new Windows.ImageViewerWindow(images, index) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
}
