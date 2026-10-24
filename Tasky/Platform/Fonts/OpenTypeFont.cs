using System.Buffers.Binary;
using System.Text;

namespace Tasky.Platform.Fonts;

/// <summary>
/// Small in-memory edits to an OpenType font file (header, 'hhea', 'hmtx', 'cmap'),
/// used to make emoji fonts behave like plain single-colour icon fonts.
/// </summary>
internal static class OpenTypeFont
{
    /// <summary>Tables that hold colour layers or bitmaps.</summary>
    private static readonly string[] ColorTables = ["COLR", "CPAL", "SVG ", "CBDT", "CBLC", "sbix"];

    /// <summary>Renames the colour tables in the table directory, so renderers draw the plain outlines.</summary>
    public static void DisableColorTables(byte[] font)
    {
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (ColorTables.Contains(Encoding.ASCII.GetString(font, record, 4))) font[record] = (byte)'x';
        }
    }

    /// <summary>
    /// Gives <paramref name="to"/> the ascent, descent and line gap ('hhea') of <paramref name="from"/>,
    /// so a line with an icon is exactly as tall as a line of text.
    /// </summary>
    public static void CopyLineMetrics(byte[] from, byte[] to)
    {
        const int AscentToLineGap = 6;   // ascender, descender, lineGap: three int16 from offset 4
        int source = TableOffset(from, "hhea"), target = TableOffset(to, "hhea");
        if (source < 0 || target < 0) return;
        Array.Copy(from, source + 4, to, target + 4, AscentToLineGap);
    }

    /// <summary>Sets the advance width of <paramref name="codepoint"/> to a share of the em size.</summary>
    public static void SetAdvance(byte[] font, int codepoint, double ems)
    {
        int head = TableOffset(font, "head"), hhea = TableOffset(font, "hhea"), hmtx = TableOffset(font, "hmtx");
        int glyph = GlyphIndex(font, codepoint);
        if (head < 0 || hhea < 0 || hmtx < 0 || glyph <= 0) return;

        int unitsPerEm = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(head + 18));
        int metrics    = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(hhea + 34));   // numberOfHMetrics
        if (glyph >= metrics) return;   // shares the last advance with other glyphs: leave it alone
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(hmtx + glyph * 4), (ushort)Math.Round(unitsPerEm * ems));
    }

    public static int TableOffset(byte[] font, string tag)
    {
        int tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (int i = 0; i < tableCount; i++)
        {
            int record = 12 + i * 16;
            if (Encoding.ASCII.GetString(font, record, 4) == tag)
                return (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
        }
        return -1;
    }

    /// <summary>Glyph index of a character from the font's Unicode 'cmap' (format 12 or 4), or 0.</summary>
    private static int GlyphIndex(byte[] font, int codepoint)
    {
        int cmap = TableOffset(font, "cmap");
        if (cmap < 0) return 0;
        int subtables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(cmap + 2));
        for (int i = 0; i < subtables; i++)
        {
            int sub = cmap + (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(cmap + 4 + i * 8 + 4));
            int glyph = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(sub)) switch
            {
                12 => LookUpFormat12(font, sub, codepoint),
                4  => LookUpFormat4(font, sub, codepoint),
                _  => 0
            };
            if (glyph > 0) return glyph;
        }
        return 0;
    }

    private static int LookUpFormat12(byte[] font, int sub, int codepoint)
    {
        long groups = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(sub + 12));
        for (int g = 0; g < groups; g++)
        {
            int group = sub + 16 + g * 12;
            long start = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(group));
            long end   = BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(group + 4));
            if (codepoint >= start && codepoint <= end)
                return (int)(BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(group + 8)) + codepoint - start);
        }
        return 0;
    }

    private static int LookUpFormat4(byte[] font, int sub, int codepoint)
    {
        if (codepoint > 0xFFFF) return 0;
        int segments = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(sub + 6)) / 2;
        int ends = sub + 14, starts = ends + segments * 2 + 2, deltas = starts + segments * 2, offsets = deltas + segments * 2;
        for (int s = 0; s < segments; s++)
        {
            int end   = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(ends + s * 2));
            int start = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(starts + s * 2));
            if (codepoint > end) continue;
            if (codepoint < start) return 0;
            short delta  = BinaryPrimitives.ReadInt16BigEndian(font.AsSpan(deltas + s * 2));
            int   offset = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offsets + s * 2));
            if (offset == 0) return (codepoint + delta) & 0xFFFF;
            int glyph = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offsets + s * 2 + offset + (codepoint - start) * 2));
            return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
        }
        return 0;
    }
}
