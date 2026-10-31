using System.Runtime.InteropServices;
using Nova.Core.Diagnostics;

namespace Nova.Windows.Shell;

public readonly record struct ExplorerLocation(IntPtr Hwnd, string Path);

/// <summary>
/// Reads the folders shown in open File Explorer windows through the documented Shell automation object
/// (Shell.Application → ShellWindows). Used for "capture the current Explorer location"; nothing is modified.
/// On Windows 11 one Explorer window can hold several tabs; each tab is reported with the same HWND.
/// </summary>
public static class ExplorerWindows
{
    public static IReadOnlyList<ExplorerLocation> GetOpenLocations()
    {
        var result = new List<ExplorerLocation>();
        object? shell = null;
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application");
            if (type == null) return result;
            shell = Activator.CreateInstance(type);
            dynamic windows = ((dynamic)shell!).Windows();
            int count = windows.Count;
            for (var i = 0; i < count; i++)
            {
                dynamic? w = null;
                try
                {
                    w = windows.Item(i);
                    if (w == null) continue;
                    string fullName = w.FullName ?? string.Empty;
                    if (!System.IO.Path.GetFileName(fullName).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    string? path = w.Document?.Folder?.Self?.Path;
                    if (string.IsNullOrEmpty(path)) continue;
                    long hwnd = w.HWND;
                    result.Add(new ExplorerLocation(new IntPtr(hwnd), path));
                }
                catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
                {
                    // Windows that are closing or showing non-folder content are skipped.
                }
                finally
                {
                    if (w != null && Marshal.IsComObject(w)) Marshal.ReleaseComObject(w);
                }
            }
            if (Marshal.IsComObject(windows)) Marshal.ReleaseComObject(windows);
        }
        catch (Exception ex)
        {
            Log.Warn("Enumerating Explorer windows failed", ex);
        }
        finally
        {
            if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
        return result;
    }
}
