using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Tasky.Helpers;

/// <summary>Native open / save pickers (Explorer on Windows, Finder on macOS) returning plain paths.</summary>
public static class FileDialogs
{
    public static readonly FilePickerFileType Images = new("Images")
    {
        Patterns                    = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],
        AppleUniformTypeIdentifiers = ["public.image"],
        MimeTypes                   = ["image/*"]
    };

    public static readonly FilePickerFileType AllFiles = new("All files") { Patterns = ["*.*"] };

    public static async Task<IReadOnlyList<string>> PickFilesAsync(
        TopLevel? owner, string title, IReadOnlyList<FilePickerFileType> filters, bool allowMultiple = true)
    {
        if (owner is null) return [];
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = title,
            AllowMultiple  = allowMultiple,
            FileTypeFilter = filters
        });
        return [.. files.Select(f => f.TryGetLocalPath()).OfType<string>()];
    }

    public static async Task<string?> SaveFileAsync(
        TopLevel? owner, string title, string suggestedName, FilePickerFileType fileType, string extension)
    {
        if (owner is null) return null;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title               = title,
            SuggestedFileName   = suggestedName,
            DefaultExtension    = extension.TrimStart('.'),
            FileTypeChoices     = [fileType],
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }
}
