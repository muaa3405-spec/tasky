using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace Tasky.Behaviors;

/// <summary>
/// Tells whether a TextBlock shows all of its text. Set <c>TextOverflow.Detect="True"</c> on a TextBlock
/// whose height or width is limited, then bind to <c>TextOverflow.IsTrimmed</c> to show a "more" hint
/// only when part of the text is hidden.
/// </summary>
public static class TextOverflow
{
    private const double Tolerance = 0.5;

    public static readonly AttachedProperty<bool> DetectProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("Detect", typeof(TextOverflow));

    public static readonly AttachedProperty<bool> IsTrimmedProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsTrimmed", typeof(TextOverflow));

    public static bool GetDetect(TextBlock element) => element.GetValue(DetectProperty);
    public static void SetDetect(TextBlock element, bool value) => element.SetValue(DetectProperty, value);
    public static bool GetIsTrimmed(TextBlock element) => element.GetValue(IsTrimmedProperty);
    public static void SetIsTrimmed(TextBlock element, bool value) => element.SetValue(IsTrimmedProperty, value);

    static TextOverflow()
    {
        DetectProperty.Changed.AddClassHandler<TextBlock>((textBlock, e) =>
        {
            if (e.NewValue is not true) return;
            // Bounds change after every layout pass that resizes the block (window resize, text edit, expand).
            textBlock.PropertyChanged += (_, change) =>
            {
                if (change.Property == Visual.BoundsProperty || change.Property == TextBlock.TextProperty)
                    Dispatcher.UIThread.Post(() => Update(textBlock), DispatcherPriority.Background);
            };
        });
    }

    private static void Update(TextBlock textBlock)
    {
        var bounds = textBlock.Bounds;
        if (string.IsNullOrEmpty(textBlock.Text) || bounds.Width <= 0)
        {
            SetIsTrimmed(textBlock, false);
            return;
        }

        double width = Math.Max(0, bounds.Width - textBlock.Padding.Left - textBlock.Padding.Right);
        double height = Math.Max(0, bounds.Height - textBlock.Padding.Top - textBlock.Padding.Bottom);

        // The whole text laid out at the same width, with no height limit and no ellipsis.
        using var full = new TextLayout(
            textBlock.Text,
            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight),
            textBlock.FontSize,
            foreground: null,
            textWrapping: textBlock.TextWrapping,
            maxWidth: textBlock.TextWrapping == TextWrapping.NoWrap ? double.PositiveInfinity : width);

        SetIsTrimmed(textBlock, full.Height > height + Tolerance
                             || full.WidthIncludingTrailingWhitespace > width + Tolerance);
    }
}
