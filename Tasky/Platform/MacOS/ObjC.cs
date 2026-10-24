using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tasky.Platform.MacOS;

/// <summary>
/// Minimal Objective-C runtime bridge for the few AppKit calls Tasky needs
/// (NSWorkspace, NSPasteboard, NSSound). Each objc_msgSend overload matches
/// one method signature, which is what both x64 and Apple-silicon Macs require.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class ObjC
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)] private static extern IntPtr objc_getClass(string name);
    [DllImport(LibObjC)] private static extern IntPtr sel_registerName(string name);
    [DllImport(LibObjC)] private static extern IntPtr objc_autoreleasePoolPush();
    [DllImport(LibObjC)] private static extern void   objc_autoreleasePoolPop(IntPtr pool);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, IntPtr a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, IntPtr a, nuint b);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, nuint a);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern IntPtr Msg(IntPtr receiver, IntPtr selector, ref CGRect a, IntPtr b, IntPtr c);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern nuint  MsgNUInt(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern int    MsgInt(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern byte   MsgBool(IntPtr receiver, IntPtr selector);
    [DllImport(LibObjC, EntryPoint = "objc_msgSend")] private static extern byte   MsgBool(IntPtr receiver, IntPtr selector, IntPtr a, IntPtr b);

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect { public double X, Y, Width, Height; }

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Send(IntPtr receiver, string selector)                       => Msg(receiver, sel_registerName(selector));
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a)             => Msg(receiver, sel_registerName(selector), a);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, IntPtr b)   => Msg(receiver, sel_registerName(selector), a, b);
    public static IntPtr Send(IntPtr receiver, string selector, IntPtr a, nuint b)    => Msg(receiver, sel_registerName(selector), a, b);
    public static IntPtr Send(IntPtr receiver, string selector, nuint a)              => Msg(receiver, sel_registerName(selector), a);
    public static IntPtr Send(IntPtr receiver, string selector, ref CGRect a, IntPtr b, IntPtr c)
        => Msg(receiver, sel_registerName(selector), ref a, b, c);
    public static nuint  SendNUInt(IntPtr receiver, string selector) => MsgNUInt(receiver, sel_registerName(selector));
    public static int    SendInt(IntPtr receiver, string selector)   => MsgInt(receiver, sel_registerName(selector));
    public static bool   SendBool(IntPtr receiver, string selector)  => MsgBool(receiver, sel_registerName(selector)) != 0;
    public static bool   SendBool(IntPtr receiver, string selector, IntPtr a, IntPtr b)
        => MsgBool(receiver, sel_registerName(selector), a, b) != 0;

    /// <summary>Runs <paramref name="action"/> inside an autorelease pool so temporary objects are freed.</summary>
    public static T WithPool<T>(Func<T> action)
    {
        var pool = objc_autoreleasePoolPush();
        try { return action(); }
        finally { objc_autoreleasePoolPop(pool); }
    }

    // ── Foundation conversions ────────────────────────────────────────────

    /// <summary>An autoreleased NSString.</summary>
    public static IntPtr NSString(string value)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(value);
        try { return Send(Class("NSString"), "stringWithUTF8String:", utf8); }
        finally { Marshal.FreeCoTaskMem(utf8); }
    }

    public static string? ToManagedString(IntPtr nsString)
        => nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, "UTF8String"));

    /// <summary>An autoreleased NSData holding a copy of <paramref name="bytes"/>.</summary>
    public static IntPtr NSData(byte[] bytes)
    {
        var buffer = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, buffer, bytes.Length);
            return Send(Class("NSData"), "dataWithBytes:length:", buffer, (nuint)bytes.Length);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public static byte[]? ToBytes(IntPtr nsData)
    {
        if (nsData == IntPtr.Zero) return null;
        int length = (int)SendNUInt(nsData, "length");
        var bytes  = new byte[length];
        if (length > 0) Marshal.Copy(Send(nsData, "bytes"), bytes, 0, length);
        return bytes;
    }

    /// <summary>The managed strings in an NSArray of NSString.</summary>
    public static List<string> ToStringList(IntPtr nsArray)
    {
        var list = new List<string>();
        if (nsArray == IntPtr.Zero) return list;
        nuint count = SendNUInt(nsArray, "count");
        for (nuint i = 0; i < count; i++)
            if (ToManagedString(Send(nsArray, "objectAtIndex:", i)) is { } s) list.Add(s);
        return list;
    }

    /// <summary>PNG bytes for any image data AppKit can read (TIFF, PNG, …).</summary>
    public static byte[]? ImageDataToPng(IntPtr imageData)
    {
        if (imageData == IntPtr.Zero) return null;
        var rep = Send(Class("NSBitmapImageRep"), "imageRepWithData:", imageData);
        return BitmapRepToPng(rep);
    }

    public static byte[]? BitmapRepToPng(IntPtr bitmapRep)
    {
        const nuint NSBitmapImageFileTypePNG = 4;
        if (bitmapRep == IntPtr.Zero) return null;
        var png = Send(bitmapRep, "representationUsingType:properties:", NSBitmapImageFileTypePNG,
                       Send(Class("NSDictionary"), "dictionary"));
        return ToBytes(png);
    }

    public static IntPtr Send(IntPtr receiver, string selector, nuint a, IntPtr b)
        => MsgUIntPtr(receiver, sel_registerName(selector), a, b);

    [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgUIntPtr(IntPtr receiver, IntPtr selector, nuint a, IntPtr b);
}
