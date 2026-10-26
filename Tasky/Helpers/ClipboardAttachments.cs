using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Tasky.Platform;

namespace Tasky.Helpers;

/// <summary>Turns Ctrl+V / ⌘V into an attachment when the clipboard holds an image or copied files.</summary>
public static class ClipboardAttachments
{
    /// <summary>
    /// Hands a copied image (as PNG bytes) or copied files to the callbacks. Returns false when the
    /// clipboard holds neither, so a normal text paste can go ahead.
    /// </summary>
    public static bool TryTake(Action<byte[]> imagePasted, Action<IReadOnlyList<string>> filesPasted)
    {
        var clipboard = PlatformServices.Clipboard;
        if (clipboard.ContainsImage() && clipboard.GetImagePng() is { } png)
        {
            imagePasted(png);
            return true;
        }
        if (clipboard.GetFiles() is { Count: > 0 } files)
        {
            filesPasted(files);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Makes the paste shortcut anywhere inside <paramref name="area"/> call <paramref name="tryAttach"/>.
    /// While typing in a text box, copied text still pastes as text (Word and browsers put a picture
    /// of copied text on the clipboard too, so text wins over the image there).
    /// </summary>
    public static void HandlePasteIn(Control area, Func<bool> tryAttach)
    {
        area.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!KeyGestures.IsPaste(e)) return;
            if (e.Source is Visual source && source.FindAncestorOfType<TextBox>(includeSelf: true) is { } textBox)
            {
                // Whether the clipboard holds text can only be read asynchronously,
                // so take over the paste and decide once it is known.
                e.Handled = true;
                PasteIntoTextBoxOrAttach(area, textBox, tryAttach);
                return;
            }
            e.Handled = tryAttach();
        }, RoutingStrategies.Tunnel);
    }

    private static async void PasteIntoTextBoxOrAttach(Control area, TextBox textBox, Func<bool> tryAttach)
    {
        if (await ClipboardHasTextAsync(area)) textBox.Paste();
        else tryAttach();
    }

    private static async Task<bool> ClipboardHasTextAsync(Control area)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(area)?.Clipboard;
            return clipboard is not null && !string.IsNullOrEmpty(await clipboard.TryGetTextAsync());
        }
        catch
        {
            return false; // another program is holding the clipboard
        }
    }
}
