using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Tasky.Helpers;
using Tasky.Models;

namespace Tasky.Windows;

public partial class ImageViewerWindow : Window
{
    private const double ZoomStep = 0.15, MinZoom = 0.2, MaxZoom = 5.0;
    private const double ClosedScale = 0.90;

    private static readonly TimeSpan OpenDuration  = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan CloseDuration = TimeSpan.FromMilliseconds(160);
    private static readonly TimeSpan ZoomDuration  = TimeSpan.FromMilliseconds(120);

    private readonly IReadOnlyList<IAttachment> _all;
    private int _index;
    private double _zoom = 1.0;
    private bool _closing;

    /// <summary>Scales the whole viewer card for the open / close animation.</summary>
    private ScaleTransform ContentScale => (ScaleTransform)Root.RenderTransform!;

    /// <summary>Zooms the image.</summary>
    private ScaleTransform ImageScale => (ScaleTransform)MainImage.RenderTransform!;

    public ImageViewerWindow() : this(Array.Empty<IAttachment>()) { }

    public ImageViewerWindow(IReadOnlyList<IAttachment> attachments, int startIndex = 0)
    {
        _all   = attachments;
        _index = startIndex;
        InitializeComponent();
        // Like WPF's PreviewMouseWheel: zoom before the ScrollViewer scrolls.
        ImageScroller.AddHandler(PointerWheelChangedEvent, Scroll_MouseWheel, RoutingStrategies.Tunnel);
        Loaded += (_, _) =>
        {
            LoadCurrent();
            PlayOpenAnimation();
        };
    }

    // ─── Open / close animations ──────────────────────────────────────────

    /// <summary>Fade + scale in.</summary>
    private void PlayOpenAnimation()
    {
        var ease = new CubicEaseOut();
        _ = Animator.Fade(Root, 0, 1, OpenDuration);
        _ = Animator.Animate(ContentScale, ScaleTransform.ScaleXProperty, ClosedScale, 1, OpenDuration, ease);
        _ = Animator.Animate(ContentScale, ScaleTransform.ScaleYProperty, ClosedScale, 1, OpenDuration, ease);
    }

    /// <summary>Fade + scale out, then close.</summary>
    private async void AnimatedClose()
    {
        if (_closing) return;
        _closing = true;
        await Task.WhenAll(
            Animator.Fade(Root, Root.Opacity, 0, CloseDuration),
            Animator.Animate(ContentScale, ScaleTransform.ScaleXProperty, ContentScale.ScaleX, ClosedScale, CloseDuration),
            Animator.Animate(ContentScale, ScaleTransform.ScaleYProperty, ContentScale.ScaleY, ClosedScale, CloseDuration));
        Close();
    }

    // ─── Image loading ────────────────────────────────────────────────────

    private void LoadCurrent()
    {
        if (_all.Count == 0) return;
        var att = _all[_index];

        MainImage.Source = ImageFiles.Load(att.FilePath);

        TitleLabel.Text = att.FileName;
        FileLabel.Text  = $"{_index + 1} / {_all.Count}  •  {att.FileName}";
        PrevBtn.IsEnabled = _index > 0;
        NextBtn.IsEnabled = _index < _all.Count - 1;

        // Reset zoom
        _zoom = 1.0;
        ImageScale.ScaleX = ImageScale.ScaleY = 1.0;
    }

    // ─── Navigation ───────────────────────────────────────────────────────

    private void Prev_Click(object? sender, RoutedEventArgs? e)
    {
        if (_index > 0) { _index--; LoadCurrent(); }
    }

    private void Next_Click(object? sender, RoutedEventArgs? e)
    {
        if (_index < _all.Count - 1) { _index++; LoadCurrent(); }
    }

    // ─── Zoom ─────────────────────────────────────────────────────────────

    private void Scroll_MouseWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0) return; // horizontal-only scroll (e.g. trackpad swipe) is not a zoom
        ZoomBy(e.Delta.Y > 0 ? ZoomStep : -ZoomStep);
        e.Handled = true;
    }

    private void ZoomIn_Click(object? sender,  RoutedEventArgs e) => ZoomBy(ZoomStep);
    private void ZoomOut_Click(object? sender, RoutedEventArgs e) => ZoomBy(-ZoomStep);
    private void ZoomReset_Click(object? sender, RoutedEventArgs? e)
    {
        _zoom = 1.0;
        AnimateZoom();
    }

    private void ZoomBy(double delta)
    {
        _zoom = Math.Clamp(_zoom + delta, MinZoom, MaxZoom);
        AnimateZoom();
    }

    private void AnimateZoom()
    {
        var ease = new CubicEaseOut();
        _ = Animator.Animate(ImageScale, ScaleTransform.ScaleXProperty, ImageScale.ScaleX, _zoom, ZoomDuration, ease);
        _ = Animator.Animate(ImageScale, ScaleTransform.ScaleYProperty, ImageScale.ScaleY, _zoom, ZoomDuration, ease);
    }

    // ─── Close ────────────────────────────────────────────────────────────

    private void Close_Click(object? sender, RoutedEventArgs e) => AnimatedClose();

    private void Backdrop_Click(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) AnimatedClose();
    }

    // ─── Keyboard ─────────────────────────────────────────────────────────

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:   AnimatedClose(); break;
            case Key.Left:     Prev_Click(this, null); break;
            case Key.Right:    Next_Click(this, null); break;
            case Key.OemPlus:  ZoomBy( ZoomStep); break;
            case Key.OemMinus: ZoomBy(-ZoomStep); break;
            case Key.D0:       ZoomReset_Click(this, null); break;
        }
    }

    // Allow dragging the borderless window from the toolbar area.
    // Like WPF, only the window's own panels and labels count — not parts inside a
    // control's template (e.g. the ScrollViewer's grid).
    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is (Grid or TextBlock) and StyledElement { TemplatedParent: null })
            BeginMoveDrag(e);
    }
}
