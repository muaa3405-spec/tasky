using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Tasky.ViewModels;

namespace Tasky.Behaviors;

/// <summary>
/// Animated drag-to-reorder for the task ItemsControl.
/// Shows a ghost card that follows the cursor and a blue drop-line
/// indicating where the card will land — similar to Azure DevOps / Trello.
/// </summary>
public static class DragDropBehavior
{
    // ─── Attached property ───────────────────────────────────────────────

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(DragDropBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject o)          => (bool)o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject o, bool v)  => o.SetValue(IsEnabledProperty, v);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl ic) return;
        if ((bool)e.NewValue)
        {
            ic.PreviewMouseLeftButtonDown += OnMouseDown;
            ic.PreviewMouseMove           += OnMouseMove;
            ic.Drop                       += OnDrop;
            ic.DragOver                   += OnDragOver;
            ic.DragLeave                  += OnDragLeave;
            ic.AllowDrop = true;
        }
        else
        {
            ic.PreviewMouseLeftButtonDown -= OnMouseDown;
            ic.PreviewMouseMove           -= OnMouseMove;
            ic.Drop                       -= OnDrop;
            ic.DragOver                   -= OnDragOver;
            ic.DragLeave                  -= OnDragLeave;
        }
    }

    // ─── Drag state ──────────────────────────────────────────────────────

    private static Point                _startPoint;
    private static TaskItemViewModel?   _dragged;
    private static bool                 _dragging;

    // Visual feedback
    private static Window?              _ghost;
    private static DropLineAdorner?     _dropAdorner;
    private static AdornerLayer?        _adornerLayer;
    private static int                  _dropIndex = -1;

    // ─── Mouse handlers ──────────────────────────────────────────────────

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(null);
        _dragged    = GetVm(e.OriginalSource as DependencyObject);
        _dragging   = false;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragged is null) return;

        var delta = e.GetPosition(null) - _startPoint;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (!_dragging)
        {
            _dragging = true;
            ShowGhost(_dragged);
        }

        DragDrop.DoDragDrop((DependencyObject)sender,
            new DataObject(typeof(TaskItemViewModel), _dragged),
            DragDropEffects.Move);

        // Cleanup after drag ends (drop or cancel)
        HideGhost();
        HideDropLine();
        _dragged  = null;
        _dragging = false;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl ic) return;

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        // Move ghost to cursor
        MoveGhost(ic, e);

        // Calculate drop index & show indicator line
        _dropIndex = GetDropIndex(ic, e);
        ShowDropLine(ic, e, _dropIndex);
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
        => HideDropLine();

    private static void OnDrop(object sender, DragEventArgs e)
    {
        HideDropLine();
        HideGhost();

        if (sender is not ItemsControl ic) return;
        var dragged = e.Data.GetData(typeof(TaskItemViewModel)) as TaskItemViewModel;
        if (dragged is null || ic.DataContext is not MainViewModel vm) return;

        var list     = vm.Tasks;
        int oldIndex = list.IndexOf(dragged);
        if (oldIndex < 0) return;

        int newIndex;
        if (_dropIndex >= ic.Items.Count)
        {
            // Dropping past the last item → go to end.
            // Use list.Count so the shift-adjustment (newIndex--) lands on list.Count-1.
            newIndex = list.Count;
        }
        else
        {
            // Map the visual slot's VM back to its index in Tasks
            var targetVm = ic.Items[_dropIndex] as TaskItemViewModel;
            newIndex = targetVm is not null ? list.IndexOf(targetVm) : list.Count;
        }

        if (newIndex < 0 || oldIndex == newIndex) return;

        // ObservableCollection.Move(old, new): 'new' is the FINAL position
        // in the post-removal list. When new > old, removing old shifts the
        // insert point down by 1, so subtract 1 to compensate.
        if (newIndex > oldIndex) newIndex--;
        if (newIndex == oldIndex) return;

        // FLIP animation — capture positions BEFORE the move
        var beforePositions = CapturePositions(ic);

        list.Move(oldIndex, newIndex);
        vm.PersistSortOrder();    // saves to DB + refreshes VisibleTasks

        // After layout is complete, animate from old positions to new positions
        ic.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            () => AnimateFlip(ic, beforePositions));
    }

    // ─── FLIP animation ──────────────────────────────────────────────────

    /// <summary>Snapshot Y positions of all item containers relative to the ItemsControl.</summary>
    public static Dictionary<TaskItemViewModel, double> CapturePositions(ItemsControl ic)
    {
        var dict = new Dictionary<TaskItemViewModel, double>();
        for (int i = 0; i < ic.Items.Count; i++)
        {
            if (ic.Items[i] is not TaskItemViewModel vm) continue;
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el) continue;
            dict[vm] = el.TranslatePoint(new Point(0, 0), ic).Y;
        }
        return dict;
    }

    /// <summary>
    /// For each item that changed Y position, start it from the old Y and
    /// smoothly animate it to the new Y — giving the "cards sliding" effect.
    /// </summary>
    public static void AnimateFlip(ItemsControl ic,
        Dictionary<TaskItemViewModel, double> before)
    {
        // Quintic ease-out: starts fast, decelerates gracefully at the end
        var ease = new System.Windows.Media.Animation.QuinticEase
            { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
        var dur = new Duration(TimeSpan.FromMilliseconds(520));

        for (int i = 0; i < ic.Items.Count; i++)
        {
            if (ic.Items[i] is not TaskItemViewModel vm) continue;
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el) continue;

            double newY = el.TranslatePoint(new Point(0, 0), ic).Y;
            if (!before.TryGetValue(vm, out double oldY)) continue;

            double delta = oldY - newY;           // how far (and which direction) it moved
            if (Math.Abs(delta) < 2) continue;   // no visible change — skip

            // Offset the element back to where it was, then animate to 0
            var xform = new System.Windows.Media.TranslateTransform(0, delta);
            el.RenderTransform = xform;

            var anim = new System.Windows.Media.Animation.DoubleAnimation(delta, 0, dur)
            {
                EasingFunction = ease
            };
            // Clean up after animation
            anim.Completed += (_, _) =>
            {
                el.RenderTransform = System.Windows.Media.Transform.Identity;
            };
            xform.BeginAnimation(
                System.Windows.Media.TranslateTransform.YProperty, anim);
        }
    }

    // ─── Ghost window ────────────────────────────────────────────────────

    private static void ShowGhost(TaskItemViewModel vm)
    {
        var card = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(14, 10, 14, 10),
            Effect          = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.4, Color = Colors.Black },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text        = vm.TaskNumberDisplay + "  " + vm.Title,
                        Foreground  = Brushes.White,
                        FontSize    = 13,
                        FontWeight  = FontWeights.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth    = 320
                    },
                    new TextBlock
                    {
                        Text       = "↕  Drag to reorder",
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        FontSize   = 11,
                        Margin     = new Thickness(0, 4, 0, 0)
                    }
                }
            }
        };

        _ghost = new Window
        {
            WindowStyle      = WindowStyle.None,
            AllowsTransparency = true,
            Background       = Brushes.Transparent,
            IsHitTestVisible = false,
            Topmost          = true,
            ShowInTaskbar    = false,
            SizeToContent    = SizeToContent.WidthAndHeight,
            Opacity          = 0.88,
            Content          = card,
            Left             = -2000,   // offscreen until first DragOver
            Top              = 0
        };
        _ghost.Show();
    }

    private static void MoveGhost(FrameworkElement ic, DragEventArgs e)
    {
        if (_ghost is null) return;
        var screen = ic.PointToScreen(e.GetPosition(ic));
        _ghost.Left = screen.X + 16;
        _ghost.Top  = screen.Y + 16;
    }

    private static void HideGhost()
    {
        _ghost?.Close();
        _ghost = null;
    }

    // ─── Drop-line adorner ───────────────────────────────────────────────

    private static int GetDropIndex(ItemsControl ic, DragEventArgs e)
    {
        var pos = e.GetPosition(ic);
        for (int i = 0; i < ic.Items.Count; i++)
        {
            if (ic.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement el) continue;
            var topLeft = el.TranslatePoint(new Point(0, 0), ic);
            var mid     = topLeft.Y + el.ActualHeight / 2;
            if (pos.Y < mid) return i;
        }
        return ic.Items.Count;
    }

    private static void ShowDropLine(ItemsControl ic, DragEventArgs e, int index)
    {
        // Get the Y position for the line
        double y;
        if (index < ic.Items.Count &&
            ic.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement el)
            y = el.TranslatePoint(new Point(0, 0), ic).Y - 2;
        else if (index > 0 &&
            ic.ItemContainerGenerator.ContainerFromIndex(index - 1) is FrameworkElement prev)
            y = prev.TranslatePoint(new Point(0, prev.ActualHeight), ic).Y + 2;
        else
            y = 0;

        // Create or update the adorner
        if (_adornerLayer is null)
        {
            _adornerLayer = AdornerLayer.GetAdornerLayer(ic);
            if (_adornerLayer is null) return;
            _dropAdorner = new DropLineAdorner(ic);
            _adornerLayer.Add(_dropAdorner);
        }

        _dropAdorner?.UpdateY(y, ic.ActualWidth);
    }

    private static void HideDropLine()
    {
        if (_adornerLayer is not null && _dropAdorner is not null)
        {
            _adornerLayer.Remove(_dropAdorner);
            _dropAdorner  = null;
            _adornerLayer = null;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static TaskItemViewModel? GetVm(DependencyObject? el)
    {
        while (el is not null)
        {
            if (el is FrameworkElement { DataContext: TaskItemViewModel vm }) return vm;
            el = VisualTreeHelper.GetParent(el);
        }
        return null;
    }

    // ─── DropLineAdorner ─────────────────────────────────────────────────

    private sealed class DropLineAdorner : Adorner
    {
        private double _y;
        private double _width;

        private static readonly Brush LineBrush =
            new SolidColorBrush(Color.FromRgb(59, 130, 246)) { Opacity = 0.9 };
        private static readonly Pen   LinePen   = new(LineBrush, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

        public DropLineAdorner(UIElement parent) : base(parent)
        {
            IsHitTestVisible = false;
        }

        public void UpdateY(double y, double width)
        {
            _y     = y;
            _width = width;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            const double circleR = 5;
            const double margin  = 14;

            // Horizontal line
            dc.DrawLine(LinePen,
                new Point(margin + circleR * 2, _y),
                new Point(_width - margin, _y));

            // Circle at the left end
            dc.DrawEllipse(LineBrush, null,
                new Point(margin + circleR, _y), circleR, circleR);
        }
    }
}
