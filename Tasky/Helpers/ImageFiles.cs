using System.IO;
using System.Windows.Media.Imaging;

namespace Tasky.Helpers;

/// <summary>Shared image helpers for attachments and thumbnails.</summary>
public static class ImageFiles
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>Encodes an in-memory image (e.g. from the clipboard) as PNG bytes.</summary>
    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// Loads an image fully into memory and releases the file straight away,
    /// so the attachment can still be deleted or replaced while it is shown.
    /// Pass <paramref name="decodeWidth"/> for thumbnails to avoid decoding at full size.
    /// Returns null when the file is missing or cannot be decoded.
    /// </summary>
    public static BitmapImage? Load(string path, int decodeWidth = 0)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            if (decodeWidth > 0) bmp.DecodePixelWidth = decodeWidth;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
