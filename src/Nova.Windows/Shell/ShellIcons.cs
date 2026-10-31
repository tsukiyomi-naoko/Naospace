using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nova.Windows.Shell;

/// <summary>
/// Small Shell icons for sidebar items. Uses SHGFI_USEFILEATTRIBUTES, which never touches the disk, so a pin on a
/// disconnected drive cannot freeze the UI. Icons are cached per extension / kind.
/// </summary>
public static class ShellIcons
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Folder => Get("folder:", isFolder: true, string.Empty);

    public static ImageSource? ForPath(string path, bool isFolder)
    {
        if (isFolder) return Folder;
        var ext = Path.GetExtension(path);
        return Get("ext:" + ext, false, string.IsNullOrEmpty(ext) ? "file" : "file" + ext);
    }

    private static ImageSource? Get(string key, bool isFolder, string probeName)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;
        ImageSource? img = null;
        var info = new SHFILEINFO();
        var attrs = isFolder ? ShellNative.FILE_ATTRIBUTE_DIRECTORY : ShellNative.FILE_ATTRIBUTE_NORMAL;
        var name = isFolder ? "folder" : probeName;
        var r = ShellNative.SHGetFileInfo(name, attrs, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(),
            ShellNative.SHGFI_ICON | ShellNative.SHGFI_SMALLICON | ShellNative.SHGFI_USEFILEATTRIBUTES);
        if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            try
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                img = bmp;
            }
            finally { Interop.Native.DestroyIcon(info.hIcon); }
        }
        Cache[key] = img;
        return img;
    }
}
