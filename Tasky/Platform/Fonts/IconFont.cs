using System.Globalization;
using System.IO;
using System.Reflection;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace Tasky.Platform.Fonts;

/// <summary>
/// The single-colour emoji font Tasky draws its icons with, so every icon takes the colour the
/// design gives it (colour emoji would ignore it):
///   Ã¢â‚¬Â¢ Windows Ã¢â‚¬â€ the installed Segoe UI Emoji with its colour tables switched off, exactly how
///     the original WPF app rendered icons;
///   Ã¢â‚¬Â¢ macOS   Ã¢â‚¬â€ the bundled Noto Emoji (SIL Open Font License).
/// The font is prepared in memory and registered as a fallback font for characters the
/// text font lacks.
/// </summary>
internal sealed class IconFont : IFontCollection
{
    private static readonly Uri CollectionKey = new("fonts:TaskyIcons");
    private const string FamilyName = "Tasky Icons";

    private static readonly string WindowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    private static readonly Uri BundledNotoEmoji = new("avares://Tasky/Assets/Fonts/NotoEmoji-Variable.ttf");

    /// <summary>A normal word space; Noto Emoji's own is as wide as an emoji and pushes text aside.</summary>
    private const double SpaceWidthEms = 0.274;

    private readonly Func<byte[]> _loadFont;
    private readonly FontSimulations _simulations;
    private IGlyphTypeface? _typeface;

    private IconFont(Func<byte[]> loadFont, FontSimulations simulations = FontSimulations.None)
    {
        _loadFont    = loadFont;
        _simulations = simulations;
    }

    public static FontFamily Family { get; } = new($"{CollectionKey}#{FamilyName}");

    /// <summary>The icon font for this computer, or null when none can be found.</summary>
    public static IconFont? ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            string emoji = Path.Combine(WindowsFonts, "seguiemj.ttf"), text = Path.Combine(WindowsFonts, "segoeui.ttf");
            if (!File.Exists(emoji) || !File.Exists(text)) return null;
            return new IconFont(() =>
            {
                var font = File.ReadAllBytes(emoji);
                OpenTypeFont.DisableColorTables(font);
                // WPF ignored fallback-font metrics: a line with an icon is as tall as one without.
                OpenTypeFont.CopyLineMetrics(from: File.ReadAllBytes(text), to: font);
                return font;
            });
        }
        return new IconFont(() =>
        {
            using var stream = AssetLoader.Open(BundledNotoEmoji);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            var font = copy.ToArray();
            OpenTypeFont.SetAdvance(font, ' ', SpaceWidthEms);
            return font;
        }, FontSimulations.Bold);   // Noto's strokes are thinner than the Windows icons
    }

    public Uri Key => CollectionKey;

    public void Initialize(IFontManagerImpl fontManager)
    {
        try
        {
            _typeface = CreateTypeface(fontManager, new MemoryStream(_loadFont()), _simulations);
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Could not load the icon font", ex);
        }
    }

    public bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight,
                                    FontStretch stretch, out IGlyphTypeface glyphTypeface)
    {
        glyphTypeface = _typeface!;
        return _typeface is not null;
    }

    public bool TryMatchCharacter(int codepoint, FontStyle style, FontWeight weight, FontStretch stretch,
                                  string? familyName, CultureInfo? culture, out Typeface typeface)
    {
        typeface = new Typeface(Family, style, weight, stretch);
        return _typeface is not null && _typeface.TryGetGlyph((uint)codepoint, out _);
    }

    /// <summary>
    /// Loads a font from memory. Avalonia 11 keeps this platform call internal (its own embedded
    /// fonts use it), so it is reached by reflection; if that ever fails, icons fall back to
    /// the system emoji font and the app keeps working.
    /// </summary>
    private static IGlyphTypeface? CreateTypeface(IFontManagerImpl fontManager, Stream stream, FontSimulations simulations)
    {
        var method = typeof(IFontManagerImpl).GetMethod("TryCreateGlyphTypeface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [typeof(Stream), typeof(FontSimulations), typeof(IGlyphTypeface).MakeByRefType()], null);
        if (method is null) return null;
        var args = new object?[] { stream, simulations, null };
        return method.Invoke(fontManager, args) is true ? args[2] as IGlyphTypeface : null;
    }

    public int Count => _typeface is null ? 0 : 1;
    public FontFamily this[int index] => Family;
    public IEnumerator<FontFamily> GetEnumerator() { if (_typeface is not null) yield return Family; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose() { }
}
