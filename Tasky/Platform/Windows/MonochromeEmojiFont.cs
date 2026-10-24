using System.Globalization;
using System.Reflection;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace Tasky.Platform.Windows;

/// <summary>
/// Windows' own Segoe UI Emoji with its colour tables switched off, so emoji icons are
/// drawn as single-colour outlines in the colour the design gives them — exactly how the
/// original WPF app rendered them. The font is read from the installed Windows copy.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class MonochromeEmojiFont : IFontCollection
{
    private static readonly Uri CollectionKey = new("fonts:TaskyMonochromeEmoji");
    private const string FamilyName = "Segoe UI Emoji";

    /// <summary>Tables that hold colour layers or bitmaps; renamed so the renderer ignores them.</summary>
    private static readonly string[] ColorTables = ["COLR", "CPAL", "SVG ", "CBDT", "CBLC", "sbix"];

    private static string FontFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "seguiemj.ttf");

    /// <summary>The text font whose line height icons must not change (WPF ignores fallback metrics).</summary>
    private static string TextFontFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");

    private IGlyphTypeface? _typeface;

    public static bool IsAvailable => File.Exists(FontFile);

    public static FontFamily Family { get; } = new($"{CollectionKey}#{FamilyName}");

    public Uri Key => CollectionKey;

    public void Initialize(IFontManagerImpl fontManager)
    {
        try
        {
            var bytes = File.ReadAllBytes(FontFile);
            DisableColorTables(bytes);
            CopyLineMetrics(from: File.ReadAllBytes(TextFontFile), to: bytes);
            _typeface = CreateTypeface(fontManager, new MemoryStream(bytes));
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Could not load the emoji icon font", ex);
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
    private static IGlyphTypeface? CreateTypeface(IFontManagerImpl fontManager, Stream stream)
    {
        var method = typeof(IFontManagerImpl).GetMethod("TryCreateGlyphTypeface",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            [typeof(Stream), typeof(FontSimulations), typeof(IGlyphTypeface).MakeByRefType()], null);
        if (method is null) return null;
        var args = new object?[] { stream, FontSimulations.None, null };
        return method.Invoke(fontManager, args) is true ? args[2] as IGlyphTypeface : null;
    }

    /// <summary>Renames the colour tables in the font's table directory (OpenType header).</summary>
    private static void DisableColorTables(byte[] font)
    {
        int tableCount = (font[4] << 8) | font[5];
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            var tag = Encoding.ASCII.GetString(font, record, 4);
            if (ColorTables.Contains(tag)) font[record] = (byte)'x';
        }
    }

    /// <summary>
    /// Gives the emoji font the text font's ascent, descent and line gap ('hhea' table), so a line
    /// with an icon is exactly as tall as a line without one — as it was in WPF.
    /// </summary>
    private static void CopyLineMetrics(byte[] from, byte[] to)
    {
        const int AscentToLineGap = 6;   // ascender, descender, lineGap: three int16 from offset 4
        int source = TableOffset(from, "hhea"), target = TableOffset(to, "hhea");
        if (source < 0 || target < 0) return;
        Array.Copy(from, source + 4, to, target + 4, AscentToLineGap);
    }

    private static int TableOffset(byte[] font, string tag)
    {
        int tableCount = (font[4] << 8) | font[5];
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (Encoding.ASCII.GetString(font, record, 4) == tag)
                return (font[record + 8] << 24) | (font[record + 9] << 16) | (font[record + 10] << 8) | font[record + 11];
        }
        return -1;
    }

    public int Count => _typeface is null ? 0 : 1;
    public FontFamily this[int index] => Family;
    public IEnumerator<FontFamily> GetEnumerator() { if (_typeface is not null) yield return Family; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public void Dispose() { }
}
