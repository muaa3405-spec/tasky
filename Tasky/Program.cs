using Avalonia;
using Avalonia.Media;
using Tasky.Platform.Windows;

namespace Tasky;

internal static class Program
{
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
                     .AfterSetup(_ => RegisterFonts())
                     .LogToTrace();

    /// <summary>
    /// Windows keeps the original Segoe UI look, with emoji icons drawn as single-colour
    /// outlines so they take the colours the design gives them. macOS uses its own system font.
    /// </summary>
    private static FontManagerOptions FontOptions()
    {
        if (!OperatingSystem.IsWindows()) return new FontManagerOptions();

        // Same order as WPF's "Segoe UI" composite font: arrows, shapes and technical symbols
        // come from Segoe UI Symbol; everything else falls back to the emoji outlines.
        var symbol = new FontFamily("Segoe UI Symbol");
        var fallbacks = SymbolFirstRanges
            .Select(range => new FontFallback { FontFamily = symbol, UnicodeRange = UnicodeRange.Parse(range) })
            .ToList();
        if (MonochromeEmojiFont.IsAvailable) fallbacks.Add(new FontFallback { FontFamily = MonochromeEmojiFont.Family });
        fallbacks.Add(new FontFallback { FontFamily = symbol });

        return new FontManagerOptions { DefaultFamilyName = "Segoe UI", FontFallbacks = fallbacks };
    }

    /// <summary>Unicode ranges WPF draws from Segoe UI Symbol before trying Segoe UI Emoji.</summary>
    private static readonly string[] SymbolFirstRanges =
    [
        "U+2000-202E", "U+2030-20CF", "U+20D0-20FF", "U+2150-22FF", "U+2400-245F",
        "U+2460-25FF", "U+27C0-2BFF", "U+1F780-1F7FF", "U+1F800-1F8FF"
    ];

    private static void RegisterFonts()
    {
        if (OperatingSystem.IsWindows() && MonochromeEmojiFont.IsAvailable)
            FontManager.Current.AddFontCollection(new MonochromeEmojiFont());
    }
}
