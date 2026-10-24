using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Tasky.Helpers;
using Tasky.ViewModels;

namespace Tasky.Behaviors;

/// <summary>
/// Animated drag-to-reorder for the task ItemsControl.
/// Shows a ghost card that follows the cursor and a blue drop-line
/// indicating where the card will land — similar to Azure DevOps / Trello.
/// The drag is tracked with pointer capture, which works the same on Windows and macOS.
/// </summary>
public static class DragDropBehavior
{
    // ─── Attached property ───────────────────────────────────────────────

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(DragDropBehavior));

    public static bool GetIsEnabled(ItemsControl o)          => o.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(ItemsControl o, bool v)  => o.SetValue(IsEnabledProperty, v);

    static DragDropBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((ic, e) =>
        {
            if (e.NewValue is true)
            {
                ic.AddHandler(InputElement.PointerPressedEvent,  OnPointerPressed,  Avalonia.Interactivity.RoutingStrategies.Tunnel);
                ic.AddHandler(InputElement.PointerMovedEvent,    OnPointerMoved,    Avalonia.Interactivity.RoutingStrategies.Tunnel);
                ic.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                ic.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
            }
            else
            {
                ic.RemoveHandler(InputElement.PointerPressedEvent,  OnPointerPressed);
                ic.RemoveHandler(InputElement.PointerMovedEvent,    OnPointerMoved);
                ic.RemoveHandler(InputElement.PointerReleasedEvent, OnPointerReleased);
                ic.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
            }
        });
    }

    /// <summary>Movement (in DIPs) before a press becomes a drag, as on Windows.</summary>
    private const double MinimumDragDistance = 4;

    // ─── Drag state ──────────────────────────────────────────────────────

    private static Point                _startPoint;
    private static TaskItemViewModel?   _dragged;
    private static bool                 _dragging;

    // Visual feedback
    private static Window?              _ghost;
    private static DropLineAdorner?     _dropAdorner;
    private static AdornerLayer?        _adornerLayer;
    private static int                  _dropIndex = -1;

    // ─── Pointer handlers ────────────────────────────────────────────────

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not ItemsControl ic || !e.GetCurrentPoint(ic).Properties.IsLeftButtonPressed) return;
        _startPoint = e.GetPosition(ic);
        _dragged    = GetVm(e.Source as Visual);
        _dragging   = false;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not ItemsControl ic || _dragged is null) return;
        if (!e.GetCurrentPoint(ic).Properties.IsLeftButtonPressed) { EndDrag(); return; }

        var pos = e.GetPosition(ic);
        if (!_dragging)
        {
            var delta = pos - _startPoint;
            if (Math.Abs(delta.X) < MinimumDragDistance && Math.Abs(delta.Y) < MinimumDragDistance) return;
            _dragging = true;
            e.Pointer.Capture(ic);
            ShowGhost(ic, _dragged);
        }

        // Move ghost to cursor
        MoveGhost(ic, pos);

        // Calculate drop index & show indicator line
        _dropIndex = GetDropIndex(ic, pos);
        ShowDropLine(ic, _dropIndex);
        e.Handled = true;
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not ItemsControl ic) return;
        bool wasDragging = _dragging;
        var dragged      = _dragged;
        int dropIndex    = _dropIndex;
        _dragged = null;
        // A plain click must reach the card's buttons untouched: only a real drag owns the pointer.
        if (!wasDragging || dragged is null) return;

        e.Pointer.Capture(null);
        EndDrag();
        e.Handled = true;   // a drag is not a click on the card
        Drop(ic, dragged, dropIndex);
    }

    private static void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dragging) EndDrag();
    }

    private static void EndDrag()
    {
        HideGhost();
        HideDropLine();
        _dragged  = null;
        _dragging = false;
    }

    private static void Drop(ItemsControl ic, TaskItemViewModel dragged, int dropIndex)
    {
        if (ic.DataContext is not MainViewModel vm) return;

        var list     = vm.Tasks;
        int oldIndex = list.IndexOf(dragged);
        if (oldIndex < 0) return;

        int newIndex;
        if (dropIndex >= ic.ItemCount)
        {
            // Dropping past the last item → go to end.
            // Use list.Count so the shift-adjustment (newIndex--) lands on list.Count-1.
            newIndex = list.Count;
        }
        else
        {
            // Map the visual slot's VM back to its index in Tasks
            var targetVm = ic.Items[dropIndex] as TaskItemViewModel;
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

        AnimateFlip(ic, beforePositions);
    }

    // ─── FLIP animation ──────────────────────────────────────────────────

    /// <summary>Snapshot Y positions of all item containers relative to the ItemsControl.</summary>
    public static Dictionary<TaskItemViewModel, double> CapturePositions(ItemsControl ic)
    {
        var dict = new Dictionary<TaskItemViewModel, double>();
        foreach (var (vm, el) in Containers(ic))
            if (el.TranslatePoint(new Point(0, 0), ic) is { } p) dict[vm] = p.Y;
        return dict;
    }

    /// <summary>
    /// For each item that changed Y position, start it from the old Y and
    /// smoothly animate it to the new Y — giving the "cards sliding" effect.
    /// </summary>
    public static void AnimateFlip(ItemsControl ic, Dictionary<TaskItemViewModel, double> before)
    {
        ic.UpdateLayout();   // let the moved cards reach their new positions first

        // Quintic ease-out: starts fast, decelerates gracefully at the end
        var ease = new QuinticEaseOut();
        var dur  = TimeSpan.FromMilliseconds(520);

        foreach (var (vm, el) in Containers(ic))
        {
            if (el.TranslatePoint(new Point(0, 0), ic) is not { } p) continue;
            if (!before.TryGetValue(vm, out double oldY)) continue;

            double delta = oldY - p.Y;            // how far (and which direction) it moved
            if (Math.Abs(delta) < 2) continue;    // no visible change — skip

            // Offset the element back to where it was, then animate to 0
            var xform = new TranslateTransform(0, delta);
            el.RenderTransform = xform;
            _ = Animator.Animate(xform, TranslateTransform.YProperty, delta, 0, dur, ease)
                        .ContinueWith(_ => Avalonia.Threading.Dispatcher.UIThread.Post(() => el.RenderTransform = null));
        }
    }

    private static IEnumerable<(TaskItemViewModel Vm, Control Container)> Containers(ItemsControl ic)
    {
        for (int i = 0; i < ic.ItemCount; i++)
        {
            if (ic.Items[i] is not TaskItemViewModel vm) continue;
            if (ic.ContainerFromIndex(i) is not Control el) continue;
            yield return (vm, el);
        }
    }

    // ─── Ghost window ────────────────────────────────────────────────────

    private static void ShowGhost(ItemsControl ic, TaskItemViewModel vm)
    {
        var card = new Border
        {
            Background      = new SolidColorBrush(Color.FromArgb(220, 30, 41, 59)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            BorderThickness = new Thickness(2),
            CornerRadius    = new CornerRadius(8),
            Padding         = new Thickness(14, 10, 14, 10),
            Margin          = new Thickness(0, 0, 12, 12),   // room for the shadow
            Effect          = new DropShadowDirectionEffect
                { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.4, Color = Colors.Black },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text         = vm.TaskNumberDisplay + "  " + vm.Title,
                        Foreground   = Brushes.White,
                        FontSize     = 13,
                        FontWeight   = FontWeight.SemiBold,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth     = 320
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
            SystemDecorations     = SystemDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent],
            Background            = Brushes.Transparent,
            IsHitTestVisible      = false,
            Topmost               = true,
            ShowInTaskbar         = false,
            ShowActivated         = false,
            CanResize             = false,
            SizeToContent         = SizeToContent.WidthAndHeight,
            Opacity               = 0.88,
            Content               = card,
            Position              = new PixelPoint(-2000, 0)   // offscreen until first move
        };
        _ghost.Show(TopLevel.GetTopLevel(ic) as Window ?? throw new InvalidOperationException());
    }

    private static void MoveGhost(Visual ic, Point pos)
    {
        if (_ghost is null) return;
        var screen = ic.PointToScreen(pos + new Vector(16, 16));
        _ghost.Position = screen;
    }

    private static void HideGhost()
    {
        _ghost?.Close();
        _ghost = null;
    }

    // ─── Drop-line adorner ───────────────────────────────────────────────

    private static int GetDropIndex(ItemsControl ic, Point pos)
    {
        for (int i = 0; i < ic.ItemCount; i++)
        {
            if (ic.ContainerFromIndex(i) is not Control el) continue;
            if (el.TranslatePoint(new Point(0, 0), ic) is not { } topLeft) continue;
            var mid = topLeft.Y + el.Bounds.Height / 2;
            if (pos.Y < mid) return i;
        }
        return ic.ItemCount;
    }

    private static void ShowDropLine(ItemsControl ic, int index)
    {
        // Get the Y position for the line
        double y = 0;
        if (index < ic.ItemCount && ic.ContainerFromIndex(index) is Control el &&
            el.TranslatePoint(new Point(0, 0), ic) is { } top)
            y = top.Y - 2;
        else if (index > 0 && ic.ContainerFromIndex(index - 1) is Control prev &&
                 prev.TranslatePoint(new Point(0, prev.Bounds.Height), ic) is { } bottom)
            y = bottom.Y + 2;

        // Create or update the adorner
        if (_adornerLayer is null)
        {
            _adornerLayer = AdornerLayer.GetAdornerLayer(ic);
            if (_adornerLayer is null) return;
            _dropAdorner = new DropLineAdorner();
            AdornerLayer.SetAdornedElement(_dropAdorner, ic);
            _adornerLayer.Children.Add(_dropAdorner);
        }

        _dropAdorner?.UpdateY(y, ic.Bounds.Width);
    }

    private static void HideDropLine()
    {
        if (_adornerLayer is not null && _dropAdorner is not null)
        {
            _adornerLayer.Children.Remove(_dropAdorner);
            _dropAdorner  = null;
            _adornerLayer = null;
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static TaskItemViewModel? GetVm(Visual? el)
    {
        // Presses on buttons, toggles and the checkbox stay clicks: the drag captures the pointer.
        if (el?.FindAncestorOfType<Button>(includeSelf: true) is not null) return null;   // ToggleButton and CheckBox derive from Button
        for (var v = el; v is not null; v = v.GetVisualParent())
            if (v is StyledElement { DataContext: TaskItemViewModel vm }) return vm;
        return null;
    }

    // ─── DropLineAdorner ─────────────────────────────────────────────────

    private sealed class DropLineAdorner : Control
    {
        private double _y;
        private double _width;

        private static readonly IBrush LineBrush =
            new SolidColorBrush(Color.FromRgb(59, 130, 246), 0.9);
        private static readonly Pen LinePen = new(LineBrush, 2.5, lineCap: PenLineCap.Round);

        public DropLineAdorner() => IsHitTestVisible = false;

        public void UpdateY(double y, double width)
        {
            _y     = y;
            _width = width;
            InvalidateVisual();
        }

        public override void Render(DrawingContext dc)
        {
            const double circleR = 5;
            const double margin  = 14;

            // Horizontal line
            dc.DrawLine(LinePen,
                new Point(margin + circleR * 2, _y),
                new Point(_width - margin, _y));

            // Circle at the left end
            dc.DrawEllipse(LineBrush, null, new Point(margin + circleR, _y), circleR, circleR);
        }
    }
}
