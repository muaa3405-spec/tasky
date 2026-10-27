using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Tasky.Behaviors;

/// <summary>
/// Gives screen readers (Narrator, VoiceOver) a name for every control that has a text tooltip
/// but no other label. Most of Tasky's buttons show only an icon, so without this a screen
/// reader would announce them as just "button".
/// </summary>
public static class AccessibleNames
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        ToolTip.TipProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is string tip && !string.IsNullOrWhiteSpace(tip)
                && string.IsNullOrEmpty(AutomationProperties.GetName(control)))
                AutomationProperties.SetName(control, FirstLine(tip));
        });
    }

    private static string FirstLine(string text)
    {
        int end = text.IndexOf('\n');
        return (end < 0 ? text : text[..end]).Trim();
    }
}
