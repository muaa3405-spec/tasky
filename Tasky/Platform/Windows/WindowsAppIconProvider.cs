using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Media.Imaging;
using static Tasky.Platform.Windows.NativeMethods;

namespace Tasky.Platform.Windows;

/// <summary>
/// App icons from executables, plus the custom icons of browser-installed
/// web apps (PWAs), which Edge and Chrome keep next to their profiles.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsAppIconProvider : IAppIconProvider
{
    /// <summary>Marks a recorded location as a browser web app named after the prefix.</summary>
    public const string PwaPrefix = "pwa://";

    // Keyed by the raw location string from the DB (may be full path or process name)
    private readonly Dictionary<string, Bitmap?> _cache = new();

    public Bitmap? GetIcon(string location)
    {
        if (string.IsNullOrEmpty(location)) return null;
        if (_cache.TryGetValue(location, out var cached)) return cached;

        var icon = location.StartsWith(PwaPrefix, StringComparison.OrdinalIgnoreCase)
            ? GetPwaIcon(location[PwaPrefix.Length..])
            : GetExeIcon(location);
        _cache[location] = icon;
        return icon;
    }

    private static Bitmap? GetExeIcon(string exePath)
    {
        // Resolve to a real file path
        string resolved = exePath;
        if (!File.Exists(resolved))
        {
            // Old DB rows store only the process name — look up the running process
            try
            {
                var procs = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exePath));
                try
                {
                    resolved = procs.Length > 0 ? procs[0].MainModule?.FileName ?? string.Empty : string.Empty;
                }
                finally { foreach (var p in procs) p.Dispose(); }
            }
            catch { resolved = string.Empty; }

            if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return null;
        }

        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(resolved);
            return icon is null ? null : ToBitmap(icon);
        }
        catch { return null; }
    }

    private static Bitmap? GetPwaIcon(string appName)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        // Edge and Chrome store the PWA icon as "<AppName>.ico" inside a _crx_... subdirectory
        string[] webAppDirs =
        [
            Path.Combine(localAppData, @"Microsoft\Edge\User Data\Default\Web Applications"),
            Path.Combine(localAppData, @"Google\Chrome\User Data\Default\Web Applications"),
        ];

        foreach (var dir in webAppDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var ico in Directory.EnumerateFiles(dir, "*.ico", SearchOption.AllDirectories))
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(ico), appName,
                            StringComparison.OrdinalIgnoreCase)) continue;

                    var img = LoadIcoFile(ico);
                    if (img != null) return img;
                }
            }
            catch { }
        }

        // Fallback: read icon from the Start Menu shortcut via shell
        string[] startMenuDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Microsoft\Windows\Start Menu\Programs"),
        ];

        foreach (var dir in startMenuDirs)
        {
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
                {
                    if (!string.Equals(Path.GetFileNameWithoutExtension(lnk), appName,
                            StringComparison.OrdinalIgnoreCase)) continue;
                    return ExtractShellIcon(lnk);
                }
            }
            catch { }
        }

        return null;
    }

    private static Bitmap? LoadIcoFile(string icoPath)
    {
        try
        {
            using var icon = new System.Drawing.Icon(icoPath);
            return ToBitmap(icon);
        }
        catch { return null; }
    }

    private static Bitmap? ExtractShellIcon(string path)
    {
        var shfi = new SHFILEINFO();
        var ret  = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON);
        if (ret == IntPtr.Zero || shfi.hIcon == IntPtr.Zero) return null;
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(shfi.hIcon);
            return ToBitmap(icon);
        }
        catch { return null; }
        finally { DestroyIcon(shfi.hIcon); }
    }

    private static Bitmap ToBitmap(System.Drawing.Icon icon)
    {
        using var bmp = icon.ToBitmap();
        using var ms  = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        return new Bitmap(ms);
    }
}
