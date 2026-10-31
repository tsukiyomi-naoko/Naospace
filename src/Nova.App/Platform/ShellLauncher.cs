using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Nova.Core.Models;

namespace Nova.App.Platform;

/// <summary>Launching files, apps, scripts, URLs, terminals and shell dialogs.</summary>
public static class ShellLauncher
{
    /// <summary>Opens with the default handler. Returns an error message or null.</summary>
    public static string? Open(string target, string? arguments = null, string? workingDirectory = null, bool elevated = false)
    {
        try
        {
            var psi = new ProcessStartInfo(target)
            {
                UseShellExecute = true,
                Arguments = arguments ?? "",
                WorkingDirectory = workingDirectory ?? SafeDirectoryOf(target) ?? "",
            };
            if (elevated) psi.Verb = "runas";
            Process.Start(psi);
            return null;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; } // UAC cancelled
        catch (Exception ex) { return ex.Message; }
    }

    public static string? Launch(PinKind kind, string target, string? args, string? workingDir, bool elevated)
    {
        switch (kind)
        {
            case PinKind.Script:
                var ext = Path.GetExtension(target).ToLowerInvariant();
                var dir = workingDir ?? SafeDirectoryOf(target);
                return ext switch
                {
                    ".ps1" => Open("powershell.exe", $"-NoExit -ExecutionPolicy Bypass -File \"{target}\" {args}", dir, elevated),
                    ".py" => Open("cmd.exe", $"/k py \"{target}\" {args}", dir, elevated),
                    ".sh" => Open("wsl.exe", $"bash \"{target}\" {args}", dir, elevated),
                    _ => Open(target, args, dir, elevated),
                };
            case PinKind.Command:
                return Open("cmd.exe", "/k " + target + (string.IsNullOrEmpty(args) ? "" : " " + args), workingDir, elevated);
            default:
                return Open(target, args, workingDir, elevated);
        }
    }

    /// <summary>Opens a folder in File Explorer, focusing an existing window when one already shows it.</summary>
    public static string? OpenInExplorer(string folder, bool reuse = true)
    {
        if (reuse && ExplorerWindows.TryFocus(folder)) return null;
        return Open("explorer.exe", $"\"{folder}\"");
    }

    public static string? RevealInExplorer(string path) => Open("explorer.exe", $"/select,\"{path}\"");

    public static string? OpenTerminal(string folder)
    {
        // Windows Terminal if present, otherwise classic console.
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        if (File.Exists(wt) && Open(wt, $"-d \"{folder}\"") is null) return null;
        return Open("cmd.exe", null, folder);
    }

    public static string? OpenWith(string path) => Open("rundll32.exe", $"shell32.dll,OpenAs_RunDLL \"{path}\"");

    public static void ShowProperties(string path, IntPtr owner)
    {
        var info = new Native.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<Native.SHELLEXECUTEINFO>(),
            fMask = Native.SEE_MASK_INVOKEIDLIST,
            hwnd = owner,
            lpVerb = "properties",
            lpFile = path,
            nShow = 5,
        };
        Native.ShellExecuteEx(ref info);
    }

    private static string? SafeDirectoryOf(string target)
    {
        try
        {
            if (Directory.Exists(target)) return target;
            var d = Path.GetDirectoryName(target);
            return !string.IsNullOrEmpty(d) && Directory.Exists(d) ? d : null;
        }
        catch (ArgumentException) { return null; }
    }
}
