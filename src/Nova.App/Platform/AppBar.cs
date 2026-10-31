using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nova.App.Platform;

/// <summary>
/// Registers a window as a Windows AppBar docked to the left or right screen edge, so the
/// shell reserves the space (maximized windows no longer cover NOVA). All geometry is done
/// in physical pixels against the monitor the window is on, which keeps mixed-DPI setups correct.
/// </summary>
public sealed class AppBar : IDisposable
{
    private readonly Window _window;
    private readonly HwndSource _source;
    private readonly uint _callbackMessage;
    private bool _registered;
    private uint _edge;
    private double _widthDip;

    public AppBar(Window window)
    {
        _window = window;
        _source = (HwndSource)PresentationSource.FromVisual(window);
        _callbackMessage = Native.RegisterWindowMessage("NovaWorkspace.AppBar");
        _source.AddHook(WndProc);
    }

    public bool IsDocked => _registered;

    public void Dock(bool right, double widthDip)
    {
        _edge = right ? Native.ABE_RIGHT : Native.ABE_LEFT;
        _widthDip = widthDip;
        if (!_registered)
        {
            var data = NewData();
            data.uCallbackMessage = _callbackMessage;
            Native.SHAppBarMessage(Native.ABM_NEW, ref data);
            _registered = true;
        }
        Reposition();
    }

    public void Undock()
    {
        if (!_registered) return;
        var data = NewData();
        Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
        _registered = false;
    }

    private Native.APPBARDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
        hWnd = _source.Handle,
    };

    private void Reposition()
    {
        var hwnd = _source.Handle;
        var monitor = Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref mi)) return;

        var dpi = Native.GetDpiForWindow(hwnd);
        var widthPx = (int)Math.Round(_widthDip * (dpi == 0 ? 96 : dpi) / 96.0);

        var data = NewData();
        data.uEdge = _edge;
        data.rc = mi.rcMonitor;
        if (_edge == Native.ABE_LEFT) data.rc.Right = data.rc.Left + widthPx;
        else data.rc.Left = data.rc.Right - widthPx;

        Native.SHAppBarMessage(Native.ABM_QUERYPOS, ref data);
        // The shell may have moved one edge (e.g. another appbar); restore our width.
        if (_edge == Native.ABE_LEFT) data.rc.Right = data.rc.Left + widthPx;
        else data.rc.Left = data.rc.Right - widthPx;
        Native.SHAppBarMessage(Native.ABM_SETPOS, ref data);

        Native.SetWindowPos(hwnd, IntPtr.Zero, data.rc.Left, data.rc.Top,
            data.rc.Right - data.rc.Left, data.rc.Bottom - data.rc.Top, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (_registered && msg == (int)_callbackMessage && wParam.ToInt32() == Native.ABN_POSCHANGED)
        {
            Reposition();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Undock();
        _source.RemoveHook(WndProc);
    }
}
