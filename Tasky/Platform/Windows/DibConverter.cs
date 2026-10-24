using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tasky.Platform.Windows;

/// <summary>
/// Converts between PNG and the device-independent bitmaps (CF_DIB) on the Windows
/// clipboard. Handles the 24- and 32-bit uncompressed layouts screenshots use.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DibConverter
{
    private const int HeaderSize   = 40;   // BITMAPINFOHEADER
    private const int BI_RGB       = 0;
    private const int BI_BITFIELDS = 3;

    /// <summary>PNG bytes for a clipboard DIB, or null for layouts we do not read.</summary>
    public static byte[]? ToPng(byte[] dib)
    {
        if (dib.Length < HeaderSize) return null;
        int headerSize  = BitConverter.ToInt32(dib, 0);
        int width       = BitConverter.ToInt32(dib, 4);
        int rawHeight   = BitConverter.ToInt32(dib, 8);
        int bitCount    = BitConverter.ToInt16(dib, 14);
        int compression = BitConverter.ToInt32(dib, 16);
        if (width <= 0 || rawHeight == 0 || bitCount is not (24 or 32)) return null;
        if (compression is not (BI_RGB or BI_BITFIELDS)) return null;

        bool bottomUp  = rawHeight > 0;
        int  height    = Math.Abs(rawHeight);
        int  pixels    = headerSize + (compression == BI_BITFIELDS && headerSize == HeaderSize ? 12 : 0);
        int  srcStride = ((width * bitCount + 31) / 32) * 4;
        if (dib.Length < pixels + srcStride * height) return null;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (int y = 0; y < height; y++)
            {
                int src = pixels + (bottomUp ? height - 1 - y : y) * srcStride;
                for (int x = 0; x < width; x++)
                {
                    int s = src + x * (bitCount / 8), d = x * 4;
                    row[d]     = dib[s];
                    row[d + 1] = dib[s + 1];
                    row[d + 2] = dib[s + 2];
                    row[d + 3] = 255;   // clipboard DIBs rarely carry real alpha; treat as opaque
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally { bitmap.UnlockBits(data); }

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>A 32-bit bottom-up DIB for a PNG, so older apps can paste the image too.</summary>
    public static byte[]? FromPng(byte[] png)
    {
        try
        {
            using var source = new Bitmap(new MemoryStream(png));
            int width = source.Width, height = source.Height, stride = width * 4;
            var dib = new byte[HeaderSize + stride * height];
            BitConverter.GetBytes(HeaderSize).CopyTo(dib, 0);
            BitConverter.GetBytes(width).CopyTo(dib, 4);
            BitConverter.GetBytes(height).CopyTo(dib, 8);              // positive = bottom-up
            BitConverter.GetBytes((short)1).CopyTo(dib, 12);           // planes
            BitConverter.GetBytes((short)32).CopyTo(dib, 14);          // bits per pixel
            BitConverter.GetBytes(stride * height).CopyTo(dib, 20);    // image size

            var data = source.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(data.Scan0 + (height - 1 - y) * data.Stride, dib, HeaderSize + y * stride, stride);
            }
            finally { source.UnlockBits(data); }
            return dib;
        }
        catch
        {
            return null;
        }
    }
}
