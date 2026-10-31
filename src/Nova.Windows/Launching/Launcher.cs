using System.Diagnostics;
using System.IO;
using Nova.Core.Diagnostics;
using Nova.Core.Model;
using Nova.Windows.Interop;
using Nova.Windows.Windowing;

namespace Nova.Windows.Launching;

/// <summary>Starts tools, scripts, files and URLs; optionally re-uses an existing application window.</summary>
public static class Launcher
{
    public static void Launch(PinNode pin)
    {
        switch (pin.Kind)
        {
            case PinKind.Url:
                ShellOpen(pin.Target, null, null);
                break;
            case PinKind.Application:
                if (pin.ReuseExistingWindow && TryActivateExisting(pin.Target)) return;
                ShellOpen(pin.Target, pin.Arguments, WorkingDirectoryFor(pin));
                break;
            case PinKind.Script:
                LaunchScript(pin);
                break;
            case PinKind.File:
                ShellOpen(pin.Target, null, WorkingDirectoryFor(pin));
                break;
            case PinKind.Folder:
                OpenInExplorer(pin.Target);
                break;
        }
    }

    public static void ShellOpen(string target, string? arguments, string? workingDirectory)
    {
        var psi = new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            Arguments = arguments ?? string.Empty,
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory))
            psi.WorkingDirectory = workingDirectory;
        Process.Start(psi)?.Dispose();
    }

    /// <summary>Opens a folder (or selects a file) in a normal Windows File Explorer window.</summary>
    public static void OpenInExplorer(string path)
    {
        var args = File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true })?.Dispose();
    }

    /// <summary>Opens Windows Terminal in <paramref name="folder"/>, falling back to cmd.exe.</summary>
    public static void OpenTerminal(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{folder}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = true, WorkingDirectory = folder })?.Dispose();
        }
    }

    private static void LaunchScript(PinNode pin)
    {
        var ext = Path.GetExtension(pin.Target).ToLowerInvariant();
        var wd = WorkingDirectoryFor(pin);
        if (ext == ".ps1")
        {
            // Respect the system's PowerShell execution policy (no bypass).
            ShellOpen("powershell.exe", $"-NoExit -NoProfile -File \"{pin.Target}\" {pin.Arguments}".TrimEnd(), wd);
        }
        else
        {
            ShellOpen(pin.Target, pin.Arguments, wd);
        }
    }

    private static string? WorkingDirectoryFor(PinNode pin)
    {
        if (!string.IsNullOrWhiteSpace(pin.WorkingDirectory)) return pin.WorkingDirectory;
        try { return Path.GetDirectoryName(pin.Target); }
        catch (ArgumentException) { return null; }
    }

    private static bool TryActivateExisting(string target)
    {
        var name = Path.GetFileNameWithoutExtension(target);
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var p in Process.GetProcessesByName(name))
        {
            using (p)
            {
                try
                {
                    var hwnd = p.MainWindowHandle;
                    if (hwnd != IntPtr.Zero && Native.IsWindowVisible(hwnd))
                    {
                        WindowTools.ActivateExternal(hwnd);
                        return true;
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        Log.Debug("No reusable window found; starting a new instance.");
        return false;
    }
}
