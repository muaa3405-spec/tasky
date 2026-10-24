using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>
/// Clipboard images, copied files and text through the Win32 clipboard.
/// Another program (a clipboard manager, Remote Desktop, Office) can briefly hold
/// the clipboard open, so opening it is retried a few times before giving up.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsClipboard : IClipboardService
{
    private const int Attempts     = 5;
    private const int RetryDelayMs = 60;

    // Browsers, Office and the Snipping Tool also put the image here as a real PNG.
    private static readonly uint PngFormat = RegisterClipboardFormat("PNG");

    public bool ContainsImage()
        => IsClipboardFormatAvailable(PngFormat) || IsClipboardFormatAvailable(CF_DIB);

    public byte[]? GetImagePng() => WithClipboard(() =>
    {
        if (IsClipboardFormatAvailable(PngFormat) && ReadGlobal(PngFormat) is { } png) return png;
        return ReadGlobal(CF_DIB) is { } dib ? DibConverter.ToPng(dib) : null;
    });

    public IReadOnlyList<string> GetFiles() => WithClipboard(() =>
    {
        var drop = GetClipboardData(CF_HDROP);
        if (drop == IntPtr.Zero) return (IReadOnlyList<string>)[];
        uint count = DragQueryFile(drop, 0xFFFFFFFF, null, 0);
        var files = new List<string>((int)count);
        for (uint i = 0; i < count; i++)
        {
            var sb = new System.Text.StringBuilder((int)DragQueryFile(drop, i, null, 0) + 1);
            DragQueryFile(drop, i, sb, (uint)sb.Capacity);
            files.Add(sb.ToString());
        }
        return files;
    }) ?? [];

    public bool SetImagePng(byte[] png) => WithClipboard(() =>
    {
        EmptyClipboard();
        bool ok = WriteGlobal(PngFormat, png);
        if (DibConverter.FromPng(png) is { } dib) ok |= WriteGlobal(CF_DIB, dib);
        return ok;
    });

    public bool SetText(string text) => WithClipboard(() =>
    {
        EmptyClipboard();
        return WriteGlobal(CF_UNICODETEXT, System.Text.Encoding.Unicode.GetBytes(text + "\0"));
    });

    // ── Helpers ───────────────────────────────────────────────────────────

    private static T? WithClipboard<T>(Func<T> action)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try { return action(); }
                catch { return default; }
                finally { CloseClipboard(); }
            }
            Thread.Sleep(RetryDelayMs);
        }
        return default;
    }

    private static byte[]? ReadGlobal(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return null;
        var ptr = GlobalLock(handle);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            var bytes = new byte[(int)GlobalSize(handle)];
            Marshal.Copy(ptr, bytes, 0, bytes.Length);
            return bytes;
        }
        finally { GlobalUnlock(handle); }
    }

    private static bool WriteGlobal(uint format, byte[] data)
    {
        var handle = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
        if (handle == IntPtr.Zero) return false;
        var ptr = GlobalLock(handle);
        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(handle);
        if (SetClipboardData(format, handle) != IntPtr.Zero) return true;   // the clipboard owns it now
        GlobalFree(handle);
        return false;
    }
}
