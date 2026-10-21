using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Media.Imaging;

namespace Tasky.Helpers;

/// <summary>
/// Clipboard access that never throws. Another program (a clipboard manager,
/// Remote Desktop, Office) can briefly hold the clipboard open, which makes
/// the WPF clipboard API throw; these helpers retry a few times and then give up.
/// </summary>
public static class SafeClipboard
{
    private const int Attempts      = 5;
    private const int RetryDelayMs  = 60;

    public static bool TrySetText(string text)          => Try(() => Clipboard.SetText(text));
    public static bool TrySetImage(BitmapSource image)  => Try(() => Clipboard.SetImage(image));

    public static bool ContainsImage()        => TryGet(Clipboard.ContainsImage);
    public static bool ContainsFileDropList() => TryGet(Clipboard.ContainsFileDropList);

    public static BitmapSource? GetImage()             => TryGet(Clipboard.GetImage);
    public static StringCollection? GetFileDropList()  => TryGet(Clipboard.GetFileDropList);

    private static bool Try(Action action)
        => TryGet(() => { action(); return true; });

    private static T? TryGet<T>(Func<T> read)
    {
        for (int attempt = 1; attempt <= Attempts; attempt++)
        {
            try { return read(); }
            catch (ExternalException) when (attempt < Attempts) { Thread.Sleep(RetryDelayMs); }
            catch (Exception) { break; }
        }
        return default;
    }
}
