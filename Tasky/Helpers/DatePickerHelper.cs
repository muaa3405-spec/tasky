using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Tasky.Helpers;

/// <summary>
/// Attached property that fixes calendar popup colours for any DatePicker that
/// uses the themed template.  WPF's CalendarItem resolves DayTitleTemplate from
/// compiled theme BAML before any XAML resource override we define, so the only
/// reliable fix is walking the visual tree after the popup renders.
///
/// Colours applied:
///   - Navigation arrows (Path/Shape inside PART_PreviousButton / PART_NextButton)
///     and month/year header TextBlock → #60A5FA blue
///   - Day-of-week header row TextBlocks (M T W T F S S) → #CBD5E1 soft slate
///   - CalendarDayButton contents → skipped (already themed via ThemedCalendarDayButton)
/// </summary>
public static class DatePickerHelper
{
    // Fresh instances per apply-pass so WPF never treats them as "already set"
    private static SolidColorBrush NewAccent() => new(Color.FromRgb(96, 165, 250));
    private static SolidColorBrush NewHeader() => new(Color.FromRgb(203, 213, 225));

    /// <summary>
    /// Subscribes to a DatePicker's CalendarOpened event and applies colour
    /// fixes each time the popup opens.  Called from the global class handler
    /// registered in App.OnStartup so every DatePicker in the app is covered.
    /// </summary>
    public static void Subscribe(DatePicker dp)
    {
        dp.CalendarOpened -= OnCalendarOpened;   // avoid double-subscribe
        dp.CalendarOpened += OnCalendarOpened;
    }

    private static void OnCalendarOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not DatePicker dp) return;
        // ApplicationIdle runs after ALL layout, rendering AND VisualStateManager
        // transitions that would otherwise reset our colour overrides.
        dp.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(() => ApplyCalendarColors(dp)));
    }

    public static void ApplyCalendarColors(DatePicker dp)
    {
        var popup = FindVisualChild<Popup>(dp);
        if (popup?.Child is not DependencyObject root) return;
        Walk(root, Ctx.None);
    }

    private enum Ctx { None, NavButton, CalendarBtn, DayButton }

    private static void Walk(DependencyObject node, Ctx ctx)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);

            var childCtx = child is CalendarDayButton ? Ctx.DayButton
                         : child is CalendarButton    ? Ctx.CalendarBtn
                         : child is Button            ? Ctx.NavButton
                         : ctx;

            // Skip CalendarDayButton entirely — already styled by ThemedCalendarDayButton
            if (childCtx == Ctx.DayButton) continue;

            bool isNav = childCtx is Ctx.NavButton or Ctx.CalendarBtn;

            // Set Foreground on the nav Button so {TemplateBinding Foreground}
            // in the button's own ControlTemplate propagates to the arrow icon.
            if (childCtx == Ctx.NavButton && child is Button navBtn)
                navBtn.Foreground = NewAccent();

            // TextBlocks: month/year header text + day-of-week row (M T W T F S S)
            if (child is System.Windows.Controls.TextBlock tb)
                tb.Foreground = isNav ? NewAccent() : NewHeader();

            // Path ONLY (arrow geometry) — NOT Rectangle or Ellipse.
            // Override regardless of existing brush type to cover TemplateBinding-
            // derived, SystemColor, or hardcoded brushes.
            if (isNav && child is System.Windows.Shapes.Path arrowPath)
            {
                if (arrowPath.Stroke != null)
                    arrowPath.Stroke = NewAccent();
                // Only recolor filled paths; skip purely transparent hit-test shapes
                if (arrowPath.Fill is { } fill &&
                    !(fill is SolidColorBrush scb && scb.Color.A == 0))
                    arrowPath.Fill = NewAccent();
            }

            Walk(child, childCtx);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }
}
