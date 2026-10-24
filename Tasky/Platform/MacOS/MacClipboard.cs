using System.Runtime.Versioning;

namespace Tasky.Platform.MacOS;

/// <summary>Clipboard images, copied Finder files and text through NSPasteboard.</summary>
[SupportedOSPlatform("macos")]
internal sealed class MacClipboard : IClipboardService
{
    private const string PngType   = "public.png";
    private const string TiffType  = "public.tiff";
    private const string TextType  = "public.utf8-plain-text";
    private const string FilesType = "NSFilenamesPboardType";
    private const string FileUrlType = "public.file-url";

    private static IntPtr Pasteboard => ObjC.Send(ObjC.Class("NSPasteboard"), "generalPasteboard");

    // Finder also puts the copied file's icon (TIFF) on the pasteboard: copied files are files, not images.
    public bool ContainsImage() => Safe(() =>
        !HasType(FileUrlType) && !HasType(FilesType) && (HasType(PngType) || HasType(TiffType)));

    public byte[]? GetImagePng() => Safe(() =>
    {
        var png = ObjC.Send(Pasteboard, "dataForType:", ObjC.NSString(PngType));
        if (png != IntPtr.Zero) return ObjC.ToBytes(png);
        return ObjC.ImageDataToPng(ObjC.Send(Pasteboard, "dataForType:", ObjC.NSString(TiffType)));
    });

    public IReadOnlyList<string> GetFiles()
        => Safe(() => (IReadOnlyList<string>)ObjC.ToStringList(
               ObjC.Send(Pasteboard, "propertyListForType:", ObjC.NSString(FilesType)))) ?? [];

    public bool SetImagePng(byte[] png) => Safe(() =>
    {
        ObjC.Send(Pasteboard, "clearContents");
        return ObjC.SendBool(Pasteboard, "setData:forType:", ObjC.NSData(png), ObjC.NSString(PngType));
    });

    public bool SetText(string text) => Safe(() =>
    {
        ObjC.Send(Pasteboard, "clearContents");
        return ObjC.SendBool(Pasteboard, "setString:forType:", ObjC.NSString(text), ObjC.NSString(TextType));
    });

    private static bool HasType(string type)
        => ObjC.ToStringList(ObjC.Send(Pasteboard, "types")).Contains(type);

    private static T? Safe<T>(Func<T> action)
    {
        try { return ObjC.WithPool(action); }
        catch { return default; }
    }
}
