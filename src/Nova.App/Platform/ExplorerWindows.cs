using System.IO;
using System.Runtime.InteropServices;
using Nova.Core.Util;

namespace Nova.App.Platform;

/// <summary>
/// Talks to open File Explorer windows through the Shell.Application automation object:
/// reuse an existing window for a folder instead of opening a duplicate, and read the
/// folder of the active Explorer window for quick capture.
/// </summary>
public static class ExplorerWindows
{
    private sealed record Entry(IntPtr Hwnd, string Path);

    private static IEnumerable<Entry> Enumerate()
    {
        var list = new List<Entry>();
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type is null) return list;
            shell = Activator.CreateInstance(type);
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic w = windows.Item(i);
                    if (w is null) continue;
                    string fullName = w.FullName;
                    if (!string.Equals(Path.GetFileName(fullName), "explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = w.Document.Folder.Self.Path;
                    long hwnd = w.HWND;
                    if (!string.IsNullOrEmpty(path)) list.Add(new Entry(new IntPtr(hwnd), path));
                }
                catch (Exception) { /* windows close while we enumerate; skip */ }
            }
        }
        catch (COMException) { }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        return list;
    }

    /// <summary>Folder shown by the given top-level Explorer window, if any.</summary>
    /// <remarks>With Windows 11 tabs several entries share one HWND; the first match is returned.</remarks>
    public static string? GetFolder(IntPtr hwnd)
    {
        var root = Native.GetAncestor(hwnd, Native.GA_ROOT);
        return Enumerate().FirstOrDefault(e => e.Hwnd == root || e.Hwnd == hwnd)?.Path;
    }

    /// <summary>Focuses an existing Explorer window showing <paramref name="folder"/>; false if none.</summary>
    public static bool TryFocus(string folder)
    {
        var match = Enumerate().FirstOrDefault(e => PathUtil.PathEquals(e.Path, folder));
        if (match is null) return false;
        if (Native.IsIconic(match.Hwnd)) Native.ShowWindow(match.Hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(match.Hwnd);
        return true;
    }
}
