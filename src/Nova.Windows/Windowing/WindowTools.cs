using System.Windows;
using System.Windows.Interop;
using Nova.Core.Layout;
using Nova.Windows.Interop;

namespace Nova.Windows.Windowing;

public static class WindowTools
{
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var list = new List<MonitorInfo>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref RECT _, IntPtr _) =>
        {
            var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (Native.GetMonitorInfo(m, ref info))
            {
                var w = info.rcWork;
                list.Add(new MonitorInfo(info.szDevice, new RectD(w.Left, w.Top, w.Width, w.Height), (info.dwFlags & 1) != 0));
            }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    public static string GetMonitorDeviceName(IntPtr hwnd)
    {
        var m = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
        return Native.GetMonitorInfo(m, ref info) ? info.szDevice : string.Empty;
    }

    /// <summary>Window bounds in physical pixels (virtual-screen coordinates).</summary>
    public static RectD GetBounds(IntPtr hwnd)
    {
        Native.GetWindowRect(hwnd, out var r);
        return new RectD(r.Left, r.Top, r.Width, r.Height);
    }

    public static void SetBounds(IntPtr hwnd, RectD r) =>
        Native.SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(r.X), (int)Math.Round(r.Y), (int)Math.Round(r.Width), (int)Math.Round(r.Height),
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

    /// <summary>Places the window behind all other windows without activating it (desktop-facing mode).</summary>
    public static void SendToBottom(IntPtr hwnd) =>
        Native.SetWindowPos(hwnd, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    public static void UseDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        var v = dark ? 1 : 0;
        _ = Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    /// <summary>Brings another application's window to the front (only works while NOVA itself is in the foreground).</summary>
    public static void ActivateExternal(IntPtr hwnd)
    {
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(hwnd);
    }
}
