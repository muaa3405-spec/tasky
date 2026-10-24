using Avalonia;
using Avalonia.Media;
using Tasky.Platform.Fonts;

namespace Tasky;

internal static class Program
{
    /// <summary>The single-colour emoji font for icons (null when none is available).</summary>
    private static readonly IconFont? Icons = IconFont.ForCurrentPlatform();

    [STAThread]
    public static void Main(string[] args)
    {
        Platform.PlatformServices.ConfigureProcess();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    /// <summary>Also used by the Avalonia XAML previewer.</summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
                     .UsePlatformDetect()
                     .With(FontOptions())
                     .With(new MacOSPlatformOptions { ShowInDock = true })
                     .AfterSetup(_ => { if (Icons is not null) FontManager.Current.AddFontCollection(Icons); })
                     .LogToTrace();

    /// <summary>
    /// Emoji icons are drawn as single-colour outlines so they take the colours the design gives
    /// them. Windows keeps the original Segoe UI text font; macOS uses its own system font.
    /// </summary>
    private static FontManagerOptions FontOptions()
    {
        var fallbacks = new List<FontFallback>();
        if (OperatingSystem.IsWindows())
        {
            // Same order as WPF's "Segoe UI" composite font: arrows, shapes and technical symbols
            // come from Segoe UI Symbol before the emoji outlines.
            var symbol = new FontFamily("Segoe UI Symbol");
            fallbacks.AddRange(SymbolFirstRanges.Select(range =>
                new FontFallback { FontFamily = symbol, UnicodeRange = UnicodeRange.Parse(range) }));
            if (Icons is not null) fallbacks.Add(new FontFallback { FontFamily = IconFont.Family });
            fallbacks.Add(new FontFallback { FontFamily = symbol });
            return new FontManagerOptions { DefaultFamilyName = "Segoe UI", FontFallbacks = fallbacks };
        }

        if (Icons is not null) fallbacks.Add(new FontFallback { FontFamily = IconFont.Family });
        return new FontManagerOptions { FontFallbacks = fallbacks };
    }

    /// <summary>Unicode ranges WPF draws from Segoe UI Symbol before trying Segoe UI Emoji.</summary>
    private static readonly string[] SymbolFirstRanges =
    [
        "U+2000-202E", "U+2030-20CF", "U+20D0-20FF", "U+2150-22FF", "U+2400-245F",
        "U+2460-25FF", "U+27C0-2BFF", "U+1F780-1F7FF", "U+1F800-1F8FF"
    ];
}
