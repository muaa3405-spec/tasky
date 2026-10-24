using System.IO;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;

namespace Tasky.Platform.MacOS;

/// <summary>App icons from Finder (NSWorkspace) for the App Usage panel.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacAppIconProvider : IAppIconProvider
{
    private const double IconSize = 48;   // shown at 24 pt; twice that for Retina screens

    private readonly Dictionary<string, Bitmap?> _cache = new();

    public Bitmap? GetIcon(string location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        if (_cache.TryGetValue(location, out var cached)) return cached;

        Bitmap? bitmap = null;
        if (Directory.Exists(location) || File.Exists(location))
        {
            try
            {
                var png = ObjC.WithPool(() => IconPng(location));
                if (png is not null) bitmap = new Bitmap(new MemoryStream(png));
            }
            catch { /* fall back to the letter avatar */ }
        }

        _cache[location] = bitmap;
        return bitmap;
    }

    private static byte[]? IconPng(string location)
    {
        var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), "sharedWorkspace");
        var image     = ObjC.Send(workspace, "iconForFile:", ObjC.NSString(location));
        if (image == IntPtr.Zero) return null;

        var rect    = new ObjC.CGRect { Width = IconSize, Height = IconSize };
        var cgImage = ObjC.Send(image, "CGImageForProposedRect:context:hints:", ref rect, IntPtr.Zero, IntPtr.Zero);
        if (cgImage == IntPtr.Zero) return null;

        var rep = ObjC.Send(ObjC.Send(ObjC.Class("NSBitmapImageRep"), "alloc"), "initWithCGImage:", cgImage);
        try { return ObjC.BitmapRepToPng(rep); }
        finally { ObjC.Send(rep, "release"); }
    }
}
