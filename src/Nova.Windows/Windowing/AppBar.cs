using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Nova.Core.Diagnostics;
using Nova.Core.Model;
using Nova.Windows.Interop;

namespace Nova.Windows.Windowing;

/// <summary>
/// Proper desktop docking using the documented Windows AppBar API (SHAppBarMessage). Unlike a borderless window placed
/// at a screen edge, a registered AppBar reserves its strip of the work area, so maximized windows do not cover it.
/// The bar is docked on the monitor the window is currently on and re-positions itself when the taskbar, other
/// AppBars, resolution or DPI change.
/// </summary>
public sealed class AppBar : IDisposable
{
    private readonly Window _window;
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private DockEdge _edge;
    private double _reservedDip;
    private double _windowDip;

    public AppBar(Window window)
    {
        _window = window;
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd)!;
        _callbackMessage = Native.RegisterWindowMessage("NovaWorkspace.AppBarCallback");
        _source.AddHook(Hook);
    }

    public bool IsRegistered { get; private set; }

    /// <summary>
    /// Docks the window. <paramref name="reservedWidthDip"/> is the strip removed from the desktop work area;
    /// <paramref name="windowWidthDip"/> may be larger (expanded browser), in which case the extra part overlaps other
    /// windows like a flyout instead of permanently shrinking everyone else's space.
    /// </summary>
    public void Dock(DockEdge edge, double reservedWidthDip, double windowWidthDip)
    {
        _edge = edge;
        _reservedDip = Math.Max(160, reservedWidthDip);
        _windowDip = Math.Max(_reservedDip, windowWidthDip);
        if (!IsRegistered)
        {
            var abd = NewData();
            abd.uCallbackMessage = _callbackMessage;
            if (Native.SHAppBarMessage(Native.ABM_NEW, ref abd) == 0)
            {
                Log.Warn("Registering the AppBar failed.");
                return;
            }
            IsRegistered = true;
        }
        UpdatePosition();
    }

    public void Undock()
    {
        if (!IsRegistered) return;
        var abd = NewData();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref abd);
        IsRegistered = false;
    }

    public void UpdatePosition()
    {
        if (!IsRegistered) return;
        var monitor = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!Native.GetMonitorInfo(monitor, ref mi)) return;
        var dpi = Native.GetDpiForWindow(_hwnd);
        if (dpi == 0) dpi = 96;
        var widthPx = (int)Math.Round(_reservedDip * dpi / 96.0);
        var windowPx = (int)Math.Round(_windowDip * dpi / 96.0);

        var abd = NewData();
        abd.uEdge = _edge == DockEdge.Left ? Native.ABE_LEFT : Native.ABE_RIGHT;
        abd.rc = mi.rcMonitor;
        if (_edge == DockEdge.Left) abd.rc.Right = abd.rc.Left + widthPx;
        else abd.rc.Left = abd.rc.Right - widthPx;

        // Ask the shell for an acceptable rectangle (it subtracts the taskbar and other AppBars), then fix our width.
        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref abd);
        if (_edge == DockEdge.Left) abd.rc.Right = abd.rc.Left + widthPx;
        else abd.rc.Left = abd.rc.Right - widthPx;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref abd);

        var left = _edge == DockEdge.Left ? abd.rc.Left : abd.rc.Right - windowPx;
        Native.SetWindowPos(_hwnd, IntPtr.Zero, left, abd.rc.Top, windowPx, abd.rc.Height,
            Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private APPBARDATA NewData() => new() { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = _hwnd };

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!IsRegistered) return IntPtr.Zero;
        if (msg == (int)_callbackMessage)
        {
            if (wParam.ToInt32() == Native.ABN_POSCHANGED)
                _window.Dispatcher.BeginInvoke(UpdatePosition);
            handled = true;
        }
        else if (msg == (int)Native.WM_ACTIVATE)
        {
            var abd = NewData();
            Native.SHAppBarMessage(Native.ABM_ACTIVATE, ref abd);
        }
        else if (msg == (int)Native.WM_WINDOWPOSCHANGED)
        {
            var abd = NewData();
            Native.SHAppBarMessage(Native.ABM_WINDOWPOSCHANGED, ref abd);
        }
        else if (msg == (int)Native.WM_DPICHANGED || msg == (int)Native.WM_DISPLAYCHANGE)
        {
            // Let WPF process the DPI change first, then re-apply the reserved strip.
            _window.Dispatcher.BeginInvoke(UpdatePosition, System.Windows.Threading.DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Undock();
        _source.RemoveHook(Hook);
    }
}
