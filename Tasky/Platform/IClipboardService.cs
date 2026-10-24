namespace Tasky.Platform;

/// <summary>
/// Clipboard access for images, copied files and text. Never throws: another
/// program can briefly hold the clipboard, so failures simply return nothing.
/// </summary>
public interface IClipboardService
{
    bool ContainsImage();

    /// <summary>The clipboard image encoded as PNG, or null.</summary>
    byte[]? GetImagePng();

    /// <summary>Paths of files copied in Explorer / Finder.</summary>
    IReadOnlyList<string> GetFiles();

    bool SetImagePng(byte[] png);

    bool SetText(string text);
}
