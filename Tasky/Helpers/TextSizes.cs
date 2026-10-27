namespace Tasky.Helpers;

/// <summary>The "Text size" choices in Settings: the whole main window is zoomed by the factor.</summary>
public static class TextSizes
{
    public const double Normal = 1.0;
    public const double Large  = 1.15;
    public const double Larger = 1.3;

    /// <summary>Keeps a hand-edited settings file from making the window unusable.</summary>
    public static double Clamp(double scale) => Math.Clamp(scale, Normal, Larger);
}
