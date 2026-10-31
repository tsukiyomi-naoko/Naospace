using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nova.App.Platform;

/// <summary>
/// Shell icons and type names. Generic icons are cached per extension (cheap, synchronous);
/// files with their own icon (exe, ico, lnk, url) are resolved per path on a dedicated
/// STA worker so large folders never block the UI thread.
/// </summary>
public static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, ImageSource?> ExtensionIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, string> TypeNames = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource?> PathIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<(string Path, Action<ImageSource?> Done)> Queue = new();
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".ico", ".lnk", ".url", ".cur", ".ani", ".msc", ".appref-ms" };
    private static Thread? _worker;

    public static bool HasOwnIcon(string path) => PerFileExtensions.Contains(Path.GetExtension(path));

    public static ImageSource? GetGeneric(string path, bool isDirectory)
    {
        var key = isDirectory ? "<dir>" : Path.GetExtension(path) is { Length: > 0 } e ? e : "<file>";
        return ExtensionIcons.GetOrAdd(key, _ =>
            Load(isDirectory ? "folder" : "file" + (key.StartsWith('.') ? key : ""),
                 isDirectory ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL,
                 Native.SHGFI_USEFILEATTRIBUTES));
    }

    /// <summary>Resolves a path-specific icon asynchronously; <paramref name="done"/> runs on the worker thread.</summary>
    public static void RequestSpecific(string path, Action<ImageSource?> done)
    {
        if (PathIcons.TryGetValue(path, out var cached)) { done(cached); return; }
        EnsureWorker();
        Queue.Add((path, done));
    }

    public static string GetTypeName(string path, bool isDirectory)
    {
        if (isDirectory) return "File folder";
        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return "File";
        return TypeNames.GetOrAdd(ext, e =>
        {
            var info = new Native.SHFILEINFO();
            Native.SHGetFileInfo("file" + e, Native.FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info),
                Native.SHGFI_TYPENAME | Native.SHGFI_USEFILEATTRIBUTES);
            return string.IsNullOrWhiteSpace(info.szTypeName) ? e.TrimStart('.').ToUpperInvariant() + " File" : info.szTypeName;
        });
    }

    private static void EnsureWorker()
    {
        if (_worker is not null) return;
        lock (Queue)
        {
            if (_worker is not null) return;
            _worker = new Thread(() =>
            {
                foreach (var (path, done) in Queue.GetConsumingEnumerable())
                {
                    var icon = PathIcons.GetOrAdd(path, p => Load(p, 0, 0));
                    try { done(icon); } catch { /* callback errors must not kill the worker */ }
                }
            }) { IsBackground = true, Name = "NOVA icon loader" };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }
    }

    private static ImageSource? Load(string path, uint attributes, uint extraFlags)
    {
        var info = new Native.SHFILEINFO();
        var result = Native.SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(info),
            Native.SHGFI_ICON | Native.SHGFI_SMALLICON | extraFlags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze(); // safe to hand across threads
            return src;
        }
        catch (COMException) { return null; }
        finally { Native.DestroyIcon(info.hIcon); }
    }
}
