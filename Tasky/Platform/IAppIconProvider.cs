using Avalonia.Media.Imaging;

namespace Tasky.Platform;

/// <summary>Looks up application icons for the App Usage panel.</summary>
public interface IAppIconProvider
{
    /// <summary>The icon for a recorded app location, or null to show a letter avatar.</summary>
    Bitmap? GetIcon(string location);
}
