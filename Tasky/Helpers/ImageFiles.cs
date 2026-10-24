using System.IO;
using Avalonia.Media.Imaging;

namespace Tasky.Helpers;

/// <summary>Shared image helpers for attachments and thumbnails.</summary>
public static class ImageFiles
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Loads an image fully into memory and releases the file straight away,
    /// so the attachment can still be deleted or replaced while it is shown.
    /// Pass <paramref name="decodeWidth"/> for thumbnails to avoid decoding at full size.
    /// Returns null when the file is missing or cannot be decoded.
    /// </summary>
    public static Bitmap? Load(string path, int decodeWidth = 0)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            return decodeWidth > 0 ? Bitmap.DecodeToWidth(stream, decodeWidth) : new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Decodes in-memory image bytes (e.g. a pasted screenshot), or null.</summary>
    public static Bitmap? FromBytes(byte[] data)
    {
        try { return new Bitmap(new MemoryStream(data)); }
        catch { return null; }
    }
}
