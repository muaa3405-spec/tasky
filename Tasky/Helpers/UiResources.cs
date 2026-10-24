using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Tasky.Helpers;

/// <summary>Theme-aware resource lookups for controls built in code.</summary>
public static class UiResources
{
    /// <summary>Binds <paramref name="property"/> to a theme brush, so it follows Dark / Light switches.</summary>
    public static T WithResource<T>(this T element, AvaloniaProperty property, string key) where T : StyledElement
    {
        element.Bind(property, element.GetResourceObservable(key));
        return element;
    }

    /// <summary>A shared control theme (e.g. "FilterChip") from the app resources.</summary>
    public static ControlTheme Theme(string key)
        => (ControlTheme)Application.Current!.FindResource(key)!;
}
