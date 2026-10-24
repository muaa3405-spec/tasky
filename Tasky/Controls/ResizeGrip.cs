using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Tasky.Controls;

/// <summary>
/// The dotted corner grip of a resizable Windows dialog: a triangle of 2×2 dots on a
/// 3px grid, drawn with the same four shades. Dragging it resizes the window.
/// </summary>
public sealed class ResizeGrip : Control
{
    private const int Step = 3, FirstColumn = 3, FirstRow = 2, Columns = 4;

    private static readonly IBrush TopLeft     = new ImmutableSolidColorBrush(Color.Parse("#E5E7E8"));
    private static readonly IBrush TopRight    = new ImmutableSolidColorBrush(Color.Parse("#B7BFCE"));
    private static readonly IBrush BottomLeft  = new ImmutableSolidColorBrush(Color.Parse("#E6EAF0"));
    private static readonly IBrush BottomRight = new ImmutableSolidColorBrush(Color.Parse("#ADB9CF"));
    private static readonly IBrush Shadow      = new ImmutableSolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));

    public ResizeGrip()
    {
        Width  = FirstColumn + Columns * Step - 1 + 2;   // 14
        Height = FirstRow + Columns * Step - 1 + 1;      // 13
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        VerticalAlignment   = Avalonia.Layout.VerticalAlignment.Bottom;
        Cursor = new Cursor(StandardCursorType.BottomRightCorner);
        Background = Brushes.Transparent;
    }

    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        Border.BackgroundProperty.AddOwner<ResizeGrip>();

    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        if (Background is { } background) context.FillRectangle(background, new Rect(Bounds.Size));
        for (int row = 0; row < Columns; row++)
        {
            int y = FirstRow + row * Step;
            for (int column = Columns - 1 - row; column < Columns; column++)
            {
                int x = FirstColumn + column * Step;
                context.FillRectangle(TopLeft,     new Rect(x,     y,     1, 1));
                context.FillRectangle(TopRight,    new Rect(x + 1, y,     1, 1));
                context.FillRectangle(BottomLeft,  new Rect(x,     y + 1, 1, 1));
                context.FillRectangle(BottomRight, new Rect(x + 1, y + 1, 1, 1));
                context.FillRectangle(Shadow,      new Rect(x,     y + 2, 2, 1));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (TopLevel.GetTopLevel(this) is Window { CanResize: true } window &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginResizeDrag(WindowEdge.SouthEast, e);
            e.Handled = true;
        }
    }
}
