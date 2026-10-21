using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Tasky.Models;

namespace Tasky.Windows;

public partial class ImageViewerWindow : Window
{
    private readonly IReadOnlyList<IAttachment> _all;
    private int _index;
    private double _zoom = 1.0;
    private const double ZoomStep = 0.15;

    public ImageViewerWindow(IReadOnlyList<IAttachment> attachments, int startIndex = 0)
    {
        _all   = attachments;
        _index = startIndex;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            LoadCurrent();
            BeginStoryboard((Storyboard)Resources["OpenAnim"]);
        };
    }

    // ─── Image loading ────────────────────────────────────────────────────

    private void LoadCurrent()
    {
        if (_all.Count == 0) return;
        var att = _all[_index];

        MainImage.Source = Tasky.Helpers.ImageFiles.Load(att.FilePath);

        TitleLabel.Text = att.FileName;
        FileLabel.Text  = $"{_index + 1} / {_all.Count}  •  {att.FileName}";
        PrevBtn.IsEnabled = _index > 0;
        NextBtn.IsEnabled = _index < _all.Count - 1;

        // Reset zoom
        _zoom = 1.0;
        ImageScale.ScaleX = ImageScale.ScaleY = 1.0;
    }

    // ─── Navigation ───────────────────────────────────────────────────────

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        if (_index > 0) { _index--; LoadCurrent(); }
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index < _all.Count - 1) { _index++; LoadCurrent(); }
    }

    // ─── Zoom ─────────────────────────────────────────────────────────────

    private void Scroll_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta > 0) ZoomBy(ZoomStep);
        else             ZoomBy(-ZoomStep);
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender,  RoutedEventArgs e) => ZoomBy(ZoomStep);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(-ZoomStep);
    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        _zoom = 1.0;
        AnimateZoom();
    }

    private void ZoomBy(double delta)
    {
        _zoom = Math.Clamp(_zoom + delta, 0.2, 5.0);
        AnimateZoom();
    }

    private void AnimateZoom()
    {
        var dur = new Duration(TimeSpan.FromMilliseconds(120));
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var sx = new DoubleAnimation(_zoom, dur) { EasingFunction = ease };
        var sy = new DoubleAnimation(_zoom, dur) { EasingFunction = ease };
        ImageScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, sx);
        ImageScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, sy);
    }

    // ─── Close ────────────────────────────────────────────────────────────

    private void Close_Click(object sender, RoutedEventArgs e)       => AnimatedClose();
    private void Backdrop_Click(object sender, MouseButtonEventArgs e) => AnimatedClose();

    private void AnimatedClose()
    {
        var sb = (Storyboard)Resources["CloseAnim"];
        BeginStoryboard(sb);
    }

    private void CloseAnim_Completed(object sender, EventArgs e) => Close();

    // ─── Keyboard ─────────────────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:              AnimatedClose(); break;
            case Key.Left:  Prev_Click(this, null!); break;
            case Key.Right: Next_Click(this, null!); break;
            case Key.OemPlus:  ZoomBy( ZoomStep); break;
            case Key.OemMinus: ZoomBy(-ZoomStep); break;
            case Key.D0:    ZoomReset_Click(this, null!); break;
        }
    }

    // Allow dragging the borderless window from the toolbar area
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.Source is System.Windows.Controls.Grid ||
            e.Source is System.Windows.Controls.TextBlock)
            try { DragMove(); } catch { }
    }
}
