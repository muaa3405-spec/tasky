namespace Tasky.Helpers;

/// <summary>The "Theme" choices in Settings, as saved in the settings file.</summary>
public static class ThemePreferences
{
    public const string Dark  = "dark";
    public const string Light = "light";

    /// <summary>Follows the operating system's light/dark setting, including live changes.</summary>
    public const string Auto  = "auto";
}
